using System;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Levelling bots return to PvE after RvR; bots avoid boss spots (0.173.0).</summary>
    [TestFixture]
    public sealed class UT_LevellingReturns
    {
        [Test]
        public void OnlyLevelFiftyStaysInRvrWithoutEnd()
        {
            Assert.That(AutonomousObjectiveAssignments.RvrTourRenews(50), Is.True);
            Assert.That(AutonomousObjectiveAssignments.RvrTourRenews(49), Is.False);
            Assert.That(AutonomousObjectiveAssignments.RvrTourRenews(30), Is.False);
        }

        [Test]
        public void FarStrongerMonsterMarksItsSpotForSixHours()
        {
            AutonomousPveBossDanger.Clear();
            var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousPveBossDanger.Marks("mob", 65, 50, false), Is.True);
                Assert.That(AutonomousPveBossDanger.Marks("mob", 59, 50, false), Is.False);
                Assert.That(AutonomousPveBossDanger.Marks("world_bot", 70, 50, true), Is.False);
            });
            AutonomousPveBossDanger.Record(276, 30_000, 37_000, now);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousPveBossDanger.IsNear(276, 31_500, 38_000, now.AddHours(1)), Is.True);
                Assert.That(AutonomousPveBossDanger.IsNear(276, 40_000, 37_000, now.AddHours(1)), Is.False);
                Assert.That(AutonomousPveBossDanger.IsNear(1, 30_000, 37_000, now.AddHours(1)), Is.False);
                Assert.That(AutonomousPveBossDanger.IsNear(276, 30_000, 37_000, now.AddHours(7)), Is.False);
            });
            AutonomousPveBossDanger.Clear();
        }
    }
}
