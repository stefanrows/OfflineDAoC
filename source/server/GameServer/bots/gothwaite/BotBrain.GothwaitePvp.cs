using DOL.GS;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        /// <summary>
        /// Class routine for an autonomous world bot driven by the Gothwaite PvP mode. It mirrors
        /// ThinkAutonomousBattleground: the mode driver chooses intent, then heals, pets, chants,
        /// songs, buffs and the native class FSM act as usual. The world controller never runs here.
        /// </summary>
        private void ThinkGothwaitePvp()
        {
            // Stage 4: the whole turn of a mode bot is timed by the mode's own load meter (GOTH_PVP_LOAD, the status command).
            using (GothwaitePvpLoadMeter.MeasureTurn())
                ThinkGothwaitePvpTurn();
        }

        private void ThinkGothwaitePvpTurn()
        {
            ThinkInterval = 750;
            GameBot bot = BotBody;
            // The driver owns movement here: a release walk left running would re-path against the mode's route.
            bot.HandOverReleaseReturnToGroup();
            bool acted;
            using (BotThinkProfiler.Measure(BotThinkPhase.GothwaitePvpDriver))
                acted = GothwaitePvpDriver.Turn(this, bot);
            if (!acted)
                return;
            AutonomousRealmAbilityActives.UseActives(bot);
            AlreadyCheckedHeals = false;
            if (!Body.IsCasting && CheckHeals())
                return;
            if (AutonomousPetSupport.Maintain(Body, Body.TargetObject as GameLiving,
                ref _nextDeployablePetTick, out _))
                return;
            TryMaintainTankChant();
            if (TryMaintainClassicSongTwist())
                return;
            if (!HasAggro && !Body.InCombat && TryMaintainTravelAndClassBuffs())
                return;
            FSM.Think();
        }
    }
}
