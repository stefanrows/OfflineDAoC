using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.GS.Commands
{
    /// <summary>The two pages of the OPEN BG window (Custom5): the Overview (who stands where) and the Setup.</summary>
    public enum OpenBgWindowPage { Overview, Setup }

    public enum OpenBgWindowIntentKind
    {
        None, SetEnabled, SetMode, CyclePeace, Adjust, ResetUnits, Relieve, Tab, Scroll, Refresh, Close, Fold, Select, OpenAll, FoldAll, Jump, Attack,
    }

    /// <summary>
    /// What a click does, decided when the window is drawn. SetEnabled: <see cref="On"/>. SetMode: <see cref="Key"/> is
    /// serverpvp or perrealm. Adjust: <see cref="Key"/> is a /bgsetup key, "preset:&lt;archetype&gt;" or "units:&lt;Realm&gt;:&lt;kind&gt;",
    /// <see cref="Amount"/> is -1 or +1 (times the key's step). ResetUnits: <see cref="Key"/> is the realm. Relieve:
    /// <see cref="Unit"/>, <see cref="Key"/> the unit's title, <see cref="On"/> the confirming second click. Tab:
    /// <see cref="Key"/> is Overview or Setup. Scroll: <see cref="Amount"/> rows. Fold: <see cref="Key"/> is the block
    /// (unit "u:31", solo block "s:Albion"). Select: <see cref="Key"/> is the row key. Jump: <see cref="Key"/> is the realm.
    /// Attack: <see cref="Key"/> is the realm ordered to attack the keep (a normal raid).
    /// </summary>
    public sealed record OpenBgWindowIntent(OpenBgWindowIntentKind Kind, string Key = "", int Amount = 0, bool On = false, int Unit = 0)
    {
        /// <summary>The identity of the click for the view signature.</summary>
        public string Id => $"{Kind}:{Key}:{Amount}:{On}:{Unit}";
    }

    /// <summary>The three state colours of the row's state/value column (slots 9-11).</summary>
    public enum OpenBgTone { Field, Blood, Ash }

    /// <summary>
    /// One row of the window as the screen decides it; each text goes to one slot of open_bg_row_slots() (see
    /// <see cref="OpenBgWindowView.ToLabels"/>). Texts are clipped to their slots by <see cref="OpenBgWindowScreen.Build"/>.
    /// <see cref="Key"/> identifies what the row stands for ("r:Albion", "u:31", "m:31:Name", "k:max_bots", ...).
    /// </summary>
    public sealed record OpenBgWindowRow
    {
        public string Key { get; init; } = string.Empty;
        /// <summary>Slot 0 (gold): "+" folded block, "-" opened block, "*" leader, ">" selected row.</summary>
        public string Mark { get; init; }
        /// <summary>Slot 1 (gold): a Setup section.</summary>
        public string Title { get; init; }
        /// <summary>Slot 2 (Ink): a unit title, a Setup setting name or a notice.</summary>
        public string Text { get; init; }
        /// <summary>Slots 3-5: the name in its realm colour; <see cref="Indent"/> puts two spaces before it (members).</summary>
        public eRealm Realm { get; init; }
        public string Name { get; init; }
        public bool Indent { get; init; }
        public string Level { get; init; }
        public string Class { get; init; }
        /// <summary>Slot 8 (Ash): "6 bots", a range.</summary>
        public string Ash { get; init; }
        /// <summary>Slots 9-11: state or value in the colour of <see cref="Tone"/>.</summary>
        public string State { get; init; }
        public OpenBgTone Tone { get; init; }
        /// <summary>Slots 12/13: bar ground (Dim) and value (Field), "|" or "=" glyphs.</summary>
        public string BarDim { get; init; }
        public string BarField { get; init; }
        /// <summary>Slot 14 (Ash, Overview only): "Raid", "12 min".</summary>
        public string Note { get; init; }
        public string LinkText { get; init; }
        public OpenBgWindowIntent Link { get; init; }
        public string MinusText { get; init; }
        public OpenBgWindowIntent Minus { get; init; }
        public string PlusText { get; init; }
        public OpenBgWindowIntent Plus { get; init; }
        /// <summary>The fold control (the mark) and the name click; on a block header both fold.</summary>
        public OpenBgWindowIntent Fold { get; init; }
        public OpenBgWindowIntent Select { get; init; }
        /// <summary>The hint line while this row is selected.</summary>
        public string Help { get; init; }
        /// <summary>The block a header row folds (null for other rows).</summary>
        public string Block { get; init; }
    }

    /// <summary>The fold, selection, jump and relieve state of one viewer's window, as the session keeps it.</summary>
    public sealed record OpenBgWindowState(IReadOnlySet<string> Opened = null, string Selected = null, int PendingRelieve = 0, string JumpTo = null);

    /// <summary>The drawn window: every server text, the visible rows (at most 18) and the paging figures.</summary>
    public sealed class OpenBgWindowView
    {
        public OpenBgWindowPage Page { get; init; }
        public string Status { get; init; } = string.Empty;
        public string Banner { get; init; } = string.Empty;
        public string Phase { get; init; } = string.Empty;
        public string Hint { get; init; } = string.Empty;
        public string Reply { get; init; } = string.Empty;
        public string PageIndicator { get; init; } = string.Empty;
        public string[] Heads { get; init; } = new string[BgHeadLabels];
        /// <summary>The crest band: three realms x five labels (name, detail, bar ground, bar value, count).</summary>
        public string[] Realms { get; init; } = new string[3 * BgRealmStride];
        public IReadOnlyList<OpenBgWindowRow> Rows { get; init; } = Array.Empty<OpenBgWindowRow>();
        public int TotalRows { get; init; }
        /// <summary>The offset after clamping (and after a crest jump).</summary>
        public int Offset { get; init; }
        /// <summary>Every block (unit or solo block) the Overview has now, for Open all and for pruning the fold set.</summary>
        public IReadOnlyList<string> Blocks { get; init; } = Array.Empty<string>();

        /// <summary>The labels for indexes 620..984 in the order of open_bg_window() (see OpenBgWindowScreen).</summary>
        public string[] ToLabels()
        {
            var labels = Enumerable.Repeat(string.Empty, BgLabelEnd - BgLabelBase).ToArray();
            void Set(int index, string text) => labels[index - BgLabelBase] = text ?? string.Empty;
            Set(BgLabelStatus, Status);
            bool overview = Page == OpenBgWindowPage.Overview;
            // 621 Overview inactive (Ash), 622 Overview active (Gold), 623 Setup inactive, 624 Setup active
            Set(BgLabelTabBase, overview ? null : OpenBgWindowScreen.TabOverview);
            Set(BgLabelTabBase + 1, overview ? OpenBgWindowScreen.ActiveTab(OpenBgWindowScreen.TabOverview) : null);
            Set(BgLabelTabBase + 2, overview ? OpenBgWindowScreen.TabSetup : null);
            Set(BgLabelTabBase + 3, overview ? null : OpenBgWindowScreen.ActiveTab(OpenBgWindowScreen.TabSetup));
            Set(BgLabelPage, PageIndicator);
            Set(BgLabelBanner, Banner);
            Set(BgLabelPhase, Phase);
            Set(BgLabelHint, Hint);
            Set(BgLabelReply, Reply);
            Set(BgLabelOpenAll, overview ? "[Open all]" : null);
            Set(BgLabelFoldAll, overview ? "[Fold all]" : null);
            for (int head = 0; head < BgHeadLabels && head < Heads.Length; head++)
                Set(BgLabelHeadBase + head, Heads[head]);
            for (int index = 0; index < 3 * BgRealmStride && index < Realms.Length; index++)
                Set(BgLabelRealmBase + index, Realms[index]);
            for (int index = 0; index < Rows.Count && index < BgRowCount; index++)
            {
                OpenBgWindowRow row = Rows[index];
                void Put(int slot, string text)
                {
                    if (!string.IsNullOrEmpty(text)) labels[OpenBgWindowScreen.RowLabelIndex(index, slot)] = text;
                }
                Put(BgSlotMark, row.Mark);
                Put(BgSlotTitle, row.Title);
                Put(BgSlotText, row.Text);
                if (!string.IsNullOrEmpty(row.Name))
                    Put(BgSlotNameBase + OpenBgWindowScreen.RealmIndex(row.Realm), (row.Indent ? OpenBgWindowScreen.MemberIndent : string.Empty) + row.Name);
                Put(BgSlotLevel, row.Level);
                Put(BgSlotClass, row.Class);
                Put(BgSlotAsh, row.Ash);
                Put(row.Tone switch { OpenBgTone.Field => BgSlotStateField, OpenBgTone.Blood => BgSlotStateBlood, _ => BgSlotStateAsh }, row.State);
                Put(BgSlotBarDim, row.BarDim);
                Put(BgSlotBarField, row.BarField);
                Put(BgSlotNote, row.Note);
                Put(BgSlotLink, row.LinkText);
                Put(BgSlotMinus, row.MinusText);
                Put(BgSlotPlus, row.PlusText);
            }
            return labels;
        }
    }

    /// <summary>
    /// Pure drawing of the OPEN BG window (Custom5) "Kriegstafel" from a status snapshot, the setup values and the viewer's
    /// fold/selection state. Label order is the contract with open_bg_window() / open_bg_row_slots() in
    /// build_companion_manager_client.py: 620 status, 621-624 tabs (Overview inactive/active, Setup inactive/active), 625
    /// page, 626 banner (title font), 627 phase, 628 hint, 629 reply, 630/631 [Open all]/[Fold all] (Overview only), 633-956
    /// 18 rows x 18 slots, 957-962 column heads, 963-977 crest band (3 realms x 5). No rights check: every viewer gets every
    /// intent (owner decision 2026-10-16). Applying an intent is the runtime's job (<see cref="ToWrite"/> words the write).
    /// </summary>
    public static class OpenBgWindowScreen
    {
        /// <summary>(x, width, MaxCharacters) of the 18 row slots, exactly as open_bg_row_slots() declares them.</summary>
        public static readonly (int X, int Width, int Characters)[] RowSlots =
        {
            (12, 14, 2), (28, 460, 115), (28, 720, 115), (28, 128, 40), (28, 128, 40), (28, 128, 40), (160, 24, 4), (190, 98, 32),
            (160, 128, 42), (292, 122, 40), (292, 122, 40), (292, 122, 40), (418, 80, 40), (418, 80, 40), (504, 132, 44),
            (640, 108, 36), (502, 44, 4), (546, 44, 4),
        };
        /// <summary>(width, MaxCharacters) of the five crest band labels of a realm (open_bg_realm_labels()).</summary>
        public static readonly (int Width, int Characters)[] RealmSlots = { (148, 40), (148, 48), (104, 52), (104, 52), (38, 8) };
        public const int StatusWidth = 650, StatusCharacters = 115, PageWidth = 160, PageCharacters = 40;
        public const int PhaseWidth = 736, HintWidth = 736, ReplyWidth = 600, LineCharacters = 115;
        public const int BannerFitWidth = 468, BannerCharacters = 40, HeadWidth = 122, HeadCharacters = 24;
        public const int TabWidth = 96, TabCharacters = 16;
        // Unit title on its header: slot 2 is 720 px wide but the "6 bots" text starts at x=160.
        public const int UnitTitleWidth = 128;
        public const string MemberIndent = "  ";
        public const string TabOverview = "Overview", TabSetup = "Setup";
        public const string OverviewHint = "+ opens a unit, - folds it. * marks the leader. Click a crest to jump.";
        public const string SetupHint = "Click a setting's name for help. [-] and [+] step a value; the link on the right switches it.";
        public const string RegionName = "Thidranki";
        public const string RelieveLink = "[Relieve]", RelieveConfirmLink = "[Yes, relieve]", AttackLink = "[Attack keep]";
        public const int RelieveConfirmSeconds = 10;
        private const int CrestBarGlyphs = 52, SetupBarGlyphs = 40, PipLimit = 10;
        private static readonly eRealm[] RealmOrder = { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };
        private static readonly OpenBgUnitKind[] Kinds = { OpenBgUnitKind.Group8, OpenBgUnitKind.Small, OpenBgUnitKind.Solo };
        private static readonly string[] PresetKeys = { "std8", "caster8", "raid8", "stealth4", "small", "duo" };
        private static readonly string[] PeaceCycle = { "auto", "all", "realm" };
        private const int PresetMax = 8;

        /// <summary>
        /// Advance widths of the stock "title" font for '!' through '~' (fonts/title_24.tga), a copy of TITLE_ADVANCES in
        /// build_companion_manager_client.py (a test compares both); a space has no glyph, 7 is the builder's estimate.
        /// </summary>
        public static readonly byte[] TitleAdvances =
        {
            7, 9, 12, 12, 15, 15, 6, 9, 9, 10, 10, 6, 9, 6, 10, 12, 8, 10, 10, 13, 10, 11, 11, 11, 12, 6, 6, 10, 11, 10, 9, 15,
            13, 11, 12, 14, 10, 10, 13, 13, 6, 9, 12, 10, 18, 13, 14, 11, 16, 12, 9, 10, 12, 12, 18, 12, 12, 11, 8, 10, 8, 10, 12,
            6, 10, 11, 9, 11, 10, 8, 10, 12, 6, 8, 10, 6, 15, 11, 11, 10, 10, 7, 9, 8, 12, 10, 15, 9, 10, 10, 9, 6, 8, 11,
        };
        public const int TitleSpaceAdvance = 7;

        /// <summary>The label index (relative to 620) of a row's slot.</summary>
        public static int RowLabelIndex(int row, int slot) => BgLabelRowBase - BgLabelBase + BgRowStride * row + slot;

        /// <summary>0 Albion, 1 Midgard, 2 Hibernia (the name slot offset and the crest column); anything else counts as Albion.</summary>
        public static int RealmIndex(eRealm realm) => realm switch { eRealm.Midgard => 1, eRealm.Hibernia => 2, _ => 0 };

        /// <summary>The active tab's text: capitals, so the active plate reads differently from the inactive one in more than colour.</summary>
        public static string ActiveTab(string tab) => tab.ToUpperInvariant();

        /// <summary>Owner defaults: bot cap 4, cycle 5, everything else 1.</summary>
        public static int StepOf(string key) => key switch { "max_bots" => 20, "cycle_min" or "cycle_max" => 5, _ => 1 };

        public static OpenBgWindowView Build(OpenBgStatusSnapshot status, OpenBgSetupValues values, OpenBgWindowPage page, int offset, string reply,
            OpenBgWindowState state = null)
        {
            state ??= new OpenBgWindowState();
            var blocks = new List<string>();
            IReadOnlyList<OpenBgWindowRow> rows = page == OpenBgWindowPage.Overview
                ? OverviewRows(status, values, state, blocks)
                : SetupRows(values, state);
            if (state.JumpTo != null)
            {
                int target = rows.ToList().FindIndex(row => row.Key == state.JumpTo);
                if (target >= 0) offset = target;
            }
            int clamped = Math.Clamp(offset, 0, Math.Max(0, rows.Count - BgRowCount));
            OpenBgWindowRow[] visible = rows.Skip(clamped).Take(BgRowCount).Select(Clipped).ToArray();
            OpenBgWindowRow selected = state.Selected == null ? null : rows.FirstOrDefault(row => row.Key == state.Selected && row.Help != null);
            string hint = selected?.Help ?? (page == OpenBgWindowPage.Overview ? OverviewHint : SetupHint);
            return new OpenBgWindowView
            {
                Page = page,
                Status = Clip(StatusText(status, values), StatusWidth, StatusCharacters),
                Banner = FitTitle(BannerText(status), BannerFitWidth, BannerCharacters),
                Phase = Clip(PhaseText(status), PhaseWidth, LineCharacters),
                Hint = Clip(hint, HintWidth, LineCharacters),
                Reply = Clip(reply, ReplyWidth, LineCharacters),
                PageIndicator = Clip(rows.Count == 0 ? "No rows" : $"Rows {clamped + 1}-{clamped + visible.Length} of {rows.Count}", PageWidth, PageCharacters),
                Heads = (page == OpenBgWindowPage.Overview
                        ? new[] { "Unit / name", "Lv", "Class", "Doing", "Strength", "Note" }
                        : new[] { "Setting", "Range", string.Empty, "Value", string.Empty, string.Empty })
                    .Select(head => Clip(head, HeadWidth, HeadCharacters)).ToArray(),
                Realms = CrestBand(status, values),
                Rows = visible,
                TotalRows = rows.Count,
                Offset = clamped,
                Blocks = blocks,
            };
        }

        /// <summary>Every row of a page (not yet clipped), for paging and tests.</summary>
        public static IReadOnlyList<OpenBgWindowRow> AllRows(OpenBgStatusSnapshot status, OpenBgSetupValues values, OpenBgWindowPage page,
            OpenBgWindowState state = null) =>
            page == OpenBgWindowPage.Overview ? OverviewRows(status, values, state ?? new OpenBgWindowState(), new List<string>()) : SetupRows(values, state ?? new OpenBgWindowState());

        // ---- clipping --------------------------------------------------------------------------------------------

        /// <summary>The text cut to the smaller of the pixel width (arial14) and the label's MaxCharacters.</summary>
        public static string Clip(string text, int width, int maxCharacters)
        {
            text = Sanitize(text ?? string.Empty, int.MaxValue).Trim();
            if (text.Length > maxCharacters)
                text = maxCharacters > 3 ? text[..(maxCharacters - 3)].TrimEnd() + "..." : text[..maxCharacters];
            return Fit(text, width);
        }

        /// <summary>Rendered width of text in the stock title font (TitleAdvances).</summary>
        public static int TitleWidth(string text)
        {
            int width = 0;
            foreach (char value in text ?? string.Empty)
                width += value is > ' ' and <= '~' ? TitleAdvances[value - '!'] : TitleSpaceAdvance;
            return width;
        }

        /// <summary>The banner text cut to the title font's pixel width and MaxCharacters, "..." when cut.</summary>
        public static string FitTitle(string text, int width, int maxCharacters)
        {
            text = Sanitize(text ?? string.Empty, int.MaxValue).Trim();
            if (text.Length > maxCharacters)
                text = text[..(maxCharacters - 3)].TrimEnd() + "...";
            if (TitleWidth(text) <= width)
                return text;
            int limit = width - TitleWidth("...");
            int length = 0;
            for (int used = 0; length < text.Length && used + TitleWidth(text[length].ToString()) <= limit; length++)
                used += TitleWidth(text[length].ToString());
            return text[..length].TrimEnd() + "...";
        }

        private static string Slot(string text, int slot, int reserve = 0) =>
            text == null ? null : Clip(text, RowSlots[slot].Width - reserve, RowSlots[slot].Characters - reserve / TextWidth(" "));

        private static OpenBgWindowRow Clipped(OpenBgWindowRow row) => row with
        {
            Mark = Slot(row.Mark, BgSlotMark), Title = Slot(row.Title, BgSlotTitle),
            Text = row.Block != null && row.Text != null ? Clip(row.Text, UnitTitleWidth, RowSlots[BgSlotText].Characters) : Slot(row.Text, BgSlotText),
            Name = Slot(row.Name, BgSlotNameBase, row.Indent ? TextWidth(MemberIndent) : 0), Level = Slot(row.Level, BgSlotLevel),
            Class = Slot(row.Class, BgSlotClass), Ash = Slot(row.Ash, BgSlotAsh), State = Slot(row.State, BgSlotStateField),
            BarDim = Slot(row.BarDim, BgSlotBarDim), BarField = Slot(row.BarField, BgSlotBarField), Note = Slot(row.Note, BgSlotNote),
            LinkText = Slot(row.LinkText, BgSlotLink), MinusText = Slot(row.MinusText, BgSlotMinus), PlusText = Slot(row.PlusText, BgSlotPlus),
        };

        // ---- header, banner, phase, crest band ------------------------------------------------------------------

        private static string ModeText(OpenBgSetupValues values) => values.ValueOf("mode") == "perrealm" ? "Per realm" : "Server PvP";

        private static string StatusText(OpenBgStatusSnapshot status, OpenBgSetupValues values)
        {
            if (status == null) return $"{RegionName} | no field report yet";
            string state = !status.CampaignEnabled ? "closed (the battleground campaign is off)"
                : !status.OpenEnabled ? "off"
                : !status.Ready ? "on, NOT ready"
                : "on and ready";
            int players = status.Players?.Length ?? 0;
            return $"{RegionName} | {state} | {players} {(players == 1 ? "player" : "players")} inside | {ModeText(values)}";
        }

        /// <summary>The banner: "Thidranki Faste: Hibernia holds it" from the keep owner text ("keep: Realm (Guild)").</summary>
        public static string BannerText(OpenBgStatusSnapshot status)
        {
            string owner = status?.KeepOwner;
            if (string.IsNullOrWhiteSpace(owner)) return "The keep: no report yet";
            int colon = owner.IndexOf(": ", StringComparison.Ordinal);
            if (colon <= 0) return owner;
            string keep = owner[..colon], rest = owner[(colon + 2)..].Trim();
            if (rest.EndsWith(" - " + BattlegroundKeepOwnership.LordDefeatedUnclaimed, StringComparison.Ordinal))
                return $"{keep}: lord down, unclaimed";
            int paren = rest.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0 && rest.EndsWith(")", StringComparison.Ordinal))
                return $"{keep}: {rest[(paren + 2)..^1]} holds it";
            return $"{keep}: {rest} holds it";
        }

        private static string PhaseName(string phase) => phase switch
        {
            "Quiet" => "Quiet",
            "Push" => "Push",
            "KeepFight" => "Keep fight",
            "Retreat" => "Retreat",
            null or "" or OpenBgStatusSnapshot.PhaseNotAvailable => "No phase yet",
            _ => phase,
        };

        private static string NextPhase(string phase) => phase switch
        {
            "Quiet" => "next push in",
            "Push" => "keep fight in",
            "KeepFight" => "retreat in",
            "Retreat" => "next cycle in",
            _ => null,
        };

        /// <summary>A game-time span: "35 min" from 90 s on, else "40 s".</summary>
        public static string Span(int seconds) => seconds >= 90 ? $"{(seconds + 30) / 60} min" : $"{Math.Max(0, seconds)} s";

        private static string PhaseText(OpenBgStatusSnapshot status)
        {
            if (status == null) return "The field report comes every 15 s.";
            var parts = new List<string> { PhaseName(status.Phase) };
            string next = NextPhase(status.Phase);
            if (next != null && status.PhaseSecondsLeft > 0) parts.Add($"{next} {Span(status.PhaseSecondsLeft)}");
            parts.Add(status.LastField == null ? "field: no figures yet"
                : $"field {status.LastField.UnitsOut} of {status.LastField.Units} units out ({status.LastField.Pct}%)");
            bool reached = status.SelectedSpeed <= 0 || Math.Abs(status.AchievedSpeed - status.SelectedSpeed) <= 0.05 * status.SelectedSpeed;
            parts.Add(reached ? $"speed {status.SelectedSpeed}x"
                : string.Format(CultureInfo.InvariantCulture, "speed {0}x ({1:0.#}x real)", status.SelectedSpeed, status.AchievedSpeed));
            // The raid summary is the longest part, so it goes last where the clip may cut it.
            if (status.Phase is "Push" or "KeepFight" && !string.IsNullOrWhiteSpace(status.Attacker)) parts.Add(status.Attacker);
            return string.Join(" | ", parts);
        }

        private static string Bar(char glyph, int count) => count <= 0 ? null : new string(glyph, count);

        private static string[] CrestBand(OpenBgStatusSnapshot status, OpenBgSetupValues values)
        {
            var labels = new string[3 * BgRealmStride];
            OpenBgBotStatus[] bots = status?.Bots ?? Array.Empty<OpenBgBotStatus>();
            OpenBgSetup setup = values.ToSetup();
            Dictionary<int, string> home = HomeRealms(bots);
            for (int index = 0; index < RealmOrder.Length; index++)
            {
                eRealm realm = RealmOrder[index];
                string name = OpenBattlegroundRoster.RealmName(realm);
                OpenBgBotStatus[] crew = bots.Where(bot => bot.Realm == name && bot.State != "leaving").ToArray();
                int inside = crew.Count(bot => bot.State == "inside");
                // units as the Overview lists them (a mixed Server PvP unit counts for its leader's realm)
                int units = bots.Where(bot => home[bot.Unit] == name && bot.State != "leaving").Select(bot => bot.Unit).Distinct().Count();
                OpenBgRealmTargets targets = setup.TargetsFor(realm);
                int plan = targets.Groups8 + targets.Small + targets.Solos;
                int field = crew.Length == 0 ? 0 : (int)Math.Round((double)CrestBarGlyphs * inside / crew.Length, MidpointRounding.AwayFromZero);
                string[] texts =
                {
                    status == null ? name : $"{name}  {crew.Length} crew",
                    status == null ? "no report yet" : $"{units} {(units == 1 ? "unit" : "units")}, plan {plan}",
                    Bar('|', CrestBarGlyphs), Bar('|', field), status == null ? null : $"{inside} in",
                };
                for (int slot = 0; slot < BgRealmStride; slot++)
                    labels[BgRealmStride * index + slot] = texts[slot] == null ? string.Empty : Clip(texts[slot], RealmSlots[slot].Width, RealmSlots[slot].Characters);
            }
            return labels;
        }

        // ---- Overview --------------------------------------------------------------------------------------------

        private static OpenBgWindowRow Notice(string key, string text) => new() { Key = key, Text = text };

        private static IReadOnlyList<OpenBgWindowRow> OverviewRows(OpenBgStatusSnapshot status, OpenBgSetupValues values, OpenBgWindowState state,
            List<string> blocks)
        {
            var rows = new List<OpenBgWindowRow>();
            if (status == null)
            {
                rows.Add(Notice("n:report", "No field report yet. It updates every 15 s; click Refresh."));
                return rows;
            }
            OpenBgBotStatus[] bots = status.Bots ?? Array.Empty<OpenBgBotStatus>();
            if (!status.CampaignEnabled || !status.OpenEnabled)
                rows.Add(Notice("n:off", "The open BG is off. Turn it on in Setup."));
            else if (!status.Ready)
                rows.Add(Notice("n:ready", $"Not ready: {status.NotReadyReason ?? "unknown"}. The crew waits until the zone is ready."));
            OpenBgRealmStatus[] realms = status.Realms ?? OpenBattlegroundStatus.RealmStatuses(values.ToSetup(), bots, null, 0);
            if (bots.Length == 0)
            {
                if (status.CampaignEnabled && status.OpenEnabled)
                {
                    (int Min, int Max) band = OpenBattlegroundProperties.ResolveBand(values.BandMin, values.BandMax);
                    rows.Add(Notice("n:crew", $"No crew yet. The roster forms units within a game minute (levels {band.Min}-{band.Max})."));
                    OpenBgFormFailureStatus failure = realms.SelectMany(realm => realm.FormFailures ?? Array.Empty<OpenBgFormFailureStatus>())
                        .OrderBy(item => item.AgeSeconds).FirstOrDefault();
                    if (failure != null)
                        rows.Add(Notice("n:failure", $"Last try {Span(failure.AgeSeconds)} ago: {failure.Text}"));
                }
            }
            else
            {
                IReadOnlySet<string> opened = state.Opened ?? new HashSet<string>();
                Dictionary<int, string> home = HomeRealms(bots);
                foreach (eRealm realm in RealmOrder)
                    AddRealm(rows, blocks, realm, bots, home, realms, values, opened, state.PendingRelieve);
            }
            OpenBgPlayerStatus[] players = status.Players ?? Array.Empty<OpenBgPlayerStatus>();
            if (players.Length > 0)
            {
                rows.Add(new OpenBgWindowRow { Key = "h:players", Text = "Players inside", Ash = Count(players.Length, "player", "players") });
                foreach (OpenBgPlayerStatus player in players)
                    rows.Add(new OpenBgWindowRow
                    {
                        Key = "p:" + player.Name, Realm = RealmOf(player.Realm), Name = player.Name, Indent = true,
                        Level = player.Level.ToString(CultureInfo.InvariantCulture), Class = "player", State = "in the field", Tone = OpenBgTone.Field,
                        Select = SelectOf("p:" + player.Name), Help = $"{player.Name}, level {player.Level}, a {player.Realm} player inside the open BG.",
                    });
            }
            OpenBgGuestStatus[] guests = status.Guests ?? Array.Empty<OpenBgGuestStatus>();
            if (guests.Length > 0)
            {
                rows.Add(new OpenBgWindowRow { Key = "h:guests", Text = "Guest squads", Ash = Count(guests.Length, "squad", "squads") });
                for (int index = 0; index < guests.Length; index++)
                {
                    OpenBgGuestStatus guest = guests[index];
                    string key = "g:" + index.ToString(CultureInfo.InvariantCulture) + ":" + guest.Realm;
                    bool target = !string.IsNullOrWhiteSpace(guest.Target);
                    rows.Add(new OpenBgWindowRow
                    {
                        Key = key, Realm = RealmOf(guest.Realm), Name = guest.Realm + " squad", Indent = true,
                        Level = guest.Level.ToString(CultureInfo.InvariantCulture), Class = $"{guest.Alive}/{guest.Size} alive",
                        State = guest.Alive == 0 ? "wiped out" : target ? "near " + guest.Target : "hunting",
                        Tone = guest.Alive == 0 ? OpenBgTone.Blood : OpenBgTone.Field, Select = SelectOf(key),
                        Help = $"A guest squad of {guest.Size} from {guest.Realm}, {guest.Alive} alive, " + (target ? $"near {guest.Target}." : "hunting."),
                    });
                }
            }
            return Selected(rows, state.Selected);
        }

        // The selected row shows the gold ">" in its mark slot (over a leader's "*").
        private static List<OpenBgWindowRow> Selected(List<OpenBgWindowRow> rows, string selected)
        {
            if (selected == null) return rows;
            int index = rows.FindIndex(row => row.Key == selected && row.Select != null);
            if (index >= 0) rows[index] = rows[index] with { Mark = ">" };
            return rows;
        }

        private static OpenBgWindowIntent SelectOf(string key) => new(OpenBgWindowIntentKind.Select, key);

        private static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

        /// <summary>
        /// The realm a unit is listed under: its leader's realm, else the realm most of it belongs to (Server PvP crews mix
        /// realms; each member keeps its own realm colour).
        /// </summary>
        public static Dictionary<int, string> HomeRealms(IEnumerable<OpenBgBotStatus> bots) =>
            bots.GroupBy(bot => bot.Unit).ToDictionary(unit => unit.Key, unit =>
            {
                string leader = LeaderOf(unit.ToArray());
                return unit.FirstOrDefault(bot => bot.Name == leader)?.Realm ?? unit.GroupBy(bot => bot.Realm)
                    .OrderByDescending(group => group.Count()).ThenBy(group => (int)RealmOf(group.Key)).First().Key;
            });

        private static void AddRealm(List<OpenBgWindowRow> rows, List<string> blocks, eRealm realm, OpenBgBotStatus[] bots, Dictionary<int, string> home,
            OpenBgRealmStatus[] realms, OpenBgSetupValues values, IReadOnlySet<string> opened, int pendingRelieve)
        {
            string name = OpenBattlegroundRoster.RealmName(realm);
            OpenBgBotStatus[] mine = bots.Where(bot => bot.Realm == name).ToArray();
            OpenBgBotStatus[] crew = mine.Where(bot => bot.State != "leaving").ToArray();
            OpenBgBotStatus[] listed = bots.Where(bot => home[bot.Unit] == name).ToArray();
            OpenBgRealmStatus summary = realms.FirstOrDefault(item => item.Realm == name);
            int units = listed.Where(bot => bot.State != "leaving").Select(bot => bot.Unit).Distinct().Count();
            int inside = crew.Count(bot => bot.State == "inside");
            OpenBgRealmTargets targets = values.ToSetup().TargetsFor(realm);
            int plan = targets.Groups8 + targets.Small + targets.Solos;
            string key = "r:" + name;
            rows.Add(new OpenBgWindowRow
            {
                Key = key, Realm = realm, Name = name.ToUpperInvariant(), Ash = $"{Count(units, "unit", "units")}, {crew.Length} crew",
                State = inside > 0 ? $"{inside} inside" : "none inside", Tone = inside > 0 ? OpenBgTone.Field : OpenBgTone.Ash,
                Note = values.ValueOf("mode") == "perrealm" && summary?.OwnSetup == true ? "own setup" : null, Select = SelectOf(key),
                Help = $"{name}: {Count(units, "unit", "units")} of {plan} planned, {crew.Length} crew, {inside} inside. Crest: jump here. {AttackLink}: raid now.",
                LinkText = AttackLink, Link = new OpenBgWindowIntent(OpenBgWindowIntentKind.Attack, name),
            });
            foreach (IGrouping<int, OpenBgBotStatus> unit in OrderedUnits(listed.Where(bot => bot.Kind != "solo")))
            {
                string block = "u:" + unit.Key.ToString(CultureInfo.InvariantCulture);
                blocks.Add(block);
                bool open = opened.Contains(block);
                OpenBgBotStatus[] members = unit.ToArray();
                string title = UnitTitle(members[0], unit.Key);
                string major = MajorState(members);
                (string doing, OpenBgTone tone) = UnitDoing(members, major);
                OpenBgWindowIntent fold = new(OpenBgWindowIntentKind.Fold, block);
                bool relievable = open && major != "leaving";
                bool confirming = relievable && pendingRelieve == unit.Key;
                rows.Add(new OpenBgWindowRow
                {
                    Key = block, Block = block, Mark = open ? "-" : "+", Text = title, Ash = Count(members.Length, "bot", "bots"),
                    State = doing, Tone = tone, BarDim = Bar('=', Math.Min(PipLimit, members.Length)),
                    BarField = Bar('=', Math.Min(PipLimit, members.Count(InField))), Note = members.Any(bot => bot.InRaid) ? "Raid" : null,
                    LinkText = relievable ? confirming ? RelieveConfirmLink : RelieveLink : null,
                    Link = relievable ? new OpenBgWindowIntent(OpenBgWindowIntentKind.Relieve, title, On: confirming, Unit: unit.Key) : null,
                    Fold = fold, Select = fold,
                });
                if (!open) continue;
                string leader = LeaderOf(members);
                foreach (OpenBgBotStatus bot in members.OrderBy(bot => bot.Name != leader).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase))
                    rows.Add(Member(bot, RealmOf(bot.Realm), title, bot.Name == leader));
            }
            OpenBgBotStatus[] solos = listed.Where(bot => bot.Kind == "solo").ToArray();
            if (solos.Length == 0) return;
            string soloBlock = "s:" + name;
            blocks.Add(soloBlock);
            bool soloOpen = opened.Contains(soloBlock);
            int present = summary?.SolosPresent ?? solos.Count(bot => bot.State != "leaving");
            int soloPlan = summary?.Solos ?? targets.Solos;
            OpenBgBotStatus[] hunting = solos.Where(InField).ToArray();
            string role = hunting.GroupBy(bot => RoleText(bot.Role) ?? "in the field").OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal).Select(group => group.Key).FirstOrDefault();
            OpenBgWindowIntent soloFold = new(OpenBgWindowIntentKind.Fold, soloBlock);
            rows.Add(new OpenBgWindowRow
            {
                Key = soloBlock, Block = soloBlock, Mark = soloOpen ? "-" : "+", Text = "Solo hunters", Ash = $"{present} of {soloPlan}",
                State = role ?? MajorState(solos), Tone = role != null ? OpenBgTone.Field : OpenBgTone.Ash,
                BarDim = Bar('=', Math.Min(PipLimit, solos.Length)), BarField = Bar('=', Math.Min(PipLimit, hunting.Length)),
                Fold = soloFold, Select = soloFold,
            });
            if (!soloOpen) return;
            foreach (OpenBgBotStatus bot in solos.OrderBy(bot => StateRank(bot.State)).ThenBy(bot => bot.Name, StringComparer.OrdinalIgnoreCase))
                rows.Add(Member(bot, realm, "the solo hunters", false));
        }

        private static OpenBgWindowRow Member(OpenBgBotStatus bot, eRealm realm, string unitTitle, bool leader)
        {
            string key = "m:" + bot.Unit.ToString(CultureInfo.InvariantCulture) + ":" + bot.Name;
            (string state, OpenBgTone tone) = MemberState(bot);
            string time = bot.State == "inside" && bot.InsideSeconds > 0 ? Span(bot.InsideSeconds) : null;
            string where = state == "in the field" ? "in the field" + (time != null ? $" for {time}" : string.Empty) : state;
            return new OpenBgWindowRow
            {
                Key = key, Mark = leader ? "*" : null, Realm = realm, Name = bot.Name, Indent = true,
                Level = bot.Level.ToString(CultureInfo.InvariantCulture), Class = bot.ClassName, State = state, Tone = tone,
                Note = time, Select = SelectOf(key),
                Help = $"{bot.Name}, level {bot.Level} {bot.ClassName} of {unitTitle}{(leader ? ", its leader" : string.Empty)}: {where}.",
            };
        }

        private static bool InField(OpenBgBotStatus bot) => bot.State == "inside" && bot.Alive;

        /// <summary>A member's state word and colour: in the field (Field), dead (Blood), travelling/outside/leaving (Ash).</summary>
        public static (string Text, OpenBgTone Tone) MemberState(OpenBgBotStatus bot) =>
            !bot.Alive ? ("dead", OpenBgTone.Blood)
            : bot.State == "inside" ? ("in the field", OpenBgTone.Field)
            : (bot.State ?? "unknown", OpenBgTone.Ash);

        // The unit's order word: its role while it is in the field, "dead" when every member inside is dead, else its state.
        private static (string, OpenBgTone) UnitDoing(OpenBgBotStatus[] members, string major)
        {
            if (major != "inside") return (major, OpenBgTone.Ash);
            if (!members.Any(InField)) return ("dead", OpenBgTone.Blood);
            return (RoleText(members.Select(bot => bot.Role).FirstOrDefault(role => !string.IsNullOrWhiteSpace(role))) ?? "in the field", OpenBgTone.Field);
        }

        private static string MajorState(IEnumerable<OpenBgBotStatus> bots) =>
            bots.GroupBy(bot => bot.State ?? "unknown").OrderByDescending(group => group.Count()).ThenBy(group => StateRank(group.Key)).First().Key;

        /// <summary>The unit's title: "caster8 #31" with a preset archetype, else "8-man #12" / "small #23".</summary>
        public static string UnitTitle(OpenBgBotStatus bot, int unit) =>
            string.IsNullOrEmpty(bot.Archetype) ? UnitLabel(bot.Kind, unit) : $"{bot.Archetype} #{unit}";

        // ---- Setup -----------------------------------------------------------------------------------------------

        private static IReadOnlyList<OpenBgWindowRow> SetupRows(OpenBgSetupValues values, OpenBgWindowState state)
        {
            bool perRealm = values.ValueOf("mode") == "perrealm";
            string peace = values.ValueOf("peace");
            string nextPeace = PeaceCycle[(Array.IndexOf(PeaceCycle, peace) + 1) % PeaceCycle.Length];
            var rows = new List<OpenBgWindowRow>
            {
                Section("Battleground"),
                SwitchRow(values, "enabled", new OpenBgWindowIntent(OpenBgWindowIntentKind.SetEnabled, On: !values.Enabled)),
                new()
                {
                    Key = "k:mode", Text = "Mode", Ash = "who fights whom", State = ModeText(values), Tone = OpenBgTone.Field,
                    LinkText = perRealm ? "[Use PvP]" : "[Use Per realm]",
                    Link = new OpenBgWindowIntent(OpenBgWindowIntentKind.SetMode, perRealm ? "serverpvp" : "perrealm"),
                    Select = SelectOf("k:mode"), Help = HelpOf("mode"),
                },
                new()
                {
                    Key = "k:peace", Text = "Landing peace", Ash = "safe at a landing", State = PeaceName(peace), Tone = OpenBgTone.Field,
                    LinkText = $"[Next: {PeaceName(nextPeace)}]", Link = new OpenBgWindowIntent(OpenBgWindowIntentKind.CyclePeace),
                    Select = SelectOf("k:peace"), Help = HelpOf("peace"),
                },
                Section("Crew"),
                NumberRow(values, "max_bots"), NumberRow(values, "groups8"), NumberRow(values, "small"), NumberRow(values, "solos"),
                Section("Level band"),
                NumberRow(values, "band_min"), NumberRow(values, "band_max"),
                Section("Group presets"),
            };
            Dictionary<string, int> mix = PresetCounts(values.Presets);
            foreach (string key in PresetKeys)
            {
                int count = mix.GetValueOrDefault(key);
                string rowKey = "p:" + key;
                rows.Add(new OpenBgWindowRow
                {
                    Key = rowKey, Text = key, Ash = PresetDescription(key), State = count.ToString(CultureInfo.InvariantCulture),
                    Tone = count == 0 ? OpenBgTone.Ash : count >= PresetMax ? OpenBgTone.Blood : OpenBgTone.Field,
                    BarDim = Bar('=', PresetMax), BarField = Bar('=', count),
                    MinusText = count > 0 ? "[-]" : null, Minus = new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "preset:" + key, -1),
                    PlusText = count < PresetMax ? "[+]" : null, Plus = new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "preset:" + key, 1),
                    Select = SelectOf(rowKey),
                    Help = $"{key} ({PresetDescription(key)}): how many units use this preset; same-size units take turns, 0 leaves it out.",
                });
            }
            rows.Add(Section("Timing"));
            rows.Add(SwitchRow(values, "drift", new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, "drift", 1)));
            rows.Add(NumberRow(values, "cycle_min"));
            rows.Add(NumberRow(values, "cycle_max"));
            if (perRealm)
            {
                rows.Add(Section("Units per realm"));
                OpenBgSetup setup = values.ToSetup();
                foreach (eRealm realm in RealmOrder)
                {
                    string name = OpenBattlegroundRoster.RealmName(realm);
                    OpenBgRealmTargets targets = setup.TargetsFor(realm);
                    bool own = setup.IsOverridden(realm);
                    foreach (OpenBgUnitKind kind in Kinds)
                    {
                        int count = targets.Count(kind), max = OpenBgRealmTargets.Max(kind);
                        string kindName = OpenBattlegroundRoster.KindName(kind);
                        string key = $"units:{name}:{kindName}";
                        bool first = kind == OpenBgUnitKind.Group8;
                        rows.Add(new OpenBgWindowRow
                        {
                            Key = key, Realm = realm, Name = $"{name} {KindTitle(kind)}", Ash = $"0-{max}" + (own ? string.Empty : ", global"),
                            State = count.ToString(CultureInfo.InvariantCulture),
                            Tone = !own ? OpenBgTone.Ash : count >= max ? OpenBgTone.Blood : OpenBgTone.Field,
                            BarDim = Bar('|', SetupBarGlyphs), BarField = Bar('|', BarCount(count, 0, max)),
                            MinusText = count > 0 ? "[-]" : null, Minus = new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, key, -1),
                            PlusText = count < max ? "[+]" : null, Plus = new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, key, 1),
                            LinkText = first && own ? "[Use global]" : null,
                            Link = first && own ? new OpenBgWindowIntent(OpenBgWindowIntentKind.ResetUnits, name) : null,
                            Select = SelectOf(key),
                            Help = $"{name} {KindTitle(kind)}: {name}'s own count. A step gives {name} its own counts; [Use global] undoes that.",
                        });
                    }
                }
            }
            return Selected(rows, state.Selected);
        }

        private static OpenBgWindowRow Section(string title) => new() { Key = "h:" + title.ToLowerInvariant().Replace(' ', '_'), Title = title };

        private static string KindTitle(OpenBgUnitKind kind) => kind switch { OpenBgUnitKind.Group8 => "8-man", OpenBgUnitKind.Small => "small", _ => "solos" };

        private static string PeaceName(string peace) => peace switch { "all" => "All", "realm" => "Realm", _ => "Auto" };

        private static string PresetDescription(string key) => key switch
        {
            "std8" => "8 bots, mixed",
            "caster8" => "8 bots, casters",
            "raid8" => "8 bots, raid lead",
            "stealth4" => "4 stealthers",
            "small" => "4 bots, small",
            "duo" => "2 bots",
            _ => key,
        };

        private static int BarCount(int value, int min, int max) =>
            max <= min ? 0 : (int)Math.Round((double)SetupBarGlyphs * (Math.Clamp(value, min, max) - min) / (max - min), MidpointRounding.AwayFromZero);

        // A switch row: Field "On" or Ash "Off", and one link that names the result. The link of "drift" is an Adjust (see ToWrite).
        private static OpenBgWindowRow SwitchRow(OpenBgSetupValues values, string key, OpenBgWindowIntent flip)
        {
            bool on = values.ValueOf(key) == OpenBgSetupParser.Switch(true);
            return new OpenBgWindowRow
            {
                Key = "k:" + key, Text = SettingLabel(key), Ash = "on or off", State = on ? "On" : "Off", Tone = on ? OpenBgTone.Field : OpenBgTone.Ash,
                LinkText = on ? "[Turn off]" : "[Turn on]", Link = flip, Select = SelectOf("k:" + key), Help = HelpOf(key),
            };
        }

        private static OpenBgWindowRow NumberRow(OpenBgSetupValues values, string key)
        {
            OpenBgSetupKey setting = OpenBgSetupParser.Find(key);
            int current = int.Parse(values.ValueOf(key), CultureInfo.InvariantCulture);
            int step = StepOf(key);
            bool down = Next(values, key, -1, out _), up = Next(values, key, 1, out _);
            return new OpenBgWindowRow
            {
                Key = "k:" + key, Text = SettingLabel(key), Ash = $"{setting.Min}-{setting.Max}" + (step > 1 ? $", step {step}" : string.Empty),
                State = current.ToString(CultureInfo.InvariantCulture),
                Tone = current >= setting.Max ? OpenBgTone.Blood : current == 0 ? OpenBgTone.Ash : OpenBgTone.Field,
                BarDim = Bar('|', SetupBarGlyphs), BarField = Bar('|', BarCount(current, setting.Min, setting.Max)),
                MinusText = down ? "[-]" : null, Minus = new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, key, -1),
                PlusText = up ? "[+]" : null, Plus = new OpenBgWindowIntent(OpenBgWindowIntentKind.Adjust, key, 1),
                Select = SelectOf("k:" + key), Help = HelpOf(key),
            };
        }

        /// <summary>The one-sentence help of a setting for the hint line.</summary>
        public static string HelpOf(string key) => key switch
        {
            "enabled" => "Open BG: the porter line and the bot crew; off sends the crew home within a game minute.",
            "mode" => "Mode: Server PvP makes only group, guild and battlegroup friends; Per realm makes the whole realm friends.",
            "peace" => "Landing peace: who is safe at a landing circle; Auto follows the mode, All protects every realm, Realm its own.",
            "max_bots" => "Bot cap: the most crew bots in the open BG at once, all realms together; the roster applies it within a minute.",
            "groups8" => "8-man per realm: how many full eight-bot groups each realm fields.",
            "small" => "Small per realm: how many groups of two to four bots each realm fields.",
            "solos" => "Solos per realm: how many lone roamers and stealthers each realm fields.",
            "band_min" => "Lowest level: the lowest level a crew bot may have; it cannot go above the highest level.",
            "band_max" => "Highest level: the highest level a crew bot may have; it cannot go below the lowest level.",
            "drift" => "Player drift: roamers and solos prefer points within sight of a player.",
            "cycle_min" => "Shortest cycle: the shortest quiet-push-fight-retreat cycle, in game minutes.",
            "cycle_max" => "Longest cycle: the longest quiet-push-fight-retreat cycle, in game minutes.",
            _ => null,
        };

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

        // Why a step of a number setting does not happen: its range end, or the paired setting it may not pass.
        private static string Blocked(OpenBgSetupValues values, string key, int direction, int current)
        {
            OpenBgSetupKey setting = OpenBgSetupParser.Find(key);
            string label = SettingLabel(key);
            if (direction > 0 && current >= setting.Max) return $"{label} is at its maximum ({setting.Max}).";
            if (direction < 0 && current <= setting.Min) return $"{label} is at its minimum ({setting.Min}).";
            return (key, direction > 0) switch
            {
                ("band_min", true) => $"Lowest level cannot go above the highest level ({values.BandMax}).",
                ("band_max", false) => $"Highest level cannot go below the lowest level ({values.BandMin}).",
                ("cycle_min", true) => $"Shortest cycle cannot go above the longest cycle ({values.CycleMax}).",
                ("cycle_max", false) => $"Longest cycle cannot go below the shortest cycle ({values.CycleMin}).",
                _ => $"{label} cannot change from {current}.",
            };
        }

        // ---- the write behind an intent ---------------------------------------------------------------------------

        /// <summary>
        /// The server property write behind a Setup intent: the row key, the value and the reply line; not ok (with the
        /// reason) for a refused or no-op change, and for intents that are not writes (relieve, navigation).
        /// </summary>
        public static (bool Ok, string RowKey, string Value, string Message) ToWrite(OpenBgWindowIntent intent, OpenBgSetupValues values)
        {
            switch (intent?.Kind)
            {
                case OpenBgWindowIntentKind.SetEnabled:
                    return (true, OpenBgSetupParser.Find("enabled").RowKey, OpenBgSetupParser.Switch(intent.On), $"Open BG: {(intent.On ? "on" : "off")}.");
                case OpenBgWindowIntentKind.SetMode when intent.Key is "serverpvp" or "perrealm":
                    return (true, OpenBgSetupParser.Find("mode").RowKey, intent.Key, $"Mode: {(intent.Key == "perrealm" ? "Per realm" : "Server PvP")}.");
                case OpenBgWindowIntentKind.CyclePeace:
                {
                    string next = PeaceCycle[(Array.IndexOf(PeaceCycle, values.ValueOf("peace")) + 1) % PeaceCycle.Length];
                    return (true, OpenBgSetupParser.Find("peace").RowKey, next == "auto" ? string.Empty : next, $"Landing peace: {PeaceName(next)}.");
                }
                case OpenBgWindowIntentKind.ResetUnits when RealmOf(intent.Key) != eRealm.None:
                    return (true, OpenBattlegroundProperties.UnitsRowKey(RealmOf(intent.Key)), string.Empty, $"{intent.Key} uses the global counts again.");
                case OpenBgWindowIntentKind.Adjust when intent.Key == "drift":
                    return (true, OpenBgSetupParser.Find("drift").RowKey, OpenBgSetupParser.Switch(!values.Drift), $"Player drift: {(values.Drift ? "off" : "on")}.");
                case OpenBgWindowIntentKind.Adjust when intent.Key.StartsWith("preset:", StringComparison.Ordinal):
                {
                    string key = intent.Key["preset:".Length..];
                    if (OpenBattlegroundArchetypes.Get(key) == null) return (false, null, null, $"Unknown preset {key}.");
                    Dictionary<string, int> mix = PresetCounts(values.Presets);
                    int current = mix.GetValueOrDefault(key);
                    int count = Math.Clamp(current + Math.Sign(intent.Amount), 0, PresetMax);
                    if (count == current)
                        return (false, null, null, intent.Amount > 0 ? $"{key} is at its maximum ({PresetMax})." : $"{key} is at its minimum (0).");
                    mix[key] = count;
                    string text = string.Join(",", PresetKeys.Where(item => mix.GetValueOrDefault(item) > 0).Select(item => $"{item}:{mix[item]}"));
                    return (true, OpenBgSetupParser.Find("presets").RowKey, text, $"{key}: {current} -> {count}.");
                }
                case OpenBgWindowIntentKind.Adjust when intent.Key.StartsWith("units:", StringComparison.Ordinal):
                {
                    string[] parts = intent.Key.Split(':');
                    eRealm realm = parts.Length == 3 ? RealmOf(parts[1]) : eRealm.None;
                    OpenBgUnitKind? kind = parts.Length == 3 ? Kinds.Cast<OpenBgUnitKind?>().FirstOrDefault(item => OpenBattlegroundRoster.KindName(item.Value) == parts[2]) : null;
                    if (realm == eRealm.None || kind == null) return (false, null, null, "Unknown units row.");
                    OpenBgRealmTargets targets = values.ToSetup().TargetsFor(realm);
                    int current = targets.Count(kind.Value), max = OpenBgRealmTargets.Max(kind.Value);
                    int next = Math.Clamp(current + Math.Sign(intent.Amount), 0, max);
                    string label = $"{parts[1]} {KindTitle(kind.Value)}";
                    if (next == current)
                        return (false, null, null, intent.Amount > 0 ? $"{label} is at its maximum ({max})." : $"{label} is at its minimum (0).");
                    return (true, OpenBattlegroundProperties.UnitsRowKey(realm), targets.With(kind.Value, next).Format(), $"{label}: {current} -> {next}.");
                }
                case OpenBgWindowIntentKind.Adjust when OpenBgSetupParser.Find(intent.Key) is { IsSwitch: false, IsText: false, IsUnits: false } setting:
                {
                    int current = int.Parse(values.ValueOf(setting.Name), CultureInfo.InvariantCulture);
                    int direction = Math.Sign(intent.Amount);
                    if (!Next(values, setting.Name, direction, out int next))
                        return (false, null, null, Blocked(values, setting.Name, direction, current));
                    return (true, setting.RowKey, next.ToString(CultureInfo.InvariantCulture), $"{SettingLabel(setting.Name)}: {current} -> {next}.");
                }
            }
            return (false, null, null, null);
        }

        /// <summary>The reply after the first Relieve click of a unit.</summary>
        public static string RelieveQuestion(string title) => $"Relieve {title}? Click again within {RelieveConfirmSeconds} s.";

        /// <summary>The reply after a confirmed Relieve.</summary>
        public static string RelievedMessage(string title) => $"{title} relieved; a fresh unit forms within a game minute.";

        // ---- helpers kept from the removed Custom8 open BG manager (OpenBgManagerScreen) ------------------------------

        private static Dictionary<string, int> PresetCounts(string presets)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach ((OpenBgArchetype archetype, int count) in OpenBattlegroundArchetypes.ParseMix(presets))
                result[archetype.Key] = Math.Min(PresetMax, result.GetValueOrDefault(archetype.Key) + count);
            return result;
        }

        public static eRealm RealmOf(string name) => name switch
        {
            "Albion" => eRealm.Albion,
            "Midgard" => eRealm.Midgard,
            "Hibernia" => eRealm.Hibernia,
            _ => eRealm.None,
        };

        // Inside first, then outside, travelling and leaving; 8-man before small; then by id.
        private static IEnumerable<IGrouping<int, OpenBgBotStatus>> OrderedUnits(IEnumerable<OpenBgBotStatus> bots) =>
            bots.GroupBy(bot => bot.Unit)
                .OrderBy(unit => StateRank(MajorState(unit)))
                .ThenBy(unit => KindRank(unit.First().Kind)).ThenBy(unit => unit.Key);

        // The unit's leader: the Leader every member reports (the group's living leader) when it is a member of the unit.
        private static string LeaderOf(IReadOnlyCollection<OpenBgBotStatus> unit) =>
            unit.Select(bot => bot.Leader).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name) && unit.Any(bot => bot.Name == name));

        /// <summary>The unit title without a preset: "8-man #12", "small #40", "solo #51".</summary>
        public static string UnitLabel(string kind, int unit) => $"{(kind == "group8" ? "8-man" : kind)} #{unit}";

        private static string RoleText(string role) => string.IsNullOrWhiteSpace(role) ? null : role.Replace('_', ' ');

        private static string SettingLabel(string name) => name switch
        {
            "enabled" => "Open BG",
            "max_bots" => "Bot cap",
            "groups8" => "8-man per realm",
            "small" => "Small per realm",
            "solos" => "Solos per realm",
            "cycle_min" => "Shortest cycle",
            "cycle_max" => "Longest cycle",
            "band_min" => "Lowest level",
            "band_max" => "Highest level",
            "drift" => "Player drift",
            _ => name,
        };

        private static int StateRank(string state) => state switch { "inside" => 0, "outside" => 1, "travelling" => 2, _ => 3 };
        private static int KindRank(string kind) => kind switch { "group8" => 0, "small" => 1, _ => 2 };
    }
}
