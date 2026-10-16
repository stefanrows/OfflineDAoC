using System;
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

    // ---- Task 5: pure page builder and session ----------------------------------------------------------------------

    private static OpenBgBotStatus Bot(string realm, string name, int unit, string kind, string archetype, string state = "inside",
        string role = "roam", bool raid = false) =>
        new(realm, name, 45, "Cleric", unit, kind, name, state, true, role, raid, 120, archetype);

    private static OpenBgStatusSnapshot Status(params OpenBgBotStatus[] bots) =>
        new(DateTime.UtcNow, true, true, true, true, null, "Fight", "Guild X", 125, "Albion", 19.8, 20, 72,
            new[] { new OpenBgPlayerStatus("Albion", "Aaron", 50) }, bots, Array.Empty<OpenBgGuestStatus>(), null,
            new OpenBgFieldFigures(3, 12, 20, 72, 28));

    private static OpenBgStatusSnapshot ThreeRealms() => Status(
        Bot("Albion", "Alb1", 1, "group8", "std8", raid: true), Bot("Albion", "Alb2", 1, "group8", "std8", raid: true),
        Bot("Midgard", "Mid1", 2, "small", "small"), Bot("Hibernia", "Hib1", 3, "solo", "", "travelling"));

    private static OpenBgSetupValues Values(string mode = "serverpvp") => new(true, 1, 2, 4, 72, 15, 45, true, Mode: mode, Presets: "std8:2,small:1");

    private static OpenBgWindowView Build(OpenBgStatusSnapshot status, OpenBgWindowPage page, int offset = 0, string mode = "serverpvp") =>
        OpenBgWindowScreen.Build(status, Values(mode), page, offset, "");

    [Test]
    public void LabelTable_IsPinned()
    {
        Assert.That(CompanionManagerProtocol.BgLabelBase, Is.EqualTo(620));
        string[] labels = Build(ThreeRealms(), OpenBgWindowPage.Lage).ToLabels();
        Assert.That(labels, Has.Length.EqualTo(365));
        Assert.That(labels[0], Is.Not.Empty);                       // 620 status
        Assert.That(labels[1], Is.Empty);                           // 621 Lage inactive
        Assert.That(labels[2], Is.EqualTo("Lage"));                 // 622 Lage active
        Assert.That(labels[3], Is.EqualTo("Steuern"));              // 623 Steuern inactive
        Assert.That(labels[4], Is.Empty);                           // 624 Steuern active
        Assert.That(labels[5], Does.Contain("of"));                 // 625 page indicator
        Assert.That(labels[6], Does.Contain("Phase"));              // 626 first header
        Assert.That(OpenBgWindowScreen.RowLabelIndex(0, 0), Is.EqualTo(633 - 620));
        Assert.That(OpenBgWindowScreen.RowLabelIndex(21, 15), Is.EqualTo(984 - 620));
        Assert.That(OpenBgWindowScreen.RowLabelIndex(3, 5), Is.EqualTo(633 + 48 + 5 - 620));
        string[] steuern = Build(ThreeRealms(), OpenBgWindowPage.Steuern).ToLabels();
        Assert.That(steuern[1], Is.EqualTo("Lage"));
        Assert.That(steuern[4], Is.EqualTo("Steuern"));
        Assert.That(steuern[2], Is.Empty);
        Assert.That(steuern[3], Is.Empty);
    }

    [Test]
    public void Lage_ShowsHeadersAndRows_ServerPvpSummarisesByArchetype()
    {
        OpenBgWindowView view = Build(ThreeRealms(), OpenBgWindowPage.Lage);
        string headers = string.Join("|", view.Header);
        foreach (string word in new[] { "Phase", "Fight", "Server PvP", "Raid", "Guild X", "Keep 11", "19.8", "Field", "28%", "Crew", "Aaron" })
            Assert.That(headers, Does.Contain(word));
        string text = string.Join("|", view.Rows.Select(row => row.Title + row.Text + row.Name + row.Unit));
        Assert.That(text, Does.Contain("std8"));
        Assert.That(text, Does.Contain("Alb1"));
        Assert.That(text, Does.Contain("Hib1"));
        Assert.That(view.Rows.All(row => row.Link == null && row.Minus == null && row.Plus == null), Is.True);
    }

    [Test]
    public void Lage_PerRealmSummarisesByRealm()
    {
        OpenBgWindowView view = Build(ThreeRealms(), OpenBgWindowPage.Lage, mode: "perrealm");
        Assert.That(string.Join("|", view.Header), Does.Contain("Per realm"));
        OpenBgWindowRow albion = view.Rows.First(row => row.Name == "Albion" && row.Realm == eRealm.Albion);
        Assert.That(albion.Value, Does.Contain("2"));
        Assert.That(view.Rows.Any(row => row.Text != null && row.Text.StartsWith("std8")), Is.False);
    }

    [Test]
    public void Scrolling_ClampsAndShowsRange()
    {
        var bots = Enumerable.Range(1, 40).Select(index => Bot("Albion", "Bot" + index, index, "solo", "")).ToArray();
        OpenBgStatusSnapshot status = Status(bots);
        OpenBgWindowView first = Build(status, OpenBgWindowPage.Lage);
        Assert.That(first.Rows, Has.Count.EqualTo(22));
        Assert.That(first.TotalRows, Is.GreaterThan(40));
        Assert.That(first.PageIndicator, Is.EqualTo($"1-22 of {first.TotalRows}"));
        OpenBgWindowView last = Build(status, OpenBgWindowPage.Lage, 9999);
        Assert.That(last.Offset, Is.EqualTo(first.TotalRows - 22));
        Assert.That(last.PageIndicator, Is.EqualTo($"{last.Offset + 1}-{first.TotalRows} of {first.TotalRows}"));
        Assert.That(Build(status, OpenBgWindowPage.Lage, -5).Offset, Is.EqualTo(0));
    }

    [Test]
    public void Texts_NeverExceedColumnWidthOrMaxCharacters()
    {
        var bots = new[] { Bot("Albion", new string('W', 60), 1, "group8", "std8") };
        OpenBgWindowView view = OpenBgWindowScreen.Build(Status(bots), Values(), OpenBgWindowPage.Lage, 0, new string('M', 400));
        string[] labels = view.ToLabels();
        for (int row = 0; row < 22; row++)
        {
            int[] widths = { 740, 740, 140, 140, 140, 140, 26, 104, 100, 150, 150, 110, 36, 36, 36, 36 };
            int[] chars = { 120, 120, 32, 32, 32, 32, 6, 32, 24, 40, 40, 32, 6, 6, 6, 6 };
            for (int slot = 0; slot < 16; slot++)
            {
                string label = labels[OpenBgWindowScreen.RowLabelIndex(row, slot)];
                Assert.That(CompanionManagerProtocol.TextWidth(label), Is.LessThanOrEqualTo(widths[slot]), $"row {row} slot {slot}");
                Assert.That(label.Length, Is.LessThanOrEqualTo(chars[slot]), $"row {row} slot {slot}");
            }
        }
        Assert.That(CompanionManagerProtocol.TextWidth(labels[11]), Is.LessThanOrEqualTo(740));   // reply line 631
        Assert.That(labels[11].Length, Is.LessThanOrEqualTo(120));
        Assert.That(labels.Skip(6).Take(5).All(label => label.Length <= 120 && CompanionManagerProtocol.TextWidth(label) <= 740), Is.True);
    }

    [Test]
    public void Steuern_HasStepsAndIntentsForEveryone()
    {
        OpenBgWindowView view = OpenBgWindowScreen.Build(ThreeRealms(), Values(), OpenBgWindowPage.Steuern, 0, "");
        Assert.That(view.Rows, Has.Count.EqualTo(22));
        OpenBgWindowRow[] rows = OpenBgWindowScreen.AllRows(ThreeRealms(), Values() with { BandMin = 42, BandMax = 45, Groups8 = 2, Small = 3, Solos = 5 }, OpenBgWindowPage.Steuern).ToArray();
        foreach (string key in new[] { "band_min", "band_max", "max_bots", "groups8", "small", "solos", "drift", "cycle_min", "cycle_max" })
        {
            OpenBgWindowRow row = rows.First(item => item.Key == "k:" + key);
            Assert.That(row.Minus?.Kind == OpenBgWindowIntentKind.Adjust || key == "drift", Is.True, key);
            Assert.That(row.Plus?.Kind == OpenBgWindowIntentKind.Adjust || key == "drift", Is.True, key);
        }
        foreach (string archetype in new[] { "std8", "caster8", "raid8", "stealth4", "small", "duo" })
        {
            OpenBgWindowRow row = rows.First(item => item.Key == "p:" + archetype);
            Assert.That(row.Plus.Key, Is.EqualTo("preset:" + archetype));
            Assert.That(row.Plus.Amount, Is.EqualTo(1));
        }
        Assert.That(rows.First(item => item.Key == "k:enabled").Link, Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.SetEnabled, On: false)));
        Assert.That(rows.First(item => item.Key == "k:mode").Link, Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.SetMode, "perrealm")));
        Assert.That(rows.First(item => item.Key == "k:peace").Link.Kind, Is.EqualTo(OpenBgWindowIntentKind.CyclePeace));
        Assert.That(rows.Count(item => item.Link?.Kind == OpenBgWindowIntentKind.Relieve), Is.EqualTo(3));
        Assert.That(rows.First(item => item.Link?.Kind == OpenBgWindowIntentKind.Relieve && item.Link.Unit == 1).Value, Is.EqualTo("[Relieve]"));
        Assert.That(rows.Any(item => item.Key.StartsWith("units:")), Is.False);   // serverpvp: no per realm rows
        var perRealm = OpenBgWindowScreen.AllRows(ThreeRealms(), Values("perrealm"), OpenBgWindowPage.Steuern);
        Assert.That(perRealm.Count(item => item.Key.StartsWith("units:")), Is.EqualTo(9));
    }

    [Test]
    public void Steuern_StepsAndLimits()
    {
        OpenBgWindowRow[] rows = OpenBgWindowScreen.AllRows(ThreeRealms(), Values(), OpenBgWindowPage.Steuern).ToArray();
        Assert.That(OpenBgWindowScreen.StepOf("max_bots"), Is.EqualTo(4));
        Assert.That(OpenBgWindowScreen.StepOf("cycle_min"), Is.EqualTo(5));
        Assert.That(OpenBgWindowScreen.StepOf("cycle_max"), Is.EqualTo(5));
        Assert.That(OpenBgWindowScreen.StepOf("groups8"), Is.EqualTo(1));
        Assert.That(rows.First(row => row.Key == "k:max_bots").Unit, Does.Contain("4"));
        OpenBgSetupKey cap = OpenBgSetupParser.Find("max_bots");
        Assert.That(rows.First(row => row.Key == "k:max_bots").Class, Is.EqualTo($"{cap.Min}-{cap.Max}"));
        // groups8 at its maximum cannot go up; at zero cannot go down
        OpenBgWindowRow top = OpenBgWindowScreen.AllRows(ThreeRealms(), Values() with { Groups8 = 4 }, OpenBgWindowPage.Steuern)
            .First(row => row.Key == "k:groups8");
        Assert.That(top.Plus, Is.Null);
        Assert.That(top.Minus, Is.Not.Null);
        var plus = OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "max_bots", 1), Values());
        Assert.That((plus.Ok, plus.RowKey, plus.Value), Is.EqualTo((true, "open_bg_max_bots", "76")));
        var cycle = OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "cycle_min", -1), Values());
        Assert.That(cycle.Value, Is.EqualTo("10"));
        var preset = OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "preset:duo", 1), Values());
        Assert.That(preset.Value, Is.EqualTo("std8:2,small:1,duo:1"));
        var off = OpenBgWindowScreen.ToWrite(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "preset:std8", -1),
            Values() with { Presets = "std8:1" });
        Assert.That((off.Ok, off.RowKey, off.Value), Is.EqualTo((true, "open_bg_presets", "")));
    }

    [Test]
    public void Session_ResolvesControls_RejectsStaleRowControlsButNotNavigation()
    {
        var session = new OpenBgWindowSession();
        DateTime now = DateTime.UtcNow;
        session.Touch(now);
        session.Page = OpenBgWindowPage.Steuern;
        OpenBgWindowView view = session.Render(ThreeRealms(), Values());
        ushort token = session.Revision;
        int plusRow = Array.FindIndex(view.Rows.ToArray(), row => row.Key == "k:band_min");
        Assert.That(plusRow, Is.GreaterThanOrEqualTo(0));
        OpenBgWindowIntent plus = session.Resolve(CompanionManagerProtocol.BgControlPlusBase + plusRow, token, now);
        Assert.That(plus, Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "band_min", 1)));
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlMinusBase + plusRow, token, now).Amount, Is.EqualTo(-1));
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlPlusBase + plusRow, (ushort)(token + 1), now), Is.Null);
        Assert.That(session.Resolve(CompanionManagerProtocol.BgControlPlusBase + plusRow, token, now.AddMinutes(31)), Is.Null);
        foreach (int control in new[] { 0xE0, 0xE1, 0xE8, 0xE9, 0xEA, 0xEB, 0xFD, 0xFE })
            Assert.That(session.Resolve(control, (ushort)(token + 7), now.AddHours(3)), Is.Not.Null, control.ToString("x"));
        Assert.That(session.Resolve(0xE0, token, now).Key, Is.EqualTo("Lage"));
        Assert.That(session.Resolve(0xE1, token, now).Key, Is.EqualTo("Steuern"));
        Assert.That(session.Resolve(0xE9, token, now), Is.EqualTo(new OpenBgWindowIntent(OpenBgWindowIntentKind.Scroll, Amount: 1)));
        Assert.That(session.Resolve(0xEA, token, now).Amount, Is.EqualTo(-22));
        Assert.That(session.Resolve(0xFD, token, now).Kind, Is.EqualTo(OpenBgWindowIntentKind.Close));
        Assert.That(session.Resolve(0xFE, token, now).Kind, Is.EqualTo(OpenBgWindowIntentKind.Refresh));
        Assert.That(session.Resolve(0x15 + 1, token, now), Is.Null);   // unknown control
        Assert.That(session.Resolve(0x80, token, now), Is.Null);
    }

    [Test]
    public void Session_RevisionChangesOnlyWithTheView_AndOffsetsArePerPage()
    {
        var session = new OpenBgWindowSession();
        session.Render(ThreeRealms(), Values());
        ushort first = session.Revision;
        session.Render(ThreeRealms(), Values());
        Assert.That(session.Revision, Is.EqualTo(first));
        session.Render(Status(), Values());
        Assert.That(session.Revision, Is.Not.EqualTo(first));
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.Scroll, Amount: 3));
        Assert.That(session.Offset, Is.EqualTo(3));
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.Tab, "Steuern"));
        Assert.That(session.Page, Is.EqualTo(OpenBgWindowPage.Steuern));
        Assert.That(session.Offset, Is.EqualTo(0));
        session.Navigate(new OpenBgWindowIntent(OpenBgWindowIntentKind.Tab, "Lage"));
        Assert.That(session.Offset, Is.EqualTo(3));
    }
}
