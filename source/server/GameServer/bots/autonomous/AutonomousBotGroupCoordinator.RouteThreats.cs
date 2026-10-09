using DOL.AI.Brain;

namespace DOL.GS
{

    public enum OutdoorRouteBlockerHandoffResult
    {
        NotHandled,
        WaitingForGroup,
        Started,
    }

    public static partial class AutonomousBotGroupCoordinator
    {
        /// <summary>Routes a leader-observed outdoor blocker to the locked PvE puller.</summary>
        public static OutdoorRouteBlockerHandoffResult TryHandoffOutdoorRouteBlocker(
            GameBot reporter, GameNPC blocker, GameBot expectedPuller)
        {
            if (reporter?.Group == null || blocker?.IsAlive != true || expectedPuller == null ||
                reporter.IsAutonomousWorldBot != true || reporter.IsTemporaryGroupHelper ||
                reporter.IsPersistentPlayerCompanion || reporter.IsPlayerLedGroup ||
                expectedPuller.IsAutonomousWorldBot != true || expectedPuller.IsTemporaryGroupHelper ||
                expectedPuller.IsPersistentPlayerCompanion || expectedPuller.IsPlayerLedGroup ||
                blocker.CurrentRegion != reporter.CurrentRegion ||
                blocker.CurrentZone != reporter.CurrentZone || reporter.CurrentZone?.IsDungeon == true)
                return OutdoorRouteBlockerHandoffResult.NotHandled;

            GameBot puller;
            using (EnterSync())
            {
                if (!TryGetSession(reporter.Group, out Session session) ||
                    session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve ||
                    AutonomousRealmRaid.GetView(reporter.Group) != null)
                    return OutdoorRouteBlockerHandoffResult.NotHandled;

                GameBot[] members = BotMembers(session.Group);
                GameBot leader = ChooseLeader(session, members);
                puller = ChoosePuller(session, members);
                if (leader != reporter || puller != expectedPuller || puller == reporter ||
                    puller?.Brain is not BotBrain)
                    return OutdoorRouteBlockerHandoffResult.NotHandled;
                if (!AutonomousDungeonPolicy.CanHandoffRouteBlocker(
                        true, session.Phase, members.Length,
                        HasRequiredPveComposition(session, members), true, puller.IsAlive,
                        puller.CurrentRegion == reporter.CurrentRegion && puller.CurrentZone == reporter.CurrentZone,
                        puller.IsOnStableMasterRoute))
                    return OutdoorRouteBlockerHandoffResult.WaitingForGroup;
            }

            if (puller.Group != reporter.Group || !puller.IsAlive ||
                puller.CurrentRegion != reporter.CurrentRegion || puller.CurrentZone != reporter.CurrentZone ||
                !CanInitiateNewPull(puller, corridorBlocker: true))
                return OutdoorRouteBlockerHandoffResult.WaitingForGroup;
            if (blocker.ObjectState != GameObject.eObjectState.Active ||
                !GameServer.ServerRules.IsAllowedToAttack(puller, blocker, true) ||
                blocker.Brain is not StandardMobBrain mob || !mob.CanAggroTarget(puller))
                return OutdoorRouteBlockerHandoffResult.NotHandled;

            if (AutonomousDefensivePull.TryBegin(puller, blocker))
            {
                MarkCombatObserved(puller.Group);
                return OutdoorRouteBlockerHandoffResult.Started;
            }

            puller.StopMovingOnPath();
            puller.StopMoving();
            puller.TargetObject = blocker;
            BotBrain brain = (BotBrain)puller.Brain;
            brain.AddToAggroList(blocker, System.Math.Max(25, blocker.EffectiveLevel * 10));
            brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
            MarkCombatObserved(puller.Group);
            return OutdoorRouteBlockerHandoffResult.Started;
        }
    }

}
