using System;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_AutonomousActivityScheduler
{
    [Test]
    public void IdentityMigrationIsDeterministicAndPreservesProgress()
    {
        var record = new OfflineWorldBotRecord { BotId = 42, GuildId = "guild-a", ClassId = 19,
            Level = 37, Experience = 123456, MoneyCopper = 98765 };
        Assert.That(AutonomousBotIdentity.Ensure(record, AutonomousGuildCharter.Hunting), Is.True);
        string type = record.PlayerType;
        int aggression = record.Aggression;
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotIdentity.Ensure(record, AutonomousGuildCharter.Social), Is.False);
            Assert.That(record.PlayerType, Is.EqualTo(type));
            Assert.That(record.Aggression, Is.EqualTo(aggression));
            Assert.That(record.Level, Is.EqualTo(37));
            Assert.That(record.Experience, Is.EqualTo(123456));
            Assert.That(record.MoneyCopper, Is.EqualTo(98765));
            Assert.That(record.Patience, Is.InRange(15, 85));
        });
        var duplicate = new OfflineWorldBotRecord { BotId = 42, GuildId = "guild-a", ClassId = 19 };
        AutonomousBotIdentity.Ensure(duplicate, AutonomousGuildCharter.Hunting);
        Assert.That(duplicate.PlayerType, Is.EqualTo(type));
        Assert.That(duplicate.Aggression, Is.EqualTo(aggression));
    }

    [Test]
    public void HunterLevelPhaseAndPersistedWallChangeNextTask()
    {
        DateTime now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var record = new OfflineWorldBotRecord { PlayerType = "Hunter", Level = 9,
            Aggression = 50, RiskTolerance = 50, Sociability = 50, Patience = 50 };
        Assert.That(AutonomousActivityScheduler.Choose(record, now, 0), Is.Not.EqualTo(eAutonomousObjectiveKind.RvR));
        record.Aggression = 85;
        record.RiskTolerance = 85;
        Assert.That(AutonomousActivityScheduler.Choose(record, now, 0), Is.Not.EqualTo(eAutonomousObjectiveKind.RvR));
        record.Aggression = record.RiskTolerance = 50;
        record.Level = 10;
        Assert.That(AutonomousActivityScheduler.Choose(record, now, 0), Is.EqualTo(eAutonomousObjectiveKind.RvR));
        for (int index = 0; index < 3; index++)
            AutonomousActivityScheduler.RecordPvpDeath(record, now.AddMinutes(index));
        Assert.That(AutonomousActivityScheduler.IsPveBlocked(record, now.AddMinutes(3)), Is.True);
        Assert.That(AutonomousActivityScheduler.Choose(record, now.AddMinutes(3), 0),
            Is.Not.EqualTo(eAutonomousObjectiveKind.RvR));
        Assert.That(AutonomousActivityScheduler.IsPveBlocked(record, now.AddMinutes(100)), Is.False);
    }

    [Test]
    public void GuildRaidAndGearWallApplyAtAssignmentBoundary()
    {
        var record = new OfflineWorldBotRecord { PlayerType = "Roamer", Level = 50,
            Aggression = 50, RiskTolerance = 50, Sociability = 50 };
        DateTime now = DateTime.UtcNow;
        Assert.That(AutonomousActivityScheduler.Choose(record, now, 0, guildRaid: true),
            Is.EqualTo(eAutonomousObjectiveKind.GroupPve));
        Assert.That(AutonomousActivityScheduler.IsUndergeared(50, 20), Is.True);
        Assert.That(AutonomousActivityScheduler.Choose(record, now, 0, undergeared: true),
            Is.Not.EqualTo(eAutonomousObjectiveKind.RvR));
        Assert.That(AutonomousActivityScheduler.IsOutleveledByGuild(25, new[] { 32, 34 }), Is.True);
        Assert.That(AutonomousActivityScheduler.IsOutleveledByGuild(25, new[] { 29, 34 }), Is.False);
    }
}
