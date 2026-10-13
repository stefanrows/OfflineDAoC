using System;
using System.IO;
using System.Text.Json;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Gothwaite PvP mode rules, the castle geometry, and the shared settings file (docs/GOTHWAITE_PVP_MODE.md sections 3,
    /// 5, 6.3 and 7). Tests that need the mode on call <see cref="TurnOn"/>; every test resets the mode afterwards.
    /// </summary>
    [TestFixture]
    public class UT_GothwaitePvpRules
    {
        private const ushort Isle = 51;
        private const int CastleX = 534958;
        private const int CastleY = 548813;

        private string _directory;

        [SetUp]
        public void SetUp()
        {
            GothwaitePvpMode.Reset();
            _directory = Path.Combine(Path.GetTempPath(), "gothwaite-rules-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            GothwaitePvpMode.Reset();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }

        private static void TurnOn() => GothwaitePvpMode.Apply(GothwaitePvpSettings.Defaults with { Enabled = true });

        [Test]
        public void TheCastleCentreIsSafeWhileTheModeIsOn()
        {
            TurnOn();

            Assert.That(GothwaitePvpRules.IsCastleSafe(Isle, CastleX, CastleY), Is.True);
        }

        [Test]
        public void TheCastleCircleEdgeIsFourThousandUnits()
        {
            TurnOn();

            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpRules.IsCastleSafe(Isle, CastleX + 3900, CastleY), Is.True);
                Assert.That(GothwaitePvpRules.IsCastleSafe(Isle, CastleX + 4100, CastleY), Is.False);
            });
        }

        [Test]
        public void GothwaiteHarbourIsNotASafeArea()
        {
            TurnOn();

            Assert.That(GothwaitePvpRules.IsCastleSafe(Isle, 525023, 542126), Is.False);
        }

        [Test]
        public void OtherRegionsAreNotSafeEvenAtTheCastleCoordinates()
        {
            TurnOn();

            Assert.That(GothwaitePvpRules.IsCastleSafe(1, CastleX, CastleY), Is.False);
        }

        [Test]
        public void OnlyAnActiveAutonomousWorldBotIsAModeBot()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpMode.IsModeBot(true, true, false, false, false), Is.True);
                Assert.That(GothwaitePvpMode.IsModeBot(false, true, false, false, false), Is.False, "mode off");
                Assert.That(GothwaitePvpMode.IsModeBot(true, false, false, false, false), Is.False, "companion or player");
                Assert.That(GothwaitePvpMode.IsModeBot(true, true, true, false, false), Is.False, "temporary helper");
                Assert.That(GothwaitePvpMode.IsModeBot(true, true, false, true, false), Is.False, "battleground encounter bot");
                Assert.That(GothwaitePvpMode.IsModeBot(true, true, false, false, true), Is.False, "player-led group");
            });
        }

        [Test]
        public void ModeOnRulesAnswerNoForANullLiving()
        {
            TurnOn();

            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpRules.SuppressesWorldRecruitment, Is.True);
                Assert.That(GothwaitePvpRules.LiftsLowLevelProtection(null), Is.False);
                Assert.That(GothwaitePvpRules.SkipsConstitutionLoss(null), Is.False);
                Assert.That(GothwaitePvpRules.TryCastleLanding(null, out _, out _), Is.False);
            });
        }

        [Test]
        public void CastleLandingStaysNearALandingAndInsideTheCircleForEveryBotId()
        {
            GothwaitePvpArena arena = GothwaitePvpArena.Gothwaite;

            for (long botId = 1; botId <= 500; botId++)
            {
                GothwaitePvpSpot landing = arena.CastleLanding(botId);
                double nearest = double.MaxValue;
                foreach (GothwaitePvpSpot spot in arena.CastleLandings)
                    nearest = Math.Min(nearest, Math.Sqrt(Math.Pow(landing.X - spot.X, 2) + Math.Pow(landing.Y - spot.Y, 2)));

                Assert.That(nearest, Is.LessThanOrEqualTo(250), $"bot {botId} lands too far from a castle landing");
                Assert.That(arena.InCastle(Isle, landing.X, landing.Y), Is.True, $"bot {botId} lands outside the circle");
            }
        }

        [Test]
        public void DefaultSettingsAreValidAndTheModeIsOff()
        {
            GothwaitePvpSettings defaults = GothwaitePvpSettings.Defaults;

            Assert.Multiple(() =>
            {
                Assert.That(defaults.Enabled, Is.False);
                Assert.That(defaults.Arena, Is.EqualTo("gothwaite"));
                Assert.That(defaults.SafeRadius, Is.EqualTo(4000));
                Assert.That(defaults.LowBandMaxLevel, Is.EqualTo(19));
                Assert.That(defaults.LowBandEdge, Is.EqualTo(20000));
                Assert.That(defaults.Edge, Is.EqualTo(30000));
                Assert.That(defaults.LevelBand, Is.EqualTo(5));
                Assert.That(defaults.RestPercent, Is.EqualTo(80));
                Assert.That(defaults.GroupShares.Total, Is.EqualTo(100));
                Assert.That(() => defaults.Validate(), Throws.Nothing);
            });
        }

        [Test]
        public void SettingsSurviveAJsonRoundTrip()
        {
            GothwaitePvpSettings settings = GothwaitePvpSettings.Defaults with
            {
                Enabled = true,
                SafeRadius = 3500,
                LevelBand = 7,
                RestPercent = 90,
                GroupShares = new GothwaitePvpGroupShares(10, 20, 30, 40),
            };
            string path = Path.Combine(_directory, GothwaitePvpSettings.FileName);
            File.WriteAllText(path, settings.Serialize());

            GothwaitePvpSettings loaded = GothwaitePvpSettings.Load(path);

            Assert.That(loaded, Is.EqualTo(settings));
        }

        [Test]
        public void AMissingSettingsFileLoadsAsTheDefaults()
        {
            GothwaitePvpSettings loaded = GothwaitePvpSettings.Load(Path.Combine(_directory, "absent.json"));

            Assert.That(loaded, Is.EqualTo(GothwaitePvpSettings.Defaults));
        }

        [Test]
        public void ValidateRejectsGroupSharesThatDoNotTotalOneHundred()
        {
            GothwaitePvpSettings settings = GothwaitePvpSettings.Defaults with { GroupShares = new GothwaitePvpGroupShares(10, 10, 10, 10) };

            Assert.That(() => settings.Validate(), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void ValidateRejectsAnEdgeInsideTheLowBandEdge()
        {
            GothwaitePvpSettings settings = GothwaitePvpSettings.Defaults with { Edge = 15000 };

            Assert.That(() => settings.Validate(), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void ValidateRejectsAnUnknownArena()
        {
            GothwaitePvpSettings settings = GothwaitePvpSettings.Defaults with { Arena = "harbour" };

            Assert.That(() => settings.Validate(), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void LoadRejectsAnUnknownJsonMember()
        {
            string path = Path.Combine(_directory, GothwaitePvpSettings.FileName);
            File.WriteAllText(path, "{ \"enabled\": true, \"bogus\": 1 }");

            Assert.That(() => GothwaitePvpSettings.Load(path), Throws.InstanceOf<JsonException>());
        }

        [Test]
        public void SaveRefusesWhileTheServerIsRunning()
        {
            string path = Path.Combine(_directory, GothwaitePvpSettings.FileName);

            Assert.That(() => GothwaitePvpSettings.Defaults.Save(path, () => false), Throws.InvalidOperationException);
            Assert.That(File.Exists(path), Is.False);
        }

        [Test]
        public void SaveWritesTheFileWhileTheServerIsStopped()
        {
            string path = Path.Combine(_directory, GothwaitePvpSettings.FileName);
            GothwaitePvpSettings settings = GothwaitePvpSettings.Defaults with { Enabled = true };

            settings.Save(path, () => true);

            Assert.That(GothwaitePvpSettings.Load(path), Is.EqualTo(settings));
        }

        [Test]
        public void InitializeTurnsTheModeOnFromAValidFile()
        {
            string path = Path.Combine(_directory, GothwaitePvpSettings.FileName);
            File.WriteAllText(path, (GothwaitePvpSettings.Defaults with { Enabled = true }).Serialize());

            GothwaitePvpMode.Initialize(_directory);

            Assert.That(GothwaitePvpMode.IsActive, Is.True);
        }

        [Test]
        public void InitializeKeepsTheModeOffForACorruptFile()
        {
            string path = Path.Combine(_directory, GothwaitePvpSettings.FileName);
            File.WriteAllText(path, "{ \"enabled\": true, ");

            GothwaitePvpMode.Initialize(_directory);

            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpMode.IsActive, Is.False);
                Assert.That(GothwaitePvpMode.Settings, Is.EqualTo(GothwaitePvpSettings.Defaults));
            });
        }

        [Test]
        public void InitializeKeepsTheModeOffWhenTheFileFailsValidation()
        {
            string path = Path.Combine(_directory, GothwaitePvpSettings.FileName);
            File.WriteAllText(path, "{ \"enabled\": true, \"arena\": \"harbour\" }");

            GothwaitePvpMode.Initialize(_directory);

            Assert.That(GothwaitePvpMode.IsActive, Is.False);
        }
    }
}
