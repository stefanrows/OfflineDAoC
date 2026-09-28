using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public class UT_AutonomousSoloConfidence
{
    // Solo natural ceiling is yellow; two steps down is green.
    private const int SoloMaximumSteps = 2;

    [Test]
    public void PveDefeatLowersCeilingAtOnce()
    {
        var confidence = new AutonomousSoloConfidence();
        confidence.RecordPveDefeat(1, requiredSteps: 1, SoloMaximumSteps);
        Assert.That(confidence.Steps, Is.EqualTo(1));
        confidence.RecordPveDefeat(1, requiredSteps: 1, SoloMaximumSteps);
        Assert.That(confidence.Steps, Is.EqualTo(2));
        confidence.RecordPveDefeat(3, requiredSteps: 2, SoloMaximumSteps);
        Assert.That(confidence.Steps, Is.EqualTo(2), "Never below green");
    }

    [Test]
    public void CeilingRecoversOneStepPerTenCleanKills()
    {
        var confidence = new AutonomousSoloConfidence();
        confidence.RecordPveDefeat(2, requiredSteps: 2, SoloMaximumSteps);

        Assert.Multiple(() =>
        {
            Assert.That(confidence.RecordKills(9), Is.False);
            Assert.That(confidence.Steps, Is.EqualTo(2));
            Assert.That(confidence.RecordKills(1), Is.True);
            Assert.That(confidence.Steps, Is.EqualTo(1));
            for (int kill = 0; kill < AutonomousSoloConfidence.KillsPerRecoveryStep - 1; kill++)
                Assert.That(confidence.RecordKills(1), Is.False);
            Assert.That(confidence.RecordKills(1), Is.True);
            Assert.That(confidence.Steps, Is.EqualTo(0));
            Assert.That(confidence.RecordKills(50), Is.False, "Already at the default ceiling");
        });
    }

    [Test]
    public void DefeatResetsTheCleanKillRun()
    {
        var confidence = new AutonomousSoloConfidence();
        confidence.RecordPveDefeat(1, requiredSteps: 1, SoloMaximumSteps);
        confidence.RecordKills(8);
        confidence.RecordPveDefeat(1, requiredSteps: 1, SoloMaximumSteps);
        Assert.That(confidence.KillsTowardRecovery, Is.Zero);
        Assert.That(confidence.RecordKills(2), Is.False);
        Assert.That(confidence.Steps, Is.EqualTo(2));
    }

    [Test]
    public void KillsAtFullConfidenceAreNotBankedAgainstALaterDefeat()
    {
        var confidence = new AutonomousSoloConfidence();
        confidence.RecordKills(30);
        confidence.RecordPveDefeat(1, requiredSteps: 1, SoloMaximumSteps);
        Assert.That(confidence.Steps, Is.EqualTo(1));
        Assert.That(confidence.RecordKills(1), Is.False);
    }

    [Test]
    public void LevelUpAndNewTaskEachRecoverOneStep()
    {
        var confidence = new AutonomousSoloConfidence();
        confidence.RecordPveDefeat(2, requiredSteps: 2, SoloMaximumSteps);
        Assert.That(confidence.RecoverStep(), Is.True, "level-up");
        Assert.That(confidence.Steps, Is.EqualTo(1));
        Assert.That(confidence.RecoverStep(), Is.True, "new task");
        Assert.That(confidence.Steps, Is.EqualTo(0));
        Assert.That(confidence.RecoverStep(), Is.False);
    }

    [Test]
    public void GankFinishedByAMobDoesNotLowerTheCeiling()
    {
        const long death = 1_000_000;
        var confidence = new AutonomousSoloConfidence();
        // An enemy player hit the leveler 12 s ago; the camp mob landed the kill.
        bool pvp = AutonomousDeathAttribution.CountsAsPvp(false, death - 12_000, death);
        if (!pvp)
            confidence.RecordPveDefeat(1, requiredSteps: 1, SoloMaximumSteps);

        Assert.Multiple(() =>
        {
            Assert.That(pvp, Is.True);
            Assert.That(AutonomousDeathAttribution.PvpSource(false, death - 12_000, death), Is.EqualTo("recent_damage"));
            Assert.That(confidence.Steps, Is.Zero);
        });
    }

    [Test]
    public void PurePveDeathStillLowersTheCeiling()
    {
        const long death = 1_000_000;
        var confidence = new AutonomousSoloConfidence();
        foreach (long lastPvpHit in new[] { 0L, death - 31_000 })
        {
            bool pvp = AutonomousDeathAttribution.CountsAsPvp(false, lastPvpHit, death);
            Assert.That(pvp, Is.False, $"last PvP hit {lastPvpHit}");
            Assert.That(AutonomousDeathAttribution.PvpSource(false, lastPvpHit, death), Is.EqualTo("none"));
        }
        confidence.RecordPveDefeat(1, requiredSteps: 1, SoloMaximumSteps);
        Assert.That(confidence.Steps, Is.EqualTo(1));
    }

    [Test]
    public void PlayerShapedKillerIsAlwaysPvp()
    {
        Assert.That(AutonomousDeathAttribution.CountsAsPvp(true, 0, 1_000), Is.True);
        Assert.That(AutonomousDeathAttribution.PvpSource(true, 0, 1_000), Is.EqualTo("killer"));
        Assert.That(AutonomousDeathAttribution.CountsAsPvp(false, 1_000, 1_000 + AutonomousDeathAttribution.RecentPvpDamageMilliseconds),
            Is.True, "Exactly 30 s still counts");
    }

    [Test]
    public void AlliedOrSelfOrHarmlessAttackIsNotHostilePvpDamage()
    {
        var victim = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        var guildMate = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        var enemy = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        // Stand-in for PvpCombatant.AreAllied: same guild, group or battlegroup.
        bool AreAllied(GameLiving first, GameLiving second) =>
            first == second || first == guildMate && second == victim || first == victim && second == guildMate;

        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDeathAttribution.IsHostilePlayerShapedDamage(victim,
                new AttackData { Attacker = guildMate, Damage = 50 }, AreAllied, out _), Is.False, "allied guild mate");
            Assert.That(AutonomousDeathAttribution.IsHostilePlayerShapedDamage(victim,
                new AttackData { Attacker = enemy, Damage = 50 }, AreAllied, out GameLiving identity), Is.True, "other guild");
            Assert.That(identity, Is.SameAs(enemy));
            Assert.That(AutonomousDeathAttribution.IsHostilePlayerShapedDamage(victim,
                new AttackData { Attacker = victim, Damage = 50 }, out _), Is.False, "self");
            Assert.That(AutonomousDeathAttribution.IsHostilePlayerShapedDamage(victim,
                new AttackData { Attacker = enemy, Damage = 0 }, AreAllied, out _), Is.False, "miss or resist");
        });
    }

    [Test]
    public void KillerTypesSeparatePlayersCompanionsWorldBotsAndMobs()
    {
        var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
        var companion = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        var mob = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));

        Assert.Multiple(() =>
        {
            Assert.That(AutonomousDeathAttribution.TypeOf(player, player), Is.EqualTo("player"));
            Assert.That(AutonomousDeathAttribution.TypeOf(companion, companion), Is.EqualTo("companion"));
            Assert.That(AutonomousDeathAttribution.TypeOf(null, mob), Is.EqualTo("mob"));
            Assert.That(AutonomousDeathAttribution.TypeOf(null, null), Is.EqualTo("none"));
        });
    }
}
