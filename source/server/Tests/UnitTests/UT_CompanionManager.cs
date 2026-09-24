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
            Assert.That(body[4], Is.EqualTo(LabelCount - 1));
            Assert.That(body.Skip(TextOffset).Take(MaximumTextLength).All(value => value == 'x'), Is.True);
            Assert.That(body[BodySize - 1], Is.Zero);
            Assert.That(Marker, Is.Not.EqualTo(0x52), "Raid marker stays separate");
        });
        Assert.That(() => BuildBody(OpLabel, 256), Throws.TypeOf<ArgumentOutOfRangeException>());
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
        Assert.That(FormatToken(0x0a9f), Is.EqualTo("0a9f"));
        foreach ((string token, string value) in new[]
                 {
                     ("0A9F", "23"), ("0a9", "23"), ("0a9ff", "23"), ("0a9f", "2"), ("0a9f", "C0"),
                     ("0a9f", "c0"), ("0a9f", "ff"), (null, "23"), ("0a9f", null), ("0a9g", "23"), ("0a9f", "-1"),
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
        var kiri = new CompanionManagerEntry("a:kiri", eRealm.Midgard, eCharacterClass.Shaman, "Kiri", "", "");
        Assert.Multiple(() =>
        {
            Assert.That(CompanionManagerSession.Matches(kiri, "kir", null, null), Is.True);
            Assert.That(CompanionManagerSession.Matches(kiri, "SHAM", null, null), Is.True);
            Assert.That(CompanionManagerSession.Matches(kiri, "mid sham", null, null), Is.True);
            Assert.That(CompanionManagerSession.Matches(kiri, "alb sham", null, null), Is.False);
            Assert.That(CompanionManagerSession.Matches(kiri, "", eRealm.Albion, null), Is.False);
            Assert.That(CompanionManagerSession.Matches(kiri, "", eRealm.Midgard, BotPveGroupRole.Healer), Is.True);
            Assert.That(CompanionManagerSession.Matches(kiri, "", null, BotPveGroupRole.Tank), Is.False);
        });
    }

    [Test]
    public void ScrollingReachesEveryStoryCompanionAndGeneratedClass()
    {
        var session = new CompanionManagerSession { Tab = CompanionManagerTab.Recruit };
        IReadOnlyList<CompanionManagerEntry> entries = RecruitEntries();
        IReadOnlyList<CompanionManagerEntry> filtered = session.Filter(entries);
        var seen = new List<string>();
        for (int page = 0; page * Rows < filtered.Count + Rows; page++)
        {
            session.Recruit.Offset = page * Rows;
            session.VisibleRows(filtered);
            seen.AddRange(session.RowKeys.Where(key => key != null && !seen.Contains(key)).ToArray());
        }
        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Count.EqualTo(78 + 39));
            Assert.That(seen, Has.Count.EqualTo(117));
            Assert.That(seen.Count(key => key.StartsWith("a:", StringComparison.Ordinal)), Is.EqualTo(78));
            Assert.That(seen.Count(key => key.StartsWith("g:", StringComparison.Ordinal)), Is.EqualTo(39));
            Assert.That(CompanionCharacterCatalog.All.All(entry => seen.Contains("a:" + entry.Key)), Is.True);
        });

        session.Recruit.Offset = 10_000;
        session.VisibleRows(filtered);
        Assert.That(session.Recruit.Offset, Is.EqualTo(filtered.Count - Rows), "Offsets clamp to the last full page");
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
        var entries = Enumerable.Range(0, 20).Select(index => new CompanionManagerEntry($"c:{index:00}", eRealm.Albion,
            eCharacterClass.Cleric, $"Name {index:00}", "", $"{index:00}")).ToArray();
        IReadOnlyList<CompanionManagerEntry> filtered = session.Filter(entries);
        session.Roster.Offset = 5;
        session.VisibleRows(filtered);
        Assert.That(session.TrySelectRow(0, out string key), Is.True);
        Assert.That(key, Is.EqualTo("c:05"));
        Assert.That(session.TrySelectRow(Rows, out _), Is.False);

        session.Roster.Offset = 15;
        session.VisibleRows(filtered);
        Assert.That(session.RowKeys.Count(value => value != null), Is.EqualTo(Rows));
        Assert.That(session.RowKeys[^1], Is.EqualTo("c:19"));

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
        view.Rows[0] = (true, eRealm.Hibernia, "Aine", "L3 Druid, active");
        view.Rows[1] = (false, eRealm.Albion, "Aldren", "L1 Armsman, benched");
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
            Assert.That(labels[LabelRowBase + 4], Is.EqualTo("L3 Druid, active"));
            Assert.That(labels[LabelRowBase + RowStride], Is.Empty);
            Assert.That(labels[LabelRowBase + RowStride + 1], Is.EqualTo("Aldren"), "Albion colour slot");
            Assert.That(labels[LabelRowBase + 2 * RowStride + 1], Is.Empty, "Unused rows stay blank");
            Assert.That(labels[LabelHeaderBase + 1], Is.EqualTo("Kiri"), "Midgard colour slot");
            Assert.That(labels[LabelDetailBase], Is.EqualTo("plain"));
            Assert.That(labels[LabelDetailBase + 1], Is.Empty);
            Assert.That(labels[LabelDetailBase + 3], Is.EqualTo("link"));
            Assert.That(labels[LabelActionBase], Is.EqualTo("[Invite]"));
            Assert.That(labels[LabelActionBase + 3], Is.EqualTo("[Open bag]"), "Disabled actions use the grey label");
            Assert.That(labels[LabelActionBase + 2], Is.Empty);
            Assert.That(labels.All(label => label.Length <= MaximumTextLength), Is.True);
            Assert.That(TextWidth(labels[LabelRowBase + 4]), Is.LessThanOrEqualTo(WidthRowInfo));
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
        Assert.That(TextWidth("abc"), Is.EqualTo(6 + 7 + 6));
        Assert.That(Fit("L50 Spiritmaster, benched", WidthRowInfo), Is.EqualTo("L50 Spiritmaster, benched"));
        string fitted = Fit(new string('m', 40), WidthAction);
        Assert.That(fitted, Does.EndWith("..."));
        Assert.That(TextWidth(fitted), Is.LessThanOrEqualTo(WidthAction));
        Assert.That(TextWidth("[Open in roster]"), Is.LessThanOrEqualTo(WidthAction));
        Assert.That(TextWidth("[Return: blocked]"), Is.LessThanOrEqualTo(WidthAction));
        Assert.That(TextWidth("Training & Tactics"), Is.LessThanOrEqualTo(132));
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
}
