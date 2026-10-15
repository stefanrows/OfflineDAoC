using DOL.GS;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        private bool _openBgParkLogged;

        /// <summary>
        /// The turn of a reserved open-BG bot between deployments: stamp the RvR objective (the roster takes it as candidate),
        /// leave a plain bot group, teleport to the own border hub (MoveTo), rest and buff there. The world controller never runs here.
        /// </summary>
        private void ThinkOpenBgStandby()
        {
            ThinkInterval = 1500;
            GameBot bot = BotBody;
            // Like the other drivers: a release walk left running would re-path against the park.
            bot.HandOverReleaseReturnToGroup();
            // A stable route owns movement until its final point (as in ThinkCore): maintain and complete it, park afterwards.
            if (GothwaitePvpOpenBgStandby.RunsStableRoute(bot.IsOnStableMasterRoute))
            {
                bot.MaintainStableMasterRoute();
                if (!bot.IsMovingOnPath && bot.CurrentPathPoint == null)
                    bot.TryCompleteStableMasterRouteAfterArrival();
                return;
            }
            if (GothwaitePvpOpenBgStandby.NeedsRvrStamp(bot.PersistentRecord?.ObjectiveKind) && bot.PersistentRecord != null)
            {
                GothwaitePvpOpenBgStandby.Stamp(bot.PersistentRecord, bot.DatabaseID, WorldSimulationClock.UtcNow,
                    AutonomousObjectiveAssignments.RollRvrTenure());
                bot.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(bot);
            }
            if (bot.Group != null && GothwaitePvpOpenBgStandby.LeavesGroup(GothwaitePvpOpenBgStandby.GroupIsBotOnly(bot.Group),
                    AutonomousBotGroupCoordinator.IsOpenBattlegroundParty(bot.Group)))
                bot.Group.RemoveMember(bot, retainSingleRemainingMember: true);

            if (!bot.IsAlive)
                return;
            if (GothwaitePvpOpenBgStandby.TryHub(bot, out ushort hubRegion, out Point3D anchor, out Point3D spread))
            {
                bool atHub = GothwaitePvpOpenBgStandby.IsAtHub(bot, hubRegion, anchor);
                if (atHub)
                    _openBgParkLogged = false;
                else if (GothwaitePvpOpenBgStandby.NeedsPark(false, bot.IsAlive, bot.InCombat, bot.IsReturningAfterRelease) &&
                         !bot.IsOnStableMasterRoute)
                {
                    if (!_openBgParkLogged)
                    {
                        _openBgParkLogged = true;
                        log.Info($"GOTH_PVP_OPENBG_PARK bot=\"{bot.Name}\" realm={bot.Realm} region={hubRegion}");
                    }
                    bot.WakeRecoveryRest();
                    bot.StopMovingOnPath();
                    bot.StopMoving();
                    if (bot.MoveTo(hubRegion, spread.X, spread.Y, spread.Z, bot.Heading))
                    {
                        bot.ForcePathReplot();
                        return;
                    }
                }
                else if (atHub && !bot.IsCasting && !HasAggro && !bot.InCombat && !BotRestRecovery.BlocksRest(bot))
                    bot.BeginRecoveryRest();
            }

            AlreadyCheckedHeals = false;
            if (!Body.IsCasting && CheckHeals())
                return;
            if (!HasAggro && !Body.InCombat && TryMaintainTravelAndClassBuffs())
                return;
            FSM.Think();
        }
    }
}
