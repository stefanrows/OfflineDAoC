// Upstream test (shadowofze/OfflineDAoC 0.38c, upstream/main 9da90910 source/server/Tests/UnitTests/UT_SiegeMusterAndRankOct6.cs) for the RvR world's event layer,
// carried in port session S1 (docs/RVR_PORT_MAP.md). It calls RealmRvrEventLayer, the upstream copy behind the
// AutonomousRvrEventLayer facade; the shared records stay on the facade. Local changes are marked "Fork:".
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_RealmRvr_SiegeMusterAndRankOct6
    {
        [TestCase(eRealm.Midgard, (ushort)1, (ushort)1, 655200f, TestName = "Midgard attacking Albion musters at its Hadrian's Wall outpost")]
        [TestCase(eRealm.Hibernia, (ushort)1, (ushort)1, 605743f, TestName = "Hibernia attacking Albion musters at its own outpost")]
        [TestCase(eRealm.Albion, (ushort)200, (ushort)200, 475835f, TestName = "Albion attacking Hibernia musters in Emain Macha")]
        [TestCase(eRealm.Midgard, (ushort)100, (ushort)100, 766235f, TestName = "A home-region target falls back to the border keep")]
        public void SiegeArmiesMusterInsideTheTargetFrontier(eRealm realm, ushort target, ushort region, float x)
        {
            Assert.That(AutonomousRvrStaging.TryGetSiegeMuster(realm, target, out var muster), Is.True);
            Assert.That(muster.RegionId, Is.EqualTo(region));
            Assert.That(muster.Position.X, Is.EqualTo(x));
        }

        // Fork: upstream's pet-rank test (AutonomousPetSupport.ChooseWeightedByRank) is not RvR and not ported.
    }
}
