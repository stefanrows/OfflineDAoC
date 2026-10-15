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
    public void TwoBombersWithAreaMezMakeABombGroup()
    {
        var members = new[]
        {
            new RvrDoctrineMember(eCharacterClass.Wizard, 50), new RvrDoctrineMember(eCharacterClass.Wizard, 50),
            new RvrDoctrineMember(eCharacterClass.Sorcerer, 50), new RvrDoctrineMember(eCharacterClass.Cleric, 50),
        };
        Assert.That(AutonomousRvrDoctrine.Derive(members, AutonomousPlayerType.Roamer, RvrLeaderTraits.Neutral).Kind,
            Is.EqualTo(RvrDoctrineKind.BombGroup));
    }
}
