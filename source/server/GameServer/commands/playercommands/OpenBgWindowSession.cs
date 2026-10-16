using System;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Server-held state of one player's OPEN BG window (Custom5): the page, one offset per page, the token (revision) of
    /// the drawn view and the intents behind its row controls. Pure: no packets, no player. Row controls of an older view
    /// or after half an hour without use are rejected; navigation and close are always accepted.
    /// </summary>
    public sealed class OpenBgWindowSession
    {
        public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(30);
        private readonly OpenBgWindowIntent[] _rowIntents = new OpenBgWindowIntent[CompanionManagerProtocol.BgControlPlusBase + CompanionManagerProtocol.BgRowCount];

        public OpenBgWindowPage Page { get; set; }
        public int LageOffset { get; set; }
        public int SteuernOffset { get; set; }
        public string Message { get; set; } = string.Empty;
        public ushort Revision { get; private set; } = 1;
        public string Signature { get; private set; } = string.Empty;
        public DateTime LastUseUtc { get; private set; } = DateTime.UtcNow;

        public int Offset
        {
            get => Page == OpenBgWindowPage.Lage ? LageOffset : SteuernOffset;
            set
            {
                if (Page == OpenBgWindowPage.Lage) LageOffset = value;
                else SteuernOffset = value;
            }
        }

        public void Touch(DateTime utcNow) => LastUseUtc = utcNow;

        /// <summary>
        /// Draws the current page with the stored offset and reply, keeps the clamped offset, records the intents of the
        /// row controls and issues a new revision only when a click could now resolve differently.
        /// </summary>
        public OpenBgWindowView Render(OpenBgStatusSnapshot status, OpenBgSetupValues values)
        {
            OpenBgWindowView view = OpenBgWindowScreen.Build(status, values, Page, Offset, Message);
            Offset = view.Offset;
            Array.Clear(_rowIntents);
            var keys = new System.Collections.Generic.List<string> { Page.ToString(), view.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            for (int row = 0; row < view.Rows.Count; row++)
            {
                OpenBgWindowRow item = view.Rows[row];
                _rowIntents[CompanionManagerProtocol.BgControlLinkBase + row] = item.Link;
                _rowIntents[CompanionManagerProtocol.BgControlMinusBase + row] = item.Minus;
                _rowIntents[CompanionManagerProtocol.BgControlPlusBase + row] = item.Plus;
                keys.Add(item.Key + "|" + (item.Link?.Id ?? string.Empty) + "|" + (item.Minus?.Id ?? string.Empty) + "|" + (item.Plus?.Id ?? string.Empty));
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

        /// <summary>
        /// The intent behind a control value (0-255), or null: unknown control, a row control without an intent, or a row
        /// control with a stale token. Navigation (tabs, scrolling, refresh, close) is always accepted.
        /// </summary>
        public OpenBgWindowIntent Resolve(int control, ushort token, DateTime utcNow)
        {
            switch (control)
            {
                case CompanionManagerProtocol.BgControlTabSituation: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Tab, "Lage");
                case CompanionManagerProtocol.BgControlTabControl: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Tab, "Steuern");
                case CompanionManagerProtocol.BgControlUp: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Scroll, Amount: -1);
                case CompanionManagerProtocol.BgControlDown: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Scroll, Amount: 1);
                case CompanionManagerProtocol.BgControlPageUp: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Scroll, Amount: -CompanionManagerProtocol.BgRowCount);
                case CompanionManagerProtocol.BgControlPageDown: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Scroll, Amount: CompanionManagerProtocol.BgRowCount);
                case CompanionManagerProtocol.BgControlClose: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Close);
                case CompanionManagerProtocol.BgControlRefresh: return new OpenBgWindowIntent(OpenBgWindowIntentKind.Refresh);
            }
            if (IsStale(token, utcNow)) return null;
            int row = RowOf(control, CompanionManagerProtocol.BgControlLinkBase);
            if (row < 0) row = RowOf(control, CompanionManagerProtocol.BgControlMinusBase);
            if (row < 0) row = RowOf(control, CompanionManagerProtocol.BgControlPlusBase);
            return row < 0 ? null : _rowIntents[control];
        }

        private static int RowOf(int control, int basis) =>
            control >= basis && control < basis + CompanionManagerProtocol.BgRowCount ? control - basis : -1;

        /// <summary>Applies a navigation intent (tab or scroll); true when the window must be redrawn.</summary>
        public bool Navigate(OpenBgWindowIntent intent)
        {
            switch (intent?.Kind)
            {
                case OpenBgWindowIntentKind.Tab:
                    Page = intent.Key == "Steuern" ? OpenBgWindowPage.Steuern : OpenBgWindowPage.Lage;
                    return true;
                case OpenBgWindowIntentKind.Scroll:
                    Offset = Math.Max(0, Offset + intent.Amount);
                    return true;
                case OpenBgWindowIntentKind.Refresh:
                    return true;
            }
            return false;
        }
    }
}
