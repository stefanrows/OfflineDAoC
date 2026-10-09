using System;
using DOL.Database;
using DOL.GS.Keeps;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_KeepLevelProgression
{
    private static readonly DateTime Start = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private static DbKeep Owned() => new()
    {
        BaseLevel = 50, Level = 5, ClaimedGuildName = "Test guild", ClaimedAt = Start.AddDays(-5)
    };

    [Test]
    public void ExistingClaimResetsOnceWithoutChangingOwnerOrClaimDate()
    {
        DbKeep keep = Owned();
        Assert.That(KeepLevelProgression.Initialize(keep, Start), Is.True);
        Assert.That(keep.Level, Is.EqualTo(1));
        Assert.That(keep.NextLevelAt, Is.EqualTo(Start.AddMinutes(12)));
        Assert.That(keep.ClaimedGuildName, Is.EqualTo("Test guild"));
        Assert.That(keep.ClaimedAt, Is.EqualTo(Start.AddDays(-5)));
        KeepLevelProgression.Advance(keep, Start.AddHours(1), 10);
        Assert.That(KeepLevelProgression.Initialize(keep, Start.AddHours(1)), Is.False);
        Assert.That(keep.Level, Is.EqualTo(5));
    }

    [TestCase(11, 1)]
    [TestCase(12, 2)]
    [TestCase(24, 3)]
    [TestCase(36, 4)]
    [TestCase(60, 5)]
    [TestCase(120, 6)]
    [TestCase(240, 7)]
    [TestCase(480, 8)]
    [TestCase(960, 9)]
    [TestCase(1920, 10)]
    [TestCase(5000, 10)]
    public void HeldKeepFollowsScheduleAndStopsAtTen(int minutes, int level)
    {
        DbKeep keep = Owned();
        KeepLevelProgression.Initialize(keep, Start);
        KeepLevelProgression.Advance(keep, Start.AddMinutes(minutes), 10);
        Assert.That(keep.Level, Is.EqualTo(level));
        if (level == 10) Assert.That(keep.NextLevelAt, Is.EqualTo(DateTime.MinValue));
    }

    [Test]
    public void ReloadedDeadlineRetainsPartialProgressAndCatchesUpDowntime()
    {
        DbKeep keep = Owned();
        KeepLevelProgression.Initialize(keep, Start);
        KeepLevelProgression.Advance(keep, Start.AddMinutes(15), 10);
        DbKeep reloaded = new()
        {
            BaseLevel = keep.BaseLevel, Level = keep.Level,
            ClaimedGuildName = keep.ClaimedGuildName, ClaimedAt = keep.ClaimedAt,
            ProgressionInitialized = keep.ProgressionInitialized, NextLevelAt = keep.NextLevelAt
        };
        Assert.That(KeepLevelProgression.Initialize(reloaded, Start.AddMinutes(20)), Is.False);
        Assert.That(reloaded.NextLevelAt, Is.EqualTo(Start.AddMinutes(24)));
        KeepLevelProgression.Advance(reloaded, Start.AddHours(8), 10);
        Assert.That(reloaded.Level, Is.EqualTo(8));
        Assert.That(reloaded.NextLevelAt, Is.EqualTo(Start.AddHours(16)));
    }

    [TestCase("", 50, 0, false)]
    [TestCase("Frontier Wardens", 50, 0, false)]
    [TestCase("Test guild", 60, 99, false)]
    [TestCase("Test guild", 100, 0, false)]
    [TestCase("Test guild", 50, 0, true)]
    public void NpcSpecialAndDefeatedKeepsAreUntouched(string owner, int baseLevel, int skin, bool defeated)
    {
        DbKeep keep = Owned();
        keep.ClaimedGuildName = owner;
        keep.BaseLevel = (byte)baseLevel;
        keep.SkinType = (byte)skin;
        keep.LordDefeated = defeated;
        Assert.That(KeepLevelProgression.Initialize(keep, Start), Is.False);
        Assert.That(KeepLevelProgression.Advance(keep, Start.AddDays(10), 10), Is.False);
        Assert.That(keep.Level, Is.EqualTo(5));
        Assert.That(keep.ProgressionInitialized, Is.False);
        Assert.That(keep.NextLevelAt, Is.EqualTo(DateTime.MinValue));
    }

    [Test]
    public void ConfiguredLowerCapStopsWithoutAnEndlessTimer()
    {
        DbKeep keep = Owned();
        KeepLevelProgression.Initialize(keep, Start);
        KeepLevelProgression.Advance(keep, Start.AddDays(2), 5);
        Assert.That(keep.Level, Is.EqualTo(5));
        Assert.That(keep.NextLevelAt, Is.EqualTo(DateTime.MinValue));
    }
}
