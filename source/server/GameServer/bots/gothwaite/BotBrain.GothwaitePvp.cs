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
            ThinkInterval = 750;
            GameBot bot = BotBody;
            // The driver owns movement here: a release walk left running would re-path against the mode's route.
            bot.HandOverReleaseReturnToGroup();
            if (!GothwaitePvpDriver.Turn(this, bot))
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
