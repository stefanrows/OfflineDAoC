using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Database;
using DOL.Events;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.Quests;
using DOL.GS.Quests.Atlantis;
using DOL.GS.ServerProperties;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Trials of Atlantis artifact core (TOA_PLAN WP3.1): artifacts level from kill experience
/// only, bonuses unlock per ArtifactBonus.Level and reach the wearer at once, the level
/// survives a reload through ClassType InventoryArtifact, encounter credit is keyed by
/// artifact ID, and the two core lookups find the artifact ECS effects.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_ArtifactCore
{
    private const string ArtifactId = "1001";
    private const string ItemId = "Alvarus_Leggings_Cloth";
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public class Defaults : DispatchProxy
    {
        /// <summary>Rows SelectObjects&lt;DbInventoryItem&gt; returns (a vault's raw rows).</summary>
        public static List<DbInventoryItem> InventoryRows = [];

        /// <summary>Text of every SendMessage through a packet proxy.</summary>
        public static List<string> Messages = [];

        protected override object Invoke(MethodInfo method, object[] args)
        {
            Type type = method.ReturnType;

            if (method.Name == "SendMessage" && args?.Length > 0 && args[0] is string text)
                Messages.Add(text);

            if (method.Name == "SelectObjects" && type == typeof(IList<DbInventoryItem>))
                return InventoryRows.ToList();

            if (type == typeof(bool))
                return true; // AddObject / SaveObject / DeleteObject succeed

            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]));

            return type != typeof(void) && type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    public class Wardrobe : DispatchProxy
    {
        public List<DbInventoryItem> Equipped = [];

        protected override object Invoke(MethodInfo method, object[] args)
        {
            if (method.Name == "get_EquippedItems")
                return Equipped;

            Type type = method.ReturnType;

            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]));

            return type != typeof(void) && type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    private sealed class Server : GameServer
    {
        public IObjectDatabase Db;
        protected override IObjectDatabase DataBaseImpl => Db;
    }

    private sealed class Wearer : GamePlayer
    {
        private Wearer() : base(null, null) { }
        public IPacketLib Packets;
        private List<(eProperty Property, int Amount)> _bonusChanges;
        public List<(eProperty Property, int Amount)> BonusChanges => _bonusChanges ??= [];
        public override IPacketLib Out => Packets;
        public override string QuestPlayerID => "artifact-test";
        private ConcurrentDictionary<AbstractQuest, byte> _quests;
        public override ConcurrentDictionary<AbstractQuest, byte> QuestList => _quests ??= new();
        protected override void OnItemBonusChanged(eProperty bonusType, int bonusAmount) => BonusChanges.Add((bonusType, bonusAmount));
    }

    private sealed class Target : GameNPC
    {
        public override bool IsAlive => true;
        public override int Health { get => 100; set { } }
        public override int MaxHealth => 100;
        public bool MezImmune;
        public override bool HasAbility(string keyName) => MezImmune && keyName == DOL.GS.Abilities.MezzImmunity;
        public override void OnAttackedByEnemy(AttackData ad) { } // the negation notify needs a world
    }

    private sealed class Vault : GameVault
    {
        public override string GetOwner(GamePlayer player = null) => "vault-owner";
    }

    private GameServer _previousServer;
    private int _previousXpRate;
    private ushort _previousGuildXp;

    [SetUp]
    public void Setup()
    {
        _previousServer = GameServer.Instance;
        var server = (Server) RuntimeHelpers.GetUninitializedObject(typeof(Server));
        server.Db = DispatchProxy.Create<IObjectDatabase, Defaults>();
        GameServer.LoadTestDouble(server);

        _previousXpRate = Properties.ARTIFACT_XP_RATE;
        _previousGuildXp = Properties.GUILD_BUFF_ARTIFACT_XP;
        Properties.ARTIFACT_XP_RATE = 350; // shipped default
        Properties.GUILD_BUFF_ARTIFACT_XP = 5;

        // Shape of our rows: numeric ID, template-name placeholders, XPRate 5.
        ArtifactMgr.Load(
            [
                new DbArtifact { ArtifactID = ArtifactId, EncounterID = "Alvarus_Leggings", QuestID = "Alvarus_Leggings", ScholarID = "1001", Credit = "Alvarus_Leggings", XPRate = 5, ReuseTimer = 1, Zone = "0", BookID = "Alvarus_Leggings_Book" },
                new DbArtifact { ArtifactID = "1002", EncounterID = "Fish_Scales", QuestID = "Fish_Scales", ScholarID = "1002", Credit = "Fish_Scales", XPRate = 5 },
            ],
            [new DbArtifactXItem { ArtifactID = ArtifactId, ItemID = ItemId, Version = "0", Realm = 0 }],
            [
                new DbArtifactBonus { ArtifactID = ArtifactId, BonusID = (int) DbArtifactBonus.ID.Bonus1, Level = 0 },
                new DbArtifactBonus { ArtifactID = ArtifactId, BonusID = (int) DbArtifactBonus.ID.Bonus2, Level = 2 },
                new DbArtifactBonus { ArtifactID = ArtifactId, BonusID = (int) DbArtifactBonus.ID.Bonus3, Level = 5 },
                new DbArtifactBonus { ArtifactID = ArtifactId, BonusID = (int) DbArtifactBonus.ID.Spell, Level = 3 },
                new DbArtifactBonus { ArtifactID = ArtifactId, BonusID = (int) DbArtifactBonus.ID.ProcSpell, Level = 10 },
            ]);
    }

    [TearDown]
    public void Teardown()
    {
        Defaults.InventoryRows = [];
        Defaults.Messages = [];
        ArtifactMgr.Load([], [], []);
        Properties.ARTIFACT_XP_RATE = _previousXpRate;
        Properties.GUILD_BUFF_ARTIFACT_XP = _previousGuildXp;
        GameServer.LoadTestDouble(_previousServer);
    }

    private static DbItemTemplate Template(string classType = "DOL.GS.InventoryArtifact")
    {
        return new DbItemTemplate
        {
            Id_nb = ItemId,
            Name = "Alvarus's Leggings",
            Object_Type = (int) eObjectType.Cloth,
            Item_Type = (int) eInventorySlot.LegsArmor,
            ClassType = classType,
            Bonus1 = 5, Bonus1Type = (int) eProperty.Strength,
            Bonus2 = 6, Bonus2Type = (int) eProperty.Constitution,
            Bonus3 = 4, Bonus3Type = (int) eProperty.Resist_Heat,
            SpellID = 29996,
            ProcSpellID = 30001,
        };
    }

    private static Wearer NewWearer(params DbInventoryItem[] equipped)
    {
        var wearer = (Wearer) RuntimeHelpers.GetUninitializedObject(typeof(Wearer));
        wearer.Packets = DispatchProxy.Create<IPacketLib, Defaults>();
        typeof(GamePlayer).GetField("_questListFinished", PrivateInstance).SetValue(wearer, new List<AbstractQuest>());
        typeof(GamePlayer).GetField("_questListFinishedLock", PrivateInstance).SetValue(wearer, new Lock());
        typeof(GamePlayer).GetField("<AvailableQuestIndexes>k__BackingField", PrivateInstance).SetValue(wearer, new ConcurrentQueue<byte>());

        IGameInventory inventory = DispatchProxy.Create<IGameInventory, Wardrobe>();
        ((Wardrobe) (object) inventory).Equipped.AddRange(equipped);
        wearer.Inventory = inventory;
        return wearer;
    }

    private static GainedExperienceEventArgs Kill(eXPSource source, long baseXp, long camp = 0, long group = 0, long outpost = 0, long guild = 0)
    {
        return new GainedExperienceEventArgs(baseXp, camp, group, guild, 0, outpost, true, true, source);
    }

    [Test]
    public void LevelFollowsTheFiftyMillionStepTable()
    {
        Assert.That(ArtifactMgr.GetLevelForExperience(0), Is.EqualTo(0));
        Assert.That(ArtifactMgr.GetLevelForExperience(49_999_999), Is.EqualTo(0));
        Assert.That(ArtifactMgr.GetLevelForExperience(50_000_000), Is.EqualTo(1));
        Assert.That(ArtifactMgr.GetLevelForExperience(260_000_000), Is.EqualTo(5));
        Assert.That(ArtifactMgr.GetLevelForExperience(499_999_999), Is.EqualTo(9));
        Assert.That(ArtifactMgr.GetLevelForExperience(500_000_000), Is.EqualTo(10));
        Assert.That(ArtifactMgr.GetLevelForExperience(long.MaxValue), Is.EqualTo(10));
        Assert.That(ArtifactMgr.GetExperienceForLevel(10), Is.EqualTo(500_000_000));

        var artifact = new InventoryArtifact(Template()) { Experience = 75_000_000 };
        Assert.That(artifact.ArtifactLevel, Is.EqualTo(1));
        Assert.That(ArtifactMgr.GetXPGainedForLevel(artifact), Is.EqualTo(50), "halfway from level 1 to 2");
    }

    [Test]
    public void OnlyKillExperienceLevelsEquippedArtifacts()
    {
        var artifact = new InventoryArtifact(Template());
        Wearer player = NewWearer(artifact);

        foreach (eXPSource source in Enum.GetValues<eXPSource>())
        {
            long before = artifact.Experience;
            ArtifactMgr.PlayerGainedExperience(GamePlayerEvent.GainedExperience, player, Kill(source, 7_000_000));
            bool counts = source is eXPSource.NPC or eXPSource.Player;
            Assert.That(artifact.Experience - before, Is.EqualTo(counts ? 100_000 : 0), $"{source}: 7 M x 5 / 350");
        }

        // DOL's sum: base + camp + group + outpost; guild and baf bonuses do not count.
        long start = artifact.Experience;
        ArtifactMgr.PlayerGainedExperience(GamePlayerEvent.GainedExperience, player, Kill(eXPSource.NPC, 3_500_000, camp: 1_400_000, group: 1_400_000, outpost: 700_000, guild: 7_000_000));
        Assert.That(artifact.Experience - start, Is.EqualTo(100_000));
    }

    [Test]
    public void ArtifactsInTheBackpackAndPlainItemsGainNothing()
    {
        var plain = new GameInventoryItem(Template(classType: null));
        Wearer player = NewWearer(plain);
        var backpack = new InventoryArtifact(Template());

        ArtifactMgr.PlayerGainedExperience(GamePlayerEvent.GainedExperience, player, Kill(eXPSource.NPC, 7_000_000));

        Assert.That(plain.Experience, Is.Zero);
        Assert.That(backpack.Experience, Is.Zero);
    }

    [Test]
    public void ZeroOrNegativeRatePropertyStopsArtifactExperience()
    {
        var artifact = new InventoryArtifact(Template());
        Wearer player = NewWearer(artifact);
        Properties.ARTIFACT_XP_RATE = 0;

        Assert.DoesNotThrow(() => ArtifactMgr.PlayerGainedExperience(GamePlayerEvent.GainedExperience, player, Kill(eXPSource.NPC, 7_000_000)));
        Assert.That(artifact.Experience, Is.Zero);
    }

    [Test]
    public void ExperienceStopsAtLevelTen()
    {
        var artifact = new InventoryArtifact(Template()) { Experience = 499_990_000 };
        Wearer player = NewWearer(artifact);

        ArtifactMgr.PlayerGainedExperience(GamePlayerEvent.GainedExperience, player, Kill(eXPSource.Player, 70_000_000));

        Assert.That(artifact.Experience, Is.EqualTo(500_000_000));
        Assert.That(artifact.ArtifactLevel, Is.EqualTo(10));
    }

    [Test]
    public void LockedBonusSlotsReadZeroUntilTheirLevel()
    {
        var artifact = new InventoryArtifact(Template());

        Assert.That(artifact.ArtifactID, Is.EqualTo(ArtifactId));
        Assert.That((artifact.Bonus1, artifact.Bonus1Type), Is.EqualTo((5, (int) eProperty.Strength)), "level 0 bonus");
        Assert.That((artifact.Bonus2, artifact.Bonus2Type), Is.EqualTo((0, 0)));
        Assert.That((artifact.Bonus3, artifact.Bonus3Type), Is.EqualTo((0, 0)));
        Assert.That(artifact.SpellID, Is.Zero);
        Assert.That(artifact.ProcSpellID, Is.Zero);
        Assert.That(artifact.GainsNewAbilityAtLevel(), Is.EqualTo(2));

        artifact.Experience = ArtifactMgr.GetExperienceForLevel(3);
        Assert.That(artifact.Bonus2, Is.EqualTo(6));
        Assert.That(artifact.SpellID, Is.EqualTo(29996), "use spell unlocks at level 3");
        Assert.That(artifact.Bonus3, Is.Zero);
        Assert.That(artifact.GainsNewAbilityAtLevel(), Is.EqualTo(5));

        artifact.Experience = ArtifactMgr.GetExperienceForLevel(10);
        Assert.That(artifact.ProcSpellID, Is.EqualTo(30001));

        // The shared template is never written: other copies keep their own state.
        Assert.That(artifact.Template.Bonus2, Is.EqualTo(6));
        Assert.That(new InventoryArtifact(artifact.Template).Bonus2, Is.Zero);
    }

    [Test]
    public void LevelUpWhileWornAppliesExactlyTheNewlyUnlockedStats()
    {
        var artifact = new InventoryArtifact(Template());
        Wearer player = NewWearer(artifact);
        artifact.OnEquipped(player); // GamePlayer.OnItemEquipped then adds Bonus1 (Str +5) through the gated getters

        ArtifactMgr.PlayerGainedExperience(GamePlayerEvent.GainedExperience, player, Kill(eXPSource.NPC, 7_000_000_000)); // 100 M: level 2

        Assert.That(artifact.ArtifactLevel, Is.EqualTo(2));
        Assert.That(player.BonusChanges, Is.EqualTo(new[] { (eProperty.Constitution, 6) }));

        // Unequip removes what the getters now report (Str 5 + Con 6), so nothing drifts.
        artifact.OnUnEquipped(player);
        artifact.Experience = ArtifactMgr.GetExperienceForLevel(6);
        Assert.That(player.BonusChanges, Has.Count.EqualTo(1), "a level change in the backpack touches no stats");

        // Equipped again at level 6, then a GM lowers the experience: the bonus is taken back.
        artifact.OnEquipped(player);
        artifact.Experience = ArtifactMgr.GetExperienceForLevel(1);
        Assert.That(player.BonusChanges.Skip(1), Is.EquivalentTo(new[] { (eProperty.Constitution, -6), (eProperty.Resist_Heat, -4) }));
    }

    [Test]
    public void ReloadThroughClassTypeKeepsTheLevel()
    {
        // The saved inventory row: same template, experience persisted in Inventory.Experience.
        var saved = new GameInventoryItem(Template()) { Experience = 260_000_000, OwnerID = "owner" };

        GameInventoryItem reloaded = GameInventoryItem.Create(saved);

        Assert.That(reloaded, Is.InstanceOf<InventoryArtifact>());
        var artifact = (InventoryArtifact) reloaded;
        Assert.That(artifact.ArtifactLevel, Is.EqualTo(5));
        Assert.That(artifact.Bonus3, Is.EqualTo(4), "level 5 bonus active after reload");
        Assert.That(artifact.ProcSpellID, Is.Zero, "level 10 proc still locked");

        // The pitfall the ClassType SQL fixes: without it every bonus is active at level 0.
        GameInventoryItem plain = GameInventoryItem.Create(new GameInventoryItem(Template(classType: null)));
        Assert.That(plain, Is.Not.InstanceOf<InventoryArtifact>());
        Assert.That(plain.ProcSpellID, Is.EqualTo(30001));
    }

    [Test]
    public void ItemOutsideTheArtifactTablesBehavesLikeAPlainItem()
    {
        DbItemTemplate template = Template();
        template.Id_nb = "not_an_artifact";
        var item = new InventoryArtifact(template);

        Assert.That(item.ArtifactID, Is.Null);
        Assert.That(item.Bonus2, Is.EqualTo(6));
        Assert.That(item.ProcSpellID, Is.EqualTo(30001));
    }

    [Test]
    public void PlaceholderRowsNeverResolveToQuestTypes()
    {
        Wearer player = NewWearer();

        Assert.That(ArtifactMgr.GetQuestType("Alvarus_Leggings"), Is.Null);
        Assert.That(ArtifactMgr.GetQuestType(""), Is.Null);
        Assert.That(ArtifactMgr.GetQuestType("DOL.GS.GamePlayer"), Is.Null, "a non-quest type is not a quest");
        Assert.That(ArtifactMgr.GetEncounterType(ArtifactId), Is.Null);
        Assert.That(ArtifactMgr.GetScholars(ArtifactId), Is.EqualTo(new[] { "1001" }));
        Assert.That(ArtifactMgr.GetArtifactIDFromCredit("alvarus_leggings_credit"), Is.EqualTo(ArtifactId));
        Assert.That(ArtifactMgr.GetArtifactIDFromCredit("Alvarus_Leggings"), Is.EqualTo(ArtifactId));
        Assert.That(ArtifactMgr.GetArtifact("unknown"), Is.Null);

        // No item template in the test database: no version fits, so no credit; and no crash.
        Assert.That(ArtifactMgr.GrantArtifactCredit(player, ArtifactId), Is.False);
        Assert.That(ArtifactMgr.GrantArtifactCredit(player, "unknown"), Is.False);
    }

    [Test]
    public void EncounterCreditIsKeyedByArtifactAndConsumedOnce()
    {
        Wearer player = NewWearer();

        ArtifactEncounter alvarus = ArtifactEncounter.GrantCredit(player, ArtifactId);
        ArtifactEncounter fish = ArtifactEncounter.GrantCredit(player, "1002");

        Assert.That(alvarus.Step, Is.EqualTo(-1), "DOL's finished marker, what RemoveEncounterCredit consumes");
        Assert.That(player.QuestList, Is.Empty, "credit never enters the journal");
        Assert.That(player.FinishedQuests(), Is.EquivalentTo(new AbstractQuest[] { alvarus, fish }),
            "a second artifact's credit is not refused as 'same quest type already finished'");
        Assert.That(ArtifactMgr.HasEncounterCredit(player, ArtifactId), Is.True);
        Assert.That(ArtifactMgr.HasEncounterCredit(player, "1003"), Is.False);

        Assert.That(ArtifactMgr.ConsumeEncounterCredit(player, ArtifactId), Is.True);
        Assert.That(ArtifactMgr.HasEncounterCredit(player, ArtifactId), Is.False);
        Assert.That(ArtifactMgr.HasEncounterCredit(player, "1002"), Is.True);
        Assert.That(ArtifactMgr.ConsumeEncounterCredit(player, ArtifactId), Is.False);
    }

    [Test]
    public void RemoveEncounterCreditAcceptsBothFinishedSteps()
    {
        Wearer player = NewWearer();
        var credit = new ArtifactEncounter(player, -2); // a quest finished by the stock FinishQuest
        player.AddFinishedQuest(credit);

        Assert.That(player.RemoveEncounterCredit(typeof(ArtifactEncounter)), Is.True);
        Assert.That(player.GetFinishedQuests(), Is.Empty);
    }

    [Test]
    public void ActiveQuestsReplacesTheCollidingQuestListExtension()
    {
        Wearer player = NewWearer();
        var quest = new ArtifactEncounter(player, 1);
        player.QuestList.TryAdd(quest, 0);

        List<AbstractQuest> active = player.ActiveQuests();
        Assert.That(active, Is.EqualTo(new AbstractQuest[] { quest }));

        active.Clear();
        Assert.That(player.QuestList, Has.Count.EqualTo(1), "a snapshot, not the live dictionary");
        Assert.That(typeof(ArtifactPlayerExtensions).GetMethod("QuestList"), Is.Null);
    }

    [Test]
    public void CeremonialBracerInterceptsAMezAndIsUsedUp()
    {
        var target = (Target) RuntimeHelpers.GetUninitializedObject(typeof(Target));
        target.ObjectState = GameObject.eObjectState.Active;
        typeof(GameNPC).GetField("m_brains", PrivateInstance).SetValue(target, new ArrayList());
        target.effectListComponent = EffectListComponent.Create(target);
        var line = new SpellLine("test", "test", "test", true);

        try
        {
            var bracer = new CeremonialBracerMezzSpellHandler(target, MakeSpell(eSpellType.CeremonialBracerMezz, 0, 991601), line);
            ECSGameSpellEffect charge = new(new(target, 60_000, 1, bracer));
            charge.FinalizeState(EffectListComponent.AddEffectResult.Added);
            var effects = (Dictionary<eEffect, List<ECSGameEffect>>) typeof(EffectListComponent)
                .GetField("_effects", PrivateInstance).GetValue(target.effectListComponent);
            effects[eEffect.Unknown] = [charge];

            Assert.That(ArtifactSpells.FindEffect(target, eSpellType.CeremonialBracerMezz), Is.SameAs(charge),
                "the lookup CCSpellHandler now uses");

            var mez = new MesmerizeSpellHandler(target, MakeSpell(eSpellType.Mesmerize, 0, 991602), line);
            MethodInfo checkResist = typeof(MesmerizeSpellHandler).GetMethod("CheckSpellResist", PrivateInstance);
            bool negated = (bool) checkResist.Invoke(mez, [target]);

            Assert.That(negated, Is.True, "the mez is negated");
            Assert.That(charge.IsActive && !charge.IsEnding, Is.False, "the bracer charge is used up");
            Assert.That(ArtifactSpells.FindEffect(target, eSpellType.CeremonialBracerMezz), Is.Null, "the intercept cannot fire twice");
        }
        finally
        {
            ServiceObjectStore.Remove(target.effectListComponent);
        }
    }

    [Test]
    public void CoreLookupsUseTheArtifactEcsEffects()
    {
        // CalcFallDamage needs a live client and region; guard the lookup in the source instead.
        string player = File.ReadAllText(FindRepoFile(Path.Combine("source", "server", "GameServer", "gameobjects", "GamePlayer.cs")));
        string cc = File.ReadAllText(FindRepoFile(Path.Combine("source", "server", "GameServer", "spells", "CCSpellHandler.cs")));

        Assert.That(player, Does.Not.Contain("FindEffectOnTarget(this, \"CloudsongFall\")"));
        Assert.That(player, Does.Contain("ArtifactSpells.FindEffect(this, eSpellType.CloudsongFall)"));
        Assert.That(cc, Does.Not.Contain("FindEffectOnTarget(target, \"CeremonialBracerMezz\")"));
        Assert.That(cc, Does.Contain("ArtifactSpells.FindEffect(target, eSpellType.CeremonialBracerMezz)"));
    }

    /// <summary>A raw Inventory row as the database layer returns it for a vault.</summary>
    private static DbInventoryItem RawRow(long experience)
    {
        DbInventoryItem row = (DbInventoryItem) RuntimeHelpers.GetUninitializedObject(typeof(DbInventoryItem));
        typeof(DbInventoryItem).GetField("m_item", PrivateInstance).SetValue(row, Template());
        row.ITemplate_Id = ItemId;
        row.Experience = experience;
        row.OwnerID = "vault-owner";
        row.SlotPosition = (int) eInventorySlot.HouseVault_First;
        row.ObjectId = "vault-row-1";
        row.IsPersisted = true;
        return row;
    }

    [Test]
    public void ItemsWithdrawnFromAVaultKeepTheirArtifactLevel()
    {
        DbInventoryItem raw = RawRow(260_000_000);
        DbInventoryItem plainRow = RawRow(0);
        plainRow.Template.ClassType = null;
        plainRow.ObjectId = "vault-row-2";
        Defaults.InventoryRows = [raw, plainRow];
        var vault = (Vault) RuntimeHelpers.GetUninitializedObject(typeof(Vault));

        List<DbInventoryItem> items = vault.GetDbItems(NewWearer()).ToList();

        var artifact = items[0] as InventoryArtifact;
        Assert.That(artifact, Is.Not.Null, "the vault hands out the artifact class, not the raw row");
        Assert.That(artifact.ArtifactLevel, Is.EqualTo(5));
        Assert.That(artifact.Bonus3, Is.EqualTo(4));
        Assert.That(artifact.ProcSpellID, Is.Zero, "level 10 proc stays locked after the withdrawal");
        Assert.That((artifact.ObjectId, artifact.OwnerID, artifact.IsPersisted), Is.EqualTo(("vault-row-1", "vault-owner", true)),
            "same row: the move saves it, never adds a second one");
        Assert.That(items[1], Is.SameAs(plainRow), "rows without a ClassType are left alone");
    }

    [Test]
    public void CompanionInventoriesWrapArtifactsLikeALogin()
    {
        DbInventoryItem raw = RawRow(100_000_000);
        raw.SlotPosition = (int) eInventorySlot.FirstBackpack;
        var inventory = new BotInventory("companion");

        inventory.LoadInventory("companion", new List<DbInventoryItem> { raw });

        var artifact = inventory.GetItem(eInventorySlot.FirstBackpack) as InventoryArtifact;
        Assert.That(artifact, Is.Not.Null);
        Assert.That(artifact.ArtifactLevel, Is.EqualTo(2));
        Assert.That(GameInventoryItem.WrapLoaded(artifact), Is.SameAs(artifact), "already a game item");
    }

    [Test]
    public void DyeAndReskinNpcsRefuseArtifacts()
    {
        Wearer player = NewWearer(); // no TempProperties: accepting the item would throw
        var artifact = new InventoryArtifact(Template());
        var dyer = (EffectNPC) RuntimeHelpers.GetUninitializedObject(typeof(EffectNPC));
        var reskinner = (ItemModel) RuntimeHelpers.GetUninitializedObject(typeof(ItemModel));

        Assert.That(dyer.ReceiveItem(player, artifact), Is.False);
        Assert.That(reskinner.ReceiveItem(player, artifact), Is.False);
        Assert.That(Defaults.Messages, Is.EqualTo(new[] { ArtifactMgr.REWORK_REFUSAL, ArtifactMgr.REWORK_REFUSAL }));

        // A plain copy of an artifact template (ClassType SQL not yet applied) is refused too.
        Assert.That(ArtifactMgr.IsArtifact(new GameInventoryItem(Template(classType: null))), Is.True);
    }

    [Test]
    public void BotsNeverRollArtifactTemplates()
    {
        // Two equally fit database weapons, so no generated fallback runs: GeneratedUniqueItem
        // captures server properties in static fields on first use, which would leak into
        // UT_AutonomousLootFlow.
        DbItemTemplate Weapon(string id, string name) => new()
        {
            Id_nb = id, Name = name, Level = 1,
            Realm = (int) eRealm.Hibernia, Object_Type = (int) eObjectType.LargeWeapons,
            Item_Type = Slot.TWOHAND, Hand = 1, Type_Damage = (int) eDamageType.Crush,
            DPS_AF = 15, SPD_ABS = 45, Quality = 89, Model = 69,
            Condition = 50000, MaxCondition = 50000, Durability = 50000, MaxDurability = 50000,
            IsPickable = true, IsTradable = true, IsDropable = true, AllowedClasses = "0",
        };

        DbItemTemplate artifactWeapon = Weapon("artifact_two_hander", "database artifact");
        DbItemTemplate plainWeapon = Weapon("plain_two_hander", "database two-hander");
        HashSet<string> chosen = [];

        ArtifactMgr.Load([new DbArtifact { ArtifactID = "2001", XPRate = 5 }],
            [new DbArtifactXItem { ArtifactID = "2001", ItemID = "artifact_two_hander", Version = "0" }], []);

        for (int roll = 0; roll < 100; roll++)
        {
            chosen.Add(BotEquipment.SelectCompanionWeapon([artifactWeapon, plainWeapon], 1, eRealm.Hibernia,
                eCharacterClass.Champion, eObjectType.LargeWeapons, eInventorySlot.TwoHandWeapon).Name);
        }

        Assert.That(chosen, Is.EquivalentTo(new[] { "database two-hander" }), "the artifact version is never rolled");
        Assert.That(ArtifactMgr.IsArtifactTemplate(artifactWeapon), Is.True);
        Assert.That(ArtifactMgr.IsArtifactTemplate(plainWeapon), Is.False);
    }

    [Test]
    public void ImmuneTargetKeepsItsCeremonialBracerCharge()
    {
        var target = (Target) RuntimeHelpers.GetUninitializedObject(typeof(Target));
        target.ObjectState = GameObject.eObjectState.Active;
        target.MezImmune = true;
        typeof(GameNPC).GetField("m_brains", PrivateInstance).SetValue(target, new ArrayList());
        target.effectListComponent = EffectListComponent.Create(target);
        var line = new SpellLine("test", "test", "test", true);

        try
        {
            var bracer = new CeremonialBracerMezzSpellHandler(target, MakeSpell(eSpellType.CeremonialBracerMezz, 0, 991603), line);
            ECSGameSpellEffect charge = new(new(target, 60_000, 1, bracer));
            charge.FinalizeState(EffectListComponent.AddEffectResult.Added);
            var effects = (Dictionary<eEffect, List<ECSGameEffect>>) typeof(EffectListComponent)
                .GetField("_effects", PrivateInstance).GetValue(target.effectListComponent);
            effects[eEffect.Unknown] = [charge];

            var mez = new MesmerizeSpellHandler(target, MakeSpell(eSpellType.Mesmerize, 0, 991604), line);
            bool negated = (bool) typeof(MesmerizeSpellHandler).GetMethod("CheckSpellResist", PrivateInstance).Invoke(mez, [target]);

            Assert.That(negated, Is.True, "immunity negates the mez");
            Assert.That(charge.IsActive && !charge.IsEnding, Is.True, "the bracer is not used up by a mez that could not land");
        }
        finally
        {
            ServiceObjectStore.Remove(target.effectListComponent);
        }
    }

    private static Spell MakeSpell(eSpellType type, int value, int id)
    {
        return new Spell(new DbSpell { SpellID = id, Type = type.ToString(), Target = "Enemy", Duration = 60, Value = value }, 50);
    }

    private static string FindRepoFile(string relativePath)
    {
        for (DirectoryInfo directory = new(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);

            if (File.Exists(candidate))
                return candidate;
        }

        Assert.Fail($"Repository file not found from the test directory: {relativePath}");
        return null;
    }
}
