using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    public static partial class AutonomousBotGroupCoordinator
    {
        private static void LogGuildRecruitment(GameBot leader, GameBot[] population, int desiredSize,
            int eligible, int reachable, int routeProbes, int routeBudgetUsed, string status)
        {
            // Exclusive first-rejection counts over the full live roster, not
            // just the already-filtered PvP pool. This makes missing population
            // distinguishable from occupied, differently assigned or busy peers.
            var rejected = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (GameBot candidate in population.Where(candidate => candidate != leader))
            {
                string reason = !candidate.IsAutonomousWorldBot || candidate.IsTemporaryGroupHelper || candidate.IsPlayerLedGroup
                    ? "excludedActor"
                    : !AutonomousCrewManager.AreInSameCrew(leader, candidate) ? "otherGuild"
                    : candidate.Group != null ? "alreadyGrouped"
                    : !AutonomousObjectiveAssignments.Is(candidate, eAutonomousObjectiveKind.RvR) ? "otherTask"
                    : !LevelsCompatible(leader.Level, candidate.Level) ? "levelMismatch"
                    : RecruitmentTarget(candidate, eAutonomousObjectiveKind.RvR) < desiredSize ? "partySizePreference"
                    : !candidate.IsAlive ? "dead"
                    : candidate.CurrentRegion == null ? "noRegion"
                    : AutonomousRealmRaid.IsReserved(candidate) ? "raidReserved"
                    : candidate.InCombat || candidate.IsAttacking ? "combat"
                    : candidate.IsOnStableMasterRoute ? "riding"
                    : AutonomousActivityScheduler.IsPveBlocked(candidate.PersistentRecord, WorldSimulationClock.UtcNow) ? "pveIntermission"
                    : "eligibleBeforeRoute";
                rejected[reason] = rejected.GetValueOrDefault(reason) + 1;
            }
            Log.Info("AUTONOMOUS_GUILD_RECRUITMENT " + JsonSerializer.Serialize(new
            {
                groupLeader = leader.DatabaseID, guild = leader.Guild?.Name ?? string.Empty,
                level = leader.Level, type = AutonomousPlayerBehavior.TypeOf(leader.PersistentRecord).ToString(),
                desiredSize, status, eligible, reachable, routeProbes, routeBudgetUsed,
                notProbed = Math.Max(0, eligible - routeProbes), rejected
            }));
        }

        private sealed record GuildRecruitmentOffer(GameBot Leader, GameBot[] Members, int Maximum, long Deadline);
        // Allocation can call this while holding its own lock. Publish immutable
        // offers instead of taking the coordinator lock in the opposite order.
        private static readonly ConcurrentDictionary<Group, GuildRecruitmentOffer> GuildRecruitmentOffers = new();

        public static bool HasOpenGuildRecruitment(GameBot candidate, GameBot peer)
        {
            Group group = peer?.Group;
            if (candidate == null || group == null || !GuildRecruitmentOffers.TryGetValue(group, out var offer))
                return false;
            return GameLoop.GameLoopTime < offer.Deadline && offer.Leader.Group == group &&
                offer.Leader.IsAlive && !offer.Leader.IsPlayerLedGroup &&
                AutonomousCrewManager.AreInSameCrew(candidate, offer.Leader) &&
                RecruitmentTarget(candidate, eAutonomousObjectiveKind.RvR) >= offer.Maximum &&
                offer.Members.Length < offer.Maximum &&
                offer.Members.All(member => member.Group == group && LevelsCompatible(member.Level, candidate.Level));
        }

        private static void PublishGuildRecruitment(Session session, GameBot[] members)
        {
            int maximum = RecruitmentTarget(session.Leader, session.ObjectiveKind);
            if (session.ObjectiveKind == eAutonomousObjectiveKind.RvR && !session.Ending &&
                IsAssemblyPhase(session.Phase) && !session.TaskClock.HasStarted &&
                GameLoop.GameLoopTime < session.RecruitmentDeadlineTick && members.Length < maximum &&
                AutonomousRealmRaid.GetView(session.Group) == null)
            {
                if (!GuildRecruitmentOffers.TryGetValue(session.Group, out var previous) ||
                    previous.Leader != session.Leader || previous.Maximum != maximum ||
                    previous.Deadline != session.RecruitmentDeadlineTick || !previous.Members.SequenceEqual(members))
                    GuildRecruitmentOffers[session.Group] = new(session.Leader, members, maximum, session.RecruitmentDeadlineTick);
            }
            else
                GuildRecruitmentOffers.TryRemove(session.Group, out _);
        }

        private static int RecruitmentTarget(GameBot leader, eAutonomousObjectiveKind kind) =>
            kind == eAutonomousObjectiveKind.GroupPve ? 8 :
                AutonomousPlayerBehavior.MaximumRvrGroupSize(AutonomousPlayerBehavior.TypeOf(leader?.PersistentRecord), leader?.Level ?? 1);

        private static int RecruitmentRolePriority(GameBot[] members, GameBot candidate) =>
            candidate.CharacterClass == null ? 0 : PickupRolePriority((eCharacterClass)candidate.CharacterClass.ID,
                !members.Any(member => member.CharacterClass != null && BotPartyRoles.IsHealingClass((eCharacterClass)member.CharacterClass.ID)),
                !members.Any(member => member.CharacterClass != null && BotPartyRoles.For((eCharacterClass)member.CharacterClass.ID) == BotPartyRole.Tank));

        private static bool CanReachRecruitmentPoint(GameBot candidate, ushort region, Vector3 point)
        {
            if (candidate.InCombat || candidate.IsAttacking || candidate.IsOnStableMasterRoute)
                return false;
            if (candidate.CurrentRegionID == region)
                return AutonomousPickupPlanning.WithinTravelBudget(
                        Vector3.Distance(new(candidate.X, candidate.Y, candidate.Z), point) / Math.Max(1d, candidate.MaxSpeed) / 60d) &&
                    AutonomousBotTownTravel.CanReachTownPoint(candidate.CurrentRegion, candidate.CurrentZone,
                        new(candidate.X, candidate.Y, candidate.Z), point);
            return AutonomousBotTownTravel.TryGetTownRoute(candidate, region, point, out _, out _, out double minutes) &&
                AutonomousPickupPlanning.WithinTravelBudget(minutes);
        }

        // Fill spare seats before creating another small party. Only initial assembly
        // can recruit: an active grind, siege, raid or recovery keeps its own roster.
        private static void FillAssemblingParties(GameBot[] available, eAutonomousObjectiveKind objective, ref int availableSlots)
        {
            int probes = 0;
            long now = GameLoop.GameLoopTime;
            foreach (Session session in Sessions.Values.Where(session => !session.Ending && session.ObjectiveKind == objective &&
                         IsAssemblyPhase(session.Phase) && !session.TaskClock.HasStarted &&
                         now < session.RecruitmentDeadlineTick && AutonomousRealmRaid.GetView(session.Group) == null)
                     .OrderBy(session => session.CreatedTick).ToArray())
            {
                GameBot leader = session.Leader;
                GameBot[] members = BotMembers(session.Group);
                int maximum = RecruitmentTarget(leader, session.ObjectiveKind);
                if (leader == null || members.Length >= maximum || members.Any(member => !member.IsAlive || member.InCombat))
                    continue;
                foreach (long expired in session.RecruitmentRetryTicks.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray())
                    session.RecruitmentRetryTicks.Remove(expired);
                var candidates = available.Where(candidate => candidate.Group == null &&
                        !session.RecruitmentRetryTicks.ContainsKey(MemberKey(candidate)) &&
                        AutonomousObjectiveAssignments.Is(candidate, session.ObjectiveKind) &&
                        members.All(member => LevelsCompatible(member.Level, candidate.Level)) &&
                        (session.ObjectiveKind == eAutonomousObjectiveKind.GroupPve ||
                         AutonomousCrewManager.AreInSameCrew(leader, candidate) &&
                         RecruitmentTarget(candidate, session.ObjectiveKind) >= maximum))
                    .OrderBy(candidate => candidate.CurrentRegionID == session.RendezvousRegion ? 0 : 1)
                    .ThenBy(FormationWaitStartedUtc).ToList();
                int shortlisted = candidates.Count;
                int attempted = 0, routeRejected = 0, campRejected = 0, slotsRejected = 0, joinRejected = 0, added = 0;
                bool changed = false;
                while (candidates.Count > 0)
                {
                    if (availableSlots <= 0 || members.Length >= maximum || probes >= 8) break;
                    // Re-evaluate missing roles after each addition, so filling
                    // healing does not keep every healer ahead of the missing tank.
                    GameBot candidate = candidates.OrderBy(candidate => RecruitmentRolePriority(members, candidate)).First();
                    candidates.Remove(candidate);
                    probes++;
                    attempted++;
                    // Retry failed candidates later, allowing other guildmates to
                    // use the bounded probe budget on the next formation pass.
                    session.RecruitmentRetryTicks[MemberKey(candidate)] = now + 30_000;
                    if (!CanReachRecruitmentPoint(candidate, session.RendezvousRegion, session.Rendezvous))
                    {
                        routeRejected++;
                        continue;
                    }
                    GameBot[] expanded = [.. members, candidate];
                    if (session.ObjectiveKind == eAutonomousObjectiveKind.GroupPve &&
                        !string.IsNullOrEmpty(session.PreferredPickupCampId) &&
                        !AutonomousWorldBotController.IsPickupCampUsable(session.PreferredPickupCampId, expanded))
                    {
                        campRejected++;
                        continue;
                    }
                    if (!TryBuildRendezvousSlots(session, expanded))
                    {
                        slotsRejected++;
                        continue;
                    }
                    if (!session.Group.AddMember(candidate))
                    {
                        joinRejected++;
                        TryBuildRendezvousSlots(session, members);
                        continue;
                    }
                    members = expanded;
                    session.RecruitmentRetryTicks.Remove(MemberKey(candidate));
                    added++;
                    availableSlots--;
                    changed = true;
                    if (candidate.CurrentRegionID != session.RendezvousRegion)
                    {
                        session.RemoteMemberIds.Add(MemberKey(candidate));
                        session.RemoteMeetupDeadlineTick ??= session.CreatedTick + RemoteMeetupTimeoutMilliseconds;
                        session.RemoteMeetupDeadlineUtc = WorldSimulationClock.UtcNow.AddMilliseconds(
                            Math.Max(0, session.RemoteMeetupDeadlineTick.Value - now));
                    }
                }
                if (attempted > 0 || now >= session.NextRecruitmentDiagnosticTick)
                {
                    session.NextRecruitmentDiagnosticTick = now + 30_000;
                    Log.Info("AUTONOMOUS_GROUP_RECRUITMENT_RESULT " + JsonSerializer.Serialize(new
                    {
                        group = session.Id, objective = session.ObjectiveKind.ToString(),
                        size = members.Length, target = maximum, availableCandidates = available.Count(candidate => candidate.Group == null),
                        retryDeferred = session.RecruitmentRetryTicks.Count, shortlisted, attempted, added,
                        routeRejected, campRejected, slotsRejected, joinRejected,
                        unprobed = candidates.Count, availableSlots,
                        remainingSeconds = Math.Max(0, session.RecruitmentDeadlineTick - now) / 1000
                    }));
                }
                if (changed)
                {
                    session.LockedSize = members.Length;
                    session.PreferredLevelBonus = RollPreferredLevelBonus(members.Length);
                    session.IsCrossRealmPve = session.ObjectiveKind == eAutonomousObjectiveKind.GroupPve &&
                        members.Select(member => member.Realm).Distinct().Skip(1).Any();
                    if (session.ObjectiveKind == eAutonomousObjectiveKind.GroupPve)
                    {
                        AssignPveRoles(members, session.PveRoles);
                        session.Puller = ChooseLockedPvePuller(session, members);
                    }
                    RebaseAttendance(session, members);
                    WriteSessionMetadata(session, members);
                    Log.Info($"AUTONOMOUS_GROUP_RECRUITED group={session.Id} objective={session.ObjectiveKind} size={members.Length}");
                }
                if (probes >= 8) break;
            }
        }
    }
}
