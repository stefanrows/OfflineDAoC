using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.DatabaseUpdate;
using DOL.GS.Keeps;
using DOL.GS.PropertyCalc;
using DOL.GS.ServerProperties;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Task 48, siege balance (owner decisions of 2026-09-28): Warden keeps
/// stand at level 1, sieges are limited per guild with a server-wide cap, a guild
/// may claim three keeps, and direct-damage spells hurt doors at half effect.</summary>
[TestFixture, NonParallelizable]
public sealed class UT_SiegeBalance
{
    private EpicTestServerScope _server;

    [SetUp]
    public void SetUp()
    {
        _server = new EpicTestServerScope();
        DOL.GS.Tests.RvrEventTestState.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        DOL.GS.Tests.RvrEventTestState.Clear();
        _server.Dispose();
    }

    // ---- 1a: Warden keeps at level 1 -----------------------------------------------

    [TestCase(true, 50, false, 5, (byte)1, TestName = "A level-5 Warden keep drops to level 1")]
    [TestCase(true, 50, false, 4, (byte)1, TestName = "Dun Crauchon at level 4 drops to level 1")]
    [TestCase(true, 50, false, 1, null, TestName = "A Warden keep at level 1 is left alone (idempotent)")]
    [TestCase(false, 50, false, 5, null, TestName = "A guild-claimed keep keeps its level")]
    [TestCase(true, 60, true, 10, null, TestName = "A relic keep is untouched")]
    [TestCase(true, 60, false, 10, null, TestName = "Only base-level-50 keeps change")]
    public void WardenKeepsStandAtLevelOne(bool garrison, int baseLevel, bool relic, int level, byte? expected) =>
        Assert.That(PvpKeepCampaign.WardenStartLevel(garrison, baseLevel, relic, level), Is.EqualTo(expected));

    [Test]
    public void RepeatingTheStartLevelRuleChangesNothing()
    {
        byte? first = PvpKeepCampaign.WardenStartLevel(true, 50, false, 5);
        Assert.That(first, Is.EqualTo(PvpKeepCampaign.WardenKeepLevel));
        Assert.That(PvpKeepCampaign.WardenStartLevel(true, 50, false, first.Value), Is.Null);
    }

    [TestCase(1, 52, 63, TestName = "Level-1 Warden keep: guards 52, lord 63")]
    [TestCase(5, 59, 70, TestName = "Level-5 keep (claimed): guards 59, lord 70")]
    public void GuardAndLordLevelsFollowTheKeepLevel(int keepLevel, int guardLevel, int lordLevel)
    {
        var keep = new GameKeep { DBKeep = new DbKeep { BaseLevel = 50, Level = (byte)keepLevel } };
        var component = (GameKeepComponent)RuntimeHelpers.GetUninitializedObject(typeof(GameKeepComponent));
        component.Keep = keep;
        var guard = (GuardFighter)RuntimeHelpers.GetUninitializedObject(typeof(GuardFighter));
        var lord = (GuardLord)RuntimeHelpers.GetUninitializedObject(typeof(GuardLord));
        guard.Component = component;
        lord.Component = component;
        const double liveMultiplier = 1.6; // keep_guard_level_multiplier in the save
        Assert.Multiple(() =>
        {
            Assert.That(AbstractGameKeep.GuardLevelFor(keep.GetBaseLevel(guard), keep.Level, liveMultiplier), Is.EqualTo(guardLevel));
            Assert.That(AbstractGameKeep.GuardLevelFor(keep.GetBaseLevel(lord), keep.Level, liveMultiplier), Is.EqualTo(lordLevel));
        });
    }

    [TestCase(1, 10_000, TestName = "Level-1 door: 10,000 hit points")]
    [TestCase(5, 50_000, TestName = "Level-5 door: 50,000 hit points")]
    public void DoorHitPointsFollowTheKeepLevel(int keepLevel, int hitPoints)
    {
        int previousBase = Properties.KEEP_DOORS_BASE_HEALTH;
        double previousModifier = Properties.KEEP_DOORS_HEALTH_UPGRADE_MODIFIER;
        try
        {
            Properties.KEEP_DOORS_BASE_HEALTH = 200;
            Properties.KEEP_DOORS_HEALTH_UPGRADE_MODIFIER = 1;
            GameKeepDoor gate = Gate(keepLevel);
            Assert.That(new MaxHealthCalculator().CalcValue(gate, eProperty.MaxHealth), Is.EqualTo(hitPoints));
        }
        finally
        {
            Properties.KEEP_DOORS_BASE_HEALTH = previousBase;
            Properties.KEEP_DOORS_HEALTH_UPGRADE_MODIFIER = previousModifier;
        }
    }

    // ---- property migration (starting_keep_level, guilds_claim_limit) ------------------

    [TestCase("4", "4", "4", true, TestName = "Untouched shipped value is migrated")]
    [TestCase("1", "1", "4", false, TestName = "Already migrated row is left alone")]
    [TestCase("4", "1", "4", false, TestName = "An operator's later choice stays")]
    [TestCase("2", "4", "4", false, TestName = "A customized value stays")]
    [TestCase(null, null, "4", false, TestName = "A missing value is not touched")]
    public void PropertyMigrationTouchesOnlyUntouchedLegacyRows(string value, string defaultValue, string legacy, bool expected) =>
        Assert.That(FrontierKeepBalanceUpdate.ShouldReplace(value, defaultValue, legacy), Is.EqualTo(expected));

    [Test]
    public void PropertyMigrationTargetsTheDecidedValues()
    {
        var changes = FrontierKeepBalanceUpdate.Changes.ToDictionary(change => change.Key);
        Assert.Multiple(() =>
        {
            Assert.That(changes["starting_keep_level"].Legacy, Is.EqualTo("4"));
            Assert.That(changes["starting_keep_level"].Target, Is.EqualTo("1"));
            Assert.That(changes["guilds_claim_limit"].Legacy, Is.EqualTo("1"));
            Assert.That(changes["guilds_claim_limit"].Target, Is.EqualTo("-1"));
            Assert.That(DefaultOf(nameof(Properties.STARTING_KEEP_LEVEL)), Is.EqualTo(1), "new saves start unclaimed keeps at 1");
            Assert.That(DefaultOf(nameof(Properties.GUILDS_CLAIM_LIMIT)), Is.EqualTo(-1), "new saves have unlimited keep claims");
        });
    }

    private static object DefaultOf(string field) =>
        typeof(Properties).GetField(field).GetCustomAttribute<ServerPropertyAttribute>().DefaultValue;

    // ---- 2b: one siege per guild, server-wide cap -------------------------------------

    private static AutonomousRvrEventLayer.LiveObjective Keep(int id) => new($"rvr-keep-{9200 + id}", $"Balance Faste {id}",
        AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Midgard, 100, 100_000 + id * 10_000, 100_000, 0, false, 0, 0, 4, 2,
        OwningGuild: PvpKeepCampaign.GarrisonName);

    private static readonly AutonomousRvrEventLayer.LiveObjective Roam = new("roam-balance", "Frontier patrol",
        AutonomousRvrEventLayer.Intent.Roam, eRealm.Midgard, 100, 50_000, 50_000, 0, false, 0, 0, 0, 0);

    private static AutonomousRvrEventLayer.Force Warband(string id, string guild) =>
        new(id, eRealm.Albion, 8, 50, 2, GuildName: guild);

    // 0.7 opens an assault (weight 1.0 for eight level-50 members) but lies above
    // the join chance for a started siege (about 0.59), so forces do not reinforce.
    private const double OpenNotJoin = 0.7;

    [Test]
    public void TwoGuildsOpenTwoSiegesAtOnce()
    {
        var keeps = Enumerable.Range(1, 3).Select(Keep).Append(Roam).ToArray();
        var first = AutonomousRvrEventLayer.ChooseOrJoin(Warband("raiders-1", "Raiders"), keeps, 1_000, OpenNotJoin);
        var second = AutonomousRvrEventLayer.ChooseOrJoin(Warband("others-1", "Others"), keeps, 2_000, OpenNotJoin);
        Assert.Multiple(() =>
        {
            Assert.That(first?.IsSharedEvent, Is.True);
            Assert.That(first?.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.AssaultKeep));
            Assert.That(second?.IsSharedEvent, Is.True, "a second guild opens its own siege");
            Assert.That(second?.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.AssaultKeep));
            Assert.That(second?.TargetId, Is.Not.EqualTo(first?.TargetId));
            Assert.That(ActiveSieges(), Is.EqualTo(2));
        });
    }

    [Test]
    public void AGuildCannotOpenASecondSiege()
    {
        var keeps = Enumerable.Range(1, 3).Select(Keep).Append(Roam).ToArray();
        var opened = AutonomousRvrEventLayer.ChooseOrJoin(Warband("raiders-1", "Raiders"), keeps, 1_000, OpenNotJoin);
        var again = AutonomousRvrEventLayer.ChooseOrJoin(Warband("raiders-2", "Raiders"), keeps, 2_000, OpenNotJoin);
        Assert.Multiple(() =>
        {
            Assert.That(opened?.IsSharedEvent, Is.True);
            Assert.That(again?.IsSharedEvent, Is.False, "the guild's second warband roams instead");
            Assert.That(again?.TargetId, Is.EqualTo(Roam.Id));
            Assert.That(ActiveSieges(), Is.EqualTo(1));
        });
        // Its second warband may still reinforce the guild's own siege.
        var reinforce = AutonomousRvrEventLayer.ChooseOrJoin(Warband("raiders-3", "Raiders"), keeps, 3_000, 0);
        Assert.That(reinforce?.TargetId, Is.EqualTo(opened.TargetId));
    }

    [Test]
    public void TheServerWideSiegeCapHolds()
    {
        int guilds = AutonomousRvrEventLayer.MaxConcurrentSieges + 1;
        var keeps = Enumerable.Range(1, guilds + 2).Select(Keep).Append(Roam).ToArray();
        for (int i = 0; i < AutonomousRvrEventLayer.MaxConcurrentSieges; i++)
            Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(Warband($"guild-{i}", $"Guild {i}"), keeps, 1_000 + i, OpenNotJoin)?.IsSharedEvent,
                Is.True, $"guild {i} opens");
        var late = AutonomousRvrEventLayer.ChooseOrJoin(Warband("late", "Late Guild"), keeps, 9_000, OpenNotJoin);
        Assert.Multiple(() =>
        {
            Assert.That(late?.IsSharedEvent, Is.False, "the cap keeps the next guild roaming");
            Assert.That(ActiveSieges(), Is.EqualTo(AutonomousRvrEventLayer.MaxConcurrentSieges));
        });
    }

    [Test]
    public void SiegeRuleIsPerSideWithACap()
    {
        string raiders = AutonomousRvrEventLayer.SiegeSideKey("Raiders", eRealm.Albion);
        string albionRealm = AutonomousRvrEventLayer.SiegeSideKey(null, eRealm.Albion);
        string[] full = Enumerable.Range(0, AutonomousRvrEventLayer.MaxConcurrentSieges).Select(i => $"guild:G{i}").ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrEventLayer.MayOpenSiege(raiders, Array.Empty<string>(), false), Is.True);
            Assert.That(AutonomousRvrEventLayer.MayOpenSiege(raiders, new[] { raiders }, false), Is.False);
            Assert.That(AutonomousRvrEventLayer.MayOpenSiege(raiders, new[] { albionRealm }, false), Is.True);
            Assert.That(AutonomousRvrEventLayer.MayOpenSiege(albionRealm, new[] { raiders }, false), Is.True);
            Assert.That(AutonomousRvrEventLayer.MayOpenSiege(raiders, full, false), Is.False);
            Assert.That(AutonomousRvrEventLayer.MayOpenSiege(raiders, Array.Empty<string>(), true), Is.False,
                "a relic-carrier event still pauses new sieges");
        });
    }

    private static int ActiveSieges()
    {
        var events = (IDictionary)typeof(AutonomousRvrEventLayer)
            .GetField("Events", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        return events.Count;
    }

    // ---- 5: damage spells on doors ------------------------------------------------------

    [TestCase(eSpellType.DirectDamage, true, TestName = "Nukes hit doors")]
    [TestCase(eSpellType.Bolt, true, TestName = "Bolts hit doors")]
    [TestCase(eSpellType.SiegeDirectDamage, true, TestName = "Siege damage still hits doors")]
    [TestCase(eSpellType.SiegeArrow, true, TestName = "Siege arrows still hit doors")]
    [TestCase(eSpellType.DamageOverTime, false, TestName = "DoTs do not affect doors")]
    [TestCase(eSpellType.Stun, false, TestName = "Stuns do not affect doors")]
    [TestCase(eSpellType.Mesmerize, false, TestName = "Mez does not affect doors")]
    [TestCase(eSpellType.SpeedDecrease, false, TestName = "Snares do not affect doors")]
    [TestCase(eSpellType.StrengthDebuff, false, TestName = "Debuffs do not affect doors")]
    [TestCase(eSpellType.DirectDamageWithDebuff, false, TestName = "Damage with a debuff rider stays off doors")]
    [TestCase(eSpellType.Lifedrain, false, TestName = "Lifedrains stay off doors")]
    public void OnlyDirectDamageAndSiegeSpellsAffectDoors(eSpellType type, bool affects) =>
        Assert.That(KeepDoorSpellPolicy.AffectsKeepDoor(type), Is.EqualTo(affects));

    [TestCase(eSpellType.DirectDamage, 0, true, TestName = "Bots nuke a gate with a single-target nuke")]
    [TestCase(eSpellType.DirectDamage, 350, false, TestName = "Bots do not cast a targeted area nuke at a gate")]
    [TestCase(eSpellType.Bolt, 0, true, TestName = "Bots bolt a gate")]
    [TestCase(eSpellType.Stun, 0, false, TestName = "Bots do not stun a gate")]
    public void BotsCastOnlySingleTargetDamageAtDoors(eSpellType type, int radius, bool worth) =>
        Assert.That(KeepDoorSpellPolicy.WorthCastingAtDoor(type, radius), Is.EqualTo(worth));

    [TestCase(eSpellType.DirectDamage, 5, 400, 150, TestName = "A 400 nuke on a level-5 door deals 150")]
    [TestCase(eSpellType.DirectDamage, 1, 400, 190, TestName = "A 400 nuke on a level-1 door deals 190")]
    [TestCase(eSpellType.Bolt, 1, 400, 190, TestName = "A 400 bolt on a level-1 door deals 190")]
    [TestCase(eSpellType.SiegeDirectDamage, 1, 400, 380, TestName = "Siege spell damage is not halved")]
    public void DamageSpellsHitDoorsAtHalfEffect(eSpellType type, int keepLevel, int dealt, int applied)
    {
        bool previousAllow = Properties.DOORS_ALLOWPETATTACK;
        int previousToughness = Properties.SET_KEEP_DOOR_TOUGHNESS;
        try
        {
            Properties.DOORS_ALLOWPETATTACK = true;
            Properties.SET_KEEP_DOOR_TOUGHNESS = 100;
            GameKeepDoor gate = Gate(keepLevel);
            var hit = new AttackData
            {
                Attacker = Caster(), Target = gate, Damage = dealt, DamageType = eDamageType.Heat,
                AttackType = AttackData.eAttackType.Spell, AttackResult = eAttackResult.HitUnstyled,
                SpellHandler = SpellOf(type),
            };
            gate.ModifyAttack(hit);
            Assert.That(hit.Damage, Is.EqualTo(applied));
        }
        finally
        {
            Properties.DOORS_ALLOWPETATTACK = previousAllow;
            Properties.SET_KEEP_DOOR_TOUGHNESS = previousToughness;
        }
    }

    [TestCase(eCharacterClass.Wizard, false, true, TestName = "A Wizard nukes the gate when no ram seat is free")]
    [TestCase(eCharacterClass.Wizard, true, false, TestName = "A Wizard rides a ram with a free seat instead")]
    [TestCase(eCharacterClass.Armsman, true, true, TestName = "Melee classes always work the gate")]
    [TestCase(eCharacterClass.Cleric, false, false, TestName = "Healers stay free")]
    public void CastersNukeTheGateOnlyWithoutAFreeRamSeat(eCharacterClass characterClass, bool freeSeat, bool works) =>
        Assert.That(AutonomousSiegeDoctrine.CanDamageDoor(characterClass, freeSeat), Is.EqualTo(works));

    private static GameKeepDoor Gate(int keepLevel)
    {
        var keep = new GameKeep { DBKeep = new DbKeep { Realm = 1, BaseLevel = 50, Level = (byte)keepLevel } };
        var component = (GameKeepComponent)RuntimeHelpers.GetUninitializedObject(typeof(GameKeepComponent));
        component.Keep = keep;
        return new GameKeepDoor { Component = component };
    }

    private sealed class Walker : GameBot
    {
        private Walker() : base((OfflineWorldBotRecord)null) { }
        public override eRealm Realm { get; set; }
        public override bool IsAlive => true;
    }

    private static GameBot Caster()
    {
        var bot = (Walker)RuntimeHelpers.GetUninitializedObject(typeof(Walker));
        bot.Realm = eRealm.Albion;
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new ArrayList());
        return bot;
    }

    private static ISpellHandler SpellOf(eSpellType type)
    {
        var handler = DispatchProxy.Create<ISpellHandler, SpellOnly>();
        ((SpellOnly)(object)handler).Spell = new Spell(new DbSpell { Type = type.ToString(), Target = "Enemy", Damage = 100 }, 50);
        return handler;
    }

    public class SpellOnly : DispatchProxy
    {
        public Spell Spell;
        protected override object Invoke(MethodInfo targetMethod, object[] args) =>
            targetMethod.Name == "get_Spell" ? Spell : throw new NotSupportedException(targetMethod.Name);
    }
}
