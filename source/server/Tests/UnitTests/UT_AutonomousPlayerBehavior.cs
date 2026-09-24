using System;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_AutonomousPlayerBehavior
{
    [Test]
    public void HunterDangerAndGreyRulesKeepLowLevelTargetsRare()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousPlayerBehavior.HunterPatrolFactor(AutonomousLevelingDanger.Mild), Is.LessThan(1));
            Assert.That(AutonomousPlayerBehavior.HunterPatrolFactor(AutonomousLevelingDanger.FullCamlann), Is.GreaterThan(1));
            Assert.That(AutonomousPlayerBehavior.GreyEngageChance(AutonomousPlayerType.Hunter, 50, 10,
                50, AutonomousLevelingDanger.Authentic, 3), Is.Zero);
            Assert.That(AutonomousPlayerBehavior.GreyEngageChance(AutonomousPlayerType.Hunter, 50, 10,
                50, AutonomousLevelingDanger.FullCamlann, 3), Is.EqualTo(1));
            Assert.That(AutonomousPlayerBehavior.GreyEngageChance(AutonomousPlayerType.Roamer, 50, 10,
                80, AutonomousLevelingDanger.FullCamlann, 3), Is.Zero);
            Assert.That(AutonomousPlayerBehavior.GreyEngageChance(AutonomousPlayerType.Hunter, 20, 12,
                50, AutonomousLevelingDanger.Mild, 3), Is.Zero);
            Assert.That(AutonomousPlayerBehavior.GreyEngageChance(AutonomousPlayerType.Hunter, 20, 12,
                85, AutonomousLevelingDanger.FullCamlann, 0), Is.Zero);
        });
        Assert.That(AutonomousPvpOpportunityPolicy.IsHunterHuntArea(35, new[] { 32, 34 },
            false, false, false, true), Is.True);
        Assert.That(AutonomousPvpOpportunityPolicy.IsHunterHuntArea(35, new[] { 32 },
            true, false, false, true), Is.False);
        Assert.That(AutonomousPvpOpportunityPolicy.HunterPatrolWeight(2, true),
            Is.GreaterThan(AutonomousPvpOpportunityPolicy.HunterPatrolWeight(0, false)));
    }

    [Test]
    public void RvrPartiesFollowTypeAndLevel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousPlayerBehavior.ChooseRvrGroupSize(AutonomousPlayerType.Hunter, 40,
                8, .8, TimeSpan.FromMinutes(20)), Is.EqualTo(4));
            Assert.That(AutonomousPlayerBehavior.ChooseRvrGroupSize(AutonomousPlayerType.Roamer, 40,
                7, .8, TimeSpan.FromMinutes(5)), Is.EqualTo(1));
            Assert.That(AutonomousPlayerBehavior.ChooseRvrGroupSize(AutonomousPlayerType.Roamer, 40,
                7, .8, TimeSpan.FromMinutes(11)), Is.EqualTo(7));
            Assert.That(AutonomousPlayerBehavior.ChooseRvrGroupSize(AutonomousPlayerType.Roamer, 40,
                8, .1, TimeSpan.Zero), Is.EqualTo(8));
            Assert.That(AutonomousPlayerBehavior.ChooseRvrGroupSize(AutonomousPlayerType.KeepWarrior, 40,
                3, .8, TimeSpan.FromMinutes(20)), Is.EqualTo(1));
            Assert.That(AutonomousPlayerBehavior.CanStartCampaign(AutonomousPlayerType.KeepWarrior, 35, 4), Is.True);
            Assert.That(AutonomousPlayerBehavior.CanStartCampaign(AutonomousPlayerType.Roamer, 50, 8), Is.False);
            Assert.That(AutonomousPlayerBehavior.NextLoopIndex(8, 7, 42), Is.Zero);
            Assert.That(AutonomousPlayerBehavior.NextLoopIndex(8, 3, 42), Is.EqualTo(4));
        });
    }

    [Test]
    public void HybridRoamsAtPrimeTimeAndCasualsTakeLongerBreaks()
    {
        DateTime prime = new DateTime(2026, 9, 24, 20, 0, 0, DateTimeKind.Local).ToUniversalTime();
        DateTime offPeak = new DateTime(2026, 9, 24, 10, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var hybrid = new OfflineWorldBotRecord { PlayerType = "Hybrid", Level = 40,
            Aggression = 50, RiskTolerance = 50, Sociability = 50 };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousActivityScheduler.Choose(hybrid, prime, .2), Is.EqualTo(eAutonomousObjectiveKind.RvR));
            Assert.That(AutonomousActivityScheduler.Choose(hybrid, offPeak, .2), Is.Not.EqualTo(eAutonomousObjectiveKind.RvR));
            Assert.That(AutonomousPlayerBehavior.TownBreakChance(AutonomousPlayerType.Casual, 50),
                Is.GreaterThan(AutonomousPlayerBehavior.TownBreakChance(AutonomousPlayerType.Leveler, 50)));
        });
    }

    [Test]
    public void LevelFiftyGearWallCountsArmorAndKeepsFarmingUntilRepaired()
    {
        Assert.That(AutonomousActivityScheduler.IsUndergeared(50, 50,
            new[] { 50, 50, 10, 10, 0, 0 }), Is.True);
        Assert.That(AutonomousActivityScheduler.IsUndergeared(50, 50,
            new[] { 50, 50, 50, 50, 10, 0 }), Is.False);
        Assert.That(AutonomousActivityScheduler.IsUndergeared(50, 20,
            new[] { 50, 50, 50, 50, 50, 50 }), Is.True);
    }
}
