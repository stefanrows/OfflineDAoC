// Upstream test (shadowofze/OfflineDAoC 0.38c, upstream/main 9da90910 source/server/Tests/UnitTests/UT_EveningAuditFixes.cs),
// the parts ported in session S2 (docs/RVR_PORT_MAP.md): keep-travel stalls, the march detour and the arrival quorum.
// Fork: the dragon, dungeon-route, battleground-wave, raise-upkeep and Summoner's Hall cases belong to S3 or to fork code
// that differs from upstream and are not carried.
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Fixes from the 2026-10-02 evening audit (19:02-22:57 run).</summary>
    [TestFixture]
    public class UT_RealmRvr_EveningAuditFixes
    {
        // 1. Keep travel: long legs are chunked and a still bot is a stall.
        [Test]
        public void LongKeepLegsAreChunkedAndStillBotsStall()
        {
            Assert.Multiple(() =>
            {
                // Hibernia portal keep -> Caer Benowyc was one ~50,000-unit leg.
                Assert.That(KeepTravelStallPolicy.NeedsShorterLeg(new(605743, 293676, 4839), new(653430, 345890, 6280)), Is.True);
                Assert.That(KeepTravelStallPolicy.NeedsShorterLeg(new(0, 0, 0), new(5000, 0, 0)), Is.False);
                Assert.That(KeepTravelStallPolicy.IsStalled(60_000, false, false), Is.True);
                Assert.That(KeepTravelStallPolicy.IsStalled(59_000, false, false), Is.False);
                Assert.That(KeepTravelStallPolicy.IsStalled(600_000, true, false), Is.False, "fighting or casting is not a stall");
                Assert.That(KeepTravelStallPolicy.IsStalled(600_000, false, true), Is.False, "riding a stable horse is not a stall");
                Assert.That(KeepTravelStallPolicy.SameSpot(new(625357, 314061, 5034), new(625600, 314200, 5034)), Is.True);
                Assert.That(KeepTravelStallPolicy.SameSpot(new(625357, 314061, 5034), new(626000, 314061, 5034)), Is.False);
            });
        }

        [Test]
        public void PointAlongWalksThePathCorners()
        {
            Vector3[] corners = [new(0, 0, 0), new(3000, 0, 0), new(3000, 3000, 0)];
            Assert.Multiple(() =>
            {
                Assert.That(KeepTravelStallPolicy.PointAlong(corners, 4000), Is.EqualTo(new Vector3(3000, 1000, 0)));
                Assert.That(KeepTravelStallPolicy.PointAlong(corners, 10_000), Is.EqualTo(new Vector3(3000, 3000, 0)), "shorter path: its end");
                Assert.That(KeepTravelStallPolicy.PointAlong([], 100), Is.Null);
            });
        }

        // A marched siege opens from its posts only with 60% of the army that marched there (at least 8).
        [TestCase(21, 12)]
        [TestCase(5, 8)]
        [TestCase(170, 102)]
        public void MarchedSiegeOpensWithMostOfTheArmy(int marched, int quorum) =>
            Assert.That(AutonomousRvrEventLayer.ArrivalQuorum(marched), Is.EqualTo(quorum));

        // The march goes around the first enemy keep ahead, on the far side from it, and ignores keeps off the line.
        [Test]
        public void ArmyDetourGoesAroundTheFirstKeepAhead()
        {
            var from = new Vector2(0, 0);
            var to = new Vector2(100_000, 0);
            var keeps = new[]
            {
                new AutonomousArmyDetour.Hazard(new Vector2(60_000, 2_000), 6_000),   // second on the line
                new AutonomousArmyDetour.Hazard(new Vector2(30_000, 1_000), 6_000),   // first on the line, just above it
                new AutonomousArmyDetour.Hazard(new Vector2(50_000, 20_000), 6_000),  // well off the line
                new AutonomousArmyDetour.Hazard(new Vector2(-10_000, 0), 6_000),      // behind the start
            };
            Vector2? detour = AutonomousArmyDetour.FirstDetour(from, to, keeps);
            Assert.That(detour.HasValue, Is.True);
            Assert.That(detour.Value.X, Is.EqualTo(30_000).Within(1));
            Assert.That(detour.Value.Y, Is.EqualTo(1_000 - 10_000).Within(1)); // below the line, radius + margin away
            Assert.That(AutonomousArmyDetour.FirstDetour(from, to, keeps[2..]), Is.Null);
            // A hazard around the target itself is not walked around.
            Assert.That(AutonomousArmyDetour.FirstDetour(from, to, [new(new Vector2(98_000, 0), 6_000)]), Is.Null);
        }
    }
}
