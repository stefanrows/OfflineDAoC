using NUnit.Framework;

namespace OfflineDaoc.Launcher;

[TestFixture]
public sealed class ClientUiPresetTests
{
    private const string CharacterIni = """
        [Panels]
        Main=10,20,300,400
        [Quickbar]
        GroupSize=10
        Hotkey_0=10,428,Lesser Rune of Mazing,1058
        Hotkey_1=52,0,Macro #0,1473
        [Quickbar2]
        GroupSize=10
        Hotkey_0=10,429,Spell Two,1059
        Hotkey_2=52,0,Macro #1,1474
        [quickbar3]
        GroupSize=10
        Hotkey_3 = 10,430,Spell Three,1060
        Hotkey_4 = 52 , 0 , Macro #2 , 1475
        [Chat]
        Hotkey_0=10,1,Not a quickbar,5
        [QuickBinds]
        Hotkey_1=10,2,Also not a quickbar,6
        """;

    private const string SanitizedIni = """
        [Panels]
        Main=10,20,300,400
        [Quickbar]
        GroupSize=10
        Hotkey_1=52,0,Macro #0,1473
        [Quickbar2]
        GroupSize=10
        Hotkey_2=52,0,Macro #1,1474
        [quickbar3]
        GroupSize=10
        Hotkey_4 = 52 , 0 , Macro #2 , 1475
        [Chat]
        Hotkey_0=10,1,Not a quickbar,5
        [QuickBinds]
        Hotkey_1=10,2,Also not a quickbar,6
        """;

    [Test]
    public void SanitizeDropsSpellSlotsInEveryQuickbarAndKeepsEverythingElse()
    {
        IReadOnlyList<string> result = ClientUiPreset.Sanitize(CharacterIni.Split('\n'));

        Assert.That(result, Is.EqualTo(SanitizedIni.Split('\n')));
    }

    [Test]
    public void SanitizeKeepsSpellSlotsOutsideTheQuickbarSections()
    {
        IReadOnlyList<string> result = ClientUiPreset.Sanitize(["[Macros]", "Hotkey_0=10,1,Spell,2"]);

        Assert.That(result, Is.EqualTo(new[] { "[Macros]", "Hotkey_0=10,1,Spell,2" }));
    }

    [Test]
    public void ListCharacterFilesKeepsOnlyCharacterInisNewestFirst()
    {
        using var workspace = new TempWorkspace();
        string oldest = WriteFile(workspace.Profile, "Ceoruner-5.ini", DateTime.UtcNow.AddHours(-3));
        string middle = WriteFile(workspace.Profile, "Sapphire-12.ini", DateTime.UtcNow.AddHours(-2));
        string newest = WriteFile(workspace.Profile, "Armand-9.ini", DateTime.UtcNow.AddHours(-1));
        WriteFile(workspace.Profile, "Ceoruner-1.ign", DateTime.UtcNow);
        WriteFile(workspace.Profile, "default2560.ini", DateTime.UtcNow);
        WriteFile(workspace.Profile, "notes.ini", DateTime.UtcNow);
        WriteFile(workspace.Profile, "Ceoruner-x.ini", DateTime.UtcNow);
        WriteFile(workspace.Profile, "-5.ini", DateTime.UtcNow);
        WriteFile(workspace.Profile, PresetName, DateTime.UtcNow);
        WriteFile(workspace.Profile, "Ceoruner-5.ini.uipreset-backup", DateTime.UtcNow);

        Assert.That(ClientUiPreset.ListCharacterFiles(workspace.Profile), Is.EqualTo(new[] { newest, middle, oldest }));
    }

    [Test]
    public void ListCharacterFilesIsEmptyWhenTheProfileDirectoryIsMissing()
    {
        using var workspace = new TempWorkspace();

        Assert.That(ClientUiPreset.ListCharacterFiles(Path.Combine(workspace.Root, "missing")), Is.Empty);
    }

    [Test]
    public void ProfileDirectoryUsesTheResolvedProfileNameUnderTheGivenRootWithoutCreatingIt()
    {
        using var workspace = new TempWorkspace();
        File.WriteAllLines(Path.Combine(workspace.ClientDirectory, "paths.dat"), ["settings=ForkProfile"]);

        string profile = ClientUiPreset.ProfileDirectory(workspace.ClientDirectory, workspace.ProfilesRoot);

        Assert.That(profile, Is.EqualTo(Path.Combine(workspace.ProfilesRoot, "ForkProfile")));
        Assert.That(Directory.Exists(profile), Is.False);
    }

    [Test]
    public void SaveWritesTheSanitizedPresetWithTheSourceLineEndings()
    {
        using var workspace = new TempWorkspace();
        string character = Path.Combine(workspace.Profile, "Ceoruner-5.ini");
        File.WriteAllText(character, CharacterIni.Replace("\n", "\r\n") + "\r\n");

        ClientUiPreset.Save(workspace.Profile, character);

        Assert.That(File.ReadAllText(Path.Combine(workspace.Profile, PresetName)), Is.EqualTo(SanitizedIni.Replace("\n", "\r\n") + "\r\n"));
    }

    [Test]
    public void SaveWithAMissingCharacterIniThrowsFileNotFound()
    {
        using var workspace = new TempWorkspace();

        Assert.That(
            () => ClientUiPreset.Save(workspace.Profile, Path.Combine(workspace.Profile, "Nobody-1.ini")),
            Throws.InstanceOf<FileNotFoundException>());
        Assert.That(ClientUiPreset.HasPreset(workspace.Profile), Is.False);
    }

    [Test]
    public void SaveThenApplyKeepsTheFirstBackupAcrossLaterApplies()
    {
        using var workspace = new TempWorkspace();
        string character = Path.Combine(workspace.Profile, "Ceoruner-5.ini");
        string backup = character + ".uipreset-backup";
        File.WriteAllText(character, CharacterIni + "\n");

        ClientUiPreset.Save(workspace.Profile, character);
        Assert.That(ClientUiPreset.HasPreset(workspace.Profile), Is.True);
        ClientUiPreset.ApplyToCharacter(workspace.Profile, character);

        Assert.That(File.ReadAllText(backup), Is.EqualTo(CharacterIni + "\n"));
        Assert.That(File.ReadAllText(character), Is.EqualTo(SanitizedIni + "\n"));

        File.WriteAllText(character, "[Panels]\nMain=1,2,3,4\n");
        ClientUiPreset.ApplyToCharacter(workspace.Profile, character);

        Assert.That(File.ReadAllText(backup), Is.EqualTo(CharacterIni + "\n"));
        Assert.That(File.ReadAllText(character), Is.EqualTo(SanitizedIni + "\n"));
    }

    [Test]
    public void ApplyToANewCharacterWritesThePresetWithoutABackupAndIsRepeatable()
    {
        using var workspace = new TempWorkspace();
        string source = Path.Combine(workspace.Profile, "Ceoruner-5.ini");
        File.WriteAllText(source, CharacterIni + "\n");
        ClientUiPreset.Save(workspace.Profile, source);
        string newCharacter = Path.Combine(workspace.Profile, "Armand-9.ini");

        ClientUiPreset.ApplyToCharacter(workspace.Profile, newCharacter);
        ClientUiPreset.ApplyToCharacter(workspace.Profile, newCharacter);

        Assert.That(File.ReadAllText(newCharacter), Is.EqualTo(SanitizedIni + "\n"));
        Assert.That(File.Exists(newCharacter + ".uipreset-backup"), Is.False);
    }

    [Test]
    public void ApplyToCharacterWithoutAPresetThrowsAndTouchesNothing()
    {
        using var workspace = new TempWorkspace();
        string character = Path.Combine(workspace.Profile, "Ceoruner-5.ini");
        File.WriteAllText(character, CharacterIni + "\n");

        Assert.That(
            () => ClientUiPreset.ApplyToCharacter(workspace.Profile, character),
            Throws.InvalidOperationException);
        Assert.That(File.ReadAllText(character), Is.EqualTo(CharacterIni + "\n"));
        Assert.That(File.Exists(character + ".uipreset-backup"), Is.False);
    }

    [Test]
    public void ReadScreenWidthReadsTheFirstValidValueInAnySectionIgnoringKeyCase()
    {
        using var workspace = new TempWorkspace();
        File.WriteAllText(Path.Combine(workspace.Profile, "user.dat"), "[keyboard]\r\nforward=w\r\n[main]\r\nScreen_Width = 2560\r\nscreen_height=1440\r\n");

        Assert.That(ClientUiPreset.ReadScreenWidth(workspace.Profile), Is.EqualTo(2560));
    }

    [TestCase("screen_width=abc")]
    [TestCase("screen_width=")]
    [TestCase("screen_width=0")]
    [TestCase("screen_width=-1280")]
    public void ReadScreenWidthIsNullForInvalidValues(string line)
    {
        using var workspace = new TempWorkspace();
        File.WriteAllText(Path.Combine(workspace.Profile, "user.dat"), line + "\n");

        Assert.That(ClientUiPreset.ReadScreenWidth(workspace.Profile), Is.Null);
    }

    [Test]
    public void ReadScreenWidthIsNullWhenUserDatIsMissing()
    {
        using var workspace = new TempWorkspace();

        Assert.That(ClientUiPreset.ReadScreenWidth(workspace.Profile), Is.Null);
    }

    [Test]
    public void InstallAsNewCharacterDefaultWritesThePresetAndBacksUpTheOriginal()
    {
        using var workspace = new TempWorkspace();
        SavePresetAndScreenWidth(workspace, "screen_width=2560");
        string original = Path.Combine(workspace.ClientDirectory, "default2560.ini");
        File.WriteAllText(original, "[Quickbar]\nHotkey_0=10,1,Old spell,2\n");

        string installed = ClientUiPreset.InstallAsNewCharacterDefault(workspace.ClientDirectory, workspace.Profile)!;

        Assert.That(installed, Is.EqualTo(original));
        Assert.That(File.ReadAllText(original), Is.EqualTo(SanitizedIni + "\n"));
        Assert.That(File.ReadAllText(original + ".uipreset-backup"), Is.EqualTo("[Quickbar]\nHotkey_0=10,1,Old spell,2\n"));
    }

    [Test]
    public void InstallAsNewCharacterDefaultIsIdempotent()
    {
        using var workspace = new TempWorkspace();
        SavePresetAndScreenWidth(workspace, "screen_width=2560");
        string original = Path.Combine(workspace.ClientDirectory, "default2560.ini");
        File.WriteAllText(original, "[Quickbar]\nHotkey_0=10,1,Old spell,2\n");

        string first = ClientUiPreset.InstallAsNewCharacterDefault(workspace.ClientDirectory, workspace.Profile)!;
        string second = ClientUiPreset.InstallAsNewCharacterDefault(workspace.ClientDirectory, workspace.Profile)!;

        Assert.That(second, Is.EqualTo(first));
        Assert.That(File.ReadAllText(original), Is.EqualTo(SanitizedIni + "\n"));
        Assert.That(File.ReadAllText(original + ".uipreset-backup"), Is.EqualTo("[Quickbar]\nHotkey_0=10,1,Old spell,2\n"));
    }

    [Test]
    public void InstallWithoutAnExistingDefaultCreatesNoBackupEvenWhenRepeated()
    {
        using var workspace = new TempWorkspace();
        SavePresetAndScreenWidth(workspace, "screen_width=2560");
        string target = Path.Combine(workspace.ClientDirectory, "default2560.ini");

        ClientUiPreset.InstallAsNewCharacterDefault(workspace.ClientDirectory, workspace.Profile);
        ClientUiPreset.InstallAsNewCharacterDefault(workspace.ClientDirectory, workspace.Profile);

        Assert.That(File.ReadAllText(target), Is.EqualTo(SanitizedIni + "\n"));
        Assert.That(File.Exists(target + ".uipreset-backup"), Is.False);
    }

    [Test]
    public void InstallReturnsNullWithoutAPreset()
    {
        using var workspace = new TempWorkspace();
        File.WriteAllText(Path.Combine(workspace.Profile, "user.dat"), "screen_width=2560\n");

        Assert.That(ClientUiPreset.InstallAsNewCharacterDefault(workspace.ClientDirectory, workspace.Profile), Is.Null);
        Assert.That(File.Exists(Path.Combine(workspace.ClientDirectory, "default2560.ini")), Is.False);
    }

    [Test]
    public void InstallReturnsNullWithoutAScreenWidth()
    {
        using var workspace = new TempWorkspace();
        string character = Path.Combine(workspace.Profile, "Ceoruner-5.ini");
        File.WriteAllText(character, CharacterIni + "\n");
        ClientUiPreset.Save(workspace.Profile, character);

        Assert.That(ClientUiPreset.InstallAsNewCharacterDefault(workspace.ClientDirectory, workspace.Profile), Is.Null);
        Assert.That(Directory.GetFiles(workspace.ClientDirectory), Is.Empty);
    }

    [Test]
    public void ClearRestoresDefaultBackupsRemovesThePresetAndLeavesCharacterFiles()
    {
        using var workspace = new TempWorkspace();
        SavePresetAndScreenWidth(workspace, "screen_width=2560");
        string character = Path.Combine(workspace.Profile, "Ceoruner-5.ini");
        string original = Path.Combine(workspace.ClientDirectory, "default2560.ini");
        File.WriteAllText(original, "[Quickbar]\nHotkey_0=10,1,Old spell,2\n");
        ClientUiPreset.InstallAsNewCharacterDefault(workspace.ClientDirectory, workspace.Profile);

        ClientUiPreset.Clear(workspace.ClientDirectory, workspace.Profile);

        Assert.That(File.ReadAllText(original), Is.EqualTo("[Quickbar]\nHotkey_0=10,1,Old spell,2\n"));
        Assert.That(File.Exists(original + ".uipreset-backup"), Is.False);
        Assert.That(ClientUiPreset.HasPreset(workspace.Profile), Is.False);
        Assert.That(File.ReadAllText(character), Is.EqualTo(CharacterIni + "\n"));
    }

    [Test]
    public void ClearRemovesADefaultThatTheInstallCreated()
    {
        using var workspace = new TempWorkspace();
        string client = Path.Combine(workspace.Root, "client");
        string profile = Path.Combine(workspace.Root, "profile");
        Directory.CreateDirectory(client);
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, "user.dat"), "screen_width=2560\n");
        string character = Path.Combine(profile, "Hero-5.ini");
        File.WriteAllText(character, CharacterIni);
        ClientUiPreset.Save(profile, character);

        string? target = ClientUiPreset.InstallAsNewCharacterDefault(client, profile);
        Assert.That(File.Exists(target), Is.True);
        ClientUiPreset.Clear(client, profile);

        Assert.That(File.Exists(target), Is.False);
        Assert.That(Directory.GetFiles(client), Is.Empty);
    }

    [Test]
    public void ClearDoesNotThrowWhenNothingExists()
    {
        using var workspace = new TempWorkspace();

        Assert.DoesNotThrow(() => ClientUiPreset.Clear(
            Path.Combine(workspace.Root, "missing-client"),
            Path.Combine(workspace.Root, "missing-profile")));
    }

    private const string PresetName = "ui-preset.ini";

    private static void SavePresetAndScreenWidth(TempWorkspace workspace, string userDatLine)
    {
        string character = Path.Combine(workspace.Profile, "Ceoruner-5.ini");
        File.WriteAllText(character, CharacterIni + "\n");
        ClientUiPreset.Save(workspace.Profile, character);
        File.WriteAllText(Path.Combine(workspace.Profile, "user.dat"), userDatLine + "\n");
    }

    private static string WriteFile(string directory, string name, DateTime lastWriteUtc)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, "[Panels]\n");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"offline-daoc-ui-preset-{Guid.NewGuid():N}");
            ClientDirectory = Path.Combine(Root, "client");
            ProfilesRoot = Path.Combine(Root, "profiles");
            Profile = Path.Combine(ProfilesRoot, "TestProfile");
            Directory.CreateDirectory(ClientDirectory);
            Directory.CreateDirectory(Profile);
        }

        public string Root { get; }
        public string ClientDirectory { get; }
        public string ProfilesRoot { get; }
        public string Profile { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
    }
}
