// Bug 178 (docs/BUGS.md): in the RvR world (Realm-1) the siege army lost members to two fork-only re-task paths that
// upstream shadowofze/OfflineDAoC 0.38c does not have: the repeated-loss memory abandoned the keep target of a solo
// siege force, and the "three PvP deaths" wall ended solo RvR tours and pulled walled bots out of their siege parties.
// Both are fork waves now (AutonomousRvrWaves): off in the RvR world, unchanged in Camlann.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture, NonParallelizable]
public sealed class UT_Bug178ArmyAssignment
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
    private long _savedTick;

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

    private sealed class ArmyBot : GameBot
    {
        private ArmyBot() : base((OfflineWorldBotRecord)null) { }
        public override bool IsAlive => true;
        public override eRealm Realm { get; set; }
        public override ushort CurrentRegionID { get; set; }
        public override void StopMoving() { }
        public override void StopMovingOnPath() { }
    }

    private static ArmyBot SoloSiegeBot(string force)
    {
        var bot = (ArmyBot)RuntimeHelpers.GetUninitializedObject(typeof(ArmyBot));
        bot.Realm = eRealm.Albion;
        bot.CurrentRegionID = 100;
        bot.DatabaseID = 3850;
        typeof(GameBot).GetField("<IsAutonomousWorldBot>k__BackingField", Instance).SetValue(bot, true);
        typeof(GameLiving).GetField("<TempProperties>k__BackingField", Instance).SetValue(bot, new PropertyCollection());
        typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot, new OfflineWorldBotRecord { ObjectiveKind = "RvR" });
        bot.TempProperties.SetProperty("RvrEventForce", force);
        return bot;
    }

    private static void Died(GameBot bot, long tick, int x, int y) =>
        typeof(GameBot).GetProperty(nameof(GameBot.LastAutonomousDeath)).SetValue(bot,
            new AutonomousDeathSnapshot(tick, 100, "Odin's Gate", x, y, 5500, "Briainorna", 50, "world_bot", "Ranger",
                eRealm.Hibernia, false, false, true, true, "world_bot", "Briainorna", "world_bot"));

    private static void SetDestination(AutonomousWorldBotController controller, string keepId)
    {
        Type destinationType = typeof(AutonomousWorldBotController).GetNestedType("CampDestination", BindingFlags.NonPublic);
        object destination = Activator.CreateInstance(destinationType,
            keepId, "keep Bledmeer Faste", "Odin's Gate", (ushort)100, 704_000, 656_000, 5_000, 1, false, true, null, 0);
        typeof(AutonomousWorldBotController).GetField("_rvrDestination", Instance).SetValue(controller, destination);
    }

    private static object Destination(AutonomousWorldBotController controller) =>
        typeof(AutonomousWorldBotController).GetField("_rvrDestination", Instance).GetValue(controller);

    /// <summary>
    /// Live 2026-10-25 19:14-19:21 (0.345.0, Realm-1): Bereralwin, Wilenhelm and Osoenred, solo forces of the Albion army
    /// on rvr-keep-79, died twice on the road near 649,000 / 623,000 and logged RVR_KEEP_ROUTE_ABANDONED "Repeated PvP
    /// losses near the same place": their forces left the siege for twenty minutes. Upstream 0.38c has no such rule.
    /// </summary>
    [TestCase(WorldModelKind.Realm, ExpectedResult = false, TestName = "RvR: two roadside deaths keep the siege target and the army assignment (upstream)")]
    [TestCase(WorldModelKind.Camlann, ExpectedResult = true, TestName = "Camlann: two roadside deaths still abandon the keep target (fork rule)")]
    public bool RepeatedRoadsideDeathsAbandonTheSiegeOnlyInCamlann(WorldModelKind world)
    {
        using var _ = WorldMode.OverrideForTests(world);
        SetTick(10 * 60 * 60_000L);
        string id = "rvr-keep-" + Guid.NewGuid().ToString("N");
        var keep = new AutonomousRvrEventLayer.LiveObjective(id, "Bledmeer Faste", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Midgard, 100, 704_000, 656_000, 5_000, false, 0, 0, 5, 2);
        var force = new AutonomousRvrEventLayer.Force("rvr-3850", eRealm.Albion, 8, 50, 1);
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(force, [keep], Now, 0)?.TargetId, Is.EqualTo(id));

        ArmyBot bot = SoloSiegeBot(force.GroupId);
        var controller = new AutonomousWorldBotController();
        SetDestination(controller, id);
        MethodInfo observe = typeof(AutonomousWorldBotController).GetMethod("ObserveRepeatedPvpLoss", Instance);

        // Two PvP deaths 500 units apart within two minutes, 60,000 units short of the keep.
        Died(bot, Now, 649_000, 623_000);
        observe.Invoke(controller, [bot]);
        SetTick(Now + 120_000);
        Died(bot, Now, 649_500, 623_000);
        observe.Invoke(controller, [bot]);

        bool abandoned = AutonomousRvrEventLayer.IsAbandoned(force.GroupId, id, Now);
        Assert.That(Destination(controller) == null, Is.EqualTo(abandoned), "the keep destination goes with the siege");
        if (world == WorldModelKind.Realm)
            Assert.That(AutonomousRvrEventLayer.KeepPlan(force.GroupId, eRealm.Albion, Now)?.TargetId, Is.EqualTo(id),
                "the solo force keeps its siege plan");
        return abandoned;
    }

    /// <summary>
    /// Live 2026-10-25 19:15:09: siege party 155 (Reneralbert, Serelorelle, Gisalbeth) formed at the march and ended four
    /// seconds later ("A member left"): two of its three members were walled ("three PvP deaths in twenty minutes") and the
    /// coordinator removed walled bots from RvR parties. Upstream has no wall.
    /// </summary>
    [TestCase(WorldModelKind.Realm, ExpectedResult = false, TestName = "RvR: no PvP wall for a world bot outside the battlegrounds (upstream)")]
    [TestCase(WorldModelKind.Camlann, ExpectedResult = true, TestName = "Camlann: the PvP wall applies (fork rule)")]
    public bool ThePvpWallFollowsTheWorld(WorldModelKind world)
    {
        using var _ = WorldMode.OverrideForTests(world);
        return AutonomousActivityScheduler.PvpWallApplies(SoloSiegeBot("rvr-3850"));
    }

    /// <summary>The two places that act on the wall ask the world rule before they end a tour or drop a party member.</summary>
    [TestCase("GameBot.cs", "if (_lastDeathWasPvp && !inRvrGroup && AutonomousActivityScheduler.PvpWallApplies(this) &&")]
    [TestCase("autonomous/AutonomousBotGroupCoordinator.cs", "!currentSession.OpenBattleground && AutonomousActivityScheduler.PvpWallApplies(bot) &&")]
    public void TheWallsReTaskPathsAskTheWorldRule(string file, string guard)
    {
        string text = File.ReadAllText(FindRepoFile(Path.Combine("source", "server", "GameServer", "bots", file)));
        Assert.That(text, Does.Contain(guard));
    }

    /// <summary>
    /// Live 2026-10-25: the defense musters of rvr-keep-79 and rvr-keep-101 opened after their battles had started and
    /// reported mustered=0, rvr-keep-52's defenders joined before the march and 14 mustered. Upstream 0.38c
    /// (RealmRvrEventLayer verbatim): once an automatic siege's battle is on, its defenders get no muster order (only a
    /// player-triggered defense does); they converge on the keep directly. Pinned here so the port keeps upstream's rule.
    /// </summary>
    [TestCase(false, ExpectedResult = true, TestName = "RvR: a defender that answers before the battle gets the defense muster order (upstream)")]
    [TestCase(true, ExpectedResult = false, TestName = "RvR: a defender drafted after an automatic battle opened gets no muster order and goes to the keep (upstream)")]
    public bool AutomaticSiegeDefendersMusterOnlyBeforeTheBattle(bool battleOpened)
    {
        using var _ = WorldMode.OverrideForTests(WorldModelKind.Realm);
        RealmRvrEventLayer.SiegeScheduling = true; // production default (live 0.345.0); TearDown restores it
        long uptime = 10 * 60 * 60_000L;
        SetTick(uptime);
        string id = "rvr-keep-" + Guid.NewGuid().ToString("N");
        var keep = new AutonomousRvrEventLayer.LiveObjective(id, "Defended keep", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Hibernia, 200, 100, 100, 0, false, 0, 0, 5, 2);
        var attacker = new AutonomousRvrEventLayer.Force(id + "-attack", eRealm.Albion, 8, 50, 1);
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(attacker, [keep], Now, 0)?.TargetId, Is.EqualTo(id));
        if (battleOpened)
        {
            // Muster deadline, one extension, march, the leader's arrival opens the battle.
            SetTick(uptime + RealmRvrEventLayer.HomeMusterMilliseconds + 1_000);
            RealmRvrEventLayer.GetRallyOrder(attacker.GroupId, eRealm.Albion, Now);
            SetTick(Now + RealmRvrEventLayer.MusterExtensionMilliseconds + 1_000);
            Assert.That(RealmRvrEventLayer.GetRallyOrder(attacker.GroupId, eRealm.Albion, Now)?.HomeMuster, Is.False, "marching");
            RealmRvrEventLayer.ReportArmyArrived(id, Now);
            Assert.That(RealmRvrEventLayer.IsBattleForce(attacker.GroupId, Now), Is.True, "the battle opened");
        }
        var defender = new AutonomousRvrEventLayer.Force(id + "-defend", eRealm.Hibernia, 8, 50, 1);
        var hit = keep with { UnderAttack = true };
        Assert.That(RealmRvrEventLayer.ChooseOrJoin(defender, [hit], Now, 0)?.Intent,
            Is.EqualTo(AutonomousRvrEventLayer.Intent.DefendEvent), "the defender answers the attack");
        return RealmRvrEventLayer.GetRallyOrder(defender.GroupId, eRealm.Hibernia, Now)?.HomeMuster == true;
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
