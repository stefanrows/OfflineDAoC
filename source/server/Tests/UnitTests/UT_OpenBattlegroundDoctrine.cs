using System.Collections.Generic;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_OpenBattlegroundDoctrine
{
    [Test]
    public void ABattlegroundGroupBreaksOffOnHealerDeathASecondGroupOrThreeToOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBattlegroundDriver.ShouldBreakOff(true, 1, 8, 8), Is.True, "healer dead");
            Assert.That(AutonomousBattlegroundDriver.ShouldBreakOff(false, 2, 8, 8), Is.True, "a second enemy group");
            Assert.That(AutonomousBattlegroundDriver.ShouldBreakOff(false, 1, 3, 9), Is.True, "3:1");
            Assert.That(AutonomousBattlegroundDriver.ShouldBreakOff(false, 1, 8, 8), Is.False, "an even fight stays");
            Assert.That(AutonomousBattlegroundDriver.ShouldBreakOff(false, 0, 1, 0), Is.False, "no enemy, nothing to break off");
        });
    }

    [Test]
    public void PetsAndTheSameOwnerCountOnceAndOnlyRealGroupsCountAsGroups()
    {
        var eightWithPets = new List<(int, int)>();
        for (int owner = 1; owner <= 8; owner++)
        {
            eightWithPets.Add((owner, 1));
            eightWithPets.Add((owner, 1)); // his pet, resolved to him
        }
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBattlegroundDriver.CountEnemies(eightWithPets), Is.EqualTo((8, 1)), "pets of one 8-man");
            Assert.That(AutonomousBattlegroundDriver.CountEnemies(new List<(int, int)> { (1, 1), (2, 1), (3, 2), (4, 2) }), Is.EqualTo((4, 2)), "two groups");
            Assert.That(AutonomousBattlegroundDriver.CountEnemies(new List<(int, int)> { (1, 1), (2, 1), (9, 0) }), Is.EqualTo((3, 1)), "a lone passer-by");
            Assert.That(AutonomousBattlegroundDriver.CountEnemies(new List<(int, int)> { (7, 0), (7, 0) }), Is.EqualTo((1, 0)), "same owner twice");
            Assert.That(AutonomousBattlegroundDriver.CountEnemies(new List<(int, int)> { (1, 1), (3, 2) }), Is.EqualTo((2, 0)), "single members of two groups are not groups");
        });
    }

    [Test]
    public void TheKeepOrderIsExemptAndTheImmunityWindowBlocksANewBreakOff()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBattlegroundDriver.BreakOffExempt(OpenBgRole.Assault, true), Is.True, "at the keep");
            Assert.That(AutonomousBattlegroundDriver.BreakOffExempt(OpenBgRole.Defend, false), Is.True);
            Assert.That(AutonomousBattlegroundDriver.BreakOffExempt(OpenBgRole.Retreat, false), Is.True);
            Assert.That(AutonomousBattlegroundDriver.BreakOffExempt(OpenBgRole.Assault, false), Is.False, "assault still on the way (muster)");
            Assert.That(AutonomousBattlegroundDriver.BreakOffExempt(OpenBgRole.Roam, false), Is.False);
            Assert.That(AutonomousBattlegroundDriver.BreakOffAllowed(100_000, 200_000), Is.False, "inside the window");
            Assert.That(AutonomousBattlegroundDriver.BreakOffAllowed(200_000, 200_000), Is.True, "after the window");
        });
    }
}
