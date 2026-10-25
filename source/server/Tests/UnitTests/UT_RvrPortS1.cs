using System;
using System.Linq;
using System.Numerics;
using DOL.GS.Keeps;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.GS.Tests;

/// <summary>
/// Port session S1 (docs/RVR_PORT_MAP.md): the AutonomousRvrEventLayer facade runs the upstream layer
/// (RealmRvrEventLayer, shadowofze/OfflineDAoC 0.38c) in the RvR world and the fork's layer (CamlannRvrEventLayer) in
/// Camlann; the upstream rules (level-50 gate, one attack per realm, muster, march thresholds, retake, repel) hold
/// through the facade; the RvR world's frontier keeps start with their realm and upstream level.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_RvrPortS1
{
    private const long Minute = 60_000;
    // Far from the live game clock, so Expire's live-clock throttle never applies; idle long enough for a sure roll.
    private const long T0 = 10_000_000_000;

    [SetUp]
    public void SetUp() => RvrEventTestState.Clear();

    [TearDown]
    public void TearDown() => RvrEventTestState.Clear();

    private static AutonomousRvrEventLayer.LiveObjective Keep(string id, eRealm owner = eRealm.Hibernia, bool relic = false) =>
        new(id, "keep " + id, relic ? AutonomousRvrEventLayer.Intent.AssaultRelicKeep : AutonomousRvrEventLayer.Intent.AssaultKeep,
            owner, 200, 100_000, 100_000, 0, relic, 0, 0, 5, 2);

    private static AutonomousRvrEventLayer.Force Army(string id, eRealm realm = eRealm.Albion, int members = 8,
        int average = 50, int minimum = 50) =>
        new(id, realm, members, average, 1, MinimumMemberLevel: minimum);

    [Test]
    public void TheRvrWorldRunsTheUpstreamLayerAndCamlannTheForkLayer()
    {
        var keep = Keep("port-s1-keep");
        using (WorldMode.OverrideForTests(WorldModelKind.Realm))
        {
            var plan = AutonomousRvrEventLayer.ChooseOrJoin(Army("alb"), [keep], T0, 0);
            Assert.That(plan?.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.AssaultKeep), "a level-50 warband of four or more opens the siege");
            var order = AutonomousRvrEventLayer.GetRallyOrder("alb", eRealm.Albion, T0);
            Assert.Multiple(() =>
            {
                Assert.That(order?.HomeMuster, Is.True, "the army musters at its portal outpost first");
                Assert.That(order?.Side, Is.EqualTo(AutonomousRvrEventLayer.RallySide.Attacker));
                Assert.That(RealmRvrEventLayer.IsTargetActive(keep.Id, T0), Is.True);
                Assert.That(CamlannRvrEventLayer.IsTargetActive(keep.Id, T0), Is.False, "the fork layer holds no RvR world event");
                Assert.That(AutonomousRvrEventLayer.Snapshot().Single().CapPerRealm, Is.EqualTo(RealmEventPolicy.AttackerCap(false)));
            });
        }
        using (WorldMode.OverrideForTests(WorldModelKind.Camlann))
            Assert.That(AutonomousRvrEventLayer.IsTargetActive(keep.Id, T0), Is.False, "Camlann reads only the fork layer");
    }

    [TestCase(49, 50, TestName = "An average below 50 opens no siege")]
    [TestCase(50, 49, TestName = "A member below 50 opens no siege")]
    public void UpstreamOpensSiegesOnlyForLevelFifty(int average, int minimum)
    {
        using var _ = WorldMode.OverrideForTests(WorldModelKind.Realm);
        var plan = AutonomousRvrEventLayer.ChooseOrJoin(Army("alb-low", average: average, minimum: minimum),
            [Keep("port-s1-low")], T0, 0);
        Assert.That(plan?.Intent, Is.Not.EqualTo(AutonomousRvrEventLayer.Intent.AssaultKeep));
        Assert.That(AutonomousRvrEventLayer.IsTargetActive("port-s1-low", T0), Is.False);
    }

    [Test]
    public void ARealmRunsOneAttackAtATime()
    {
        using var _ = WorldMode.OverrideForTests(WorldModelKind.Realm);
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(Army("alb-1"), [Keep("port-s1-a")], T0, 0)?.TargetId, Is.EqualTo("port-s1-a"));
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(Army("alb-2"), [Keep("port-s1-b")], T0 + 2 * Minute, 0), Is.Null,
            "a second Albion warband keeps roaming instead of opening a second Albion siege");
        Assert.That(AutonomousRvrEventLayer.IsTargetActive("port-s1-b", T0 + 2 * Minute), Is.False);
    }

    [TestCase(false, 128, ExpectedResult = 108, TestName = "A keep army marches at 108")]
    [TestCase(true, 240, ExpectedResult = 170, TestName = "A relic army marches at 170")]
    [TestCase(false, 64, ExpectedResult = 54, TestName = "A smaller keep army marches at 85 % of the assigned")]
    [TestCase(true, 100, ExpectedResult = 85, TestName = "A smaller relic army marches at 85 % of the assigned")]
    [TestCase(false, 4, ExpectedResult = 8, TestName = "Never fewer than eight")]
    public int MarchThresholdsAreUpstreams(bool relic, int assigned) => AutonomousRvrEventLayer.MarchThreshold(relic, assigned);

    [Test]
    public void ArrivalQuorumAndDefenseWaveAreUpstreams()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrEventLayer.ArrivalQuorum(108), Is.EqualTo(64), "60 % of the army that marched opens the siege");
            Assert.That(AutonomousRvrEventLayer.ArrivalQuorum(5), Is.EqualTo(8));
            Assert.That(AutonomousRvrEventLayer.DefenseWaveSize(240), Is.EqualTo(192), "a player-called defense marches at 80 %");
            Assert.That(RealmRvrEventLayer.HomeMusterMilliseconds, Is.EqualTo(12 * Minute));
            Assert.That(RealmRvrEventLayer.MusterExtensionMilliseconds, Is.EqualTo(5 * Minute));
            Assert.That(RealmRvrEventLayer.RelicMusterMilliseconds, Is.EqualTo(25 * Minute));
            Assert.That(RealmRvrEventLayer.RepelMilliseconds, Is.EqualTo(10 * Minute));
            Assert.That(RealmEventPolicy.DefenderCap(false), Is.EqualTo(64));
            Assert.That(RealmEventPolicy.ThirdRealmCap(true), Is.EqualTo(120));
        });
    }

    [TestCase(true, false, false, false, 0L, ExpectedResult = false, TestName = "Defenders wait for the attack to land")]
    [TestCase(true, true, false, false, 0L, ExpectedResult = true, TestName = "Defenders answer an observed attack")]
    [TestCase(false, true, true, true, 4 * Minute, ExpectedResult = false, TestName = "The third realm waits five minutes into the battle")]
    [TestCase(false, true, true, true, 5 * Minute, ExpectedResult = true, TestName = "The third realm joins after five minutes when it intervenes")]
    [TestCase(false, true, true, false, 60 * Minute, ExpectedResult = false, TestName = "A third realm that stays out never joins")]
    public bool ResponsesFollowTheUpstreamOrder(bool defender, bool observed, bool battle, bool intervenes, long since) =>
        RealmEventPolicy.CanRespond(defender, observed, battle, intervenes, since);

    [Test]
    public void ARealmThatLostItsKeepToAPlayerComesBackOnceToRetakeIt()
    {
        using var _ = WorldMode.OverrideForTests(WorldModelKind.Realm);
        var keep = Keep("port-s1-retake", eRealm.Midgard);
        Assert.That(AutonomousRvrEventLayer.BeginDefenseResponse(keep, eRealm.Albion, "offline", T0,
            AutonomousRvrEventLayer.DefenseScale.Massive), Is.True);
        AutonomousRvrEventLayer.EndTarget(keep.Id, T0 + Minute, eRealm.Albion);
        var retake = AutonomousRvrEventLayer.Snapshot().SingleOrDefault(notice => notice.TargetId == keep.Id);
        Assert.That(retake, Is.Not.Null, "the losing realm rallies to retake the keep");
        Assert.Multiple(() =>
        {
            Assert.That(retake.Attacker, Is.EqualTo(eRealm.Midgard));
            Assert.That(retake.Defender, Is.EqualTo(eRealm.Albion));
            Assert.That(retake.CapPerRealm, Is.EqualTo(240), "sized like the defense that failed");
        });
        AutonomousRvrEventLayer.EndTarget(keep.Id, T0 + 2 * Minute, eRealm.Midgard);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(keep.Id, T0 + 2 * Minute), Is.False, "a retake never repeats");
    }

    [Test]
    public void ASiegeWhoseArmyLeftTheKeepForTenMinutesIsRepelled()
    {
        using var world = WorldMode.OverrideForTests(WorldModelKind.Realm);
        RealmRvrEventLayer.StagedAssault = false;
        var keep = Keep("port-s1-repel");
        Assert.That(AutonomousRvrEventLayer.ForceStart(keep, eRealm.Albion, T0, out _), Is.True);
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(Army("alb-repel"), [keep], T0 + 1, 0)?.TargetId, Is.EqualTo(keep.Id));
        AutonomousRvrEventLayer.ReportAttendance(keep.Id, "alb-repel", eRealm.Albion, 1, true, T0 + 1_000, null, new Vector3(100_000, 100_000, 0));
        Assert.That(AutonomousRvrEventLayer.TryConsumeRelease("alb-repel", T0 + 2_000, out _), Is.False, "the army is at the keep");
        long gone = T0 + 2_000 + RealmRvrEventLayer.RepelMilliseconds + 1;
        Assert.That(AutonomousRvrEventLayer.TryConsumeRelease("alb-repel", gone, out string reason), Is.True);
        Assert.That(reason, Does.StartWith("Siege defended"));
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(keep.Id, gone), Is.False);
    }

    [Test]
    public void EachWorldAnswersTheOtherLayersCallsWithANeutralDefault()
    {
        using (WorldMode.OverrideForTests(WorldModelKind.Realm))
        {
            AutonomousRvrEventLayer.AbandonTarget("force", "port-s1-x", T0);
            AutonomousRvrEventLayer.NoteKeepRouteFailure("port-s1-x", T0);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRvrEventLayer.MusterPhaseOf("force"), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.None));
                Assert.That(AutonomousRvrEventLayer.ReportMuster("force", 8, 8, 8, false, T0), Is.EqualTo(AutonomousRvrSiegeMuster.Phase.None));
                Assert.That(AutonomousRvrEventLayer.GuildArmyPlan("force", null, T0), Is.Null);
                Assert.That(AutonomousRvrEventLayer.IsAbandoned("force", "port-s1-x", T0), Is.False);
                Assert.That(AutonomousRvrEventLayer.IsKeepRouteBlocked("port-s1-x", T0), Is.False);
                Assert.That(AutonomousRvrEventLayer.RestartMarch("force", T0), Is.False);
                Assert.That(AutonomousRvrEventLayer.RenewClaimPlan("force", "port-s1-x", T0), Is.False);
                Assert.That(AutonomousRvrEventLayer.MusterTimeRemaining("force", T0), Is.Null);
            });
        }
        using (WorldMode.OverrideForTests(WorldModelKind.Camlann))
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRvrEventLayer.MarchLeader("port-s1-x"), Is.Null);
                Assert.That(AutonomousRvrEventLayer.ArmyMembers("port-s1-x"), Is.Empty);
                Assert.That(AutonomousRvrEventLayer.IsEventForce("force", T0), Is.False);
                Assert.That(AutonomousRvrEventLayer.SoloSiegeRosters(T0), Is.Empty);
                Assert.That(AutonomousRvrEventLayer.TrySiegeRelease("force", eRealm.Albion, T0, out _, out _), Is.False);
                Assert.That(AutonomousRvrEventLayer.MergeSoloForces("port-s1-x", eRealm.Albion, [1L], "party"), Is.False);
            });
        }
    }

    [TestCase(WorldModelKind.Camlann, false, eRealm.None, (ushort)163, ExpectedResult = true, TestName = "Camlann besieges a Warden keep")]
    [TestCase(WorldModelKind.Realm, false, eRealm.None, (ushort)163, ExpectedResult = false, TestName = "RvR: a neutral frontier keep is no siege objective")]
    [TestCase(WorldModelKind.Realm, false, eRealm.None, (ushort)252, ExpectedResult = true, TestName = "RvR: a neutral battleground keep is")]
    [TestCase(WorldModelKind.Realm, false, eRealm.Midgard, (ushort)100, ExpectedResult = true, TestName = "RvR: a realm keep is")]
    [TestCase(WorldModelKind.Realm, true, eRealm.Midgard, (ushort)100, ExpectedResult = false, TestName = "RvR: never a portal keep")]
    [TestCase(WorldModelKind.Camlann, true, eRealm.None, (ushort)163, ExpectedResult = false, TestName = "Camlann: never a portal keep")]
    public bool SiegeObjectivesFollowTheWorld(WorldModelKind world, bool portal, eRealm realm, ushort region) =>
        AutonomousRvrKeepPolicy.IsSiegeObjective(world, portal, realm, region);

    [Test]
    public void TheTwentyOneFrontierKeepsStartAtTheUpstreamLevels()
    {
        var levels = RvrKeepStart.FrontierKeepLevels;
        Assert.Multiple(() =>
        {
            Assert.That(levels.Count, Is.EqualTo(21));
            Assert.That(levels.Keys.Count(id => id is >= 50 and <= 56), Is.EqualTo(7), "seven Albion keeps");
            Assert.That(levels.Keys.Count(id => id is >= 75 and <= 81), Is.EqualTo(7), "seven Midgard keeps");
            Assert.That(levels.Keys.Count(id => id is >= 100 and <= 106), Is.EqualTo(7), "seven Hibernia keeps");
            Assert.That(RvrKeepStart.StartLevel(50), Is.EqualTo((byte)5), "Caer Benowyc");
            Assert.That(RvrKeepStart.StartLevel(100), Is.EqualTo((byte)4), "Dun Crauchon");
            Assert.That(RvrKeepStart.StartLevel(103), Is.EqualTo((byte)1), "Dun nGed");
            Assert.That(RvrKeepStart.StartLevel(57), Is.Null, "a relic keep keeps its own start");
            Assert.That(RvrKeepStart.StartLevel(1), Is.Null, "a portal keep keeps its own start");
        });
    }

    [TestCase(WorldModelKind.Realm, 50, eRealm.None, eRealm.Albion, true, false, false, ExpectedResult = (byte)5, TestName = "RvR: a Warden-held Benowyc returns to Albion at level 5")]
    [TestCase(WorldModelKind.Realm, 100, eRealm.None, eRealm.Hibernia, true, false, false, ExpectedResult = (byte)4, TestName = "RvR: Dun Crauchon returns to Hibernia at level 4")]
    [TestCase(WorldModelKind.Camlann, 50, eRealm.None, eRealm.Albion, true, false, false, ExpectedResult = null, TestName = "Camlann keeps its Wardens")]
    [TestCase(WorldModelKind.Realm, 50, eRealm.Midgard, eRealm.Albion, true, false, false, ExpectedResult = null, TestName = "RvR: a keep another realm took stays taken")]
    [TestCase(WorldModelKind.Realm, 50, eRealm.None, eRealm.Albion, false, false, false, ExpectedResult = null, TestName = "RvR: a guild's claim is left alone")]
    [TestCase(WorldModelKind.Realm, 57, eRealm.None, eRealm.Albion, true, false, true, ExpectedResult = null, TestName = "RvR: relic keeps keep their own rule")]
    [TestCase(WorldModelKind.Realm, 1, eRealm.None, eRealm.Albion, true, true, false, ExpectedResult = null, TestName = "RvR: portal keeps keep their own rule")]
    [TestCase(WorldModelKind.Realm, 11, eRealm.None, eRealm.None, true, false, false, ExpectedResult = null, TestName = "RvR: a battleground keep stays with the Wardens")]
    public byte? WardenFrontierKeepsMoveOnceToTheirOriginalRealm(WorldModelKind world, int keepId, eRealm realm, eRealm original,
        bool unclaimedOrWardens, bool portal, bool relic) =>
        PvpKeepCampaign.FrontierKeepStart(world, keepId, realm, original, unclaimedOrWardens, portal, relic);

    [Test]
    public void DefenseRallyFallsBackToTheNearerBorderKeep()
    {
        Assert.That(AutonomousRvrStaging.TryGetBorderKeepNear(eRealm.Albion, 1, new Vector2(530_000, 360_000), out var near), Is.True);
        Assert.That(near.Name, Is.EqualTo("Snowdonia Fortress"));
        Assert.That(AutonomousRvrStaging.TryGetBorderKeepNear(eRealm.Albion, 100, new Vector2(530_000, 360_000), out var other), Is.True);
        Assert.That(other.Name, Is.EqualTo("Castle Sauvage"), "another region falls back to the first border keep");
        Assert.That(AutonomousRvrStaging.TryGetBorderKeep(eRealm.Albion, out var first) && first.Name == "Castle Sauvage", Is.True,
            "the fork's first border keep is unchanged");
    }
}
