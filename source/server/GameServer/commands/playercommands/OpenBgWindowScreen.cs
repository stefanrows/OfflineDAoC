using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.GS.Commands
{
    /// <summary>The two pages of the OPEN BG window (Custom5): the read-only situation and the steering rows.</summary>
    public enum OpenBgWindowPage { Lage, Steuern }

    public enum OpenBgWindowIntentKind
    {
        None, SetEnabled, SetMode, CyclePeace, Adjust, ResetUnits, Relieve, Tab, Scroll, Refresh, Close,
    }

    /// <summary>
    /// What a click does, decided when the window is drawn. SetEnabled: <see cref="On"/>. SetMode: <see cref="Key"/> is
    /// serverpvp or perrealm. Adjust: <see cref="Key"/> is a /bgsetup key, "preset:&lt;archetype&gt;" or "units:&lt;Realm&gt;:&lt;kind&gt;",
    /// <see cref="Amount"/> is -1 or +1 (times the key's step). ResetUnits: <see cref="Key"/> is the realm. Relieve:
    /// <see cref="Unit"/>. Tab: <see cref="Key"/> is Lage or Steuern. Scroll: <see cref="Amount"/> rows.
    /// </summary>
    public sealed record OpenBgWindowIntent(OpenBgWindowIntentKind Kind, string Key = "", int Amount = 0, bool On = false, int Unit = 0)
    {
        /// <summary>The identity of the click for the view signature.</summary>
        public string Id => $"{Kind}:{Key}:{Amount}:{On}:{Unit}";
    }

    /// <summary>
    /// One row of the window as the screen decides it: a section title, a plain text line, or the columns. The texts
    /// are already clipped to their columns. <see cref="Key"/> identifies what the row stands for.
    /// </summary>
    public sealed record OpenBgWindowRow(string Key, string Title = null, string Text = null, eRealm Realm = eRealm.None, string Name = null,
        string Level = null, string Class = null, string Unit = null, string State = null, bool StateActive = false, string Value = null,
        OpenBgWindowIntent Link = null, OpenBgWindowIntent Minus = null, OpenBgWindowIntent Plus = null, bool Steps = false);

    /// <summary>The drawn window: texts, the visible rows (at most 22) and the paging figures.</summary>
    public sealed class OpenBgWindowView
    {
        public OpenBgWindowPage Page { get; init; }
        public string Status { get; init; } = string.Empty;
        public string PageIndicator { get; init; } = string.Empty;
        public string[] Header { get; init; } = new string[BgHeaderLabels];
        public string[] Reply { get; init; } = new string[BgReplyLines];
        public IReadOnlyList<OpenBgWindowRow> Rows { get; init; } = Array.Empty<OpenBgWindowRow>();
        public int TotalRows { get; init; }
        /// <summary>The offset after clamping.</summary>
        public int Offset { get; init; }

        /// <summary>The labels for indexes 620..984 in the slot order of open_bg_window() (see OpenBgWindowScreen).</summary>
        public string[] ToLabels()
        {
            var labels = Enumerable.Repeat(string.Empty, BgLabelEnd - BgLabelBase).ToArray();
            labels[BgLabelStatus - BgLabelBase] = Status;
            bool lage = Page == OpenBgWindowPage.Lage;
            // 621 Lage inactive, 622 Lage active, 623 Steuern inactive, 624 Steuern active
            labels[1] = lage ? string.Empty : "Lage";
            labels[2] = lage ? "Lage" : string.Empty;
            labels[3] = lage ? "Steuern" : string.Empty;
            labels[4] = lage ? string.Empty : "Steuern";
            labels[BgLabelPage - BgLabelBase] = PageIndicator;
            for (int line = 0; line < BgHeaderLabels; line++)
                labels[BgLabelHeaderBase - BgLabelBase + line] = Header[line] ?? string.Empty;
            for (int line = 0; line < BgReplyLines; line++)
                labels[BgLabelReplyBase - BgLabelBase + line] = Reply[line] ?? string.Empty;
            for (int index = 0; index < Rows.Count && index < BgRowCount; index++)
            {
                OpenBgWindowRow row = Rows[index];
                void Put(int slot, string text)
                {
                    if (!string.IsNullOrEmpty(text)) labels[OpenBgWindowScreen.RowLabelIndex(index, slot)] = text;
                }
                if (row.Title != null) { Put(0, row.Title); continue; }
                if (row.Text != null) { Put(1, row.Text); continue; }
                Put(row.Realm switch { eRealm.Albion => 2, eRealm.Midgard => 3, eRealm.Hibernia => 4, _ => 5 }, row.Name);
                Put(6, row.Level);
                Put(7, row.Class);
                Put(8, row.Unit);
                Put(row.StateActive ? 9 : 10, row.State);
                Put(11, row.Value);
                if (row.Steps)
                {
                    Put(row.Minus != null ? 12 : 13, "[-]");
                    Put(row.Plus != null ? 14 : 15, "[+]");
                }
            }
            return labels;
        }
    }

    /// <summary>
    /// Pure drawing of the OPEN BG window (Custom5) from a status snapshot and the setup rows. Label order (the contract
    /// with open_bg_window() in build_companion_manager_client.py): 620 status, 621-624 tabs (Lage inactive/active, Steuern
    /// inactive/active), 625 page indicator, 626-630 header lines, 631-632 reply lines, then 22 rows of 16 slots from 633.
    /// No rights check: every viewer gets the steering intents (owner decision 2026-10-16). Applying an intent is the
    /// runtime's job (<see cref="ToWrite"/> words the write).
    /// </summary>
    public static class OpenBgWindowScreen
    {
        // Pixel width and MaxCharacters of the row slots, as the XML declares them.
        private static readonly (int Width, int Characters)[] RowSlots =
        {
            (740, 120), (740, 120), (140, 32), (140, 32), (140, 32), (140, 32), (26, 6), (104, 32), (100, 24), (150, 40), (150, 40),
            (110, 32), (36, 6), (36, 6), (36, 6), (36, 6),
        };
        private const int StatusWidth = 630, StatusCharacters = 120, LineWidth = 740, LineCharacters = 120, PageWidth = 130, PageCharacters = 24;
        private static readonly eRealm[] RealmOrder = { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };
        private static readonly OpenBgUnitKind[] Kinds = { OpenBgUnitKind.Group8, OpenBgUnitKind.Small, OpenBgUnitKind.Solo };
        private static readonly string[] PresetKeys = { "std8", "caster8", "raid8", "stealth4", "small", "duo" };
        private static readonly string[] PeaceCycle = { "auto", "all", "realm" };

        /// <summary>The label index (relative to 620) of a row's slot.</summary>
        public static int RowLabelIndex(int row, int slot) => BgLabelRowBase - BgLabelBase + BgRowStride * row + slot;

        /// <summary>Owner defaults: bot cap 4, cycle 5, everything else 1.</summary>
        public static int StepOf(string key) => key switch { "max_bots" => 4, "cycle_min" or "cycle_max" => 5, _ => 1 };

        public static OpenBgWindowView Build(OpenBgStatusSnapshot status, OpenBgSetupValues values, OpenBgWindowPage page, int offset, string reply)
        {
            IReadOnlyList<OpenBgWindowRow> rows = AllRows(status, values, page);
            int clamped = Math.Clamp(offset, 0, Math.Max(0, rows.Count - BgRowCount));
            OpenBgWindowRow[] visible = rows.Skip(clamped).Take(BgRowCount).Select(Clipped).ToArray();
            string[] header = (page == OpenBgWindowPage.Lage ? LageHeader(status, values) : SteuernHeader(values))
                .Select(line => Clip(line, LineWidth, LineCharacters)).ToArray();
            string[] replies = Wrap(reply ?? string.Empty, BgReplyLines);
            return new OpenBgWindowView
            {
                Page = page,
                Status = Clip(StatusText(status), StatusWidth, StatusCharacters),
                PageIndicator = Clip(rows.Count == 0 ? "Nothing to show" : $"{clamped + 1}-{clamped + visible.Length} of {rows.Count}", PageWidth, PageCharacters),
                Header = header,
                Reply = replies,
                Rows = visible,
                TotalRows = rows.Count,
                Offset = clamped,
            };
        }

        /// <summary>Every row of a page (not yet clipped to columns), for paging and tests.</summary>
        public static IReadOnlyList<OpenBgWindowRow> AllRows(OpenBgStatusSnapshot status, OpenBgSetupValues values, OpenBgWindowPage page) =>
            page == OpenBgWindowPage.Lage ? LageRows(status, values) : SteuernRows(status, values);

        // ---- clipping --------------------------------------------------------------------------------------------

        /// <summary>The text cut to the smaller of the pixel width (arial14) and the label's MaxCharacters.</summary>
        public static string Clip(string text, int width, int maxCharacters)
        {
            text = Sanitize(text ?? string.Empty, int.MaxValue).Trim();
            if (text.Length > maxCharacters)
                text = maxCharacters > 3 ? text[..(maxCharacters - 3)].TrimEnd() + "..." : text[..maxCharacters];
            return Fit(text, width);
        }

        private static string Slot(string text, int slot) => text == null ? null : Clip(text, RowSlots[slot].Width, RowSlots[slot].Characters);

        private static OpenBgWindowRow Clipped(OpenBgWindowRow row) => row with
        {
            Title = Slot(row.Title, 0), Text = Slot(row.Text, 1), Name = Slot(row.Name, 2), Level = Slot(row.Level, 6),
            Class = Slot(row.Class, 7), Unit = Slot(row.Unit, 8), State = Slot(row.State, 9), Value = Slot(row.Value, 11),
        };

        private static string[] Wrap(string text, int lines)
        {
            string clean = Sanitize(text, int.MaxValue).Trim();
            var result = new string[lines];
            for (int line = 0; line < lines; line++)
            {
                if (clean.Length == 0) { result[line] = string.Empty; continue; }
                int take = 0;
                int width = 0;
                int lastSpace = -1;
                while (take < clean.Length && take < LineCharacters && width + TextWidth(clean[take].ToString()) <= LineWidth)
                {
                    width += TextWidth(clean[take].ToString());
                    if (clean[take] == ' ') lastSpace = take;
                    take++;
                }
                if (take < clean.Length && lastSpace > 0 && line < lines - 1) take = lastSpace;
                result[line] = line == lines - 1 ? Clip(clean, LineWidth, LineCharacters) : clean[..take].TrimEnd();
                clean = clean[take..].TrimStart();
            }
            return result;
        }

        // ---- Lage ------------------------------------------------------------------------------------------------

        private static string StatusText(OpenBgStatusSnapshot status) => status == null ? "no state yet" : StateText(status);

        private static string ModeText(OpenBgSetupValues values) => values.ValueOf("mode") == "perrealm" ? "Per realm" : "Server PvP";

        private static string[] LageHeader(OpenBgStatusSnapshot status, OpenBgSetupValues values)
        {
            string[] lines = new string[BgHeaderLabels];
            if (status == null)
            {
                lines[0] = "No open BG state yet.";
                return lines;
            }
            OpenBgBotStatus[] bots = status.Bots ?? Array.Empty<OpenBgBotStatus>();
            string phase = string.IsNullOrWhiteSpace(status.Phase) ? OpenBgStatusSnapshot.PhaseNotAvailable : status.Phase;
            if (status.PhaseSecondsLeft > 0) phase += $" {status.PhaseSecondsLeft / 60}m {status.PhaseSecondsLeft % 60}s left";
            lines[0] = $"Phase: {phase} | Mode: {ModeText(values)}, landing peace {values.ValueOf("peace")}";
            OpenBgBotStatus[] raid = bots.Where(bot => bot.InRaid).ToArray();
            lines[1] = $"Raid: {raid.Select(bot => bot.Unit).Distinct().Count()} units, {raid.Length} bots, lead " +
                (string.IsNullOrWhiteSpace(status.Attacker) ? "none" : status.Attacker) + " | Keep 11: " + (status.KeepOwner ?? "unknown");
            string field = status.LastField == null ? "Field: no figures yet"
                : $"Field: {status.LastField.UnitsOut}/{status.LastField.Units} units, {status.LastField.BotsOut}/{status.LastField.Bots} bots out ({status.LastField.Pct}%)";
            lines[2] = string.Format(CultureInfo.InvariantCulture, "World speed: {0}x set, {1:0.0}x achieved | {2}", status.SelectedSpeed,
                status.AchievedSpeed, field);
            (int Min, int Max) band = OpenBattlegroundProperties.ResolveBand(values.BandMin, values.BandMax);
            int outside = Count(bots, "outside");
            lines[3] = $"Crew: {Count(bots, "inside")} inside, {Count(bots, "travelling")} travelling, {Count(bots, "leaving")} leaving" +
                (outside > 0 ? $", {outside} outside" : string.Empty) + $" | cap {status.Cap}, level {band.Min}-{band.Max}";
            OpenBgPlayerStatus[] players = status.Players ?? Array.Empty<OpenBgPlayerStatus>();
            lines[4] = players.Length == 0 ? "Players inside: none"
                : $"Players inside ({players.Length}): " + string.Join(", ", players.Select(player => $"{player.Name} {player.Level}"));
            return lines;
        }

        private static IReadOnlyList<OpenBgWindowRow> LageRows(OpenBgStatusSnapshot status, OpenBgSetupValues values)
        {
            var rows = new List<OpenBgWindowRow>();
            OpenBgBotStatus[] bots = status?.Bots ?? Array.Empty<OpenBgBotStatus>();
            OpenBgSetup setup = values.ToSetup();
            OpenBgRealmStatus[] realms = status?.Realms ?? OpenBattlegroundStatus.RealmStatuses(setup, bots, null, 0);
            if (values.ValueOf("mode") == "perrealm")
            {
                rows.Add(new OpenBgWindowRow("h:realms", Title: "Units per realm (present/target)"));
                foreach (eRealm realm in RealmOrder)
                {
                    string name = OpenBattlegroundRoster.RealmName(realm);
                    OpenBgRealmStatus summary = realms.FirstOrDefault(item => item.Realm == name);
                    OpenBgRealmTargets targets = setup.TargetsFor(realm);
                    rows.Add(new OpenBgWindowRow("s:" + name, Realm: realm, Name: name,
                        Class: $"8-man {summary?.Groups8Present ?? 0}/{targets.Groups8}", Unit: $"small {summary?.SmallPresent ?? 0}/{targets.Small}",
                        State: $"solo {summary?.SolosPresent ?? 0}/{targets.Solos}", Value: $"{bots.Count(bot => bot.Realm == name && bot.State != "leaving")} bots"));
                }
            }
            else
            {
                rows.Add(new OpenBgWindowRow("h:archetypes", Title: "Units by archetype"));
                foreach (IGrouping<string, OpenBgBotStatus> group in bots.GroupBy(bot => string.IsNullOrEmpty(bot.Archetype) ? "no archetype" : bot.Archetype)
                    .OrderBy(group => group.Key == "no archetype").ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
                    rows.Add(new OpenBgWindowRow("a:" + group.Key, Text: $"{group.Key}: {group.Select(bot => bot.Unit).Distinct().Count()} units, " +
                        $"{group.Count()} bots, {group.Count(bot => bot.State == "inside")} inside"));
            }
            foreach (eRealm realm in RealmOrder)
            {
                string name = OpenBattlegroundRoster.RealmName(realm);
                rows.Add(new OpenBgWindowRow("r:" + name, Title: RealmHeader(name, realms.FirstOrDefault(item => item.Realm == name))));
                foreach (IGrouping<int, OpenBgBotStatus> unit in OrderedUnits(bots.Where(bot => bot.Realm == name)))
                {
                    string leader = LeaderOf(unit);
                    foreach (OpenBgBotStatus bot in unit.OrderBy(bot => bot.Name != leader).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase))
                        rows.Add(new OpenBgWindowRow("u:" + unit.Key.ToString(CultureInfo.InvariantCulture) + ":" + bot.Name, Realm: realm, Name: bot.Name,
                            Level: bot.Level.ToString(CultureInfo.InvariantCulture), Class: bot.ClassName,
                            Unit: string.IsNullOrEmpty(bot.Archetype) ? UnitLabel(bot.Kind, bot.Unit) : $"{bot.Archetype} #{bot.Unit}",
                            State: StateText(bot), StateActive: bot.State == "inside" && bot.Alive));
                }
            }
            OpenBgGuestStatus[] guests = status?.Guests ?? Array.Empty<OpenBgGuestStatus>();
            if (guests.Length > 0)
            {
                rows.Add(new OpenBgWindowRow("h:guests", Title: $"Guest squads ({guests.Length})"));
                foreach (OpenBgGuestStatus guest in guests)
                    rows.Add(new OpenBgWindowRow("g:" + guest.Realm + ":" + guest.Target, Realm: RealmOf(guest.Realm), Name: guest.Realm + " squad",
                        Level: guest.Level.ToString(CultureInfo.InvariantCulture), Class: $"{guest.Alive}/{guest.Size} alive", Unit: "guest",
                        State: string.IsNullOrWhiteSpace(guest.Target) ? "hunting" : "near " + guest.Target, StateActive: guest.Alive > 0));
            }
            OpenBgPlayerStatus[] players = status?.Players ?? Array.Empty<OpenBgPlayerStatus>();
            if (players.Length > 0)
            {
                rows.Add(new OpenBgWindowRow("h:players", Title: $"Players inside ({players.Length})"));
                foreach (OpenBgPlayerStatus player in players)
                    rows.Add(new OpenBgWindowRow("p:" + player.Name, Realm: RealmOf(player.Realm), Name: player.Name,
                        Level: player.Level.ToString(CultureInfo.InvariantCulture), Class: player.Realm, Unit: "player", State: "inside", StateActive: true));
            }
            return rows;
        }

        // ---- Steuern ---------------------------------------------------------------------------------------------

        private static string[] SteuernHeader(OpenBgSetupValues values)
        {
            (int Min, int Max) band = OpenBattlegroundProperties.ResolveBand(values.BandMin, values.BandMax);
            OpenBgSetup setup = values.ToSetup();
            return new[]
            {
                $"Open BG {(values.Enabled ? "on" : "off")} | Mode: {ModeText(values)} | landing peace {values.ValueOf("peace")}",
                "Every player may change these rows; the roster reads a change within a game minute.",
                $"Crew level {band.Min}-{band.Max}, bot cap {setup.MaxBots}, per realm {setup.Groups8} 8-man / {setup.GroupsSmall} small / {setup.Solos} solos",
                "Presets: " + (PresetCounts(values.Presets).Any() ? values.ValueOf("presets") : "off"),
                string.Empty,
            };
        }

        private static IReadOnlyList<OpenBgWindowRow> SteuernRows(OpenBgStatusSnapshot status, OpenBgSetupValues values)
        {
            var rows = new List<OpenBgWindowRow> { new("h:open", Title: "Open BG") };
            rows.Add(SwitchRow(values, "enabled", "Open BG on", new OpenBgWindowIntent(OpenBgWindowIntentKind.SetEnabled, On: !values.Enabled)));
            bool perRealm = values.ValueOf("mode") == "perrealm";
            rows.Add(new OpenBgWindowRow("k:mode", Name: "Mode", Class: "serverpvp|perrealm", State: perRealm ? "perrealm" : "serverpvp", StateActive: true,
                Value: perRealm ? "[serverpvp]" : "[perrealm]", Link: new OpenBgWindowIntent(OpenBgWindowIntentKind.SetMode, perRealm ? "serverpvp" : "perrealm")));
            rows.Add(new OpenBgWindowRow("k:peace", Name: "Landing peace", Class: "auto|all|realm", State: values.ValueOf("peace"), StateActive: true,
                Value: "[change]", Link: new OpenBgWindowIntent(OpenBgWindowIntentKind.CyclePeace)));
            rows.Add(new OpenBgWindowRow("h:crew", Title: "Crew level and size"));
            foreach (string key in new[] { "band_min", "band_max", "max_bots", "groups8", "small", "solos" })
                rows.Add(NumberRow(values, key));
            rows.Add(new OpenBgWindowRow("h:presets", Title: "Group presets (units of that kind)"));
            Dictionary<string, int> mix = PresetCounts(values.Presets);
            foreach (string key in PresetKeys)
            {
                int count = mix.GetValueOrDefault(key);
                OpenBgArchetype archetype = OpenBattlegroundArchetypes.Get(key);
                rows.Add(new OpenBgWindowRow("p:" + key, Name: "Preset " + key, Class: "0-8", Unit: $"size {archetype.Size}", State: count.ToString(CultureInfo.InvariantCulture),
                    StateActive: count > 0, Steps: true,
                    Minus: count > 0 ? new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "preset:" + key, -1) : null,
                    Plus: count < 8 ? new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "preset:" + key, 1) : null));
            }
            if (perRealm)
            {
                rows.Add(new OpenBgWindowRow("h:units", Title: "Units per realm (8-man / small / solo)"));
                OpenBgSetup setup = values.ToSetup();
                foreach (eRealm realm in RealmOrder)
                {
                    string name = OpenBattlegroundRoster.RealmName(realm);
                    OpenBgRealmTargets targets = setup.TargetsFor(realm);
                    foreach (OpenBgUnitKind kind in Kinds)
                    {
                        int count = targets.Count(kind), max = OpenBgRealmTargets.Max(kind);
                        string kindName = OpenBattlegroundRoster.KindName(kind);
                        string key = $"units:{name}:{kindName}";
                        bool first = kind == OpenBgUnitKind.Group8;
                        rows.Add(new OpenBgWindowRow(key, Realm: realm, Name: name, Class: $"0-{max}", Unit: kindName, State: count.ToString(CultureInfo.InvariantCulture),
                            StateActive: true, Steps: true, Value: first ? (setup.IsOverridden(realm) ? "[global]" : "global") : null,
                            Link: first && setup.IsOverridden(realm) ? new OpenBgWindowIntent(OpenBgWindowIntentKind.ResetUnits, name) : null,
                            Minus: count > 0 ? new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, key, -1) : null,
                            Plus: count < max ? new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, key, 1) : null));
                    }
                }
            }
            rows.Add(new OpenBgWindowRow("h:timing", Title: "Drift and cycle"));
            rows.Add(SwitchRow(values, "drift", "Player drift", new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "drift", 1)));
            rows.Add(NumberRow(values, "cycle_min"));
            rows.Add(NumberRow(values, "cycle_max"));
            OpenBgBotStatus[] bots = status?.Bots ?? Array.Empty<OpenBgBotStatus>();
            if (bots.Length > 0)
            {
                rows.Add(new OpenBgWindowRow("h:relieve", Title: "Relieve a unit by hand"));
                foreach (eRealm realm in RealmOrder)
                {
                    string name = OpenBattlegroundRoster.RealmName(realm);
                    foreach (IGrouping<int, OpenBgBotStatus> unit in OrderedUnits(bots.Where(bot => bot.Realm == name)))
                    {
                        OpenBgBotStatus first = unit.First();
                        string leader = LeaderOf(unit) ?? first.Name;
                        string state = unit.GroupBy(bot => bot.State).OrderByDescending(group => group.Count()).First().Key;
                        rows.Add(new OpenBgWindowRow("x:" + unit.Key.ToString(CultureInfo.InvariantCulture), Realm: realm, Name: leader,
                            Level: first.Level.ToString(CultureInfo.InvariantCulture), Class: $"{unit.Count()} bots",
                            Unit: string.IsNullOrEmpty(first.Archetype) ? UnitLabel(first.Kind, unit.Key) : $"{first.Archetype} #{unit.Key}",
                            State: StateText(first), StateActive: state == "inside",
                            Value: state == "leaving" ? null : "[Relieve]",
                            Link: state == "leaving" ? null : new OpenBgWindowIntent(OpenBgWindowIntentKind.Relieve, Unit: unit.Key)));
                    }
                }
            }
            return rows;
        }

        // A switch row: the value, and one link that flips it. The link of "drift" is an Adjust whose amount flips (see ToWrite).
        private static OpenBgWindowRow SwitchRow(OpenBgSetupValues values, string key, string label, OpenBgWindowIntent flip)
        {
            bool on = values.ValueOf(key) == OpenBgSetupParser.Switch(true);
            return new OpenBgWindowRow("k:" + key, Name: label, Class: "on|off", State: on ? "on" : "off", StateActive: on,
                Value: on ? "[turn off]" : "[turn on]", Link: flip);
        }

        private static OpenBgWindowRow NumberRow(OpenBgSetupValues values, string key)
        {
            OpenBgSetupKey setting = OpenBgSetupParser.Find(key);
            int current = int.Parse(values.ValueOf(key), CultureInfo.InvariantCulture);
            return new OpenBgWindowRow("k:" + key, Name: SettingLabel(key), Class: setting.Range, Unit: "step " + StepOf(key),
                State: current.ToString(CultureInfo.InvariantCulture), StateActive: true, Steps: true,
                Minus: Next(values, key, -1, out _) ? new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, key, -1) : null,
                Plus: Next(values, key, 1, out _) ? new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, key, 1) : null);
        }

        // The value one step in a direction, clamped to the key's range; false when it does not change or the parser refuses it.
        private static bool Next(OpenBgSetupValues values, string key, int direction, out int next)
        {
            OpenBgSetupKey setting = OpenBgSetupParser.Find(key);
            int current = int.Parse(values.ValueOf(key), CultureInfo.InvariantCulture);
            next = Math.Clamp(Math.Clamp(current, setting.Min, setting.Max) + direction * StepOf(key), setting.Min, setting.Max);
            if (next == current) return false;
            int cycleMin = Math.Clamp(values.CycleMin, OpenBattlegroundProperties.CycleMinutesMin, OpenBattlegroundProperties.CycleMinutesMax);
            int cycleMax = Math.Clamp(values.CycleMax, cycleMin, OpenBattlegroundProperties.CycleMinutesMax);
            (int Min, int Max) band = OpenBattlegroundProperties.ResolveBand(values.BandMin, values.BandMax);
            return OpenBgSetupParser.Parse(new[] { "&bgsetup", key, next.ToString(CultureInfo.InvariantCulture) }, cycleMin, cycleMax, band.Min, band.Max).ok;
        }

        // ---- the write behind an intent ---------------------------------------------------------------------------

        /// <summary>
        /// The server property write behind a steering intent: the row key, the value and the reply line; not ok (with the
        /// reason) for a refused or no-op change, and for intents that are not writes (relieve, navigation).
        /// </summary>
        public static (bool Ok, string RowKey, string Value, string Message) ToWrite(OpenBgWindowIntent intent, OpenBgSetupValues values)
        {
            switch (intent?.Kind)
            {
                case OpenBgWindowIntentKind.SetEnabled:
                    return (true, OpenBgSetupParser.Find("enabled").RowKey, OpenBgSetupParser.Switch(intent.On), $"Open BG {(intent.On ? "on" : "off")}.");
                case OpenBgWindowIntentKind.SetMode when intent.Key is "serverpvp" or "perrealm":
                    return (true, OpenBgSetupParser.Find("mode").RowKey, intent.Key, $"Mode: {(intent.Key == "perrealm" ? "Per realm" : "Server PvP")}.");
                case OpenBgWindowIntentKind.CyclePeace:
                {
                    string next = PeaceCycle[(Array.IndexOf(PeaceCycle, values.ValueOf("peace")) + 1) % PeaceCycle.Length];
                    return (true, OpenBgSetupParser.Find("peace").RowKey, next == "auto" ? string.Empty : next, $"Landing peace: {next}.");
                }
                case OpenBgWindowIntentKind.ResetUnits when RealmOf(intent.Key) != eRealm.None:
                    return (true, OpenBattlegroundProperties.UnitsRowKey(RealmOf(intent.Key)), string.Empty, $"{intent.Key} uses the global setup again.");
                case OpenBgWindowIntentKind.Adjust when intent.Key == "drift":
                    return (true, OpenBgSetupParser.Find("drift").RowKey, OpenBgSetupParser.Switch(!values.Drift), $"Player drift: {(values.Drift ? "off" : "on")}.");
                case OpenBgWindowIntentKind.Adjust when intent.Key.StartsWith("preset:", StringComparison.Ordinal):
                {
                    string key = intent.Key["preset:".Length..];
                    Dictionary<string, int> mix = PresetCounts(values.Presets);
                    int count = Math.Clamp(mix.GetValueOrDefault(key) + Math.Sign(intent.Amount), 0, 8);
                    if (OpenBattlegroundArchetypes.Get(key) == null) return (false, null, null, $"Unknown preset {key}.");
                    mix[key] = count;
                    string text = string.Join(",", PresetKeys.Where(item => mix.GetValueOrDefault(item) > 0).Select(item => $"{item}:{mix[item]}"));
                    return (true, OpenBgSetupParser.Find("presets").RowKey, text, $"Preset {key}: {count}.");
                }
                case OpenBgWindowIntentKind.Adjust when intent.Key.StartsWith("units:", StringComparison.Ordinal):
                {
                    string[] parts = intent.Key.Split(':');
                    eRealm realm = parts.Length == 3 ? RealmOf(parts[1]) : eRealm.None;
                    OpenBgUnitKind? kind = parts.Length == 3 ? Kinds.Cast<OpenBgUnitKind?>().FirstOrDefault(item => OpenBattlegroundRoster.KindName(item.Value) == parts[2]) : null;
                    if (realm == eRealm.None || kind == null) return (false, null, null, "Unknown units row.");
                    OpenBgRealmTargets targets = values.ToSetup().TargetsFor(realm);
                    int current = targets.Count(kind.Value);
                    int next = Math.Clamp(current + Math.Sign(intent.Amount), 0, OpenBgRealmTargets.Max(kind.Value));
                    if (next == current) return (false, null, null, $"{parts[1]} {parts[2]}: already at {current}.");
                    return (true, OpenBattlegroundProperties.UnitsRowKey(realm), targets.With(kind.Value, next).Format(), $"{parts[1]} {parts[2]}: {current} -> {next}.");
                }
                case OpenBgWindowIntentKind.Adjust when OpenBgSetupParser.Find(intent.Key) is { IsSwitch: false, IsText: false, IsUnits: false } setting:
                {
                    int current = int.Parse(values.ValueOf(setting.Name), CultureInfo.InvariantCulture);
                    if (!Next(values, setting.Name, Math.Sign(intent.Amount), out int next))
                        return (false, null, null, $"{SettingLabel(setting.Name)}: cannot go further than {current}.");
                    return (true, setting.RowKey, next.ToString(CultureInfo.InvariantCulture), $"{SettingLabel(setting.Name)}: {current} -> {next}.");
                }
            }
            return (false, null, null, null);
        }

        // ---- helpers kept from the removed Custom8 open BG manager (OpenBgManagerScreen) ------------------------------

        private static Dictionary<string, int> PresetCounts(string presets)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach ((OpenBgArchetype archetype, int count) in OpenBattlegroundArchetypes.ParseMix(presets))
                result[archetype.Key] = Math.Min(8, result.GetValueOrDefault(archetype.Key) + count);
            return result;
        }

        public static eRealm RealmOf(string name) => name switch
        {
            "Albion" => eRealm.Albion,
            "Midgard" => eRealm.Midgard,
            "Hibernia" => eRealm.Hibernia,
            _ => eRealm.None,
        };

        private static string RealmHeader(string name, OpenBgRealmStatus summary) =>
            summary == null ? name : $"{name}: 8-man {summary.Groups8Present}/{summary.Groups8}, small {summary.SmallPresent}/{summary.Small}, " +
                $"solo {summary.SolosPresent}/{summary.Solos}, {summary.Bots} bots" + (summary.OwnSetup ? " (own setup)" : string.Empty);

        // Inside first, then outside, travelling and leaving; 8-man before small before solo; then by id.
        private static IEnumerable<IGrouping<int, OpenBgBotStatus>> OrderedUnits(IEnumerable<OpenBgBotStatus> bots) =>
            bots.GroupBy(bot => bot.Unit)
                .OrderBy(unit => StateRank(unit.GroupBy(bot => bot.State).OrderByDescending(group => group.Count()).First().Key))
                .ThenBy(unit => KindRank(unit.First().Kind)).ThenBy(unit => unit.Key);

        private static string LeaderOf(IEnumerable<OpenBgBotStatus> unit) =>
            unit.Select(bot => bot.Leader).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

        /// <summary>The unit column: "8-man #12", "small #40", "solo #51".</summary>
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

        private static int Count(IEnumerable<OpenBgBotStatus> bots, string state) => bots.Count(bot => bot.State == state);
        private static int StateRank(string state) => state switch { "inside" => 0, "outside" => 1, "travelling" => 2, _ => 3 };
        private static int KindRank(string kind) => kind switch { "group8" => 0, "small" => 1, _ => 2 };
    }
}
