using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_BattlegroundGarrisonFallback
    {
        [Test]
        public void GarrisonMembersKeepTwoHundredUnitsApart()
        {
            Assert.That(BattlegroundNativeKeepData.GuardSpacing, Is.EqualTo(200f));
        }

        [Test]
        public void FirstSpacedPointTakesThePreferredPointThatClearsEveryUsedPoint()
        {
            var used = new List<Vector3> { new(0, 0, 0) };
            var ordered = new List<Vector3> { new(100, 0, 0), new(250, 0, 0), new(0, 300, 0) };
            Assert.That(BattlegroundNativeKeepData.FirstSpacedPoint(ordered, used), Is.EqualTo(new Vector3(250, 0, 0)));
        }

        [Test]
        public void FirstSpacedPointIsNullWhenEveryCandidateCrowdsAUsedPoint()
        {
            var used = new List<Vector3> { new(0, 0, 0), new(300, 0, 0) };
            var ordered = new List<Vector3> { new(100, 0, 0), new(0, 150, 0) };
            Assert.That(BattlegroundNativeKeepData.FirstSpacedPoint(ordered, used), Is.Null);
        }

        [Test]
        public void SpacingIsMeasuredInThreeDimensions()
        {
            // 150 across the plane and 150 in height is about 212 apart: spaced.
            var used = new List<Vector3> { new(0, 0, 0) };
            var ordered = new List<Vector3> { new(150, 0, 150) };
            Assert.That(BattlegroundNativeKeepData.FirstSpacedPoint(ordered, used), Is.Not.Null);
        }
    }
}
