using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Task 48, points 1-5: safe border hubs, planner door rule, keep-route
/// give-up and idle siege close, warband boarding, frontier release.</summary>
[TestFixture, NonParallelizable]
public sealed class UT_RvrRoamingFoundations
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

    private sealed class Walker : GameBot
    {
        private Walker() : base((OfflineWorldBotRecord)null) { }
        public override eRealm Realm { get; set; }
        public override bool IsAlive => true;
    }

    private sealed class PvpKeeps : DefaultKeepManager
    {
        protected override bool IsPvpServer => true;
    }

    private static Walker RvrBot(eRealm realm, Guild guild = null)
    {
        var bot = (Walker)RuntimeHelpers.GetUninitializedObject(typeof(Walker));
        bot.Realm = realm;
        typeof(GameBot).GetField("_dummyClient", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new BotDummyClient());
        typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, true);
        typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot, new OfflineWorldBotRecord { ObjectiveKind = "RvR" });
        bot.Guild = guild;
        return bot;
    }

    private sealed class ConfiguredServer : GameServer
    {
        private static readonly IObjectDatabase Empty =
            DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private static readonly GameServerConfiguration Normal = new() { ServerType = EGameServerType.GST_Normal };
        protected override IObjectDatabase DataBaseImpl => Empty;
        public override GameServerConfiguration Configuration => Normal;
    }

    private static Guild MakeGuild(string id, string name)
    {
        // Guild's static constructor reads the server configuration once.
        GameServer previous = GameServer.Instance;
        GameServer.LoadTestDouble((ConfiguredServer)RuntimeHelpers.GetUninitializedObject(typeof(ConfiguredServer)));
        try { RuntimeHelpers.RunClassConstructor(typeof(Guild).TypeHandle); }
        finally { GameServer.LoadTestDouble(previous); }
        var guild = (Guild)RuntimeHelpers.GetUninitializedObject(typeof(Guild));
        typeof(Guild).GetField("m_DBguild", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(guild, new DbGuild { GuildID = id, GuildName = name });
        return guild;
    }

    private static GameKeep Keep(byte realm, byte baseLevel, Guild guild = null) =>
        new() { DBKeep = new DbKeep { Realm = realm, BaseLevel = baseLevel }, Guild = guild };

    // ---- Point 1: safe border hubs --------------------------------------

    [TestCase(eRealm.Albion, (ushort)1, 585085, 477504)]
    [TestCase(eRealm.Midgard, (ushort)100, 766235, 669173)]
    [TestCase(eRealm.Hibernia, (ushort)200, 333229, 419539)]
    public void BorderHubIsSafeInsideItsRadiusOnly(eRealm realm, ushort region, int x, int y)
    {
        Assert.That(AutonomousRvrStaging.TryGetBorderKeep(realm, out var hub), Is.True);
        Assert.That((hub.RegionId, (int)hub.Position.X, (int)hub.Position.Y), Is.EqualTo((region, x, y)),
            "Hub centres are the imported area rows of the same name");
        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.IsSafeBorderHub(region, x, y), Is.True, "centre");
            Assert.That(PvpCombatant.IsSafeBorderHub(region, x + 3_400, y), Is.True, "inside the 3,500 radius");
            Assert.That(PvpCombatant.IsSafeBorderHub(region, x - 2_400, y + 2_400), Is.True, "diagonal inside");
            Assert.That(PvpCombatant.IsSafeBorderHub(region, x + 3_600, y), Is.False, "just outside");
            Assert.That(PvpCombatant.IsSafeBorderHub(region, x, y - 10_000), Is.False, "the frontier beyond the gate");
            Assert.That(PvpCombatant.IsSafeBorderHub((ushort)(region == 1 ? 100 : 1), x, y), Is.False, "same numbers, other region");
        });
    }

    [TestCase((ushort)100, 648740, 583654, TestName = "Bledmeer Faste stays open PvP")]
    [TestCase((ushort)100, 596055, 581400, TestName = "Odin's Gate outside the portal keep area stays open PvP")]
    [TestCase((ushort)1, 653811, 616998, TestName = "Hadrian's Wall field stays open PvP")]
    [TestCase((ushort)200, 420000, 380000, TestName = "Emain Macha field stays open PvP")]
    public void FrontierItselfIsNotAHub(ushort region, int x, int y) =>
        Assert.That(PvpCombatant.IsSafeBorderHub(region, x, y), Is.False);

    [Test]
    public void HubProtectsAttackerAndDefenderButNotTheFieldOutside()
    {
        var rules = new PvPServerRules();
        var inside = new HubPlayer { Realm = eRealm.Albion, CurrentRegionID = 1, X = 585891, Y = 476614 };
        var outside = new HubPlayer { Realm = eRealm.Midgard, CurrentRegionID = 1, X = 585085 + 3_700, Y = 477504 };
        var field = new HubPlayer { Realm = eRealm.Hibernia, CurrentRegionID = 1, X = 585085 + 4_200, Y = 477504 };

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.IsSafeArea(inside), Is.True);
            Assert.That(rules.IsAllowedToAttack(inside, outside, true), Is.False, "no attacks out of the hub");
            Assert.That(rules.IsAllowedToAttack(outside, inside, true), Is.False, "no attacks into the hub from its edge");
            Assert.That(rules.IsAllowedToAttack(outside, field, true), Is.True, "the frontier outside stays open");
        });
    }

    private sealed class HubPlayer : GamePlayer
    {
        public HubPlayer() : base(null, null) { }
        public override bool IsAlive => true;
        public override byte Level { get; set; } = 50;
        public override eRealm Realm { get; set; }
        public override GameClient Client { get; } = new BotDummyClient();
        public override bool IsInvulnerableToAttack => false;
        public override ushort CurrentRegionID { get; set; }
        public override bool SafetyFlag { get; set; }
    }

    // ---- Point 2: planner uses the runtime door rule ---------------------

    [TestCase(eRealm.Albion)]
    [TestCase(eRealm.Midgard)]
    [TestCase(eRealm.Hibernia)]
    public void PortalKeepWithRealmZeroIsPassableForEveryRealm(eRealm realm)
    {
        var portal = Keep(0, 255);
        Assert.That(portal.IsPortalKeep, Is.True);
        Assert.That(AutonomousRvrTravel.CanPassKeep(RvrBot(realm), portal, new PvpKeeps()), Is.True);
    }

    [Test]
    public void OwnGuildKeepIsPassableAndHostileGuildKeepIsNot()
    {
        var mine = MakeGuild("guild-a", "North Road");
        var theirs = MakeGuild("guild-b", "South Road");
        var keeps = new PvpKeeps();
        var bot = RvrBot(eRealm.Midgard, mine);

        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrTravel.CanPassKeep(bot, Keep(2, 50, mine), keeps), Is.True, "own guild");
            Assert.That(AutonomousRvrTravel.CanPassKeep(bot, Keep(2, 50, theirs), keeps), Is.False, "hostile guild, same realm byte");
            Assert.That(AutonomousRvrTravel.CanPassKeep(RvrBot(eRealm.Midgard), Keep(2, 50, mine), keeps), Is.False, "guildless stranger");
            Assert.That(AutonomousRvrTravel.CanPassKeep(bot, Keep(1, 50, MakeGuild("wardens", PvpKeepCampaign.GarrisonName)), keeps),
                Is.False, "the Frontier Wardens garrison stays hostile");
        });
    }

    [Test]
    public void PlannerCountsPortalKeepDoorsAsFriendly()
    {
        var portal = Keep(0, 255);
        var enemy = Keep(2, 50, MakeGuild("guild-b", "South Road"));
        portal.Doors["portal"] = new GameKeepDoor { X = 100, Y = 0, Z = 0 };
        enemy.Doors["enemy"] = new GameKeepDoor { X = 900, Y = 0, Z = 0 };
        var bot = RvrBot(eRealm.Hibernia);
        var keeps = new PvpKeeps();

        var doors = AutonomousKeepApproachNavigation.FriendlyDoors(new AbstractGameKeep[] { portal, enemy },
            keep => AutonomousRvrTravel.CanPassKeep(bot, keep, keeps));
        Assert.That(doors.Select(door => door.X), Is.EqualTo(new[] { 100f }));
    }

    [Test]
    public void WithoutAConfiguredServerTheDoorRuleFallsBackToRealmIdentity()
    {
        var keep = Keep(2, 50);
        Assert.That(AutonomousRvrTravel.CanPassKeep(RvrBot(eRealm.Midgard), keep), Is.True);
        Assert.That(AutonomousRvrTravel.CanPassKeep(RvrBot(eRealm.Albion), keep), Is.False);
    }

    // ---- Point 3: give up and close idle sieges --------------------------

    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(3, true)]
    [TestCase(4, true)]
    public void KeepRouteIsAbandonedAfterThreeConsecutiveFailures(int failures, bool abandon) =>
        Assert.That(AutonomousWorldBotController.ShouldAbandonKeepRoute(failures), Is.EqualTo(abandon));

    [Test]
    public void AutomaticSiegeWithNobodyAtTheKeepClosesAfterFifteenMinutes()
    {
        const long start = 50_000_000;
        var target = new AutonomousRvrEventLayer.LiveObjective("rvr-keep-9075", "Test Faste",
            AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Midgard, 100, 0, 0, 0, false, 0, 0, 4, 2);
        Assert.That(AutonomousRvrEventLayer.ForceStart(target, eRealm.Hibernia, start, out string reason), Is.True, reason);

        Sweep(start + AutonomousRvrEventLayer.IdleBattleMilliseconds - 1);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.True, "not idle yet");

        AutonomousRvrEventLayer.ReportBattleActivity(target.Id, start + 10 * 60_000);
        Sweep(start + 24 * 60_000);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.True, "a bot reached the approach at ten minutes");

        Sweep(start + 25 * 60_000);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.False, "fifteen idle minutes close it");
        Assert.That(AutonomousRvrEventLayer.CooldownRemaining(target.Id, start + 25 * 60_000), Is.GreaterThan(0));
    }

    [TestCase(true, false, false, 15 * 60_000L, true)]
    [TestCase(true, false, false, 14 * 60_000L, false)]
    [TestCase(false, false, false, 60 * 60_000L, false)]
    [TestCase(true, true, false, 60 * 60_000L, false)]
    [TestCase(true, false, true, 60 * 60_000L, false)]
    public void OnlyStartedAutomaticSiegesCountAsIdle(bool started, bool defense, bool player, long idle, bool expected) =>
        Assert.That(AutonomousRvrEventLayer.IsIdleBattle(started, defense, player, 1_000, 1_000 + idle), Is.EqualTo(expected));

    [Test]
    public void LeavingASiegeReleasesTheForceFromItsCommitment()
    {
        const long start = 60_000_000;
        var target = new AutonomousRvrEventLayer.LiveObjective("rvr-keep-9076", "Route Faste",
            AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Midgard, 100, 0, 0, 0, false, 0, 0, 4, 2);
        Assert.That(AutonomousRvrEventLayer.ForceStart(target, eRealm.Hibernia, start, out _), Is.True);
        var events = (System.Collections.IDictionary)typeof(AutonomousRvrEventLayer)
            .GetField("Events", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        object active = events[target.Id];
        ((System.Collections.Generic.Dictionary<string, int>)active.GetType().GetField("Attackers").GetValue(active)).Add("stuck-force", 3);
        Assert.That(AutonomousRvrEventLayer.KeepPlan("stuck-force", eRealm.Hibernia, start + 1), Is.Not.Null);

        AutonomousRvrEventLayer.RemoveForce("stuck-force");
        Assert.That(AutonomousRvrEventLayer.KeepPlan("stuck-force", eRealm.Hibernia, start + 2), Is.Null);
        Assert.That(AutonomousRvrEventLayer.IsForceCommitted("stuck-force", start + 2), Is.False);
    }

    private static System.Collections.Generic.Dictionary<string, int> Bucket(string targetId, string name)
    {
        var events = (System.Collections.IDictionary)typeof(AutonomousRvrEventLayer)
            .GetField("Events", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        object active = events[targetId];
        return (System.Collections.Generic.Dictionary<string, int>)active.GetType().GetField(name).GetValue(active);
    }

    private static AutonomousRvrEventLayer.LiveObjective OpenSiege(string id, long start)
    {
        var target = new AutonomousRvrEventLayer.LiveObjective(id, "March Faste",
            AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Midgard, 100, 100_000, 100_000, 0, false, 0, 0, 4, 2);
        Assert.That(AutonomousRvrEventLayer.ForceStart(target, eRealm.Hibernia, start, out string reason), Is.True, reason);
        return target;
    }

    [Test]
    public void AMarchingArmyKeepsTheSiegeOpenPastFifteenMinutes()
    {
        const long start = 80_000_000;
        var target = OpenSiege("rvr-keep-9077", start);
        Bucket(target.Id, "Attackers").Add("marching", 8);
        // Walk from the far border: one port, then about 1,000 units closer every two minutes.
        AutonomousRvrEventLayer.ReportMarch(target.Id, "marching", 1, 1, new(585085, 477504, 0), false, start + 60_000);
        AutonomousRvrEventLayer.ReportMarch(target.Id, "marching", 1, 100, new(100_000, 140_000, 0), false, start + 4 * 60_000);
        for (int minute = 6; minute <= 30; minute += 2)
        {
            AutonomousRvrEventLayer.ReportMarch(target.Id, "marching", 1, 100,
                new(100_000, 140_000 - (minute - 4) * 500, 0), false, start + minute * 60_000L);
            Sweep(start + minute * 60_000L);
        }
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.True, "still marching at 30 minutes");

        // The march stalls (route back-off): nothing closer, no port.
        for (int minute = 32; minute <= 44; minute += 2)
        {
            AutonomousRvrEventLayer.ReportMarch(target.Id, "marching", 1, 100, new(100_000, 127_000, 0), false, start + minute * 60_000L);
            Sweep(start + minute * 60_000L);
        }
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.True, "fourteen idle minutes");
        Sweep(start + 45 * 60_000L);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.False, "fifteen idle minutes close it");
    }

    [Test]
    public void AStuckForceOrDefendersAloneDoNotKeepTheSiegeOpen()
    {
        const long start = 90_000_000;
        var target = OpenSiege("rvr-keep-9078", start);
        Bucket(target.Id, "Attackers").Add("stuck", 3);
        Bucket(target.Id, "Defenders").Add("holding", 8);
        AutonomousRvrEventLayer.ReportMarch(target.Id, "stuck", 7, 100, new(596055, 581400, 0), false, start + 60_000);
        for (int minute = 2; minute <= 16; minute++)
        {
            AutonomousRvrEventLayer.ReportMarch(target.Id, "stuck", 7, 100, new(596055 + minute * 10, 581400, 0), false, start + minute * 60_000L);
            AutonomousRvrEventLayer.ReportMarch(target.Id, "holding", 20, 100, new(100_000 + minute * 600, 100_000, 0), false, start + minute * 60_000L);
            AutonomousRvrEventLayer.ReportBattleActivity(target.Id, start + minute * 60_000L, "holding");
        }
        Sweep(start + 16 * 60_000L);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.False);
    }

    [Test]
    public void APacingBotAtTheHubOrARepeatedSegmentDoesNotKeepTheSiegeOpen()
    {
        const long start = 95_000_000;
        var target = OpenSiege("rvr-keep-9080", start);
        Bucket(target.Id, "Attackers").Add("pacer", 1);
        Bucket(target.Id, "Attackers").Add("replanner", 1);
        // Pacer: stays at Castle Sauvage (region 1, keep in region 100), walking
        // 1,000 units back and forth every minute, never in combat.
        AutonomousRvrEventLayer.ReportMarch(target.Id, "pacer", 30, 1, new(585085, 477504, 0), false, start);
        // Replanner: reaches the same road segment again and again after replans.
        AutonomousRvrEventLayer.ReportMarch(target.Id, "replanner", 31, 100, new(100_000, 120_000, 0), false, start);
        for (int minute = 1; minute <= 19; minute++)
        {
            int x = minute % 2 == 0 ? 585085 : 586085;
            AutonomousRvrEventLayer.ReportMarch(target.Id, "pacer", 30, 1, new(x, 477504, 0), false, start + minute * 60_000L);
            AutonomousRvrEventLayer.ReportMarch(target.Id, "replanner", 31, 100,
                new(100_000, minute % 2 == 0 ? 120_000 : 121_000, 0), false, start + minute * 60_000L);
            if (minute == 17)
            {
                Sweep(start + minute * 60_000L);
                Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.True,
                    "three pacing credits (minutes 1-3) carry it to minute 17");
            }
        }
        Sweep(start + 18 * 60_000L);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.False,
            "no further credit from pacing or the repeated segment: closed 15 minutes after minute 3");
    }

    [Test]
    public void AbandonmentBindsTheWholeWarbandButNotOtherForces()
    {
        const long start = 100_000_000;
        var target = OpenSiege("rvr-keep-9079", start);
        var warband = new AutonomousRvrEventLayer.Force("warband-2", eRealm.Hibernia, 2, 50, 1);
        Bucket(target.Id, "Attackers").Add(warband.GroupId, 2);
        Assert.That(AutonomousRvrEventLayer.KeepPlan(warband.GroupId, eRealm.Hibernia, start + 1)?.TargetId, Is.EqualTo(target.Id));

        // Member two (not the leader) hits three route failures first.
        AutonomousRvrEventLayer.AbandonTarget(warband.GroupId, target.Id, start + 2);

        // Member one (the leader) re-plans: no committed plan, no rejoin, no reopen.
        Assert.That(AutonomousRvrEventLayer.KeepPlan(warband.GroupId, eRealm.Hibernia, start + 3), Is.Null);
        Assert.That(AutonomousRvrEventLayer.IsAbandoned(warband.GroupId, target.Id, start + 3), Is.True);
        for (int roll = 0; roll < 10; roll++)
            Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(warband, [target], start + 4 + roll, roll / 10d)?.TargetId,
                Is.Not.EqualTo(target.Id));
        Assert.That(Bucket(target.Id, "Attackers").ContainsKey(warband.GroupId), Is.False);

        var other = warband with { GroupId = "other-warband" };
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(other, [target], start + 20, 0)?.TargetId, Is.EqualTo(target.Id),
            "another warband may still answer the siege");

        Assert.That(AutonomousRvrEventLayer.IsAbandoned(warband.GroupId, target.Id,
            start + 2 + AutonomousRvrEventLayer.AbandonedTargetMilliseconds), Is.False, "twenty minutes later it may try again");
    }

    [Test]
    public void BoardingPassLeavesAfterSixtySecondsWithoutTheStraggler()
    {
        const long t0 = 110_000_000;
        string force = "board-loop-" + Guid.NewGuid();
        string[] party = ["tank", "healer", "straggler"];
        bool Ready(string member) => member != "straggler";
        bool Incoming(string member) => member == "straggler";

        Assert.That(AutonomousFrontierTransport.SelectBoarders(party, Ready, Incoming, force, 100, t0, out var ready, out int incoming),
            Is.Empty, "first pass waits");
        Assert.That((ready.Length, incoming), Is.EqualTo((2, 1)));
        Assert.That(AutonomousFrontierTransport.SelectBoarders(party, Ready, Incoming, force, 100, t0 + 30_000, out _, out _), Is.Empty);
        Assert.That(AutonomousFrontierTransport.SelectBoarders(party, Ready, Incoming, force, 100, t0 + 60_000, out _, out incoming),
            Is.EqualTo(new[] { "tank", "healer" }), "after a minute the two at the porter leave");
        Assert.That(incoming, Is.EqualTo(1), "logged as left_behind=1");

        string next = force + "-b";
        Assert.That(AutonomousFrontierTransport.SelectBoarders(party, Ready, Incoming, next, 100, t0, out _, out _), Is.Empty);
        Assert.That(AutonomousFrontierTransport.SelectBoarders(party, _ => true, _ => false, next, 100, t0 + 20_000, out _, out _),
            Is.EqualTo(party), "the straggler arrives after twenty seconds: all three board");
    }

    private static void Sweep(long now) => AutonomousRvrEventLayer.TryConsumeRelease("sweep-only", now, out _);

    // ---- Point 4: warbands board together -------------------------------

    [Test]
    public void GroupOfThreeWithOneMemberTwentySecondsAwayBoardsAsThree()
    {
        const long t0 = 70_000_000;
        string force = "board-test-" + Guid.NewGuid();
        // Two at the porter, one running in about 20 s (~4,000 units) away.
        Assert.That(AutonomousFrontierTransport.IsIncoming(true, true, false, 4_000), Is.True);
        long muster = AutonomousFrontierTransport.MusterStart(force, 100, t0);
        Assert.That(AutonomousFrontierTransport.DecideBoarding(2, 1, muster, t0),
            Is.EqualTo(AutonomousFrontierTransport.BoardingDecision.Wait));
        Assert.That(AutonomousFrontierTransport.MusterStart(force, 100, t0 + 20_000), Is.EqualTo(t0), "one muster clock per warband");
        // Twenty seconds later the straggler is ready too: all three board.
        Assert.That(AutonomousFrontierTransport.DecideBoarding(3, 0, muster, t0 + 20_000),
            Is.EqualTo(AutonomousFrontierTransport.BoardingDecision.Board));
        AutonomousFrontierTransport.EndMuster(force, 100);
        Assert.That(AutonomousFrontierTransport.MusterStart(force, 100, t0 + 30_000), Is.EqualTo(t0 + 30_000));
        AutonomousFrontierTransport.EndMuster(force, 100);
    }

    [Test]
    public void AfterAMinuteThoseAtThePorterLeaveWithoutTheStraggler()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousFrontierTransport.DecideBoarding(2, 1, 0, 59_999),
                Is.EqualTo(AutonomousFrontierTransport.BoardingDecision.Wait));
            Assert.That(AutonomousFrontierTransport.DecideBoarding(2, 1, 0, 60_000),
                Is.EqualTo(AutonomousFrontierTransport.BoardingDecision.Board));
            Assert.That(AutonomousFrontierTransport.DecideBoarding(1, 0, 0, 0),
                Is.EqualTo(AutonomousFrontierTransport.BoardingDecision.Board), "a solo roamer never waits");
            Assert.That(AutonomousFrontierTransport.DecideBoarding(0, 2, 0, 600_000),
                Is.EqualTo(AutonomousFrontierTransport.BoardingDecision.Wait), "nobody ready, nobody leaves");
        });
    }

    [TestCase(true, true, false, 20_000d, false, TestName = "Too far away to wait for")]
    [TestCase(true, false, false, 100d, false, TestName = "Other region is not waited for")]
    [TestCase(false, true, false, 100d, false, TestName = "Dead member is not waited for")]
    [TestCase(true, true, true, 100d, false, TestName = "Member already across is not waited for")]
    [TestCase(true, true, false, 6_000d, true, TestName = "Member within the muster radius is waited for")]
    public void OnlyNearbyLivingMembersHoldTheDeparture(bool alive, bool sameRegion, bool across, double distance, bool expected) =>
        Assert.That(AutonomousFrontierTransport.IsIncoming(alive, sameRegion, across, distance), Is.EqualTo(expected));

    // ---- Point 5: release behaviour --------------------------------------

    [TestCase(eRealm.Albion, (ushort)1)]
    [TestCase(eRealm.Midgard, (ushort)100)]
    [TestCase(eRealm.Hibernia, (ushort)200)]
    public void FrontierPvpDeathReleasesInsideTheOwnHub(eRealm realm, ushort hubRegion)
    {
        Assert.That(AutonomousRvrStaging.TryFrontierPvpRelease(realm, true, true, true, out ushort region, out Point3D point), Is.True);
        Assert.That(region, Is.EqualTo(hubRegion));
        Assert.That(PvpCombatant.IsSafeBorderHub(region, point.X, point.Y), Is.True);
    }

    [Test]
    public void FrontierPvpReleasePrefersTheHubBindstone()
    {
        try
        {
            BotReleaseBindPoints.Replace(1, new[]
            {
                new DbBindPoint { X = 585891, Y = 476614, Z = 2600, Realm = 0 }, // Castle Sauvage hub bind
                new DbBindPoint { X = 565893, Y = 491838, Z = 1873, Realm = 1 }, // outside the hub
            });
            Assert.That(AutonomousRvrStaging.TryFrontierPvpRelease(eRealm.Albion, true, true, true, out _, out Point3D point), Is.True);
            Assert.That((point.X, point.Y), Is.EqualTo((585891, 476614)));
        }
        finally { BotReleaseBindPoints.Replace(1, Array.Empty<DbBindPoint>()); }
    }

    [TestCase(false, true, true, TestName = "PvE bots keep their nearest bind")]
    [TestCase(true, false, true, TestName = "Monster deaths keep their nearest bind")]
    [TestCase(true, true, false, TestName = "Deaths outside the frontier keep their nearest bind")]
    public void OnlyFrontierPvpDeathsOfRvrBotsReleaseAtTheHub(bool rvr, bool pvp, bool frontier) =>
        Assert.That(AutonomousRvrStaging.TryFrontierPvpRelease(eRealm.Midgard, rvr, pvp, frontier, out _, out _), Is.False);

    [Test]
    public void ImmuneBotDoesNotOpenAFight()
    {
        var hunter = RvrBot(eRealm.Albion);
        typeof(GameBot).GetField("_pvpInvulnerabilityTick", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(hunter, long.MaxValue);
        Assert.That(hunter.IsInvulnerableToAttack, Is.True);
        var prey = new HubPlayer { Realm = eRealm.Midgard, CurrentRegionID = 0 };
        Assert.That(AutonomousPvpOpportunityPolicy.Select(hunter, new GameLiving[] { prey }, (_, _) => true), Is.Null);
        Assert.That(new PvPServerRules().IsAllowedToAttack(hunter, prey, true), Is.False);
    }
}
