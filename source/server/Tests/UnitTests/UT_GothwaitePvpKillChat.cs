using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// The Gothwaite PvP kill chat (docs/GOTHWAITE_PVP_MODE.md section 8, stage 4): the eligibility of a kill, the 15 per cent chance on a
/// seeded draw, the per-bot and per-arena limits of <see cref="GothwaitePvpChatGate"/>, and the plain lines (no blocked term, the victim's
/// name only when it is clean, "rez pls" only while a group member is dead). Pure: no GameBot, no timer, no say.
/// </summary>
[TestFixture]
public class UT_GothwaitePvpKillChat
{
    private const int Seed = 20261013;
    private const string Victim = "Brisk";

    [Test]
    public void OnlyAnArenaKillByAModeBotOfAPlayerShapedVictimWithTheModeOnQualifies()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GothwaitePvpKillChat.Eligible(true, true, true, true, true), Is.True);
            Assert.That(GothwaitePvpKillChat.Eligible(false, true, true, true, true), Is.False, "the mode is off");
            Assert.That(GothwaitePvpKillChat.Eligible(true, false, true, true, true), Is.False, "the killer is not player-shaped");
            Assert.That(GothwaitePvpKillChat.Eligible(true, true, false, true, true), Is.False, "the killer is not a mode bot");
            Assert.That(GothwaitePvpKillChat.Eligible(true, true, true, false, true), Is.False, "the victim is a monster");
            Assert.That(GothwaitePvpKillChat.Eligible(true, true, true, true, false), Is.False, "the death is outside the arena");
        });
    }

    [TestCase(0.0, true)]
    [TestCase(0.1499, true)]
    [TestCase(0.15, false)]
    [TestCase(0.999, false)]
    public void TheChanceIsFifteenPerCentOfTheRolls(double roll, bool passes)
    {
        Assert.That(GothwaitePvpKillChat.ChancePasses(roll), Is.EqualTo(passes));
    }

    [Test]
    public void ASeededDrawPassesAboutOneKillInSeven()
    {
        var random = new Random(Seed);
        int passes = 0;
        for (int draw = 0; draw < 10_000; draw++)
            if (GothwaitePvpKillChat.ChancePasses(random.NextDouble()))
                passes++;

        Assert.That(passes, Is.InRange(1350, 1650), "15 per cent of 10 000 kills, within 150");
    }

    [Test]
    public void ABotSpeaksAtMostOncePerTwoMinutesWhateverTheKillRate()
    {
        var gate = new GothwaitePvpChatGate();
        var spoke = new List<long>();
        // A kill every five seconds for ten minutes, each one passing the chance: only the gate decides who speaks.
        for (long now = 0; now <= 600_000; now += 5_000)
            if (gate.TryClaim(7, 51, now))
                spoke.Add(now);

        Assert.That(spoke, Is.EqualTo(new long[] { 0, 120_000, 240_000, 360_000, 480_000, 600_000 }));
    }

    [Test]
    public void TheArenaSpeaksAtMostOnceEveryThirtySecondsWhicheverBotSpeaks()
    {
        var gate = new GothwaitePvpChatGate();

        Assert.Multiple(() =>
        {
            Assert.That(gate.TryClaim(1, 51, 0), Is.True);
            Assert.That(gate.TryClaim(2, 51, 10_000), Is.False, "another bot in the same region waits for the region window");
            Assert.That(gate.TryClaim(2, 51, 29_999), Is.False);
            Assert.That(gate.TryClaim(2, 51, 30_000), Is.True);
            Assert.That(gate.TryClaim(1, 51, 60_000), Is.False, "the first bot still waits for its own two minutes");
            Assert.That(gate.TryClaim(3, 52, 40_000), Is.True, "another region has its own window");
        });
    }

    [Test]
    public void ARefusedClaimMovesNoLimit()
    {
        var gate = new GothwaitePvpChatGate();
        gate.TryClaim(1, 51, 0);

        Assert.That(gate.TryClaim(1, 51, 10_000), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(gate.TryClaim(2, 51, 30_000), Is.True, "the refused claim did not restart the region window");
            Assert.That(gate.TryClaim(1, 51, 120_000), Is.True, "nor the bot's own window");
        });
    }

    [Test]
    public void ClearForgetsEveryLimit()
    {
        var gate = new GothwaitePvpChatGate();
        gate.TryClaim(1, 51, 0);

        gate.Clear();

        Assert.That(gate.TryClaim(1, 51, 1), Is.True);
    }

    [Test]
    public void TheLinesAreTheFixedPhrasesWithTheVictimsName()
    {
        var random = new Random(Seed);

        string[] lines = Enumerable.Range(0, 1000)
            .Select(_ => GothwaitePvpKillChat.Line(Victim, false, random))
            .Distinct()
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();

        Assert.That(lines, Is.EquivalentTo(new[] { "gg", "inc more from the harbor", "Brisk down", "good fight, Brisk" }));
    }

    [Test]
    public void RezPlsJoinsTheLinesOnlyWhileAGroupMemberIsDead()
    {
        var random = new Random(Seed);
        string[] withoutDeadMember = Enumerable.Range(0, 500).Select(_ => GothwaitePvpKillChat.Line(Victim, false, random)).ToArray();
        string[] withDeadMember = Enumerable.Range(0, 500).Select(_ => GothwaitePvpKillChat.Line(Victim, true, random)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(withoutDeadMember, Has.None.EqualTo("rez pls"));
            Assert.That(withDeadMember, Has.Some.EqualTo("rez pls"));
        });
    }

    [Test]
    public void ABlockedVictimNameNeverReachesALine()
    {
        var random = new Random(Seed);

        string[] lines = Enumerable.Range(0, 300).Select(_ => GothwaitePvpKillChat.Line("fag", true, random)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(lines.Any(line => line.Contains("fag", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(lines.All(line => !AutonomousChatSafetyPolicy.ContainsBlockedTerm(line)), Is.True);
            Assert.That(lines, Has.Some.EqualTo("gg"), "the plain line stands in for the name");
        });
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("   ")]
    public void AMissingNameStillGivesAPlainLine(string name)
    {
        var random = new Random(Seed);

        string[] lines = Enumerable.Range(0, 200).Select(_ => GothwaitePvpKillChat.Line(name, false, random)).Distinct().ToArray();

        Assert.That(lines, Is.EquivalentTo(new[] { "gg", "inc more from the harbor" }));
    }

    [Test]
    public void EveryLineIsShortAndHasNoFormattingBraces()
    {
        var random = new Random(Seed);
        string longest = new('x', 32);

        IEnumerable<string> lines = Enumerable.Range(0, 400).Select(index => GothwaitePvpKillChat.Line(longest, index % 2 == 0, random));

        Assert.That(lines.All(line => line.Length <= 60 && !line.Contains('{') && !line.Contains('}')), Is.True);
    }
}
