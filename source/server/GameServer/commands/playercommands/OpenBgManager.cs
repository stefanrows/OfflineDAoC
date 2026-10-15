using System;
using System.Runtime.CompilerServices;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.GS.Commands
{
    /// <summary>
    /// The open BG manager (/bgsetup, owner decision 2026-10-11 14:50): the Companion Manager's Custom8 window driven
    /// with the open BG's live crew and setup. Every player may use it. It shares the window's transport
    /// (<see cref="CompanionWindowTransport"/>) and takes the window over from an open Companion Manager session (and
    /// gives it back the same way). While it is open it redraws every 15 wall seconds; only changed labels are sent.
    /// Clicks arrive as /companions ui &lt;token&gt; &lt;control&gt; and are routed here while this manager owns the window.
    /// </summary>
    public static class OpenBgManager
    {
        public const int RefreshWallMilliseconds = OpenBattlegroundStatus.PublishWallMilliseconds;
        private static readonly ConditionalWeakTable<GamePlayer, OpenBgManagerSession> Sessions = new();

        public static bool TryGetSession(GamePlayer player, out OpenBgManagerSession session)
        {
            session = null;
            return player != null && Sessions.TryGetValue(player, out session);
        }

        /// <summary>Opens or re-shows the window with a full label refresh.</summary>
        public static void Open(GamePlayer player, string message = null)
        {
            if (player == null)
                return;
            CompanionWindowOwner.Claim(player, CompanionWindowKind.OpenBg, previous =>
            {
                if (previous == CompanionWindowKind.Companions)
                    CompanionManager.Close(player, hide: false);
            });
            OpenBgManagerSession session = Sessions.GetValue(player, CreateSession);
            session.LastUseUtc = DateTime.UtcNow;
            if (message != null)
                session.Message = message;
            Array.Clear(session.SentLabels);
            Render(player, session, show: true, forceToken: true);
            CompanionWindowTransport.ScheduleGuidance(player, session, () => IsCurrent(player, session), () =>
                CompanionWindowTransport.Tell(player, "The open BG window did not answer: this client lacks the Companion Manager window. " +
                    "Use /bgsetup text for the same state as text pages."));
            StartRefresh(player, session);
        }

        public static void Close(GamePlayer player) => Close(player, hide: true);

        /// <summary>Ends the session; <paramref name="hide"/> false when the Companion Manager takes the window over.</summary>
        public static void Close(GamePlayer player, bool hide)
        {
            if (player == null || !Sessions.TryGetValue(player, out OpenBgManagerSession session))
                return;
            Sessions.Remove(player);
            session.RefreshTimer?.Stop();
            session.RefreshTimer = null;
            CompanionWindowOwner.Release(player, CompanionWindowKind.OpenBg);
            if (hide)
                CompanionWindowTransport.Hide(player);
        }

        /// <summary>Redraws an open window with <paramref name="message"/> (a /bgsetup change); false when none is open.</summary>
        public static bool RefreshIfOpen(GamePlayer player, string message)
        {
            if (player == null || !Sessions.TryGetValue(player, out OpenBgManagerSession session) || !IsCurrent(player, session))
                return false;
            if (message != null)
                session.Message = message;
            Render(player, session);
            return true;
        }

        /// <summary>The window's [Search] (prefilled /companions find): bots and players whose name or class matches.</summary>
        public static void SetQuery(GamePlayer player, string text)
        {
            if (player == null)
                return;
            OpenBgManagerSession session = Sessions.GetValue(player, CreateSession);
            session.Query = CompanionManagerSession.NormalizeQuery(text);
            session.LiveOffset = 0;
            session.Message = session.Query.Length == 0 ? "Search cleared." : $"Showing bots and players matching \"{session.Query}\".";
            Open(player);
        }

        public static void HandleClientControl(GamePlayer player, string tokenText, string controlText)
        {
            if (player == null)
                return;
            if (!TryParseClientControl(tokenText, controlText, out ushort token, out int control))
            {
                CompanionWindowTransport.Tell(player, "That open BG window control was not recognized.");
                return;
            }
            if (!Sessions.TryGetValue(player, out OpenBgManagerSession session))
            {
                if (control != ControlReady)
                    Open(player, "The open BG window was reopened; nothing was changed. Please choose again.");
                return;
            }
            session.ClientConfirmed = true;
            if (control == ControlReady)
                return;

            DateTime now = DateTime.UtcNow;
            bool stale = session.IsStale(token, now);
            session.LastUseUtc = now;
            StartRefresh(player, session);
            if (stale && CompanionManagerSession.IsIndexedControl(control))
            {
                session.Message = "The window changed before that click, so nothing was done. Please choose again.";
                Render(player, session);
                return;
            }

            session.Message = string.Empty;
            switch (control)
            {
                case ControlTabRoster:
                    session.Tab = OpenBgManagerTab.Live;
                    session.DetailOffset = 0;
                    break;
                case ControlTabRecruit:
                    session.Tab = OpenBgManagerTab.Setup;
                    session.DetailOffset = 0;
                    break;
                case >= ControlRealmAll and <= ControlRealmHibernia:
                    session.Realm = control switch
                    {
                        ControlRealmAlbion => eRealm.Albion,
                        ControlRealmMidgard => eRealm.Midgard,
                        ControlRealmHibernia => eRealm.Hibernia,
                        _ => null,
                    };
                    session.LiveOffset = session.SetupOffset = 0;
                    break;
                case >= ControlRows16 and <= ControlRows34:
                    session.RowCount = RowSizes[control - ControlRows16];
                    SaveRowCount(player, session.RowCount);
                    session.LiveOffset = session.SetupOffset = 0;
                    session.DetailOffset = 0;
                    session.Message = $"Showing {session.RowCount} list rows and {session.DetailCount} detail lines - " +
                        $"a window about {WindowHeightFor(session.RowCount)} pixels tall fits them.";
                    break;
                case ControlListUp:
                    session.Offset--;
                    break;
                case ControlListDown:
                    session.Offset++;
                    break;
                case ControlListPageUp:
                    session.Offset -= session.RowCount;
                    break;
                case ControlListPageDown:
                    session.Offset += session.RowCount;
                    break;
                case ControlDetailUp:
                    session.DetailOffset -= session.DetailCount - 1;
                    break;
                case ControlDetailDown:
                    session.DetailOffset += session.DetailCount - 1;
                    break;
                case ControlClear:
                    session.Query = string.Empty;
                    session.LiveOffset = 0;
                    session.Message = "Search cleared.";
                    break;
                case ControlRefresh:
                    Array.Clear(session.SentLabels);
                    break;
                case ControlClose:
                    Close(player);
                    return;
                case >= ControlRowBase and < ControlRowBase + Rows:
                    session.TrySelectRow(control - ControlRowBase);
                    break;
                case >= ControlDetailBase and < ControlDetailBase + DetailLines:
                    Apply(player, session, session.DetailIntents[control - ControlDetailBase]);
                    break;
                case >= ControlActionBase and < ControlActionBase + Actions:
                    Apply(player, session, session.ActionIntents[control - ControlActionBase]);
                    break;
            }
            if (IsCurrent(player, session))
                Render(player, session);
        }

        private static OpenBgManagerSession CreateSession(GamePlayer player)
        {
            var session = new OpenBgManagerSession
            {
                Message = "Live crew per realm. Select a unit to see it; the buttons add or remove units of its realm.",
            };
            // The window is the Companion Manager's: fill as many rows as the player chose there.
            var preferences = new CompanionManagerSession();
            CompanionManagerPreferences.Load(player, preferences);
            session.RowCount = preferences.RowCount;
            return session;
        }

        private static void SaveRowCount(GamePlayer player, int rows)
        {
            var preferences = new CompanionManagerSession();
            CompanionManagerPreferences.Load(player, preferences);
            preferences.RowCount = rows;
            CompanionManagerPreferences.Save(player, preferences);
        }

        private static bool IsCurrent(GamePlayer player, OpenBgManagerSession session) =>
            Sessions.TryGetValue(player, out OpenBgManagerSession current) && ReferenceEquals(current, session) &&
            CompanionWindowOwner.Current(player) == CompanionWindowKind.OpenBg;

        private static void Apply(GamePlayer player, OpenBgManagerSession session, OpenBgIntent intent)
        {
            if (intent == null)
                return;
            string message;
            switch (intent.Kind)
            {
                case OpenBgIntentKind.Write:
                    message = OpenBgSetupStore.TryWrite(intent.RowKey, intent.Value, player.Name, out string error)
                        ? intent.Message
                        : $"Could not save the open BG setup ({intent.RowKey}): {error}";
                    break;
                case OpenBgIntentKind.Relieve:
                    OpenBgSetupStore.Relieve(intent.Unit, player.Name, out message);
                    break;
                default:
                    session.Message = intent.Message;
                    return;
            }
            session.Message = message;
            CompanionWindowTransport.Tell(player, message);
        }

        private static void Render(GamePlayer player, OpenBgManagerSession session, bool show = false, bool forceToken = false)
        {
            CompanionManagerView view = OpenBgManagerScreen.Build(session, OpenBattlegroundStatus.Capture(), OpenBgSetupValues.Current(),
                player.Realm, OpenBattlegroundRoster.IsReliefPending, out string signature);
            bool changed = session.CommitView(signature);
            CompanionWindowTransport.Publish(player, session.SentLabels, view.ToLabels(), changed || forceToken, session.Revision, show);
        }

        /// <summary>Whether the 15-second redraw goes on: the window is open, the player plays, and used it within half an hour.</summary>
        public static bool KeepsRefreshing(bool current, bool playing, DateTime lastUseUtc, DateTime utcNow) =>
            current && playing && utcNow - lastUseUtc <= OpenBgManagerSession.IdleLimit;

        // The timer runs in game time; like the status file it is scaled to 15 wall seconds at any world speed.
        private static void StartRefresh(GamePlayer player, OpenBgManagerSession session)
        {
            if (session.RefreshTimer != null || player.Client?.ClientState != GameClient.eClientState.Playing)
                return;
            session.RefreshTimer = new ECSGameTimer(player, timer =>
            {
                bool playing = player.Client?.ClientState == GameClient.eClientState.Playing &&
                    player.ObjectState == GameObject.eObjectState.Active;
                if (!KeepsRefreshing(IsCurrent(player, session), playing, session.LastUseUtc, DateTime.UtcNow))
                {
                    if (ReferenceEquals(session.RefreshTimer, timer))
                        session.RefreshTimer = null;
                    return 0;
                }
                try
                {
                    Render(player, session);
                }
                catch (Exception)
                {
                    // A failed redraw waits for the next one; the window keeps its last labels.
                }
                return OpenBattlegroundStatus.GameIntervalFor(RefreshWallMilliseconds, OfflineWorldSpeedControl.EffectiveMultiplier);
            }, OpenBattlegroundStatus.GameIntervalFor(RefreshWallMilliseconds, OfflineWorldSpeedControl.EffectiveMultiplier));
        }
    }
}
