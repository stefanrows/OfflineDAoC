using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public sealed class UT_AutonomousPartyContinuity
    {
        [Test]
        public void ProductivePartyMayContinueOnlyOnce()
        {
            Assert.That(Eligible(renewals: 0), Is.True);
            Assert.That(Eligible(renewals: 1), Is.False);
        }

        [Test]
        public void CappedExperienceRequiresProvenKills()
        {
            Assert.That(Eligible(experience: 0, kills: 0), Is.False);
            Assert.That(Eligible(experience: 0, kills: 3), Is.True);
        }

        [Test]
        public void ContinuationRespectsServicesCasualtiesAndOtherActivities()
        {
            Assert.That(Eligible(service: true), Is.False);
            Assert.That(Eligible(ready: false), Is.False);
            Assert.That(Eligible(recovering: true), Is.False);
            Assert.That(Eligible(wipes: 1), Is.False);
            Assert.That(Eligible(raid: true), Is.False);
            Assert.That(Eligible(objective: eAutonomousObjectiveKind.RvR), Is.False);
            Assert.That(Eligible(objective: eAutonomousObjectiveKind.SoloPve), Is.False);
            Assert.That(Eligible(members: 1), Is.False);
        }

        private static bool Eligible(int renewals = 0, int members = 4, bool raid = false,
            bool ready = true, bool service = false, bool recovering = false, int wipes = 0,
            long experience = 100, long kills = 0,
            eAutonomousObjectiveKind objective = eAutonomousObjectiveKind.GroupPve) =>
            AutonomousBotGroupCoordinator.CanContinuePveParty(objective, renewals, members,
                raid, ready, service, recovering, wipes, experience, kills);
    }
}
