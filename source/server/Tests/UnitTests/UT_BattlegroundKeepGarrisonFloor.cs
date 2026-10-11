using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Bug 148: the lord and garrison of Thidranki Keep stood on the main keep's roof (+857 above the keep) because the highest gated point won.</summary>
    [TestFixture]
    public class UT_BattlegroundKeepGarrisonFloor
    {
        private const double DoorZ = 4000;
        private const string ServerKeep = "offline-bg-keep:238";

        [Test]
        public void TheLordTakesTheDoorFloorNotTheRoof()
        {
            var gated = new List<(Vector3 Point, float DoorDistance)>
            {
                (new Vector3(0, 0, 4857), 900f),   // roof, deepest behind the doors
                (new Vector3(10, 0, 4148), 500f),  // an upper floor
                (new Vector3(20, 0, 4010), 400f),  // the lord room on the door floor
                (new Vector3(30, 0, 4000), 300f),
            };

            List<Vector3> ordered = BattlegroundNativeKeepData.GarrisonOrder(gated, DoorZ);

            Assert.That(ordered[0], Is.EqualTo(new Vector3(20, 0, 4010)), "same band as the door floor, deepest behind the doors");
            Assert.That(ordered[^1], Is.EqualTo(new Vector3(0, 0, 4857)), "the roof comes last");
        }

        [Test]
        public void FloorsBelowTheDoorsRankByTheirDistanceToTheDoorFloorToo()
        {
            var gated = new List<(Vector3 Point, float DoorDistance)>
            {
                (new Vector3(0, 0, 3500), 900f),
                (new Vector3(10, 0, 4300), 100f),
                (new Vector3(20, 0, 3990), 100f),
            };

            Assert.That(BattlegroundNativeKeepData.GarrisonOrder(gated, DoorZ)[0], Is.EqualTo(new Vector3(20, 0, 3990)));
        }

        [Test]
        public void RetainersPreferTheLordsFloorAndFallBackToTheRest()
        {
            var lord = new Vector3(0, 0, 4010);
            var upper = new Vector3(300, 0, 4857);
            var sameFloor = new Vector3(600, 0, 4040);
            var farBelow = new Vector3(900, 0, 3800);

            List<Vector3> order = BattlegroundNativeKeepData.RetainerOrder(new[] { upper, lord, sameFloor, farBelow }, lord);

            Assert.That(order, Is.EqualTo(new List<Vector3> { lord, sameFloor, upper, farBelow }));
        }

        [Test]
        public void ARoofLordInAServerBuiltFullKeepIsReplaced()
        {
            // Keep 143: the lord was saved at Z 4928 with the door floor at about 4071.
            Assert.That(BattlegroundNativeKeepData.ShouldReplaceGarrison(ServerKeep, false, 4928, 4071), Is.True);
        }

        [Test]
        public void TowersLowLordsAndNativeKeepsAreLeftAlone()
        {
            Assert.That(BattlegroundNativeKeepData.ShouldReplaceGarrison(ServerKeep, true, 4458, 4000), Is.False, "a tower keeps its top (keep 140, +458)");
            Assert.That(BattlegroundNativeKeepData.ShouldReplaceGarrison(ServerKeep, false, 4148, 4000), Is.False, "Leirvik, +148");
            Assert.That(BattlegroundNativeKeepData.ShouldReplaceGarrison(ServerKeep, false, 4400, 4000), Is.False, "exactly the limit");
            Assert.That(BattlegroundNativeKeepData.ShouldReplaceGarrison("offline-native-bg:252", false, 5000, 4000), Is.False, "a native keep keeps its saved garrison");
            Assert.That(BattlegroundNativeKeepData.ShouldReplaceGarrison("Atlas BG", false, 5000, 4000), Is.False);
            Assert.That(BattlegroundNativeKeepData.ShouldReplaceGarrison(null, false, 5000, 4000), Is.False);
        }

        [Test]
        public void ReplacementIsOneTimeBecauseTheNewLordStandsOnTheDoorFloor()
        {
            var gated = new List<(Vector3 Point, float DoorDistance)> { (new Vector3(0, 0, 4928), 900f), (new Vector3(10, 0, 4075), 300f) };
            Vector3 lord = BattlegroundNativeKeepData.GarrisonOrder(gated, 4071)[0];

            Assert.That(BattlegroundNativeKeepData.ShouldReplaceGarrison(ServerKeep, false, lord.Z, 4071), Is.False);
        }
    }
}
