using System;
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;
using Decision = DOL.GS.AutonomousFrontierTransport.RegroupDecision;
using Mob = DOL.GS.AutonomousRvrMobAvoidance.MobView;

namespace DOL.UnitTests;

/// <summary>Night report proposals 2 and 3 (docs/BUGS.md 65/66): RvR routes
/// bend around named and far-above monsters and dense camps, RvR forces stay
/// out of the shared frontier dungeons, far-above attackers are disengaged,
/// and warbands regroup at the hub before porting back out.</summary>
[TestFixture]
public sealed class UT_RvrRoamAvoidanceAndRegroup
{
    // ---- Proposal 2 (i): route bends around a level-75 named monster -----

    [Test]
    public void RouteBendsAroundTheLevelSeventyFiveArchwizard()
    {
        Vector2 start = new(0, 0), goal = new(6_000, 0);
        var dangers = AutonomousRvrMobAvoidance.BuildDangers(50,
            [new Mob(new(3_000, 100), 75, "Illusion of Aidon the Archwizard", true)]);
        Assert.That(dangers, Has.Count.EqualTo(1));
        Assert.That(dangers[0].Radius, Is.EqualTo(1_650f), "700 + 25 levels x 55, capped");

        Vector2[] bends = AutonomousRvrMobAvoidance.BypassCandidates(start, goal, dangers);
        Assert.That(bends, Is.Not.Empty, "the route bends instead of stalling");
        Vector2 bend = bends[0];
        Assert.Multiple(() =>
        {
            Assert.That(bend.Y, Is.LessThan(0), "passes on the far side from the monster");
            Assert.That(AutonomousRvrMobAvoidance.LegClear(start, bend, dangers), Is.True);
            Assert.That(AutonomousRvrMobAvoidance.LegClear(bend, goal, dangers), Is.True);
            float detour = Vector2.Distance(start, bend) + Vector2.Distance(bend, goal) - 6_000;
            Assert.That(detour, Is.LessThanOrEqualTo(AutonomousRvrMobAvoidance.MaximumDetour), "bounded detour");
            Assert.That(Vector2.Distance(bend, dangers[0].Center), Is.GreaterThanOrEqualTo(dangers[0].Radius));
        });
    }

    [Test]
    public void NoBendWhenNothingBlocksOrTheGoalItselfIsTheCamp()
    {
        var archwizard = AutonomousRvrMobAvoidance.BuildDangers(50,
            [new Mob(new(3_000, 5_000), 75, "Illusion of Aidon the Archwizard", true)]);
        Assert.That(AutonomousRvrMobAvoidance.BypassCandidates(new(0, 0), new(6_000, 0), archwizard), Is.Empty,
            "a monster far off the route changes nothing");
        var onGoal = AutonomousRvrMobAvoidance.BuildDangers(50, [new Mob(new(6_000, 0), 65, "Black Lady", true)]);
        Assert.That(AutonomousRvrMobAvoidance.BypassCandidates(new(0, 0), new(6_000, 0), onGoal), Is.Empty,
            "the destination filter owns a goal inside a danger; the route does not freeze");
        var far = AutonomousRvrMobAvoidance.BuildDangers(50, [new Mob(new(20_000, 0), 75, "Illusion of Aidon the Archwizard", true)]);
        Assert.That(AutonomousRvrMobAvoidance.BypassCandidates(new(0, 0), new(40_000, 0), far), Is.Empty,
            "only the next stretch of the route is bent");
    }

    [TestCase(50, 75, "Illusion of Aidon the Archwizard", true, TestName = "Level 75 named is avoided")]
    [TestCase(50, 65, "Black Lady", true, TestName = "Level 65 named is avoided")]
    [TestCase(50, 58, "reanimated guardian", true, TestName = "Red level 58 monster is avoided")]
    [TestCase(50, 55, "Ogress", true, TestName = "Named level 55 (orange) is avoided")]
    [TestCase(50, 55, "cavernous yeti", false, TestName = "Unnamed orange 55 is not avoided alone")]
    [TestCase(50, 48, "phantom magi", false, TestName = "Yellow 48 is not avoided alone")]
    [TestCase(20, 25, "black bear", true, TestName = "Red for a level-20 bot is avoided")]
    public void AvoidedMonsters(int actor, int mob, string name, bool expected) =>
        Assert.That(AutonomousRvrMobAvoidance.IsAvoidedMob(actor, mob, name), Is.EqualTo(expected));

    [Test]
    public void DenseAggressiveCampsAreDangersButLooseOrPeacefulOnesAreNot()
    {
        Mob[] Pack(int count, bool aggressive) => Enumerable.Range(0, count)
            .Select(index => new Mob(new(5_000 + index * 150, 200), 47, "siabra lookout", aggressive)).ToArray();
        Assert.Multiple(() =>
        {
            var camp = AutonomousRvrMobAvoidance.BuildDangers(50, Pack(4, true));
            Assert.That(camp, Has.Count.EqualTo(1));
            Assert.That(camp[0].Radius, Is.GreaterThanOrEqualTo(AutonomousRvrMobAvoidance.DenseCampRadius));
            Assert.That(AutonomousRvrMobAvoidance.BuildDangers(50, Pack(3, true)), Is.Empty, "three is not a dense camp");
            Assert.That(AutonomousRvrMobAvoidance.BuildDangers(50, Pack(6, false)), Is.Empty, "non-aggressive monsters never start a fight");
            var greys = Enumerable.Range(0, 6).Select(index => new Mob(new(index * 100, 0), 20, "grey rat", true)).ToArray();
            Assert.That(AutonomousRvrMobAvoidance.BuildDangers(50, greys), Is.Empty, "green and grey camps are ignored");
            Assert.That(AutonomousRvrMobAvoidance.BypassCandidates(new(3_000, 0), new(10_000, 0),
                AutonomousRvrMobAvoidance.BuildDangers(50, Pack(4, true))), Is.Not.Empty, "a dense camp on the road is bent around");
        });
    }

    [Test]
    public void PatrolSpotsAreClearingsNotNamedOrCrowdedCamps()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrMobAvoidance.IsSuitablePatrolCamp(50, [48, 49], 3, "mature skimmer"), Is.True);
            Assert.That(AutonomousRvrMobAvoidance.IsSuitablePatrolCamp(50, [65], 1, "Black Lady"), Is.False);
            Assert.That(AutonomousRvrMobAvoidance.IsSuitablePatrolCamp(50, [56, 57], 2, "buzzing nuisance"), Is.False);
            Assert.That(AutonomousRvrMobAvoidance.IsSuitablePatrolCamp(50, [47], AutonomousRvrMobAvoidance.PatrolDenseMobCount,
                "siabra lookout"), Is.False, "a crowded camp is not a patrol clearing");
        });
    }

    // ---- Proposal 2 (ii): RvR forces stay out of the frontier dungeons ---

    [TestCase((ushort)246, true)]
    [TestCase((ushort)248, true)]
    [TestCase((ushort)276, true)]
    [TestCase((ushort)277, true)]
    [TestCase((ushort)1, false)]
    [TestCase((ushort)100, false)]
    [TestCase((ushort)200, false)]
    [TestCase((ushort)249, false)] // Darkness Falls stays a Hunter hunting ground
    public void SharedFrontierDungeonsAreNotRvrDestinations(ushort region, bool forbidden) =>
        Assert.That(AutonomousRvrMobAvoidance.IsForbiddenRvrRegion(region), Is.EqualTo(forbidden));

    [Test]
    public void RvrForceDoesNotRouteThroughTheDungeonTunnelBetweenFrontiers()
    {
        // The installed zone points: Albion frontier 1 - Hall of the Corrupt
        // 277 - Summoner's Hall 248 - Marfach Caverns 276 - Hibernia 200.
        DbZonePoint[] edges =
        [
            Edge(88, 1, 277), Edge(89, 277, 1), Edge(90, 277, 248), Edge(98, 248, 277),
            Edge(96, 276, 248), Edge(97, 248, 276), Edge(94, 200, 276), Edge(95, 276, 200),
            Edge(93, 100, 246), Edge(91, 246, 100), Edge(92, 246, 248), Edge(99, 248, 246),
        ];
        DbZonePoint Find(DbZonePoint[] usable, ushort from, ushort to) =>
            AutonomousZoneCrossingSearch.FindFirstCrossing(usable, from, to, 0, 0, null);
        DbZonePoint[] Rvr(ushort goal) => edges.Where(edge => AutonomousRvrMobAvoidance.AllowsRvrCrossing(edge.TargetRegion, goal)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(Find(edges, 1, 200)?.TargetRegion, Is.EqualTo((ushort)277), "unfiltered, the tunnel is the only road");
            Assert.That(Find(Rvr(200), 1, 200), Is.Null, "an RvR force ports instead of walking the tunnel");
            Assert.That(Find(Rvr(100), 200, 100), Is.Null);
            Assert.That(Find(Rvr(200), 277, 200), Is.Null, "no way deeper toward the goal");
            Assert.That(AutonomousRvrMobAvoidance.NearestExit(edges, 277, 0, 0)?.TargetRegion, Is.EqualTo((ushort)1),
                "a bot already inside takes the nearest exit");
            Assert.That(AutonomousRvrMobAvoidance.NearestExit(edges, 248, 0, 0)?.TargetRegion, Is.Not.EqualTo((ushort)248),
                "from Summoner's Hall the first step leads toward an exit");
            Assert.That(AutonomousRvrMobAvoidance.NearestExit(edges, 248, 0, 0) is { } step &&
                AutonomousRvrMobAvoidance.NearestExit(edges, step.TargetRegion, 0, 0) is { } exit &&
                !AutonomousRvrMobAvoidance.IsForbiddenRvrRegion(exit.TargetRegion), Is.True, "two steps and it is outside");
            Assert.That(Find(Rvr(277), 1, 277)?.TargetRegion, Is.EqualTo((ushort)277),
                "a goal inside the complex itself stays reachable");
        });
    }

    private static readonly DbZonePoint[] Tunnel =
    [
        Edge(88, 1, 277), Edge(89, 277, 1), Edge(90, 277, 248), Edge(98, 248, 277),
        Edge(96, 276, 248), Edge(97, 248, 276), Edge(94, 200, 276), Edge(95, 276, 200),
    ];

    private static DbZonePoint Search(System.Collections.Generic.IReadOnlyList<DbZonePoint> edges, ushort from, ushort to) =>
        AutonomousZoneCrossingSearch.FindFirstCrossing(edges, from, to, 0, 0, null);

    [Test]
    public void RelicCarrierKeepsTheDungeonRoadOthersOnlyAsALastResort()
    {
        DbZonePoint Choose(ushort from, ushort to, bool relic, bool noPorter) =>
            AutonomousRvrMobAvoidance.ChooseRvrCrossing(Tunnel, from, to, 0, 0, relic, noPorter, false,
                edges => Search(edges, from, to), out _);
        Assert.Multiple(() =>
        {
            Assert.That(Choose(1, 200, relic: true, noPorter: false)?.TargetRegion, Is.EqualTo((ushort)277),
                "a relic carrier cannot port, so it may cross to its pad");
            Assert.That(Choose(1, 200, relic: false, noPorter: false), Is.Null, "with a usable porter nobody walks the tunnel");
            Assert.That(Choose(1, 200, relic: false, noPorter: true)?.TargetRegion, Is.EqualTo((ushort)277),
                "no porter route: the tunnel is the last resort, the force is not stranded");
            Assert.That(Choose(277, 200, relic: false, noPorter: false)?.TargetRegion, Is.EqualTo((ushort)1), "inside: nearest exit");
        });
    }

    [Test]
    public void ABotOnTheFallbackRoadWalksThroughInsteadOfTurningBack()
    {
        DbZonePoint Choose(ushort from, bool relicParty, bool noPorter, bool committed, out bool commits) =>
            AutonomousRvrMobAvoidance.ChooseRvrCrossing(Tunnel, from, 200, 0, 0, relicParty, noPorter, committed,
                edges => Search(edges, from, 200), out commits);

        Assert.That(Choose(1, false, noPorter: true, committed: false, out bool commits)?.TargetRegion, Is.EqualTo((ushort)277));
        Assert.That(commits, Is.True, "choosing the fallback road commits the bot to it");
        Assert.Multiple(() =>
        {
            Assert.That(Choose(277, false, noPorter: false, committed: true, out bool again)?.TargetRegion, Is.EqualTo((ushort)248),
                "inside Hall of the Corrupt it goes on toward Summoner's Hall, not back to region 1");
            Assert.That(again, Is.True, "the commitment is kept inside the complex");
            Assert.That(Choose(277, relicParty: true, noPorter: false, committed: false, out _)?.TargetRegion, Is.EqualTo((ushort)248),
                "an escort in the carrier's group walks on with the carrier");
            Assert.That(Choose(248, false, noPorter: false, committed: true, out _)?.TargetRegion, Is.EqualTo((ushort)276));
            Assert.That(Choose(276, false, noPorter: false, committed: true, out bool leaving)?.TargetRegion, Is.EqualTo((ushort)200));
            Assert.That(leaving, Is.False, "the step out of the complex ends the commitment");
            Assert.That(Choose(277, false, noPorter: false, committed: false, out bool none)?.TargetRegion, Is.EqualTo((ushort)1),
                "a bot that never chose the road takes the nearest exit");
            Assert.That(none, Is.False);
        });
    }

    [Test]
    public void AlbionWarbandInOdinReachesEmainInTwoHops()
    {
        // Albion's portal keep in Odin's Gate sells only the home medallion.
        AutonomousFrontierTransport.Passage first = AutonomousFrontierTransport.ChoosePassage(
            eRealm.Albion, 100, 200, medallion => medallion == "home_necklace");
        Assert.That(first?.Region, Is.EqualTo((ushort)1), "first home to Castle Sauvage");
        Assert.That(first.Medallion, Is.EqualTo("home_necklace"));
        AutonomousFrontierTransport.Passage second = AutonomousFrontierTransport.ChoosePassage(
            eRealm.Albion, 1, 200, medallion => medallion == "emain_necklace");
        Assert.That(second?.Region, Is.EqualTo((ushort)200), "then onward to Emain from the own hub");
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousFrontierTransport.ChoosePassage(eRealm.Midgard, 1, 200, m => m == "home_necklace")?.Region,
                Is.EqualTo((ushort)100), "Midgard in Hadrian's goes home first as well");
            Assert.That(AutonomousFrontierTransport.ChoosePassage(eRealm.Hibernia, 100, 1, m => m == "hadrian_necklace")?.Region,
                Is.EqualTo((ushort)1), "Hibernia's portal keeps sell the next frontier directly");
            Assert.That(AutonomousFrontierTransport.ChoosePassage(eRealm.Albion, 100, 200, _ => false,
                m => m == "emain_necklace")?.Region, Is.EqualTo((ushort)200), "a ticket already held is used");
            Assert.That(AutonomousFrontierTransport.ChoosePassage(eRealm.Albion, 100, 200, _ => false), Is.Null,
                "nothing on sale: no passage (the crossing search may then fall back)");
        });
    }

    [Test]
    public void BendsExpireAndAFailedBendFallsBackToTheRoadOrder()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrMobAvoidance.KeepBend(true, false, 0, 10_000), Is.True);
            Assert.That(AutonomousRvrMobAvoidance.KeepBend(true, false, 0, AutonomousRvrMobAvoidance.BendLifetimeMilliseconds), Is.False,
                "a bend that is not reached in time is dropped");
            Assert.That(AutonomousRvrMobAvoidance.KeepBend(true, true, 0, 10_000), Is.False);
            Assert.That(AutonomousRvrMobAvoidance.KeepBend(false, false, 0, 10_000), Is.False, "a new road leg drops it");
        });

        Vector3 road = new(1_000, 0, 0), bend = new(500, -900, 0);
        int dropped = 0, issued = 0;
        Assert.That(AutonomousRvrMobAvoidance.TryWalkBend(road, bend, _ => { issued++; return false; }, () => dropped++), Is.False,
            "the caller now issues its ordinary road order with its own failure handling");
        Assert.That((issued, dropped), Is.EqualTo((1, 1)));
        Assert.That(AutonomousRvrMobAvoidance.TryWalkBend(road, bend, _ => true, () => dropped++), Is.True);
        Assert.That(dropped, Is.EqualTo(1));
        Assert.That(AutonomousRvrMobAvoidance.TryWalkBend(road, road, _ => { issued++; return true; }, () => dropped++), Is.False);
        Assert.That(issued, Is.EqualTo(1), "no bend, no extra order");
    }

    private static DbZonePoint Edge(ushort id, ushort from, ushort to) =>
        new() { Id = id, SourceRegion = from, TargetRegion = to };

    // ---- Proposal 2 (iii): disengage from far-above attackers ------------

    [TestCase(50, 75, "Illusion of Aidon the Archwizard", true)]
    [TestCase(50, 65, "Black Lady", true)]
    [TestCase(50, 58, "reanimated guardian", true)]
    [TestCase(50, 51, "defiled skeleton", false)]
    [TestCase(50, 47, "siabra lookout", false)]
    public void FarAboveAttackersAreDisengagedOrdinaryOnesFought(int actor, int mob, string name, bool disengage) =>
        Assert.That(AutonomousRvrMobAvoidance.ShouldDisengage(actor, mob, name), Is.EqualTo(disengage));

    [Test]
    public void ConScaleMatchesTheOriginalForALevelFifty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrMobAvoidance.Con(50, 35), Is.EqualTo(ConColor.GREY));
            Assert.That(AutonomousRvrMobAvoidance.Con(50, 48), Is.EqualTo(ConColor.YELLOW));
            Assert.That(AutonomousRvrMobAvoidance.Con(50, 55), Is.EqualTo(ConColor.ORANGE));
            Assert.That(AutonomousRvrMobAvoidance.Con(50, 58), Is.EqualTo(ConColor.RED));
            Assert.That(AutonomousRvrMobAvoidance.Con(50, 75), Is.EqualTo(ConColor.PURPLE));
        });
    }

    // ---- Proposal 3: regroup at the hub before porting again -------------

    private const long Now = 500_000_000;
    private const long Minute = 60_000;

    private static Decision Decide(bool warband, long? releasedAgo, bool gathered = false, long? departedAgo = null,
        bool outbound = true, bool defender = false, long? earliestAgo = null) =>
        AutonomousFrontierTransport.DecideRegroup(outbound, warband,
            earliestAgo.HasValue ? Now - earliestAgo : releasedAgo.HasValue ? Now - releasedAgo : null,
            releasedAgo.HasValue ? Now - releasedAgo : null, gathered,
            departedAgo.HasValue ? Now - departedAgo : null, defender, Now);

    [Test]
    public void WarbandReleasedSixtySecondsAgoDoesNotBoard()
    {
        Assert.That(Decide(true, Minute), Is.EqualTo(Decision.ReleaseHold));
        Assert.That(Decide(true, Minute, gathered: true), Is.EqualTo(Decision.ReleaseHold),
            "even gathered, nobody ports straight off the bindstone");
    }

    [Test]
    public void WarbandBoardsOnceRegroupedOrWhenTheWindowEnds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Decide(true, 90_000), Is.EqualTo(Decision.Regroup), "members still in the field or dead");
            Assert.That(Decide(true, 90_000, gathered: true), Is.EqualTo(Decision.Board), "everyone alive at the porter");
            Assert.That(Decide(true, 90_000, earliestAgo: 3 * Minute), Is.EqualTo(Decision.Board),
                "the first waiting member has waited three minutes");
            Assert.That(Decide(true, null), Is.EqualTo(Decision.Board), "a warband that never died leaves at once");
        });
    }

    [Test]
    public void DeparturesPerWarbandAreCappedToOnePerFiveMinutes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Decide(true, null, departedAgo: 2 * Minute), Is.EqualTo(Decision.DepartureCap));
            Assert.That(Decide(true, null, departedAgo: 5 * Minute), Is.EqualTo(Decision.Board));
            Assert.That(Decide(true, null, departedAgo: 10_000), Is.EqualTo(Decision.Board),
                "the rest of the same departure, split over transfer slices, still follows");
            Assert.That(Decide(true, null, departedAgo: 2 * Minute, defender: true), Is.EqualTo(Decision.Board),
                "a keep under attack is answered without the cap");
            Assert.That(Decide(false, null, departedAgo: Minute), Is.EqualTo(Decision.Board), "solo bots are not capped");
        });

        string force = "cap-" + Guid.NewGuid();
        AutonomousFrontierTransport.RecordDeparture(force, Now);
        AutonomousFrontierTransport.RecordDeparture(force, Now + 10_000);
        Assert.That(AutonomousFrontierTransport.LastDeparture(force), Is.EqualTo(Now), "a continuation keeps the start");
        AutonomousFrontierTransport.RecordDeparture(force, Now + 6 * Minute);
        Assert.That(AutonomousFrontierTransport.LastDeparture(force), Is.EqualTo(Now + 6 * Minute));
        AutonomousFrontierTransport.ForgetDeparture(force);
    }

    [Test]
    public void SoloBotsWaitAfterReleaseButNotForAGroup()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Decide(false, 30_000), Is.EqualTo(Decision.ReleaseHold));
            Assert.That(Decide(false, 80_000), Is.EqualTo(Decision.Board), "about 75 s after release a solo leaves");
            Assert.That(Decide(true, 30_000, outbound: false), Is.EqualTo(Decision.Board), "the way home is never held");
        });
    }

    [Test]
    public void OldReleasesNoLongerCount()
    {
        Assert.That(AutonomousFrontierTransport.RecentRelease(Now - 4 * Minute, Now), Is.Null);
        Assert.That(AutonomousFrontierTransport.RecentRelease(Now - Minute, Now), Is.EqualTo(Now - Minute));
        Assert.That(AutonomousFrontierTransport.RecentRelease((long?)null, Now), Is.Null);
    }
}
