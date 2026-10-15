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
    /// /bgsetup (every player, owner decision 2026-10-11 14:50; alias /openbg): the open BG's manager window, the same
    /// Custom8 window as the Companion Manager (<see cref="OpenBgManager"/>), with the live crew, the per-realm units and
    /// the setup (spec 2026-10-11 §7). /bgsetup text [page] keeps the earlier text pages for a client without that
    /// window; /bgsetup &lt;key&gt; &lt;value&gt; writes the open_bg_* server property row and refreshes the properties like
    /// /serverproperties refresh; /bgsetup relieve &lt;unit&gt; relieves a unit by hand. The roster and the director read
    /// a new value on their next tick.
    /// </summary>
    [CmdAttribute("&bgsetup", new[] { "&openbg" }, ePrivLevel.Player,
        "Open BG (Thidranki 252) manager: live crew, units per realm and setup",
        "/bgsetup - opens the open BG window: live crew per realm, + and - units per realm, setup",
        "/bgsetup text [page] - the same as text pages (page 1 live, page 2 setup, 3+ more crew)",
        "/bgsetup <key> <value> - keys: enabled on|off, groups8 0-4, small 0-6, solos 0-12, max_bots 8-120, cycle_min 5-120, cycle_max 5-120, band_min 1-50, band_max 1-50, drift on|off",
        "/bgsetup mode serverpvp|perrealm - the friend rule: Server PvP (Camlann rule, mixed crews) or Per realm (same realm is a friend in region 252)",
        "/bgsetup peace all|realm|auto - whose landing peace holds at a landing circle (auto = by mode)",
        "/bgsetup presets <list>|off - group presets, e.g. std8:2,caster8:1,small:2; matched by unit size, same-size units cycle through the listed archetypes by count, the group counts still decide how many units exist (off = the group counts as before)",
        "/bgsetup albion|midgard|hibernia <8-man/small/solos> - a realm's own crew, e.g. 1/2/4; global = the groups8/small/solos values",
        "/bgsetup relieve <unit> - that unit leaves within a game minute; the roster forms a replacement if the setup still wants one")]
    public sealed class OpenBgSetupCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null) return;
            if (args.Length == 1)
            {
                OpenBgManager.Open(player);
                return;
            }
            if (args[1].Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                int textPage = args.Length > 2 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : 1;
                ShowPage(client, textPage);
                if (textPage <= 1)
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
            if (args[1].Equals("relieve", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length != 3 || !int.TryParse(args[2].TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int unit))
                {
                    DisplayMessage(client, "Use /bgsetup relieve <unit>, e.g. /bgsetup relieve 12. The unit numbers are in the window and in /bgsetup text.");
                    return;
                }
                OpenBgSetupStore.Relieve(unit, player.Name, out string relieveMessage);
                DisplayMessage(client, relieveMessage);
                OpenBgManager.RefreshIfOpen(player, relieveMessage);
                return;
            }

            (bool ok, string key, string value, string error) = OpenBgSetupParser.Parse(args);
            if (!ok)
            {
                DisplayMessage(client, error);
                return;
            }
            if (!OpenBgSetupStore.TryWrite(key, value, player.Name, out string writeError))
            {
                DisplayMessage(client, $"open BG setup: could not write {key}: {writeError}");
                return;
            }
            OpenBgSetupKey written = OpenBgSetupParser.Find(key);
            string shown = value.Length > 0 ? value : written.IsUnits ? "global" : written.Name == "peace" ? "auto" : written.Name == "presets" ? "off" : value;
            string message = $"open BG setup: {written.Name} = {shown}. Properties refreshed; the roster reads it on its next tick.";
            if (written.Name is "band_min" or "band_max" or "enabled" or "max_bots")
            {
                string note = OpenBgSetupPages.GothwaiteReserveNote(GothwaitePvpMode.IsActive, GothwaitePvpOpenBgReserve.Set.Count);
                if (note.Length > 0) message += " " + note;
            }
            DisplayMessage(client, message);
            if (!OpenBgManager.RefreshIfOpen(player, message))
                ShowPage(client, 2);
        }

        private static void ShowPage(GameClient client, int page)
        {
            IReadOnlyList<IReadOnlyList<string>> pages = OpenBgSetupPages.Pages(OpenBattlegroundStatus.Capture(), OpenBgSetupValues.Current());
            page = Math.Clamp(page, 1, pages.Count);
            string caption = page == 2 ? "open BG setup" : "open BG live";
            client.Out.SendCustomTextWindow($"{caption} {page}/{pages.Count}", pages[page - 1].ToArray());
        }
    }

    /// <summary>Writes the open BG setup rows and queues hand reliefs for the command and the window alike.</summary>
    public static class OpenBgSetupStore
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(OpenBgSetupStore));

        /// <summary>Writes the row, logs <c>OPEN_BG_SETUP</c> and refreshes the properties; false with the reason on a database error.</summary>
        public static bool TryWrite(string rowKey, string value, string by, out string error)
        {
            error = null;
            try
            {
                WriteProperty(rowKey, value ?? string.Empty);
            }
            catch (Exception exception)
            {
                Log.Error($"OPEN_BG_SETUP key={rowKey} value={value} by={by} failed", exception);
                error = exception.Message;
                return false;
            }
            Log.Info($"OPEN_BG_SETUP key={rowKey} value={value} by={by}");
            return true;
        }

        /// <summary>Queues a hand relief (<see cref="OpenBattlegroundRoster.RequestRelief"/>) and words the result.</summary>
        public static bool Relieve(int unit, string by, out string message)
        {
            if (!OpenBattlegroundRoster.RequestRelief(unit))
            {
                message = $"Unit #{unit} is not in the open BG crew (it may have left already).";
                return false;
            }
            Log.Info($"OPEN_BG_RELIEF_REQUESTED unit={unit} by={by}");
            message = ReliefMessage(unit);
            return true;
        }

        public static string ReliefMessage(int unit) =>
            $"Unit #{unit} leaves within a minute; its bots may return after the short cooldown.";

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

    /// <summary>
    /// One /bgsetup key: the short name, its server property row, the range and a one-line help. <paramref name="IsUnits"/>
    /// marks a realm's own crew row (text "8-man/small/solos", empty for the global targets).
    /// </summary>
    public sealed record OpenBgSetupKey(string Name, string RowKey, bool IsSwitch, int Min, int Max, string Help, bool IsUnits = false, string Choices = null)
    {
        /// <summary>A text key (mode, peace, presets): its allowed values instead of a number range.</summary>
        public bool IsText => Choices != null;

        public string Range => IsSwitch ? "on|off" : IsUnits ? "8-man/small/solos or global" : Choices ?? $"{Min}-{Max}";
    }

    /// <summary>The raw open_bg_* values as the rows hold them (before the clamping at use); a realm's units text is "" for the global targets.</summary>
    public sealed record OpenBgSetupValues(bool Enabled, int Groups8, int Small, int Solos, int MaxBots, int CycleMin, int CycleMax, bool Drift,
        string AlbionUnits = "", string MidgardUnits = "", string HiberniaUnits = "", string Mode = "serverpvp", string Peace = "", string Presets = "",
        int BandMin = OpenBattlegroundProperties.DefaultBandMin, int BandMax = OpenBattlegroundProperties.DefaultBandMax)
    {
        public static OpenBgSetupValues Current() => new(OpenBattlegroundProperties.OPEN_BG_ENABLED, OpenBattlegroundProperties.OPEN_BG_GROUPS8,
            OpenBattlegroundProperties.OPEN_BG_GROUPS_SMALL, OpenBattlegroundProperties.OPEN_BG_SOLOS, OpenBattlegroundProperties.OPEN_BG_MAX_BOTS,
            OpenBattlegroundProperties.OPEN_BG_CYCLE_MIN_MINUTES, OpenBattlegroundProperties.OPEN_BG_CYCLE_MAX_MINUTES,
            OpenBattlegroundProperties.OPEN_BG_PLAYER_DRIFT, OpenBattlegroundProperties.UnitsText(eRealm.Albion),
            OpenBattlegroundProperties.UnitsText(eRealm.Midgard), OpenBattlegroundProperties.UnitsText(eRealm.Hibernia),
            OpenBattlegroundProperties.Mode, OpenBattlegroundProperties.LandingPeace, OpenBattlegroundProperties.Presets,
            OpenBattlegroundProperties.OPEN_BG_BAND_MIN, OpenBattlegroundProperties.OPEN_BG_BAND_MAX);

        /// <summary>A realm's raw units text ("" for the global targets or another realm).</summary>
        public string UnitsOf(eRealm realm) => (realm switch
        {
            eRealm.Albion => AlbionUnits,
            eRealm.Midgard => MidgardUnits,
            eRealm.Hibernia => HiberniaUnits,
            _ => null,
        }) ?? string.Empty;

        /// <summary>The setup the roster plans with: the clamped globals and each realm's readable own targets.</summary>
        public OpenBgSetup ToSetup() =>
            new(Math.Clamp(Groups8, 0, OpenBattlegroundProperties.Groups8Max), Math.Clamp(Small, 0, OpenBattlegroundProperties.GroupsSmallMax),
                Math.Clamp(Solos, 0, OpenBattlegroundProperties.SolosMax),
                Math.Clamp(MaxBots, OpenBattlegroundProperties.MaxBotsMin, OpenBattlegroundProperties.MaxBotsMax))
            {
                Albion = OpenBgRealmTargets.TryParse(AlbionUnits, out OpenBgRealmTargets albion) ? albion : null,
                Midgard = OpenBgRealmTargets.TryParse(MidgardUnits, out OpenBgRealmTargets midgard) ? midgard : null,
                Hibernia = OpenBgRealmTargets.TryParse(HiberniaUnits, out OpenBgRealmTargets hibernia) ? hibernia : null,
            };

        public string ValueOf(string name) => name switch
        {
            "enabled" => OpenBgSetupParser.Switch(Enabled),
            "groups8" => Groups8.ToString(CultureInfo.InvariantCulture),
            "small" => Small.ToString(CultureInfo.InvariantCulture),
            "solos" => Solos.ToString(CultureInfo.InvariantCulture),
            "max_bots" => MaxBots.ToString(CultureInfo.InvariantCulture),
            "cycle_min" => CycleMin.ToString(CultureInfo.InvariantCulture),
            "cycle_max" => CycleMax.ToString(CultureInfo.InvariantCulture),
            "band_min" => BandMin.ToString(CultureInfo.InvariantCulture),
            "band_max" => BandMax.ToString(CultureInfo.InvariantCulture),
            "drift" => OpenBgSetupParser.Switch(Drift),
            "albion" => UnitsShown(AlbionUnits),
            "midgard" => UnitsShown(MidgardUnits),
            "hibernia" => UnitsShown(HiberniaUnits),
            "mode" => OpenBattlegroundMode.ParseMode(Mode) == OpenBgMode.PerRealm ? "perrealm" : "serverpvp",
            "peace" => string.IsNullOrWhiteSpace(Peace) ? "auto" : Peace.Trim().ToLowerInvariant(),
            "presets" => string.IsNullOrWhiteSpace(Presets) ? "off" : Presets.Trim(),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown open BG setup key."),
        };

        private static string UnitsShown(string text) => string.IsNullOrWhiteSpace(text) ? "global" : text.Trim();
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
            new("band_min", "open_bg_band_min", false, OpenBattlegroundProperties.BandLevelMin, OpenBattlegroundProperties.BandLevelMax, "lowest crew bot level"),
            new("band_max", "open_bg_band_max", false, OpenBattlegroundProperties.BandLevelMin, OpenBattlegroundProperties.BandLevelMax, "highest crew bot level"),
            new("drift", "open_bg_player_drift", true, 0, 1, "roamers and solos prefer points within sight of a player"),
            new("albion", "open_bg_units_albion", false, 0, 0, "Albion's own crew; global uses groups8/small/solos", IsUnits: true),
            new("midgard", "open_bg_units_midgard", false, 0, 0, "Midgard's own crew; global uses groups8/small/solos", IsUnits: true),
            new("hibernia", "open_bg_units_hibernia", false, 0, 0, "Hibernia's own crew; global uses groups8/small/solos", IsUnits: true),
            new("mode", "open_bg_mode", false, 0, 0, "friend rule: serverpvp = group, guild and battlegroup only, mixed crews; perrealm = the same realm is a friend in region 252", Choices: "serverpvp|perrealm"),
            new("peace", "open_bg_landing_peace", false, 0, 0, "whose landing peace holds at a landing circle: all realms, only the landing's own realm, or auto = by mode", Choices: "all|realm|auto"),
            new("presets", "open_bg_presets", false, 0, 0, "group presets as name:count, e.g. std8:2,caster8:1,small:2 (std8 caster8 raid8 stealth4 small duo); matched by unit size, same-size units cycle through the listed archetypes by count, the unit targets still decide how many units exist; off = the group counts as before", Choices: "list|off"),
        };

        public const string Usage = "Use /bgsetup <key> <value>. Keys: enabled on|off, groups8 0-4, small 0-6, solos 0-12, " +
            "max_bots 8-120, cycle_min 5-120, cycle_max 5-120 (min not above max), band_min 1-50, band_max 1-50 (min not above max), drift on|off, " +
            "albion|midgard|hibernia 8-man/small/solos (e.g. 1/2/4) or global, mode serverpvp|perrealm, peace all|realm|auto, " +
            "presets name:count list (e.g. std8:2,caster8:1; same-size units cycle through the archetypes by count) or off.";

        /// <summary>The words that put a realm back on the global targets.</summary>
        public static bool IsGlobalWord(string text) =>
            string.IsNullOrWhiteSpace(text) || text.Trim().ToLowerInvariant() is "global" or "default" or "none" or "-" or "off";

        public static string Switch(bool on) => on ? "True" : "False";

        /// <summary>The key by its short name or its row key, case-insensitive; null when unknown.</summary>
        public static OpenBgSetupKey Find(string name) =>
            string.IsNullOrWhiteSpace(name) ? null : Keys.FirstOrDefault(key =>
                key.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) || key.RowKey.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Parses against the current cycle bounds (the clamped properties).</summary>
        public static (bool ok, string key, string value, string error) Parse(string[] args) =>
            Parse(args, OpenBattlegroundProperties.CycleMinMinutes, OpenBattlegroundProperties.CycleMaxMinutes,
                OpenBattlegroundProperties.BandMin, OpenBattlegroundProperties.BandMax);

        // mode, peace and presets: a rejected value returns no row, so the stored value stays as it was.
        private static (bool ok, string key, string value, string error) ParseText(OpenBgSetupKey key, string text)
        {
            string word = text.ToLowerInvariant();
            switch (key.Name)
            {
                case "mode" when word is "serverpvp" or "perrealm":
                    return (true, key.RowKey, word, null);
                case "peace" when word is "all" or "realm":
                    return (true, key.RowKey, word, null);
                case "peace" when word == "auto":
                    return (true, key.RowKey, string.Empty, null);
                case "presets" when word == "off":
                    return (true, key.RowKey, string.Empty, null);
                case "presets" when OpenBattlegroundArchetypes.ParseMix(text).Count > 0:
                    return (true, key.RowKey, text, null);
            }
            string expected = key.Name == "presets" ? "a list of name:count such as std8:2,caster8:1 (std8 caster8 raid8 stealth4 small duo), or off" : key.Range.Replace("|", " or ");
            return (false, key.RowKey, null, $"{key.Name} takes {expected}, not '{text}'. See /bgsetup help.");
        }

        /// <summary>
        /// ok with the row key and the value to store ("True"/"False" or an integer), or an error line. A new
        /// cycle_min may not exceed the current maximum, a new cycle_max may not fall below the current minimum.
        /// </summary>
        public static (bool ok, string key, string value, string error) Parse(string[] args, int currentCycleMin, int currentCycleMax,
            int currentBandMin = OpenBattlegroundProperties.DefaultBandMin, int currentBandMax = OpenBattlegroundProperties.DefaultBandMax)
        {
            if (args == null || args.Length < 3) return (false, null, null, Usage);
            OpenBgSetupKey key = Find(args[1]);
            if (key == null) return (false, null, null, $"Unknown key '{args[1]}'. {Usage}");
            // Only a presets list may be typed with spaces after its commas.
            if (args.Length != 3 && key.Name != "presets") return (false, null, null, Usage);
            string text = (args.Length == 3 ? args[2] : string.Join(" ", args.Skip(2)))?.Trim() ?? string.Empty;
            if (key.IsText) return ParseText(key, text);
            if (key.IsUnits)
            {
                if (IsGlobalWord(text)) return (true, key.RowKey, string.Empty, null);
                string[] parts = text.Split('/');
                var values = new int[3];
                bool numbers = parts.Length == 3;
                for (int index = 0; numbers && index < 3; index++)
                    numbers = int.TryParse(parts[index].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out values[index]);
                if (!numbers)
                    return (false, key.RowKey, null, $"{key.Name} takes 8-man/small/solos such as 1/2/4, or global; not '{text}'.");
                if (values[0] is < 0 or > OpenBattlegroundProperties.Groups8Max || values[1] is < 0 or > OpenBattlegroundProperties.GroupsSmallMax ||
                    values[2] is < 0 or > OpenBattlegroundProperties.SolosMax)
                    return (false, key.RowKey, null, $"{key.Name} must be 8-man 0-{OpenBattlegroundProperties.Groups8Max}, small " +
                        $"0-{OpenBattlegroundProperties.GroupsSmallMax}, solos 0-{OpenBattlegroundProperties.SolosMax}; '{text}' is out of range.");
                return (true, key.RowKey, new OpenBgRealmTargets(values[0], values[1], values[2]).Format(), null);
            }
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
            if (key.Name == "band_min" && number > currentBandMax)
                return (false, key.RowKey, null, $"band_min {number} is above band_max {currentBandMax}; raise band_max first.");
            if (key.Name == "band_max" && number < currentBandMin)
                return (false, key.RowKey, null, $"band_max {number} is below band_min {currentBandMin}; lower band_min first.");
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

        /// <summary>With the Gothwaite mode on the open BG reserve is fixed at server start; empty when the mode is off.</summary>
        public static string GothwaiteReserveNote(bool modeActive, int reserveCount) => modeActive
            ? $"Gothwaite mode: the open BG reserve is fixed at server start (reserve {reserveCount}); restart the server to apply."
            : string.Empty;

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
                $"{Count(bots, "outside")} outside; cap {status.Cap}. Setup per realm: {values.Groups8}x8, {values.Small} small, {values.Solos} solos." +
                OwnSetups(values),
                players.Length == 0 ? "Players inside: none."
                    : $"Players inside ({players.Length}): " + string.Join(", ", players.Select(player => $"{player.Name} {player.Level} {player.Realm}")) + ".",
                GuestLine(status.Guests),
            };
            return lines.Select(Fit).ToList();
        }

        // " Own setup: Albion 1/2/4." for the realms whose open_bg_units_* row overrides the global targets.
        private static string OwnSetups(OpenBgSetupValues values)
        {
            string[] own = new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }
                .Where(realm => OpenBgRealmTargets.TryParse(values.UnitsOf(realm), out _))
                .Select(realm => $"{OpenBattlegroundRoster.RealmName(realm)} {values.ToSetup().TargetsFor(realm).Format()}").ToArray();
            return own.Length == 0 ? string.Empty : " Own setup: " + string.Join(", ", own) + ".";
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
            foreach (OpenBgSetupKey key in OpenBgSetupParser.Keys)
            {
                string value = values.ValueOf(key.Name);
                string line = $"{key.Name} = {value} ({key.Range}): {key.Help}.";
                string inUse = InUse(key, values);
                if (inUse != null && inUse != value) line += $" In use: {inUse}.";
                lines.Add(line);
            }
            lines.Add("Rows: server properties open_bg_* (category pvp). The launcher's open BG tab edits them while the server is stopped.");
            return lines.Select(Fit).ToList();
        }

        // The clamped value the roster uses for a number or a realm's units; null for a switch.
        private static string InUse(OpenBgSetupKey key, OpenBgSetupValues values)
        {
            int cycleMin = OpenBattlegroundProperties.Clamped("open_bg_cycle_min_minutes", values.CycleMin, OpenBattlegroundProperties.CycleMinutesMin,
                OpenBattlegroundProperties.CycleMinutesMax);
            int? number = key.Name switch
            {
                "groups8" => OpenBattlegroundProperties.Clamped("open_bg_groups8", values.Groups8, 0, OpenBattlegroundProperties.Groups8Max),
                "small" => OpenBattlegroundProperties.Clamped("open_bg_groups_small", values.Small, 0, OpenBattlegroundProperties.GroupsSmallMax),
                "solos" => OpenBattlegroundProperties.Clamped("open_bg_solos", values.Solos, 0, OpenBattlegroundProperties.SolosMax),
                "max_bots" => OpenBattlegroundProperties.Clamped("open_bg_max_bots", values.MaxBots, OpenBattlegroundProperties.MaxBotsMin,
                    OpenBattlegroundProperties.MaxBotsMax),
                "band_min" => OpenBattlegroundProperties.ResolveBand(values.BandMin, values.BandMax).Min,
                "band_max" => OpenBattlegroundProperties.ResolveBand(values.BandMin, values.BandMax).Max,
                "cycle_min" => cycleMin,
                "cycle_max" => OpenBattlegroundProperties.Clamped("open_bg_cycle_max_minutes", values.CycleMax, cycleMin, OpenBattlegroundProperties.CycleMinutesMax),
                _ => null,
            };
            if (number != null) return number.Value.ToString(CultureInfo.InvariantCulture);
            if (!key.IsUnits) return null;
            string text = values.ValueOf(key.Name);
            if (text == "global") return "global";
            return OpenBgRealmTargets.TryParse(text, out OpenBgRealmTargets targets) ? targets.Format() : "global (unreadable text)";
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
