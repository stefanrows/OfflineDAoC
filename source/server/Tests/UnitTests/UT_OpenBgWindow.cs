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
        session.Page = OpenBgWindowPage.Steuern;
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
        Assert.That(refused.Message, Does.Contain("cannot go further"));
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
}
