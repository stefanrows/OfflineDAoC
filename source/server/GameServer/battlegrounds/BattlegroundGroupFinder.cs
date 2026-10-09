using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Events;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>Only players who explicitly opt in can acquire a new group alliance.</summary>
    public static class BattlegroundGroupFinder
    {
        private sealed record Entry(GamePlayer Player, ushort RegionId, string Mode, long JoinedAt);
        private static readonly object Gate = new();
        private static readonly Dictionary<GamePlayer, Entry> Waiting = new();
        private static ECSGameTimer _timer;

        [GameServerStartedEvent]
        public static void Start(DOLEvent e, object sender, EventArgs args)
        {
            _timer?.Stop();
            _timer = new ECSGameTimer(new GameNPC(), Tick, 30_000);
        }

        [GameServerStoppedEvent]
        public static void Stop(DOLEvent e, object sender, EventArgs args)
        {
            _timer?.Stop();
            _timer = null;
            lock (Gate) Waiting.Clear();
        }

        public static void Command(GameClient client, string[] args, string mode)
        {
            if (client?.Player == null) return;
            if (args.Length > 2 || args.Length == 2 && !args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                Send(client.Player, $"Use /{(mode == "xp" ? "LFxp" : "LFrvr")} [off].");
                return;
            }
            Set(client.Player, args.Length == 2 ? "off" : mode, out string message);
            Send(client.Player, message);
        }

        public static bool Set(GamePlayer player, string mode, out string message)
        {
            if (player == null) { message = "A player is required."; return false; }
            mode = mode?.ToLowerInvariant();
            lock (Gate)
            {
                if (mode == "off")
                {
                    Waiting.Remove(player);
                    message = "Battleground automatic grouping is off.";
                    return true;
                }
                if (mode is not ("xp" or "pvp"))
                { message = "Choose xp, pvp or off."; return false; }
                BattlegroundDefinition definition = BattlegroundCampaignCatalog.Find(player.CurrentRegionID);
                if (!BattlegroundCampaignPolicy.CanEnter(definition, player, out message)) return false;
                if (player.Group != null)
                { message = "Leave your existing group before opting in to automatic grouping."; return false; }
                if (!player.IsAlive || player.InCombat)
                { message = "Opt in while alive and out of combat."; return false; }
                long joined = Waiting.TryGetValue(player, out Entry prior) && prior.Mode == mode ? prior.JoinedAt : GameLoop.GameLoopTime;
                Waiting[player] = new Entry(player, player.CurrentRegionID, mode, joined);
                message = $"Queued for battleground {mode} grouping. Matches run every 30 seconds; use /battleground lfg off to cancel.";
                return true;
            }
        }

        private static bool Available(Entry entry) => entry.Player.ObjectState == GameObject.eObjectState.Active &&
            entry.Player.Client?.ClientState == GameClient.eClientState.Playing && entry.Player.Group == null &&
            entry.Player.CurrentRegionID == entry.RegionId && BattlegroundCampaignPolicy.IsEnabled &&
            BattlegroundCampaignPolicy.IsEligible(entry.Player, BattlegroundCampaignCatalog.Find(entry.RegionId));

        private static int Tick(ECSGameTimer timer)
        {
            lock (Gate)
            {
                foreach (Entry entry in Waiting.Values.ToArray())
                    if (!Available(entry)) Waiting.Remove(entry.Player);
                Entry[] candidates = Waiting.Values.Where(x => x.Player.IsAlive && !x.Player.InCombat)
                    .OrderBy(x => x.JoinedAt).ThenBy(x => x.Player.Name, StringComparer.Ordinal).ToArray();
                foreach (Entry leader in candidates)
                {
                    if (!Waiting.ContainsKey(leader.Player) || !Available(leader)) continue;
                    Entry[] matches = candidates.Where(x => Waiting.ContainsKey(x.Player) && Available(x) &&
                        x.RegionId == leader.RegionId && x.Mode == leader.Mode &&
                        (x.Mode == "pvp" || Math.Abs(x.Player.Level - leader.Player.Level) <= 3)).ToArray();
                    if (matches.Length < 2) continue;
                    Group group = new(leader.Player);
                    if (!GroupMgr.AddGroup(group)) continue;
                    var admitted = new List<Entry>();
                    // Native Group construction does not add its leader. Admit
                    // the elected leader first so no group has an absent leader.
                    if (!Available(leader) || !leader.Player.IsAlive || leader.Player.InCombat || !group.AddMember(leader.Player))
                    {
                        group.DisbandGroup();
                        continue;
                    }
                    admitted.Add(leader);
                    foreach (Entry match in matches.Where(x => x.Player != leader.Player).Take(group.MaximumMemberCount - 1))
                    {
                        if (!Available(match) || !match.Player.IsAlive || match.Player.InCombat || !group.AddMember(match.Player)) continue;
                        admitted.Add(match);
                    }
                    if (group.MemberCount < 2)
                    {
                        group.DisbandGroup();
                        continue;
                    }
                    // Failed formation retains the queue and never tells a
                    // lone player that a complete match was made.
                    foreach (Entry match in admitted)
                    {
                        Waiting.Remove(match.Player);
                        Send(match.Player, "Your opted-in battleground group has formed. Automatic grouping is now off.");
                    }
                }
            }
            return 30_000;
        }

        private static void Send(GamePlayer player, string message) =>
            player.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
    }
}
