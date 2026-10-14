using System;
using System.Linq;
using DOL.GS;
using DOL.GS.ServerRules;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        /// <summary>A held turn thinks again no sooner than this, so the end of a hold is not missed by more than a tenth of a second.</summary>
        private const int IncomingFireMinThinkMilliseconds = 100;

        // The hit that is pulling this bot into its combat state right now (set just before the state change, cleared after it),
        // and the reaction hold the state drew from it: the kind, and the game time it ends (0 = no hold). The pending pair is
        // written and read on the thread that changes the state; a hit handled on another entity's service thread while this
        // bot's own Think re-enters the combat state could at worst draw a hold of another hit's kind, or none (the FSM itself
        // has the same race), never leave the bot stuck: every hold is time-bound.
        private BotIncomingFireKind _pendingIncomingFire;
        private GameLiving _pendingIncomingFireAttacker;
        private BotIncomingFireKind _incomingFireKind;
        private long _incomingFireHoldUntil;

        /// <summary>
        /// Enters the combat state because of a hit of this kind (one of the bot's own, or a mate's) by this attacker. The hit is
        /// visible to <see cref="BeginIncomingFireHold"/> during the state change only, so no other entry into the combat state ever
        /// sees it; an entry that does not happen (already fighting, or nothing on the list) draws nothing.
        /// </summary>
        private void EnterAggroFromIncomingFire(BotIncomingFireKind kind, GameLiving attacker)
        {
            _pendingIncomingFire = kind;
            _pendingIncomingFireAttacker = attacker;
            try
            {
                if (FSM.GetCurrentState() != FSM.GetState(eFSMStateType.AGGRO))
                    FSM.SetCurrentState(eFSMStateType.AGGRO);
            }
            finally
            {
                _pendingIncomingFire = BotIncomingFireKind.None;
                _pendingIncomingFireAttacker = null;
            }
        }

        /// <summary>Called when the combat state is entered: draws the reaction hold for the pending hit, or none for any other entry.</summary>
        private void BeginIncomingFireHold()
        {
            int delay = BotIncomingFireReaction.Draw(BotBody, _pendingIncomingFire, _pendingIncomingFireAttacker);
            _incomingFireKind = delay > 0 ? _pendingIncomingFire : BotIncomingFireKind.None;
            _incomingFireHoldUntil = delay > 0 ? GameLoop.GameLoopTime + delay : 0;
            if (delay > 0 && log.IsDebugEnabled)
                log.Debug($"BOT_INCOMING_FIRE bot={Body?.Name} kind={_incomingFireKind} hold={delay}ms attacker={_pendingIncomingFireAttacker?.Name}");
        }

        /// <summary>Called when the combat state is left.</summary>
        private void EndIncomingFireHold()
        {
            _incomingFireKind = BotIncomingFireKind.None;
            _incomingFireHoldUntil = 0;
        }

        /// <summary>
        /// True while the bot is still reacting to the hit that drew it into combat: the drawn delay, then for a damage dealer that
        /// follows a caller the wait for its call (not when there is only one enemy to pick). An attacker in melee reach ends both:
        /// a hold that a far hit (a spell, an arrow, a mate's hit) drew at once, and the wait for the call whatever the hold's kind.
        /// A held turn shortens its own think interval to the time left (the brain sets the interval again on every turn), so the
        /// answer comes when the hold ends, not a combat cadence later.
        /// </summary>
        private bool HoldsIncomingFire()
        {
            if (_incomingFireHoldUntil <= 0)
                return false;
            long now = GameLoop.GameLoopTime;
            bool inReach = RecentDirectAttacker() is { } attacker && BotIncomingFireReaction.InMeleeReach(Body, attacker);
            bool pressed = _incomingFireKind != BotIncomingFireKind.Melee && inReach;
            long until;
            if (BotIncomingFireReaction.Holds(now, _incomingFireHoldUntil, pressed))
                until = _incomingFireHoldUntil;
            else if (!inReach && !IsHealer && !HoldsExclusiveSupportRole() &&
                BotIncomingFireReaction.AwaitsCall(BotBody, out bool follower) &&
                BotIncomingFireReaction.WaitsForCall(now, _incomingFireHoldUntil, follower, false, PlayerShapedEnemiesOnList()))
                until = _incomingFireHoldUntil + BotIncomingFireReaction.CallWaitMilliseconds;
            else
            {
                EndIncomingFireHold();
                return false;
            }
            ThinkInterval = (int)Math.Clamp(until - now, IncomingFireMinThinkMilliseconds, Math.Max(IncomingFireMinThinkMilliseconds, ThinkInterval));
            return true;
        }

        // The distinct player-shaped enemies on the aggro list (a pet counts as its owner).
        private int PlayerShapedEnemiesOnList() =>
            AggroList.Keys.Where(living => living != null && living.IsAlive)
                .Select(PvpCombatant.Resolve)
                .Where(fighter => fighter != null && fighter != Body)
                .Distinct()
                .Count();

        /// <summary>
        /// The direct attacker as the target choice should see it: unchanged for a bot outside the reaction rules; for a world bot
        /// only while it has no other live player-shaped target on its list (the attacker's own pet counts as the attacker), or the
        /// attacker is in melee reach. Otherwise null, and the kill order (where the attacker still competes by threat and weight)
        /// decides. A mob as the current target never holds the bot: players dropped the mob for a ganker at once.
        /// </summary>
        private GameLiving KillOrderAttacker(GameLiving directAttacker)
        {
            if (directAttacker == null || !BotIncomingFireReaction.Applies(BotBody))
                return directAttacker;
            bool hasOtherPlayer = Body.TargetObject is GameLiving { IsAlive: true } current && current != directAttacker &&
                PvpCombatant.Resolve(current) is { } fighter && fighter != PvpCombatant.Resolve(directAttacker) && AggroList.ContainsKey(current);
            return BotIncomingFireReaction.OverridesKillOrder(hasOtherPlayer, BotIncomingFireReaction.InMeleeReach(Body, directAttacker))
                ? directAttacker
                : null;
        }
    }
}
