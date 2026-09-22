using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_CamlannPopulationTuning
{
    [TestCase(0, 0)]
    [TestCase(1, 0)]
    [TestCase(2, 0)]
    [TestCase(3, 1)]
    [TestCase(4, 2)]
    [TestCase(8, 2)]
    [TestCase(10, 3)]
    [TestCase(100, 25)]
    public void SoloReserveLeavesRoomForSmallCrews(int roster, int expected)
    {
        Assert.That(CamlannPopulationTuning.MinimumSoloRvrReserve(roster), Is.EqualTo(expected));
    }

    [Test]
    public void WarbandDistributionHasScoutsPairsSmallCrewsAndEightManRoams()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CamlannPopulationTuning.RollWarbandSize(8, 0.00), Is.EqualTo(1));
            Assert.That(CamlannPopulationTuning.RollWarbandSize(8, 0.149999), Is.EqualTo(1));
            Assert.That(CamlannPopulationTuning.RollWarbandSize(8, 0.15), Is.EqualTo(2));
            Assert.That(CamlannPopulationTuning.RollWarbandSize(8, 0.449999), Is.EqualTo(2));
            Assert.That(CamlannPopulationTuning.RollWarbandSize(8, 0.45), Is.EqualTo(3));
            Assert.That(CamlannPopulationTuning.RollWarbandSize(8, 0.649999), Is.EqualTo(5));
            Assert.That(CamlannPopulationTuning.RollWarbandSize(8, 0.65), Is.EqualTo(8));
            Assert.That(CamlannPopulationTuning.RollWarbandSize(8, 0.999999), Is.EqualTo(8));
        });
    }

    [Test]
    public void WarbandRollCapsEveryShapeToAvailableSlots()
    {
        for (int maximum = 1; maximum <= AutonomousCrewManager.MaximumCrewSize; maximum++)
            for (int step = 0; step < 100; step++)
                Assert.That(CamlannPopulationTuning.RollWarbandSize(maximum, step / 100d),
                    Is.InRange(1, maximum));
    }

    [Test]
    public void KeepAndRelicObjectiveCooldownIsLongEnoughToAvoidImmediateRepeat()
    {
        Assert.That(CamlannPopulationTuning.TargetCooldownMilliseconds, Is.EqualTo(30 * 60_000L));
        Assert.That(AutonomousRvrEventLayer.TargetCooldownMilliseconds,
            Is.EqualTo(CamlannPopulationTuning.TargetCooldownMilliseconds));
    }
}
