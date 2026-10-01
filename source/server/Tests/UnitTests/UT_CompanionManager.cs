using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_CompanionManager
{
    [Test]
    public void BodyIsFixedSizeVersionedAndAlwaysTerminated()
    {
        byte[] body = BuildBody(OpLabel, LabelCount - 1, new string('x', 300));
        Assert.Multiple(() =>
        {
            Assert.That(body, Has.Length.EqualTo(BodySize));
            Assert.That(body[0], Is.Zero, "DebugMode flag byte stays off");
            Assert.That(body[1], Is.EqualTo(Marker));
            Assert.That(body[2], Is.EqualTo(ProtocolVersion));
            Assert.That(body[3], Is.EqualTo(OpLabel));
            Assert.That(body[4] | body[5] << 8, Is.EqualTo(LabelCount - 1));
            Assert.That(body.Skip(TextOffset).Take(MaximumTextLength).All(value => value == 'x'), Is.True);
            Assert.That(body[BodySize - 1], Is.Zero);
            Assert.That(Marker, Is.Not.EqualTo(0x52), "Raid marker stays separate");
        });
        Assert.That(body[5], Is.EqualTo((LabelCount - 1) >> 8), "The label index is 16 bits wide");
        Assert.That(BuildBody(OpLabel, 300)[4..6], Is.EqualTo(new byte[] { 44, 1 }));
        Assert.That(() => BuildBody(OpLabel, 65536), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void TextIsFoldedToPrintableAscii()
    {
        Assert.That(Sanitize("Kiri’s “note” — a→b…\né"), Is.EqualTo("Kiri's \"note\" - a>b... "));
        Assert.That(Sanitize(null), Is.Empty);
        Assert.That(Sanitize(new string('a', 200)), Has.Length.EqualTo(MaximumTextLength));
        Assert.That(Sanitize(new string('a', 200), int.MaxValue), Has.Length.EqualTo(200));
    }

    [Test]
    public void ClientControlsAcceptOnlyExactLowercaseHex()
    {
        Assert.That(TryParseClientControl("0a9f", "23", out ushort revision, out int control), Is.True);
        Assert.That((revision, control), Is.EqualTo(((ushort)0x0a9f, 0x23)));
        Assert.That(TryParseClientControl("0a9f", "df", out _, out _), Is.True, "The last manager control");
        Assert.That(FormatToken(0x0a9f), Is.EqualTo("0a9f"));
        foreach ((string token, string value) in new[]
                 {
                     ("0A9F", "23"), ("0a9", "23"), ("0a9ff", "23"), ("0a9f", "2"), ("0a9f", "E0"),
                     ("0a9f", "e0"), ("0a9f", "ff"), (null, "23"), ("0a9f", null), ("0a9g", "23"), ("0a9f", "-1"),
                 })
            Assert.That(TryParseClientControl(token, value, out _, out _), Is.False, $"{token} {value}");
    }

    [Test]
    public void QueryPolicyLimitsLengthAndCharacters()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CompanionManagerSession.NormalizeQuery("  Mid   <b>heal</b>;  "), Is.EqualTo("Mid bhealb"));
            Assert.That(CompanionManagerSession.NormalizeQuery("O'Brien-Smith"), Is.EqualTo("O'Brien-Smith"));
            Assert.That(CompanionManagerSession.NormalizeQuery(new string('a', 40)), Has.Length.EqualTo(24));
            Assert.That(CompanionManagerSession.NormalizeQuery("[\"]"), Is.Empty);
            Assert.That(CompanionManagerSession.NormalizeQuery(null), Is.Empty);
        });
    }

    [Test]
    public void SearchMatchesNameClassAndRealmAcrossTermsWithFilters()
    {
        var kiri = new CompanionManagerEntry("a:kiri", eRealm.Midgard, eCharacterClass.Shaman, "Kiri", Origin: "Story", State: "Available");
        Assert.Multiple(() =>
        {
            Assert.That(CompanionManagerSession.Matches(kiri, "kir", null, null), Is.True);
            Assert.That(CompanionManagerSession.Matches(kiri, "SHAM", null, null), Is.True);
            Assert.That(CompanionManagerSession.Matches(kiri, "mid sham", null, null), Is.True);
            Assert.That(CompanionManagerSession.Matches(kiri, "alb sham", null, null), Is.False);
            Assert.That(CompanionManagerSession.Matches(kiri, "", eRealm.Albion, null), Is.False);
            Assert.That(CompanionManagerSession.Matches(kiri, "", eRealm.Midgard, BotPveGroupRole.Healer), Is.True);
            Assert.That(CompanionManagerSession.Matches(kiri, "", null, BotPveGroupRole.Tank), Is.False);
            Assert.That(CompanionManagerSession.Matches(kiri, "story avail", null, null), Is.True, "Origin and state are searchable");
            Assert.That(CompanionManagerSession.Matches(kiri, "bench", null, null), Is.False);
        });
    }

    [Test]
    public void ScrollingReachesEveryStoryCompanionAndGeneratedClass()
    {
        var session = new CompanionManagerSession { Tab = CompanionManagerTab.Recruit };
        IReadOnlyList<CompanionManagerEntry> entries = RecruitEntries();
        IReadOnlyList<CompanionManagerLine> filtered = session.Arrange(session.Filter(entries));
        int rows = session.RowCount;
        var seen = new List<string>();
        for (int page = 0; page * rows < filtered.Count + rows; page++)
        {
            session.Recruit.Offset = page * rows;
            session.VisibleRows(filtered);
            seen.AddRange(session.RowKeys.Where(key => key != null && !key.StartsWith(CompanionManagerSession.HeaderPrefix, StringComparison.Ordinal) &&
                !seen.Contains(key)).ToArray());
        }
        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(78 + 39));
            Assert.That(filtered.Count(line => line.IsHeader), Is.EqualTo(3), "Candidates are sectioned by realm");
            Assert.That(seen, Has.Count.EqualTo(117));
            Assert.That(seen.Count(key => key.StartsWith("a:", StringComparison.Ordinal)), Is.EqualTo(78));
            Assert.That(seen.Count(key => key.StartsWith("g:", StringComparison.Ordinal)), Is.EqualTo(39));
            Assert.That(CompanionCharacterCatalog.All.All(entry => seen.Contains("a:" + entry.Key)), Is.True);
        });

        session.Recruit.Offset = 10_000;
        session.VisibleRows(filtered);
        Assert.That(session.Recruit.Offset, Is.EqualTo(filtered.Count - rows), "Offsets clamp to the last full page");
        session.Recruit.Offset = -5;
        session.VisibleRows(filtered);
        Assert.That(session.Recruit.Offset, Is.Zero);
    }

    [Test]
    public void EveryRealmAndRoleFilterKeepsBothRecruitmentKinds()
    {
        var session = new CompanionManagerSession { Tab = CompanionManagerTab.Recruit };
        IReadOnlyList<CompanionManagerEntry> entries = RecruitEntries();
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            session.Recruit.Realm = realm;
            session.Recruit.Role = null;
            IReadOnlyList<CompanionManagerEntry> filtered = session.Filter(entries);
            Assert.That(filtered.All(entry => entry.Realm == realm), Is.True);
            int classes = TemporaryGroupClassCatalog.ForRealm(realm).Count();
            Assert.That(classes, Is.GreaterThanOrEqualTo(12), realm.ToString());
            Assert.That(filtered.Count(entry => entry.Key.StartsWith("a:", StringComparison.Ordinal)), Is.EqualTo(2 * classes), realm.ToString());
            Assert.That(filtered.Count(entry => entry.Key.StartsWith("g:", StringComparison.Ordinal)), Is.EqualTo(classes), realm.ToString());
            foreach (BotPveGroupRole role in Enum.GetValues<BotPveGroupRole>())
            {
                session.Recruit.Role = role;
                Assert.That(session.Filter(entries).All(entry => BotPartyRoles.CanFill(entry.Class, role)), Is.True);
            }
        }
    }

    [Test]
    public void RowClicksResolveOnlyToRecordedKeysAndRevisionsTrackClickMeaning()
    {
        var session = new CompanionManagerSession();
        session.Group = CompanionManagerGroup.None;
        session.RowCount = RowSizes[0];
        var entries = Enumerable.Range(0, 30).Select(index => new CompanionManagerEntry($"c:{index:00}", eRealm.Albion,
            eCharacterClass.Cleric, $"Name {index:00}")).ToArray();
        IReadOnlyList<CompanionManagerLine> filtered = session.Arrange(session.Filter(entries));
        session.Roster.Offset = 5;
        session.VisibleRows(filtered);
        Assert.That(session.TrySelectRow(0, out string key), Is.True);
        Assert.That(key, Is.EqualTo("c:05"));
        Assert.That(session.TrySelectRow(Rows, out _), Is.False);
        Assert.That(session.TrySelectRow(session.RowCount, out _), Is.False, "Rows beyond the chosen size resolve to nothing");

        session.Roster.Offset = 15;
        session.VisibleRows(filtered);
        Assert.That(session.RowKeys.Count(value => value != null), Is.EqualTo(session.RowCount));
        Assert.That(session.RowKeys[session.RowCount - 1], Is.EqualTo("c:29"));

        ushort first = session.Revision;
        Assert.That(session.CommitView("a"), Is.True);
        ushort second = session.Revision;
        Assert.That(session.CommitView("a"), Is.False, "Unchanged click meaning keeps the token");
        Assert.That(session.Revision, Is.EqualTo(second));
        Assert.That(second, Is.Not.EqualTo(first));

        DateTime now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        session.Region = 1;
        session.LastUseUtc = now;
        Assert.Multiple(() =>
        {
            Assert.That(session.IsStale(second, 1, now), Is.False);
            Assert.That(session.IsStale(first, 1, now), Is.True);
            Assert.That(session.IsStale(second, 2, now), Is.True);
            Assert.That(session.IsStale(second, 1, now + CompanionManagerSession.IdleLimit + TimeSpan.FromSeconds(1)), Is.True);
            Assert.That(CompanionManagerSession.IsIndexedControl(ControlRowBase + Rows - 1), Is.True);
            Assert.That(CompanionManagerSession.IsIndexedControl(ControlDetailBase + DetailLines - 1), Is.True);
            Assert.That(CompanionManagerSession.IsIndexedControl(ControlActionBase), Is.True);
            Assert.That(CompanionManagerSession.IsIndexedControl(ControlListDown), Is.False);
            Assert.That(CompanionManagerSession.IsIndexedControl(ControlTabRecruit), Is.False);
        });
    }

    [Test]
    public void RevisionNeverReturnsTheClientDefaultToken()
    {
        var session = new CompanionManagerSession();
        for (int index = 0; index < ushort.MaxValue + 2; index++)
        {
            session.CommitView(index.ToString());
            Assert.That(session.Revision, Is.Not.Zero);
        }
    }

    [Test]
    public void ViewMapsStateOntoFixedColourLabels()
    {
        var view = new CompanionManagerView
        {
            Status = "status", Message = "message", Header = "Kiri", HeaderRealm = eRealm.Midgard,
            ListIndicator = "1-3 of 3",
        };
        view.Toggles[ToggleTabRoster] = ("Roster (1/78)", false);
        view.Toggles[ToggleTabRecruit] = ("Recruit", true);
        view.Rows[0] = new CompanionManagerView.RowView(null, true, eRealm.Hibernia, "Aine", "3", "Druid", "Story", "Active", true);
        view.Rows[1] = new CompanionManagerView.RowView(null, false, eRealm.Albion, "Aldren", "1", "Armsman", "Regular", "Bench", false);
        view.Rows[2] = new CompanionManagerView.RowView("[-] In your group (1)", false, eRealm.None, null, null, null, null, null, false);
        view.Details[0] = ("plain", false);
        view.Details[1] = ("link", true);
        view.ActionLabels[0] = ("[Invite]", true);
        view.ActionLabels[1] = ("[Open bag]", false);
        string[] labels = view.ToLabels();
        Assert.Multiple(() =>
        {
            Assert.That(labels, Has.Length.EqualTo(LabelCount));
            Assert.That(labels[LabelStatus], Is.EqualTo("status"));
            Assert.That(labels[LabelMessage], Is.EqualTo("message"));
            Assert.That(labels[LabelToggleBase + 2 * ToggleTabRoster], Is.EqualTo("Roster (1/78)"));
            Assert.That(labels[LabelToggleBase + 2 * ToggleTabRoster + 1], Is.Empty);
            Assert.That(labels[LabelToggleBase + 2 * ToggleTabRecruit], Is.Empty);
            Assert.That(labels[LabelToggleBase + 2 * ToggleTabRecruit + 1], Is.EqualTo("Recruit"));
            Assert.That(labels[LabelRowBase], Is.EqualTo(">"));
            Assert.That(labels[LabelRowBase + 3], Is.EqualTo("Aine"), "Hibernia colour slot");
            Assert.That(labels[LabelRowBase + 1], Is.Empty);
            Assert.That(labels[LabelRowBase + RowLevel], Is.EqualTo("3"));
            Assert.That(labels[LabelRowBase + RowClass], Is.EqualTo("Druid"));
            Assert.That(labels[LabelRowBase + RowType], Is.EqualTo("Story"));
            Assert.That(labels[LabelRowBase + RowStateActive], Is.EqualTo("Active"), "Active state uses the green label");
            Assert.That(labels[LabelRowBase + RowStateIdle], Is.Empty);
            Assert.That(labels[LabelRowBase + RowStride], Is.Empty);
            Assert.That(labels[LabelRowBase + RowStride + 1], Is.EqualTo("Aldren"), "Albion colour slot");
            Assert.That(labels[LabelRowBase + RowStride + RowStateIdle], Is.EqualTo("Bench"), "Benched state uses the grey label");
            Assert.That(labels[LabelRowBase + RowStride + RowStateActive], Is.Empty);
            Assert.That(labels[LabelRowBase + 2 * RowStride], Is.EqualTo("[-] In your group (1)"), "A header fills the first label of its row");
            Assert.That(labels[LabelRowBase + 2 * RowStride + RowName], Is.Empty);
            Assert.That(labels[LabelRowBase + 3 * RowStride + RowName], Is.Empty, "Unused rows stay blank");
            Assert.That(labels[LabelHeaderBase + 1], Is.EqualTo("Kiri"), "Midgard colour slot");
            Assert.That(labels[LabelDetailBase], Is.EqualTo("plain"));
            Assert.That(labels[LabelDetailBase + 1], Is.Empty);
            Assert.That(labels[LabelDetailBase + 3], Is.EqualTo("link"));
            Assert.That(labels[LabelActionBase], Is.EqualTo("[Invite]"));
            Assert.That(labels[LabelActionBase + 3], Is.EqualTo("[Open bag]"), "Disabled actions use the grey label");
            Assert.That(labels[LabelActionBase + 2], Is.Empty);
            Assert.That(labels.All(label => label.Length <= MaximumTextLength), Is.True);
            Assert.That(TextWidth(labels[LabelRowBase + RowClass]), Is.LessThanOrEqualTo(WidthRowClass));
            Assert.That(labels[LabelDetailUp], Is.Empty, "Nothing to scroll hides [Up]");
            Assert.That(labels[LabelDetailDown], Is.Empty, "Nothing to scroll hides [Down]");
        });
    }

    [Test]
    public void DetailScrollLinksAppearOnlyInTheDirectionThatScrolls()
    {
        string[] Links(bool up, bool down)
        {
            string[] labels = new CompanionManagerView { DetailCanScrollUp = up, DetailCanScrollDown = down }.ToLabels();
            return new[] { labels[LabelDetailUp], labels[LabelDetailDown] };
        }

        Assert.Multiple(() =>
        {
            Assert.That(Links(false, true), Is.EqualTo(new[] { "", "[Down]" }), "Top of a long page");
            Assert.That(Links(true, true), Is.EqualTo(new[] { "[Up]", "[Down]" }), "Middle of a long page");
            Assert.That(Links(true, false), Is.EqualTo(new[] { "[Up]", "" }), "Bottom of a long page");
            Assert.That(LabelDetailUp, Is.GreaterThan(LabelActionBase + 2 * Actions - 1),
                "Appended after the 0.32.1 labels so older clients ignore them");
            Assert.That(LabelCount, Is.EqualTo(LabelDetailDown + 1));
        });
    }

    [Test]
    public void WrapUsesLabelPixelsKeepsWordsAndSplitsOnlyOverlongOnes()
    {
        Assert.That(CompanionManagerSession.Wrap("one two three four", TextWidth("one two")),
            Is.EqualTo(new[] { "one two", "three", "four" }));
        Assert.That(CompanionManagerSession.Wrap("abcdefghijk", TextWidth("abcd")), Is.EqualTo(new[] { "abcd", "efgh", "ijk" }));
        Assert.That(CompanionManagerSession.Wrap("a\n\nb", 100), Is.EqualTo(new[] { "a", "", "b" }));
        IReadOnlyList<string> copy = CompanionManagerSession.Wrap(CompanionManager.StoryCopy, WidthDetail - 6);
        Assert.That(copy.All(line => TextWidth(line) <= WidthDetail - 6), Is.True);
        Assert.That(string.Join(' ', copy), Is.EqualTo(CompanionManager.StoryCopy));
    }

    [Test]
    public void FitShortensOnlyTextWiderThanItsLabel()
    {
        Assert.That(TextWidth("abc"), Is.EqualTo(8 + 8 + 7), "arial14 advances");
        Assert.That(Fit("Spiritmaster", WidthRowClass), Is.EqualTo("Spiritmaster"));
        Assert.That(Fit("Squad 2 lead", WidthRowState), Is.EqualTo("Squad 2 lead"));
        string fitted = Fit(new string('m', 40), WidthAction);
        Assert.That(fitted, Does.EndWith("..."));
        Assert.That(TextWidth(fitted), Is.LessThanOrEqualTo(WidthAction));
        Assert.That(TextWidth("[Open in roster]"), Is.LessThanOrEqualTo(WidthAction));
        Assert.That(TextWidth("[Return: blocked]"), Is.LessThanOrEqualTo(WidthAction));
        Assert.That(TextWidth("Training & Tactics"), Is.LessThanOrEqualTo(123));
    }

    private static CompanionManagerEntry Entry(string key, eRealm realm, eCharacterClass characterClass, string name, int level,
        bool active, BotPveGroupRole role = BotPveGroupRole.Attacker) =>
        new(key, realm, characterClass, name, level, "Regular", active ? "Active" : "Bench", active, active, role);

    private static CompanionManagerEntry[] SampleRoster() => new[]
    {
        Entry("c:1", eRealm.Albion, eCharacterClass.Cleric, "Cora", 50, false, BotPveGroupRole.Healer),
        Entry("c:2", eRealm.Midgard, eCharacterClass.Warrior, "Brandr", 41, true, BotPveGroupRole.Tank),
        Entry("c:3", eRealm.Albion, eCharacterClass.Wizard, "Alys", 12, false),
        Entry("c:4", eRealm.Hibernia, eCharacterClass.Druid, "Dara", 30, true, BotPveGroupRole.Healer),
        Entry("c:5", eRealm.Albion, eCharacterClass.Armsman, "Bors", 41, false, BotPveGroupRole.Tank),
        Entry("c:6", eRealm.Midgard, eCharacterClass.Skald, "Astrun", 5, false, BotPveGroupRole.Buffer),
    };

    private static string[] Layout(IEnumerable<CompanionManagerLine> lines) =>
        lines.Select(line => line.IsHeader ? line.Header : line.Entry.Name).ToArray();

    [Test]
    public void SmartGroupingLeadsWithTheGroupThenBenchedByRealmAndSortsByLevel()
    {
        var session = new CompanionManagerSession();
        string[] layout = Layout(session.Arrange(session.Filter(SampleRoster())));
        Assert.That(layout, Is.EqualTo(new[]
        {
            "[-] In your group (2)", "Brandr", "Dara",
            "[-] On the bench - Albion (3)", "Cora", "Bors", "Alys",
            "[-] On the bench - Midgard (1)", "Astrun",
        }));
    }

    [Test]
    public void EachGroupingAndSortOrdersTheSameRoster()
    {
        var session = new CompanionManagerSession();
        IReadOnlyList<CompanionManagerEntry> roster = session.Filter(SampleRoster());
        session.Group = CompanionManagerGroup.Realm;
        Assert.That(Layout(session.Arrange(roster)), Is.EqualTo(new[]
        {
            "[-] Albion (3)", "Cora", "Bors", "Alys", "[-] Midgard (2)", "Brandr", "Astrun", "[-] Hibernia (1)", "Dara",
        }));
        session.Group = CompanionManagerGroup.Role;
        Assert.That(Layout(session.Arrange(roster)), Is.EqualTo(new[]
        {
            "[-] Tank (2)", "Bors", "Brandr", "[-] Healer (2)", "Cora", "Dara", "[-] Buffer (1)", "Astrun", "[-] Attacker (1)", "Alys",
        }));
        session.Group = CompanionManagerGroup.Level;
        Assert.That(Layout(session.Arrange(roster)), Is.EqualTo(new[]
        {
            "[-] Level 50 (1)", "Cora", "[-] Levels 40-49 (2)", "Bors", "Brandr", "[-] Levels 30-39 (1)", "Dara",
            "[-] Levels 10-19 (1)", "Alys", "[-] Levels 1-9 (1)", "Astrun",
        }), "Highest band first; ties sort by name");
        session.Group = CompanionManagerGroup.None;
        session.Sort = CompanionManagerSort.Name;
        Assert.That(Layout(session.Arrange(roster)), Is.EqualTo(new[] { "Alys", "Astrun", "Bors", "Brandr", "Cora", "Dara" }));
        session.Sort = CompanionManagerSort.Class;
        Assert.That(Layout(session.Arrange(roster)), Is.EqualTo(new[] { "Bors", "Cora", "Dara", "Astrun", "Brandr", "Alys" }),
            "Armsman, Cleric, Druid, Skald, Warrior, Wizard");
    }

    [Test]
    public void ClickingAHeaderFoldsItsSectionAndKeepsTheCount()
    {
        var session = new CompanionManagerSession { RowCount = RowSizes[0] };
        IReadOnlyList<CompanionManagerEntry> roster = session.Filter(SampleRoster());
        session.VisibleRows(session.Arrange(roster));
        Assert.That(session.TrySelectRow(0, out _), Is.False, "A header is never selectable");
        Assert.That(session.TryToggleSection(1), Is.False, "A companion row does not fold anything");
        Assert.That(session.TryToggleSection(3), Is.True);
        string[] folded = Layout(session.VisibleRows(session.Arrange(roster)));
        Assert.That(folded, Is.EqualTo(new[]
        {
            "[-] In your group (2)", "Brandr", "Dara", "[+] On the bench - Albion (3)", "[-] On the bench - Midgard (1)", "Astrun",
        }));
        Assert.That(session.RowKeys[3], Is.EqualTo("h:Roster|bench:1"));
        Assert.That(session.TryToggleSection(3), Is.True);
        Assert.That(Layout(session.Arrange(roster)), Has.Length.EqualTo(9));
    }

    [Test]
    public void ListSizesMapToDetailLinesAndWindowHeights()
    {
        Assert.That(RowSizes, Is.EqualTo(new[] { 16, 22, 28, 34 }));
        Assert.That(RowSizes[^1], Is.EqualTo(Rows));
        Assert.That(Rows - DetailReserve, Is.EqualTo(DetailLines));
        var session = new CompanionManagerSession();
        Assert.That(session.RowCount, Is.EqualTo(22), "The default window is 700 tall");
        Assert.That(WindowHeightFor(session.RowCount), Is.LessThanOrEqualTo(700));
        foreach (int size in RowSizes)
        {
            session.RowCount = size;
            Assert.That(session.DetailCount, Is.EqualTo(size - DetailReserve));
        }
        Assert.That(WindowHeightFor(28), Is.EqualTo(820));
    }

    [Test]
    public void ViewPreferencesRoundTripAndRejectAnythingUnknown()
    {
        string saved = CompanionManagerPreferences.Serialize(CompanionManagerGroup.Role, CompanionManagerSort.Class, 28);
        Assert.That(saved, Is.EqualTo("group=Role;sort=Class;rows=28"));
        Assert.That(CompanionManagerPreferences.TryParse(saved, out var group, out var sort, out int rows), Is.True);
        Assert.That((group, sort, rows), Is.EqualTo((CompanionManagerGroup.Role, CompanionManagerSort.Class, 28)));
        foreach (string bad in new[] { null, "", "group=Role", "group=Role;sort=Class;rows=30", "group=9;sort=Class;rows=28",
                     "group=Role;sort=Level;rows=x", "garbage" })
            Assert.That(CompanionManagerPreferences.TryParse(bad, out _, out _, out _), Is.False, bad);
    }

    [Test]
    public void RecruitCandidatesAreSectionedByRealmWhateverTheGroupingChoice()
    {
        var session = new CompanionManagerSession { Tab = CompanionManagerTab.Recruit, Group = CompanionManagerGroup.Level };
        IReadOnlyList<CompanionManagerEntry> entries = session.Filter(RecruitEntries());
        Assert.That(session.Arrange(entries).Count(line => line.IsHeader), Is.Zero, "Candidates have no level to group by");
        session.Group = CompanionManagerGroup.Smart;
        Assert.That(session.Arrange(entries).Where(line => line.IsHeader).Select(line => line.Header.Split(' ')[1]),
            Is.EqualTo(new[] { "Albion", "Midgard", "Hibernia" }));
    }

    [Test]
    public void RecruitmentCopyMatchesTheAgreedWording()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CompanionManager.StoryCopy, Is.EqualTo("Story companions (authored): Named characters with their own background and personality. You can recruit each one once."));
            Assert.That(CompanionManager.CreateCopy, Is.EqualTo("Create a companion (generated): Choose a class and a new person is created for your roster."));
            Assert.That(CompanionManager.PermanenceCopy, Is.EqualTo("Both are permanent companions who earn XP and keep their training and gear. /spawn helpers are temporary."));
        });
    }

    private static IReadOnlyList<CompanionManagerEntry> RecruitEntries() =>
        (IReadOnlyList<CompanionManagerEntry>)typeof(CompanionManager)
            .GetMethod("RecruitEntries", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { new List<PlayerCompanionRecord>() });

    [Test]
    public void GearTabListsEveryWornSlotInSheetOrderAndMatchesPairedSlots()
    {
        eInventorySlot[] sheet = PersistentCompanionGear.SheetSlots;
        Assert.Multiple(() =>
        {
            Assert.That(sheet, Has.Length.EqualTo(19));
            Assert.That(sheet, Is.Unique);
            Assert.That(sheet[0], Is.EqualTo(eInventorySlot.HeadArmor), "the sheet starts with the helm");
            Assert.That(sheet, Does.Not.Contain(eInventorySlot.FirstQuiver));
            foreach (eInventorySlot slot in sheet)
            {
                Assert.That(slot, Is.InRange(eInventorySlot.MinEquipable, eInventorySlot.MaxEquipable));
                Assert.That(CompanionManager.SlotLabel(slot), Does.Match("^[A-Z]"), slot.ToString());
            }
            foreach ((eInventorySlot left, eInventorySlot right) in new[]
                     {
                         (eInventorySlot.LeftBracer, eInventorySlot.RightBracer),
                         (eInventorySlot.LeftRing, eInventorySlot.RightRing),
                     })
            {
                Assert.That(Array.IndexOf(sheet, right), Is.EqualTo(Array.IndexOf(sheet, left) + 1), $"{right} follows {left}");
                Assert.That(PersistentCompanionGear.FitsSlot(left, right), Is.True, "a paired item fits either side");
                Assert.That(PersistentCompanionGear.FitsSlot(right, left), Is.True);
            }
            Assert.That(PersistentCompanionGear.FitsSlot(eInventorySlot.HeadArmor, eInventorySlot.HeadArmor), Is.True);
            Assert.That(PersistentCompanionGear.FitsSlot(eInventorySlot.RightHandWeapon, eInventorySlot.TwoHandWeapon), Is.False);
            Assert.That(PersistentCompanionGear.FitsSlot(eInventorySlot.LeftRing, eInventorySlot.LeftBracer), Is.False);
            Assert.That(PersistentCompanionGear.FitsSlot(eInventorySlot.Invalid, eInventorySlot.Invalid), Is.False,
                "an unusable item fits nowhere");
        });
    }

    [Test]
    public void GiveAndEquipRejectsUnusableBeforeTransfer()
    {
        Assert.That(PersistentCompanionGear.CanGiveForSlot(eInventorySlot.Invalid, eInventorySlot.HeadArmor, true, out string unusable), Is.False);
        Assert.That(unusable, Does.Contain("cannot use"));
        Assert.That(PersistentCompanionGear.CanGiveForSlot(eInventorySlot.TorsoArmor, eInventorySlot.HeadArmor, true, out string wrongSlot), Is.False);
        Assert.That(wrongSlot, Does.Contain("does not fit"));
        Assert.That(PersistentCompanionGear.CanGiveForSlot(eInventorySlot.HeadArmor, eInventorySlot.HeadArmor, false, out _), Is.False);
        Assert.That(PersistentCompanionGear.CanGiveForSlot(eInventorySlot.LeftRing, eInventorySlot.RightRing, true, out _), Is.True);
    }

    [Test]
    public void InventoryWindowPutsWornSlotsFirstThenBackpack()
    {
        Assert.That(PersistentCompanionInventoryView.WornPosition(eInventorySlot.HeadArmor), Is.EqualTo(1));
        Assert.That(PersistentCompanionInventoryView.WornPosition(eInventorySlot.Mythical), Is.EqualTo(19));
        Assert.That(PersistentCompanionInventoryView.WornPosition(eInventorySlot.RightRing),
            Is.EqualTo(PersistentCompanionInventoryView.WornPosition(eInventorySlot.LeftRing) + 1));
        Assert.That(PersistentCompanionInventoryView.BackpackFirstPosition, Is.EqualTo(21));
        Assert.That(PersistentCompanionInventoryView.BackpackFirstPosition + 39, Is.LessThanOrEqualTo(100));
    }

    [TestCase(PersistentCompanionInventoryView.Area.OwnerBackpack, PersistentCompanionInventoryView.Area.Worn, PersistentCompanionInventoryView.MoveKind.GiveAndEquip)]
    [TestCase(PersistentCompanionInventoryView.Area.Worn, PersistentCompanionInventoryView.Area.OwnerBackpack, PersistentCompanionInventoryView.MoveKind.UnequipToOwner)]
    [TestCase(PersistentCompanionInventoryView.Area.CompanionBackpack, PersistentCompanionInventoryView.Area.Worn, PersistentCompanionInventoryView.MoveKind.Equip)]
    [TestCase(PersistentCompanionInventoryView.Area.Worn, PersistentCompanionInventoryView.Area.CompanionBackpack, PersistentCompanionInventoryView.MoveKind.Unequip)]
    [TestCase(PersistentCompanionInventoryView.Area.Worn, PersistentCompanionInventoryView.Area.Worn, PersistentCompanionInventoryView.MoveKind.Refused)]
    [TestCase(PersistentCompanionInventoryView.Area.CompanionBackpack, PersistentCompanionInventoryView.Area.CompanionBackpack, PersistentCompanionInventoryView.MoveKind.WithinCompanion)]
    [TestCase(PersistentCompanionInventoryView.Area.OwnerBackpack, PersistentCompanionInventoryView.Area.CompanionBackpack, PersistentCompanionInventoryView.MoveKind.GiveToCompanion)]
    [TestCase(PersistentCompanionInventoryView.Area.CompanionBackpack, PersistentCompanionInventoryView.Area.OwnerBackpack, PersistentCompanionInventoryView.MoveKind.ReturnToOwner)]
    [TestCase(PersistentCompanionInventoryView.Area.Other, PersistentCompanionInventoryView.Area.Worn, PersistentCompanionInventoryView.MoveKind.Refused)]
    public void InventoryWindowRoutesEachDrag(PersistentCompanionInventoryView.Area from,
        PersistentCompanionInventoryView.Area to, PersistentCompanionInventoryView.MoveKind expected)
    {
        Assert.That(PersistentCompanionInventoryView.ClassifyMove(from, to), Is.EqualTo(expected));
    }

    [TestCase(PersistentCompanionInventoryView.Area.OwnerBackpack, PersistentCompanionInventoryView.Area.CompanionBackpack)]
    [TestCase(PersistentCompanionInventoryView.Area.CompanionBackpack, PersistentCompanionInventoryView.Area.OwnerBackpack)]
    [TestCase(PersistentCompanionInventoryView.Area.Worn, PersistentCompanionInventoryView.Area.OwnerBackpack)]
    public void ShiftRightClickSendsItemToTheOtherSide(PersistentCompanionInventoryView.Area from,
        PersistentCompanionInventoryView.Area expected)
    {
        Assert.That(PersistentCompanionInventoryView.TargetArea(from, PersistentCompanionInventoryView.Area.Other,
            toGeneralHousing: true), Is.EqualTo(expected));
        Assert.That(PersistentCompanionInventoryView.TargetArea(from, PersistentCompanionInventoryView.Area.Worn,
            toGeneralHousing: false), Is.EqualTo(PersistentCompanionInventoryView.Area.Worn));
    }

    [TestCase(0, 1)]  // the incoming item needs one slot; a replaced item takes its place
    [TestCase(1, 2)]  // a one-hander also pushes out a worn two-hander
    [TestCase(2, 3)]  // a two-hander pushes out both hands
    public void GivingNeedsRoomForTheItemAndEveryDisplacedWeapon(int displacedWeapons, int expected)
    {
        Assert.That(PersistentCompanionGear.FreeSlotsNeededToGive(displacedWeapons), Is.EqualTo(expected));
    }

    [Test]
    public void FailedGiveTellsWhereTheItemIs()
    {
        Assert.That(PersistentCompanionGear.GiveFailedMessage("Axe", "Freya", returned: true, "busy"),
            Does.Contain("came back to you"));
        string stuck = PersistentCompanionGear.GiveFailedMessage("Axe", "Freya", returned: false, "busy");
        Assert.That(stuck, Does.Not.Contain("came back"));
        Assert.That(stuck, Does.Contain("Freya's backpack"));
    }
}
