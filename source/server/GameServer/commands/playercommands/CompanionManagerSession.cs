using System;
using System.Collections.Generic;
using System.Linq;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.GS.Commands
{
    public enum CompanionManagerTab { Roster, Recruit }

    public enum CompanionManagerDetailTab { Overview, Training, Gear }

    /// <summary>One list row. <see cref="Key"/> is the only identity a row click resolves to.</summary>
    public sealed record CompanionManagerEntry(string Key, eRealm Realm, eCharacterClass Class, string Name,
        string Info, string SortKey);

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
        public string Query { get; set; } = string.Empty;
        public int DetailOffset { get; set; }
        public string SelectedItemId { get; set; }
        /// <summary>A build chosen in the detail panel; it applies only while <c>EntryKey</c> stays selected.</summary>
        public (string EntryKey, string PlanId) BuildChoice { get; set; }
        public ushort Revision { get; private set; } = 1;
        public string Signature { get; private set; } = string.Empty;
        public ushort Region { get; set; }
        public DateTime LastUseUtc { get; set; }
        public bool ClientConfirmed { get; set; }
        public bool GuidancePending { get; set; }
        public string Message { get; set; } = string.Empty;
        public string[] RowKeys { get; } = new string[Rows];
        public string[] SentLabels { get; } = new string[LabelCount];
        internal Action[] DetailActions { get; } = new Action[DetailLines];
        internal Action[] ActionHandlers { get; } = new Action[Actions];
        internal PersistentCompanionInventoryView Bag { get; set; }

        public CompanionManagerListState Current => Tab == CompanionManagerTab.Roster ? Roster : Recruit;

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

        /// <summary>Every search term must appear in the name, class, or realm.</summary>
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
                    !entry.Realm.ToString().Contains(term, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        public IReadOnlyList<CompanionManagerEntry> Filter(IEnumerable<CompanionManagerEntry> entries) =>
            entries.Where(entry => Matches(entry, Query, Current.Realm, Current.Role))
                .OrderBy(entry => entry.SortKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .ToArray();

        /// <summary>Clamps the list offset and records the identity behind each visible row.</summary>
        public IReadOnlyList<CompanionManagerEntry> VisibleRows(IReadOnlyList<CompanionManagerEntry> filtered)
        {
            Current.Offset = Math.Clamp(Current.Offset, 0, Math.Max(0, filtered.Count - Rows));
            CompanionManagerEntry[] visible = filtered.Skip(Current.Offset).Take(Rows).ToArray();
            for (int row = 0; row < Rows; row++)
                RowKeys[row] = row < visible.Length ? visible[row].Key : null;
            return visible;
        }

        public bool TrySelectRow(int row, out string key)
        {
            key = row is >= 0 and < Rows ? RowKeys[row] : null;
            if (key == null)
                return false;
            if (Current.SelectedKey != key)
            {
                Current.SelectedKey = key;
                DetailOffset = 0;
                SelectedItemId = null;
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
        public (bool Selected, eRealm Realm, string Name, string Info)[] Rows { get; } =
            new (bool, eRealm, string, string)[CompanionManagerProtocol.Rows];
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
                (bool selected, eRealm realm, string name, string info) = Rows[row];
                int baseIndex = LabelRowBase + RowStride * row;
                if (name == null)
                    continue;
                labels[baseIndex] = selected ? ">" : string.Empty;
                labels[baseIndex + 1 + RealmSlot(realm)] = Fit(Sanitize(name), WidthRowName);
                labels[baseIndex + 4] = Fit(Sanitize(info), WidthRowInfo);
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
