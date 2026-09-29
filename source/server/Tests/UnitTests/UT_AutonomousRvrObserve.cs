using System;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Wave 3 of livelier RvR bots (C5, principle P2): a group that sees
/// a fight holds outside caster reach and decides on the leader's call, with
/// the counted add rule ("wait until at least THREE of the enemy group are
/// down"), push on CC, stragglers, and leaving when seen or charged.</summary>
[TestFixture]
public sealed class UT_AutonomousRvrObserve
{
    private static readonly RvrLeaderTraits Ordinary = new(50, 55, 50);
    private static readonly RvrLeaderTraits Bold = new(70, 60, 50);
    private static readonly RvrLeaderTraits Aggressive = new(60, 55, 50);
    private static readonly RvrLeaderTraits Cautious = new(50, 30, 50);

    /// <summary>An 8-man watching an engaged 8-man, nothing decided yet.</summary>
    private static RvrObserveSituation Watching(RvrLeaderTraits traits, int down = 0, int ours = 6,
        RvrDoctrineKind kind = RvrDoctrineKind.AssistTrain) => new(
        kind, traits, ours, TheirSize: 8, TheirAlive: 8 - down, TheirDown: down,
        WatchedEngaged: true, AppetiteAccepts: false, HasMezzer: false, StragglerAvailable: false,
        WatchedClosingMilliseconds: 0, NewPartyNearOrBehind: false, Seen: false, SeenAccepted: false,
        ElapsedMilliseconds: 3_000, CapSeconds: 60, ImperfectionRoll: 0.9);

    private static RvrObserveTrigger Trigger(int parties = 2, bool fight = false) => new(
        true, RvrDoctrineKind.SmallMan, parties, fight, false, false, false, false, false, false, false, false);

    [Test]
    public void AddThresholdIsThreeOfEightTwoForBoldOneWhenClearlyBigger()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.AddThreshold(8, 8, 6, 50), Is.EqualTo(3));
            Assert.That(AutonomousRvrObserve.AddThreshold(4, 4, 3, 50), Is.EqualTo(2), "ceil(3/8 of 4)");
            Assert.That(AutonomousRvrObserve.AddThreshold(2, 2, 1, 50), Is.EqualTo(1));
            Assert.That(AutonomousRvrObserve.AddThreshold(8, 8, 6, 70), Is.EqualTo(2), "bold leader");
            Assert.That(AutonomousRvrObserve.AddThreshold(2, 2, 1, 70), Is.EqualTo(1), "bold is never stricter");
            Assert.That(AutonomousRvrObserve.AddThreshold(8, 4, 6, 50), Is.EqualTo(1), "6 >= 1.5 x 4 alive");
            Assert.That(AutonomousRvrObserve.AddThreshold(8, 5, 6, 50), Is.EqualTo(3), "6 < 1.5 x 5");
        });
    }

    [Test]
    public void OrdinaryLeaderAddsOnlyWhenThreeAreDown()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.Decide(Watching(Ordinary, down: 2)), Is.EqualTo(RvrObserveDecision.Wait));
            Assert.That(AutonomousRvrObserve.Decide(Watching(Ordinary, down: 3)), Is.EqualTo(RvrObserveDecision.ThirdParty));
            Assert.That(AutonomousRvrObserve.Decide(Watching(Bold, down: 1)), Is.EqualTo(RvrObserveDecision.Wait));
            Assert.That(AutonomousRvrObserve.Decide(Watching(Bold, down: 2)), Is.EqualTo(RvrObserveDecision.ThirdParty));
        });
    }

    [Test]
    public void BiggerGroupAddsAtOneDownButNeverIntoSurvivorsTwiceItsSize()
    {
        RvrObserveSituation big = Watching(Ordinary, down: 0, ours: 8) with { TheirSize = 6, TheirAlive = 5, TheirDown = 1 };
        RvrObserveSituation duo = Watching(Bold, down: 3, ours: 2);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.Decide(big), Is.EqualTo(RvrObserveDecision.ThirdParty), "8 >= 1.5 x 5");
            Assert.That(AutonomousRvrObserve.Decide(duo), Is.EqualTo(RvrObserveDecision.Wait), "5 survivors > 2 x 2");
        });
    }

    [Test]
    public void AppetiteEngagesAPartyWeWouldFightBusyOrNot()
    {
        RvrObserveSituation accepted = Watching(Ordinary) with { AppetiteAccepts = true };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.Decide(accepted), Is.EqualTo(RvrObserveDecision.ThirdParty));
            Assert.That(AutonomousRvrObserve.Decide(accepted with { WatchedEngaged = false }),
                Is.EqualTo(RvrObserveDecision.ThirdParty), "observing is no reason to spare a party we would fight");
            Assert.That(AutonomousRvrObserve.Decide(accepted with { MayEngage = false }),
                Is.EqualTo(RvrObserveDecision.Wait));
        });
    }

    [Test]
    public void PushOnCrowdControlOnlyForAggressiveLeadersWithAMezzer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.Decide(Watching(Aggressive) with { HasMezzer = true }),
                Is.EqualTo(RvrObserveDecision.PushCc));
            Assert.That(AutonomousRvrObserve.Decide(Watching(Ordinary) with { HasMezzer = true }),
                Is.EqualTo(RvrObserveDecision.Wait), "Aggression 50 waits for the count");
            Assert.That(AutonomousRvrObserve.Decide(Watching(Aggressive)), Is.EqualTo(RvrObserveDecision.Wait),
                "no mezzer");
            Assert.That(AutonomousRvrObserve.Decide(Watching(Aggressive) with { HasMezzer = true, WatchedEngaged = false }),
                Is.EqualTo(RvrObserveDecision.Wait), "nothing to push into");
        });
    }

    [Test]
    public void StragglersAreForSmallDoctrinesOnly()
    {
        RvrObserveSituation straggler = Watching(Ordinary, down: 3) with { StragglerAvailable = true };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.IsSmallDoctrine(RvrDoctrineKind.CasterDuo), Is.True);
            Assert.That(AutonomousRvrObserve.IsSmallDoctrine(RvrDoctrineKind.AssistTrain), Is.False);
            Assert.That(AutonomousRvrObserve.Decide(straggler with { TheirDown = 0, TheirAlive = 8 }), Is.EqualTo(RvrObserveDecision.Wait),
                "an assist train ignores stragglers");
            Assert.That(AutonomousRvrObserve.Decide(straggler with { Kind = RvrDoctrineKind.SmallMan, TheirDown = 0, TheirAlive = 8 }),
                Is.EqualTo(RvrObserveDecision.Straggler));
            Assert.That(AutonomousRvrObserve.Decide(straggler with { Kind = RvrDoctrineKind.SmallMan }),
                Is.EqualTo(RvrObserveDecision.ThirdParty), "a small-man still adds at the count first");
            Assert.That(AutonomousRvrObserve.Decide(straggler with { Kind = RvrDoctrineKind.StealthPack }),
                Is.EqualTo(RvrObserveDecision.Straggler), "stealthers prefer the straggler");
        });
    }

    [Test]
    public void StragglerRuleIsLowHealthOrFarFromItsParty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.IsStraggler(29, 0), Is.True);
            Assert.That(AutonomousRvrObserve.IsStraggler(30, 1_200), Is.False);
            Assert.That(AutonomousRvrObserve.IsStraggler(100, 1_201), Is.True);
        });
    }

    [Test]
    public void LeavesWhenChargedByABiggerPartyFlankedOrSeen()
    {
        RvrObserveSituation idle = Watching(Ordinary) with { WatchedEngaged = false };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.Decide(idle with { WatchedClosingMilliseconds = 6_000 }),
                Is.EqualTo(RvrObserveDecision.Leave));
            Assert.That(AutonomousRvrObserve.Decide(idle with { WatchedClosingMilliseconds = 3_000 }),
                Is.EqualTo(RvrObserveDecision.Wait), "not yet 6 s of closing");
            Assert.That(AutonomousRvrObserve.Decide(idle with { WatchedClosingMilliseconds = 6_000, AppetiteAccepts = true }),
                Is.EqualTo(RvrObserveDecision.ThirdParty), "a party we would fight is met, not fled");
            Assert.That(AutonomousRvrObserve.Decide(Watching(Ordinary, down: 5) with { NewPartyNearOrBehind = true }),
                Is.EqualTo(RvrObserveDecision.Leave), "a third party behind beats the count");
            Assert.That(AutonomousRvrObserve.Decide(idle with { Seen = true }), Is.EqualTo(RvrObserveDecision.Leave));
            Assert.That(AutonomousRvrObserve.Decide(idle with { Seen = true, SeenAccepted = true }),
                Is.EqualTo(RvrObserveDecision.Wait));
        });
    }

    [Test]
    public void HoldIsCappedByPatienceThenCautiousLeaveAndOthersRoamOn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.CapSeconds(0, 0.5), Is.EqualTo(36));
            Assert.That(AutonomousRvrObserve.CapSeconds(50, 0.5), Is.EqualTo(66));
            Assert.That(AutonomousRvrObserve.CapSeconds(100, 0.5), Is.EqualTo(96));
            Assert.That(AutonomousRvrObserve.CapSeconds(100, 1), Is.EqualTo(110));
            Assert.That(AutonomousRvrObserve.CapSeconds(0, 0), Is.EqualTo(31));
            Assert.That(AutonomousRvrObserve.CapSeconds(500, 5), Is.LessThanOrEqualTo(AutonomousRvrObserve.HardCapSeconds));
        });
        RvrObserveSituation idle = Watching(Ordinary) with { WatchedEngaged = false, CapSeconds = 66 };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.Decide(idle with { ElapsedMilliseconds = 65_000 }), Is.EqualTo(RvrObserveDecision.Wait));
            Assert.That(AutonomousRvrObserve.Decide(idle with { ElapsedMilliseconds = 66_000 }), Is.EqualTo(RvrObserveDecision.RoamOn));
            Assert.That(AutonomousRvrObserve.Decide(idle with { Traits = Cautious, ElapsedMilliseconds = 66_000 }),
                Is.EqualTo(RvrObserveDecision.Leave));
        });
    }

    [Test]
    public void ImperfectionMakesDaringLeadersAddEarlyAndCarefulOnesLeaveEarly()
    {
        RvrObserveSituation early = Watching(Bold) with { ImperfectionRoll = 0.10 };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.Decide(early), Is.EqualTo(RvrObserveDecision.ThirdParty), "0 down, added anyway");
            Assert.That(AutonomousRvrObserve.Decide(early with { Traits = Cautious }), Is.EqualTo(RvrObserveDecision.Leave));
            Assert.That(AutonomousRvrObserve.Decide(early with { WatchedEngaged = false }), Is.EqualTo(RvrObserveDecision.Wait),
                "nothing to add on");
            Assert.That(AutonomousRvrObserve.Decide(early with { ImperfectionRoll = 0.15 }), Is.EqualTo(RvrObserveDecision.Wait),
                "85 % of holds follow the rules");
        });
    }

    [Test]
    public void NoEngageDecisionWhenTheGroupMayNotHuntAndAnEmptySceneRoamsOn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.Decide(Watching(Ordinary, down: 5) with { MayEngage = false }),
                Is.EqualTo(RvrObserveDecision.Wait));
            Assert.That(AutonomousRvrObserve.Decide(Watching(Ordinary) with { SceneGone = true }),
                Is.EqualTo(RvrObserveDecision.RoamOn));
        });
    }

    [Test]
    public void TriggerNeedsTwoPartiesOrARunningFightAndAFreeGroup()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.ShouldObserve(Trigger()), Is.True);
            Assert.That(AutonomousRvrObserve.ShouldObserve(Trigger(parties: 1)), Is.False, "one idle party: plain appetite");
            Assert.That(AutonomousRvrObserve.ShouldObserve(Trigger(parties: 1, fight: true)), Is.True);
            Assert.That(AutonomousRvrObserve.ShouldObserve(Trigger(parties: 0, fight: true)), Is.False, "heat without hostiles");
            Assert.That(AutonomousRvrObserve.ShouldObserve(Trigger() with { DoctrineApplies = false }), Is.False);
            Assert.That(AutonomousRvrObserve.ShouldObserve(Trigger() with { MemberInCombat = true }), Is.False);
            Assert.That(AutonomousRvrObserve.ShouldObserve(Trigger() with { BattleForce = true }), Is.False);
            Assert.That(AutonomousRvrObserve.ShouldObserve(Trigger() with { RelicInGroup = true }), Is.False);
            Assert.That(AutonomousRvrObserve.ShouldObserve(Trigger() with { SiegeEvent = true }), Is.False);
            Assert.That(AutonomousRvrObserve.ShouldObserve(Trigger() with { Departing = true }), Is.False);
            Assert.That(AutonomousRvrObserve.ShouldObserve(Trigger() with { Retreating = true }), Is.False);
            Assert.That(AutonomousRvrObserve.ShouldObserve(Trigger() with { Pausing = true }), Is.False);
            Assert.That(AutonomousRvrObserve.ShouldObserve(Trigger() with { CoolingDown = true }), Is.False);
            Assert.That(AutonomousRvrObserve.ShouldObserve(Trigger() with { Kind = RvrDoctrineKind.KeepRaid }), Is.False);
        });
    }

    [Test]
    public void StepBackEndsOutsideCasterReach()
    {
        Vector3 here = new(10_000, 10_000, 0);
        Vector3? back = AutonomousRvrObserve.StepBackPoint(here, new(11_000, 10_000, 0));
        Assert.Multiple(() =>
        {
            Assert.That(back.HasValue, Is.True);
            Assert.That(back.Value.X, Is.EqualTo(8_650).Within(0.5), "2,350 from the hostile, straight away");
            Assert.That(back.Value.Y, Is.EqualTo(10_000).Within(0.5));
            Assert.That(AutonomousRvrObserve.StepBackPoint(here, new(12_300, 10_000, 0)), Is.Null, "already 2,300 away");
            Assert.That(AutonomousRvrObserve.StepBackPoint(here, new(11_000, 10_000, 0), 0)!.Value.X,
                Is.EqualTo(8_800).Within(0.5), "margin 0: exactly 2,200");
            Assert.That(AutonomousRvrObserve.StepBackPoint(here, new(11_000, 10_000, 0), 900)!.Value.X,
                Is.EqualTo(8_400).Within(0.5), "margin clamped to 400");
        });
    }

    [Test]
    public void GeometryHelpers()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.PassesNear(new(0, 0), new(10_000, 0), new(5_000, 2_000), 2_200), Is.True);
            Assert.That(AutonomousRvrObserve.PassesNear(new(0, 0), new(10_000, 0), new(5_000, 2_500), 2_200), Is.False);
            Assert.That(AutonomousRvrObserve.PassesNear(new(0, 0), new(10_000, 0), new(12_000, 0), 2_200), Is.True,
                "the destination end counts");
            Assert.That(AutonomousRvrObserve.IsNearOrBehind(new(0, 0), new(4_000, 0), new(0, 2_000)), Is.True, "near");
            Assert.That(AutonomousRvrObserve.IsNearOrBehind(new(0, 0), new(4_000, 0), new(-4_000, 500)), Is.True, "behind");
            Assert.That(AutonomousRvrObserve.IsNearOrBehind(new(0, 0), new(4_000, 0), new(4_500, 1_000)), Is.False,
                "joins the fight in front");
        });
    }

    [Test]
    public void HeatAgeAndLabels()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrHeat.IsYoungerThan(new(Vector3.Zero, 1 - 30 / 720d), TimeSpan.FromSeconds(60)), Is.True);
            Assert.That(AutonomousRvrHeat.IsYoungerThan(new(Vector3.Zero, 1 - 90 / 720d), TimeSpan.FromSeconds(60)), Is.False);
            Assert.That(AutonomousRvrObserve.Label(RvrObserveDecision.ThirdParty), Is.EqualTo("third_party"));
            Assert.That(AutonomousRvrObserve.Label(RvrObserveDecision.PushCc), Is.EqualTo("push_cc"));
            Assert.That(AutonomousRvrObserve.Label(RvrObserveDecision.RoamOn), Is.EqualTo("roam_on"));
            Assert.That(AutonomousRvrObserve.Label(RvrObserveDecision.Attacked), Is.EqualTo("attacked"));
        });
    }

    [Test]
    public void OnlyGroupsOrFightingEnemiesCountAsParties()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.Qualifies(1, false), Is.False, "a lone enemy walking by");
            Assert.That(AutonomousRvrObserve.Qualifies(1, true), Is.True, "a soloer fighting someone");
            Assert.That(AutonomousRvrObserve.Qualifies(2, false), Is.True);
        });
    }

    [Test]
    public void OurOwnFightHeatIsNotAFightToWatch()
    {
        Vector2 heat = new(10_000, 10_000);
        Vector2[] parties = [new(10_800, 10_000)];
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.HeatIsTheirs(heat, parties, null), Is.True);
            Assert.That(AutonomousRvrObserve.HeatIsTheirs(heat, parties, new Vector2(10_500, 10_500)), Is.False,
                "within 1,500 of our own recent fight");
            Assert.That(AutonomousRvrObserve.HeatIsTheirs(heat, parties, new Vector2(13_000, 10_000)), Is.True);
            Assert.That(AutonomousRvrObserve.HeatIsTheirs(heat, [new Vector2(12_000, 10_000)], null), Is.False,
                "heat must lie at a sighted party");
        });
    }

    [Test]
    public void OnlyAThreatLeaveGoesIntoTheDangerMemory()
    {
        RvrObserveSituation idle = Watching(Ordinary) with { WatchedEngaged = false };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrObserve.IsThreatLeave(idle with { Seen = true }), Is.True);
            Assert.That(AutonomousRvrObserve.IsThreatLeave(idle with { NewPartyNearOrBehind = true }), Is.True);
            Assert.That(AutonomousRvrObserve.IsThreatLeave(idle with { WatchedClosingMilliseconds = 6_000 }), Is.True);
            RvrObserveSituation early = idle with { Traits = Cautious, ImperfectionRoll = 0.05 };
            Assert.That(AutonomousRvrObserve.Decide(early), Is.EqualTo(RvrObserveDecision.Leave));
            Assert.That(AutonomousRvrObserve.IsThreatLeave(early), Is.False, "the imperfection leave saw no fight");
            RvrObserveSituation capped = idle with { Traits = Cautious, ElapsedMilliseconds = 60_000 };
            Assert.That(AutonomousRvrObserve.Decide(capped), Is.EqualTo(RvrObserveDecision.Leave));
            Assert.That(AutonomousRvrObserve.IsThreatLeave(capped), Is.False);
        });
    }
}
