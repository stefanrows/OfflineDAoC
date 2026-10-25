// R6 review fixes of the upstream RvR port (docs/RVR_PORT_MAP.md, "R6 fixes"). The RvR world (Realm-1) behaves like
// upstream shadowofze/OfflineDAoC 0.38c; Camlann stays unchanged.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS.Keeps;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture, NonParallelizable]
public sealed class UT_RvrR6Fixes
{
    private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
    private long _savedTick;

    // Ten hours of server uptime: the live clock is a large positive number, never the tests' usual 0.
    private const long Uptime = 10 * 60 * 60_000L;

    private static long Now => GameLoop.GameLoopTime;
    private static void SetTick(long value) => typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime)).SetValue(null, value);

    [SetUp]
    public void SetUp()
    {
        _savedTick = GameLoop.GameLoopTime;
        RvrEventTestState.ClearRealm();
        RealmRvrEventLayer.StagedAssault = true;
    }

    [TearDown]
    public void TearDown()
    {
        SetTick(_savedTick);
        RvrEventTestState.Clear();
    }

    // ---- 1. the event layer's deadlines fire on the live clock -------------------------------------------------

    private static (AutonomousRvrEventLayer.LiveObjective Keep, AutonomousRvrEventLayer.Force Attacker) OpenSiege(string id)
    {
        var keep = new AutonomousRvrEventLayer.LiveObjective(id, "Live clock keep",
            AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Hibernia, 200, 100, 100, 0, false, 0, 0, 5, 2);
        var attacker = new AutonomousRvrEventLayer.Force(id + "-attack", eRealm.Albion, 8, 50, 1);
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(attacker, [keep], Now, 0)?.TargetId, Is.EqualTo(id));
        return (keep, attacker);
    }

    private static long MusterRemaining(string id) =>
        RealmRvrEventLayer.Snapshot().Single(notice => notice.TargetId == id).MusterRemainingMilliseconds;

    /// <summary>Live 2026-10-25: a siege opened 18:06, its muster deadline 18:18 passed and no army marched by 18:26,
    /// because every live-clock Expire call returned (long.MinValue overflow). Every step below asks the live clock.</summary>
    [Test]
    public void TheRallyExtendsMarchesAndIsRepelledOnTheLiveClock()
    {
        string id = "rvr-keep-" + Guid.NewGuid().ToString("N");
        SetTick(Uptime);
        var (_, attacker) = OpenSiege(id);
        Assert.That(RealmRvrEventLayer.GetRallyOrder(attacker.GroupId, eRealm.Albion, Now)?.HomeMuster, Is.True,
            "the attackers gather at home first");

        // Muster deadline (12 min) with nobody gathered: one 5-minute extension.
        SetTick(Uptime + RealmRvrEventLayer.HomeMusterMilliseconds + 1_000);
        Assert.Multiple(() =>
        {
            Assert.That(RealmRvrEventLayer.GetRallyOrder(attacker.GroupId, eRealm.Albion, Now)?.HomeMuster, Is.True);
            Assert.That(MusterRemaining(id), Is.InRange(RealmRvrEventLayer.MusterExtensionMilliseconds - 1_000,
                RealmRvrEventLayer.MusterExtensionMilliseconds), "the extension moved the march five minutes on");
        });

        // Extended deadline: the army marches.
        SetTick(Now + RealmRvrEventLayer.MusterExtensionMilliseconds + 1_000);
        Assert.Multiple(() =>
        {
            Assert.That(RealmRvrEventLayer.GetRallyOrder(attacker.GroupId, eRealm.Albion, Now)?.HomeMuster, Is.False,
                "the march order fired on the live clock");
            Assert.That(MusterRemaining(id), Is.EqualTo(0), "marching");
        });

        // Eight attackers reach the posts: the marched army attacks at once.
        for (long bot = 1; bot <= 8; bot++)
            RealmRvrEventLayer.ReportAttendance(id, attacker.GroupId, eRealm.Albion, bot, true, Now);
        Assert.That(RealmRvrEventLayer.IsBattleForce(attacker.GroupId, Now), Is.True, "the battle opened");

        // A second later the layer sees them; then nobody stands at the keep for ten minutes: repelled.
        SetTick(Now + 1_000);
        Assert.That(RealmRvrEventLayer.TryConsumeRelease(attacker.GroupId, Now, out _), Is.False);
        SetTick(Now + RealmRvrEventLayer.AttendanceFreshnessMilliseconds + RealmRvrEventLayer.RepelMilliseconds + 1_000);
        Assert.Multiple(() =>
        {
            Assert.That(RealmRvrEventLayer.TryConsumeRelease(attacker.GroupId, Now, out string reason), Is.True,
                "the repel ended the siege and released the army");
            Assert.That(reason, Does.StartWith("Siege defended"));
            Assert.That(RealmRvrEventLayer.Snapshot().Any(notice => notice.TargetId == id), Is.False);
        });
    }

    [Test]
    public void AMarchedRallyThatNeverArrivesFailsAtItsDeadlineOnTheLiveClock()
    {
        string id = "rvr-keep-" + Guid.NewGuid().ToString("N");
        SetTick(Uptime);
        var (_, attacker) = OpenSiege(id);
        SetTick(Uptime + RealmRvrEventLayer.HomeMusterMilliseconds + 1_000);
        RealmRvrEventLayer.GetRallyOrder(attacker.GroupId, eRealm.Albion, Now);
        SetTick(Now + RealmRvrEventLayer.MusterExtensionMilliseconds + 1_000);
        Assert.That(RealmRvrEventLayer.GetRallyOrder(attacker.GroupId, eRealm.Albion, Now)?.HomeMuster, Is.False);

        SetTick(Now + RealmEventPolicy.RecruitmentMilliseconds(0) + 1_000);
        Assert.Multiple(() =>
        {
            Assert.That(RealmRvrEventLayer.TryConsumeRelease(attacker.GroupId, Now, out string reason), Is.True,
                "the one-hour timer ended the rally and released its army");
            Assert.That(reason, Does.StartWith("Rally failed"));
        });
    }

    [Test]
    public void TheLiveClockThrottleStillSkipsCallsWithinHalfASecond()
    {
        string id = "rvr-keep-" + Guid.NewGuid().ToString("N");
        SetTick(Uptime);
        var (_, attacker) = OpenSiege(id);
        // The deadline passes, but the next live call comes 100 ms after the last run: no expiry yet ...
        SetTick(Uptime + RealmRvrEventLayer.HomeMusterMilliseconds + 1_000);
        RealmRvrEventLayer.GetRallyOrder(attacker.GroupId, eRealm.Albion, Now);
        long extended = MusterRemaining(id);
        SetTick(Now + 100);
        Type layer = typeof(RealmRvrEventLayer);
        long lastRun = (long)layer.GetField("_lastExpireTick", Hidden).GetValue(null);
        RealmRvrEventLayer.GetRallyOrder(attacker.GroupId, eRealm.Albion, Now);
        Assert.Multiple(() =>
        {
            Assert.That(extended, Is.GreaterThan(1), "the first live call ran (extension)");
            Assert.That((long)layer.GetField("_lastExpireTick", Hidden).GetValue(null), Is.EqualTo(lastRun),
                "a call within ExpireIntervalMilliseconds is throttled");
        });
    }

    // ---- 2. the fork's Camlann march restraint is off in the RvR world ------------------------------------------

    private sealed class MarchBot : GameBot
    {
        private MarchBot() : base((OfflineWorldBotRecord)null) { }
        public override bool IsAlive => true;
        public override eRealm Realm { get; set; }
        public override ushort CurrentRegionID { get; set; }
    }

    private static MarchBot WorldBot(string force)
    {
        var bot = (MarchBot)RuntimeHelpers.GetUninitializedObject(typeof(MarchBot));
        bot.Realm = eRealm.Albion;
        bot.CurrentRegionID = 1;
        typeof(GameBot).GetField("<IsAutonomousWorldBot>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, true);
        typeof(GameLiving).GetField("<TempProperties>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new PropertyCollection());
        typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot, new OfflineWorldBotRecord { ObjectiveKind = "RvR" });
        bot.TempProperties.SetProperty("RvrEventForce", force);
        return bot;
    }

    [TestCase(WorldModelKind.Camlann, ExpectedResult = true, TestName = "Camlann: a bot far from its keep plan marches without fighting")]
    [TestCase(WorldModelKind.Realm, ExpectedResult = false, TestName = "RvR: no march restraint, the column fights on the way (upstream)")]
    public bool TheMarchRestraintFollowsTheWorld(WorldModelKind world)
    {
        using var _ = WorldMode.OverrideForTests(world);
        string id = "rvr-keep-" + Guid.NewGuid().ToString("N");
        // A keep in another region: under the fork rule the bot is "marching" there.
        var keep = new AutonomousRvrEventLayer.LiveObjective(id, "March keep", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Hibernia, 200, 100, 100, 0, false, 0, 0, 5, 2);
        var force = new AutonomousRvrEventLayer.Force(id + "-attack", eRealm.Albion, 8, 50, 2);
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(force, [keep], 1, 0)?.TargetId, Is.EqualTo(id));
        MarchBot bot = WorldBot(force.GroupId);
        Assert.That(AutonomousRvrEventLayer.KeepPlan(force.GroupId, eRealm.Albion, GameLoop.GameLoopTime)?.TargetId, Is.EqualTo(id),
            "both worlds have the keep plan the old rule restrained on");
        return AutonomousSiegeMarch.IsMarching(bot);
    }

    [TestCase(WorldModelKind.Camlann, ExpectedResult = true)]
    [TestCase(WorldModelKind.Realm, ExpectedResult = false)]
    public bool RestrainsMarchOnlyInCamlann(WorldModelKind world) => AutonomousSiegeMarch.RestrainsMarch(world);

    // ---- 8. guild keep defense is off in the RvR world ----------------------------------------------------------

    [TestCase(WorldModelKind.Camlann, ExpectedResult = true, TestName = "Camlann: world bots answer their guild's keep alarm")]
    [TestCase(WorldModelKind.Realm, ExpectedResult = false, TestName = "RvR: no guild keep defense (upstream: realm defense only)")]
    public bool GuildKeepDefenseFollowsTheWorld(WorldModelKind world)
    {
        using var _ = WorldMode.OverrideForTests(world);
        Assert.That(AutonomousGuildKeepDefense.Applies(world), Is.EqualTo(world == WorldModelKind.Camlann));
        MarchBot bot = WorldBot("rvr-guild-defense");
        Assert.That(AutonomousGuildKeepDefense.PlanFor(bot), Is.Null);
        return AutonomousGuildKeepDefense.Eligible(bot);
    }

    // ---- 4. starting_keep_level: RvR world upstream's 4 ---------------------------------------------------------

    [TestCase(WorldModelKind.Realm, 1, ExpectedResult = 4, TestName = "RvR: upstream's starting level 4 whatever the saved property")]
    [TestCase(WorldModelKind.Camlann, 1, ExpectedResult = 1, TestName = "Camlann: the property (1)")]
    [TestCase(WorldModelKind.Camlann, 4, ExpectedResult = 4, TestName = "Camlann: an owner-set property is honoured")]
    public int StartingKeepLevelFollowsTheWorld(WorldModelKind world, int property) =>
        PvpKeepCampaign.StartingLevel(world, property);

    [TestCase(WorldModelKind.Realm, 4, 0)]
    [TestCase(WorldModelKind.Realm, 6, 2)]
    [TestCase(WorldModelKind.Camlann, 4, 3)]
    [TestCase(WorldModelKind.Camlann, 1, 0)]
    public void CaptureRealmPointsValueLevelsAboveTheWorldsStartingLevel(WorldModelKind world, int level, int upgrades)
    {
        int savedStart = ServerProperties.Properties.STARTING_KEEP_LEVEL;
        int savedBase = ServerProperties.Properties.KEEP_RP_BASE;
        int savedMultiplier = ServerProperties.Properties.KEEP_RP_MULTIPLIER;
        int savedUpgrade = ServerProperties.Properties.UPGRADE_MULTIPLIER;
        try
        {
            ServerProperties.Properties.STARTING_KEEP_LEVEL = 1;
            ServerProperties.Properties.KEEP_RP_BASE = 4500;
            ServerProperties.Properties.KEEP_RP_MULTIPLIER = 50;
            ServerProperties.Properties.UPGRADE_MULTIPLIER = 10;
            using var _ = WorldMode.OverrideForTests(world);
            var keep = new GameKeep { DBKeep = new DOL.Database.DbKeep { BaseLevel = 50, Level = (byte)level } };
            var rules = ScriptMgr.CreateServerRules(EGameServerType.GST_PvP, world);
            Assert.That(rules.GetRealmPointsForKeep(keep), Is.EqualTo(4500 + upgrades * 10));
        }
        finally
        {
            ServerProperties.Properties.STARTING_KEEP_LEVEL = savedStart;
            ServerProperties.Properties.KEEP_RP_BASE = savedBase;
            ServerProperties.Properties.KEEP_RP_MULTIPLIER = savedMultiplier;
            ServerProperties.Properties.UPGRADE_MULTIPLIER = savedUpgrade;
        }
    }

    // ---- 3. forced realm raids: Stop event and no fake clock -----------------------------------------------------

    private static readonly BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static object ForcedRvrRaid(AutonomousRealmRaid.Definition definition)
    {
        Type raidType = typeof(AutonomousRealmRaid).GetNestedType("Raid", BindingFlags.NonPublic);
        object raid = Activator.CreateInstance(raidType, true);
        raidType.GetField("Definition").SetValue(raid, definition);
        raidType.GetField("Forced").SetValue(raid, true);
        // What Start writes for a forced expedition of the RvR world (upstream 0.38c: no time limit).
        raidType.GetField("Deadline").SetValue(raid, long.MaxValue);
        raidType.GetField("Created").SetValue(raid, GameLoop.GameLoopTime);
        raidType.GetField("Hub").SetValue(raid, RealmRaidMuster.Hubs.Single(hub => hub.Event == definition.Id));
        return raid;
    }

    private static T Field<T>(string name) => (T)typeof(AutonomousRealmRaid).GetField(name, Any).GetValue(null);

    [Test]
    public void StopEventEndsAForcedExpeditionReleasesItsReservationsAndSetsNoCooldown()
    {
        using var _ = WorldMode.OverrideForTests(WorldModelKind.Realm);
        AutonomousRealmRaid.Definition definition = AutonomousRealmRaid.Definitions.First(d => d.IsDungeon);
        object raid = ForcedRvrRaid(definition);
        var raids = Field<System.Collections.IDictionary>("Raids");
        var reservations = Field<System.Collections.Concurrent.ConcurrentDictionary<long, string>>("Reservations");
        var cooldowns = Field<Dictionary<string, long>>("Cooldowns");
        const long reservedBot = 987_654_321;
        try
        {
            raids[definition.Id] = raid;
            reservations[reservedBot] = definition.Id;
            cooldowns.Remove(definition.Id);

            AutonomousRealmRaid.Summary running = AutonomousRealmRaid.Snapshot().Single(summary => summary.Id == definition.Id);
            Assert.Multiple(() =>
            {
                Assert.That(running.Remaining, Is.EqualTo(-1), "no clock: the launcher shows 'no time limit'");
                Assert.That(running.Phase, Is.EqualTo("Muster"));
                Assert.That(running.State, Does.Contain("starts when 200 are staged"));
            });

            Assert.That(AutonomousRealmRaid.Stop(definition.Id, out string reason), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reason, Does.Contain("stopped").And.Contain("no cooldown"));
                Assert.That(raids.Contains(definition.Id), Is.False, "the expedition ended");
                Assert.That(reservations.ContainsKey(reservedBot), Is.False, "its reserved bots are free again");
                Assert.That(cooldowns.ContainsKey(definition.Id), Is.False, "a player stop sets no cooldown");
            });
            Assert.That(AutonomousRealmRaid.Stop(definition.Id, out string again), Is.False);
            Assert.That(again, Is.EqualTo("This expedition is not running."));
        }
        finally
        {
            raids.Remove(definition.Id);
            reservations.TryRemove(reservedBot, out string _);
            cooldowns.Remove(definition.Id);
        }
    }

    [Test]
    public void AnEndedExpeditionStillGetsItsCooldown()
    {
        AutonomousRealmRaid.Definition definition = AutonomousRealmRaid.Definitions.First(d => d.IsDungeon);
        object raid = ForcedRvrRaid(definition);
        var cooldowns = Field<Dictionary<string, long>>("Cooldowns");
        try
        {
            cooldowns.Remove(definition.Id);
            typeof(AutonomousRealmRaid).GetMethod("End", Any).Invoke(null, [raid, GameLoop.GameLoopTime, "Staging failed: 12 adventurers arrived."]);
            Assert.That(cooldowns.ContainsKey(definition.Id), Is.True, "only a player stop skips the cooldown");
        }
        finally { cooldowns.Remove(definition.Id); }
    }

    [Test]
    public void ForcedRaidsHaveNoClockOfTheirOwn()
    {
        // Upstream 0.38c: the encounter or the launcher's STOP EVENT (RealmEventsPanel) ends a forced expedition.
        Assert.That(RealmRaidRecruitmentPolicy.BattleExpired(true, long.MaxValue - 1, long.MaxValue), Is.False,
            "upstream: a forced raid has no battle time limit");
        Assert.That(RealmRaidRecruitmentPolicy.StagingExpired(true, long.MaxValue - 1, 0, 0, 0, true), Is.False,
            "upstream: a forced raid's staging never expires on its own");
    }
}
