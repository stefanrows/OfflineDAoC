using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using DOL.Logging;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.GS.Commands
{
    /// <summary>How /bgsetup routes its arguments (<see cref="OpenBgWindow.RouteBgsetup"/>).</summary>
    public enum OpenBgSetupRoute { Open, Ui, Text, Page, Help, Relieve, Write }

    /// <summary>What the server knows about the client's OPEN BG window (Custom5).</summary>
    public enum OpenBgClientState { Unknown, Waiting, Confirmed, Missing }

    /// <summary>
    /// The 3 s probe of the OPEN BG window: after the show operation the patched client answers with the ready control
    /// (<see cref="CompanionManagerProtocol.BgReadyControl"/>). Without that answer within <see cref="Wait"/> the client
    /// has no such window (<see cref="OpenBgClientState.Missing"/>) and /bgsetup falls back to the text pages for the
    /// rest of the login. Pure: the runtime feeds it the wall clock.
    /// </summary>
    public sealed class OpenBgClientProbe
    {
        public static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(CompanionWindowTransport.GuidanceDelayMilliseconds);

        public OpenBgClientState State { get; private set; } = OpenBgClientState.Unknown;
        public DateTime SentUtc { get; private set; }

        /// <summary>False once the client proved it has no OPEN BG window.</summary>
        public bool UsesWindow => State != OpenBgClientState.Missing;

        /// <summary>The window was sent at <paramref name="utcNow"/>; true when the ready control is still awaited.</summary>
        public bool Sent(DateTime utcNow)
        {
            if (State is OpenBgClientState.Confirmed or OpenBgClientState.Missing)
                return false;
            State = OpenBgClientState.Waiting;
            SentUtc = utcNow;
            return true;
        }

        /// <summary>
        /// The client answered (ready or any click): it has the window. True when it had been given up as Missing: a late
        /// window that finally appeared must be treated as open again.
        /// </summary>
        public bool Ready()
        {
            bool revived = State == OpenBgClientState.Missing;
            State = OpenBgClientState.Confirmed;
            return revived;
        }

        /// <summary>True exactly once: the wait ran out without an answer.</summary>
        public bool Expire(DateTime utcNow)
        {
            if (State != OpenBgClientState.Waiting || utcNow - SentUtc < Wait)
                return false;
            State = OpenBgClientState.Missing;
            return true;
        }

        public TimeSpan Remaining(DateTime utcNow)
        {
            if (State != OpenBgClientState.Waiting)
                return TimeSpan.Zero;
            TimeSpan left = SentUtc + Wait - utcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public enum OpenBgWindowActionKind { Ignore, Ready, Stale, Navigate, Close, Write, Relieve, Refused }

    /// <summary>What one OPEN BG window click does, decided without packets or database (<see cref="OpenBgWindow.PlanClick"/>).</summary>
    public sealed record OpenBgWindowAction(OpenBgWindowActionKind Kind, string Message = "", string RowKey = null, string Value = null,
        int Unit = 0, OpenBgWindowIntent Intent = null);

    /// <summary>
    /// Runtime of the OPEN BG window (Custom5, spec 2026-10-16-open-bg-window-client-design.md §6): one session per player,
    /// the labels 620-984 over <see cref="CompanionWindowTransport"/> with its own token (op 12) and show (op 10), clicks as
    /// /bgsetup ui &lt;tttt&gt; &lt;cc&gt;, a redraw every 15 wall seconds while open, and the 3 s text fallback for a client
    /// without the window. No rights check: every player may steer (owner amendment 2026-10-16). Independent of the
    /// Companion Manager's Custom8 window.
    /// </summary>
    public static class OpenBgWindow
    {
        public const int RefreshWallMilliseconds = OpenBattlegroundStatus.PublishWallMilliseconds;
        public const string ClientMissingHint =
            "This client has no OPEN BG window; update the client files (UPDATE OFFLINE DAOC.cmd / Install-CompanionManager.ps1)";
        public const string StaleMessage = "The window changed before that click, so nothing was done. Please choose again.";
        public const string ReopenedMessage = "The OPEN BG window was reopened; nothing was changed. Please choose again.";

        private sealed class Runtime
        {
            public readonly OpenBgWindowSession Window = new();
            public readonly string[] SentLabels = new string[BgLabelEnd - BgLabelBase];
            public readonly OpenBgClientProbe Probe = new();
            public bool Open;
            public ECSGameTimer RefreshTimer;
            public ECSGameTimer ProbeTimer;
        }

        private static readonly ConditionalWeakTable<GamePlayer, Runtime> Sessions = new();
        private static readonly Logger Log = LoggerManager.Create(typeof(OpenBgWindow));

        // ---- pure decisions ----------------------------------------------------------------------------------------

        /// <summary>
        /// /bgsetup routing: no argument opens the window; "ui" is the window's click (checked before any key, "ui" is no
        /// key); "text [page]" the text pages; a lone number a text page; "help"; "relieve"; anything else is a key write.
        /// </summary>
        public static OpenBgSetupRoute RouteBgsetup(string[] args)
        {
            if (args == null || args.Length <= 1)
                return OpenBgSetupRoute.Open;
            string first = args[1] ?? string.Empty;
            if (first.Equals("ui", StringComparison.OrdinalIgnoreCase))
                return OpenBgSetupRoute.Ui;
            if (first.Equals("text", StringComparison.OrdinalIgnoreCase))
                return OpenBgSetupRoute.Text;
            if (args.Length == 2 && int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return OpenBgSetupRoute.Page;
            if (args.Length == 2 && first.Equals("help", StringComparison.OrdinalIgnoreCase))
                return OpenBgSetupRoute.Help;
            if (first.Equals("relieve", StringComparison.OrdinalIgnoreCase))
                return OpenBgSetupRoute.Relieve;
            return OpenBgSetupRoute.Write;
        }

        /// <summary>A click resolved against the session: ready, a stale row click, or the intent's action.</summary>
        public static OpenBgWindowAction PlanClick(OpenBgWindowSession session, int control, ushort token, DateTime utcNow, OpenBgSetupValues values)
        {
            if (control == BgReadyControl)
                return new OpenBgWindowAction(OpenBgWindowActionKind.Ready);
            bool stale = session.IsStale(token, utcNow);
            OpenBgWindowIntent intent = session.Resolve(control, token, utcNow);
            if (intent == null)
                return stale && IsRowControl(control)
                    ? new OpenBgWindowAction(OpenBgWindowActionKind.Stale, StaleMessage)
                    : new OpenBgWindowAction(OpenBgWindowActionKind.Ignore);
            return PlanIntent(intent, values);
        }

        /// <summary>The action behind an intent; writes carry the row, value and reply (<see cref="OpenBgWindowScreen.ToWrite"/>).</summary>
        public static OpenBgWindowAction PlanIntent(OpenBgWindowIntent intent, OpenBgSetupValues values)
        {
            switch (intent?.Kind)
            {
                case null:
                case OpenBgWindowIntentKind.None:
                    return new OpenBgWindowAction(OpenBgWindowActionKind.Ignore);
                case OpenBgWindowIntentKind.Tab:
                case OpenBgWindowIntentKind.Scroll:
                case OpenBgWindowIntentKind.Refresh:
                    return new OpenBgWindowAction(OpenBgWindowActionKind.Navigate, Intent: intent);
                case OpenBgWindowIntentKind.Close:
                    return new OpenBgWindowAction(OpenBgWindowActionKind.Close, Intent: intent);
                case OpenBgWindowIntentKind.Relieve:
                    return new OpenBgWindowAction(OpenBgWindowActionKind.Relieve, Unit: intent.Unit, Intent: intent);
            }
            (bool ok, string rowKey, string value, string message) = OpenBgWindowScreen.ToWrite(intent, values);
            return ok
                ? new OpenBgWindowAction(OpenBgWindowActionKind.Write, message ?? string.Empty, rowKey, value, Intent: intent)
                : new OpenBgWindowAction(OpenBgWindowActionKind.Refused, message ?? string.Empty, Intent: intent);
        }

        public static bool IsRowControl(int control) =>
            control is >= BgControlLinkBase and < BgControlLinkBase + BgRowCount
                or >= BgControlMinusBase and < BgControlMinusBase + BgRowCount
                or >= BgControlPlusBase and < BgControlPlusBase + BgRowCount;

        /// <summary>
        /// Whether the server treats the window as open: shown and used within <see cref="OpenBgWindowSession.IdleLimit"/>.
        /// The stock close X hides the window natively without telling the server, so an idle window counts as closed.
        /// </summary>
        public static bool IsOpen(bool shown, DateTime lastUseUtc, DateTime utcNow) =>
            shown && utcNow - lastUseUtc <= OpenBgWindowSession.IdleLimit;

        /// <summary>
        /// Whether a /bgsetup change by chat redraws the window, and then shows it again (op 10): the stock close X sends
        /// nothing, so a window the server believes open may be hidden where the player expects the result.
        /// </summary>
        public static bool RedrawsChatChange(bool usesWindow, bool shown, DateTime lastUseUtc, DateTime utcNow) =>
            usesWindow && IsOpen(shown, lastUseUtc, utcNow);

        /// <summary>Whether the 15-second redraw goes on: the window is open, the player plays, and used it within half an hour.</summary>
        public static bool KeepsRefreshing(bool current, bool playing, DateTime lastUseUtc, DateTime utcNow) =>
            playing && IsOpen(current, lastUseUtc, utcNow);

        // ---- runtime -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Opens or re-shows the window with a full label refresh (labels, token op 12, show op 10). A client that never
        /// answered the first open within 3 s gets the text page instead.
        /// </summary>
        public static void Open(GamePlayer player, string message = null)
        {
            if (player == null)
                return;
            Runtime runtime = Sessions.GetValue(player, _ => new Runtime());
            if (!runtime.Probe.UsesWindow)
            {
                ShowTextPage(player);
                return;
            }
            DateTime now = DateTime.UtcNow;
            runtime.Window.Touch(now);
            runtime.Open = true;
            if (message != null)
                runtime.Window.Message = message;
            Array.Clear(runtime.SentLabels);
            Render(player, runtime, show: true, forceToken: true);
            if (runtime.Probe.Sent(now))
                ScheduleProbe(player, runtime);
            StartRefresh(player, runtime);
        }

        /// <summary>Hides the window (op 11) and stops its redraw; the client state of this login is kept.</summary>
        public static void Close(GamePlayer player)
        {
            if (player == null || !Sessions.TryGetValue(player, out Runtime runtime))
                return;
            runtime.Open = false;
            runtime.RefreshTimer?.Stop();
            runtime.RefreshTimer = null;
            CompanionWindowTransport.Send(player, OpBgHide);
        }

        /// <summary>Redraws an open window with <paramref name="message"/> (a /bgsetup change by chat); false when none is open.</summary>
        public static bool RefreshIfOpen(GamePlayer player, string message)
        {
            if (player == null || !Sessions.TryGetValue(player, out Runtime runtime) ||
                !RedrawsChatChange(runtime.Probe.UsesWindow, runtime.Open, runtime.Window.LastUseUtc, DateTime.UtcNow))
                return false;
            if (message != null)
                runtime.Window.Message = message;
            Render(player, runtime, show: true);
            return true;
        }

        /// <summary>A /bgsetup ui &lt;token&gt; &lt;control&gt; click of the OPEN BG window.</summary>
        public static void HandleClientControl(GamePlayer player, string tokenText, string controlText)
        {
            if (player == null)
                return;
            if (!TryParseBgControl(tokenText, controlText, out ushort token, out int control))
            {
                CompanionWindowTransport.Tell(player, "That OPEN BG window control was not recognized.");
                return;
            }
            if (!Sessions.TryGetValue(player, out Runtime runtime))
            {
                if (control != BgReadyControl)
                    Open(player, ReopenedMessage);
                return;
            }
            bool revived = runtime.Probe.Ready();
            DateTime now = DateTime.UtcNow;
            if (revived)
            {
                // A late ready after the text fallback: the window did appear, so it is open and refreshes again.
                runtime.Window.Touch(now);
                runtime.Open = true;
                StartRefresh(player, runtime);
            }
            OpenBgWindowAction action = PlanClick(runtime.Window, control, token, now, OpenBgSetupValues.Current());
            if (action.Kind is OpenBgWindowActionKind.Ready or OpenBgWindowActionKind.Ignore)
                return;
            runtime.Window.Touch(now);
            runtime.Open = true;
            switch (action.Kind)
            {
                case OpenBgWindowActionKind.Close:
                    Close(player);
                    return;
                case OpenBgWindowActionKind.Navigate:
                    runtime.Window.Message = string.Empty;
                    runtime.Window.Navigate(action.Intent);
                    if (action.Intent.Kind == OpenBgWindowIntentKind.Refresh)
                        Array.Clear(runtime.SentLabels);
                    break;
                case OpenBgWindowActionKind.Write:
                {
                    string reply = OpenBgSetupStore.TryWrite(action.RowKey, action.Value, player.Name, out string error)
                        ? OpenBgSetupPages.WithReserveNote(action.Message, OpenBgSetupParser.Find(action.RowKey)?.Name)
                        : $"Could not save the open BG setup ({action.RowKey}): {error}";
                    runtime.Window.Message = reply;
                    CompanionWindowTransport.Tell(player, reply);
                    break;
                }
                case OpenBgWindowActionKind.Relieve:
                {
                    OpenBgSetupStore.Relieve(action.Unit, player.Name, out string reply);
                    runtime.Window.Message = reply;
                    CompanionWindowTransport.Tell(player, reply);
                    break;
                }
                default:
                    runtime.Window.Message = action.Message;
                    break;
            }
            StartRefresh(player, runtime);
            Render(player, runtime);
        }

        private static void Render(GamePlayer player, Runtime runtime, bool show = false, bool forceToken = false)
        {
            ushort before = runtime.Window.Revision;
            OpenBgWindowView view = runtime.Window.Render(OpenBattlegroundStatus.Capture(), OpenBgSetupValues.Current());
            bool changed = runtime.Window.Revision != before;
            CompanionWindowTransport.Publish(player, runtime.SentLabels, view.ToLabels(), changed || forceToken, runtime.Window.Revision, show,
                BgLabelBase, OpBgToken, OpBgShow);
        }

        private static void ShowTextPage(GamePlayer player)
        {
            if (player.Client != null)
                OpenBgSetupCommandHandler.ShowPage(player.Client, 1);
        }

        private static bool IsCurrent(GamePlayer player, Runtime runtime) =>
            Sessions.TryGetValue(player, out Runtime current) && ReferenceEquals(current, runtime) && runtime.Open;

        private static int GameInterval(int wallMilliseconds) =>
            OpenBattlegroundStatus.GameIntervalFor(Math.Max(1, wallMilliseconds), OfflineWorldSpeedControl.EffectiveMultiplier);

        // The probe timer runs in game time like the refresh; it is scaled so the client gets 3 wall seconds at any world
        // speed, and the wall clock decides (a timer that fires early waits for the rest).
        private static void ScheduleProbe(GamePlayer player, Runtime runtime)
        {
            if (runtime.ProbeTimer != null || player.Client?.ClientState != GameClient.eClientState.Playing)
                return;
            // A timer callback must never throw: the timer service kicks a GamePlayer owner to the character screen.
            runtime.ProbeTimer = new ECSGameTimer(player, timer =>
            {
                try
                {
                    DateTime now = DateTime.UtcNow;
                    if (runtime.Probe.Expire(now))
                    {
                        runtime.ProbeTimer = null;
                        runtime.Open = false;
                        runtime.RefreshTimer?.Stop();
                        runtime.RefreshTimer = null;
                        if (player.Client?.ClientState == GameClient.eClientState.Playing)
                        {
                            try
                            {
                                ShowTextPage(player);
                                CompanionWindowTransport.Tell(player, ClientMissingHint);
                            }
                            catch (Exception exception)
                            {
                                Log.Warn($"OPEN_BG_WINDOW fallback text failed player={player.Name}: {exception.Message}");
                            }
                        }
                        return 0;
                    }
                    if (runtime.Probe.State == OpenBgClientState.Waiting && player.Client?.ClientState == GameClient.eClientState.Playing)
                        return GameInterval((int)Math.Ceiling(runtime.Probe.Remaining(now).TotalMilliseconds));
                }
                catch (Exception exception)
                {
                    Log.Warn($"OPEN_BG_WINDOW probe failed: {exception.Message}");
                }
                runtime.ProbeTimer = null;
                return 0;
            }, GameInterval(CompanionWindowTransport.GuidanceDelayMilliseconds));
        }

        // The timer runs in game time; like the status file it is scaled to 15 wall seconds at any world speed.
        private static void StartRefresh(GamePlayer player, Runtime runtime)
        {
            if (runtime.RefreshTimer != null || player.Client?.ClientState != GameClient.eClientState.Playing)
                return;
            // A timer callback must never throw: the timer service kicks a GamePlayer owner to the character screen.
            runtime.RefreshTimer = new ECSGameTimer(player, timer =>
            {
                try
                {
                    bool playing = player.Client?.ClientState == GameClient.eClientState.Playing &&
                        player.ObjectState == GameObject.eObjectState.Active;
                    if (!KeepsRefreshing(IsCurrent(player, runtime), playing, runtime.Window.LastUseUtc, DateTime.UtcNow))
                    {
                        if (ReferenceEquals(runtime.RefreshTimer, timer))
                            runtime.RefreshTimer = null;
                        return 0;
                    }
                    Render(player, runtime);
                    return GameInterval(RefreshWallMilliseconds);
                }
                catch (Exception exception)
                {
                    // A failed redraw waits for the next one; the window keeps its last labels.
                    Log.Warn($"OPEN_BG_WINDOW refresh failed: {exception.Message}");
                    try
                    {
                        return GameInterval(RefreshWallMilliseconds);
                    }
                    catch (Exception)
                    {
                        return RefreshWallMilliseconds;
                    }
                }
            }, GameInterval(RefreshWallMilliseconds));
        }
    }
}
