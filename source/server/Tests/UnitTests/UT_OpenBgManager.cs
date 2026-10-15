using System;
using System.Linq;
using DOL.GS.Commands;
using NUnit.Framework;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.GS.Tests
{
    // The open BG window (owner decision 2026-10-11 14:50): the Companion Manager's Custom8 window drawn with the live
    // crew per realm, + and - units per realm, hand reliefs and the setup rows; one window per client.
    [TestFixture]
    public class UT_OpenBgManager
    {
        private static readonly OpenBgSetupValues Defaults = new(true, 1, 2, 4, 72, 15, 45, true);

        private static readonly OpenBgBotStatus[] Crew =
        {
            new("Albion", "Aldwyn", 50, "Cleric", 3, "group8", "Aldwyn", "inside", true, "assault", true, 720),
            new("Albion", "Brannoc", 47, "Armsman", 3, "group8", "Aldwyn", "inside", false, "assault", true, 700),
            new("Albion", "Cerys", 44, "Infiltrator", 4, "solo", null, "travelling", true),
            new("Midgard", "Hrolf", 50, "Healer", 7, "small", "Hrolf", "inside", true, "bridge_watch", false, 60),
            new("Hibernia", "Aine", 48, "Ranger", 9, "solo", null, "leaving", true),
        };

        private static OpenBgStatusSnapshot Snapshot(OpenBgBotStatus[] bots = null, OpenBgRealmStatus[] realms = null) =>
            new(new DateTime(2026, 10, 11, 3, 0, 0, DateTimeKind.Utc), true, true, true, true, null, "Push", "raid 1 units/2 bots, lead Ashen Concord",
                200, "Thidranki Faste: Midgard (Northmen)", 19.6, 20, 72, new[] { new OpenBgPlayerStatus("Albion", "Ked", 50) },
                bots ?? Crew, new[] { new OpenBgGuestStatus("Hibernia", 22, 4, 3, "Ked") }, realms);

        private static CompanionManagerView Build(OpenBgManagerSession session, OpenBgStatusSnapshot status = null, OpenBgSetupValues values = null,
            Func<int, bool> pending = null) =>
            OpenBgManagerScreen.Build(session, status ?? Snapshot(), values ?? Defaults, eRealm.Albion, pending, out _);

        // Detail text with the wrapped lines joined again.
        private static string Details(CompanionManagerView view) => string.Join(" ", view.Details.Select(detail => detail.Text ?? string.Empty));

        [Test]
        public void LiveTabNamesItsTogglesAndHidesTheCompanionOnes()
        {
            var session = new OpenBgManagerSession();
            CompanionManagerView view = Build(session);
            string[] labels = view.ToLabels();
            Assert.Multiple(() =>
            {
                Assert.That(view.Toggles[ToggleTabRoster], Is.EqualTo(("Live (3)", true)));
                Assert.That(view.Toggles[ToggleTabRecruit], Is.EqualTo(("Setup", false)));
                Assert.That(view.Toggles[ToggleTabActive].Text, Is.Null, "the third tab is hidden; guests are part of Live");
                Assert.That(view.Toggles[ToggleRealmAll], Is.EqualTo(("All realms", true)));
                Assert.That(view.Toggles[ToggleRealmHibernia], Is.EqualTo(("Hibernia", false)));
                for (int toggle = ToggleRoleAny; toggle <= ToggleSortClass; toggle++)
                    Assert.That(view.Toggles[toggle].Text, Is.Null, $"toggle {toggle} hidden");
                Assert.That(view.Toggles[ToggleRows22], Is.EqualTo(("22", true)), "row sizes work like the Companion Manager's");
                Assert.That(labels[LabelToggleBase + 2 * ToggleTabActive], Is.Empty);
                Assert.That(labels[LabelToggleBase + 2 * ToggleTabActive + 1], Is.Empty);
                Assert.That(labels[LabelToggleBase + 2 * ToggleDetailGear], Is.Empty);
                Assert.That(view.Status, Does.Contain("Open BG, Thidranki: on and ready | Push 3m 20s left | 3 bots inside, cap 72"));
            });
        }

        [Test]
        public void LiveRowsAreGroupedByRealmAndUnit()
        {
            var session = new OpenBgManagerSession { RowCount = 34 };
            CompanionManagerView view = Build(session);
            Assert.Multiple(() =>
            {
                Assert.That(view.Rows[0].Header, Does.StartWith("Albion: 8-man 1/1, small 0/2, solo 1/4, 3 bots"));
                Assert.That(session.RowKeys[0], Is.EqualTo("r:Albion"));
                Assert.That(view.Rows[1], Is.EqualTo(new CompanionManagerView.RowView(null, true, eRealm.Albion, "Aldwyn", "50", "Cleric", "8-man #3",
                    "assault, raid", true)), "the leader first, the first unit selected");
                Assert.That(view.Rows[2], Is.EqualTo(new CompanionManagerView.RowView(null, true, eRealm.Albion, "Brannoc", "47", "Armsman", "8-man #3",
                    "dead", false)), "every row of the selected unit is marked");
                Assert.That(view.Rows[3], Is.EqualTo(new CompanionManagerView.RowView(null, false, eRealm.Albion, "Cerys", "44", "Infiltrator", "solo #4",
                    "travelling", false)));
                Assert.That(session.RowKeys[1], Is.EqualTo("u:3"));
                Assert.That(session.RowKeys[2], Is.EqualTo("u:3"));
                Assert.That(view.Rows[4].Header, Does.StartWith("Midgard: 8-man 0/1, small 1/2"));
                Assert.That(view.Rows[5].State, Is.EqualTo("bridge watch"));
                Assert.That(view.Rows[5].Origin, Is.EqualTo("small #7"));
                Assert.That(view.Rows[6].Header, Does.StartWith("Hibernia:"));
                Assert.That(view.Rows[7].State, Is.EqualTo("leaving"));
                Assert.That(view.Rows[8].Header, Is.EqualTo("Guest squads (1)"));
                Assert.That(view.Rows[9], Is.EqualTo(new CompanionManagerView.RowView(null, false, eRealm.Hibernia, "Hibernia squad", "22", "3/4 alive",
                    "guest", "near Ked", true)));
                Assert.That(view.Rows[10].Header, Is.EqualTo("Players inside (1)"));
                Assert.That(view.Rows[11].Name, Is.EqualTo("Ked"));
                Assert.That(view.ListIndicator, Is.EqualTo("1-12 of 12"));
            });
        }

        [Test]
        public void TheRealmFilterAndSearchNarrowTheList()
        {
            var session = new OpenBgManagerSession { Realm = eRealm.Midgard };
            CompanionManagerView view = Build(session);
            Assert.Multiple(() =>
            {
                Assert.That(view.Rows[0].Header, Does.StartWith("Midgard:"));
                Assert.That(view.Rows[1].Name, Is.EqualTo("Hrolf"));
                Assert.That(view.Rows[2], Is.Not.Null.And.Property("Header").EqualTo("Players inside (1)"), "no Hibernia guests under the Midgard filter");
                Assert.That(view.ActionLabels[0].Text, Is.EqualTo("+ 8-man Midgard"));
            });
            session = new OpenBgManagerSession { Query = "infil" };
            view = Build(session);
            Assert.That(view.Rows.Where(row => row?.Name != null).Select(row => row.Name), Is.EqualTo(new[] { "Cerys" }));
        }

        [Test]
        public void TheSelectedUnitsDetailShowsTheTopBlockMembersAndAReliefLink()
        {
            OpenBgRealmStatus[] realms = OpenBattlegroundStatus.RealmStatuses(Defaults.ToSetup(), Crew,
                (realm, kind) => realm == eRealm.Albion && kind == OpenBgUnitKind.Group8 ? ("needs 6, 2 RvR candidates in the pool; far from a porter 4", 0L) : null,
                300_000);
            var session = new OpenBgManagerSession { RowCount = 34 };
            CompanionManagerView view = Build(session, Snapshot(realms: realms));
            string details = Details(view);
            Assert.Multiple(() =>
            {
                Assert.That(view.Header, Is.EqualTo("8-man #3, Albion"));
                Assert.That(view.Subheader, Is.EqualTo("Leader Aldwyn | assault | keep raid"));
                Assert.That(details, Does.Contain("Open BG: on and ready."));
                Assert.That(details, Does.Contain("Phase: Push, 3m 20s left. Raid: raid 1 units/2 bots, lead Ashen Concord."));
                Assert.That(details, Does.Contain("Keep 11: Thidranki Faste: Midgard (Northmen)."));
                Assert.That(details, Does.Contain("World speed: 20x selected, 19.6x achieved."));
                Assert.That(details, Does.Contain("Players inside: Ked 50 Albion."));
                Assert.That(details, Does.Contain("Crew: 3 inside, 1 travelling, 1 leaving; cap 72."));
                Assert.That(details, Does.Contain("Albion 8-man group #3: 2 members, 1 alive."));
                Assert.That(details, Does.Contain("Inside, role assault, part of this cycle's keep raid; inside for 12 game min."));
                Assert.That(details, Does.Contain("  Aldwyn, 50 Cleric, leader"));
                Assert.That(details, Does.Contain("  Brannoc, 47 Armsman, dead"));
                Assert.That(details, Does.Contain("Last form failure (8-man group, 5 game min ago): needs 6"));
            });
            int link = Array.FindIndex(view.Details, detail => detail.Text == "[Relieve this unit now]");
            Assert.That(link, Is.GreaterThanOrEqualTo(0));
            Assert.Multiple(() =>
            {
                Assert.That(view.Details[link].Link, Is.True);
                Assert.That(session.DetailIntents[link], Is.EqualTo(new OpenBgIntent(OpenBgIntentKind.Relieve,
                    "Unit #3 leaves within a minute; its bots may return after the short cooldown.", Unit: 3)));
            });

            // A requested relief replaces the link with a note.
            view = Build(session, Snapshot(realms: realms), pending: unit => unit == 3);
            Assert.That(Details(view), Does.Contain("Relief requested"));
            Assert.That(view.Details.Any(detail => detail.Link), Is.False);
        }

        [Test]
        public void PlusAndMinusWriteTheRealmsOwnSetup()
        {
            var session = new OpenBgManagerSession();
            CompanionManagerView view = Build(session);
            Assert.Multiple(() =>
            {
                Assert.That(view.ActionLabels.Select(action => action.Text), Is.EqualTo(new[]
                {
                    "+ 8-man Albion", "- 8-man Albion", "+ small Albion", "- small Albion", "+ solo Albion", "- solo Albion",
                }));
                Assert.That(view.ActionLabels.All(action => action.Enabled));
                Assert.That(session.ActionIntents[0], Is.EqualTo(new OpenBgIntent(OpenBgIntentKind.Write,
                    "Albion: 8-man 1 -> 2. The roster forms it within a minute if the 40-50 pool allows.", "open_bg_units_albion", "2/2/4")));
                Assert.That(session.ActionIntents[3].Value, Is.EqualTo("1/1/4"));
                Assert.That(session.ActionIntents[3].Message, Is.EqualTo("Albion: small 2 -> 1. The roster relieves the surplus unit within a minute."));
                Assert.That(session.ActionIntents[5].Value, Is.EqualTo("1/2/3"));
            });

            // At a bound the button is greyed and only explains; above the cap the write still happens with a warning.
            view = Build(session, values: Defaults with { AlbionUnits = "4/0/0", MaxBots = 40 });
            Assert.Multiple(() =>
            {
                Assert.That(view.ActionLabels[0].Enabled, Is.False);
                Assert.That(session.ActionIntents[0], Is.EqualTo(new OpenBgIntent(OpenBgIntentKind.Message, "Albion already has the most 8-man units (4).")));
                Assert.That(view.ActionLabels[3].Enabled, Is.False);
                Assert.That(session.ActionIntents[3].Message, Is.EqualTo("Albion has no small unit to remove."));
                Assert.That(session.ActionIntents[4].Value, Is.EqualTo("4/0/1"));
                Assert.That(session.ActionIntents[4].Message, Does.EndWith("The bot cap of 40 binds: the setup wants at least 65 bots."));
            });
        }

        [Test]
        public void WithoutARealmTheButtonsOnlyAskForOne()
        {
            var session = new OpenBgManagerSession { Tab = OpenBgManagerTab.Live, LiveSelected = OpenBgManagerScreen.PlayersKey };
            CompanionManagerView view = Build(session);
            Assert.Multiple(() =>
            {
                Assert.That(view.ActionLabels[0], Is.EqualTo(("+ 8-man", false)));
                Assert.That(session.ActionIntents[0].Kind, Is.EqualTo(OpenBgIntentKind.Message));
                Assert.That(Details(view), Does.Contain("Ked, level 50, Albion."));
            });
        }

        [Test]
        public void ARealmLineShowsItsTargetsAndCanGoBackToTheGlobalSetup()
        {
            var session = new OpenBgManagerSession { LiveSelected = "r:Midgard" };
            CompanionManagerView view = Build(session, values: Defaults with { MidgardUnits = "2/1/0" });
            string details = Details(view);
            Assert.Multiple(() =>
            {
                Assert.That(view.Header, Is.EqualTo("Midgard crew"));
                Assert.That(view.HeaderRealm, Is.EqualTo(eRealm.Midgard));
                Assert.That(view.Rows.Where(row => row?.Header != null).Select(row => row.Header),
                    Has.Some.EqualTo("> Midgard: 8-man 0/2, small 1/1, solo 0/0, 1 bots (own setup)"), "the selected realm line is marked");
                Assert.That(details, Does.Contain("Targets: 8-man 2, small 1, solo 0 (own setup; the global setup is 1/2/4)."));
                Assert.That(view.ActionLabels[0].Text, Is.EqualTo("+ 8-man Midgard"));
            });
            int link = Array.FindIndex(view.Details, detail => detail.Text == "[Use the global setup for Midgard]");
            Assert.That(link, Is.GreaterThanOrEqualTo(0));
            Assert.That(session.DetailIntents[link], Is.EqualTo(new OpenBgIntent(OpenBgIntentKind.Write,
                "Midgard uses the global setup again (1/2/4).", "open_bg_units_midgard", string.Empty)));
        }

        [Test]
        public void SetupTabListsTheNineRealmTargetsAndTheGlobalRows()
        {
            var session = new OpenBgManagerSession { Tab = OpenBgManagerTab.Setup, RowCount = 34 };
            CompanionManagerView view = Build(session, values: Defaults with { HiberniaUnits = "0/0/6" });
            Assert.Multiple(() =>
            {
                Assert.That(view.Toggles[ToggleTabRecruit], Is.EqualTo(("Setup", true)));
                Assert.That(view.Rows[0].Header, Does.StartWith("Crew per realm"));
                Assert.That(view.Rows[1], Is.EqualTo(new CompanionManagerView.RowView(null, true, eRealm.Albion, "Albion 8-man", "1", "present 1",
                    "global", "full", true)));
                Assert.That(view.Rows[2], Is.EqualTo(new CompanionManagerView.RowView(null, false, eRealm.Albion, "Albion small", "2", "present 0",
                    "global", "short 2", false)));
                Assert.That(view.Rows[7], Is.EqualTo(new CompanionManagerView.RowView(null, false, eRealm.Hibernia, "Hibernia 8-man", "0", "present 0",
                    "own", "full", true)));
                Assert.That(view.Rows[9].Name, Is.EqualTo("Hibernia solo"));
                Assert.That(view.Rows[9].Level, Is.EqualTo("6"));
                Assert.That(view.Rows[10].Header, Is.EqualTo("Whole open BG"));
                Assert.That(view.Rows.Skip(11).Take(8).Select(row => row.Name), Is.EqualTo(new[]
                {
                    "Open BG on", "Bot cap", "8-man per realm", "Small per realm", "Solos per realm", "Shortest cycle", "Longest cycle", "Player drift",
                }));
                Assert.That(view.Rows[11].Level, Is.EqualTo("on"));
                Assert.That(session.RowKeys[1], Is.EqualTo(OpenBgManagerScreen.TargetKey(eRealm.Albion, OpenBgUnitKind.Group8)));
            });

            // The selected target row offers its steps; the selected Albion target makes the buttons act on Albion.
            string details = Details(view);
            Assert.That(details, Does.Contain("Albion uses the global setup 1/2/4"));
            int raise = Array.FindIndex(view.Details, detail => detail.Text == "[Raise to 2]");
            Assert.That(session.DetailIntents[raise].RowKey, Is.EqualTo("open_bg_units_albion"));
            Assert.That(session.DetailIntents[raise].Value, Is.EqualTo("2/2/4"));
            Assert.That(view.ActionLabels[0].Text, Is.EqualTo("+ 8-man Albion"));
        }

        [Test]
        public void SetupRowsOfferStepsAndSwitches()
        {
            var session = new OpenBgManagerSession { Tab = OpenBgManagerTab.Setup, SetupSelected = "k:max_bots" };
            CompanionManagerView view = Build(session);
            string[] links = view.Details.Where(detail => detail.Link).Select(detail => detail.Text).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(view.Header, Is.EqualTo("Bot cap"));
                Assert.That(links, Is.EqualTo(new[] { "[Raise to 73]", "[Raise to 77]", "[Lower to 71]", "[Lower to 67]" }));
                Assert.That(session.DetailIntents.Where(intent => intent != null).Select(intent => intent.Key),
                    Is.EqualTo(new[] { "w:open_bg_max_bots=73", "w:open_bg_max_bots=77", "w:open_bg_max_bots=71", "w:open_bg_max_bots=67" }));
            });

            session.SetupSelected = "k:enabled";
            view = Build(session);
            int link = Array.FindIndex(view.Details, detail => detail.Link);
            Assert.That(view.Details[link].Text, Is.EqualTo("[Turn off]"));
            Assert.That(session.DetailIntents[link].Key, Is.EqualTo("w:open_bg_enabled=False"));

            // The cycle bounds never cross: at min = max only the steps apart are offered.
            session.SetupSelected = "k:cycle_min";
            view = Build(session, values: Defaults with { CycleMin = 45, CycleMax = 45 });
            Assert.That(view.Details.Where(detail => detail.Link).Select(detail => detail.Text), Is.EqualTo(new[] { "[Lower to 44]", "[Lower to 40]" }));
        }

        [Test]
        public void TitlesAreNotSelectableAndTheSignatureTracksClickMeaning()
        {
            var session = new OpenBgManagerSession { Tab = OpenBgManagerTab.Setup };
            OpenBgManagerScreen.Build(session, Snapshot(), Defaults, eRealm.Albion, null, out string first);
            Assert.That(session.TrySelectRow(0), Is.False, "the title row");
            OpenBgManagerScreen.Build(session, Snapshot(), Defaults, eRealm.Albion, null, out string again);
            Assert.That(again, Is.EqualTo(first), "an unchanged view keeps its signature");
            Assert.That(session.TrySelectRow(2), Is.True);
            OpenBgManagerScreen.Build(session, Snapshot(), Defaults, eRealm.Albion, null, out string selected);
            Assert.That(selected, Is.Not.EqualTo(first));
            Assert.That(session.CommitView(selected), Is.True);
            Assert.That(session.CommitView(selected), Is.False);
            Assert.That(session.IsStale(session.Revision, DateTime.UtcNow), Is.True, "never used");
            session.LastUseUtc = DateTime.UtcNow;
            Assert.That(session.IsStale(session.Revision, DateTime.UtcNow), Is.False);
            Assert.That(session.IsStale((ushort)(session.Revision + 1), DateTime.UtcNow), Is.True);
        }

        [Test]
        public void ASelectedUnitThatLeftFallsBackToTheFirstUnit()
        {
            var session = new OpenBgManagerSession { LiveSelected = "u:99" };
            CompanionManagerView view = Build(session);
            Assert.That(session.LiveSelected, Is.EqualTo("u:3"));
            Assert.That(view.Header, Is.EqualTo("8-man #3, Albion"));
        }

        [Test]
        public void CompanionsAndGearWindowClicksStayIndependentOfCustom8Ownership()
        {
            // COMPANIONS tiles (0xA8-0xBF) and the gear ready control (0xDC) are not Custom8 clicks: they must reach
            // the Companion Manager's handler even while the open BG manager owns the window.
            Assert.That(CompanionManager.IsIndependentWindowControl("0001", "a8"), Is.True, "first COMPANIONS tile");
            Assert.That(CompanionManager.IsIndependentWindowControl("0001", "bf"), Is.True, "COMPANIONS ready");
            Assert.That(CompanionManager.IsIndependentWindowControl("0001", "dc"), Is.True, "gear ready");
            Assert.That(CompanionManager.IsIndependentWindowControl("0001", "dd"), Is.False, "the window's own ready stays with its owner");
            Assert.That(CompanionManager.IsIndependentWindowControl("0001", "00"), Is.False, "a tab click stays with its owner");
            Assert.That(CompanionManager.IsIndependentWindowControl("zz", "a8"), Is.False, "a malformed token is not routed");
        }

        [Test]
        public void OneWindowPerClientOpeningOneClosesTheOther()
        {
            object player = new();
            var closed = new System.Collections.Generic.List<CompanionWindowKind>();
            Assert.That(CompanionWindowOwner.Current(player), Is.EqualTo(CompanionWindowKind.None));
            Assert.That(CompanionWindowOwner.Claim(player, CompanionWindowKind.Companions, closed.Add), Is.EqualTo(CompanionWindowKind.None));
            Assert.That(closed, Is.Empty);
            Assert.That(CompanionWindowOwner.RoutesToOpenBg(player), Is.False, "clicks go to the Companion Manager");
            Assert.That(CompanionWindowOwner.Claim(player, CompanionWindowKind.OpenBg, closed.Add), Is.EqualTo(CompanionWindowKind.Companions));
            Assert.That(closed, Is.EqualTo(new[] { CompanionWindowKind.Companions }));
            Assert.That(CompanionWindowOwner.RoutesToOpenBg(player), Is.True, "clicks go to the open BG manager");
            // Reopening the same manager closes nothing; a release by the former owner changes nothing.
            CompanionWindowOwner.Claim(player, CompanionWindowKind.OpenBg, closed.Add);
            CompanionWindowOwner.Release(player, CompanionWindowKind.Companions);
            Assert.That(closed, Has.Count.EqualTo(1));
            Assert.That(CompanionWindowOwner.Current(player), Is.EqualTo(CompanionWindowKind.OpenBg));
            CompanionWindowOwner.Claim(player, CompanionWindowKind.Companions, closed.Add);
            Assert.That(closed, Is.EqualTo(new[] { CompanionWindowKind.Companions, CompanionWindowKind.OpenBg }));
            CompanionWindowOwner.Release(player, CompanionWindowKind.Companions);
            Assert.That(CompanionWindowOwner.Current(player), Is.EqualTo(CompanionWindowKind.None));
            Assert.That(CompanionWindowOwner.RoutesToOpenBg(new object()), Is.False, "an unknown player keeps the Companion Manager's fallback");
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
            DateTime now = new(2026, 10, 11, 15, 0, 0, DateTimeKind.Utc);
            Assert.Multiple(() =>
            {
                Assert.That(OpenBgManager.KeepsRefreshing(true, true, now.AddMinutes(-29), now), Is.True);
                Assert.That(OpenBgManager.KeepsRefreshing(true, true, now.AddMinutes(-31), now), Is.False, "idle half an hour");
                Assert.That(OpenBgManager.KeepsRefreshing(false, true, now, now), Is.False, "closed or taken over");
                Assert.That(OpenBgManager.KeepsRefreshing(true, false, now, now), Is.False, "logged out");
                Assert.That(OpenBgManager.RefreshWallMilliseconds, Is.EqualTo(15_000));
            });
        }
    }
}
