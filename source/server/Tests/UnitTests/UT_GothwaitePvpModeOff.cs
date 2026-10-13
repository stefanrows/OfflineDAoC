using System;
using System.IO;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The mode-off regression contract (docs/GOTHWAITE_PVP_MODE.md section 5.5). With the Gothwaite PvP mode off every
    /// hook answers "no change", so the server behaves as it did before the mode existed.
    /// </summary>
    [TestFixture]
    public class UT_GothwaitePvpModeOff
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            GothwaitePvpMode.Reset();
            _directory = Path.Combine(Path.GetTempPath(), "gothwaite-off-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            GothwaitePvpMode.Reset();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }

        [Test]
        public void AMissingSettingsFileLeavesTheModeOff()
        {
            GothwaitePvpMode.Initialize(_directory);

            Assert.That(GothwaitePvpMode.IsActive, Is.False);
        }

        [Test]
        public void TheCastleIsNotASafeAreaWhileTheModeIsOff()
        {
            Assert.That(GothwaitePvpRules.IsCastleSafe(51, 534958, 548813), Is.False);
            Assert.That(GothwaitePvpRules.IsCastleSafe(51, 534958 + 3900, 548813), Is.False);
            Assert.That(PvpCombatant.IsSafeArea(null), Is.False);
            Assert.That(GothwaitePvpRules.BlocksSafeImpact(null, null), Is.False, "no spell impact is blocked while the mode is off");
        }

        [Test]
        public void NoBotIsDispatchedToTheModeWhileTheModeIsOff()
        {
            Assert.That(GothwaitePvpMode.IsModeBot(false, true, false, false, false), Is.False);
            Assert.That(GothwaitePvpMode.IsModeBot(null), Is.False);
        }

        [Test]
        public void NoDirectorIsSuppressedAndNoBotIsLiftedWhileTheModeIsOff()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpRules.SuppressesWorldRecruitment, Is.False);
                Assert.That(GothwaitePvpRules.LiftsLowLevelProtection(null), Is.False);
                Assert.That(GothwaitePvpRules.SkipsConstitutionLoss(null), Is.False);
            });
        }

        [Test]
        public void NoCastleLandingIsAssignedWhileTheModeIsOff()
        {
            bool assigned = GothwaitePvpRules.TryCastleLanding(null, out ushort regionId, out Point3D point);

            Assert.Multiple(() =>
            {
                Assert.That(assigned, Is.False);
                Assert.That(regionId, Is.EqualTo(0));
                Assert.That(point, Is.Null);
            });
        }

        [Test]
        public void SubTenProtectionOfPlayersAndSafetyFlagsIsUnchanged()
        {
            Assert.Multiple(() =>
            {
                Assert.That(PvpCombatant.IsSafetyProtected(5, true, false), Is.True);
                Assert.That(PvpCombatant.IsSafetyProtected(5, false, false), Is.False);
                Assert.That(PvpCombatant.IsSafetyProtected(10, true, false), Is.False);
                Assert.That(PvpCombatant.IsSafetyProtected(5, true, true), Is.False);
            });
        }

        [Test]
        public void ThePlannerDoesNothingWithTheModeOffAndNoStoredHomes()
        {
            var bots = new[]
            {
                new OfflineWorldBotRecord { BotId = 1, Name = "A", RegionId = 1, X = 10, Y = 20, Z = 30 },
                new OfflineWorldBotRecord { BotId = 2, Name = "B", RegionId = 51, X = 534958, Y = 548813, Z = 4800 },
            };

            GothwaitePvpHomePlan plan = GothwaitePvpHomeStore.Plan(false, bots, Array.Empty<GothwaitePvpHomeRecord>());

            Assert.Multiple(() =>
            {
                Assert.That(plan.ToSnapshot, Is.Empty);
                Assert.That(plan.ToRestore, Is.Empty);
                Assert.That(plan.Orphans, Is.Empty);
                Assert.That(plan.Kept, Is.EqualTo(0));
            });
        }

        [Test]
        public void ThePlannerNeverSnapshotsWhileTheModeIsOff()
        {
            var bots = new[]
            {
                new OfflineWorldBotRecord { BotId = 1, Name = "A", RegionId = 1 },
                new OfflineWorldBotRecord { BotId = 2, Name = "B", RegionId = 2 },
            };
            var homes = new[] { new GothwaitePvpHomeRecord { HomeId = 1, BotId = 2, RegionId = 2 } };

            GothwaitePvpHomePlan plan = GothwaitePvpHomeStore.Plan(false, bots, homes);

            Assert.That(plan.ToSnapshot, Is.Empty);
        }
    }
}
