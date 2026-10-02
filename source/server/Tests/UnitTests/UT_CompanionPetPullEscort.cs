using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using Threat = DOL.GS.CompanionPetPullEscort.Threat;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_CompanionPetPullEscort
    {
        private static readonly Vector3 Camp = new(0, 0, 0);

        [Test]
        public void OwnerNearCampIsNotOut()
        {
            Assert.That(CompanionPetPullEscort.IsOwnerOut(new Vector3(300, 0, 0), Camp), Is.False);
            Assert.That(CompanionPetPullEscort.IsOwnerOut(new Vector3(900, 0, 0), Camp), Is.True);
        }

        [Test]
        public void TrailsBehindTheOwnerTowardCamp()
        {
            Vector3? point = CompanionPetPullEscort.EscortPoint(new Vector3(1000, 0, 0), Camp, []);
            Assert.That(point, Is.EqualTo(new Vector3(750, 0, 0)));
        }

        [Test]
        public void StepsBackTowardCampAroundAnIdleMob()
        {
            // A mob with 300 aggro range stands beside the trailing spot.
            Threat[] threats = [new(new Vector2(750, 100), 300)];
            Vector3? point = CompanionPetPullEscort.EscortPoint(new Vector3(1000, 0, 0), Camp, threats);
            Assert.That(point, Is.Not.Null);
            Assert.That(CompanionPetPullEscort.IsSafe(point.Value, threats), Is.True);
            Assert.That(point.Value.X, Is.LessThan(750));
        }

        [Test]
        public void WaitsWhenTheWholeWayIsUnsafe()
        {
            Threat[] threats = [new(new Vector2(500, 0), 2000)];
            Assert.That(CompanionPetPullEscort.EscortPoint(new Vector3(1000, 0, 0), Camp, threats), Is.Null);
        }

        [Test]
        public void DamageOnlyNearCamp()
        {
            Assert.That(CompanionPetPullEscort.MayDamage(new Vector3(500, 0, 0), Camp), Is.True);
            Assert.That(CompanionPetPullEscort.MayDamage(new Vector3(800, 0, 0), Camp), Is.False);
        }
    }
}
