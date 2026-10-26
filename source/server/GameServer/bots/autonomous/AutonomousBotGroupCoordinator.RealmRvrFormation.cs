using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS;

public static partial class AutonomousBotGroupCoordinator
{
    // RvR world (upstream 0.38c TryFormGroups, RvR half; owner 2026-10-26 "erst Original, dann messen"): warbands form as
    // in the original. The leader is the bot closest to its realm's border keep (realms take turns), the size is rolled
    // evenly from 1 to 8, members come from the leader's own region and realm, and 35 % of each realm's RvR roster stays
    // solo. Camlann keeps its player-type sizes, cross-region pickup probes, assembling-party fill and field backfill.
    private const long RealmRvrFormationIntervalMilliseconds = 15_000L;

    private static void TryFormRealmRvrGroups(GameBot[] activeRoster)
    {
        Dictionary<eRealm, int> initiallyGroupedByRealm = activeRoster
            .Where(bot => bot.Group != null)
            .GroupBy(bot => bot.Realm)
            .ToDictionary(group => group.Key, group => group.Count());

        GameBot[] available = activeRoster
            .Where(bot => bot.IsAlive && !bot.IsTemporaryGroupHelper && !bot.IsPlayerLedGroup && bot.Group == null && bot.CurrentRegion != null &&
                          !AutonomousRealmRaid.IsReserved(bot) &&
                          // The open BG roster owns its crew: a solo of it is never pulled into another party.
                          !AutonomousBattlegroundParticipation.IsOpenCrew(bot) &&
                          // siege recruits are merged into full parties by FormSiegeParties, not random warbands
                          !AutonomousRvrEventLayer.IsForceCommitted($"rvr-{bot.DatabaseID}", GameLoop.GameLoopTime) &&
                          AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR))
            .OrderBy(_ => Random.Shared.Next())
            .ToArray();
        var claimed = new HashSet<GameBot>();
        int rendezvousChecks = 0;
        // RvR stages at the realm's fixed border keep, so the leader is the
        // bot closest to it.
        var crossings = new Dictionary<(eRealm Realm, ushort Region), DbZonePoint>();
        IEnumerable<GameBot> leaders = AutonomousRvrStaging.ClosestToStagingFirst(available, bot => bot.Realm, bot =>
            AutonomousRvrStaging.TryGetNearestBorderKeep(bot, out AutonomousRvrStaging.BorderKeep keep, crossings)
                ? AutonomousWorldBotController.EstimateTravelMinutes(bot, keep.RegionId,
                    (int)keep.Position.X, (int)keep.Position.Y, crossings)
                : double.MaxValue);

        foreach (GameBot leader in leaders)
        {
            // Bad geometry must not turn one formation pass into thousands of
            // native queries. Continue with more candidates on the next pass.
            if (rendezvousChecks >= 6) break;
            if (claimed.Contains(leader))
                continue;
            // RvR reserves are realm-local: Albion grouping cannot consume the
            // independent Midgard/Hibernia roamer reserve, and vice versa.
            int leaderSlots = AvailableGroupSlotsForObjective(eAutonomousObjectiveKind.RvR,
                activeRoster.Count(candidate => candidate.Realm == leader.Realm),
                initiallyGroupedByRealm.GetValueOrDefault(leader.Realm));
            int largestAllowed = Math.Min(8, leaderSlots - claimed.Count(candidate => candidate.Realm == leader.Realm));
            // One realm's full reserve must not prevent a later realm from
            // creating its own compatible RvR roaming party.
            if (largestAllowed < 2)
                continue;
            // A one means this actor remains an independent roamer; two
            // through eight create an actual warband.
            int rolledSize = AutonomousRvrStaging.RollWarbandSize(WorldModelKind.Realm, largestAllowed, Random.Shared.NextDouble());
            if (rolledSize == 1)
                continue;
            GameBot[] compatible = available
                .Where(candidate => candidate != leader && !claimed.Contains(candidate) && candidate.Group == null &&
                                     AutonomousObjectiveAssignments.Is(candidate, eAutonomousObjectiveKind.RvR) &&
                                     RealmTravel.SharesParty(leader, candidate) &&
                                     candidate.CurrentRegionID == leader.CurrentRegionID &&
                                     LevelsCompatible(leader.Level, candidate.Level))
                .OrderBy(candidate => Math.Abs(candidate.Level - leader.Level))
                .ThenBy(candidate => candidate.GetDistanceTo(leader))
                .Take(rolledSize - 1)
                .ToArray();
            if (compatible.Length != rolledSize - 1)
                continue;

            var group = new Group(leader);
            GroupMgr.AddGroup(group);
            if (!group.AddMember(leader))
            {
                GroupMgr.RemoveGroup(group);
                continue;
            }
            claimed.Add(leader);
            foreach (GameBot candidate in compatible)
            {
                if (group.AddMember(candidate))
                    claimed.Add(candidate);
            }
            if (group.MemberCount < 2)
            {
                group.DisbandGroup();
                continue;
            }

            rendezvousChecks++;
            Session session = NewSession(group, leader, rolledSize, eAutonomousObjectiveKind.RvR);
            if (session == null)
            {
                GameBot[] unmatched = BotMembers(group);
                group.DisbandGroup();
                foreach (GameBot member in unmatched)
                    AutonomousObjectiveAssignments.BeginSoloAfterGroupTask(member, "No connected rendezvous is available");
                continue;
            }
            Sessions[group] = session;
            GameBot[] formedMembers = BotMembers(group);
            foreach (GameBot member in formedMembers)
                LastFormationAttemptTick.Remove(MemberKey(member));
            WriteSessionMetadata(session, formedMembers);
            Log.Info($"AUTONOMOUS_GROUP_FORMED group={session.Id} realm={GlobalConstants.RealmToName(leader.Realm)} " +
                     $"size={formedMembers.Length} lockedSize={session.LockedSize} objective={eAutonomousObjectiveKind.RvR} " +
                     $"members=\"{string.Join(",", formedMembers.Select(member => member.Name))}\" " +
                     $"rendezvous={session.RendezvousRegion}:{(int)session.Rendezvous.X},{(int)session.Rendezvous.Y},{(int)session.Rendezvous.Z} " +
                     "formation=upstream");
            if (claimed.Count >= 15)
                break;
        }
    }
}
