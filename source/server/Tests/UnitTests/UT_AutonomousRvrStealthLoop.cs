using System;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// The stealther loop of autonomous RvR assassins (P9): open on a soft target,
/// one kill, break off when friends answer or health drops, hide again after
/// the class restealth delay.
/// </summary>
[TestFixture]
public sealed class UT_AutonomousRvrStealthLoop
{
    private static readonly RvrClassTraits Cleric = AutonomousRvrDoctrine.TraitsOf(eCharacterClass.Cleric);
    private static readonly RvrClassTraits Wizard = AutonomousRvrDoctrine.TraitsOf(eCharacterClass.Wizard);
    private static readonly RvrClassTraits Armsman = AutonomousRvrDoctrine.TraitsOf(eCharacterClass.Armsman);
    private static readonly RvrClassTraits Thane = AutonomousRvrDoctrine.TraitsOf(eCharacterClass.Thane);

    private static RvrStealthTargetView View(RvrClassTraits traits, int party = 1, double nearestFriend = double.PositiveInfinity,
        bool edge = false, bool resting = false, bool last = false, int health = 100, double distance = 800) =>
        new(traits, distance, health, party, nearestFriend, edge, resting, last);

    [Test]
    public void CastersAndHealersAreSoftTanksAndMeleeCastersAreNot()
    {
        Assert.That(AutonomousRvrStealthLoop.IsSoft(Cleric), Is.True);
        Assert.That(AutonomousRvrStealthLoop.IsSoft(Wizard), Is.True);
        Assert.That(AutonomousRvrStealthLoop.IsSoft(Armsman), Is.False);
        Assert.That(AutonomousRvrStealthLoop.IsSoft(Thane), Is.False);
    }

    [Test]
    public void ReasonsFollowWhatTheStealtherSees()
    {
        Assert.That(AutonomousRvrStealthLoop.Classify(View(Armsman)), Is.EqualTo(RvrStealthOpenReason.Lone));
        Assert.That(AutonomousRvrStealthLoop.Classify(View(Cleric, 8, 300, edge: true)), Is.EqualTo(RvrStealthOpenReason.BackLine));
        Assert.That(AutonomousRvrStealthLoop.Classify(View(Armsman, 8, 200, edge: true, resting: true)), Is.EqualTo(RvrStealthOpenReason.Resting));
        Assert.That(AutonomousRvrStealthLoop.Classify(View(Armsman, 3, 200, resting: true)), Is.EqualTo(RvrStealthOpenReason.Resting));
        // Resting in the middle of a full group is still the middle of a full group.
        Assert.That(AutonomousRvrStealthLoop.Classify(View(Armsman, 8, 200, resting: true)), Is.EqualTo(RvrStealthOpenReason.None));
        Assert.That(AutonomousRvrStealthLoop.Classify(View(Armsman, 5, 400, last: true)), Is.EqualTo(RvrStealthOpenReason.LastInLine));
        Assert.That(AutonomousRvrStealthLoop.Classify(View(Armsman, 5, 1_300)), Is.EqualTo(RvrStealthOpenReason.Straggler));
        Assert.That(AutonomousRvrStealthLoop.Classify(View(Armsman, 5, 200, health: 20)), Is.EqualTo(RvrStealthOpenReason.Straggler));
        // A friend farther than 1,500 away leaves a lone walker.
        Assert.That(AutonomousRvrStealthLoop.Classify(View(Wizard, 3, 1_600)), Is.EqualTo(RvrStealthOpenReason.Lone));
    }

    [Test]
    public void NeverOpensOnAFullGroupsTankOrACasterInTheMiddle()
    {
        Assert.That(AutonomousRvrStealthLoop.Classify(View(Armsman, 8, 150)), Is.EqualTo(RvrStealthOpenReason.None));
        Assert.That(AutonomousRvrStealthLoop.Classify(View(Cleric, 8, 150, edge: false)), Is.EqualTo(RvrStealthOpenReason.None));
        Assert.That(AutonomousRvrStealthLoop.Score(View(Armsman, 8, 150)), Is.Zero);
        Assert.That(AutonomousRvrStealthLoop.Choose([View(Armsman, 8, 150), View(Cleric, 8, 150)]), Is.EqualTo(-1));
    }

    [Test]
    public void SoftTargetsComeFirstThenTheSlowestToRiseThenLoneThenLastInLine()
    {
        RvrStealthTargetView backLineHealer = View(Cleric, 8, 300, edge: true);
        RvrStealthTargetView restingTank = View(Armsman, 8, 200, edge: true, resting: true);
        RvrStealthTargetView loneTank = View(Armsman);
        RvrStealthTargetView lastTank = View(Armsman, 5, 400, last: true);
        RvrStealthTargetView stragglerTank = View(Armsman, 5, 1_300);
        double[] scores =
        [
            AutonomousRvrStealthLoop.Score(backLineHealer), AutonomousRvrStealthLoop.Score(restingTank),
            AutonomousRvrStealthLoop.Score(loneTank), AutonomousRvrStealthLoop.Score(lastTank),
            AutonomousRvrStealthLoop.Score(stragglerTank),
        ];
        for (int index = 1; index < scores.Length; index++)
            Assert.That(scores[index - 1], Is.GreaterThan(scores[index]), $"rank {index}");
        Assert.That(AutonomousRvrStealthLoop.Choose([loneTank, stragglerTank, backLineHealer, lastTank]), Is.EqualTo(2));
        // A lone caster beats a lone tank.
        Assert.That(AutonomousRvrStealthLoop.Choose([View(Armsman), View(Wizard)]), Is.EqualTo(1));
    }

    [Test]
    public void BreaksOffAfterOneKillWhenFriendsAnswerOrBelowFortyPercent()
    {
        Assert.That(AutonomousRvrStealthLoop.BreakReason(true, 0, 90), Is.EqualTo(RvrStealthBreakReason.Kill));
        Assert.That(AutonomousRvrStealthLoop.BreakReason(false, 1, 90), Is.EqualTo(RvrStealthBreakReason.Friends));
        Assert.That(AutonomousRvrStealthLoop.BreakReason(false, 0, 39), Is.EqualTo(RvrStealthBreakReason.LowHp));
        Assert.That(AutonomousRvrStealthLoop.BreakReason(false, 2, 39), Is.EqualTo(RvrStealthBreakReason.LowHp));
        Assert.That(AutonomousRvrStealthLoop.BreakReason(false, 0, 40), Is.EqualTo(RvrStealthBreakReason.None));
        // Just after a break a chased stealther fights instead of looping: no
        // second friends or low-health break inside the 30 s cooldown.
        Assert.That(AutonomousRvrStealthLoop.BreakReason(false, 1, 90, recentBreak: true), Is.EqualTo(RvrStealthBreakReason.None));
        Assert.That(AutonomousRvrStealthLoop.BreakReason(false, 1, 30, recentBreak: true), Is.EqualTo(RvrStealthBreakReason.None));
        Assert.That(AutonomousRvrStealthLoop.BreakReason(true, 1, 30, recentBreak: true), Is.EqualTo(RvrStealthBreakReason.Kill));
        // After the cooldown, a low-health run once more only when nobody stands in melee range.
        Assert.That(AutonomousRvrStealthLoop.BreakReason(false, 0, 30, brokeBefore: true, attackerInMelee: true),
            Is.EqualTo(RvrStealthBreakReason.None));
        Assert.That(AutonomousRvrStealthLoop.BreakReason(false, 0, 30, brokeBefore: true, attackerInMelee: false),
            Is.EqualTo(RvrStealthBreakReason.LowHp));
        Assert.That(AutonomousRvrStealthLoop.BreakReason(false, 0, 30, brokeBefore: false, attackerInMelee: true),
            Is.EqualTo(RvrStealthBreakReason.LowHp), "The first break may leave a melee.");
    }

    [Test]
    public void AGroupedStealtherDropsStealthToKeepUpWithAVisibleWalkingLeader()
    {
        Assert.That(AutonomousRvrStealthLoop.ShouldTrailVisible(true, false, true, 450), Is.True);
        Assert.That(AutonomousRvrStealthLoop.ShouldTrailVisible(true, false, true, 250), Is.False, "Close behind: keep stealth.");
        Assert.That(AutonomousRvrStealthLoop.ShouldTrailVisible(true, false, false, 900), Is.False, "Leader stands: keep stealth.");
        Assert.That(AutonomousRvrStealthLoop.ShouldTrailVisible(true, true, true, 900), Is.False, "Leader hidden too.");
        Assert.That(AutonomousRvrStealthLoop.ShouldTrailVisible(false, false, true, 900), Is.False, "Solo or leading.");
    }

    [TestCase(eCharacterClass.Infiltrator, 0, 6_000)]
    [TestCase(eCharacterClass.Shadowblade, 0, 6_000)]
    [TestCase(eCharacterClass.Nightshade, 0, 6_000)]
    [TestCase(eCharacterClass.Scout, 0, 10_000)]
    [TestCase(eCharacterClass.Hunter, 0, 10_000)]
    // The server's 10 s combat window is the floor: a bot never hides sooner than a player could.
    [TestCase(eCharacterClass.Infiltrator, 10_000, 10_000)]
    [TestCase(eCharacterClass.Ranger, 10_000, 10_000)]
    public void RestealthDelayByClassNeverBeatsTheServerRule(eCharacterClass characterClass, int serverWindow, int expected)
    {
        Assert.That(AutonomousRvrStealthLoop.RestealthDelayMilliseconds(characterClass, serverWindow), Is.EqualTo(expected));
    }

    [Test]
    public void TheBreakRunGoesThreeToSixHundredAwayAndTurnsAside()
    {
        Assert.That(AutonomousRvrStealthLoop.BreakRunDistance(0), Is.EqualTo(300));
        Assert.That(AutonomousRvrStealthLoop.BreakRunDistance(1), Is.EqualTo(600));
        Vector3 here = new(10_000, 10_000, 0);
        Vector3 threat = new(10_000, 9_000, 0);
        foreach (double turn in new[] { 0.0, 0.25, 0.75, 1.0 })
        {
            Vector3 point = AutonomousRvrStealthLoop.BreakPoint(here, threat, 0.5, turn);
            float run = Vector2.Distance(new(here.X, here.Y), new(point.X, point.Y));
            Assert.That(run, Is.EqualTo(450).Within(0.5));
            // Away from the threat, but not straight back along the line.
            Assert.That(point.Y, Is.GreaterThan(here.Y));
            double angle = Math.Abs(Math.Atan2(point.X - here.X, point.Y - here.Y)) * 180 / Math.PI;
            Assert.That(angle, Is.InRange(19.9, 60.1), $"turn {turn}");
        }
    }

    [Test]
    public void LogLabelsMatchTheDocumentedValues()
    {
        Assert.That(AutonomousRvrStealthLoop.Label(RvrStealthOpenReason.BackLine), Is.EqualTo("back_line"));
        Assert.That(AutonomousRvrStealthLoop.Label(RvrStealthOpenReason.LastInLine), Is.EqualTo("last_in_line"));
        Assert.That(AutonomousRvrStealthLoop.Label(RvrStealthBreakReason.LowHp), Is.EqualTo("low_hp"));
        Assert.That(AutonomousRvrStealthLoop.Label(RvrStealthBreakReason.Friends), Is.EqualTo("friends"));
    }
}
