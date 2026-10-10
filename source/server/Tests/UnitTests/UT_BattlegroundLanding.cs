using DOL.GS;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    // Bug 147: a battleground entrant lands where its group leader lands; a solo or leading bot lands at its own realm's portal keep.
    [TestFixture]
    public class UT_BattlegroundLanding
    {
        // Landing order is by keep id: Cathal Valley is Albion, Midgard, Hibernia; Murdaigean is Hibernia, Midgard, Albion.
        private static readonly eRealm[] Cathal = { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };
        private static readonly eRealm[] Murdaigean = { eRealm.Hibernia, eRealm.Midgard, eRealm.Albion };

        [Test]
        public void SoloOrLeadingBotLandsAtItsOwnRealmPortalKeep()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundCampaignPolicy.ChooseLanding(eRealm.Hibernia, null, Cathal, 0), Is.EqualTo(2), "Eilrina in Cathal Valley");
                Assert.That(BattlegroundCampaignPolicy.ChooseLanding(eRealm.Midgard, null, Cathal, 0), Is.EqualTo(1));
                Assert.That(BattlegroundCampaignPolicy.ChooseLanding(eRealm.Albion, null, Cathal, 2), Is.EqualTo(0));
                Assert.That(BattlegroundCampaignPolicy.ChooseLanding(eRealm.Hibernia, null, Murdaigean, 2), Is.EqualTo(0), "keep id order, not realm order");
            });
        }

        [Test]
        public void FollowerLandsWhereItsLeaderLands()
        {
            // GetLanding passes the leader's realm and camp, so a Hibernian follower of an Albion leader goes to the Albion side.
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundCampaignPolicy.ChooseLanding(eRealm.Albion, 1, Cathal, 0), Is.EqualTo(1), "player leader's camp");
                Assert.That(BattlegroundCampaignPolicy.ChooseLanding(eRealm.Albion, null, Cathal, 1), Is.EqualTo(0), "bot leader's realm keep");
            });
        }

        [Test]
        public void NoOwnRealmLandingKeepsTheFallback()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundCampaignPolicy.ChooseLanding(eRealm.Hibernia, null, new[] { eRealm.Albion, eRealm.Midgard }, 1), Is.EqualTo(1));
                Assert.That(BattlegroundCampaignPolicy.ChooseLanding(eRealm.Hibernia, null, new eRealm[0], 2), Is.EqualTo(2), "landing realms unknown");
                Assert.That(BattlegroundCampaignPolicy.ChooseLanding(eRealm.Hibernia, null, null, 2), Is.EqualTo(2));
                Assert.That(BattlegroundCampaignPolicy.ChooseLanding(eRealm.None, null, Cathal, 1), Is.EqualTo(1), "no realm");
            });
        }

        [Test]
        public void SoloPlayerKeepsItsHashCamp()
        {
            // A player passes its hash camp as the leader landing, so its realm never changes the choice.
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                for (int hashed = 0; hashed < 3; hashed++)
                    Assert.That(BattlegroundCampaignPolicy.ChooseLanding(realm, hashed, Cathal, hashed), Is.EqualTo(hashed), $"{realm} {hashed}");
        }
    }
}
