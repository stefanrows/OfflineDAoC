using System;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_GothwaitePvpOpenBgStandby
    {
        [TestCase(true, true, false, false, true)]
        [TestCase(false, true, false, false, false)]
        [TestCase(true, false, false, false, false)]
        [TestCase(true, true, true, false, false)]
        [TestCase(true, true, false, true, false)]
        public void IsStandby_Only_For_Reserved_Bot_Outside_Any_Battleground_And_Not_Player_Led(
            bool mode, bool reserved, bool inBg, bool playerLed, bool expected) =>
            Assert.That(GothwaitePvpOpenBgStandby.IsStandby(mode, reserved, inBg, playerLed), Is.EqualTo(expected));

        [TestCase("RvR", false)]
        [TestCase("SoloPve", true)]
        [TestCase("GroupPve", true)]
        [TestCase("", true)]
        [TestCase(null, true)]
        public void NeedsRvrStamp_Unless_Already_RvR(string kind, bool expected) =>
            Assert.That(GothwaitePvpOpenBgStandby.NeedsRvrStamp(kind), Is.EqualTo(expected));

        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        public void LeavesGroup_Only_For_Bot_Only_Group_That_Is_Not_Open_Bg_Party(bool botOnly, bool openBg, bool expected) =>
            Assert.That(GothwaitePvpOpenBgStandby.LeavesGroup(botOnly, openBg), Is.EqualTo(expected));

        [TestCase(false, true, false, false, true)]
        [TestCase(true, true, false, false, false)]
        [TestCase(false, false, false, false, false)]
        [TestCase(false, true, true, false, false)]
        [TestCase(false, true, false, true, false)]
        public void NeedsPark_Only_When_Away_Alive_Calm_And_Not_Returning(
            bool atHub, bool alive, bool combat, bool returning, bool expected) =>
            Assert.That(GothwaitePvpOpenBgStandby.NeedsPark(atHub, alive, combat, returning), Is.EqualTo(expected));

        [Test]
        public void Stamp_Writes_Exactly_The_RvR_Branch_Fields()
        {
            var record = new OfflineWorldBotRecord
            {
                ObjectiveKind = "SoloPve", ObjectivePveMode = "Time", ObjectivePveKills = 7, ObjectiveRvrEligibleUtc = "x",
            };
            DateTime now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
            GothwaitePvpOpenBgStandby.Stamp(record, 42, now, TimeSpan.FromMinutes(90));
            Assert.That(record.ObjectiveKind, Is.EqualTo("RvR"));
            Assert.That(record.ObjectiveAssignmentId, Is.EqualTo("gothpvp-openbg-42"));
            Assert.That(record.ObjectiveAssignedUtc, Is.EqualTo(now.ToString("O")));
            Assert.That(record.ObjectiveExpiresUtc, Is.EqualTo(now.AddMinutes(90).ToString("O")));
            Assert.That(record.ObjectivePveMode, Is.Empty);
            Assert.That(record.ObjectivePveKills, Is.EqualTo(0));
            Assert.That(record.ObjectivePveKillTarget, Is.EqualTo(0));
            Assert.That(record.ObjectiveRvrEligibleUtc, Is.Empty);
            Assert.That(GothwaitePvpOpenBgStandby.NeedsRvrStamp(record.ObjectiveKind), Is.False);
        }
    }
}
