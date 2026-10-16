using System;
using System.Collections.Generic;
using System.Globalization;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Server-held state of one player's OPEN BG window (Custom5): the page, one offset per page, the opened units (new
    /// units start folded, vanished ones are pruned), the selected row per page, a pending crest jump, the Relieve
    /// confirmation (10 s), the token (revision) of the drawn view and the intents behind its row controls. Pure: no
    /// packets, no player. Row controls of an older view or after half an hour without use are rejected; navigation,
    /// crests, Open/Fold all and close are always accepted.
    /// </summary>
    public sealed class OpenBgWindowSession
    {
        public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan RelieveWindow = TimeSpan.FromSeconds(OpenBgWindowScreen.RelieveConfirmSeconds);
        private readonly OpenBgWindowIntent[] _rowIntents = new OpenBgWindowIntent[BgControlSelectBase + BgRowCount];
        private readonly HashSet<string> _opened = new(StringComparer.Ordinal);
        private IReadOnlyList<string> _blocks = Array.Empty<string>();

        public OpenBgWindowPage Page { get; set; }
        public int OverviewOffset { get; set; }
        public int SetupOffset { get; set; }
        public string Message { get; set; } = string.Empty;
        public ushort Revision { get; private set; } = 1;
        public string Signature { get; private set; } = string.Empty;
        public DateTime LastUseUtc { get; private set; } = DateTime.UtcNow;
        public string OverviewSelected { get; private set; }
        public string SetupSelected { get; private set; }
        /// <summary>The realm row ("r:Albion") the next Overview drawing scrolls to, after a crest click.</summary>
        public string JumpTo { get; private set; }
        /// <summary>The unit whose Relieve waits for the second click, and until when.</summary>
        public int PendingRelieve { get; private set; }
        public DateTime PendingRelieveUntilUtc { get; private set; }

        /// <summary>The opened blocks (units "u:31", solo blocks "s:Albion").</summary>
        public IReadOnlySet<string> Opened => _opened;

        public int Offset
        {
            get => Page == OpenBgWindowPage.Overview ? OverviewOffset : SetupOffset;
            set
            {
                if (Page == OpenBgWindowPage.Overview) OverviewOffset = value;
                else SetupOffset = value;
            }
        }

        public string Selected
        {
            get => Page == OpenBgWindowPage.Overview ? OverviewSelected : SetupSelected;
            private set
            {
                if (Page == OpenBgWindowPage.Overview) OverviewSelected = value;
                else SetupSelected = value;
            }
        }

        public void Touch(DateTime utcNow) => LastUseUtc = utcNow;

        public OpenBgWindowView Render(OpenBgStatusSnapshot status, OpenBgSetupValues values) => Render(status, values, DateTime.UtcNow);

        /// <summary>
        /// Draws the current page with the stored offset, fold set, selection and reply; keeps the clamped offset, prunes
        /// opened units that are gone, drops an expired Relieve confirmation, records the intents of the row controls and
        /// issues a new revision only when a click could now resolve differently.
        /// </summary>
        public OpenBgWindowView Render(OpenBgStatusSnapshot status, OpenBgSetupValues values, DateTime utcNow)
        {
            if (PendingRelieve != 0 && utcNow > PendingRelieveUntilUtc)
                PendingRelieve = 0;
            bool jumping = Page == OpenBgWindowPage.Overview && JumpTo != null;
            var state = new OpenBgWindowState(_opened, Selected, PendingRelieve, jumping ? JumpTo : null);
            OpenBgWindowView view = OpenBgWindowScreen.Build(status, values, Page, Offset, Message, state);
            Offset = view.Offset;
            if (jumping) JumpTo = null;
            if (Page == OpenBgWindowPage.Overview && status != null)
            {
                _blocks = view.Blocks;
                _opened.IntersectWith(view.Blocks);
            }
            Array.Clear(_rowIntents);
            var keys = new List<string> { Page.ToString(), view.Offset.ToString(CultureInfo.InvariantCulture) };
            for (int row = 0; row < view.Rows.Count; row++)
            {
                OpenBgWindowRow item = view.Rows[row];
                _rowIntents[BgControlLinkBase + row] = item.Link;
                _rowIntents[BgControlMinusBase + row] = item.Minus;
                _rowIntents[BgControlPlusBase + row] = item.Plus;
                _rowIntents[BgControlFoldBase + row] = item.Fold ?? item.Select;
                _rowIntents[BgControlSelectBase + row] = item.Select;
                keys.Add(item.Key + "|" + (item.Link?.Id ?? string.Empty) + "|" + (item.Minus?.Id ?? string.Empty) + "|" + (item.Plus?.Id ?? string.Empty) +
                    "|" + (item.Fold?.Id ?? string.Empty) + "|" + (item.Select?.Id ?? string.Empty));
            }
            CommitView(string.Join('\n', keys));
            return view;
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

        /// <summary>The row controls: link, [-], [+], fold (the mark) and name select, 18 rows each.</summary>
        public static bool IsRowControl(int control) =>
            RowOf(control, BgControlLinkBase) >= 0 || RowOf(control, BgControlMinusBase) >= 0 || RowOf(control, BgControlPlusBase) >= 0 ||
            RowOf(control, BgControlFoldBase) >= 0 || RowOf(control, BgControlSelectBase) >= 0;

        /// <summary>
        /// The intent behind a control value (0-255), or null: unknown control, a row control without an intent, or a row
        /// control with a stale token. Navigation (tabs, scrolling, crests, refresh, close, and Open/Fold all on the
        /// Overview) is always accepted.
        /// </summary>
        public OpenBgWindowIntent Resolve(int control, ushort token, DateTime utcNow)
        {
            switch (control)
            {
                case BgControlTabSituation: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Tab, nameof(OpenBgWindowPage.Overview));
                case BgControlTabControl: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Tab, nameof(OpenBgWindowPage.Setup));
                case BgControlUp: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Scroll, Amount: -1);
                case BgControlDown: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Scroll, Amount: 1);
                case BgControlPageUp: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Scroll, Amount: -BgRowCount);
                case BgControlPageDown: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Scroll, Amount: BgRowCount);
                case BgControlClose: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Close);
                case BgControlRefresh: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Refresh);
                case BgControlOpenAll: return Page == OpenBgWindowPage.Overview ? new OpenBgWindowIntent(OpenBgWindowIntentKind.OpenAll) : null;
                case BgControlFoldAll: return Page == OpenBgWindowPage.Overview ? new OpenBgWindowIntent(OpenBgWindowIntentKind.FoldAll) : null;
                case >= BgControlCrestBase and < BgControlCrestBase + 3:
                    return new OpenBgWindowIntent(OpenBgWindowIntentKind.Jump,
                        OpenBattlegroundRoster.RealmName(new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }[control - BgControlCrestBase]));
            }
            if (!IsRowControl(control) || IsStale(token, utcNow)) return null;
            return _rowIntents[control];
        }

        private static int RowOf(int control, int basis) => control >= basis && control < basis + BgRowCount ? control - basis : -1;

        /// <summary>Applies a view intent (tab, scroll, fold, select, open/fold all, crest jump); true when the window must be redrawn.</summary>
        public bool Navigate(OpenBgWindowIntent intent)
        {
            switch (intent?.Kind)
            {
                case OpenBgWindowIntentKind.Tab:
                    Page = intent.Key == nameof(OpenBgWindowPage.Setup) ? OpenBgWindowPage.Setup : OpenBgWindowPage.Overview;
                    return true;
                case OpenBgWindowIntentKind.Scroll:
                    Offset = Math.Max(0, Offset + intent.Amount);
                    return true;
                case OpenBgWindowIntentKind.Refresh:
                    return true;
                case OpenBgWindowIntentKind.Fold when !string.IsNullOrEmpty(intent.Key):
                    if (!_opened.Remove(intent.Key)) _opened.Add(intent.Key);
                    return true;
                case OpenBgWindowIntentKind.Select:
                    Selected = Selected == intent.Key ? null : intent.Key;
                    return true;
                case OpenBgWindowIntentKind.OpenAll:
                    _opened.UnionWith(_blocks);
                    return true;
                case OpenBgWindowIntentKind.FoldAll:
                    _opened.Clear();
                    return true;
                case OpenBgWindowIntentKind.Jump when !string.IsNullOrEmpty(intent.Key):
                    Page = OpenBgWindowPage.Overview;
                    JumpTo = "r:" + intent.Key;
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A Relieve click: the first click (or a confirming click after the 10 s ran out) arms the confirmation and asks;
        /// the confirming click within 10 s for the same unit returns true (the runtime then relieves) and clears it.
        /// </summary>
        public (bool Relieve, string Message) RelieveClick(OpenBgWindowIntent intent, DateTime utcNow)
        {
            if (intent?.Kind != OpenBgWindowIntentKind.Relieve || intent.Unit == 0)
                return (false, null);
            if (intent.On && PendingRelieve == intent.Unit && utcNow <= PendingRelieveUntilUtc)
            {
                PendingRelieve = 0;
                return (true, null);
            }
            PendingRelieve = intent.Unit;
            PendingRelieveUntilUtc = utcNow + RelieveWindow;
            return (false, OpenBgWindowScreen.RelieveQuestion(intent.Key));
        }
    }
}
