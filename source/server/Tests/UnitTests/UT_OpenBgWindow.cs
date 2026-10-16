using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_OpenBgWindow
{
    [Test]
    public void TryParseBgControl_AcceptsAllByteValues_AndRejectsMalformedInput()
    {
        Assert.That(CompanionManagerProtocol.TryParseBgControl("0001", "ff", out ushort revision, out int value), Is.True);
        Assert.That(revision, Is.EqualTo(1));
        Assert.That(value, Is.EqualTo(255));
        Assert.That(CompanionManagerProtocol.TryParseBgControl("00a0", "00", out revision, out value), Is.True);
        Assert.That((revision, value), Is.EqualTo((0xA0, 0)));
        Assert.That(CompanionManagerProtocol.TryParseBgControl("0001", "FF", out _, out _), Is.False);
        Assert.That(CompanionManagerProtocol.TryParseBgControl("001", "ff", out _, out _), Is.False);
        Assert.That(CompanionManagerProtocol.TryParseBgControl("0001", "f", out _, out _), Is.False);
        Assert.That(CompanionManagerProtocol.TryParseBgControl("000g", "ff", out _, out _), Is.False);
        Assert.That(CompanionManagerProtocol.TryParseBgControl(null, "ff", out _, out _), Is.False);
    }

    [Test]
    public void LastField_RoundTrips_AndStartsEmpty()
    {
        OpenBgMetrics.ResetLastField();
        Assert.That(OpenBgMetrics.TryGetLastField(out _), Is.False);
        OpenBgMetrics.PublishField(3, 12, 20, 72);
        Assert.That(OpenBgMetrics.TryGetLastField(out OpenBgFieldFigures field), Is.True);
        Assert.That(field, Is.EqualTo(new OpenBgFieldFigures(3, 12, 20, 72, 28)));
        Assert.That(OpenBgMetrics.FieldLine(3, 12, 20, 72), Does.Contain("pct=28"));
        OpenBgMetrics.ResetLastField();
    }

    [Test]
    public void BotStatus_ArchetypeDefaultsEmpty_AndUnknownUnitHasNone()
    {
        var bot = new OpenBgBotStatus("Albion", "A", 50, "Cleric", 1, "small", null, "inside", true);
        Assert.That(bot.Archetype, Is.EqualTo(""));
        Assert.That(OpenBattlegroundRoster.TryGetUnitArchetype(-12345, out string archetype), Is.False);
        Assert.That(archetype, Is.EqualTo(""));
        Assert.That(OpenBattlegroundStatus.ArchetypeOf(true, "classic8"), Is.EqualTo("classic8"));
        Assert.That(OpenBattlegroundStatus.ArchetypeOf(true, null), Is.EqualTo(""));
        Assert.That(OpenBattlegroundStatus.ArchetypeOf(false, "x"), Is.EqualTo(""));
    }

    // ---- Task 5 / redesign Task 3: the "Kriegstafel" screen and session ------------------------------------------

    private static OpenBgBotStatus Bot(string realm, string name, int unit, string kind, string archetype, string state = "inside",
        string role = "roam", bool raid = false, string leader = null, bool alive = true, int inside = 720) =>
        new(realm, name, 45, "Cleric", unit, kind, leader ?? name, state, alive, role, raid, inside, archetype);

    private static OpenBgStatusSnapshot Status(params OpenBgBotStatus[] bots) =>
        new(DateTime.UtcNow, true, true, true, true, null, "Quiet", "raid 2 units/9 bots, lead Guild X", 2100, "Thidranki Faste: Albion (Guild X)",
            19.8, 20, 72, new[] { new OpenBgPlayerStatus("Albion", "Aaron", 50) }, bots, Array.Empty<OpenBgGuestStatus>(), null,
            new OpenBgFieldFigures(3, 12, 20, 72, 28));

    // Albion: an 8-man std8 unit #1 led by Alb2 (raid), Midgard: a small unit #2, Hibernia: a travelling solo #3.
    private static OpenBgStatusSnapshot ThreeRealms() => Status(
        Bot("Albion", "Alb1", 1, "group8", "std8", raid: true, leader: "Alb2"), Bot("Albion", "Alb2", 1, "group8", "std8", raid: true, leader: "Alb2"),
        Bot("Albion", "Alb3", 1, "group8", "std8", raid: true, leader: "Alb2", alive: false),
        Bot("Midgard", "Mid1", 2, "small", "small"), Bot("Hibernia", "Hib1", 3, "solo", "", "travelling", inside: 0));

    private static OpenBgSetupValues Values(string mode = "serverpvp") => new(true, 1, 2, 4, 72, 15, 45, true, Mode: mode, Presets: "std8:2,small:1");

    private static OpenBgWindowView Build(OpenBgStatusSnapshot status, OpenBgWindowPage page, int offset = 0, string mode = "serverpvp",
        OpenBgWindowState state = null) =>
        OpenBgWindowScreen.Build(status, Values(mode), page, offset, "", state);

    private static OpenBgWindowState Opened(params string[] blocks) => new(new HashSet<string>(blocks));

    private static string Label(string[] labels, int index) => labels[index - CompanionManagerProtocol.BgLabelBase];

    private static string Slot(string[] labels, int row, int slot) => labels[OpenBgWindowScreen.RowLabelIndex(row, slot)];

    private static int RowOf(OpenBgWindowView view, string key) => view.Rows.ToList().FindIndex(row => row.Key == key);

    [Test]
    public void LabelTable_IsPinned()
    {
        Assert.That(CompanionManagerProtocol.BgLabelBase, Is.EqualTo(620));
        string[] labels = Build(ThreeRealms(), OpenBgWindowPage.Overview).ToLabels();
        Assert.That(labels, Has.Length.EqualTo(365));
        Assert.That(Label(labels, 620), Does.StartWith("Thidranki | on and ready | 1 player inside"));
        Assert.That((Label(labels, 621), Label(labels, 622), Label(labels, 623), Label(labels, 624)), Is.EqualTo(("", "OVERVIEW", "Setup", "")));
        Assert.That(Label(labels, 625), Is.EqualTo("Rows 1-8 of 8"));
        Assert.That(Label(labels, 626), Is.EqualTo("Thidranki Faste: Guild X holds it"));
        Assert.That(Label(labels, 627), Is.EqualTo("Quiet | next push in 35 min | field 3 of 12 units out (28%) | speed 20x"));
        Assert.That(Label(labels, 628), Is.EqualTo(OpenBgWindowScreen.OverviewHint));
        Assert.That((Label(labels, 630), Label(labels, 631), Label(labels, 632)), Is.EqualTo(("[Open all]", "[Fold all]", "")));
        Assert.That(Enumerable.Range(957, 6).Select(index => Label(labels, index)), Is.EqualTo(new[] { "Unit / name", "Lv", "Class", "Doing", "Strength", "Note" }));
        // crest band: Albion at 963-967 (two crew, one of them dead but inside: both count as in)
        Assert.That(Enumerable.Range(963, 5).Select(index => Label(labels, index)),
            Is.EqualTo(new[] { "Albion  3 crew", "1 unit, plan 7", new string('|', 52), new string('|', 52), "3 in" }));
        Assert.That(Label(labels, 975), Is.EqualTo(new string('|', 52)));       // Hibernia ground
        Assert.That(Label(labels, 976), Is.Empty);                              // Hibernia: its solo travels
        Assert.That(Label(labels, 977), Is.EqualTo("0 in"));
        Assert.That(labels.Skip(978 - 620), Is.All.Empty, "978-984 stay free");
        Assert.That(OpenBgWindowScreen.RowLabelIndex(0, 0), Is.EqualTo(633 - 620));
        Assert.That(OpenBgWindowScreen.RowLabelIndex(17, 17), Is.EqualTo(956 - 620));
        Assert.That(OpenBgWindowScreen.RowLabelIndex(3, 5), Is.EqualTo(633 + 54 + 5 - 620));
        string[] setup = Build(ThreeRealms(), OpenBgWindowPage.Setup).ToLabels();
        Assert.That((Label(setup, 621), Label(setup, 622), Label(setup, 623), Label(setup, 624)), Is.EqualTo(("Overview", "", "", "SETUP")));
        Assert.That((Label(setup, 630), Label(setup, 631)), Is.EqualTo(("", "")), "Open/Fold all only on the Overview");
        Assert.That(Enumerable.Range(957, 6).Select(index => Label(setup, index)), Is.EqualTo(new[] { "Setting", "Range", "", "Value", "", "" }));
        Assert.That(Label(setup, 628), Is.EqualTo(OpenBgWindowScreen.SetupHint));
    }

    [Test]
    public void RowSlotTable_MatchesOpenBgRowSlotsOfTheBuilder()
    {
        // The literal table of open_bg_row_slots() (x, width, MaxCharacters), slot order 0-17; test_companion_manager_client.py pins
        // the same literals against the XML.
        (int, int, int)[] builder =
        {
            (12, 14, 2), (28, 460, 115), (28, 720, 115), (28, 128, 40), (28, 128, 40), (28, 128, 40), (160, 24, 4), (190, 98, 32),
            (160, 128, 42), (292, 122, 40), (292, 122, 40), (292, 122, 40), (418, 80, 40), (418, 80, 40), (504, 132, 44),
            (640, 108, 36), (502, 44, 4), (546, 44, 4),
        };
        Assert.That(OpenBgWindowScreen.RowSlots.Select(slot => (slot.X, slot.Width, slot.Characters)), Is.EqualTo(builder));
        Assert.That(OpenBgWindowScreen.RowSlots, Has.Length.EqualTo(CompanionManagerProtocol.BgRowStride));
        Assert.That((CompanionManagerProtocol.BgSlotMark, CompanionManagerProtocol.BgSlotText, CompanionManagerProtocol.BgSlotNameBase,
            CompanionManagerProtocol.BgSlotStateField, CompanionManagerProtocol.BgSlotBarDim, CompanionManagerProtocol.BgSlotPlus), Is.EqualTo((0, 2, 3, 9, 12, 17)));
        Assert.That(OpenBgWindowScreen.RealmSlots, Is.EqualTo(new[] { (148, 40), (148, 48), (104, 52), (104, 52), (38, 8) }));
    }

    private static string BuilderSource()
    {
        for (var directory = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
        {
            string path = System.IO.Path.Combine(directory.FullName, "source", "server", "tools", "build_companion_manager_client.py");
            if (System.IO.File.Exists(path)) return System.IO.File.ReadAllText(path);
            path = System.IO.Path.Combine(directory.FullName, "tools", "build_companion_manager_client.py");
            if (System.IO.File.Exists(path)) return System.IO.File.ReadAllText(path);
        }
        return null;
    }

    private static int[] PythonTuple(string source, string name)
    {
        int start = source.IndexOf(name + " = (", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), name);
        int open = source.IndexOf('(', start), close = source.IndexOf(')', open);
        return source[(open + 1)..close].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
    }

    [Test]
    public void TitleAdvances_AreTheBuildersTable_AndTheBannerFitsItsCaps()
    {
        Assert.That(OpenBgWindowScreen.TitleAdvances, Has.Length.EqualTo('~' - '!' + 1));
        string source = BuilderSource();
        if (source == null)
            Assert.Inconclusive("build_companion_manager_client.py not found above the test directory");
        Assert.That(OpenBgWindowScreen.TitleAdvances.Select(value => (int)value), Is.EqualTo(PythonTuple(source, "TITLE_ADVANCES")));
        Assert.That(source, Does.Contain($"TITLE_SPACE_ADVANCE = {OpenBgWindowScreen.TitleSpaceAdvance}"));
        // the arial14 table of the protocol is the builder's as well
        int[] arial = PythonTuple(source, "ARIAL14_ADVANCES");
        Assert.That(Enumerable.Range(0, arial.Length).Select(index => CompanionManagerProtocol.TextWidth(((char)('!' + index)).ToString())), Is.EqualTo(arial));
        Assert.That(OpenBgWindowScreen.TitleWidth("A B"), Is.EqualTo(OpenBgWindowScreen.TitleAdvances['A' - '!'] + 7 + OpenBgWindowScreen.TitleAdvances['B' - '!']));
        string fitted = OpenBgWindowScreen.FitTitle(new string('W', 60), OpenBgWindowScreen.BannerFitWidth, OpenBgWindowScreen.BannerCharacters);
        Assert.That(OpenBgWindowScreen.TitleWidth(fitted), Is.LessThanOrEqualTo(468));
        Assert.That(fitted, Does.EndWith("..."));
        Assert.That(OpenBgWindowScreen.FitTitle("Thidranki Faste: Albion holds it", 468, 40), Is.EqualTo("Thidranki Faste: Albion holds it"));
        var longOwner = Status() with { KeepOwner = "Thidranki Faste: Hibernia (" + new string('M', 50) + ")" };
        string banner = Build(longOwner, OpenBgWindowPage.Overview).Banner;
        Assert.That(OpenBgWindowScreen.TitleWidth(banner), Is.LessThanOrEqualTo(468));
        Assert.That(banner.Length, Is.LessThanOrEqualTo(40));
    }

    [Test]
    public void Banner_AndPhaseLine_ReadTheSnapshot()
    {
        Assert.That(OpenBgWindowScreen.BannerText(Status() with { KeepOwner = "Thidranki Faste: Hibernia" }), Is.EqualTo("Thidranki Faste: Hibernia holds it"));
        Assert.That(OpenBgWindowScreen.BannerText(Status() with { KeepOwner = "Thidranki Faste: Midgard - lord defeated, unclaimed" }),
            Is.EqualTo("Thidranki Faste: lord down, unclaimed"));
        Assert.That(OpenBgWindowScreen.BannerText(null), Is.EqualTo("The keep: no report yet"));
        OpenBgWindowView fight = Build(ThreeRealms() with { Phase = "KeepFight", PhaseSecondsLeft = 40, AchievedSpeed = 12 }, OpenBgWindowPage.Overview);
        Assert.That(fight.Phase, Is.EqualTo("Keep fight | retreat in 40 s | field 3 of 12 units out (28%) | speed 20x (12x real) | " +
            "raid 2 units/9 bots, lead..."), "the raid summary goes last, where the 115-character clip cuts it");
        Assert.That(Build(ThreeRealms() with { Phase = "Push", Attacker = new string('x', 90) }, OpenBgWindowPage.Overview).Phase,
            Does.StartWith("Push | keep fight in 35 min | field 3 of 12 units out (28%) | speed 20x | xxx").And.EndWith("..."));
        Assert.That(Build(ThreeRealms() with { LastField = null, AchievedSpeed = 20 }, OpenBgWindowPage.Overview).Phase,
            Is.EqualTo("Quiet | next push in 35 min | field: no figures yet | speed 20x"));
        Assert.That(OpenBgWindowScreen.Span(89), Is.EqualTo("89 s"));
        Assert.That(OpenBgWindowScreen.Span(150), Is.EqualTo("3 min"));
    }

    [Test]
    public void Overview_StartsFolded_WithRealmRowsUnitHeadersAndSoloBlocks()
    {
        OpenBgWindowView view = Build(ThreeRealms(), OpenBgWindowPage.Overview);
        Assert.That(view.Rows.Select(row => row.Key), Is.EqualTo(new[] { "r:Albion", "u:1", "r:Midgard", "u:2", "r:Hibernia", "s:Hibernia", "h:players", "p:Aaron" }));
        Assert.That(view.Blocks, Is.EqualTo(new[] { "u:1", "u:2", "s:Hibernia" }));
        string[] labels = view.ToLabels();
        // realm row: realm colour name at the title x, Ash summary, Field inside count
        Assert.That(Slot(labels, 0, CompanionManagerProtocol.BgSlotNameBase), Is.EqualTo("ALBION"));
        Assert.That(Slot(labels, 0, CompanionManagerProtocol.BgSlotAsh), Is.EqualTo("1 unit, 3 crew"));
        Assert.That(Slot(labels, 0, CompanionManagerProtocol.BgSlotStateField), Is.EqualTo("3 inside"));
        Assert.That(Slot(labels, 2, CompanionManagerProtocol.BgSlotNameBase + 1), Is.EqualTo("MIDGARD"));
        Assert.That(Slot(labels, 4, CompanionManagerProtocol.BgSlotStateAsh), Is.EqualTo("none inside"));
        // unit header: gold "+", Ink title (slot 2, not the gold slot 1), "3 bots", order word, pips, note Raid, no Relieve while folded
        Assert.That(Slot(labels, 1, CompanionManagerProtocol.BgSlotMark), Is.EqualTo("+"));
        Assert.That(Slot(labels, 1, CompanionManagerProtocol.BgSlotTitle), Is.Empty);
        Assert.That(Slot(labels, 1, CompanionManagerProtocol.BgSlotText), Is.EqualTo("std8 #1"));
        Assert.That(Slot(labels, 1, CompanionManagerProtocol.BgSlotAsh), Is.EqualTo("3 bots"));
        Assert.That(Slot(labels, 1, CompanionManagerProtocol.BgSlotStateField), Is.EqualTo("roam"));
        Assert.That(Slot(labels, 1, CompanionManagerProtocol.BgSlotBarDim), Is.EqualTo("==="));
        Assert.That(Slot(labels, 1, CompanionManagerProtocol.BgSlotBarField), Is.EqualTo("=="), "the dead member is no pip");
        Assert.That(Slot(labels, 1, CompanionManagerProtocol.BgSlotNote), Is.EqualTo("Raid"));
        Assert.That(Slot(labels, 1, CompanionManagerProtocol.BgSlotLink), Is.Empty);
        Assert.That(Slot(labels, 3, CompanionManagerProtocol.BgSlotText), Is.EqualTo("small #2"));
        Assert.That(Slot(labels, 3, CompanionManagerProtocol.BgSlotAsh), Is.EqualTo("1 bot"));
        // solo block: "Solo hunters", present of plan, Ash state while nobody is in the field
        Assert.That(Slot(labels, 5, CompanionManagerProtocol.BgSlotText), Is.EqualTo("Solo hunters"));
        Assert.That(Slot(labels, 5, CompanionManagerProtocol.BgSlotAsh), Is.EqualTo("1 of 4"));
        Assert.That(Slot(labels, 5, CompanionManagerProtocol.BgSlotStateAsh), Is.EqualTo("travelling"));
        Assert.That(Slot(labels, 5, CompanionManagerProtocol.BgSlotBarDim), Is.EqualTo("="));
        Assert.That(Slot(labels, 5, CompanionManagerProtocol.BgSlotBarField), Is.Empty);
        // gold is only in the marks: no row uses the gold title slot on the Overview
        for (int row = 0; row < 18; row++)
            Assert.That(Slot(labels, row, CompanionManagerProtocol.BgSlotTitle), Is.Empty, $"row {row}");
        Assert.That(view.Rows.All(row => row.Minus == null && row.Plus == null), Is.True);
    }

    [Test]
    public void OpenedUnit_ListsLeaderFirst_WithStarAndStateColours()
    {
        var status = Status(Bot("Albion", "Zed", 7, "group8", "caster8", leader: "Mia", role: "assault"),
            Bot("Albion", "Mia", 7, "group8", "caster8", leader: "Mia", role: "assault", inside: 30),
            Bot("Albion", "Abe", 7, "group8", "caster8", leader: "Mia", alive: false),
            Bot("Albion", "Bob", 7, "group8", "caster8", "travelling", leader: "Mia", inside: 0),
            Bot("Albion", "Cy", 7, "group8", "caster8", "leaving", leader: "Mia"));
        OpenBgWindowView view = Build(status, OpenBgWindowPage.Overview, state: Opened("u:7"));
        Assert.That(view.Rows.Select(row => row.Key).Take(7), Is.EqualTo(new[] { "r:Albion", "u:7", "m:7:Mia", "m:7:Abe", "m:7:Bob", "m:7:Cy", "m:7:Zed" }));
        string[] labels = view.ToLabels();
        Assert.That(Slot(labels, 1, CompanionManagerProtocol.BgSlotMark), Is.EqualTo("-"));
        Assert.That(Slot(labels, 1, CompanionManagerProtocol.BgSlotStateField), Is.EqualTo("assault"));
        Assert.That(Slot(labels, 2, CompanionManagerProtocol.BgSlotMark), Is.EqualTo("*"), "the leader comes first with the gold star");
        Assert.That(Slot(labels, 2, CompanionManagerProtocol.BgSlotNameBase), Is.EqualTo("  Mia"), "members are indented under the title");
        Assert.That(Slot(labels, 2, CompanionManagerProtocol.BgSlotLevel), Is.EqualTo("45"));
        Assert.That(Slot(labels, 2, CompanionManagerProtocol.BgSlotClass), Is.EqualTo("Cleric"));
        Assert.That(Slot(labels, 2, CompanionManagerProtocol.BgSlotStateField), Is.EqualTo("in the field"));
        Assert.That(Slot(labels, 2, CompanionManagerProtocol.BgSlotNote), Is.EqualTo("30 s"));
        Assert.That(Slot(labels, 3, CompanionManagerProtocol.BgSlotStateBlood), Is.EqualTo("dead"));
        Assert.That(Slot(labels, 3, CompanionManagerProtocol.BgSlotStateField), Is.Empty);
        Assert.That(Slot(labels, 4, CompanionManagerProtocol.BgSlotStateAsh), Is.EqualTo("travelling"));
        Assert.That(Slot(labels, 4, CompanionManagerProtocol.BgSlotNote), Is.Empty, "no time outside the field");
        Assert.That(Slot(labels, 5, CompanionManagerProtocol.BgSlotStateAsh), Is.EqualTo("leaving"));
        Assert.That(Slot(labels, 6, CompanionManagerProtocol.BgSlotMark), Is.Empty);
        Assert.That(Slot(labels, 6, CompanionManagerProtocol.BgSlotNote), Is.EqualTo("12 min"));
        Assert.That(OpenBgWindowScreen.MemberState(Bot("Albion", "X", 1, "solo", "", "outside")), Is.EqualTo(("outside", OpenBgTone.Ash)));
        // a unit whose members inside are all dead reads "dead" in Blood
        OpenBgWindowView wiped = Build(Status(Bot("Albion", "Dee", 9, "small", "", alive: false), Bot("Albion", "Eve", 9, "small", "", alive: false)), OpenBgWindowPage.Overview);
        Assert.That(wiped.Rows[1].State, Is.EqualTo("dead"));
        Assert.That(wiped.Rows[1].Tone, Is.EqualTo(OpenBgTone.Blood));
        Assert.That(wiped.Rows[1].Text, Is.EqualTo("small #9"));
    }

    [Test]
    public void MixedRealmUnit_IsListedOnceUnderItsLeadersRealm_MembersKeepTheirColours()
    {
        // Server PvP crews mix realms: unit #67 is led by a Hibernian and has an Albion member.
        var status = Status(Bot("Albion", "Alby", 67, "group8", "caster8", leader: "Hibi"), Bot("Hibernia", "Hibi", 67, "group8", "caster8", leader: "Hibi"),
            Bot("Hibernia", "Hob", 67, "group8", "caster8", leader: "Hibi"));
        OpenBgWindowView view = Build(status, OpenBgWindowPage.Overview, state: Opened("u:67"));
        Assert.That(view.Rows.Select(row => row.Key).Take(7), Is.EqualTo(new[] { "r:Albion", "r:Midgard", "r:Hibernia", "u:67", "m:67:Hibi", "m:67:Alby", "m:67:Hob" }));
        Assert.That(view.Rows.Count(row => row.Key == "u:67"), Is.EqualTo(1));
        Assert.That(view.Rows[5].Realm, Is.EqualTo(eRealm.Albion), "the Albion member keeps the Albion colour");
        Assert.That(view.Rows[4].Realm, Is.EqualTo(eRealm.Hibernia));
        Assert.That(view.Rows[0].Ash, Is.EqualTo("0 units, 1 crew"), "Albion lists no unit but counts its crew");
        Assert.That(view.Rows[2].Ash, Is.EqualTo("1 unit, 2 crew"));
        Assert.That(OpenBgWindowScreen.HomeRealms(new[] { Bot("Albion", "A", 5, "small", "", leader: "Gone"), Bot("Midgard", "M", 5, "small", "", leader: "Gone"),
            Bot("Midgard", "N", 5, "small", "", leader: "Gone") })[5], Is.EqualTo("Midgard"), "without a known leader the majority decides");
    }

    [Test]
    public void SoloBlock_OpensToItsHunters()
    {
        var status = Status(Bot("Midgard", "Sol2", 21, "solo", "", role: "lurk"), Bot("Midgard", "Sol1", 20, "solo", "", role: "lurk"),
            Bot("Midgard", "Sol3", 22, "solo", "", "travelling", inside: 0));
        OpenBgWindowView view = Build(status, OpenBgWindowPage.Overview, state: Opened("s:Midgard"));
        Assert.That(view.Rows.Select(row => row.Key).Take(6), Is.EqualTo(new[] { "r:Albion", "r:Midgard", "s:Midgard", "m:20:Sol1", "m:21:Sol2", "m:22:Sol3" }),
            "a realm without crew keeps its row");
        Assert.That(view.Rows[0].State, Is.EqualTo("none inside"));
        OpenBgWindowRow block = view.Rows[2];
        Assert.That((block.Mark, block.Text, block.State, block.Tone, block.BarDim, block.BarField), Is.EqualTo(("-", "Solo hunters", "lurk", OpenBgTone.Field, "===", "==")));
        Assert.That(view.Rows.Skip(3).Take(3).All(row => row.Mark == null && row.Realm == eRealm.Midgard && row.Indent), Is.True, "solos carry no leader mark");
        Assert.That(block.Link, Is.Null, "a solo block has no Relieve");
        Assert.That(block.Fold, Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.Fold, "s:Midgard")));
    }

    [Test]
    public void Session_FoldsAndOpensUnits_OpenAllFoldAll_AndPrunesVanishedUnits()
    {
        var session = new OpenBgWindowSession();
        DateTime now = DateTime.UtcNow;
        session.Touch(now);
        OpenBgWindowView view = session.Render(ThreeRealms(), Values(), now);
        Assert.That(session.Opened, Is.Empty, "units start folded");
        int header = RowOf(view, "u:1");
        OpenBgWindowIntent fold = session.Resolve(CompanionManagerProtocol.BgControlFoldBase + header, session.Revision, now);
        Assert.That(fold, Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.Fold, "u:1")));
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlSelectBase + header, session.Revision, now), Is.EqualTo(fold),
            "a click on the unit title folds as well");
        Assert.That(OpenBgWindow.PlanClick(session, CompanionManagerProtocol.BgControlFoldBase + header, session.Revision, now, Values()).Kind,
            Is.EqualTo(OpenBgWindowActionKind.Navigate));
        session.Navigate(fold);
        view = session.Render(ThreeRealms(), Values(), now);
        Assert.That(view.Rows.Select(row => row.Key).Take(5), Is.EqualTo(new[] { "r:Albion", "u:1", "m:1:Alb2", "m:1:Alb1", "m:1:Alb3" }));
        session.Navigate(fold);
        Assert.That(session.Opened, Is.Empty, "a second click folds it again");
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlOpenAll, 0, now.AddHours(2)), Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.OpenAll)));
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.OpenAll));
        Assert.That(session.Opened, Is.EquivalentTo(new[] { "u:1", "u:2", "s:Hibernia" }));
        view = session.Render(ThreeRealms(), Values(), now);
        Assert.That(view.Rows.Count(row => row.Key.StartsWith("m:", StringComparison.Ordinal)), Is.EqualTo(5));
        // unit #1 leaves the snapshot: its id is pruned, so a new unit with the same id would start folded
        session.Render(Status(Bot("Midgard", "Mid1", 2, "small", "small")), Values(), now);
        Assert.That(session.Opened, Is.EquivalentTo(new[] { "u:2" }));
        // a missing report does not forget the fold set
        session.Render(null, Values(), now);
        Assert.That(session.Opened, Is.EquivalentTo(new[] { "u:2" }));
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.FoldAll));
        Assert.That(session.Opened, Is.Empty);
        // Open/Fold all belong to the Overview: on Setup their (empty) click areas do nothing
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.Tab, "Setup"));
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlOpenAll, session.Revision, now), Is.Null);
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlFoldAll, session.Revision, now), Is.Null);
    }

    [Test]
    public void Paging_Shows18Rows_ClampsAndShowsRange()
    {
        var bots = Enumerable.Range(1, 40).Select(index => Bot("Albion", "Bot" + index, index, "small", "")).ToArray();
        OpenBgStatusSnapshot status = Status(bots);
        OpenBgWindowView first = Build(status, OpenBgWindowPage.Overview);
        Assert.That(first.Rows, Has.Count.EqualTo(18));
        Assert.That(first.TotalRows, Is.EqualTo(1 + 40 + 2 + 2), "realm rows, 40 unit headers, players");
        Assert.That(first.PageIndicator, Is.EqualTo($"Rows 1-18 of {first.TotalRows}"));
        OpenBgWindowView last = Build(status, OpenBgWindowPage.Overview, 9999);
        Assert.That(last.Offset, Is.EqualTo(first.TotalRows - 18));
        Assert.That(last.PageIndicator, Is.EqualTo($"Rows {last.Offset + 1}-{first.TotalRows} of {first.TotalRows}"));
        Assert.That(Build(status, OpenBgWindowPage.Overview, -5).Offset, Is.EqualTo(0));
        var session = new OpenBgWindowSession();
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlPageDown, 0, DateTime.UtcNow).Amount, Is.EqualTo(18));
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlPageUp, 0, DateTime.UtcNow).Amount, Is.EqualTo(-18));
    }

    [Test]
    public void CrestClick_JumpsToTheRealmSection_FromEitherPage()
    {
        var bots = Enumerable.Range(1, 30).Select(index => Bot("Albion", "Bot" + index, index, "small", ""))
            .Append(Bot("Midgard", "Mid1", 99, "small", ""))
            .Concat(Enumerable.Range(101, 30).Select(index => Bot("Hibernia", "Hib" + index, index, "small", ""))).ToArray();
        var session = new OpenBgWindowSession();
        DateTime now = DateTime.UtcNow;
        session.Touch(now);
        session.Page = OpenBgWindowPage.Setup;
        session.Render(Status(bots), Values(), now);
        OpenBgWindowIntent crest = session.Resolve(CompanionManagerProtocol.BgControlCrestBase + 1, 0, now.AddHours(1));
        Assert.That(crest, Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.Jump, "Midgard")), "crests are navigation: no token needed");
        Assert.That(OpenBgWindow.PlanIntent(crest, Values()).Kind, Is.EqualTo(OpenBgWindowActionKind.Navigate));
        session.Navigate(crest);
        Assert.That(session.Page, Is.EqualTo(OpenBgWindowPage.Overview));
        OpenBgWindowView view = session.Render(Status(bots), Values(), now);
        Assert.That(view.Rows[0].Key, Is.EqualTo("r:Midgard"));
        Assert.That(view.Offset, Is.EqualTo(31));
        Assert.That(session.OverviewOffset, Is.EqualTo(31));
        // the jump is done once; afterwards the offset stays where the player scrolls
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.Scroll, Amount: -3));
        Assert.That(session.Render(Status(bots), Values(), now).Offset, Is.EqualTo(28));
        session.Navigate(session.Resolve(CompanionManagerProtocol.BgControlCrestBase, 0, now));
        Assert.That(session.Render(Status(bots), Values(), now).Offset, Is.EqualTo(0));
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlCrestBase + 2, 0, now).Key, Is.EqualTo("Hibernia"));
    }

    [Test]
    public void Selection_ShowsTheGoldMark_AndTheRowsHelpInTheHint()
    {
        var session = new OpenBgWindowSession();
        DateTime now = DateTime.UtcNow;
        session.Touch(now);
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.Fold, "u:1"));
        OpenBgWindowView view = session.Render(ThreeRealms(), Values(), now);
        int leader = RowOf(view, "m:1:Alb2");
        OpenBgWindowIntent select = session.Resolve(CompanionManagerProtocol.BgControlSelectBase + leader, session.Revision, now);
        Assert.That(select, Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.Select, "m:1:Alb2")));
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlFoldBase + leader, session.Revision, now), Is.EqualTo(select),
            "the mark of a member row selects it too");
        session.Navigate(select);
        view = session.Render(ThreeRealms(), Values(), now);
        Assert.That(view.Rows[leader].Mark, Is.EqualTo(">"), "the selection mark replaces the leader star");
        Assert.That(view.Hint, Is.EqualTo("Alb2, level 45 Cleric of std8 #1, its leader: in the field for 12 min."));
        session.Navigate(select);
        view = session.Render(ThreeRealms(), Values(), now);
        Assert.That(view.Rows[leader].Mark, Is.EqualTo("*"));
        Assert.That(view.Hint, Is.EqualTo(OpenBgWindowScreen.OverviewHint), "a second click deselects");
        // Setup: one help sentence per setting; the selection is kept per page
        session.Navigate(select);
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.Tab, "Setup"));
        view = session.Render(ThreeRealms(), Values(), now);
        Assert.That(view.Hint, Is.EqualTo(OpenBgWindowScreen.SetupHint));
        int cap = RowOf(view, "k:max_bots");
        session.Navigate(session.Resolve(CompanionManagerProtocol.BgControlSelectBase + cap, session.Revision, now));
        view = session.Render(ThreeRealms(), Values(), now);
        Assert.That(view.Rows[cap].Mark, Is.EqualTo(">"));
        Assert.That(view.Hint, Does.StartWith("Bot cap: the most crew bots"));
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.Tab, "Overview"));
        Assert.That(session.Render(ThreeRealms(), Values(), now).Hint, Does.StartWith("Alb2, level 45"));
        foreach (OpenBgWindowRow row in OpenBgWindowScreen.AllRows(ThreeRealms(), Values("perrealm"), OpenBgWindowPage.Setup).Where(row => row.Select != null))
        {
            Assert.That(row.Help, Is.Not.Null.And.Not.Empty, row.Key);
            Assert.That(row.Help.Length, Is.LessThanOrEqualTo(115), row.Key);
            Assert.That(CompanionManagerProtocol.TextWidth(row.Help), Is.LessThanOrEqualTo(OpenBgWindowScreen.HintWidth), row.Key);
        }
    }

    [Test]
    public void Relieve_SitsAtTheOpenedUnitHeader_AndNeedsASecondClickWithin10Seconds()
    {
        var session = new OpenBgWindowSession();
        DateTime now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
        session.Touch(now);
        OpenBgWindowView view = session.Render(ThreeRealms(), Values(), now);
        Assert.That(view.Rows.Any(row => row.Link != null), Is.False, "no Relieve while folded");
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.Fold, "u:1"));
        view = session.Render(ThreeRealms(), Values(), now);
        int header = RowOf(view, "u:1");
        Assert.That(view.ToLabels()[OpenBgWindowScreen.RowLabelIndex(header, CompanionManagerProtocol.BgSlotLink)], Is.EqualTo("[Relieve]"));
        ushort before = session.Revision;
        OpenBgWindowAction first = OpenBgWindow.PlanClick(session, CompanionManagerProtocol.BgControlLinkBase + header, before, now, Values());
        Assert.That((first.Kind, first.Unit, first.Intent.On, first.Intent.Key), Is.EqualTo((OpenBgWindowActionKind.Relieve, 1, false, "std8 #1")));
        Assert.That(session.RelieveClick(first.Intent, now), Is.EqualTo((false, "Relieve std8 #1? Click again within 10 s.")));
        view = session.Render(ThreeRealms(), Values(), now);
        Assert.That(view.Rows[header].LinkText, Is.EqualTo("[Yes, relieve]"), "the confirm state shows at the header");
        Assert.That(session.Revision, Is.Not.EqualTo(before), "a double click on the old view is stale, not a confirmation");
        Assert.That(OpenBgWindow.PlanClick(session, CompanionManagerProtocol.BgControlLinkBase + header, before, now, Values()).Kind,
            Is.EqualTo(OpenBgWindowActionKind.Stale));
        OpenBgWindowAction second = OpenBgWindow.PlanClick(session, CompanionManagerProtocol.BgControlLinkBase + header, session.Revision, now.AddSeconds(9), Values());
        Assert.That(second.Intent.On, Is.True);
        Assert.That(session.RelieveClick(second.Intent, now.AddSeconds(9)), Is.EqualTo((true, (string)null)));
        Assert.That(session.PendingRelieve, Is.EqualTo(0));
        Assert.That(session.Render(ThreeRealms(), Values(), now.AddSeconds(9)).Rows[header].LinkText, Is.EqualTo("[Relieve]"));
        Assert.That(OpenBgWindowScreen.RelievedMessage("std8 #1"), Is.EqualTo("std8 #1 relieved; a fresh unit forms within a game minute."));
        // too late: the confirmation expires after 10 s; the drawing drops it and a late confirm asks again
        session.RelieveClick(first.Intent, now);
        Assert.That(session.Render(ThreeRealms(), Values(), now.AddSeconds(11)).Rows[header].LinkText, Is.EqualTo("[Relieve]"));
        session.RelieveClick(first.Intent, now);
        Assert.That(session.RelieveClick(second.Intent, now.AddSeconds(10.5)), Is.EqualTo((false, "Relieve std8 #1? Click again within 10 s.")));
        // a confirm for another unit does not relieve
        Assert.That(session.RelieveClick(second.Intent with { Unit = 2 }, now.AddSeconds(11)).Relieve, Is.False);
        // leaving units cannot be relieved
        OpenBgWindowView leaving = Build(Status(Bot("Albion", "L1", 5, "small", "", "leaving")), OpenBgWindowPage.Overview, state: Opened("u:5"));
        Assert.That(leaving.Rows.Single(row => row.Key == "u:5").Link, Is.Null);
    }

    [Test]
    public void Setup_HasSectionsSwitchesSteppersAndBars_ForEveryViewer()
    {
        OpenBgWindowRow[] rows = OpenBgWindowScreen.AllRows(ThreeRealms(), Values() with { BandMin = 42, BandMax = 45 }, OpenBgWindowPage.Setup).ToArray();
        Assert.That(rows.Where(row => row.Title != null).Select(row => row.Title),
            Is.EqualTo(new[] { "Battleground", "Crew", "Level band", "Group presets", "Timing" }));
        Assert.That(rows, Has.Length.EqualTo(5 + 3 + 4 + 2 + 6 + 3));
        OpenBgWindowRow enabled = rows.Single(row => row.Key == "k:enabled");
        Assert.That((enabled.Text, enabled.Ash, enabled.State, enabled.Tone, enabled.LinkText), Is.EqualTo(("Open BG", "on or off", "On", OpenBgTone.Field, "[Turn off]")));
        Assert.That(enabled.Link, Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.SetEnabled, On: false)));
        OpenBgWindowRow off = OpenBgWindowScreen.AllRows(null, Values() with { Enabled = false }, OpenBgWindowPage.Setup).Single(row => row.Key == "k:enabled");
        Assert.That((off.State, off.Tone, off.LinkText), Is.EqualTo(("Off", OpenBgTone.Ash, "[Turn on]")));
        OpenBgWindowRow mode = rows.Single(row => row.Key == "k:mode");
        Assert.That((mode.State, mode.LinkText, mode.Link), Is.EqualTo(("Server PvP", "[Use Per realm]", new OpenBgWindowIntent(OpenBgWindowIntentKind.SetMode, "perrealm"))));
        OpenBgWindowRow peace = rows.Single(row => row.Key == "k:peace");
        Assert.That((peace.State, peace.LinkText, peace.Link.Kind), Is.EqualTo(("Auto", "[Next: All]", OpenBgWindowIntentKind.CyclePeace)));
        OpenBgWindowRow cap = rows.Single(row => row.Key == "k:max_bots");
        Assert.That((cap.Text, cap.Ash, cap.State, cap.Tone, cap.MinusText, cap.PlusText), Is.EqualTo(("Bot cap", "8-120, step 4", "72", OpenBgTone.Field, "[-]", "[+]")));
        Assert.That(cap.BarDim, Is.EqualTo(new string('|', 40)));
        Assert.That(cap.BarField, Is.EqualTo(new string('|', 23)), "(72 - 8) / 112 of 40");
        Assert.That(rows.Single(row => row.Key == "k:cycle_min").Ash, Is.EqualTo("5-120, step 5"));
        Assert.That(rows.Single(row => row.Key == "k:groups8").Ash, Is.EqualTo("0-4"));
        Assert.That(rows.Single(row => row.Key == "k:drift").LinkText, Is.EqualTo("[Turn off]"));
        foreach (string key in new[] { "band_min", "band_max", "max_bots", "groups8", "small", "solos", "cycle_min", "cycle_max" })
        {
            OpenBgWindowRow row = rows.Single(item => item.Key == "k:" + key);
            Assert.That(row.Minus, Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, key, -1)), key);
            Assert.That(row.Plus, Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, key, 1)), key);
        }
        foreach (string archetype in new[] { "std8", "caster8", "raid8", "stealth4", "small", "duo" })
        {
            OpenBgWindowRow row = rows.Single(item => item.Key == "p:" + archetype);
            Assert.That(row.Plus, Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "preset:" + archetype, 1)));
            Assert.That(row.BarDim, Is.EqualTo("========"));
        }
        OpenBgWindowRow std8 = rows.Single(row => row.Key == "p:std8");
        Assert.That((std8.Ash, std8.State, std8.BarField), Is.EqualTo(("8 bots, mixed", "2", "==")));
        OpenBgWindowRow stealth = rows.Single(row => row.Key == "p:stealth4");
        Assert.That((stealth.State, stealth.Tone, stealth.MinusText, stealth.BarField), Is.EqualTo(("0", OpenBgTone.Ash, (string)null, (string)null)));
        Assert.That(rows.Any(row => row.Key.StartsWith("units:", StringComparison.Ordinal)), Is.False, "serverpvp: no per realm rows");
        Assert.That(rows.Any(row => row.Link?.Kind == OpenBgWindowIntentKind.Relieve), Is.False, "Relieve moved to the Overview");
        Assert.That(rows.Single(row => row.Key == "k:mode").Help, Does.StartWith("Mode:"));
    }

    [Test]
    public void Setup_PerRealmUnits_OnlyInPerRealmMode()
    {
        OpenBgSetupValues values = Values("perrealm") with { AlbionUnits = "1/2/4" };
        OpenBgWindowRow[] rows = OpenBgWindowScreen.AllRows(ThreeRealms(), values, OpenBgWindowPage.Setup).ToArray();
        Assert.That(rows.Single(row => row.Key == "k:mode").LinkText, Is.EqualTo("[Use PvP]"));
        Assert.That(rows.Count(row => row.Key.StartsWith("units:", StringComparison.Ordinal)), Is.EqualTo(9));
        Assert.That(rows.Last(row => row.Title != null).Title, Is.EqualTo("Units per realm"));
        OpenBgWindowRow albion = rows.Single(row => row.Key == "units:Albion:group8");
        Assert.That((albion.Realm, albion.Name, albion.Ash, albion.State, albion.Tone, albion.LinkText),
            Is.EqualTo((eRealm.Albion, "Albion 8-man", "0-4", "1", OpenBgTone.Field, "[Use global]")));
        Assert.That(albion.Link, Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.ResetUnits, "Albion")));
        OpenBgWindowRow midgard = rows.Single(row => row.Key == "units:Midgard:group8");
        Assert.That((midgard.Ash, midgard.Tone, midgard.Link), Is.EqualTo(("0-4, global", OpenBgTone.Ash, (OpenBgWindowIntent)null)));
        var write = OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "units:Midgard:solo", 1), values);
        Assert.That((write.Ok, write.Value, write.Message), Is.EqualTo((true, "1/2/5", "Midgard solos: 4 -> 5.")));
        var top = OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "units:Albion:group8", 1), values with { AlbionUnits = "4/2/4" });
        Assert.That((top.Ok, top.Message), Is.EqualTo((false, "Albion 8-man is at its maximum (4).")));
        Assert.That(OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.ResetUnits, "Albion"), values).Message,
            Is.EqualTo("Albion uses the global counts again."));
    }

    [Test]
    public void Setup_StepsAndLimits_ExplainTheLimitInTheReply()
    {
        Assert.That(OpenBgWindowScreen.StepOf("max_bots"), Is.EqualTo(4));
        Assert.That(OpenBgWindowScreen.StepOf("cycle_min"), Is.EqualTo(5));
        Assert.That(OpenBgWindowScreen.StepOf("cycle_max"), Is.EqualTo(5));
        Assert.That(OpenBgWindowScreen.StepOf("groups8"), Is.EqualTo(1));
        var plus = OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "max_bots", 1), Values() with { MaxBots = 68 });
        Assert.That(plus, Is.EqualTo((true, "open_bg_max_bots", "72", "Bot cap: 68 -> 72.")));
        // at the maximum the [+] label is empty, the click still lands and the reply explains why nothing happens
        OpenBgWindowRow top = OpenBgWindowScreen.AllRows(ThreeRealms(), Values() with { MaxBots = 120 }, OpenBgWindowPage.Setup).Single(row => row.Key == "k:max_bots");
        Assert.That((top.PlusText, top.MinusText, top.Tone, top.BarField), Is.EqualTo(((string)null, "[-]", OpenBgTone.Blood, new string('|', 40))));
        OpenBgWindowAction refused = OpenBgWindow.PlanIntent(top.Plus, Values() with { MaxBots = 120 });
        Assert.That((refused.Kind, refused.Message), Is.EqualTo((OpenBgWindowActionKind.Refused, "Bot cap is at its maximum (120).")));
        Assert.That(OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "groups8", -1), Values() with { Groups8 = 0 }).Message,
            Is.EqualTo("8-man per realm is at its minimum (0)."));
        Assert.That(OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "band_min", 1), Values() with { BandMin = 45, BandMax = 45 }).Message,
            Is.EqualTo("Lowest level cannot go above the highest level (45)."));
        Assert.That(OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "cycle_max", -1), Values() with { CycleMin = 45, CycleMax = 45 }).Message,
            Is.EqualTo("Longest cycle cannot go below the shortest cycle (45)."));
        OpenBgWindowRow band = OpenBgWindowScreen.AllRows(ThreeRealms(), Values() with { BandMin = 45, BandMax = 45 }, OpenBgWindowPage.Setup).Single(row => row.Key == "k:band_min");
        Assert.That(band.PlusText, Is.Null);
        var cycle = OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "cycle_min", -1), Values());
        Assert.That((cycle.Value, cycle.Message), Is.EqualTo(("10", "Shortest cycle: 15 -> 10.")));
        var preset = OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "preset:duo", 1), Values());
        Assert.That((preset.Value, preset.Message), Is.EqualTo(("std8:2,small:1,duo:1", "duo: 0 -> 1.")));
        var none = OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "preset:std8", -1), Values() with { Presets = "std8:1" });
        Assert.That((none.Ok, none.RowKey, none.Value), Is.EqualTo((true, "open_bg_presets", "")));
        Assert.That(OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "preset:std8", 1), Values() with { Presets = "std8:8" }).Message,
            Is.EqualTo("std8 is at its maximum (8)."));
        Assert.That(OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.CyclePeace), Values() with { Peace = "all" }),
            Is.EqualTo((true, "open_bg_landing_peace", "realm", "Landing peace: Realm.")));
        Assert.That(OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.SetEnabled, On: false), Values()).Message, Is.EqualTo("Open BG: off."));
    }

    [Test]
    public void EmptyStates_UseTheAnalysisCopy()
    {
        Assert.That(Build(null, OpenBgWindowPage.Overview).Rows.Single().Text, Is.EqualTo("No field report yet. It updates every 15 s; click Refresh."));
        Assert.That(Build(Status() with { OpenEnabled = false, Players = Array.Empty<OpenBgPlayerStatus>() }, OpenBgWindowPage.Overview).Rows.Single().Text,
            Is.EqualTo("The open BG is off. Turn it on in Setup."));
        Assert.That(Build(ThreeRealms() with { Ready = false, NotReadyReason = "no navmesh" }, OpenBgWindowPage.Overview).Rows[0].Text,
            Is.EqualTo("Not ready: no navmesh. The crew waits until the zone is ready."));
        var failures = new[] { new OpenBgRealmStatus("Albion", 1, 2, 4, false, 0, 0, 0, 0, new[] { new OpenBgFormFailureStatus("group8", "no free bots", 130) }),
            new OpenBgRealmStatus("Midgard", 1, 2, 4, false, 0, 0, 0, 0, Array.Empty<OpenBgFormFailureStatus>()) };
        OpenBgWindowView empty = Build(Status() with { Players = Array.Empty<OpenBgPlayerStatus>(), Realms = failures }, OpenBgWindowPage.Overview);
        Assert.That(empty.Rows.Select(row => row.Text), Is.EqualTo(new[]
        {
            "No crew yet. The roster forms units within a game minute (levels 40-50).", "Last try 2 min ago: no free bots",
        }));
        Assert.That(Build(null, OpenBgWindowPage.Overview).Status, Is.EqualTo("Thidranki | no field report yet"));
        Assert.That(Build(null, OpenBgWindowPage.Overview).ToLabels()[963 - 620 + 1], Is.EqualTo("no report yet"));
    }

    [Test]
    public void Texts_NeverExceedTheirSlotWidthOrMaxCharacters_AndStayAscii()
    {
        string huge = new string('W', 60);
        var status = Status(Bot("Albion", huge, 1, "group8", huge, role: huge, leader: huge), Bot("Midgard", "Mid", 2, "solo", "", role: huge)) with
        {
            KeepOwner = huge + ": " + huge, Phase = "Push", Attacker = huge + huge, NotReadyReason = huge,
            Players = new[] { new OpenBgPlayerStatus("Hibernia", huge, 50) },
            Guests = new[] { new OpenBgGuestStatus("Albion", 50, 4, 3, huge) },
        };
        foreach (OpenBgWindowPage page in new[] { OpenBgWindowPage.Overview, OpenBgWindowPage.Setup })
        foreach (string mode in new[] { "serverpvp", "perrealm" })
        {
            var state = new OpenBgWindowState(new HashSet<string> { "u:1", "s:Midgard" }, "m:1:" + huge, 1);
            OpenBgWindowView view = OpenBgWindowScreen.Build(status, Values(mode), page, 0, new string('M', 400) + "\u2014\u00e9", state);
            string[] labels = view.ToLabels();
            for (int row = 0; row < view.Rows.Count; row++)
                if (view.Rows[row].Block != null)
                    Assert.That(CompanionManagerProtocol.TextWidth(view.Rows[row].Text), Is.LessThanOrEqualTo(OpenBgWindowScreen.UnitTitleWidth),
                        "a unit title ends before the Ash column");
            for (int row = 0; row < 18; row++)
            for (int slot = 0; slot < 18; slot++)
            {
                string label = Slot(labels, row, slot);
                Assert.That(CompanionManagerProtocol.TextWidth(label), Is.LessThanOrEqualTo(OpenBgWindowScreen.RowSlots[slot].Width), $"{page} row {row} slot {slot}");
                Assert.That(label.Length, Is.LessThanOrEqualTo(OpenBgWindowScreen.RowSlots[slot].Characters), $"{page} row {row} slot {slot}");
            }
            void Within(int index, int width, int characters)
            {
                string label = Label(labels, index);
                Assert.That(CompanionManagerProtocol.TextWidth(label), Is.LessThanOrEqualTo(width), index.ToString());
                Assert.That(label.Length, Is.LessThanOrEqualTo(characters), index.ToString());
            }
            Within(620, 650, 115);
            Within(625, 160, 40);
            Within(627, 736, 115);
            Within(628, 736, 115);
            Within(629, 600, 115);
            for (int head = 957; head < 963; head++) Within(head, 122, 24);
            for (int realm = 0; realm < 3; realm++)
                for (int slot = 0; slot < 5; slot++)
                    Within(963 + 5 * realm + slot, OpenBgWindowScreen.RealmSlots[slot].Width, OpenBgWindowScreen.RealmSlots[slot].Characters);
            Assert.That(OpenBgWindowScreen.TitleWidth(Label(labels, 626)), Is.LessThanOrEqualTo(468));
            Assert.That(Label(labels, 626).Length, Is.LessThanOrEqualTo(40));
            foreach (string label in labels)
            {
                Assert.That(label.All(ch => ch is >= ' ' and <= '~'), Is.True, label);
                Assert.That(label.Length, Is.LessThanOrEqualTo(CompanionManagerProtocol.MaximumTextLength));
            }
        }
    }

    [Test]
    public void EveryViewerGetsEveryIntent_NoGmDistinction()
    {
        // Owner amendment 2026-10-16: no GM lock. Neither the screen nor the session knows who looks.
        Assert.That(typeof(OpenBgWindowScreen).GetMethod(nameof(OpenBgWindowScreen.Build)).GetParameters().Select(p => p.ParameterType),
            Is.EqualTo(new[] { typeof(OpenBgStatusSnapshot), typeof(OpenBgSetupValues), typeof(OpenBgWindowPage), typeof(int), typeof(string), typeof(OpenBgWindowState) }));
        var first = new OpenBgWindowSession();
        var second = new OpenBgWindowSession();
        foreach (OpenBgWindowSession session in new[] { first, second })
        {
            session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.Fold, "u:1"));
            session.Page = OpenBgWindowPage.Setup;
        }
        Assert.That(first.Render(ThreeRealms(), Values()).ToLabels(), Is.EqualTo(second.Render(ThreeRealms(), Values()).ToLabels()));
        Assert.That(first.Signature, Is.EqualTo(second.Signature));
        OpenBgWindowRow[] setup = OpenBgWindowScreen.AllRows(ThreeRealms(), Values(), OpenBgWindowPage.Setup).ToArray();
        Assert.That(setup.Count(row => row.Link != null || row.Plus != null), Is.EqualTo(3 + 4 + 2 + 6 + 3));
    }

    [Test]
    public void Session_ResolvesControls_RejectsStaleRowControlsButNotNavigation()
    {
        var session = new OpenBgWindowSession();
        DateTime now = DateTime.UtcNow;
        session.Touch(now);
        session.Page = OpenBgWindowPage.Setup;
        OpenBgWindowView view = session.Render(ThreeRealms(), Values(), now);
        ushort token = session.Revision;
        int row = RowOf(view, "k:band_min");
        Assert.That(row, Is.GreaterThanOrEqualTo(0));
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlPlusBase + row, token, now), Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "band_min", 1)));
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlMinusBase + row, token, now).Amount, Is.EqualTo(-1));
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlPlusBase + row, (ushort)(token + 1), now), Is.Null);
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlPlusBase + row, token, now.AddMinutes(31)), Is.Null);
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlSelectBase + row, token, now), Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.Select, "k:band_min")));
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlSelectBase + row, (ushort)(token + 1), now), Is.Null);
        foreach (int control in new[] { 0xE0, 0xE1, 0xE8, 0xE9, 0xEA, 0xEB, 0xF0, 0xF1, 0xF2, 0xFD, 0xFE })
            Assert.That(session.Resolve(control, (ushort)(token + 7), now.AddHours(3)), Is.Not.Null, control.ToString("x"));
        Assert.That(session.Resolve(0xE0, token, now).Key, Is.EqualTo("Overview"));
        Assert.That(session.Resolve(0xE1, token, now).Key, Is.EqualTo("Setup"));
        Assert.That(session.Resolve(0xE9, token, now), Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.Scroll, Amount: 1)));
        Assert.That(session.Resolve(0xEA, token, now).Amount, Is.EqualTo(-18));
        Assert.That(session.Resolve(0xFD, token, now).Kind, Is.EqualTo(OpenBgWindowIntentKind.Close));
        Assert.That(session.Resolve(0xFE, token, now).Kind, Is.EqualTo(OpenBgWindowIntentKind.Refresh));
        Assert.That(session.Resolve(0x12, token, now), Is.Null, "row 18 does not exist");
        Assert.That(session.Resolve(0xA0, token, now), Is.Null, "unknown control");
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlSelectBase, token, now), Is.Null, "a section row selects nothing");
        Assert.That(OpenBgWindow.IsRowControl(0x60 + 17) && OpenBgWindow.IsRowControl(0x80) && !OpenBgWindow.IsRowControl(0x92) && !OpenBgWindow.IsRowControl(0xF0), Is.True);
    }

    [Test]
    public void Session_RevisionChangesOnlyWithTheView_AndOffsetsArePerPage()
    {
        var session = new OpenBgWindowSession();
        session.Render(ThreeRealms(), Values());
        ushort first = session.Revision;
        session.Render(ThreeRealms(), Values());
        Assert.That(session.Revision, Is.EqualTo(first));
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.Select, "r:Albion"));
        session.Render(ThreeRealms(), Values());
        Assert.That(session.Revision, Is.EqualTo(first), "a selection changes the mark and hint, not what a click does");
        session.Render(Status(), Values());
        Assert.That(session.Revision, Is.Not.EqualTo(first));
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.Scroll, Amount: 3));
        Assert.That(session.Offset, Is.EqualTo(3));
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.Tab, "Setup"));
        Assert.That(session.Page, Is.EqualTo(OpenBgWindowPage.Setup));
        Assert.That(session.Offset, Is.EqualTo(0));
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.Tab, "Overview"));
        Assert.That(session.Offset, Is.EqualTo(3));
    }

    /// <summary>
    /// Not a check: writes the labels 620-984 of an Overview and a Setup drawing of a status snapshot as JSON for the mock
    /// renderer (docs/open-bg-redesign/render.py). OPEN_BG_SAMPLE is an open-bg-status.json, OPEN_BG_LABEL_DUMP the output
    /// directory (Windows paths; pass both through WSLENV from WSL).
    /// </summary>
    [Test, Explicit("writes the mock renderer's input")]
    public void DumpLabelsForTheMockRenderer()
    {
        string sample = Environment.GetEnvironmentVariable("OPEN_BG_SAMPLE"), output = Environment.GetEnvironmentVariable("OPEN_BG_LABEL_DUMP");
        if (string.IsNullOrEmpty(sample) || string.IsNullOrEmpty(output))
            Assert.Ignore("set OPEN_BG_SAMPLE and OPEN_BG_LABEL_DUMP");
        OpenBgStatusSnapshot status = System.Text.Json.JsonSerializer.Deserialize<OpenBgStatusSnapshot>(System.IO.File.ReadAllText(sample));
        OpenBgSetupValues values = new(true, 1, 3, 6, 72, 15, 45, true, Presets: "std8:2,caster8:1,raid8:1,small:2");
        DateTime now = DateTime.UtcNow;
        var session = new OpenBgWindowSession();
        session.Touch(now);
        OpenBgWindowView view = session.Render(status, values, now);
        OpenBgWindowRow unit = view.Rows.First(row => row.Block != null && row.Block.StartsWith("u:", StringComparison.Ordinal));
        session.Navigate(unit.Fold);
        view = session.Render(status, values, now);
        int header = view.Rows.ToList().FindIndex(row => row.Key == unit.Key);
        (_, string question) = session.RelieveClick(view.Rows[header].Link, now);
        session.Message = question;
        session.Navigate(view.Rows[header + 2].Select);
        void Write(string name, string[] labels) => System.IO.File.WriteAllText(System.IO.Path.Combine(output, name),
            System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(0, labels.Length).ToDictionary(index => (index + 620).ToString(), index => labels[index])));
        Write("overview.json", session.Render(status, values, now).ToLabels());
        session.Message = "Bot cap: 68 -> 72.";
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.Tab, "Setup"));
        view = session.Render(status, values, now);
        session.Navigate(view.Rows.First(row => row.Key == "k:max_bots").Select);
        Write("setup.json", session.Render(status, values, now).ToLabels());
    }

    // ---- Task 6: runtime decisions, routing, transport, client probe ---------------------------------------------

    [TestCase(new[] { "&bgsetup" }, OpenBgSetupRoute.Open)]
    [TestCase(new[] { "&bgsetup", "ui", "0001", "ff" }, OpenBgSetupRoute.Ui)]
    [TestCase(new[] { "&bgsetup", "UI", "0001", "e0" }, OpenBgSetupRoute.Ui)]
    [TestCase(new[] { "&bgsetup", "ui" }, OpenBgSetupRoute.Ui)]
    [TestCase(new[] { "&bgsetup", "ui", "zz", "q", "x" }, OpenBgSetupRoute.Ui)]
    [TestCase(new[] { "&bgsetup", "text" }, OpenBgSetupRoute.Text)]
    [TestCase(new[] { "&bgsetup", "text", "3" }, OpenBgSetupRoute.Text)]
    [TestCase(new[] { "&bgsetup", "2" }, OpenBgSetupRoute.Page)]
    [TestCase(new[] { "&bgsetup", "help" }, OpenBgSetupRoute.Help)]
    [TestCase(new[] { "&bgsetup", "relieve", "5" }, OpenBgSetupRoute.Relieve)]
    [TestCase(new[] { "&bgsetup", "relieve" }, OpenBgSetupRoute.Relieve)]
    [TestCase(new[] { "&bgsetup", "groups8", "2" }, OpenBgSetupRoute.Write)]
    [TestCase(new[] { "&bgsetup", "presets", "std8:2,", "small:1" }, OpenBgSetupRoute.Write)]
    [TestCase(new[] { "&bgsetup", "nonsense" }, OpenBgSetupRoute.Write)]
    public void RouteBgsetup_SendsEachFormToItsPath(string[] args, OpenBgSetupRoute expected)
    {
        Assert.That(OpenBgWindow.RouteBgsetup(args), Is.EqualTo(expected));
    }

    [Test]
    public void RouteBgsetup_EmptyOrNullArgsOpenTheWindow()
    {
        Assert.That(OpenBgWindow.RouteBgsetup(null), Is.EqualTo(OpenBgSetupRoute.Open));
        Assert.That(OpenBgWindow.RouteBgsetup(Array.Empty<string>()), Is.EqualTo(OpenBgSetupRoute.Open));
    }

    [Test]
    public void ClientProbe_MissingAfterThreeSecondsWithoutReady_ConfirmedByReady()
    {
        DateTime t0 = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
        var probe = new OpenBgClientProbe();
        Assert.That(probe.State, Is.EqualTo(OpenBgClientState.Unknown));
        Assert.That(probe.UsesWindow, Is.True);
        Assert.That(probe.Sent(t0), Is.True, "the first open waits for the ready control");
        Assert.That(probe.State, Is.EqualTo(OpenBgClientState.Waiting));
        Assert.That(probe.Expire(t0.AddMilliseconds(2999)), Is.False, "still inside the 3 s");
        Assert.That(probe.Remaining(t0.AddMilliseconds(2000)), Is.EqualTo(TimeSpan.FromSeconds(1)));
        Assert.That(probe.Expire(t0.AddSeconds(3)), Is.True, "no ready within 3 s");
        Assert.That(probe.State, Is.EqualTo(OpenBgClientState.Missing));
        Assert.That(probe.UsesWindow, Is.False, "later /bgsetup calls go to the text page");
        Assert.That(probe.Expire(t0.AddSeconds(10)), Is.False, "the fallback fires once");
        Assert.That(probe.Sent(t0.AddSeconds(20)), Is.False);

        var patched = new OpenBgClientProbe();
        patched.Sent(t0);
        patched.Ready();
        Assert.That(patched.State, Is.EqualTo(OpenBgClientState.Confirmed));
        Assert.That(patched.Expire(t0.AddSeconds(5)), Is.False, "a ready client never falls back");
        Assert.That(patched.Sent(t0.AddSeconds(6)), Is.False, "a confirmed client needs no new wait");
        Assert.That(patched.UsesWindow, Is.True);

        var reopened = new OpenBgClientProbe();
        reopened.Sent(t0);
        reopened.Sent(t0.AddSeconds(2));
        Assert.That(reopened.Expire(t0.AddSeconds(3)), Is.False, "a second open restarts the wait");
        Assert.That(reopened.Expire(t0.AddSeconds(5)), Is.True);
        Assert.That(OpenBgWindow.ClientMissingHint, Is.EqualTo(
            "This client has no OPEN BG window; update the client files (UPDATE OFFLINE DAOC.cmd / Install-CompanionManager.ps1)"));
    }

    [Test]
    public void PlanClick_StaleRowTokenIsRejected_NavigationAndReadyAreNot()
    {
        var session = new OpenBgWindowSession();
        DateTime now = DateTime.UtcNow;
        session.Touch(now);
        session.Page = OpenBgWindowPage.Setup;
        OpenBgWindowView view = session.Render(ThreeRealms(), Values());
        int row = Array.FindIndex(view.Rows.ToArray(), item => item.Key == "k:groups8");
        ushort token = session.Revision;
        OpenBgWindowAction stale = OpenBgWindow.PlanClick(session, CompanionManagerProtocol.BgControlPlusBase + row, (ushort)(token + 1), now, Values());
        Assert.That(stale.Kind, Is.EqualTo(OpenBgWindowActionKind.Stale));
        Assert.That(stale.Message, Does.Contain("nothing was done"));
        Assert.That(OpenBgWindow.PlanClick(session, CompanionManagerProtocol.BgReadyControl, 0, now, Values()).Kind, Is.EqualTo(OpenBgWindowActionKind.Ready));
        Assert.That(OpenBgWindow.PlanClick(session, CompanionManagerProtocol.BgControlTabSituation, (ushort)(token + 1), now, Values()).Kind,
            Is.EqualTo(OpenBgWindowActionKind.Navigate));
        Assert.That(OpenBgWindow.PlanClick(session, CompanionManagerProtocol.BgControlClose, 0, now, Values()).Kind, Is.EqualTo(OpenBgWindowActionKind.Close));
        Assert.That(OpenBgWindow.PlanClick(session, 0x80, token, now, Values()).Kind, Is.EqualTo(OpenBgWindowActionKind.Ignore));
        OpenBgWindowAction write = OpenBgWindow.PlanClick(session, CompanionManagerProtocol.BgControlPlusBase + row, token, now, Values());
        Assert.That((write.Kind, write.RowKey, write.Value), Is.EqualTo((OpenBgWindowActionKind.Write, "open_bg_groups8", "2")));
    }

    [Test]
    public void PlanIntent_WritesAndRelievesForEveryPlayer_WithoutARightsCheck()
    {
        // Owner amendment 2026-10-16: no GM lock. The plan has no privilege input: any player's click writes or relieves.
        OpenBgWindowAction write = OpenBgWindow.PlanIntent(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "groups8", 1), Values());
        Assert.That((write.Kind, write.RowKey, write.Value), Is.EqualTo((OpenBgWindowActionKind.Write, "open_bg_groups8", "2")));
        Assert.That(write.Message, Does.Contain("1 -> 2"));
        OpenBgWindowAction relieve = OpenBgWindow.PlanIntent(new OpenBgWindowIntent(OpenBgWindowIntentKind.Relieve, Unit: 5), Values());
        Assert.That((relieve.Kind, relieve.Unit), Is.EqualTo((OpenBgWindowActionKind.Relieve, 5)));
        OpenBgWindowAction refused = OpenBgWindow.PlanIntent(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "groups8", 1), Values() with { Groups8 = 4 });
        Assert.That(refused.Kind, Is.EqualTo(OpenBgWindowActionKind.Refused));
        Assert.That(refused.Message, Is.EqualTo("8-man per realm is at its maximum (4)."));
        Assert.That(OpenBgWindow.PlanIntent(new OpenBgWindowIntent(OpenBgWindowIntentKind.Scroll, Amount: 1), Values()).Kind, Is.EqualTo(OpenBgWindowActionKind.Navigate));
        Assert.That(OpenBgWindow.PlanIntent(null, Values()).Kind, Is.EqualTo(OpenBgWindowActionKind.Ignore));
        Assert.That(typeof(OpenBgWindow).GetMethod(nameof(OpenBgWindow.PlanIntent)).GetParameters().Select(p => p.ParameterType),
            Is.EqualTo(new[] { typeof(OpenBgWindowIntent), typeof(OpenBgSetupValues) }), "no rights parameter");
    }

    [Test]
    public void HandleClientControl_IgnoresNullPlayerAndBadArguments()
    {
        Assert.DoesNotThrow(() => OpenBgWindow.HandleClientControl(null, "zz", "q"));
        Assert.DoesNotThrow(() => OpenBgWindow.HandleClientControl(null, null, null));
        Assert.DoesNotThrow(() => OpenBgWindow.Open(null));
        Assert.DoesNotThrow(() => OpenBgWindow.Close(null));
    }

    [Test]
    public void Transport_DefaultsKeepTheCustom8Sequence_AndBgUsesItsOwnBaseAndOps()
    {
        var sent = new string[3] { "a", null, null };
        var steps = CompanionWindowTransport.Plan(sent, new[] { "a", "b", "" }, true, 0x0012, true);
        Assert.That(steps, Is.EqualTo(new[]
        {
            (CompanionManagerProtocol.OpLabel, 1, "b"), (CompanionManagerProtocol.OpLabel, 2, ""),
            (CompanionManagerProtocol.OpToken, 0, "0012"), (CompanionManagerProtocol.OpShow, 0, ""),
        }));
        Assert.That(sent, Is.EqualTo(new[] { "a", "b", "" }), "sent labels are recorded");
        Assert.That(CompanionWindowTransport.Plan(sent, new[] { "a", "b", "" }, false, 1, false), Is.Empty);
        var bg = CompanionWindowTransport.Plan(new string[2], new[] { "x", "y" }, true, 0x0003, true,
            CompanionManagerProtocol.BgLabelBase, CompanionManagerProtocol.OpBgToken, CompanionManagerProtocol.OpBgShow);
        Assert.That(bg, Is.EqualTo(new[]
        {
            (CompanionManagerProtocol.OpLabel, 620, "x"), (CompanionManagerProtocol.OpLabel, 621, "y"),
            (CompanionManagerProtocol.OpBgToken, 0, "0003"), (CompanionManagerProtocol.OpBgShow, 0, ""),
        }));
        var publish = typeof(CompanionWindowTransport).GetMethod(nameof(CompanionWindowTransport.Publish)).GetParameters();
        Assert.That(publish.Single(p => p.Name == "labelBase").DefaultValue, Is.EqualTo(0));
        Assert.That(publish.Single(p => p.Name == "tokenOp").DefaultValue, Is.EqualTo(CompanionManagerProtocol.OpToken));
        Assert.That(publish.Single(p => p.Name == "showOp").DefaultValue, Is.EqualTo(CompanionManagerProtocol.OpShow));
    }

    [Test]
    public void OnlyChangedLabelsAreSent()
    {
        var sent = new string[3] { "a", "b", null };
        Assert.That(CompanionWindowTransport.ChangedLabels(sent, new[] { "a", "x", string.Empty }), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(CompanionWindowTransport.ChangedLabels(new[] { "a" }, new[] { "a" }), Is.Empty);
    }

    [Test]
    public void TheWindowRefreshesOnlyWhileOpenPlayedAndUsed()
    {
        DateTime now = new(2026, 10, 16, 15, 0, 0, DateTimeKind.Utc);
        Assert.Multiple(() =>
        {
            Assert.That(OpenBgWindow.KeepsRefreshing(true, true, now.AddMinutes(-29), now), Is.True);
            Assert.That(OpenBgWindow.KeepsRefreshing(true, true, now.AddMinutes(-31), now), Is.False, "idle half an hour counts as closed");
            Assert.That(OpenBgWindow.KeepsRefreshing(false, true, now, now), Is.False, "closed");
            Assert.That(OpenBgWindow.KeepsRefreshing(true, false, now, now), Is.False, "logged out");
            Assert.That(OpenBgWindow.IsOpen(true, now.AddMinutes(-31), now), Is.False, "the stock close X does not tell the server");
            Assert.That(OpenBgWindow.IsOpen(true, now.AddMinutes(-1), now), Is.True);
            Assert.That(OpenBgWindow.RefreshWallMilliseconds, Is.EqualTo(15_000));
        });
    }

    // ---- Task 6 review fixes ---------------------------------------------------------------------------------------

    [Test]
    public void ClientProbe_LateReadyAfterMissingRevivesTheWindow()
    {
        DateTime t0 = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
        var probe = new OpenBgClientProbe();
        probe.Sent(t0);
        Assert.That(probe.Expire(t0.AddSeconds(3)), Is.True);
        Assert.That(probe.Ready(), Is.True, "a ready after the fallback revives the window");
        Assert.That(probe.State, Is.EqualTo(OpenBgClientState.Confirmed));
        Assert.That(probe.UsesWindow, Is.True);
        Assert.That(probe.Ready(), Is.False, "a further ready is a plain answer");
        var waiting = new OpenBgClientProbe();
        waiting.Sent(t0);
        Assert.That(waiting.Ready(), Is.False, "an in-time ready is no revival");
        Assert.That(new OpenBgClientProbe().Ready(), Is.False);
    }

    [Test]
    public void ChatChangesRedrawAndShowOnlyAnOpenWindow()
    {
        DateTime now = new(2026, 10, 16, 15, 0, 0, DateTimeKind.Utc);
        Assert.That(OpenBgWindow.RedrawsChatChange(true, true, now.AddMinutes(-5), now), Is.True);
        Assert.That(OpenBgWindow.RedrawsChatChange(false, true, now, now), Is.False, "client without the window: text page");
        Assert.That(OpenBgWindow.RedrawsChatChange(true, false, now, now), Is.False, "closed");
        Assert.That(OpenBgWindow.RedrawsChatChange(true, true, now.AddMinutes(-31), now), Is.False, "idle counts as closed");
        // the redraw of a chat change shows the window again (op 10 last), so a window closed with the stock X reappears
        var steps = CompanionWindowTransport.Plan(new string[1], new[] { "x" }, false, 1, true,
            CompanionManagerProtocol.BgLabelBase, CompanionManagerProtocol.OpBgToken, CompanionManagerProtocol.OpBgShow);
        Assert.That(steps[^1].Operation, Is.EqualTo(CompanionManagerProtocol.OpBgShow));
    }

    [Test]
    public void ClickedAndChatWritesShareTheGothwaiteReserveNote()
    {
        string note = OpenBgSetupPages.GothwaiteReserveNote(true, 24);
        foreach (string name in new[] { "band_min", "band_max", "enabled", "max_bots" })
        {
            Assert.That(OpenBgSetupPages.WithReserveNote("done.", name, true, 24), Is.EqualTo("done. " + note), name);
            Assert.That(OpenBgSetupPages.WithReserveNote("done.", name, false, 24), Is.EqualTo("done."), name + " mode off");
        }
        Assert.That(OpenBgSetupPages.WithReserveNote("done.", "groups8", true, 24), Is.EqualTo("done."));
        Assert.That(OpenBgSetupPages.WithReserveNote("done.", null, true, 24), Is.EqualTo("done."));
        // the window's write names its setting by row key; the parser maps it back to the name the note checks
        OpenBgWindowAction write = OpenBgWindow.PlanIntent(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "band_min", 1), Values());
        Assert.That(OpenBgSetupParser.Find(write.RowKey).Name, Is.EqualTo("band_min"));
        Assert.That(OpenBgSetupParser.Find(OpenBgWindow.PlanIntent(new OpenBgWindowIntent(OpenBgWindowIntentKind.SetEnabled, On: false), Values()).RowKey).Name,
            Is.EqualTo("enabled"));
    }

    [Test]
    public void UnitLeaderName_UsesTheLivingLeader_AndASoloIsItsOwnLeader()
    {
        Assert.That(OpenBattlegroundStatus.UnitLeaderName("Leadbot", "Follower"), Is.EqualTo("Leadbot"), "bot leader of a group");
        Assert.That(OpenBattlegroundStatus.UnitLeaderName(null, "Solobot"), Is.EqualTo("Solobot"), "no group");
        Assert.That(OpenBattlegroundStatus.UnitLeaderName("", "Solobot"), Is.EqualTo("Solobot"), "group without a leader name");
    }
}
