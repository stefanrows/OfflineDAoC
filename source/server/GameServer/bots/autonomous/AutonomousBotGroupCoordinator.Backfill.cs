using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DOL.GS
{
    /// <summary>
    /// Warbands keep recruiting after they leave, as players did: "we're
    /// out at the bridge, come join". Three sources, in order:
    /// guildmates roaming nearby, guildmates waiting LFG at the border keep
    /// (they run out to the group), and a second small group of the same guild
    /// met in the field, which merges into the bigger one.
    /// </summary>
    public static partial class AutonomousBotGroupCoordinator
    {
        public const int FieldBackfillIntervalMilliseconds = 15_000;
        public const int NearbyGuildmateRadius = 3_000;
        public const int MergeRadius = 2_500;
        public const string LfgProperty = "OfflineDAoC.RvrLfgWaiting";

        private static readonly ConditionalWeakTable<Session, StrongBox<long>> NextFieldBackfill = new();
        // Members moving between two groups of their guild keep their RvR tour.
        private static readonly HashSet<long> TransferringMembers = new();

        private static bool IsTransferring(GameBot bot) => TransferringMembers.Contains(MemberKey(bot));

        public const int TargetChoiceTimeoutMilliseconds = 5 * 60_000;
        private static readonly ConditionalWeakTable<Session, StrongBox<long>> ChoosingSince = new();

        /// <summary>
        /// A party that cannot agree on a target for five minutes breaks up
        /// instead of standing in town forever; its members pick new work.
        /// </summary>
        private static void ExpireStalledTargetChoices(long now)
        {
            foreach (Session session in Sessions.Values.ToArray())
            {
                StrongBox<long> since = ChoosingSince.GetValue(session, _ => new StrongBox<long>(0));
                if (session.Ending || session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve ||
                    session.Phase != "Choosing group target")
                {
                    since.Value = 0;
                    continue;
                }
                if (since.Value == 0)
                    since.Value = now;
                else if (now - since.Value >= TargetChoiceTimeoutMilliseconds)
                    FinishGroupTask(session, "No shared target was chosen within five minutes");
            }
        }

        private static bool FieldActive(Session session) =>
            !session.Ending && session.ObjectiveKind == eAutonomousObjectiveKind.RvR &&
            session.TaskClock.HasStarted && !session.Recovery.IsRegrouping &&
            AutonomousRealmRaid.GetView(session.Group) == null;

        private static void BackfillFieldWarbands(GameBot[] available, ref int availableSlots)
        {
            long now = GameLoop.GameLoopTime;
            int probes = 0;
            foreach (Session session in Sessions.Values.Where(FieldActive).OrderBy(session => session.CreatedTick).ToArray())
            {
                StrongBox<long> next = NextFieldBackfill.GetValue(session, _ => new StrongBox<long>(0));
                if (now < next.Value)
                    continue;
                next.Value = now + FieldBackfillIntervalMilliseconds;

                GameBot leader = session.Leader;
                GameBot[] members = BotMembers(session.Group);
                int maximum = RecruitmentTarget(leader, session.ObjectiveKind);
                if (leader == null || !leader.IsAlive || members.Length >= maximum)
                    continue;

                if (TryMergeSmallerWarband(session, leader, members, maximum))
                    members = BotMembers(session.Group);

                Vector3 leaderPosition = new(leader.X, leader.Y, leader.Z);
                var candidates = available.Where(candidate => candidate.Group == null && candidate.IsAlive &&
                        AutonomousObjectiveAssignments.Is(candidate, eAutonomousObjectiveKind.RvR) &&
                        AutonomousCrewManager.AreInSameCrew(leader, candidate) &&
                        members.All(member => LevelsCompatible(member.Level, candidate.Level)))
                    .Select(candidate => (Bot: candidate,
                        Nearby: candidate.CurrentRegionID == leader.CurrentRegionID &&
                                candidate.IsWithinRadius(leader, NearbyGuildmateRadius),
                        Lfg: candidate.TempProperties.GetProperty<bool>(LfgProperty)))
                    .Where(entry => entry.Nearby || entry.Lfg)
                    .OrderBy(entry => entry.Nearby ? 0 : 1)
                    .ThenBy(entry => RvrRecruitmentPriority(members, entry.Bot, maximum))
                    .ToList();

                bool changed = false;
                foreach (var entry in candidates)
                {
                    if (members.Length >= maximum || availableSlots <= 0 || probes >= 6)
                        break;
                    // A nearby guildmate walks over; an LFG member must be able to run out.
                    if (!entry.Nearby)
                    {
                        probes++;
                        if (!CanReachRecruitmentPoint(entry.Bot, leader.CurrentRegionID, leaderPosition))
                            continue;
                    }
                    if (!session.Group.AddMember(entry.Bot))
                        continue;
                    entry.Bot.TempProperties.RemoveProperty(LfgProperty);
                    members = [.. members, entry.Bot];
                    availableSlots--;
                    changed = true;
                    Log.Info($"AUTONOMOUS_WARBAND_BACKFILL group={session.Id} joined=\"{entry.Bot.Name}\" " +
                             $"source={(entry.Nearby ? "nearby" : "border-keep-lfg")} size={members.Length}");
                }
                if (changed)
                {
                    session.LockedSize = members.Length;
                    WriteSessionMetadata(session, members);
                }
            }
        }

        /// <summary>
        /// Two small groups of one guild meeting in the field become one: the
        /// smaller group's members move over without ending their RvR tour.
        /// </summary>
        private static bool TryMergeSmallerWarband(Session session, GameBot leader, GameBot[] members, int maximum)
        {
            Session other = Sessions.Values.Where(candidate => candidate != session && FieldActive(candidate) &&
                    candidate.Leader is { IsAlive: true } otherLeader &&
                    otherLeader.CurrentRegionID == leader.CurrentRegionID &&
                    otherLeader.IsWithinRadius(leader, MergeRadius) &&
                    AutonomousCrewManager.AreInSameCrew(leader, otherLeader) &&
                    !AutonomousRvrEventLayer.IsBattleForce(candidate.Id, GameLoop.GameLoopTime))
                .FirstOrDefault(candidate =>
                {
                    GameBot[] theirs = BotMembers(candidate.Group);
                    return theirs.Length <= members.Length && members.Length + theirs.Length <= maximum &&
                           theirs.All(bot => bot.IsAlive && !bot.InCombat &&
                               members.All(member => LevelsCompatible(member.Level, bot.Level)));
                });
            if (other == null)
                return false;

            GameBot[] movers = BotMembers(other.Group);
            foreach (GameBot mover in movers)
                TransferringMembers.Add(MemberKey(mover));
            try
            {
                FinishGroupTask(other, "Merged into a larger warband of the same guild");
                foreach (GameBot mover in movers)
                    if (mover.Group == null && mover.IsAlive)
                        session.Group.AddMember(mover);
            }
            finally
            {
                foreach (GameBot mover in movers)
                    TransferringMembers.Remove(MemberKey(mover));
            }
            Log.Info($"AUTONOMOUS_WARBAND_MERGED into={session.Id} from={other.Id} moved={movers.Length} " +
                     $"size={BotMembers(session.Group).Length}");
            return true;
        }
    }
}
