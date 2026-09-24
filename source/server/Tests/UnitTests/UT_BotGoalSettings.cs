using System;
using System.IO;
using DOL.GS;
using NUnit.Framework;
using OfflineDaoc.Configuration;

[TestFixture, NonParallelizable]
public sealed class UT_BotGoalSettings
{
    private string _directory;
    private string PathName => Path.Combine(_directory, BotGoalSettings.FileName);

    [SetUp] public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "population-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown] public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        AutonomousBotGoalPolicy.Initialize(_directory);
    }

    [TestCase(PopulationPreset.Camlann2003, 25, 10, 30, 15, 12, 8, PopulationDanger.Authentic)]
    [TestCase(PopulationPreset.Peaceful, 45, 20, 25, 3, 5, 2, PopulationDanger.Mild)]
    [TestCase(PopulationPreset.Bloodbath, 10, 5, 25, 30, 20, 10, PopulationDanger.FullCamlann)]
    [TestCase(PopulationPreset.KeepWars, 15, 5, 25, 10, 20, 25, PopulationDanger.Authentic)]
    public void NamedPresetsHaveValidTypeMix(PopulationPreset preset, int leveler, int casual,
        int hybrid, int hunter, int roamer, int keepWarrior, PopulationDanger danger)
    {
        BotGoalSettings settings = BotGoalSettings.ForPreset(preset);
        Assert.Multiple(() =>
        {
            Assert.That(settings.Mix, Is.EqualTo(new PlayerTypeMix(leveler, casual, hybrid, hunter, roamer, keepWarrior)));
            Assert.That(settings.Mix.Total, Is.EqualTo(100));
            Assert.That(settings.Danger, Is.EqualTo(danger));
            Assert.DoesNotThrow(settings.Validate);
        });
    }

    [Test]
    public void CustomMixValidationAndAtomicSavePreserveLastGoodFile()
    {
        BotGoalSettings.Defaults.Save(PathName, () => true);
        string original = File.ReadAllText(PathName);
        BotGoalSettings bad = BotGoalSettings.Defaults with
        {
            Preset = PopulationPreset.Custom,
            Mix = new PlayerTypeMix(50, 10, 30, 15, 12, 8),
        };
        Assert.Throws<InvalidDataException>(() => bad.Save(PathName, () => true));
        Assert.Throws<InvalidDataException>(() => (BotGoalSettings.Defaults with
            { Danger = PopulationDanger.FullCamlann }).Save(PathName, () => true));
        Assert.Throws<InvalidOperationException>(() => BotGoalSettings.Defaults.Save(PathName, () => false));
        int calls = 0;
        Assert.Throws<InvalidOperationException>(() => BotGoalSettings.Defaults.Save(PathName, () => ++calls == 1));
        Assert.That(File.ReadAllText(PathName), Is.EqualTo(original));
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
    }

    [Test]
    public void VersionOneMapsToNearestPresetWithoutWritingUntilSave()
    {
        string legacy = """
            {"Version":1,"Levels1To19":{"SoloPve":10,"GroupPve":90,"RvR":0},
             "Levels20To49":{"SoloPve":20,"GroupPve":70,"RvR":10},
             "Level50":{"SoloPve":20,"GroupPve":40,"RvR":40}}
            """;
        File.WriteAllText(PathName, legacy);
        BotGoalLoadResult loaded = BotGoalSettings.LoadDetailed(PathName);
        Assert.That(loaded.MigratedFromV1, Is.True);
        Assert.That(loaded.Settings.Version, Is.EqualTo(2));
        Assert.That(loaded.Settings.Preset, Is.EqualTo(loaded.MappedPreset));
        Assert.That(File.ReadAllText(PathName), Is.EqualTo(legacy));
        AutonomousBotGoalPolicy.Initialize(_directory);
        Assert.That(AutonomousBotGoalPolicy.LegacyFileMapped, Is.True);
        loaded.Settings.Save(PathName, () => true);
        Assert.That(BotGoalSettings.LoadDetailed(PathName).MigratedFromV1, Is.False);
    }

    [Test]
    public void StartupReadsDangerAndMixWithoutTouchingSavedBotTask()
    {
        BotGoalSettings settings = BotGoalSettings.ForPreset(PopulationPreset.Bloodbath) with
            { WorldShape = PopulationWorldShape.Established };
        settings.Save(PathName, () => true);
        AutonomousBotGoalPolicy.Initialize(_directory);
        var record = new OfflineWorldBotRecord { PlayerType = "Leveler", Level = 41,
            ObjectiveKind = "GroupPve", ObjectiveAssignmentId = "saved", Experience = 12345 };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGoalPolicy.Settings.Mix.Hunter, Is.EqualTo(30));
            Assert.That(AutonomousBotGoalPolicy.Settings.WorldShape, Is.EqualTo(PopulationWorldShape.Established));
            Assert.That(AutonomousBotGoalPolicy.Danger, Is.EqualTo(AutonomousLevelingDanger.FullCamlann));
            Assert.That(record.ObjectiveAssignmentId, Is.EqualTo("saved"));
            Assert.That(record.Experience, Is.EqualTo(12345));
        });
    }

    [Test]
    public void SelectedMixCanExcludeTypesForNewBotsWithoutChangingStampedIdentity()
    {
        var onlyLevelers = new PlayerTypeMix(100, 0, 0, 0, 0, 0);
        for (int ordinal = 0; ordinal < 20; ordinal++)
        {
            Assert.That(AutonomousBotIdentity.CharterForOrdinal(ordinal, onlyLevelers),
                Is.AnyOf(AutonomousGuildCharter.Leveling, AutonomousGuildCharter.Social));
            Assert.That(AutonomousBotIdentity.TypeFor(ordinal + 1, 1, "guild", AutonomousGuildCharter.Hunting, onlyLevelers),
                Is.EqualTo(AutonomousPlayerType.Leveler));
        }
        var stamped = new OfflineWorldBotRecord { BotId = 10, ClassId = 1,
            GuildId = "guild", PlayerType = "Hunter", Experience = 12345 };
        Assert.That(AutonomousBotIdentity.Ensure(stamped, AutonomousGuildCharter.Leveling, onlyLevelers), Is.False);
        Assert.That(stamped.PlayerType, Is.EqualTo("Hunter"));
        Assert.That(stamped.Experience, Is.EqualTo(12345));
    }

    [TestCase("{}")] [TestCase("null")] [TestCase("not json")]
    [TestCase("{\"Version\":2,\"Preset\":\"Custom\",\"Mix\":null}")]
    [TestCase("{\"Version\":9}")]
    public void BrokenFilesAreRejected(string json)
    {
        File.WriteAllText(PathName, json);
        Assert.That(() => BotGoalSettings.Load(PathName), Throws.Exception);
    }
}
