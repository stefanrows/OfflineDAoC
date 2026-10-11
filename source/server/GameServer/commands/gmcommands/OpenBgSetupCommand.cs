using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using DOL.Database;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS.Commands
{
    /// <summary>
    /// /bgsetup (GM): the open BG's live state and setup (spec 2026-10-11 §7). Without arguments it opens the status
    /// window (page 1 live state, page 2 setup) and prints the setup to the chat; /bgsetup &lt;page&gt; opens another
    /// page; /bgsetup &lt;key&gt; &lt;value&gt; writes the open_bg_* server property row and refreshes the properties like
    /// /serverproperties refresh. The roster (and the director, stage 2) read the new value on their next tick.
    /// </summary>
    [CmdAttribute("&bgsetup", ePrivLevel.GM,
        "Open BG (Thidranki 252) live state and setup",
        "/bgsetup - live state window (page 1) and setup (page 2); the setup is also printed to the chat",
        "/bgsetup <page> - another page; pages 3+ continue the crew list",
        "/bgsetup <key> <value> - keys: enabled on|off, groups8 0-4, small 0-6, solos 0-12, max_bots 8-120, cycle_min 5-120, cycle_max 5-120, drift on|off")]
    public sealed class OpenBgSetupCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(OpenBgSetupCommandHandler));

        public void OnCommand(GameClient client, string[] args)
        {
            if (client?.Player == null) return;
            if (args.Length == 1)
            {
                ShowPage(client, 1);
                foreach (string line in OpenBgSetupPages.ChatLines(OpenBgSetupValues.Current()))
                    DisplayMessage(client, line);
                return;
            }
            if (args.Length == 2 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int page))
            {
                ShowPage(client, page);
                return;
            }
            if (args.Length == 2 && args[1].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                DisplaySyntax(client);
                return;
            }

            (bool ok, string key, string value, string error) = OpenBgSetupParser.Parse(args);
            if (!ok)
            {
                DisplayMessage(client, error);
                return;
            }
            try
            {
                WriteProperty(key, value);
            }
            catch (Exception exception)
            {
                Log.Error($"OPEN_BG_SETUP key={key} value={value} by={client.Player.Name} failed", exception);
                DisplayMessage(client, $"open BG setup: could not write {key}: {exception.Message}");
                return;
            }
            Log.Info($"OPEN_BG_SETUP key={key} value={value} by={client.Player.Name}");
            DisplayMessage(client, $"open BG setup: {OpenBgSetupParser.Find(key).Name} = {value}. Properties refreshed; the roster reads it on its next tick.");
            ShowPage(client, 2);
        }

        private static void ShowPage(GameClient client, int page)
        {
            IReadOnlyList<IReadOnlyList<string>> pages = OpenBgSetupPages.Pages(OpenBattlegroundStatus.Capture(), OpenBgSetupValues.Current());
            page = Math.Clamp(page, 1, pages.Count);
            string caption = page == 2 ? "open BG setup" : "open BG live";
            client.Out.SendCustomTextWindow($"{caption} {page}/{pages.Count}", pages[page - 1].ToArray());
        }

        /// <summary>Writes the row (pattern FrontierKeepBalanceUpdate) and refreshes every property like /serverproperties refresh.</summary>
        private static void WriteProperty(string rowKey, string value)
        {
            DbServerProperty row = DOLDB<DbServerProperty>.SelectObject(DB.Column("Key").IsEqualTo(rowKey));
            if (row == null)
            {
                ServerPropertyAttribute attribute = typeof(OpenBattlegroundProperties).GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Select(field => field.GetCustomAttribute<ServerPropertyAttribute>())
                    .First(candidate => candidate != null && candidate.Key == rowKey);
                row = new DbServerProperty
                {
                    Category = attribute.Category,
                    Key = rowKey,
                    Description = attribute.Description,
                    DefaultValue = attribute.DefaultValue.ToString(),
                    Value = value,
                };
                GameServer.Database.AddObject(row);
            }
            else
            {
                row.Value = value;
                GameServer.Database.SaveObject(row);
            }
            Properties.Refresh();
        }
    }

    /// <summary>One /bgsetup key: the short name, its server property row, the range and a one-line help.</summary>
    public sealed record OpenBgSetupKey(string Name, string RowKey, bool IsSwitch, int Min, int Max, string Help)
    {
        public string Range => IsSwitch ? "on|off" : $"{Min}-{Max}";
    }

    /// <summary>The raw open_bg_* values as the rows hold them (before the clamping at use).</summary>
    public sealed record OpenBgSetupValues(bool Enabled, int Groups8, int Small, int Solos, int MaxBots, int CycleMin, int CycleMax, bool Drift)
    {
        public static OpenBgSetupValues Current() => new(OpenBattlegroundProperties.OPEN_BG_ENABLED, OpenBattlegroundProperties.OPEN_BG_GROUPS8,
            OpenBattlegroundProperties.OPEN_BG_GROUPS_SMALL, OpenBattlegroundProperties.OPEN_BG_SOLOS, OpenBattlegroundProperties.OPEN_BG_MAX_BOTS,
            OpenBattlegroundProperties.OPEN_BG_CYCLE_MIN_MINUTES, OpenBattlegroundProperties.OPEN_BG_CYCLE_MAX_MINUTES,
            OpenBattlegroundProperties.OPEN_BG_PLAYER_DRIFT);

        public string ValueOf(string name) => name switch
        {
            "enabled" => OpenBgSetupParser.Switch(Enabled),
            "groups8" => Groups8.ToString(CultureInfo.InvariantCulture),
            "small" => Small.ToString(CultureInfo.InvariantCulture),
            "solos" => Solos.ToString(CultureInfo.InvariantCulture),
            "max_bots" => MaxBots.ToString(CultureInfo.InvariantCulture),
            "cycle_min" => CycleMin.ToString(CultureInfo.InvariantCulture),
            "cycle_max" => CycleMax.ToString(CultureInfo.InvariantCulture),
            "drift" => OpenBgSetupParser.Switch(Drift),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown open BG setup key."),
        };
    }

    /// <summary>Pure parser of /bgsetup &lt;key&gt; &lt;value&gt;. The args are the handler's: args[0] is the command word.</summary>
    public static class OpenBgSetupParser
    {
        public static readonly OpenBgSetupKey[] Keys =
        {
            new("enabled", "open_bg_enabled", true, 0, 1, "open BG on/off: porter line and bot crew"),
            new("groups8", "open_bg_groups8", false, 0, OpenBattlegroundProperties.Groups8Max, "full 8-man bot groups per realm"),
            new("small", "open_bg_groups_small", false, 0, OpenBattlegroundProperties.GroupsSmallMax, "small bot groups (2-4) per realm"),
            new("solos", "open_bg_solos", false, 0, OpenBattlegroundProperties.SolosMax, "solo roamers and stealthers per realm"),
            new("max_bots", "open_bg_max_bots", false, OpenBattlegroundProperties.MaxBotsMin, OpenBattlegroundProperties.MaxBotsMax, "most bots inside, all realms together"),
            new("cycle_min", "open_bg_cycle_min_minutes", false, OpenBattlegroundProperties.CycleMinutesMin, OpenBattlegroundProperties.CycleMinutesMax, "shortest quiet-push-fight-retreat cycle, game minutes"),
            new("cycle_max", "open_bg_cycle_max_minutes", false, OpenBattlegroundProperties.CycleMinutesMin, OpenBattlegroundProperties.CycleMinutesMax, "longest cycle, game minutes"),
            new("drift", "open_bg_player_drift", true, 0, 1, "roamers and solos prefer points within sight of a player"),
        };

        public const string Usage = "Use /bgsetup <key> <value>. Keys: enabled on|off, groups8 0-4, small 0-6, solos 0-12, " +
            "max_bots 8-120, cycle_min 5-120, cycle_max 5-120 (min not above max), drift on|off.";

        public static string Switch(bool on) => on ? "True" : "False";

        /// <summary>The key by its short name or its row key, case-insensitive; null when unknown.</summary>
        public static OpenBgSetupKey Find(string name) =>
            string.IsNullOrWhiteSpace(name) ? null : Keys.FirstOrDefault(key =>
                key.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) || key.RowKey.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Parses against the current cycle bounds (the clamped properties).</summary>
        public static (bool ok, string key, string value, string error) Parse(string[] args) =>
            Parse(args, OpenBattlegroundProperties.CycleMinMinutes, OpenBattlegroundProperties.CycleMaxMinutes);

        /// <summary>
        /// ok with the row key and the value to store ("True"/"False" or an integer), or an error line. A new
        /// cycle_min may not exceed the current maximum, a new cycle_max may not fall below the current minimum.
        /// </summary>
        public static (bool ok, string key, string value, string error) Parse(string[] args, int currentCycleMin, int currentCycleMax)
        {
            if (args == null || args.Length != 3) return (false, null, null, Usage);
            OpenBgSetupKey key = Find(args[1]);
            if (key == null) return (false, null, null, $"Unknown key '{args[1]}'. {Usage}");
            string text = args[2]?.Trim() ?? string.Empty;
            if (key.IsSwitch)
            {
                bool? on = text.ToLowerInvariant() switch
                {
                    "on" or "true" or "1" or "yes" => true,
                    "off" or "false" or "0" or "no" => false,
                    _ => null,
                };
                return on == null
                    ? (false, key.RowKey, null, $"{key.Name} takes on or off, not '{text}'.")
                    : (true, key.RowKey, Switch(on.Value), null);
            }
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                return (false, key.RowKey, null, $"{key.Name} takes a whole number {key.Range}, not '{text}'.");
            if (number < key.Min || number > key.Max)
                return (false, key.RowKey, null, $"{key.Name} must be {key.Range}; {number} is out of range.");
            if (key.Name == "cycle_min" && number > currentCycleMax)
                return (false, key.RowKey, null, $"cycle_min {number} is above cycle_max {currentCycleMax}; raise cycle_max first.");
            if (key.Name == "cycle_max" && number < currentCycleMin)
                return (false, key.RowKey, null, $"cycle_max {number} is below cycle_min {currentCycleMin}; lower cycle_min first.");
            return (true, key.RowKey, number.ToString(CultureInfo.InvariantCulture), null);
        }
    }

    /// <summary>
    /// Pure text of the /bgsetup window. Page 1 is the live state and as much of the crew as fits one detail packet,
    /// page 2 the setup, pages 3+ the rest of the crew. A native detail window carries at most 2048 bytes.
    /// </summary>
    public static class OpenBgSetupPages
    {
        public const int PageBudgetBytes = 1800;
        public const int LineWidth = 120;
        private static readonly string[] RealmOrder = { "Albion", "Midgard", "Hibernia" };

        /// <summary>What a line costs in the detail packet: a line byte and a length byte per 255 characters, plus the text.</summary>
        public static int LineCost(string line) => (line?.Length ?? 0) + 2 * (1 + (line?.Length ?? 0) / 255);

        public static IReadOnlyList<IReadOnlyList<string>> Pages(OpenBgStatusSnapshot status, OpenBgSetupValues values)
        {
            List<string> header = LiveHeader(status, values);
            List<string> crew = CrewLines(status);
            const string footerTemplate = "Page 1/99: /bgsetup 2 = setup, /bgsetup 3 = more crew. A snapshot; rerun to refresh.";
            var livePages = new List<List<string>>();
            var current = new List<string>(header);
            int used = header.Sum(LineCost) + LineCost(footerTemplate);
            foreach (string line in crew)
            {
                if (used + LineCost(line) > PageBudgetBytes && current.Count > 0)
                {
                    livePages.Add(current);
                    current = new List<string> { "Crew, continued:" };
                    used = LineCost(current[0]) + LineCost(footerTemplate);
                }
                current.Add(line);
                used += LineCost(line);
            }
            livePages.Add(current);

            int total = livePages.Count + 1;
            var pages = new List<IReadOnlyList<string>>();
            for (int index = 0; index < total; index++)
            {
                int number = index + 1;
                List<string> lines = number == 1 ? livePages[0] : number == 2 ? SetupLines(values) : livePages[number - 2];
                lines = new List<string>(lines)
                {
                    total > 2
                        ? $"Page {number}/{total}: /bgsetup 2 = setup, /bgsetup 3 = more crew. A snapshot; rerun to refresh."
                        : $"Page {number}/{total}: /bgsetup 1 = live, /bgsetup 2 = setup. A snapshot; rerun to refresh."
                };
                pages.Add(lines);
            }
            return pages;
        }

        public static List<string> LiveHeader(OpenBgStatusSnapshot status, OpenBgSetupValues values)
        {
            string state = !status.CampaignEnabled ? "closed (the battleground campaign is off)"
                : !status.OpenEnabled ? "off (enabled = False)"
                : !status.Ready ? "on, NOT ready: " + (status.NotReadyReason ?? "unknown")
                : "on and ready";
            string phase = "Phase: " + (string.IsNullOrWhiteSpace(status.Phase) ? OpenBgStatusSnapshot.PhaseNotAvailable : status.Phase);
            if (!string.IsNullOrWhiteSpace(status.Attacker)) phase += ", attacker " + status.Attacker;
            if (status.PhaseSecondsLeft > 0) phase += $", {status.PhaseSecondsLeft / 60}m {status.PhaseSecondsLeft % 60}s left";
            OpenBgBotStatus[] bots = status.Bots ?? Array.Empty<OpenBgBotStatus>();
            OpenBgPlayerStatus[] players = status.Players ?? Array.Empty<OpenBgPlayerStatus>();
            var lines = new List<string>
            {
                "open BG, Thidranki (region 252): " + state + ".",
                phase + ".",
                "Keep 11: " + (status.KeepOwner ?? "unknown") + ".",
                string.Format(CultureInfo.InvariantCulture, "World speed: achieved {0:0.0}x (effective {1}x); cycles and relief run in game time.",
                    status.AchievedSpeed, status.SelectedSpeed),
                $"Crew: {Count(bots, "inside")} inside, {Count(bots, "travelling")} travelling, {Count(bots, "leaving")} leaving, " +
                $"{Count(bots, "outside")} outside; cap {status.Cap}. Setup per realm: {values.Groups8}x8, {values.Small} small, {values.Solos} solos.",
                players.Length == 0 ? "Players inside: none."
                    : $"Players inside ({players.Length}): " + string.Join(", ", players.Select(player => $"{player.Name} {player.Level} {player.Realm}")) + ".",
                GuestLine(status.Guests),
            };
            return lines.Select(Fit).ToList();
        }

        /// <summary>The guest squads on a low human's level (spec 2026-10-11 §8), one entry per realm.</summary>
        public static string GuestLine(OpenBgGuestStatus[] guests) =>
            guests == null || guests.Length == 0 ? "Guest squads: none."
                : $"Guest squads ({guests.Length}): " + string.Join(", ", guests.Select(guest =>
                    $"{guest.Realm} level {guest.Level}, {guest.Alive}/{guest.Size} alive" + (string.IsNullOrEmpty(guest.Target) ? "" : $", hunting near {guest.Target}"))) + ".";

        /// <summary>Per realm a summary line, then one line per unit (wrapped): id, kind, leader, members with level and class.</summary>
        public static List<string> CrewLines(OpenBgStatusSnapshot status)
        {
            OpenBgBotStatus[] bots = status.Bots ?? Array.Empty<OpenBgBotStatus>();
            var lines = new List<string>();
            foreach (string realm in RealmOrder.Concat(bots.Select(bot => bot.Realm).Where(realm => !RealmOrder.Contains(realm)).Distinct()))
            {
                OpenBgBotStatus[] mine = bots.Where(bot => bot.Realm == realm).ToArray();
                var units = mine.GroupBy(bot => bot.Unit).Select(unit => (Id: unit.Key, Members: unit.ToArray(),
                        State: unit.GroupBy(bot => bot.State).OrderByDescending(group => group.Count()).First().Key)).ToArray();
                if (mine.Length == 0)
                {
                    lines.Add($"{realm}: no crew.");
                    continue;
                }
                string summary = $"{realm}: {Count(mine, "inside")} inside, {Count(mine, "travelling")} travelling";
                if (Count(mine, "leaving") > 0) summary += $", {Count(mine, "leaving")} leaving";
                if (Count(mine, "outside") > 0) summary += $", {Count(mine, "outside")} outside";
                summary += $"; units {units.Count(unit => unit.Members[0].Kind == "group8")} group8, " +
                    $"{units.Count(unit => unit.Members[0].Kind == "small")} small, {units.Count(unit => unit.Members[0].Kind == "solo")} solo.";
                lines.Add(summary);
                foreach (var unit in units.OrderBy(unit => StateRank(unit.State)).ThenBy(unit => KindRank(unit.Members[0].Kind)).ThenBy(unit => unit.Id))
                {
                    OpenBgBotStatus first = unit.Members[0];
                    string prefix = $"  #{unit.Id} {first.Kind}";
                    string leader = unit.Members.Select(bot => bot.Leader).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
                    if (first.Kind != "solo" && leader != null) prefix += ", leader " + leader;
                    if (unit.State != "inside") prefix += " [" + unit.State + "]";
                    IEnumerable<string> members = unit.Members.OrderBy(bot => bot.Name != leader).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(bot => $"{bot.Name} {bot.Level} {bot.ClassName}" + (bot.Alive ? string.Empty : " dead") +
                            (bot.State == unit.State ? string.Empty : " " + bot.State));
                    lines.AddRange(Wrap(prefix + ": ", members));
                }
            }
            return lines;
        }

        public static List<string> SetupLines(OpenBgSetupValues values)
        {
            var lines = new List<string> { "Change with /bgsetup <key> <value>. The roster reads it on its next tick (60 game s)." };
            int[] inUse =
            {
                0, OpenBattlegroundProperties.Clamped("open_bg_groups8", values.Groups8, 0, OpenBattlegroundProperties.Groups8Max),
                OpenBattlegroundProperties.Clamped("open_bg_groups_small", values.Small, 0, OpenBattlegroundProperties.GroupsSmallMax),
                OpenBattlegroundProperties.Clamped("open_bg_solos", values.Solos, 0, OpenBattlegroundProperties.SolosMax),
                OpenBattlegroundProperties.Clamped("open_bg_max_bots", values.MaxBots, OpenBattlegroundProperties.MaxBotsMin, OpenBattlegroundProperties.MaxBotsMax),
                OpenBattlegroundProperties.Clamped("open_bg_cycle_min_minutes", values.CycleMin, OpenBattlegroundProperties.CycleMinutesMin, OpenBattlegroundProperties.CycleMinutesMax),
                0, 0,
            };
            inUse[6] = OpenBattlegroundProperties.Clamped("open_bg_cycle_max_minutes", values.CycleMax, inUse[5], OpenBattlegroundProperties.CycleMinutesMax);
            for (int index = 0; index < OpenBgSetupParser.Keys.Length; index++)
            {
                OpenBgSetupKey key = OpenBgSetupParser.Keys[index];
                string value = values.ValueOf(key.Name);
                string line = $"{key.Name} = {value} ({key.Range}): {key.Help}.";
                if (!key.IsSwitch && inUse[index].ToString(CultureInfo.InvariantCulture) != value) line += $" In use: {inUse[index]}.";
                lines.Add(line);
            }
            lines.Add("Rows: server properties open_bg_* (category pvp). The launcher's open BG tab edits them while the server is stopped.");
            return lines.Select(Fit).ToList();
        }

        /// <summary>The setup as chat lines: one per key with its range.</summary>
        public static List<string> ChatLines(OpenBgSetupValues values) =>
            new[] { "open BG setup (/bgsetup <key> <value>):" }
                .Concat(OpenBgSetupParser.Keys.Select(key => $"  {key.Name} = {values.ValueOf(key.Name)} ({key.Range})")).ToList();

        private static int Count(IEnumerable<OpenBgBotStatus> bots, string state) => bots.Count(bot => bot.State == state);
        private static int StateRank(string state) => state switch { "inside" => 0, "outside" => 1, "travelling" => 2, _ => 3 };
        private static int KindRank(string kind) => kind switch { "group8" => 0, "small" => 1, _ => 2 };
        private static string Fit(string line) => line.Length <= 250 ? line : line[..247] + "...";

        private static IEnumerable<string> Wrap(string prefix, IEnumerable<string> items)
        {
            var lines = new List<string>();
            string current = prefix;
            bool empty = true;
            foreach (string item in items)
            {
                string next = empty ? current + item : current + ", " + item;
                if (!empty && next.Length > LineWidth)
                {
                    lines.Add(current + ",");
                    current = "      " + item;
                }
                else current = next;
                empty = false;
            }
            lines.Add(current);
            return lines;
        }
    }
}
