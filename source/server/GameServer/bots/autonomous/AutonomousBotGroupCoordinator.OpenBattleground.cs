using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// open BG (spec 2026-10-11 §5): the roster forms its groups before departure through the ordinary RvR party
    /// path of <see cref="TryFormGroups(eAutonomousObjectiveKind)"/>: a real group, an RvR session and a muster at
    /// the medallion porter the group boards at. After the muster each member walks the battleground travel of
    /// an assigned participant (medallion, porter); the porter boards the leader first and the members land with it.
    /// The session takes no recruits, no field backfill and no warband merge while it is an open-BG unit, so the
    /// crew stays exactly the roster's unit.
    /// </summary>
    public static partial class AutonomousBotGroupCoordinator
    {
        public static bool TryFormOpenBattlegroundParty(GameBot leader, IReadOnlyList<GameBot> others, Vector3 muster, string musterName,
            out Group group, out string groupId, out string reason)
        {
            group = null;
            groupId = null;
            reason = null;
            if (leader?.CurrentRegion == null || others == null || others.Count == 0)
            {
                reason = "no members";
                return false;
            }
            GameBot[] all = [leader, .. others];
            if (all.Distinct().Count() != all.Length ||
                all.Any(bot => bot == null || !bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper || bot.Group != null ||
                    !bot.IsAlive || bot.CurrentRegionID != leader.CurrentRegionID))
            {
                reason = "a member is not free in the leader's region";
                return false;
            }
            // Pulse disbands a party whose members carry different objectives; an RvR session needs RvR members.
            if (all.Any(bot => !AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR)))
            {
                reason = "a member is not on an RvR tour";
                return false;
            }
            using (EnterSync())
            {
                var created = new Group(leader);
                GroupMgr.AddGroup(created);
                if (!created.AddMember(leader))
                {
                    GroupMgr.RemoveGroup(created);
                    reason = "the leader could not start a group";
                    return false;
                }
                foreach (GameBot member in others)
                    created.AddMember(member);
                if (created.MemberCount < 2)
                {
                    created.DisbandGroup();
                    reason = "fewer than two members joined";
                    return false;
                }
                Session session = NewSession(created, leader, created.MemberCount, eAutonomousObjectiveKind.RvR,
                    pickupDestination: new PickupDestination(null, muster, musterName ?? "battleground porter", leader.CurrentRegionID));
                if (session == null)
                {
                    // The members were alone before; they stay alone with their own tours.
                    created.DisbandGroup();
                    reason = "no muster formation around the porter";
                    return false;
                }
                session.OpenBattleground = true;
                Sessions[created] = session;
                GameBot[] formed = BotMembers(created);
                foreach (GameBot member in formed)
                    LastFormationAttemptTick.Remove(MemberKey(member));
                WriteSessionMetadata(session, formed);
                Log.Info($"AUTONOMOUS_GROUP_FORMED group={session.Id} crew={leader.Guild?.Name ?? "unassigned"} realm={leader.Realm} " +
                         $"size={formed.Length} lockedSize={session.LockedSize} objective={eAutonomousObjectiveKind.RvR} purpose=open_bg " +
                         $"members=\"{string.Join(",", formed.Select(member => member.Name))}\" " +
                         $"rendezvous={session.RendezvousRegion}:{(int)session.Rendezvous.X},{(int)session.Rendezvous.Y},{(int)session.Rendezvous.Z}");
                group = created;
                groupId = session.Id;
                return true;
            }
        }

        /// <summary>
        /// Refills an open-BG unit's party that lost members before boarding (live run 0.242.0: such units were relieved
        /// whole and replaced, which churned new units and new stragglers). Each recruit must be a free RvR-tour world bot
        /// in the muster's region with a formation slot there; it joins the party and walks to the porter like the rest.
        /// Returns the bots that joined (none when the party is no longer an open-BG unit or is ending).
        /// </summary>
        public static GameBot[] TryRefillOpenBattlegroundParty(Group group, IReadOnlyList<GameBot> recruits)
        {
            if (group == null || recruits == null || recruits.Count == 0) return [];
            using (EnterSync())
            {
                if (!TryGetSession(group, out Session session) || !session.OpenBattleground || session.Ending) return [];
                GameBot[] members = BotMembers(group);
                if (members.Length == 0) return [];
                var added = new List<GameBot>();
                foreach (GameBot recruit in recruits)
                {
                    if (recruit == null || !recruit.IsAutonomousWorldBot || recruit.IsTemporaryGroupHelper || recruit.Group != null ||
                        !recruit.IsAlive || recruit.CurrentRegionID != session.RendezvousRegion ||
                        !AutonomousObjectiveAssignments.Is(recruit, eAutonomousObjectiveKind.RvR))
                        continue;
                    GameBot[] expanded = [.. members, recruit];
                    if (!TryBuildRendezvousSlots(session, expanded)) continue;
                    if (!group.AddMember(recruit))
                    {
                        TryBuildRendezvousSlots(session, members);
                        continue;
                    }
                    members = expanded;
                    LastFormationAttemptTick.Remove(MemberKey(recruit));
                    added.Add(recruit);
                }
                if (added.Count == 0) return [];
                session.LockedSize = members.Length;
                RebaseAttendance(session, members, preserveArrivals: true);
                WriteSessionMetadata(session, members);
                Log.Info($"AUTONOMOUS_GROUP_RECRUITED group={session.Id} objective={session.ObjectiveKind} size={members.Length} purpose=open_bg_refill " +
                         $"added=\"{string.Join(",", added.Select(member => member.Name))}\"");
                return added.ToArray();
            }
        }

        /// <summary>An open-BG group still in its coordinator assembly (leader staging or meeting up at its porter).</summary>
        public static bool IsOpenBattlegroundAssembling(Group group)
        {
            if (group == null) return false;
            using (EnterSync())
                return TryGetSession(group, out Session session) && IsAssemblyPhase(session.Phase);
        }

        /// <summary>The group is an open-BG roster unit; it never enlists in a world RvR event while it is one.</summary>
        public static bool IsOpenBattlegroundParty(Group group)
        {
            if (group == null) return false;
            using (EnterSync())
                return TryGetSession(group, out Session session) && session.OpenBattleground;
        }

        /// <summary>The unit has left the open BG roster: its group (if it still exists) is an ordinary warband again.</summary>
        public static void ReleaseOpenBattlegroundParty(Group group)
        {
            if (group == null) return;
            using (EnterSync())
                if (TryGetSession(group, out Session session))
                    session.OpenBattleground = false;
        }
    }
}
