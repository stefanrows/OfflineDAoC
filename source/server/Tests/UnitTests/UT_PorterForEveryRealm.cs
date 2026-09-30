using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Bug 74: every realm uses every frontier porter; stragglers follow their force.</summary>
    [TestFixture]
    public sealed class UT_PorterForEveryRealm
    {
        [Test]
        public void HumanLandsAtThePortersLanding()
        {
            Assert.Multiple(() =>
            {
                // A Hibernian at an Albion porter taking Odin lands where Albion lands.
                Assert.That(AutonomousFrontierTransport.PorterLanding(eRealm.Albion, "odin_necklace")?.Name,
                    Is.EqualTo(AutonomousFrontierTransport.Destination(eRealm.Albion, 100).Location.Name));
                Assert.That(AutonomousFrontierTransport.PorterLanding(eRealm.Midgard, "home_necklace")?.Name,
                    Is.EqualTo(AutonomousFrontierTransport.Destination(eRealm.Midgard, 100).Location.Name));
                // The porter's own frontier medallion leads to its home portal keep.
                Assert.That(AutonomousFrontierTransport.PorterLanding(eRealm.Albion, "hadrian_necklace")?.Name,
                    Is.EqualTo(AutonomousFrontierTransport.Destination(eRealm.Albion, 1).Location.Name));
                // Inner-keep medallions only at a porter of that realm.
                Assert.That(AutonomousFrontierTransport.PorterLanding(eRealm.Albion, "snowdonia_necklace"), Is.Not.Null);
                Assert.That(AutonomousFrontierTransport.PorterLanding(eRealm.Hibernia, "snowdonia_necklace"), Is.Null);
                Assert.That(AutonomousFrontierTransport.IsFrontierMedallion("city_necklace"), Is.False);
            });
        }

        [Test]
        public void StragglerOfAForceAlreadyAcrossBoardsAtOnce()
        {
            const long now = 1_000_000;
            Assert.Multiple(() =>
            {
                // Without a member across: the five-minute force cap holds.
                Assert.That(AutonomousFrontierTransport.DecideRegroup(true, true, null, null, false, now - 60_000, false, now),
                    Is.EqualTo(AutonomousFrontierTransport.RegroupDecision.DepartureCap));
                // With a member across: board, no cap, no regroup hold.
                Assert.That(AutonomousFrontierTransport.DecideRegroup(true, true, now - 90_000, now - 90_000, false, now - 60_000, false, now, true),
                    Is.EqualTo(AutonomousFrontierTransport.RegroupDecision.Board));
                // Its own release hold still applies.
                Assert.That(AutonomousFrontierTransport.DecideRegroup(true, true, now - 10_000, now - 10_000, false, now - 60_000, false, now, true),
                    Is.EqualTo(AutonomousFrontierTransport.RegroupDecision.ReleaseHold));
                Assert.That(AutonomousFrontierTransport.IsMixedRealm(new[] { eRealm.Albion, eRealm.Midgard }), Is.True);
                Assert.That(AutonomousFrontierTransport.IsMixedRealm(new[] { eRealm.Albion, eRealm.Albion }), Is.False);
            });
        }
    }
}
