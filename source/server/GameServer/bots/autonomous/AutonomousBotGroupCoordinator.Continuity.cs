using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS
{
    public static partial class AutonomousBotGroupCoordinator
    {
        // Task 70: a productive party keeps playing session after session; only a
        // real need (training, full bags, wipe, broken composition) ends it.
        public static bool CanContinuePveParty(eAutonomousObjectiveKind objective, int renewals,
            int memberCount, bool raid, bool ready, bool serviceNeeded, bool recovering,
            int wipePenalty, long experienceGained, long killsGained) =>
            objective == eAutonomousObjectiveKind.GroupPve &&
            IsOrdinaryPvePartySize(memberCount) && !raid && ready && !serviceNeeded &&
            !recovering && wipePenalty == 0 && (experienceGained > 0 || killsGained > 0);

        // Task 70: an RvR group's tour never runs out. The expired clock simply
        // restarts, so the warband keeps roaming together instead of disbanding.
        private static bool TryContinueRvrTask(Session session, GameBot[] members)
        {
            if (session.Ending || session.ObjectiveKind != eAutonomousObjectiveKind.RvR ||
                !session.TaskClock.HasExpired(GameLoop.GameLoopTime) || members.Length == 0)
                return false;
            session.Renewals++;
            session.TaskClock = new AutonomousGroupTaskClock(eAutonomousObjectiveKind.RvR);
            session.TaskClock.Start(GameLoop.GameLoopTime, WorldSimulationClock.UtcNow);
            WriteSessionMetadata(session, members);
            Log.Info($"AUTONOMOUS_GROUP_TASK_RENEWED group={session.Id} kind=RvR size={members.Length} " +
                $"renewals={session.Renewals}");
            return true;
        }

        private static bool TryContinueTask(Session session, GameBot[] members) =>
            TryContinueRvrTask(session, members) || TryContinuePveTask(session, members);

        private static bool TryContinuePveTask(Session session, GameBot[] members)
        {
            if (session.Ending || session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve ||
                !session.TaskClock.HasExpired(GameLoop.GameLoopTime))
                return false;
            bool ready = members.All(member => member.PersistentRecord != null && member.IsAlive &&
                member.HealthPercent >= 75 && !member.InCombat && !member.IsAttacking &&
                (member.Brain as BotBrain)?.HasAggro != true &&
                !member.IsTemporaryGroupHelper && !member.IsPlayerLedGroup &&
                member.CurrentRegionID == session.RendezvousRegion &&
                !AutonomousRealmRaid.IsReserved(member) &&
                AutonomousObjectiveAssignments.Is(member, eAutonomousObjectiveKind.GroupPve));
            bool serviceNeeded = members.Any(member => member.HasSpendableAutonomousTrainingPoints ||
                AutonomousBotEconomy.IsBackpackFull(member));
            long experience = members.Sum(member => member.Experience);
            long kills = members.Sum(member => (long)(member.PersistentRecord?.ObjectivePveKills ?? 0));
            if (!CanContinuePveParty(session.ObjectiveKind, session.Renewals, members.Length,
                    AutonomousRealmRaid.GetView(session.Group) != null, ready, serviceNeeded,
                    session.Recovery.IsRegrouping, session.WipePenalty,
                    experience - session.TaskStartingExperience, kills - session.TaskStartingKills) ||
                !HasRequiredPveComposition(session, members) ||
                !AutonomousWorldBotController.HasLocalPickupCamp(members, session.RendezvousRegion))
                return false;

            session.Renewals++;
            session.TaskClock = new AutonomousGroupTaskClock(eAutonomousObjectiveKind.GroupPve);
            // Reselect against current levels and live population: the old camp may
            // have become grey or depleted. Even target selection has a time limit.
            session.TaskClock.BeginTravel(GameLoop.GameLoopTime, WorldSimulationClock.UtcNow);
            session.Camp = null;
            session.PreferredPickupCampId = string.Empty;
            session.DungeonArrivalRegion = 0;
            session.DungeonInteriorStagingPoint = default;
            session.DungeonArrivalHoldUntilTick = 0;
            session.DungeonArrivalCompletedCampId = string.Empty;
            session.RecoveringBetweenPulls = false;
            session.Phase = "Choosing group target";
            foreach (GameBot member in members)
                AutonomousObjectiveAssignments.MarkPveTaskCompleted(member);
            WriteSessionMetadata(session, members);
            Log.Info($"AUTONOMOUS_GROUP_TASK_RENEWED group={session.Id} size={members.Length} " +
                $"renewals={session.Renewals} experienceGained={experience - session.TaskStartingExperience} " +
                $"killsGained={kills - session.TaskStartingKills}");
            return true;
        }
    }
}
