using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Encounter credit from kills (TOA_PLAN WP3.3): the death of a creature named in
/// ArtifactEncounterTarget grants the artifact's encounter credit to every player who earned the
/// kill (group mode) or also to their battlegroup (battlegroup mode); never to bots or companions,
/// never twice, never after the artifact was awakened; an unmapped creature grants nothing. Rows
/// have the shape of pack 070.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_ArtifactEncounterCredit
{
    private const string Leggings = "1001";
    private const string Harp = "1042";
    private const string LeggingsName = "Alvarus's Leggings";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly Dictionary<string, DbItemTemplate> Templates = new(StringComparer.OrdinalIgnoreCase);

    public class Database : DispatchProxy
    {
        protected override object Invoke(MethodInfo method, object[] args)
        {
            Type type = method.ReturnType;

            if (method.Name == "FindObjectByKey" && type == typeof(DbItemTemplate))
                return args[0] is string key && Templates.TryGetValue(key, out DbItemTemplate template) ? template : null;

            if (type == typeof(bool))
                return true;

            if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0]));

            return type != typeof(void) && type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    public class Packets : DispatchProxy
    {
        public readonly List<string> Messages = [];

        protected override object Invoke(MethodInfo method, object[] args)
        {
            if (method.Name == nameof(IPacketLib.SendMessage) && args?.Length > 0 && args[0] is string text)
                Messages.Add(text);

            return method.ReturnType != typeof(void) && method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }

    public class Armsman : DispatchProxy
    {
        protected override object Invoke(MethodInfo method, object[] args)
        {
            if (method.Name == "get_ID")
                return (int) eCharacterClass.Armsman;

            if (method.Name == "get_Name")
                return "Armsman";

            return method.ReturnType != typeof(void) && method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }

    private sealed class Player : GamePlayer
    {
        private Player() : base(null, null) { }
        public IPacketLib PacketLib;
        public ICharacterClass Class;
        public string TestName;
        public bool Alive;
        public int TestX;
        public ushort Region;
        public override string Name { get => TestName; set => TestName = value; }
        public override byte Level { get => 50; set { } }
        public override bool IsAlive => Alive;
        public override int X => TestX;
        public override int Y => 0;
        public override int Z => 0;
        public override ushort CurrentRegionID { get => Region; set { } }
        public override IPacketLib Out => PacketLib;
        public override ICharacterClass CharacterClass => Class;
        public override string QuestPlayerID => "encounter-" + TestName;
        public override void EnemyKilled(GameLiving enemy) { }
        private ConcurrentDictionary<AbstractQuest, byte> _quests;
        public override ConcurrentDictionary<AbstractQuest, byte> QuestList => _quests ??= new();
        public List<string> Messages => ((Packets) (object) PacketLib).Messages;
    }

    private sealed class Mob : GameNPC
    {
        public int TestX;
        public ushort Region;
        public override int X => TestX;
        public override int Y => 0;
        public override int Z => 0;
        public override ushort CurrentRegionID { get => Region; set { } }
        public override byte Level { get => 65; set { } }
    }

    private sealed class Bot : GameBot
    {
        private Bot() : base((OfflineWorldBotRecord) null) { }
        public string TestName;
        public override string Name { get => TestName; set => TestName = value; }
        public override byte Level { get => 50; set { } }
    }

    private sealed class Server : GameServer
    {
        public IObjectDatabase Db;
        protected override IObjectDatabase DataBaseImpl => Db;
    }

    private GameServer _previousServer;

    [SetUp]
    public void Setup()
    {
        _previousServer = GameServer.Instance;
        var server = (Server) RuntimeHelpers.GetUninitializedObject(typeof(Server));
        server.Db = DispatchProxy.Create<IObjectDatabase, Database>();
        GameServer.LoadTestDouble(server);

        Templates.Clear();
        Templates["Alvarus_Leggings_plate"] = new DbItemTemplate
        {
            Id_nb = "Alvarus_Leggings_plate", Name = LeggingsName, Object_Type = (int) eObjectType.Plate,
            Item_Type = (int) eInventorySlot.LegsArmor, AllowedClasses = "2", ClassType = "DOL.GS.InventoryArtifact",
        };
        Templates["Phoebus_Harp"] = new DbItemTemplate
        {
            Id_nb = "Phoebus_Harp", Name = "Phoebus' Harp", Object_Type = (int) eObjectType.Magical,
            Item_Type = (int) eInventorySlot.Neck, AllowedClasses = "2", ClassType = "DOL.GS.InventoryArtifact",
        };

        ArtifactMgr.Load(
            [
                new DbArtifact { ArtifactID = Leggings, EncounterID = "Alvarus_Leggings", QuestID = "Alvarus_Leggings", Credit = "Alvarus_Leggings", XPRate = 5, Zone = "0", BookID = "Alvarus' Letter, complete" },
                new DbArtifact { ArtifactID = Harp, EncounterID = "Phoebus_Harp", QuestID = "Phoebus_Harp", Credit = "Phoebus_Harp", XPRate = 5, Zone = "0" },
            ],
            [
                new DbArtifactXItem { ArtifactID = Leggings, ItemID = "Alvarus_Leggings_plate", Version = "6", Realm = 0 },
                new DbArtifactXItem { ArtifactID = Harp, ItemID = "Phoebus_Harp", Version = "0", Realm = 0 },
            ],
            []);

        // Pack 070's rows for these two artifacts.
        ArtifactEncounterCredit.Load(
        [
            Target("toa-enc-1001-minkhat", Leggings, "Minkhat", "battlegroup", zone: "Mesothalassa"),
            Target("toa-enc-1042-hallea", Harp, "Hallea", "battlegroup", zone: "Mesothalassa"),
            Target("toa-enc-1042-kosma", Harp, "Kosma", "battlegroup", zone: "Mesothalassa"),
            Target("toa-enc-1042-zoea", Harp, "Zoea", "battlegroup", zone: "Mesothalassa"),
        ]);
    }

    [TearDown]
    public void Teardown()
    {
        ArtifactEncounterCredit.Load([]);
        ArtifactMgr.Load([], [], []);
        Templates.Clear();
        GameServer.LoadTestDouble(_previousServer);
    }

    private static DbArtifactEncounterTarget Target(string id, string artifactID, string mob, string mode, int region = 0, string zone = "")
    {
        var row = new DbArtifactEncounterTarget { ArtifactID = artifactID, MobName = mob, Region = region, CreditMode = mode, Zone = zone };
        row.ObjectId = id;
        return row;
    }

    private static Player NewPlayer(string name, int x = 0, bool alive = true, ushort region = 30)
    {
        var player = (Player) RuntimeHelpers.GetUninitializedObject(typeof(Player));
        player.ObjectState = GameObject.eObjectState.Active;
        player.TestName = name;
        player.Alive = alive;
        player.TestX = x;
        player.Region = region;
        player.Realm = eRealm.Albion;
        player.PacketLib = DispatchProxy.Create<IPacketLib, Packets>();
        player.Class = DispatchProxy.Create<ICharacterClass, Armsman>();
        typeof(GamePlayer).GetField("_questListFinished", PrivateInstance).SetValue(player, new List<AbstractQuest>());
        typeof(GamePlayer).GetField("_questListFinishedLock", PrivateInstance).SetValue(player, new Lock());
        typeof(GamePlayer).GetField("<AvailableQuestIndexes>k__BackingField", PrivateInstance).SetValue(player, new ConcurrentQueue<byte>());
        typeof(GameLiving).GetField("<TempProperties>k__BackingField", PrivateInstance).SetValue(player, new PropertyCollection());
        return player;
    }

    private static Mob NewMob(string name, int x = 0, ushort region = 30)
    {
        var mob = (Mob) RuntimeHelpers.GetUninitializedObject(typeof(Mob));
        mob.Name = name;
        mob.ObjectState = GameObject.eObjectState.Active;
        mob.TestX = x;
        mob.Region = region;
        typeof(GameNPC).GetField("m_brains", PrivateInstance).SetValue(mob, new ArrayList());
        typeof(GameLiving).GetField("<TempProperties>k__BackingField", PrivateInstance).SetValue(mob, new PropertyCollection());
        return mob;
    }

    private static void Die(GameNPC dead, GameObject killer, params GamePlayer[] earners) =>
        ArtifactEncounterCredit.OnDying(GameLivingEvent.Dying, dead, new DyingEventArgs(killer, earners.ToList()));

    private static BattleGroup BattlegroupOf(params Player[] members)
    {
        var battlegroup = new BattleGroup();

        foreach (Player member in members)
        {
            battlegroup.Members.Add(member, false);
            member.TempProperties.SetProperty(BattleGroup.BATTLEGROUP_PROPERTY, battlegroup);
        }

        return battlegroup;
    }

    [Test]
    public void MappedKill_GrantsCreditToEveryEarnerInRange_AndTellsThem()
    {
        Player killer = NewPlayer("Killer");
        Player mate = NewPlayer("Mate", x: 2000);
        Player corpse = NewPlayer("Corpse", alive: false);
        Player distant = NewPlayer("Distant", x: WorldMgr.MAX_EXPFORKILL_DISTANCE + 500);
        Player elsewhere = NewPlayer("Elsewhere", region: 73);

        // The Dying event lists the attackers and their group members in range (GameLiving.ProcessDeath).
        Die(NewMob("Minkhat"), killer, killer, mate, corpse, distant, elsewhere);

        foreach (Player earned in new[] { killer, mate })
        {
            Assert.That(ArtifactMgr.HasEncounterCredit(earned, Leggings), Is.True, earned.Name);
            Assert.That(earned.Messages.Single(), Does.StartWith("With Minkhat defeated in Mesothalassa, ").And.Contain(LeggingsName).And.Contain("scholar"));
            ArtifactEncounter credit = earned.GetFinishedQuests().OfType<ArtifactEncounter>().Single();
            Assert.That(credit.Step, Is.EqualTo(ArtifactEncounter.CREDIT_STEP), "the scholar's credit format");
        }

        foreach (Player missed in new[] { corpse, distant, elsewhere })
        {
            Assert.That(ArtifactMgr.HasEncounterCredit(missed, Leggings), Is.False, missed.Name);
            Assert.That(missed.Messages, Is.Empty);
        }
    }

    [Test]
    public void ChatLine_UsesTheArticleForACommonName_AndTheZone()
    {
        ArtifactEncounterCredit.Load([Target("toa-enc-1001-protector-of-light", Leggings, "protector of light", "group", zone: "Oceanus Boreal")]);
        Player player = NewPlayer("Belted");

        Die(NewMob("protector of light"), player, player);

        Assert.That(player.Messages.Single(), Does.StartWith("With the protector of light defeated in Oceanus Boreal, you have earned"));

        // A creature of several zones: the table's zone list is not repeated as the place of death.
        ArtifactEncounterCredit.Load([Target("toa-enc-1042-tame-shark", Harp, "tame shark", "battlegroup", zone: "Mesothalassa, Oceanus Hesperos or Oceanus Boreal")]);
        Die(NewMob("tame shark"), player, player);
        Assert.That(player.Messages.Last(), Does.StartWith("With the tame shark defeated, you have earned"));
    }

    [Test]
    public void NameMatchIgnoresCase_AndTheKillerAloneCountsWithoutAList()
    {
        Player killer = NewPlayer("Killer");

        ArtifactEncounterCredit.OnDying(GameLivingEvent.Dying, NewMob("MINKHAT"), new DyingEventArgs(killer));

        Assert.That(ArtifactMgr.HasEncounterCredit(killer, Leggings), Is.True);
    }

    [Test]
    public void CreditIsNotGrantedTwice_NorAfterTheArtifactWasAwakened()
    {
        Player player = NewPlayer("Twice");
        Die(NewMob("Minkhat"), player, player);
        Die(NewMob("Minkhat"), player, player);

        Assert.That(player.GetFinishedQuests().OfType<ArtifactEncounter>().Count(), Is.EqualTo(1));
        Assert.That(player.Messages, Has.Count.EqualTo(1), "the second kill says nothing");

        Player veteran = NewPlayer("Veteran");
        ArtifactQuest.RecordAwakened(veteran, Leggings, "Alvarus_Leggings_plate");
        Die(NewMob("Minkhat"), veteran, veteran);

        Assert.That(ArtifactMgr.HasEncounterCredit(veteran, Leggings), Is.False, "awakened once per character");
        Assert.That(veteran.Messages, Is.Empty);

        // Credit used up by a scholar is not handed out again either: the awakening record stays.
        Player used = NewPlayer("Used");
        Die(NewMob("Minkhat"), used, used);
        Assert.That(ArtifactMgr.ConsumeEncounterCredit(used, Leggings), Is.True);
        ArtifactQuest.RecordAwakened(used, Leggings, "Alvarus_Leggings_plate");
        Die(NewMob("Minkhat"), used, used);
        Assert.That(ArtifactMgr.HasEncounterCredit(used, Leggings), Is.False);
    }

    [Test]
    public void BotsAndCompanionsNeverEarn()
    {
        // A companion or world bot is a GameNPC: alone it leaves no player in the Dying event.
        Bot bot = (Bot) RuntimeHelpers.GetUninitializedObject(typeof(Bot));
        Mob minkhat = NewMob("Minkhat");
        Assert.DoesNotThrow(() => ArtifactEncounterCredit.OnDying(GameLivingEvent.Dying, minkhat, new DyingEventArgs(bot, [])));
        Assert.That(ArtifactEncounterCredit.Earners(minkhat, new DyingEventArgs(bot, []), ArtifactCreditMode.Battlegroup), Is.Empty);

        // A bot that happens to carry a mapped name is no encounter.
        Bot named = (Bot) RuntimeHelpers.GetUninitializedObject(typeof(Bot));
        named.Name = "Minkhat";
        Player player = NewPlayer("Player");
        Assert.That(ArtifactEncounterCredit.IsCreditKill(named), Is.False);
        ArtifactEncounterCredit.OnDying(GameLivingEvent.Dying, named, new DyingEventArgs(player, [player]));
        Assert.That(ArtifactMgr.HasEncounterCredit(player, Leggings), Is.False);
    }

    [Test]
    public void BattlegroupEncounter_CreditsTheBattlegroupInRange_GroupEncounterOnlyTheEarners()
    {
        Player killer = NewPlayer("Killer");
        Player raider = NewPlayer("Raider", x: 3000);
        Player straggler = NewPlayer("Straggler", x: WorldMgr.MAX_EXPFORKILL_DISTANCE + 500);
        Player fallen = NewPlayer("Fallen", alive: false);
        BattlegroupOf(killer, raider, straggler, fallen);

        Die(NewMob("Kosma"), killer, killer);

        Assert.That(ArtifactMgr.HasEncounterCredit(killer, Harp), Is.True);
        Assert.That(ArtifactMgr.HasEncounterCredit(raider, Harp), Is.True, "battlegroup member in range, not in the kill list");
        Assert.That(raider.Messages.Single(), Does.Contain("Kosma").And.Contain("Artifact Scholar"),
            "this test's harp has no scroll set: the bookless wording");
        Assert.That(ArtifactMgr.HasEncounterCredit(straggler, Harp), Is.False, "out of range");
        Assert.That(ArtifactMgr.HasEncounterCredit(fallen, Harp), Is.False, "dead");

        // The same battlegroup on a group encounter: only the players of the Dying event.
        ArtifactEncounterCredit.Load([Target("toa-enc-1001-cynere", Leggings, "Cynere", "group")]);
        Die(NewMob("Cynere"), killer, killer);

        Assert.That(ArtifactMgr.HasEncounterCredit(killer, Leggings), Is.True);
        Assert.That(ArtifactMgr.HasEncounterCredit(raider, Leggings), Is.False, "group credit does not reach the battlegroup");
    }

    [Test]
    public void UnmappedCreature_OrAnotherRegion_GrantsNothing()
    {
        Player player = NewPlayer("Hunter");
        Die(NewMob("Minkhat marauder"), player, player);
        Die(NewMob("Khonorn"), player, player);

        Assert.That(player.GetFinishedQuests(), Is.Empty);
        Assert.That(player.Messages, Is.Empty);

        // A row with a region serves that region only; region 0 serves every region.
        ArtifactEncounterCredit.Load([Target("toa-enc-1001-minkhat-73", Leggings, "Minkhat", "group", region: 73)]);
        Die(NewMob("Minkhat", region: 30), player, player);
        Assert.That(ArtifactMgr.HasEncounterCredit(player, Leggings), Is.False);

        Player albion = NewPlayer("Albion", region: 73);
        Die(NewMob("Minkhat", region: 73), albion, albion);
        Assert.That(ArtifactMgr.HasEncounterCredit(albion, Leggings), Is.True);
    }

    [Test]
    public void Mapping_ParsesModes_DropsDuplicates_AndNamesTheGuardians()
    {
        Assert.That(ArtifactEncounterCredit.ParseMode("battlegroup"), Is.EqualTo(ArtifactCreditMode.Battlegroup));
        Assert.That(ArtifactEncounterCredit.ParseMode(" BattleGroup "), Is.EqualTo(ArtifactCreditMode.Battlegroup));
        Assert.That(ArtifactEncounterCredit.ParseMode("group"), Is.EqualTo(ArtifactCreditMode.Group));
        Assert.That(ArtifactEncounterCredit.ParseMode(null), Is.EqualTo(ArtifactCreditMode.Group));

        Assert.That(ArtifactEncounterCredit.Count, Is.EqualTo(4));
        Assert.That(ArtifactEncounterCredit.DescribeGuardians(Harp), Is.EqualTo("Hallea, Kosma or Zoea in Mesothalassa"));
        Assert.That(ArtifactEncounterCredit.DescribeGuardians(Leggings), Is.EqualTo("Minkhat in Mesothalassa"));
        Assert.That(ArtifactEncounterCredit.DescribeGuardians("9999"), Is.Null);
        Assert.That(ArtifactEncounterCredit.WithArticle("protector of light"), Is.EqualTo("the protector of light"));
        Assert.That(ArtifactEncounterCredit.WithArticle("wavelord", firstLetterUppercase: true), Is.EqualTo("The wavelord"));
        Assert.That(ArtifactEncounterCredit.WithArticle("High Priest Akil"), Is.EqualTo("High Priest Akil"));

        ArtifactEncounterCredit.Load(
        [
            Target("x1", Leggings, "Landrine", "battlegroup", zone: "Stygian Delta"),
            Target("x2", Leggings, "protector of light", "battlegroup", zone: "Oceanus Boreal"),
            Target("x3", Leggings, "Hounthro", "battlegroup", zone: "Stygian Delta"),
            Target("x4", Leggings, "Kertom", "battlegroup"),
        ]);
        Assert.That(ArtifactEncounterCredit.DescribeGuardians(Leggings),
            Is.EqualTo("Landrine or Hounthro in Stygian Delta, the protector of light in Oceanus Boreal or Kertom in Atlantis"),
            "grouped by zone in table order; articles for common names; no zone reads Atlantis");

        int kept = ArtifactEncounterCredit.Load(
        [
            Target("a", Leggings, " Minkhat ", "group"),
            Target("b", Leggings, "minkhat", "group"),
            Target("c", Leggings, "", "group"),
            Target("d", "", "Minkhat", "group"),
            null,
        ]);
        Assert.That(kept, Is.EqualTo(1), "one pair per artifact, creature and region; blank rows ignored");
        Assert.That(ArtifactEncounterCredit.TargetsFor("MINKHAT", 30).Single().MobName, Is.EqualTo("Minkhat"));
    }

    [Test]
    public void ScholarWithoutCredit_NamesTheGuardian_AndAphaestia()
    {
        Player player = NewPlayer("Student");

        string reply = ArtifactAwakening.Explain(ArtifactRefusal.NoCredit, player, Harp);

        Assert.That(reply, Does.Contain("defeat Hallea, Kosma or Zoea in Mesothalassa").And.Contain("Aphaestia"));

        ArtifactEncounterCredit.Load([]);
        Assert.That(ArtifactAwakening.Explain(ArtifactRefusal.NoCredit, player, Harp),
            Does.Contain("Face the encounter").And.Contain("Aphaestia"), "no mapping: the old hint");
    }
}
