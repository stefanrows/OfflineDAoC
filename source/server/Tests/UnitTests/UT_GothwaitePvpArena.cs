using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.GS.Tests;

/// <summary>
/// The Gothwaite PvP arena rules of stage 3 (docs/GOTHWAITE_PVP_MODE.md sections 6.2, 6.4, 6.5 and 8): the patrol choice, the
/// dwell, the return and chase-limit predicates, the edge margin, the stuck rule, the leader promotion, the scan offset and the
/// arena log lines. Pure functions only: no GameBot and no database.
/// </summary>
[TestFixture]
public class UT_GothwaitePvpArena
{
    private const int Draws = 10_000;
    private const int EdgeOfLowBand = 20_000;
    private const int EdgeOfHighBand = 30_000;
    private static readonly GothwaitePvpArena Arena = GothwaitePvpArena.Gothwaite;

    [SetUp]
    public void SetUp() => GothwaitePvpMode.Reset();

    [TearDown]
    public void TearDown() => GothwaitePvpMode.Reset();

    private static void TurnOn() => GothwaitePvpMode.Apply(GothwaitePvpSettings.Defaults with { Enabled = true });

    private static GothwaitePvpSpot PatrolPointNamed(string name) => Arena.PatrolPoints.Single(point => point.Name == name);

    // One patrol choice for a bot of this level, with the arena's own points, the goal edge of the level and the given weights.
    private static GothwaitePvpSpot? Pick(IReadOnlyList<GothwaitePvpSpot> points, GothwaitePvpSpot? last, int level, Random random,
        Func<GothwaitePvpSpot, double> weightOf = null, Func<GothwaitePvpSpot, bool> usable = null)
    {
        int goalEdge = GothwaitePvpArenaRules.GoalEdge(GothwaitePvpArena.EdgeFor(level, GothwaitePvpMode.Settings));
        return GothwaitePvpArenaRules.ChoosePatrolPoint(Arena, goalEdge, points, last, usable, weightOf ?? (_ => 1), random);
    }

    private static List<string> PickSequence(int seed, int level, int count)
    {
        var random = new Random(seed);
        IReadOnlyList<GothwaitePvpSpot> points = Arena.PatrolPointsFor(level, GothwaitePvpMode.Settings);
        var names = new List<string>();
        GothwaitePvpSpot? last = null;
        for (int i = 0; i < count; i++)
        {
            GothwaitePvpSpot? next = Pick(points, last, level, random);
            names.Add(next?.Name ?? "none");
            last = next;
        }
        return names;
    }

    [Test]
    public void EveryPatrolPointLiesOutsideTheCastleCircle()
    {
        Assert.Multiple(() =>
        {
            foreach (GothwaitePvpSpot point in Arena.PatrolPoints)
                Assert.That(Arena.CastleDistance(point.X, point.Y), Is.GreaterThan(Arena.SafeRadius), point.Name);
        });
    }

    [Test]
    public void ThePatrolChoiceNeverRepeatsTheLastPoint()
    {
        TurnOn();
        IReadOnlyList<GothwaitePvpSpot> points = Arena.PatrolPointsFor(25, GothwaitePvpMode.Settings);
        var random = new Random(7);
        GothwaitePvpSpot last = points[0];
        int repeats = 0;
        for (int i = 0; i < Draws; i++)
        {
            GothwaitePvpSpot? next = Pick(points, last, 25, random);
            Assert.That(next, Is.Not.Null, "a patrol point is always left to choose");
            if (next.Value == last)
                repeats++;
            last = next.Value;
        }

        Assert.That(repeats, Is.Zero, "the point just left is never picked again on the next draw");
    }

    [TestCase(1)]
    [TestCase(19)]
    [TestCase(20)]
    [TestCase(25)]
    [TestCase(50)]
    public void ThePatrolChoiceNeverLeavesTheGoalEdgeForAnyLevel(int level)
    {
        TurnOn();
        IReadOnlyList<GothwaitePvpSpot> points = Arena.PatrolPointsFor(level, GothwaitePvpMode.Settings);
        int goalEdge = GothwaitePvpArenaRules.GoalEdge(GothwaitePvpArena.EdgeFor(level, GothwaitePvpMode.Settings));
        var random = new Random(level);
        GothwaitePvpSpot? last = null;
        for (int i = 0; i < 2_000; i++)
        {
            GothwaitePvpSpot? next = Pick(points, last, level, random);
            Assert.That(next, Is.Not.Null);
            Assert.That(Arena.CastleDistance(next.Value.X, next.Value.Y), Is.LessThanOrEqualTo(goalEdge), $"level {level}: {next.Value.Name}");
            last = next;
        }
    }

    [Test]
    public void ALowLevelBotNeverChoosesAPointBeyondItsBand()
    {
        TurnOn();
        IReadOnlyList<GothwaitePvpSpot> points = Arena.PatrolPoints;
        var random = new Random(19);
        GothwaitePvpSpot? last = null;
        for (int i = 0; i < 2_000; i++)
        {
            // The points here are the whole list, the high band included: only the goal edge may filter them out.
            GothwaitePvpSpot? next = Pick(points, last, 1, random);
            Assert.That(next, Is.Not.Null);
            Assert.That(Arena.CastleDistance(next.Value.X, next.Value.Y), Is.LessThanOrEqualTo(EdgeOfLowBand - GothwaitePvpArenaRules.EdgeMargin));
            last = next;
        }
    }

    [Test]
    public void HeatRaisesThePatrolChanceOfAPoint()
    {
        TurnOn();
        GothwaitePvpSpot hot = PatrolPointNamed("GP01");
        GothwaitePvpSpot cold = PatrolPointNamed("GP03");
        var points = new[] { hot, cold };
        // The same distance to the bot for both points, so only the heat differs: a weight of 3 against 1.
        Func<GothwaitePvpSpot, double> weightOf = point => GothwaitePvpArenaRules.PatrolWeight(point == hot ? 2 : 0, 0);
        var random = new Random(11);
        int hotPicks = 0;
        for (int i = 0; i < Draws; i++)
            if (Pick(points, null, 25, random, weightOf) == hot)
                hotPicks++;

        double share = hotPicks / (double)Draws;
        Assert.That(share, Is.EqualTo(3.0 / 4.0).Within(0.02), "a point with heat 2 is picked three times as often as a cold one");
    }

    [Test]
    public void ANearerPointIsOnlySlightlyPreferred()
    {
        Assert.That(GothwaitePvpArenaRules.PatrolWeight(0, 0) / GothwaitePvpArenaRules.PatrolWeight(0, EdgeOfLowBand),
            Is.EqualTo(3.0).Within(1e-9), "one point at the bot's feet weighs three times a point 20,000 units away");

        TurnOn();
        GothwaitePvpSpot near = PatrolPointNamed("GP01");
        GothwaitePvpSpot far = PatrolPointNamed("GP03");
        var points = new[] { near, far };
        Func<GothwaitePvpSpot, double> weightOf = point => GothwaitePvpArenaRules.PatrolWeight(0, point == near ? 0 : EdgeOfLowBand);
        var random = new Random(13);
        int nearPicks = 0;
        for (int i = 0; i < Draws; i++)
            if (Pick(points, null, 25, random, weightOf) == near)
                nearPicks++;

        Assert.That(nearPicks / (double)Draws, Is.EqualTo(0.75).Within(0.02), "the near point wins three draws in four");
    }

    [Test]
    public void ThePatrolWeightFollowsTheHeatAndDistanceFormula()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.PatrolWeight(0, 0), Is.EqualTo(1.0), "no heat, no distance");
            Assert.That(GothwaitePvpArenaRules.PatrolWeight(2, 0), Is.EqualTo(3.0), "heat adds one per unit");
            Assert.That(GothwaitePvpArenaRules.PatrolWeight(GothwaitePvpArenaRules.HeatCap, 0), Is.EqualTo(4.0), "heat up to the cap counts");
            Assert.That(GothwaitePvpArenaRules.PatrolWeight(120, 0), Is.EqualTo(4.0), "heat above the cap counts as the cap");
            Assert.That(GothwaitePvpArenaRules.PatrolWeight(0, 10_000), Is.EqualTo(0.5), "10,000 units halve the weight");
            Assert.That(GothwaitePvpArenaRules.PatrolWeight(-3, 0), Is.EqualTo(1.0), "negative heat counts as none");
            Assert.That(GothwaitePvpArenaRules.PatrolWeight(double.NaN, 0), Is.EqualTo(1.0), "unknown heat counts as none");
        });
    }

    [Test]
    public void TheSameSeedGivesTheSamePatrolSequence()
    {
        TurnOn();
        Assert.That(PickSequence(42, 25, 200), Is.EqualTo(PickSequence(42, 25, 200)));
        Assert.That(PickSequence(42, 25, 200), Is.Not.EqualTo(PickSequence(43, 25, 200)), "another seed draws another route");
    }

    [Test]
    public void AnUnusablePointIsNeverPicked()
    {
        TurnOn();
        IReadOnlyList<GothwaitePvpSpot> points = Arena.PatrolPointsFor(25, GothwaitePvpMode.Settings);
        var random = new Random(5);
        for (int i = 0; i < Draws; i++)
            Assert.That(Pick(points, null, 25, random, usable: point => point.Name != "GP03")?.Name, Is.Not.EqualTo("GP03"));
    }

    [Test]
    public void NoPatrolPointLeftGivesNone()
    {
        TurnOn();
        GothwaitePvpSpot only = PatrolPointNamed("GP01");
        var random = new Random(1);

        Assert.Multiple(() =>
        {
            Assert.That(Pick(new[] { only }, only, 25, random), Is.Null, "the only point is the one just left");
            Assert.That(Pick(new[] { only }, null, 25, random, usable: _ => false), Is.Null, "every point is refused");
            Assert.That(Pick(Array.Empty<GothwaitePvpSpot>(), null, 25, random), Is.Null, "no point at all");
        });
    }

    [Test]
    public void TheDwellIsSixToFifteenSecondsPerVisit()
    {
        var random = new Random(3);
        int shortest = int.MaxValue;
        int longest = int.MinValue;
        for (int i = 0; i < Draws; i++)
        {
            int dwell = GothwaitePvpArenaRules.DwellMilliseconds(random);
            shortest = Math.Min(shortest, dwell);
            longest = Math.Max(longest, dwell);
        }

        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.DwellMinMilliseconds, Is.EqualTo(6_000));
            Assert.That(GothwaitePvpArenaRules.DwellMaxMilliseconds, Is.EqualTo(15_000));
            Assert.That(shortest, Is.InRange(GothwaitePvpArenaRules.DwellMinMilliseconds, GothwaitePvpArenaRules.DwellMinMilliseconds + 100));
            Assert.That(longest, Is.InRange(GothwaitePvpArenaRules.DwellMaxMilliseconds - 100, GothwaitePvpArenaRules.DwellMaxMilliseconds));
        });
    }

    [Test]
    public void AFighterReturnsBelowThirtyFivePercentHealthOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.ReturnReason(34, 100, usesPower: false, leaderLost: false, wiped: false),
                Is.EqualTo("low_health"));
            Assert.That(GothwaitePvpArenaRules.ReturnReason(35, 100, usesPower: false, leaderLost: false, wiped: false), Is.Null,
                "35 is not below 35");
            Assert.That(GothwaitePvpArenaRules.ReturnReason(0, 0, usesPower: false, leaderLost: false, wiped: false),
                Is.EqualTo("low_health"));
        });
    }

    [Test]
    public void ACasterReturnsBelowTwentyPercentPowerAndAFighterIgnoresPower()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.ReturnReason(100, 19, usesPower: true, leaderLost: false, wiped: false),
                Is.EqualTo("low_power"));
            Assert.That(GothwaitePvpArenaRules.ReturnReason(100, 20, usesPower: true, leaderLost: false, wiped: false), Is.Null,
                "20 is not below 20");
            Assert.That(GothwaitePvpArenaRules.ReturnReason(100, 0, usesPower: false, leaderLost: false, wiped: false), Is.Null,
                "a fighter without mana does not return for power");
        });
    }

    [Test]
    public void TheReturnReasonsKeepTheirOrder()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.ReturnReason(30, 10, usesPower: true, leaderLost: true, wiped: true), Is.EqualTo("low_health"));
            Assert.That(GothwaitePvpArenaRules.ReturnReason(100, 10, usesPower: true, leaderLost: true, wiped: true), Is.EqualTo("low_power"));
            Assert.That(GothwaitePvpArenaRules.ReturnReason(100, 100, usesPower: false, leaderLost: true, wiped: true), Is.EqualTo("leader_lost"));
            Assert.That(GothwaitePvpArenaRules.ReturnReason(100, 100, usesPower: false, leaderLost: false, wiped: true), Is.EqualTo("wiped"));
        });
    }

    [Test]
    public void AFollowerLosesItsGroupOnlyWhenItsLeaderIsGoneAndNoOneElseIsNear()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.FollowerLost(leaderPresent: true, aliveNearOthers: 0), Is.False, "the leader is still here");
            Assert.That(GothwaitePvpArenaRules.FollowerLost(leaderPresent: false, aliveNearOthers: 1), Is.False, "a member is near");
            Assert.That(GothwaitePvpArenaRules.FollowerLost(leaderPresent: false, aliveNearOthers: 0), Is.True, "orphaned");
        });
    }

    [Test]
    public void AGroupIsWipedOnlyWhenNoOtherMemberLivesAndASoloNeverIs()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.Wiped(solo: false, aliveOthers: 0), Is.True);
            Assert.That(GothwaitePvpArenaRules.Wiped(solo: false, aliveOthers: 1), Is.False);
            Assert.That(GothwaitePvpArenaRules.Wiped(solo: true, aliveOthers: 0), Is.False, "a solo has no group to wipe");
        });
    }

    [Test]
    public void TheChaseLimitDropsAFightForTheCastleTheEdgeOrTheBotsOwnEdgePlusSlack()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.ChaseLimitReason(true, 3_000, 5_000, EdgeOfLowBand), Is.EqualTo("castle"), "a target in the circle");
            Assert.That(GothwaitePvpArenaRules.ChaseLimitReason(false, EdgeOfLowBand + 1, 5_000, EdgeOfLowBand), Is.EqualTo("edge"),
                "a target one unit beyond the edge");
            Assert.That(GothwaitePvpArenaRules.ChaseLimitReason(false, double.PositiveInfinity, 5_000, EdgeOfLowBand), Is.EqualTo("edge"),
                "a target outside the arena region");
            Assert.That(GothwaitePvpArenaRules.ChaseLimitReason(false, 15_000, EdgeOfLowBand + 1_001, EdgeOfLowBand),
                Is.EqualTo("self_edge"), "the bot is past its edge plus the slack");
            Assert.That(GothwaitePvpArenaRules.ChaseLimitReason(false, 15_000, EdgeOfLowBand + 1_000, EdgeOfLowBand), Is.Null,
                "exactly the slack is still inside");
            Assert.That(GothwaitePvpArenaRules.ChaseLimitReason(true, EdgeOfLowBand + 5_000, EdgeOfLowBand + 5_000, EdgeOfLowBand),
                Is.EqualTo("castle"), "the castle is named before the edge");
            Assert.That(GothwaitePvpArenaRules.ChaseLimitReason(false, 19_999, 19_999, EdgeOfLowBand), Is.Null, "a fight inside the arena goes on");
        });
    }

    [Test]
    public void TheChaseLimitDoesNotLookAtTheTargetsConSoARetaliationStands()
    {
        // The predicate has no con input: a grey or purple target that hits the bot from inside the arena is not dropped for its con.
        Assert.That(GothwaitePvpArenaRules.ChaseLimitReason(false, 12_000, 12_000, EdgeOfLowBand), Is.Null);
    }

    [Test]
    public void TheEdgeOfEachBandComesFromTheSettings()
    {
        TurnOn();
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArena.EdgeFor(1, GothwaitePvpMode.Settings), Is.EqualTo(EdgeOfLowBand));
            Assert.That(GothwaitePvpArena.EdgeFor(19, GothwaitePvpMode.Settings), Is.EqualTo(EdgeOfLowBand));
            Assert.That(GothwaitePvpArena.EdgeFor(20, GothwaitePvpMode.Settings), Is.EqualTo(EdgeOfHighBand));
            Assert.That(GothwaitePvpArena.EdgeFor(50, GothwaitePvpMode.Settings), Is.EqualTo(EdgeOfHighBand));
        });
    }

    [Test]
    public void TheGoalEdgeKeepsAnArrivingBotInsideItsEdge()
    {
        TurnOn();
        Assert.That(GothwaitePvpArenaRules.GoalEdge(EdgeOfLowBand), Is.EqualTo(EdgeOfLowBand - GothwaitePvpArenaRules.EdgeMargin));
        foreach (GothwaitePvpSpot point in Arena.PatrolPointsFor(1, GothwaitePvpMode.Settings))
        {
            double castleDistance = Arena.CastleDistance(point.X, point.Y);
            if (castleDistance <= GothwaitePvpArenaRules.GoalEdge(EdgeOfLowBand))
                Assert.That(castleDistance + GothwaitePvpArenaRules.EdgeMargin, Is.LessThanOrEqualTo(EdgeOfLowBand), point.Name);
        }
    }

    [Test]
    public void OutsideTheEdgeMeansStrictlyBeyondIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.OutsideEdge(EdgeOfLowBand, EdgeOfLowBand), Is.False);
            Assert.That(GothwaitePvpArenaRules.OutsideEdge(EdgeOfLowBand + 1, EdgeOfLowBand), Is.True);
        });
    }

    [Test]
    public void AWalkingLeaderNeedsNinetySixUnitsOfProgressWithinFortyFiveSeconds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.MadeProgress(distance: 904, reference: 1_000), Is.True, "96 units closer");
            Assert.That(GothwaitePvpArenaRules.MadeProgress(distance: 905, reference: 1_000), Is.False, "95 units closer is not enough");
            Assert.That(GothwaitePvpArenaRules.ProgressWindowElapsed(now: 44_999, since: 0), Is.False);
            Assert.That(GothwaitePvpArenaRules.ProgressWindowElapsed(now: 45_000, since: 0), Is.True);
        });
    }

    [Test]
    public void AFollowerMovesNinetySixUnitsToCountAsProgress()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.MovedEnough(95.9), Is.False);
            Assert.That(GothwaitePvpArenaRules.MovedEnough(96), Is.True);
        });
    }

    [Test]
    public void TheStuckRuleRepicksTwiceThenSendsAPatrollerHome()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.StuckAction(1, canRepick: true), Is.EqualTo("repick"));
            Assert.That(GothwaitePvpArenaRules.StuckAction(2, canRepick: true), Is.EqualTo("repick"));
            Assert.That(GothwaitePvpArenaRules.StuckAction(3, canRepick: true), Is.EqualTo("castle"), "the third in a row");
            Assert.That(GothwaitePvpArenaRules.StuckAction(1, canRepick: false), Is.EqualTo("castle"), "no patrol goal to pick");
        });
    }

    [Test]
    public void ThePromotionChoosesTheFirstLivingMemberInTheArenaInJoinOrder()
    {
        var members = new[]
        {
            new GothwaitePvpPromotionCandidate(1, Alive: false, InArena: true),
            new GothwaitePvpPromotionCandidate(2, Alive: true, InArena: false),
            new GothwaitePvpPromotionCandidate(3, Alive: true, InArena: true),
            new GothwaitePvpPromotionCandidate(4, Alive: true, InArena: true),
        };

        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.FirstAliveInArena(members), Is.EqualTo(2), "the third member, by join order");
            Assert.That(GothwaitePvpArenaRules.FirstAliveInArena(new[] { members[0], members[1] }), Is.EqualTo(-1), "none qualifies");
            Assert.That(GothwaitePvpArenaRules.FirstAliveInArena(Array.Empty<GothwaitePvpPromotionCandidate>()), Is.EqualTo(-1));
            Assert.That(GothwaitePvpArenaRules.FirstAliveInArena(null), Is.EqualTo(-1));
        });
    }

    [Test]
    public void AScanNeedsItsTurnAndAnArenaBotThatIsNotReturningOrOutsideItsEdge()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.MayScan(due: true, returning: false, outsideOwnEdge: false, isLeader: true, leaderHasLegalTarget: false),
                Is.True, "a leader scans when its turn comes");
            Assert.That(GothwaitePvpArenaRules.MayScan(due: false, returning: false, outsideOwnEdge: false, isLeader: true, leaderHasLegalTarget: false),
                Is.False, "not yet due");
            Assert.That(GothwaitePvpArenaRules.MayScan(due: true, returning: true, outsideOwnEdge: false, isLeader: true, leaderHasLegalTarget: false),
                Is.False, "a returning bot walks home");
            Assert.That(GothwaitePvpArenaRules.MayScan(due: true, returning: false, outsideOwnEdge: true, isLeader: true, leaderHasLegalTarget: false),
                Is.False, "outside its own edge the walk back comes first");
        });
    }

    [Test]
    public void AFollowerScansOnlyWhileItsLeaderHasNoLegalTarget()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.MayScan(due: true, returning: false, outsideOwnEdge: false, isLeader: false, leaderHasLegalTarget: false),
                Is.True, "the leader has nothing to assist");
            Assert.That(GothwaitePvpArenaRules.MayScan(due: true, returning: false, outsideOwnEdge: false, isLeader: false, leaderHasLegalTarget: true),
                Is.False, "the assist covers the leader's target");
        });
    }

    [Test]
    public void TheScanOffsetsSpreadTheBotsAcrossOneScanCycle()
    {
        var offsets = new HashSet<int>();
        for (long bot = 1; bot <= 2_000; bot++)
        {
            int offset = GothwaitePvpArenaRules.ScanOffsetMilliseconds(bot);
            Assert.That(offset, Is.InRange(0, AutonomousBattlegroundDriver.ScanMilliseconds - 1));
            offsets.Add(offset);
        }

        Assert.That(offsets.Count, Is.GreaterThan(200), "bots do not all scan on one tick");
    }

    [Test]
    public void TheWalkBackPointIsInsideTheGoalEdgeWhenOneIsLeft()
    {
        TurnOn();
        IReadOnlyList<GothwaitePvpSpot> points = Arena.PatrolPointsFor(1, GothwaitePvpMode.Settings);
        // A bot just beyond its edge, on the line to the nearest patrol point of the high band.
        GothwaitePvpSpot beyond = PatrolPointNamed("GP21");
        double scale = (EdgeOfLowBand + 200) / Arena.CastleDistance(beyond.X, beyond.Y);
        int x = Arena.CastleX + (int)Math.Round((beyond.X - Arena.CastleX) * scale);
        int y = Arena.CastleY + (int)Math.Round((beyond.Y - Arena.CastleY) * scale);

        GothwaitePvpSpot? walkBack = GothwaitePvpArenaRules.WalkBackPoint(Arena, EdgeOfLowBand, points, x, y, _ => true);

        Assert.Multiple(() =>
        {
            Assert.That(walkBack, Is.Not.Null);
            Assert.That(Arena.CastleDistance(walkBack.Value.X, walkBack.Value.Y), Is.LessThanOrEqualTo(EdgeOfLowBand - GothwaitePvpArenaRules.EdgeMargin));
        });
    }

    [Test]
    public void TheNearestUsablePointIsPickedAndAnUnusableOneIsSkipped()
    {
        TurnOn();
        IReadOnlyList<GothwaitePvpSpot> points = Arena.PatrolPointsFor(25, GothwaitePvpMode.Settings);
        GothwaitePvpSpot here = PatrolPointNamed("GP03");

        GothwaitePvpSpot? nearest = GothwaitePvpArenaRules.NearestPoint(Arena, 30_000, points, here.X, here.Y, _ => true);
        GothwaitePvpSpot? skipped = GothwaitePvpArenaRules.NearestPoint(Arena, 30_000, points, here.X, here.Y, point => point != here);

        Assert.Multiple(() =>
        {
            Assert.That(nearest, Is.EqualTo(here), "the point at the bot's feet");
            Assert.That(skipped.HasValue && skipped.Value != here, Is.True, "the refused point is skipped for another");
        });
    }

    [Test]
    public void TheRoamFloorKeepsPatrolGoalsOutOfTheGateRingBuffer()
    {
        TurnOn();
        GothwaitePvpSettings settings = GothwaitePvpMode.Settings;
        int floor = Arena.MinimumRoamDistance(settings.GateRing);
        IReadOnlyList<GothwaitePvpSpot> points = Arena.PatrolPointsFor(25, settings);

        Assert.Multiple(() =>
        {
            Assert.That(floor, Is.EqualTo(4_000 + 1_500 + 500), "the ring's outer edge plus the margin");
            Assert.That(points.Select(point => point.Name), Does.Not.Contain("GP01"), "GP01 (4,564) lies inside the ring buffer");
            Assert.That(points.Select(point => point.Name), Does.Contain("GP03"), "GP03 (6,971) lies beyond it");
            Assert.That(points.All(point => Arena.CastleDistance(point.X, point.Y) > floor), Is.True, "every goal is beyond the floor");
            Assert.That(Arena.PatrolPoints.Select(point => point.Name), Does.Contain("GP01"), "the point stays in the data");
        });
    }

    [Test]
    public void WithoutAGateRingOnlyTheMarginBeyondTheCastleKeepsGoalsOut()
    {
        GothwaitePvpSettings settings = GothwaitePvpSettings.Defaults with { Enabled = true, GateRing = 0 };
        IReadOnlyList<GothwaitePvpSpot> points = Arena.PatrolPointsFor(25, settings);

        Assert.That(points.Select(point => point.Name), Does.Contain("GP01"), "4,564 is beyond 4,500, the castle edge plus the margin");
    }

    [Test]
    public void TheGateRingIsTheCastleCircleWidenedByTheRingSetting()
    {
        const ushort region = 51;
        int castleX = Arena.CastleX, castleY = Arena.CastleY;

        Assert.Multiple(() =>
        {
            Assert.That(Arena.InGateRing(region, castleX + 5_499, castleY, 1_500), Is.True, "just inside 4,000 + 1,500");
            Assert.That(Arena.InGateRing(region, castleX + 5_501, castleY, 1_500), Is.False, "just beyond it");
            Assert.That(Arena.InGateRing(1, castleX, castleY, 1_500), Is.False, "another region is never in this ring");
            Assert.That(Arena.InGateRing(region, castleX + 3_999, castleY, 0), Is.True, "with no ring it is the castle circle");
            Assert.That(Arena.InGateRing(region, castleX + 4_001, castleY, 0), Is.False, "just outside the circle with no ring");
        });
    }

    [Test]
    public void AnOpeningIsRefusedWhileEitherSideStandsInsideTheRing()
    {
        const ushort region = 51;
        const int ring = 1_500;
        int castleX = Arena.CastleX, castleY = Arena.CastleY;
        int inside = Arena.SafeRadius + ring - 1_000, beyond = Arena.SafeRadius + ring + 1_000;

        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpTargetRules.RingAllowsOpening(Arena, ring, region, castleX + inside, castleY, region, castleX + beyond, castleY),
                Is.False, "the bot stands inside the ring");
            Assert.That(GothwaitePvpTargetRules.RingAllowsOpening(Arena, ring, region, castleX + beyond, castleY, region, castleX + inside, castleY),
                Is.False, "the target stands inside the ring");
            Assert.That(GothwaitePvpTargetRules.RingAllowsOpening(Arena, ring, region, castleX + inside, castleY, region, castleX + inside, castleY),
                Is.False, "both stand inside the ring");
            Assert.That(GothwaitePvpTargetRules.RingAllowsOpening(Arena, ring, region, castleX + beyond, castleY, region, castleX + beyond, castleY),
                Is.True, "both stand beyond the ring");
        });
    }

    [Test]
    public void TheFirstPickAfterTheGateIgnoresHeatAndSpreadsOverEveryAllowedPoint()
    {
        TurnOn();
        GothwaitePvpSpot hot = PatrolPointNamed("GP03");
        var points = new[] { hot, PatrolPointNamed("GP05"), PatrolPointNamed("GP06"), PatrolPointNamed("GP12") };
        Func<GothwaitePvpSpot, double> firstWeight = point => GothwaitePvpArenaRules.RoamWeight(true, point == hot ? 50 : 0, 0);
        var random = new Random(17);
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < Draws; i++)
        {
            string name = Pick(points, null, 25, random, firstWeight)!.Value.Name;
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        Assert.Multiple(() =>
        {
            foreach (GothwaitePvpSpot point in points)
                Assert.That(counts.GetValueOrDefault(point.Name) / (double)Draws, Is.EqualTo(0.25).Within(0.02), $"{point.Name} gets its even share");
        });
    }

    [Test]
    public void ALaterPickStillFavoursHeat()
    {
        TurnOn();
        GothwaitePvpSpot hot = PatrolPointNamed("GP03");
        var points = new[] { hot, PatrolPointNamed("GP05") };
        Func<GothwaitePvpSpot, double> laterWeight = point => GothwaitePvpArenaRules.RoamWeight(false, point == hot ? 5 : 0, 0);
        var random = new Random(23);
        int hotPicks = 0;
        for (int i = 0; i < Draws; i++)
            if (Pick(points, null, 25, random, laterWeight) == hot)
                hotPicks++;

        Assert.That(hotPicks / (double)Draws, Is.EqualTo(4.0 / 5.0).Within(0.02), "heat 5 (capped at 3) weighs four times a cold point after the first pick");
    }

    [Test]
    public void GatePileHeatNoLongerPullsEveryGroupBack()
    {
        // Second live run (2026-10-13): hundreds of fights by the gate gave its nearest point a weight near 100 against 1.
        TurnOn();
        GothwaitePvpSpot hot = PatrolPointNamed("GP03");
        var points = new[] { hot, PatrolPointNamed("GP05"), PatrolPointNamed("GP06"), PatrolPointNamed("GP12") };
        Func<GothwaitePvpSpot, double> laterWeight = point => GothwaitePvpArenaRules.RoamWeight(false, point == hot ? 100 : 0, 0);
        var random = new Random(29);
        int hotPicks = 0;
        for (int i = 0; i < Draws; i++)
            if (Pick(points, null, 25, random, laterWeight) == hot)
                hotPicks++;

        Assert.That(hotPicks / (double)Draws, Is.EqualTo(4.0 / 7.0).Within(0.02), "the hot point weighs 4 against 1 for each of the other three");
    }

    [Test]
    public void ClaimedPointsAreShared()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.RoamWeight(true, 0, 0, 0), Is.EqualTo(1.0), "an unclaimed point keeps its weight");
            Assert.That(GothwaitePvpArenaRules.RoamWeight(true, 0, 0, 3), Is.EqualTo(0.25), "three groups there quarter it");
            Assert.That(GothwaitePvpArenaRules.RoamWeight(false, 3, 0, 1), Is.EqualTo(2.0), "heat and claims combine");
            Assert.That(GothwaitePvpArenaRules.RoamWeight(true, 0, 0, -2), Is.EqualTo(1.0), "negative claims count as none");
        });

        TurnOn();
        GothwaitePvpSpot crowded = PatrolPointNamed("GP03");
        GothwaitePvpSpot empty = PatrolPointNamed("GP05");
        var points = new[] { crowded, empty };
        Func<GothwaitePvpSpot, double> weightOf = point => GothwaitePvpArenaRules.RoamWeight(true, 0, 0, point == crowded ? 4 : 0);
        var random = new Random(31);
        int emptyPicks = 0;
        for (int i = 0; i < Draws; i++)
            if (Pick(points, null, 25, random, weightOf) == empty)
                emptyPicks++;

        Assert.That(emptyPicks / (double)Draws, Is.EqualTo(5.0 / 6.0).Within(0.02), "a point four groups already claimed is picked one time in six");
    }

    [Test]
    public void TheOutboundLegEndsOnArrivalOrAfterThreeMinutes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.OutboundOver(arrivedAtGoal: true, now: 1_000, since: 1_000), Is.True, "arrived");
            Assert.That(GothwaitePvpArenaRules.OutboundOver(false, 1_000 + GothwaitePvpArenaRules.OutboundMaxMilliseconds - 1, 1_000),
                Is.False, "still walking within the cap");
            Assert.That(GothwaitePvpArenaRules.OutboundOver(false, 1_000 + GothwaitePvpArenaRules.OutboundMaxMilliseconds, 1_000),
                Is.True, "the cap ran out");
            Assert.That(GothwaitePvpArenaRules.OutboundMaxMilliseconds, Is.EqualTo(180_000));
        });
    }

    [Test]
    public void OnlyARingOrCastleDropSendsTheLeaderOutbound()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.DropSendsOutbound(GothwaitePvpArenaRules.ChaseRing), Is.True);
            Assert.That(GothwaitePvpArenaRules.DropSendsOutbound(GothwaitePvpArenaRules.ChaseCastle), Is.True);
            Assert.That(GothwaitePvpArenaRules.DropSendsOutbound(GothwaitePvpArenaRules.ChaseEdge), Is.False, "edge: nearest point inside");
            Assert.That(GothwaitePvpArenaRules.DropSendsOutbound(GothwaitePvpArenaRules.ChaseSelfEdge), Is.False, "self edge: nearest point inside");
            Assert.That(GothwaitePvpArenaRules.DropSendsOutbound(null), Is.False);
        });
    }

    [Test]
    public void TheScanIsRefusedInsideTheGateRing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.MayScan(due: true, returning: false, outsideOwnEdge: false, isLeader: true,
                leaderHasLegalTarget: false, inGateRing: true), Is.False, "a leader inside the ring opens nothing");
            Assert.That(GothwaitePvpArenaRules.MayScan(due: true, returning: false, outsideOwnEdge: false, isLeader: false,
                leaderHasLegalTarget: false, inGateRing: true), Is.False, "a follower inside the ring opens nothing");
            Assert.That(GothwaitePvpArenaRules.MayScan(due: true, returning: false, outsideOwnEdge: false, isLeader: true,
                leaderHasLegalTarget: false, inGateRing: false), Is.True, "outside the ring the scan goes on");
        });
    }

    [Test]
    public void AFightOpenedOutsideTheRingIsDroppedInsideItUnlessTheTargetAttacked()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.GateRingReason(targetInRing: true, botInRing: false, targetAttackedUs: false), Is.EqualTo("ring"),
                "the target walked into the ring");
            Assert.That(GothwaitePvpArenaRules.GateRingReason(targetInRing: false, botInRing: true, targetAttackedUs: false), Is.EqualTo("ring"),
                "the bot itself is in the ring");
            Assert.That(GothwaitePvpArenaRules.GateRingReason(targetInRing: true, botInRing: true, targetAttackedUs: true), Is.Null,
                "a target that attacked the bot is fought back (self-defence)");
            Assert.That(GothwaitePvpArenaRules.GateRingReason(targetInRing: false, botInRing: false, targetAttackedUs: false), Is.Null,
                "nobody is in the ring");
        });
    }

    [Test]
    public void AGroupHasPassedTheGateWhenItsLeaderIsAtTheExitOrOutsideTheRing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpArenaRules.GateWalkDone(arrivedAtExit: true, outsideGateRing: false), Is.True, "at the exit");
            Assert.That(GothwaitePvpArenaRules.GateWalkDone(arrivedAtExit: false, outsideGateRing: true), Is.True, "already out on the way");
            Assert.That(GothwaitePvpArenaRules.GateWalkDone(arrivedAtExit: false, outsideGateRing: false), Is.False, "still walking inside the ring");
        });
    }

    [Test]
    public void TheArenaLogLinesKeepTheOwnersGrepFormat()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpLines.Chase("Ana", "Bo", "castle"),
                Is.EqualTo("GOTH_PVP_CHASE_LIMIT bot=\"Ana\" target=\"Bo\" reason=castle"));
            Assert.That(GothwaitePvpLines.Stuck("Ana", "1,2,3", "repick"),
                Is.EqualTo("GOTH_PVP_STUCK bot=\"Ana\" goal=1,2,3 action=repick"));
            Assert.That(GothwaitePvpLines.Edge("Ana", 20500, 20000),
                Is.EqualTo("GOTH_PVP_EDGE bot=\"Ana\" distance=20500 edge=20000"));
            Assert.That(GothwaitePvpLines.GroupLeader("7", "Bo", "Ana", "leader_lost"),
                Is.EqualTo("GOTH_PVP_GROUP_LEADER group=7 leader=\"Bo\" previous=\"Ana\" reason=leader_lost"));
            Assert.That(GothwaitePvpLines.Return("Ana", "low_health", "health=30 power=10"),
                Is.EqualTo("GOTH_PVP_RETURN bot=\"Ana\" reason=low_health health=30 power=10"));
            Assert.That(GothwaitePvpLines.Outbound("Ana", "GP07", 10637, "gate"),
                Is.EqualTo("GOTH_PVP_OUTBOUND bot=\"Ana\" spot=GP07 distance=10637 reason=gate"));
            Assert.That(GothwaitePvpLines.OutboundEnd("Ana", "arrived", 10512),
                Is.EqualTo("GOTH_PVP_OUTBOUND_END bot=\"Ana\" reason=arrived distance=10512"));
            Assert.That(GothwaitePvpLines.Spot(null), Is.EqualTo("none"));
            Assert.That(GothwaitePvpLines.Spot(new Point3D(1, 2, 3)), Is.EqualTo("1,2,3"));
        });
    }
}
