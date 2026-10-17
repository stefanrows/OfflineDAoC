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
            // The driver owns movement here, as the world controller does outside: a release
            // walk left running would re-path every tick against the driver's route (bug 127).
            bot.HandOverReleaseReturnToGroup();
            // Keep guards and other vicinity brains only think near a client or a bot that wakes them; the battleground
            // turn skipped the wake, so in a bots-only open BG the central keep's guards never fought (live run 2026-10-17).
            WakeNearbyNaturalAggroBrains();
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
