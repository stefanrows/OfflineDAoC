using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.GS.Commands
{
    /// <summary>The open BG window's two tabs: the live crew, and the setup rows.</summary>
    public enum OpenBgManagerTab { Live, Setup }

    public enum OpenBgIntentKind { Write, Relieve, Message }

    /// <summary>
    /// What a click on a detail link or an action button does, decided when the window is drawn: write a setup row
    /// (<paramref name="RowKey"/> = <paramref name="Value"/>), relieve <paramref name="Unit"/>, or only show
    /// <paramref name="Message"/>. The message is what the player reads after a write or relief.
    /// </summary>
    public sealed record OpenBgIntent(OpenBgIntentKind Kind, string Message, string RowKey = null, string Value = null, int Unit = 0)
    {
        /// <summary>The identity of the click for the view signature.</summary>
        public string Key => Kind switch
        {
            OpenBgIntentKind.Write => $"w:{RowKey}={Value}",
            OpenBgIntentKind.Relieve => $"r:{Unit}",
            _ => "m:" + Message,
        };
    }

    /// <summary>Server-held state of one player's open BG window (the Companion Manager's Custom8 window).</summary>
    public sealed class OpenBgManagerSession : ICompanionWindowState
    {
        public static readonly TimeSpan IdleLimit = CompanionManagerSession.IdleLimit;

        public OpenBgManagerTab Tab { get; set; }
        /// <summary>The realm filter (null: all realms). It also picks the realm the action buttons change when no row is selected.</summary>
        public eRealm? Realm { get; set; }
        public int LiveOffset { get; set; }
        public int SetupOffset { get; set; }
        public string LiveSelected { get; set; }
        public string SetupSelected { get; set; }
        public int DetailOffset { get; set; }
        public int RowCount { get; set; } = RowSizes[1];
        public string Query { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public ushort Revision { get; private set; } = 1;
        public string Signature { get; private set; } = string.Empty;
        public DateTime LastUseUtc { get; set; }
        public bool ClientConfirmed { get; set; }
        public bool GuidancePending { get; set; }
        public string[] RowKeys { get; } = new string[Rows];
        public string[] SentLabels { get; } = new string[LabelCount];
        public OpenBgIntent[] DetailIntents { get; } = new OpenBgIntent[DetailLines];
        public OpenBgIntent[] ActionIntents { get; } = new OpenBgIntent[Actions];
        internal ECSGameTimer RefreshTimer { get; set; }

        public int DetailCount => Math.Clamp(RowCount - DetailReserve, 1, DetailLines);

        public int Offset
        {
            get => Tab == OpenBgManagerTab.Live ? LiveOffset : SetupOffset;
            set
            {
                if (Tab == OpenBgManagerTab.Live) LiveOffset = value;
                else SetupOffset = value;
            }
        }

        public string SelectedKey
        {
            get => Tab == OpenBgManagerTab.Live ? LiveSelected : SetupSelected;
            set
            {
                if (Tab == OpenBgManagerTab.Live) LiveSelected = value;
                else SetupSelected = value;
            }
        }

        /// <summary>A row click selects what the row stands for; section titles without a detail do nothing.</summary>
        public bool TrySelectRow(int row)
        {
            string key = row is >= 0 and < Rows ? RowKeys[row] : null;
            if (key == null || key.StartsWith(OpenBgManagerScreen.TitlePrefix, StringComparison.Ordinal))
                return false;
            if (SelectedKey != key)
            {
                SelectedKey = key;
                DetailOffset = 0;
            }
            return true;
        }

        /// <summary>A new revision is issued only when a click could now resolve differently.</summary>
        public bool CommitView(string signature)
        {
            if (string.Equals(signature, Signature, StringComparison.Ordinal))
                return false;
            Signature = signature;
            Revision = (ushort)(Revision == ushort.MaxValue ? 1 : Revision + 1);
            return true;
        }

        /// <summary>A click on an older view or after half an hour without use is not applied.</summary>
        public bool IsStale(ushort token, DateTime utcNow) => token != Revision || utcNow - LastUseUtc > IdleLimit;
    }

    /// <summary>
    /// Pure drawing of the open BG window from a status snapshot and the setup rows: list rows, detail lines, the six
    /// action buttons and the toggles' texts, and the intent behind every clickable line. The runtime
    /// (<see cref="OpenBgManager"/>) captures the snapshot, publishes the labels and applies the intents.
    /// </summary>
    public static class OpenBgManagerScreen
    {
        public const string TitlePrefix = "h:";
        public const string RealmPrefix = "r:";
        public const string UnitPrefix = "u:";
        public const string TargetPrefix = "t:";
        public const string SettingPrefix = "k:";
        public const string GuestsKey = "s:guests";
        public const string PlayersKey = "s:players";
        private const int DetailWidth = WidthDetail - 6;
        private static readonly eRealm[] RealmOrder = { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };
        private static readonly OpenBgUnitKind[] Kinds = { OpenBgUnitKind.Group8, OpenBgUnitKind.Small, OpenBgUnitKind.Solo };
        private static readonly string[] GlobalSettings = { "enabled", "max_bots", "groups8", "small", "solos", "cycle_min", "cycle_max", "drift" };

        private sealed record Row(string Key, string Header = null, eRealm Realm = eRealm.None, string Name = null, string Level = null,
            string Class = null, string Type = null, string State = null, bool StateActive = false)
        {
            public bool IsHeader => Header != null;
        }

        private sealed record Line(string Text, OpenBgIntent Intent = null);

        /// <summary>
        /// Draws the window for <paramref name="session"/> and records each row's key and each link's and button's
        /// intent on it. <paramref name="homeRealm"/> colours the rows of the whole open BG; <paramref name="reliefPending"/>
        /// tells whether a hand relief of a unit waits (null: none). <paramref name="signature"/> changes whenever a click
        /// could resolve differently.
        /// </summary>
        public static CompanionManagerView Build(OpenBgManagerSession session, OpenBgStatusSnapshot status, OpenBgSetupValues values,
            eRealm homeRealm, Func<int, bool> reliefPending, out string signature)
        {
            var view = new CompanionManagerView();
            OpenBgBotStatus[] bots = status?.Bots ?? Array.Empty<OpenBgBotStatus>();
            OpenBgSetup setup = values.ToSetup();
            OpenBgRealmStatus[] realms = status?.Realms ?? OpenBattlegroundStatus.RealmStatuses(setup, bots, null, 0);
            bool live = session.Tab == OpenBgManagerTab.Live;
            IReadOnlyList<Row> rows = live ? LiveRows(session, status, bots, realms) : SetupRows(session, values, setup, realms, homeRealm);

            // The selection survives a refresh while its row exists; else the first selectable row.
            if (session.SelectedKey == null || rows.All(row => row.Key != session.SelectedKey))
            {
                session.SelectedKey = rows.FirstOrDefault(row => row.Key.StartsWith(UnitPrefix, StringComparison.Ordinal) ||
                    row.Key.StartsWith(TargetPrefix, StringComparison.Ordinal))?.Key ??
                    rows.FirstOrDefault(row => !row.Key.StartsWith(TitlePrefix, StringComparison.Ordinal))?.Key;
                session.DetailOffset = 0;
            }
            session.Offset = Math.Clamp(session.Offset, 0, Math.Max(0, rows.Count - session.RowCount));
            Row[] visible = rows.Skip(session.Offset).Take(session.RowCount).ToArray();
            for (int index = 0; index < Rows; index++)
            {
                session.RowKeys[index] = index < visible.Length ? visible[index].Key : null;
                if (index >= visible.Length) continue;
                Row row = visible[index];
                bool selected = row.Key == session.SelectedKey;
                view.Rows[index] = row.IsHeader
                    ? new CompanionManagerView.RowView((selected ? "> " : string.Empty) + row.Header, false, eRealm.None, null, null, null, null, null, false)
                    : new CompanionManagerView.RowView(null, selected, row.Realm, row.Name, row.Level, row.Class, row.Type, row.State, row.StateActive);
            }

            view.Toggles[ToggleTabRoster] = ($"Live ({bots.Count(bot => bot.State == "inside")})", live);
            view.Toggles[ToggleTabRecruit] = ("Setup", !live);
            view.Toggles[ToggleTabActive] = (null, false);
            view.Toggles[ToggleDetailOverview] = (null, false);
            view.Toggles[ToggleDetailTraining] = (null, false);
            view.Toggles[ToggleDetailGear] = (null, false);
            view.Toggles[ToggleRealmAll] = ("All realms", session.Realm == null);
            view.Toggles[ToggleRealmAlbion] = ("Albion", session.Realm == eRealm.Albion);
            view.Toggles[ToggleRealmMidgard] = ("Midgard", session.Realm == eRealm.Midgard);
            view.Toggles[ToggleRealmHibernia] = ("Hibernia", session.Realm == eRealm.Hibernia);
            for (int toggle = ToggleRoleAny; toggle <= ToggleSortClass; toggle++)
                view.Toggles[toggle] = (null, false);
            for (int size = 0; size < RowSizes.Length; size++)
                view.Toggles[ToggleRows16 + size] = (RowSizes[size].ToString(CultureInfo.InvariantCulture), session.RowCount == RowSizes[size]);

            var lines = new List<Line>();
            if (live) AddTopBlock(lines, status, bots);
            eRealm actionRealm = AddSelectionDetail(view, lines, session, status, values, setup, bots, realms, reliefPending, homeRealm);
            if (actionRealm == eRealm.None && session.Realm is eRealm filtered) actionRealm = filtered;

            int detailCount = session.DetailCount;
            session.DetailOffset = Math.Clamp(session.DetailOffset, 0, Math.Max(0, lines.Count - detailCount));
            Array.Clear(session.DetailIntents);
            Array.Clear(session.ActionIntents);
            var keys = new List<string>(session.RowKeys.Select(key => key ?? string.Empty))
            {
                session.Tab.ToString(), session.SelectedKey ?? string.Empty, detailCount.ToString(CultureInfo.InvariantCulture),
            };
            for (int line = 0; line < detailCount; line++)
            {
                int index = session.DetailOffset + line;
                if (index >= lines.Count)
                {
                    keys.Add(string.Empty);
                    continue;
                }
                view.Details[line] = (lines[index].Text, lines[index].Intent != null);
                session.DetailIntents[line] = lines[index].Intent;
                keys.Add(lines[index].Intent?.Key ?? string.Empty);
            }
            (string Text, bool Enabled, OpenBgIntent Intent)[] actions = ActionButtons(values, setup, actionRealm);
            for (int action = 0; action < Actions; action++)
            {
                view.ActionLabels[action] = (actions[action].Text, actions[action].Enabled);
                session.ActionIntents[action] = actions[action].Intent;
                keys.Add(actions[action].Intent.Key);
            }
            view.DetailIndicator = lines.Count > detailCount
                ? $"Lines {session.DetailOffset + 1}-{Math.Min(lines.Count, session.DetailOffset + detailCount)} of {lines.Count}"
                : string.Empty;
            view.DetailCanScrollUp = session.DetailOffset > 0;
            view.DetailCanScrollDown = session.DetailOffset + detailCount < lines.Count;
            view.ListIndicator = rows.Count == 0 ? "Nothing to show" : $"{session.Offset + 1}-{session.Offset + visible.Length} of {rows.Count}";
            view.Status = StatusLine(status, bots, session.Query);
            view.Message = session.Message;
            signature = string.Join('|', keys);
            return view;
        }

        // ---- list rows -------------------------------------------------------------------------------------------

        private static IReadOnlyList<Row> LiveRows(OpenBgManagerSession session, OpenBgStatusSnapshot status, OpenBgBotStatus[] bots,
            OpenBgRealmStatus[] realms)
        {
            var rows = new List<Row>();
            foreach (eRealm realm in RealmOrder.Where(realm => session.Realm == null || session.Realm == realm))
            {
                string name = OpenBattlegroundRoster.RealmName(realm);
                OpenBgRealmStatus summary = realms.FirstOrDefault(item => item.Realm == name);
                rows.Add(new Row(RealmPrefix + name, Header: RealmHeader(name, summary)));
                OpenBgBotStatus[] mine = bots.Where(bot => bot.Realm == name && Matches(bot.Name, bot.ClassName, session.Query)).ToArray();
                foreach (IGrouping<int, OpenBgBotStatus> unit in OrderedUnits(mine))
                {
                    string leader = LeaderOf(unit);
                    foreach (OpenBgBotStatus bot in unit.OrderBy(bot => bot.Name != leader).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase))
                        rows.Add(new Row(UnitPrefix + unit.Key.ToString(CultureInfo.InvariantCulture), Realm: realm, Name: bot.Name,
                            Level: bot.Level.ToString(CultureInfo.InvariantCulture), Class: bot.ClassName, Type: UnitLabel(bot.Kind, bot.Unit),
                            State: StateText(bot), StateActive: bot.State == "inside" && bot.Alive));
                }
            }
            OpenBgGuestStatus[] guests = (status?.Guests ?? Array.Empty<OpenBgGuestStatus>())
                .Where(guest => (session.Realm == null || guest.Realm == OpenBattlegroundRoster.RealmName(session.Realm.Value)) &&
                    Matches(guest.Realm + " squad", guest.Target, session.Query)).ToArray();
            if (guests.Length > 0)
            {
                rows.Add(new Row(GuestsKey, Header: $"Guest squads ({guests.Length})"));
                for (int index = 0; index < guests.Length; index++)
                {
                    OpenBgGuestStatus guest = guests[index];
                    rows.Add(new Row(GuestsKey, Realm: RealmOf(guest.Realm), Name: $"{guest.Realm} squad",
                        Level: guest.Level.ToString(CultureInfo.InvariantCulture), Class: $"{guest.Alive}/{guest.Size} alive", Type: "guest",
                        State: string.IsNullOrWhiteSpace(guest.Target) ? "hunting" : "near " + guest.Target, StateActive: guest.Alive > 0));
                }
            }
            OpenBgPlayerStatus[] players = (status?.Players ?? Array.Empty<OpenBgPlayerStatus>())
                .Where(player => Matches(player.Name, player.Realm, session.Query)).ToArray();
            if (players.Length > 0)
            {
                rows.Add(new Row(PlayersKey, Header: $"Players inside ({players.Length})"));
                foreach (OpenBgPlayerStatus player in players)
                    rows.Add(new Row(PlayersKey, Realm: RealmOf(player.Realm), Name: player.Name,
                        Level: player.Level.ToString(CultureInfo.InvariantCulture), Class: player.Realm, Type: "player", State: "inside", StateActive: true));
            }
            return rows;
        }

        private static IReadOnlyList<Row> SetupRows(OpenBgManagerSession session, OpenBgSetupValues values, OpenBgSetup setup,
            OpenBgRealmStatus[] realms, eRealm homeRealm)
        {
            var rows = new List<Row> { new(TitlePrefix + "crew", Header: "Crew per realm: target, present, own or global setup") };
            foreach (eRealm realm in RealmOrder.Where(realm => session.Realm == null || session.Realm == realm))
            {
                string name = OpenBattlegroundRoster.RealmName(realm);
                OpenBgRealmStatus summary = realms.FirstOrDefault(item => item.Realm == name);
                OpenBgRealmTargets targets = setup.TargetsFor(realm);
                foreach (OpenBgUnitKind kind in Kinds)
                {
                    int target = targets.Count(kind), present = Present(summary, kind);
                    rows.Add(new Row(TargetKey(realm, kind), Realm: realm, Name: $"{name} {KindLabel(kind)}",
                        Level: target.ToString(CultureInfo.InvariantCulture), Class: $"present {present}", Type: setup.IsOverridden(realm) ? "own" : "global",
                        State: present == target ? "full" : present < target ? $"short {target - present}" : $"surplus {present - target}",
                        StateActive: present >= target));
                }
            }
            rows.Add(new Row(TitlePrefix + "global", Header: "Whole open BG"));
            foreach (string name in GlobalSettings)
            {
                OpenBgSetupKey key = OpenBgSetupParser.Find(name);
                rows.Add(new Row(SettingPrefix + name, Realm: homeRealm, Name: SettingLabel(name), Level: ShortValue(key, values),
                    Class: key.Range.Replace('|', '/'), Type: name is "groups8" or "small" or "solos" ? "default" : string.Empty,
                    State: InUseText(key, values), StateActive: true));
            }
            return rows;
        }

        private static string RealmHeader(string name, OpenBgRealmStatus summary) =>
            summary == null ? name : $"{name}: 8-man {summary.Groups8Present}/{summary.Groups8}, small {summary.SmallPresent}/{summary.Small}, " +
                $"solo {summary.SolosPresent}/{summary.Solos}, {summary.Bots} bots" + (summary.OwnSetup ? " (own setup)" : string.Empty);

        // Inside first, then outside, travelling and leaving; 8-man before small before solo; then by id.
        private static IEnumerable<IGrouping<int, OpenBgBotStatus>> OrderedUnits(IEnumerable<OpenBgBotStatus> bots) =>
            bots.GroupBy(bot => bot.Unit)
                .OrderBy(unit => StateRank(unit.GroupBy(bot => bot.State).OrderByDescending(group => group.Count()).First().Key))
                .ThenBy(unit => KindRank(unit.First().Kind)).ThenBy(unit => unit.Key);

        private static bool Matches(string name, string other, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            foreach (string term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if ((name ?? string.Empty).IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (other ?? string.Empty).IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            return true;
        }

        // ---- detail panel ----------------------------------------------------------------------------------------

        private static void AddTopBlock(List<Line> lines, OpenBgStatusSnapshot status, OpenBgBotStatus[] bots)
        {
            if (status == null)
            {
                AddText(lines, "No open BG state yet.");
                return;
            }
            AddText(lines, "Open BG: " + StateText(status) + ".");
            string phase = "Phase: " + (string.IsNullOrWhiteSpace(status.Phase) ? OpenBgStatusSnapshot.PhaseNotAvailable : status.Phase);
            if (status.PhaseSecondsLeft > 0) phase += $", {status.PhaseSecondsLeft / 60}m {status.PhaseSecondsLeft % 60}s left";
            AddText(lines, phase + ". Raid: " + (string.IsNullOrWhiteSpace(status.Attacker) ? "none" : status.Attacker) + ".");
            AddText(lines, "Keep 11: " + (status.KeepOwner ?? "unknown") + ".");
            AddText(lines, string.Format(CultureInfo.InvariantCulture, "World speed: {0}x selected, {1:0.0}x achieved.", status.SelectedSpeed,
                status.AchievedSpeed));
            OpenBgPlayerStatus[] players = status.Players ?? Array.Empty<OpenBgPlayerStatus>();
            AddText(lines, players.Length == 0 ? "Players inside: none."
                : "Players inside: " + string.Join(", ", players.Select(player => $"{player.Name} {player.Level} {player.Realm}")) + ".");
            AddText(lines, $"Crew: {Count(bots, "inside")} inside, {Count(bots, "travelling")} travelling, {Count(bots, "leaving")} leaving" +
                (Count(bots, "outside") > 0 ? $", {Count(bots, "outside")} outside" : string.Empty) + $"; cap {status.Cap}.");
            lines.Add(new Line(string.Empty));
        }

        // Fills the header and the selection's lines; returns the realm the action buttons change (None: no realm selected).
        private static eRealm AddSelectionDetail(CompanionManagerView view, List<Line> lines, OpenBgManagerSession session,
            OpenBgStatusSnapshot status, OpenBgSetupValues values, OpenBgSetup setup, OpenBgBotStatus[] bots, OpenBgRealmStatus[] realms,
            Func<int, bool> reliefPending, eRealm homeRealm)
        {
            string key = session.SelectedKey ?? string.Empty;
            view.HeaderRealm = homeRealm;
            if (key.StartsWith(UnitPrefix, StringComparison.Ordinal) &&
                int.TryParse(key[UnitPrefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int unit))
            {
                OpenBgBotStatus[] members = bots.Where(bot => bot.Unit == unit).ToArray();
                if (members.Length == 0)
                {
                    view.Header = $"Unit #{unit}";
                    AddText(lines, "This unit has left the open BG.");
                    return eRealm.None;
                }
                eRealm realm = RealmOf(members[0].Realm);
                AddUnitDetail(view, lines, unit, members, realm, realms, reliefPending);
                return realm;
            }
            if (key.StartsWith(RealmPrefix, StringComparison.Ordinal))
            {
                eRealm realm = RealmOf(key[RealmPrefix.Length..]);
                AddRealmDetail(view, lines, realm, values, setup, realms, bots);
                return realm;
            }
            if (key.StartsWith(TargetPrefix, StringComparison.Ordinal) && TryParseTargetKey(key, out eRealm targetRealm, out OpenBgUnitKind kind))
            {
                AddTargetDetail(view, lines, targetRealm, kind, values, setup, realms);
                return targetRealm;
            }
            if (key.StartsWith(SettingPrefix, StringComparison.Ordinal))
            {
                AddSettingDetail(view, lines, OpenBgSetupParser.Find(key[SettingPrefix.Length..]), values);
                return eRealm.None;
            }
            if (key == GuestsKey)
            {
                view.Header = "Guest squads";
                AddText(lines, "While a player below level 40 is inside, each realm sends one squad on that player's level. Guest squads earn and drop nothing and leave five minutes after the last such player.");
                OpenBgGuestStatus[] guests = status?.Guests ?? Array.Empty<OpenBgGuestStatus>();
                AddText(lines, OpenBgSetupPages.GuestLine(guests));
                return eRealm.None;
            }
            if (key == PlayersKey)
            {
                view.Header = "Players inside";
                OpenBgPlayerStatus[] players = status?.Players ?? Array.Empty<OpenBgPlayerStatus>();
                foreach (OpenBgPlayerStatus player in players)
                    AddText(lines, $"{player.Name}, level {player.Level}, {player.Realm}.");
                if (players.Length == 0) AddText(lines, "Nobody is inside.");
                return eRealm.None;
            }
            view.Header = "Open BG";
            AddText(lines, session.Tab == OpenBgManagerTab.Live
                ? "No crew yet. The roster fills each realm from its level 40-50 world bots within a minute while the open BG is on."
                : "Select a row to change it.");
            return eRealm.None;
        }

        private static void AddUnitDetail(CompanionManagerView view, List<Line> lines, int unit, OpenBgBotStatus[] members, eRealm realm,
            OpenBgRealmStatus[] realms, Func<int, bool> reliefPending)
        {
            OpenBgBotStatus first = members[0];
            string leader = LeaderOf(members);
            string state = members.GroupBy(bot => bot.State).OrderByDescending(group => group.Count()).First().Key;
            view.Header = $"{UnitLabel(first.Kind, unit)}, {first.Realm}";
            view.HeaderRealm = realm;
            view.Subheader = (first.Kind == "solo" ? first.Name : "Leader " + (leader ?? "none")) +
                (first.Role != null ? " | " + RoleText(first.Role) : string.Empty) + (first.InRaid ? " | keep raid" : string.Empty);
            AddText(lines, $"{first.Realm} {KindWords(first.Kind)} #{unit}: {members.Length} {(members.Length == 1 ? "member" : "members")}, " +
                $"{members.Count(bot => bot.Alive)} alive.");
            string where = state switch
            {
                "inside" => first.Role != null ? $"Inside, role {RoleText(first.Role)}" : "Inside, no order yet",
                "travelling" => "Travelling: gathering at its porter",
                "leaving" => "Leaving: walking to its portal keep",
                _ => "Entered, but outside Thidranki right now",
            };
            if (first.InRaid) where += ", part of this cycle's keep raid";
            int inside = members.Where(bot => bot.State == "inside").Select(bot => bot.InsideSeconds).DefaultIfEmpty(0).Max();
            if (inside > 0) where += $"; inside for {inside / 60} game min";
            AddText(lines, where + ".");
            lines.Add(new Line("Members:"));
            foreach (OpenBgBotStatus bot in members.OrderBy(bot => bot.Name != leader).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase))
                lines.Add(new Line($"  {bot.Name}, {bot.Level} {bot.ClassName}" + (bot.Name == leader && members.Length > 1 ? ", leader" : string.Empty) +
                    (bot.Alive ? string.Empty : ", dead") + (bot.State == state ? string.Empty : ", " + bot.State)));
            OpenBgFormFailureStatus failure = FailureOf(realms, first.Realm, first.Kind);
            if (failure != null)
                AddText(lines, $"Last form failure ({KindWords(first.Kind)}, {Ago(failure.AgeSeconds)}): {failure.Text}.");
            if (reliefPending?.Invoke(unit) == true)
                AddText(lines, "Relief requested: the unit leaves on the roster's next tick.");
            else if (state != "leaving")
                lines.Add(new Line("[Relieve this unit now]", new OpenBgIntent(OpenBgIntentKind.Relieve, OpenBgSetupStore.ReliefMessage(unit), Unit: unit)));
        }

        private static void AddRealmDetail(CompanionManagerView view, List<Line> lines, eRealm realm, OpenBgSetupValues values, OpenBgSetup setup,
            OpenBgRealmStatus[] realms, OpenBgBotStatus[] bots)
        {
            string name = OpenBattlegroundRoster.RealmName(realm);
            OpenBgRealmStatus summary = realms.FirstOrDefault(item => item.Realm == name);
            OpenBgRealmTargets targets = setup.TargetsFor(realm);
            view.Header = name + " crew";
            view.HeaderRealm = realm;
            view.Subheader = setup.IsOverridden(realm) ? $"Own setup {targets.Format()}" : $"Global setup {targets.Format()}";
            AddText(lines, $"Targets: 8-man {targets.Groups8}, small {targets.Small}, solo {targets.Solos}" +
                (setup.IsOverridden(realm) ? $" (own setup; the global setup is {values.Groups8}/{values.Small}/{values.Solos})." : " (the global setup)."));
            AddText(lines, $"Present: 8-man {Present(summary, OpenBgUnitKind.Group8)}, small {Present(summary, OpenBgUnitKind.Small)}, " +
                $"solo {Present(summary, OpenBgUnitKind.Solo)}; {bots.Count(bot => bot.Realm == name && bot.State == "inside")} bots inside, " +
                $"{bots.Count(bot => bot.Realm == name && bot.State == "travelling")} travelling.");
            foreach (OpenBgFormFailureStatus failure in summary?.FormFailures ?? Array.Empty<OpenBgFormFailureStatus>())
                AddText(lines, $"Last form failure ({KindWords(failure.Kind)}, {Ago(failure.AgeSeconds)}): {failure.Text}.");
            AddText(lines, $"The buttons below add or remove {name} units; the roster acts within a game minute.");
            if (setup.IsOverridden(realm))
                lines.Add(new Line($"[Use the global setup for {name}]", UnitsWrite(realm, null, $"{name} uses the global setup again " +
                    $"({values.Groups8}/{values.Small}/{values.Solos}).")));
        }

        private static void AddTargetDetail(CompanionManagerView view, List<Line> lines, eRealm realm, OpenBgUnitKind kind, OpenBgSetupValues values,
            OpenBgSetup setup, OpenBgRealmStatus[] realms)
        {
            string name = OpenBattlegroundRoster.RealmName(realm);
            OpenBgRealmStatus summary = realms.FirstOrDefault(item => item.Realm == name);
            int target = setup.TargetsFor(realm).Count(kind);
            view.Header = $"{name} {KindLabel(kind)}";
            view.HeaderRealm = realm;
            view.Subheader = $"Target {target}, present {Present(summary, kind)}";
            AddText(lines, KindHelp(kind));
            AddText(lines, setup.IsOverridden(realm)
                ? $"{name} has its own setup {setup.TargetsFor(realm).Format()} (8-man/small/solos)."
                : $"{name} uses the global setup {setup.TargetsFor(realm).Format()}; a change here gives {name} its own setup and leaves the other realms as they are.");
            OpenBgFormFailureStatus failure = FailureOf(realms, name, OpenBattlegroundRoster.KindName(kind));
            if (failure != null) AddText(lines, $"Last form failure ({Ago(failure.AgeSeconds)}): {failure.Text}.");
            foreach (int step in new[] { 1, -1 })
            {
                int next = target + step;
                if (next < 0 || next > OpenBgRealmTargets.Max(kind)) continue;
                (OpenBgIntent intent, _) = Change(values, setup, realm, kind, step);
                lines.Add(new Line($"[{(step > 0 ? "Raise" : "Lower")} to {next}]", intent));
            }
            if (setup.IsOverridden(realm))
                lines.Add(new Line($"[Use the global setup for {name}]", UnitsWrite(realm, null, $"{name} uses the global setup again " +
                    $"({values.Groups8}/{values.Small}/{values.Solos}).")));
        }

        private static void AddSettingDetail(CompanionManagerView view, List<Line> lines, OpenBgSetupKey key, OpenBgSetupValues values)
        {
            if (key == null) return;
            view.Header = SettingLabel(key.Name);
            view.Subheader = $"{key.Name} = {values.ValueOf(key.Name)} ({key.Range})";
            AddText(lines, char.ToUpperInvariant(key.Help[0]) + key.Help[1..] + ".");
            string inUse = InUseText(key, values);
            if (inUse.Length > 0) AddText(lines, inUse + ".");
            int cycleMin = Math.Clamp(values.CycleMin, OpenBattlegroundProperties.CycleMinutesMin, OpenBattlegroundProperties.CycleMinutesMax);
            int cycleMax = Math.Clamp(values.CycleMax, cycleMin, OpenBattlegroundProperties.CycleMinutesMax);
            if (key.IsSwitch)
            {
                bool on = values.ValueOf(key.Name) == OpenBgSetupParser.Switch(true);
                string value = OpenBgSetupParser.Switch(!on);
                lines.Add(new Line(on ? "[Turn off]" : "[Turn on]", new OpenBgIntent(OpenBgIntentKind.Write,
                    $"{SettingLabel(key.Name)}: {(on ? "off" : "on")}.", key.RowKey, value)));
                return;
            }
            (int Min, int Max) band = OpenBattlegroundProperties.ResolveBand(values.BandMin, values.BandMax);
            int current = int.Parse(values.ValueOf(key.Name), CultureInfo.InvariantCulture);
            int[] steps = key.Max - key.Min >= 20 ? new[] { 1, 5, -1, -5 } : new[] { 1, -1 };
            foreach (int step in steps)
            {
                int next = Math.Clamp(current, key.Min, key.Max) + step;
                (bool ok, string rowKey, string value, _) = OpenBgSetupParser.Parse(new[] { "&bgsetup", key.Name, next.ToString(CultureInfo.InvariantCulture) },
                    cycleMin, cycleMax, band.Min, band.Max);
                if (!ok) continue;
                lines.Add(new Line($"[{(step > 0 ? "Raise" : "Lower")} to {next}]", new OpenBgIntent(OpenBgIntentKind.Write,
                    $"{SettingLabel(key.Name)}: {current} -> {next}." + (key.Name is "groups8" or "small" or "solos"
                        ? " Realms with their own setup keep it." : string.Empty), rowKey, value)));
            }
        }

        // ---- the six action buttons ------------------------------------------------------------------------------

        private static (string Text, bool Enabled, OpenBgIntent Intent)[] ActionButtons(OpenBgSetupValues values, OpenBgSetup setup, eRealm realm)
        {
            var actions = new (string, bool, OpenBgIntent)[Actions];
            int index = 0;
            foreach (OpenBgUnitKind kind in Kinds)
                foreach (int step in new[] { 1, -1 })
                {
                    string text = $"{(step > 0 ? "+" : "-")} {KindLabel(kind)}";
                    if (realm == eRealm.None)
                    {
                        actions[index++] = (text, false, new OpenBgIntent(OpenBgIntentKind.Message,
                            "Select a realm first: click a realm's line or a unit, or pick Albion, Midgard or Hibernia above."));
                        continue;
                    }
                    (OpenBgIntent intent, bool possible) = Change(values, setup, realm, kind, step);
                    actions[index++] = ($"{text} {OpenBattlegroundRoster.RealmName(realm)}", possible, intent);
                }
            return actions;
        }

        /// <summary>
        /// The write behind "+"/"-" of a kind for a realm: the realm's targets (its own, else the global ones) with that
        /// kind one up or down, stored as the realm's own setup. At a bound only a message. The text warns when the
        /// targets of all realms then book more seats than the cap.
        /// </summary>
        private static (OpenBgIntent Intent, bool Possible) Change(OpenBgSetupValues values, OpenBgSetup setup, eRealm realm, OpenBgUnitKind kind, int step)
        {
            string name = OpenBattlegroundRoster.RealmName(realm);
            OpenBgRealmTargets targets = setup.TargetsFor(realm);
            int current = targets.Count(kind), next = current + step;
            if (next < 0)
                return (new OpenBgIntent(OpenBgIntentKind.Message, $"{name} has no {KindLabel(kind)} unit to remove."), false);
            if (next > OpenBgRealmTargets.Max(kind))
                return (new OpenBgIntent(OpenBgIntentKind.Message, $"{name} already has the most {KindLabel(kind)} units ({OpenBgRealmTargets.Max(kind)})."), false);
            OpenBgRealmTargets changed = targets.With(kind, next);
            OpenBgSetup after = setup.WithOverride(realm, changed);
            string message = $"{name}: {KindLabel(kind)} {current} -> {next}. " + (step > 0
                ? $"The roster forms it within a minute if the {OpenBattlegroundRoster.MinLevel}-{OpenBattlegroundRoster.MaxLevel} pool allows."
                : "The roster relieves the surplus unit within a minute.");
            int planned = RealmOrder.Sum(item => after.TargetsFor(item).PlannedBots);
            if (step > 0 && planned > after.MaxBots)
                message += $" The bot cap of {after.MaxBots} binds: the setup wants at least {planned} bots.";
            return (UnitsWrite(realm, changed, message), true);
        }

        private static OpenBgIntent UnitsWrite(eRealm realm, OpenBgRealmTargets? targets, string message) =>
            new(OpenBgIntentKind.Write, message, OpenBattlegroundProperties.UnitsRowKey(realm), targets?.Format() ?? string.Empty);

        // ---- small helpers ---------------------------------------------------------------------------------------

        public static string TargetKey(eRealm realm, OpenBgUnitKind kind) =>
            $"{TargetPrefix}{OpenBattlegroundRoster.RealmName(realm)}:{OpenBattlegroundRoster.KindName(kind)}";

        private static bool TryParseTargetKey(string key, out eRealm realm, out OpenBgUnitKind kind)
        {
            string[] parts = key[TargetPrefix.Length..].Split(':');
            realm = parts.Length == 2 ? RealmOf(parts[0]) : eRealm.None;
            kind = parts.Length == 2 ? KindOf(parts[1]) : OpenBgUnitKind.Solo;
            return realm != eRealm.None;
        }

        public static eRealm RealmOf(string name) => name switch
        {
            "Albion" => eRealm.Albion,
            "Midgard" => eRealm.Midgard,
            "Hibernia" => eRealm.Hibernia,
            _ => eRealm.None,
        };

        private static OpenBgUnitKind KindOf(string name) => name switch
        {
            "group8" => OpenBgUnitKind.Group8,
            "small" => OpenBgUnitKind.Small,
            _ => OpenBgUnitKind.Solo,
        };

        public static string KindLabel(OpenBgUnitKind kind) => kind switch
        {
            OpenBgUnitKind.Group8 => "8-man",
            OpenBgUnitKind.Small => "small",
            _ => "solo",
        };

        private static string KindWords(string kind) => kind switch
        {
            "group8" => "8-man group",
            "small" => "small group",
            _ => "solo",
        };

        private static string KindHelp(OpenBgUnitKind kind) => kind switch
        {
            OpenBgUnitKind.Group8 => "8-man groups (0-4) hold bridges, roam and storm the keep. One forms with six to eight bots.",
            OpenBgUnitKind.Small => "Small groups of two to four (0-6); the size is rolled when the group forms.",
            _ => "Solo roamers and stealthers (0-12); they lurk near the bridges.",
        };

        /// <summary>The list's unit column: "8-man #12", "small #40", "solo #51".</summary>
        public static string UnitLabel(string kind, int unit) => $"{(kind == "group8" ? "8-man" : kind)} #{unit}";

        private static string RoleText(string role) => role?.Replace('_', ' ');

        private static string StateText(OpenBgBotStatus bot) => bot.State switch
        {
            "inside" when !bot.Alive => "dead",
            "inside" => (RoleText(bot.Role) ?? "inside") + (bot.InRaid ? ", raid" : string.Empty),
            _ => bot.State,
        };

        private static string StateText(OpenBgStatusSnapshot status) =>
            !status.CampaignEnabled ? "closed (the battleground campaign is off)"
            : !status.OpenEnabled ? "off"
            : !status.Ready ? "on, NOT ready: " + (status.NotReadyReason ?? "unknown")
            : "on and ready";

        private static string StatusLine(OpenBgStatusSnapshot status, OpenBgBotStatus[] bots, string query)
        {
            if (status == null) return "Open BG, Thidranki";
            string phase = string.IsNullOrWhiteSpace(status.Phase) ? OpenBgStatusSnapshot.PhaseNotAvailable : status.Phase;
            string line = $"Open BG, Thidranki: {StateText(status)} | {phase}" +
                (status.PhaseSecondsLeft > 0 ? $" {status.PhaseSecondsLeft / 60}m {status.PhaseSecondsLeft % 60}s left" : string.Empty) +
                $" | {Count(bots, "inside")} bots inside, cap {status.Cap} | refreshes every 15 s";
            return string.IsNullOrEmpty(query) ? line : line + $" | search \"{query}\"";
        }

        private static string LeaderOf(IEnumerable<OpenBgBotStatus> unit) =>
            unit.Select(bot => bot.Leader).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

        private static OpenBgFormFailureStatus FailureOf(OpenBgRealmStatus[] realms, string realm, string kind) =>
            realms.FirstOrDefault(item => item.Realm == realm)?.FormFailures?.FirstOrDefault(failure => failure.Kind == kind);

        private static int Present(OpenBgRealmStatus summary, OpenBgUnitKind kind) => summary == null ? 0 : kind switch
        {
            OpenBgUnitKind.Group8 => summary.Groups8Present,
            OpenBgUnitKind.Small => summary.SmallPresent,
            _ => summary.SolosPresent,
        };

        private static string Ago(int seconds) => seconds < 60 ? "just now" : $"{seconds / 60} game min ago";

        private static string SettingLabel(string name) => name switch
        {
            "enabled" => "Open BG on",
            "max_bots" => "Bot cap",
            "groups8" => "8-man per realm",
            "small" => "Small per realm",
            "solos" => "Solos per realm",
            "cycle_min" => "Shortest cycle",
            "cycle_max" => "Longest cycle",
            "band_min" => "Crew level from",
            "band_max" => "Crew level to",
            "drift" => "Player drift",
            _ => name,
        };

        private static string ShortValue(OpenBgSetupKey key, OpenBgSetupValues values) =>
            key.IsSwitch ? (values.ValueOf(key.Name) == OpenBgSetupParser.Switch(true) ? "on" : "off") : values.ValueOf(key.Name);

        // "in use 120" when the row holds an out-of-range number; minutes for the cycle rows.
        private static string InUseText(OpenBgSetupKey key, OpenBgSetupValues values)
        {
            if (key.IsSwitch || key.IsUnits) return string.Empty;
            int raw = int.Parse(values.ValueOf(key.Name), CultureInfo.InvariantCulture);
            int used = Math.Clamp(raw, key.Min, key.Max);
            string unit = key.Name.StartsWith("cycle", StringComparison.Ordinal) ? " game min" : string.Empty;
            return used == raw ? (unit.Length > 0 ? raw + unit : string.Empty) : $"in use {used}{unit}";
        }

        private static int Count(IEnumerable<OpenBgBotStatus> bots, string state) => bots.Count(bot => bot.State == state);
        private static int StateRank(string state) => state switch { "inside" => 0, "outside" => 1, "travelling" => 2, _ => 3 };
        private static int KindRank(string kind) => kind switch { "group8" => 0, "small" => 1, _ => 2 };

        private static void AddText(List<Line> lines, string text)
        {
            foreach (string line in CompanionManagerSession.Wrap(Sanitize(text, int.MaxValue), DetailWidth))
                lines.Add(new Line(line));
        }
    }
}
