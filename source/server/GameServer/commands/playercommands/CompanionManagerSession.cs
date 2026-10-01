using System;
using System.Collections.Generic;
using System.Linq;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.GS.Commands
{
    /// <summary>Active lists only the roster companions currently in the player's group.</summary>
    public enum CompanionManagerTab { Roster, Recruit, Active }

    public enum CompanionManagerDetailTab { Overview, Training, Gear }

    /// <summary>How the list is split into collapsible sections.</summary>
    public enum CompanionManagerGroup { Smart, Realm, Role, Level, None }

    public enum CompanionManagerSort { Level, Name, Class }

    /// <summary>
    /// One list entry. <see cref="Key"/> is the only identity a row click resolves to. The
    /// remaining fields are the list columns and the facts grouping and sorting use.
    /// </summary>
    public sealed record CompanionManagerEntry(string Key, eRealm Realm, eCharacterClass Class, string Name,
        int Level = 0, string Origin = "", string State = "", bool StateActive = false, bool InGroup = false,
        BotPveGroupRole Role = BotPveGroupRole.Attacker, string ClassText = null)
    {
        public string LevelText => Level > 0 ? Level.ToString() : string.Empty;
        public string ClassDisplay => ClassText ?? Class.ToString();
    }

    /// <summary>A list line: a section header (<see cref="Entry"/> is null) or a companion.</summary>
    public sealed record CompanionManagerLine(string SectionKey, string Header, CompanionManagerEntry Entry)
    {
        public bool IsHeader => Entry == null;
    }

    public sealed class CompanionManagerListState
    {
        public eRealm? Realm { get; set; }
        public BotPveGroupRole? Role { get; set; }
        public int Offset { get; set; }
        public string SelectedKey { get; set; }
    }

    /// <summary>Server-held state for one player's Companion Manager window.</summary>
    public sealed class CompanionManagerSession
    {
        public const int MaximumQueryLength = 24;
        public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(30);

        public CompanionManagerTab Tab { get; set; }
        public CompanionManagerDetailTab DetailTab { get; set; }
        public CompanionManagerListState Roster { get; } = new();
        public CompanionManagerListState Recruit { get; } = new();
        public CompanionManagerListState Active { get; } = new();
        public string Query { get; set; } = string.Empty;
        public CompanionManagerGroup Group { get; set; } = CompanionManagerGroup.Smart;
        public CompanionManagerSort Sort { get; set; } = CompanionManagerSort.Level;
        /// <summary>List rows filled in the window; the client cannot report how tall it is.</summary>
        public int RowCount { get; set; } = RowSizes[1];
        public HashSet<string> CollapsedSections { get; } = new(StringComparer.Ordinal);
        public int DetailOffset { get; set; }
        public bool RealmAbilityView { get; set; }
        public string SelectedItemId { get; set; }
        /// <summary>The worn slot opened in the Gear tab, or Invalid.</summary>
        public eInventorySlot SelectedSlot { get; set; } = eInventorySlot.Invalid;
        /// <summary>A build chosen in the detail panel; it applies only while <c>EntryKey</c> stays selected.</summary>
        public (string EntryKey, string PlanId) BuildChoice { get; set; }
        /// <summary>Only the selected companion can receive a second-click delete confirmation.</summary>
        public string DeleteConfirmationId { get; set; }
        public ushort Revision { get; private set; } = 1;
        public string Signature { get; private set; } = string.Empty;
        public ushort Region { get; set; }
        public DateTime LastUseUtc { get; set; }
        public bool ClientConfirmed { get; set; }
        public bool GuidancePending { get; set; }
        public string Message { get; set; } = string.Empty;
        public string[] RowKeys { get; } = new string[Rows];
        public string[] SentLabels { get; } = new string[LabelCount];
        /// <summary>Detail lines filled at the current <see cref="RowCount"/>.</summary>
        public int DetailCount => Math.Clamp(RowCount - DetailReserve, 1, DetailLines);
        internal Action[] DetailActions { get; } = new Action[DetailLines];
        internal Action[] ActionHandlers { get; } = new Action[Actions];
        internal PersistentCompanionInventoryView Bag { get; set; }

        public CompanionManagerListState Current => Tab switch
        {
            CompanionManagerTab.Recruit => Recruit,
            CompanionManagerTab.Active => Active,
            _ => Roster,
        };

        /// <summary>Roster and Active both list owned companions and share their detail panel.</summary>
        public bool ShowsCompanions => Tab != CompanionManagerTab.Recruit;

        /// <summary>Letters, digits, spaces, apostrophes, and hyphens; at most 24 characters.</summary>
        public static string NormalizeQuery(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            var kept = new List<char>(Math.Min(text.Length, MaximumQueryLength));
            foreach (char value in text.Trim())
            {
                if (kept.Count >= MaximumQueryLength)
                    break;
                if (value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '\'' or '-')
                    kept.Add(value);
                else if (char.IsWhiteSpace(value) && kept.Count > 0 && kept[^1] != ' ')
                    kept.Add(' ');
            }
            return new string(kept.ToArray()).Trim();
        }

        /// <summary>Every search term must appear in the name, class, realm, origin, or state.</summary>
        public static bool Matches(CompanionManagerEntry entry, string query, eRealm? realm, BotPveGroupRole? role)
        {
            if (realm != null && entry.Realm != realm)
                return false;
            if (role != null && !BotPartyRoles.CanFill(entry.Class, role.Value))
                return false;
            if (string.IsNullOrEmpty(query))
                return true;
            foreach (string term in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!entry.Name.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Class.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Realm.ToString().Contains(term, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Origin.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                    !entry.State.Contains(term, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        public IReadOnlyList<CompanionManagerEntry> Filter(IEnumerable<CompanionManagerEntry> entries) =>
            entries.Where(entry => Matches(entry, Query, Current.Realm, Current.Role)).ToArray();

        public static string SectionKey(string tab, string section) => tab + "|" + section;

        /// <summary>
        /// Sorts the entries and, unless grouping is off, splits them into sections with a header
        /// line each. A collapsed section keeps its header and count and hides its companions
        /// unless <paramref name="expandAll"/> is set.
        /// </summary>
        public IReadOnlyList<CompanionManagerLine> Arrange(IReadOnlyList<CompanionManagerEntry> entries, bool expandAll = false)
        {
            bool recruit = Tab == CompanionManagerTab.Recruit;
            // Candidates have no level: Level sorts them like Class, with story people first.
            CompanionManagerSort sort = recruit && Sort == CompanionManagerSort.Level ? CompanionManagerSort.Class : Sort;
            IOrderedEnumerable<CompanionManagerEntry> sorted = sort switch
            {
                CompanionManagerSort.Name => entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
                CompanionManagerSort.Class => entries.OrderBy(entry => entry.Class.ToString(), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Origin == "Story" ? 0 : 1).ThenByDescending(entry => entry.Level)
                    .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
                _ => entries.OrderByDescending(entry => entry.Level).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
            };
            CompanionManagerEntry[] ordered = sorted.ThenBy(entry => entry.Key, StringComparer.Ordinal).ToArray();

            CompanionManagerGroup mode = Group;
            // Recruit candidates have no level or group state: Smart is by realm, and Level is not grouped.
            if (recruit)
                mode = mode switch
                {
                    CompanionManagerGroup.Smart => CompanionManagerGroup.Realm,
                    CompanionManagerGroup.Level => CompanionManagerGroup.None,
                    _ => mode,
                };
            var lines = new List<CompanionManagerLine>(ordered.Length + 8);
            if (mode == CompanionManagerGroup.None)
            {
                foreach (CompanionManagerEntry entry in ordered)
                    lines.Add(new CompanionManagerLine(null, null, entry));
                return lines;
            }
            string tab = Tab.ToString();
            foreach (var section in ordered.GroupBy(entry => SectionOf(entry, mode)).OrderBy(group => group.Key.Order))
            {
                string key = SectionKey(tab, section.Key.Key);
                bool collapsed = !expandAll && CollapsedSections.Contains(key);
                lines.Add(new CompanionManagerLine(key, $"{(collapsed ? "[+]" : "[-]")} {section.Key.Title} ({section.Count()})", null));
                if (collapsed)
                    continue;
                foreach (CompanionManagerEntry entry in section)
                    lines.Add(new CompanionManagerLine(null, null, entry));
            }
            return lines;
        }

        private static (int Order, string Key, string Title) SectionOf(CompanionManagerEntry entry, CompanionManagerGroup mode)
        {
            string realm = TemporaryGroupClassCatalog.RealmName(entry.Realm);
            switch (mode)
            {
                case CompanionManagerGroup.Realm:
                    return ((int)entry.Realm, "realm:" + (int)entry.Realm, realm);
                case CompanionManagerGroup.Role:
                    return ((int)entry.Role, "role:" + (int)entry.Role, BotPartyRoles.GroupRoleLabel(entry.Role));
                case CompanionManagerGroup.Level:
                    int band = entry.Level >= 50 ? 50 : entry.Level / 10 * 10;
                    return (-band, "level:" + band, band >= 50 ? "Level 50" : band == 0 ? "Levels 1-9" : $"Levels {band}-{band + 9}");
                default:
                    return entry.InGroup ? (0, "group", "In your group")
                        : ((int)entry.Realm, "bench:" + (int)entry.Realm, "On the bench - " + realm);
            }
        }

        /// <summary>
        /// Clamps the list offset and records what each visible row resolves to: the entry key, or
        /// "h:" and the section key for a header.
        /// </summary>
        public IReadOnlyList<CompanionManagerLine> VisibleRows(IReadOnlyList<CompanionManagerLine> lines)
        {
            Current.Offset = Math.Clamp(Current.Offset, 0, Math.Max(0, lines.Count - RowCount));
            CompanionManagerLine[] visible = lines.Skip(Current.Offset).Take(RowCount).ToArray();
            for (int row = 0; row < Rows; row++)
                RowKeys[row] = row < visible.Length ? (visible[row].IsHeader ? HeaderPrefix + visible[row].SectionKey : visible[row].Entry.Key) : null;
            return visible;
        }

        public const string HeaderPrefix = "h:";

        /// <summary>A header click expands or collapses its section.</summary>
        public bool TryToggleSection(int row)
        {
            string key = row is >= 0 and < Rows ? RowKeys[row] : null;
            if (key == null || !key.StartsWith(HeaderPrefix, StringComparison.Ordinal))
                return false;
            string section = key[HeaderPrefix.Length..];
            if (!CollapsedSections.Remove(section))
                CollapsedSections.Add(section);
            return true;
        }

        public bool TrySelectRow(int row, out string key)
        {
            key = row is >= 0 and < Rows ? RowKeys[row] : null;
            if (key == null || key.StartsWith(HeaderPrefix, StringComparison.Ordinal))
                return false;
            if (Current.SelectedKey != key)
            {
                Current.SelectedKey = key;
                DeleteConfirmationId = null;
                DetailOffset = 0;
                RealmAbilityView = false;
                SelectedItemId = null;
                SelectedSlot = eInventorySlot.Invalid;
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

        public bool IsStale(ushort token, ushort region, DateTime utcNow) =>
            token != Revision || region != Region || utcNow - LastUseUtc > IdleLimit;

        public static bool IsIndexedControl(int control) =>
            control is >= ControlRowBase and < ControlRowBase + Rows or
                >= ControlDetailBase and < ControlDetailBase + DetailLines or
                >= ControlActionBase and < ControlActionBase + Actions;

        /// <summary>Word wraps text to a pixel width in the manager's label font.</summary>
        public static IReadOnlyList<string> Wrap(string text, int maxWidth)
        {
            var lines = new List<string>();
            foreach (string paragraph in (text ?? string.Empty).Split('\n'))
            {
                string line = string.Empty;
                foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    string remaining = word;
                    while (TextWidth(remaining) > maxWidth)
                    {
                        if (line.Length > 0)
                        {
                            lines.Add(line);
                            line = string.Empty;
                        }
                        int length = 1;
                        while (length < remaining.Length && TextWidth(remaining[..(length + 1)]) <= maxWidth)
                            length++;
                        lines.Add(remaining[..length]);
                        remaining = remaining[length..];
                    }
                    if (line.Length == 0)
                        line = remaining;
                    else if (TextWidth(line + " " + remaining) <= maxWidth)
                        line += " " + remaining;
                    else
                    {
                        lines.Add(line);
                        line = remaining;
                    }
                }
                if (line.Length > 0 || paragraph.Length == 0)
                    lines.Add(line);
            }
            return lines;
        }
    }

    /// <summary>Display model for one render; <see cref="ToLabels"/> maps it onto the native adapters.</summary>
    public sealed class CompanionManagerView
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ListIndicator { get; set; } = string.Empty;
        public string Header { get; set; } = string.Empty;
        public eRealm HeaderRealm { get; set; } = eRealm.Albion;
        public string Subheader { get; set; } = string.Empty;
        public string DetailIndicator { get; set; } = string.Empty;
        public bool DetailCanScrollUp { get; set; }
        public bool DetailCanScrollDown { get; set; }
        public (string Text, bool Active)[] Toggles { get; } = new (string, bool)[ToggleCount];
        /// <summary>One list row: a section header, or a companion's columns. Null leaves the row blank.</summary>
        public sealed record RowView(string Header, bool Selected, eRealm Realm, string Name, string Level,
            string Class, string Origin, string State, bool StateActive);

        public RowView[] Rows { get; } = new RowView[CompanionManagerProtocol.Rows];
        public (string Text, bool Link)[] Details { get; } = new (string, bool)[DetailLines];
        public (string Text, bool Enabled)[] ActionLabels { get; } = new (string, bool)[CompanionManagerProtocol.Actions];

        public string[] ToLabels()
        {
            var labels = new string[LabelCount];
            Array.Fill(labels, string.Empty);
            labels[LabelStatus] = Fit(Sanitize(Status), WidthStatus);
            labels[LabelMessage] = Fit(Sanitize(Message), WidthMessage);
            for (int index = 0; index < ToggleCount; index++)
            {
                (string text, bool active) = Toggles[index];
                labels[LabelToggleBase + 2 * index + (active ? 1 : 0)] = text ?? string.Empty;
            }
            for (int row = 0; row < CompanionManagerProtocol.Rows; row++)
            {
                RowView view = Rows[row];
                if (view == null)
                    continue;
                int baseIndex = LabelRowBase + RowStride * row;
                if (view.Header != null)
                {
                    labels[baseIndex] = Fit(Sanitize(view.Header), WidthRowHeader);
                    continue;
                }
                labels[baseIndex] = view.Selected ? ">" : string.Empty;
                labels[baseIndex + RowName + RealmSlot(view.Realm)] = Fit(Sanitize(view.Name), WidthRowName);
                labels[baseIndex + RowLevel] = Fit(Sanitize(view.Level), WidthRowLevel);
                labels[baseIndex + RowClass] = Fit(Sanitize(view.Class), WidthRowClass);
                labels[baseIndex + RowType] = Fit(Sanitize(view.Origin), WidthRowType);
                labels[baseIndex + (view.StateActive ? RowStateActive : RowStateIdle)] = Fit(Sanitize(view.State), WidthRowState);
            }
            labels[LabelListIndicator] = ListIndicator;
            labels[LabelHeaderBase + RealmSlot(HeaderRealm)] = Fit(Sanitize(Header), WidthDetail);
            labels[LabelSubheader] = Fit(Sanitize(Subheader), WidthDetail);
            for (int line = 0; line < DetailLines; line++)
            {
                (string text, bool link) = Details[line];
                labels[LabelDetailBase + 2 * line + (link ? 1 : 0)] = Fit(Sanitize(text), WidthDetail);
            }
            labels[LabelDetailIndicator] = DetailIndicator;
            // Clients before 0.33.0 ignore these two indexes and keep static [Up]/[Down] text.
            labels[LabelDetailUp] = DetailCanScrollUp ? "[Up]" : string.Empty;
            labels[LabelDetailDown] = DetailCanScrollDown ? "[Down]" : string.Empty;
            for (int action = 0; action < CompanionManagerProtocol.Actions; action++)
            {
                (string text, bool enabled) = ActionLabels[action];
                labels[LabelActionBase + 2 * action + (enabled ? 0 : 1)] = Fit(Sanitize(text), WidthAction);
            }
            for (int index = 0; index < labels.Length; index++)
                labels[index] = Sanitize(labels[index]);
            return labels;
        }

        public static int RealmSlot(eRealm realm) => realm switch
        {
            eRealm.Midgard => 1,
            eRealm.Hibernia => 2,
            _ => 0,
        };
    }
}
