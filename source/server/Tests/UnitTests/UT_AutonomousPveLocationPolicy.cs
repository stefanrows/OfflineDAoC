using System;
using System.Text.Json;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public sealed class UT_AutonomousPveLocationPolicy
    {
        [Test]
        public void VisitorRemainsAfterActivityChangesAndSaveReload()
        {
            var record = new OfflineWorldBotRecord { Realm = 1, RegionId = 100, ObjectiveKind = "GroupPve" };
            foreach (string phase in new[] { "Task complete", "Between tasks", "Taking a town break", "Awaiting objective" })
            {
                record.ObjectiveKind = "SoloPve";
                record.ObjectivePhase = phase;
                record.ObjectiveAssignmentId = "next-task";
                var reloaded = JsonSerializer.Deserialize<OfflineWorldBotRecord>(JsonSerializer.Serialize(record));
                Assert.That(AutonomousObjectiveAssignments.ShouldDeferAutomaticForeignFrontierReturn(reloaded, false), Is.True, phase);
            }
        }

        [Test]
        public void ExplicitRvrReturnSurvivesDowntimeButFinishesIndependentlyOfPveRequirement()
        {
            var record = new OfflineWorldBotRecord
            {
                ObjectiveKind = "SoloPve",
                ObjectiveRvrEligibleUtc = AutonomousObjectiveAssignments.PveCompletionRequired,
                ObjectivePhase = "Taking a town break",
            };
            record = JsonSerializer.Deserialize<OfflineWorldBotRecord>(JsonSerializer.Serialize(record));
            Assert.That(AutonomousObjectiveAssignments.ShouldDeferAutomaticForeignFrontierReturn(record, false), Is.False);
            Assert.That(AutonomousObjectiveAssignments.CompletePostRvrReturn(record), Is.True);
            record.ObjectivePhase = "Traveling with a new party";
            Assert.That(AutonomousObjectiveAssignments.ShouldDeferAutomaticForeignFrontierReturn(record, false), Is.True);
            Assert.That(AutonomousObjectiveAssignments.IsRvrEligible(record, DateTime.UtcNow), Is.False);
            Assert.That(AutonomousObjectiveAssignments.CompletePostRvrReturn(record), Is.False);
            record.ObjectiveRvrEligibleUtc = string.Empty;
            Assert.That(AutonomousObjectiveAssignments.IsRvrEligible(record, DateTime.UtcNow), Is.True);
        }

        [Test]
        public void ActivePvePartyDefersReturnRegardlessOfRealmComposition()
        {
            var record = new OfflineWorldBotRecord
            {
                ObjectiveKind = "GroupPve",
                ObjectiveRvrEligibleUtc = AutonomousObjectiveAssignments.PveCompletionRequired,
            };
            Assert.That(AutonomousObjectiveAssignments.ShouldDeferAutomaticForeignFrontierReturn(record, true), Is.True);
            record.ObjectiveKind = "RvR";
            Assert.That(AutonomousObjectiveAssignments.ShouldDeferAutomaticForeignFrontierReturn(record, true), Is.False);
        }

        [Test]
        public void LegacyTimedRvrIntermissionRetainsItsReturnIntent()
        {
            DateTime now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
            var record = new OfflineWorldBotRecord { ObjectiveRvrEligibleUtc = now.AddMinutes(10).ToString("O") };
            Assert.That(AutonomousObjectiveAssignments.HasPostRvrReturnIntent(record, now), Is.True);
            Assert.That(AutonomousObjectiveAssignments.HasPostRvrReturnIntent(record, now.AddMinutes(11)), Is.False);
        }
    }
}
