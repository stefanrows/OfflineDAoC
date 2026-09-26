using System;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    public static partial class AutonomousBotGroupCoordinator
    {
        private static int RecruitmentTarget(GameBot leader, eAutonomousObjectiveKind kind) =>
            kind == eAutonomousObjectiveKind.GroupPve ? 8 :
                AutonomousPlayerBehavior.MaximumRvrGroupSize(AutonomousPlayerBehavior.TypeOf(leader?.PersistentRecord), leader?.Level ?? 1);

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
        private static void FillAssemblingParties(GameBot[] available, ref int availableSlots)
        {
            int probes = 0;
            long now = GameLoop.GameLoopTime;
            foreach (Session session in Sessions.Values.Where(session => !session.Ending &&
                         IsAssemblyPhase(session.Phase) && !session.TaskClock.HasStarted &&
                         now < session.RecruitmentDeadlineTick && AutonomousRealmRaid.GetView(session.Group) == null)
                     .OrderBy(session => session.CreatedTick).ToArray())
            {
                GameBot leader = session.Leader;
                GameBot[] members = BotMembers(session.Group);
                int maximum = RecruitmentTarget(leader, session.ObjectiveKind);
                if (leader == null || members.Length >= maximum || members.Any(member => !member.IsAlive || member.InCombat))
                    continue;
                var candidates = available.Where(candidate => candidate.Group == null &&
                        AutonomousObjectiveAssignments.Is(candidate, session.ObjectiveKind) &&
                        members.All(member => LevelsCompatible(member.Level, candidate.Level)) &&
                        (session.ObjectiveKind == eAutonomousObjectiveKind.GroupPve ||
                         AutonomousCrewManager.AreInSameCrew(leader, candidate) &&
                         RecruitmentTarget(candidate, session.ObjectiveKind) >= maximum))
                    .OrderBy(candidate => PickupRolePriority(leader,
                        members.FirstOrDefault(member => member != leader && member.CharacterClass != null &&
                            BotPartyRoles.IsHealingClass((eCharacterClass)member.CharacterClass.ID)), candidate))
                    .ThenBy(candidate => candidate.CurrentRegionID == session.RendezvousRegion ? 0 : 1)
                    .ThenBy(FormationWaitStartedUtc).ToArray();
                bool changed = false;
                foreach (GameBot candidate in candidates)
                {
                    if (availableSlots <= 0 || members.Length >= maximum || probes >= 8) break;
                    probes++;
                    if (!CanReachRecruitmentPoint(candidate, session.RendezvousRegion, session.Rendezvous)) continue;
                    GameBot[] expanded = [.. members, candidate];
                    if (!TryBuildRendezvousSlots(session, expanded)) continue;
                    if (!session.Group.AddMember(candidate))
                    {
                        TryBuildRendezvousSlots(session, members);
                        continue;
                    }
                    members = expanded;
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
