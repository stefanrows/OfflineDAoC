using DOL.GS;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        /// <summary>
        /// Class routine for an admitted autonomous participant inside its battleground.
        /// It mirrors ThinkBattlegroundEncounter: the driver chooses intent, then heals,
        /// pets, chants, songs, buffs and the native class FSM act as usual.
        /// </summary>
        private void ThinkAutonomousBattleground()
        {
            ThinkInterval = 750;
            GameBot bot = BotBody;
            if (!AutonomousBattlegroundDriver.Turn(this, bot))
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
