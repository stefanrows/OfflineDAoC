using System;
using System.Linq;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;

namespace DOL.GS
{
    /// <summary>Legal battleground opponents, shared by encounter actors and autonomous participants.</summary>
    public static class BattlegroundCombatTargets
    {
        public static GameLiving FindOpponent(GameBot bot, int range) => FindOpponent(bot, range, null);

        /// <summary>The nearest legal opponent that <paramref name="accept"/> (when given) also allows.</summary>
        public static GameLiving FindOpponent(GameBot bot, int range, Func<GameLiving, bool> accept) => FindOpponent(bot, range, accept, false);

        /// <summary>
        /// As above; <paramref name="keepGuards"/> also takes keep guards (never a portal keep's), ranked behind a
        /// player-shaped opponent at the same distance (<see cref="GuardRankPenalty"/>). A ranged bot also takes a target up
        /// on a wall (<see cref="BotSiegeRuntime.CanEngage"/>); a melee bot only what it can walk to.
        /// </summary>
        public static GameLiving FindOpponent(GameBot bot, int range, Func<GameLiving, bool> accept, bool keepGuards)
        {
            Zone zone = bot?.CurrentZone;
            if (zone == null) return null;
            ushort radius = (ushort)Math.Clamp(range, 0, ushort.MaxValue);
            return bot.GetPlayersInRadius(radius).Cast<GameLiving>()
                .Concat(bot.GetNPCsInRadius(radius).Where(npc => PvpCombatant.IsPlayerShaped(npc) ||
                    keepGuards && npc is GameKeepGuard { IsPortalKeepGuard: false }))
                .Where(target => IsLegal(bot, target, zone) && !target.IsStealthed && !BotPvpCrowdControl.Protected(bot, target) &&
                    (accept == null || accept(target)) && BotSiegeRuntime.CanEngage(bot, target))
                .OrderBy(target => bot.GetDistanceTo(target) + (target is GameKeepGuard ? GuardRankPenalty : 0)).FirstOrDefault();
        }

        /// <summary>Distance added to a keep guard when the nearest opponent is picked.</summary>
        public const int GuardRankPenalty = 600;

        public static bool IsLegal(GameBot bot, GameLiving target, Zone zone) => target != null && target != bot && target.IsAlive &&
            target.ObjectState == GameObject.eObjectState.Active && target.CurrentZone == zone &&
            GameServer.ServerRules.IsAllowedToAttack(bot, target, true);
    }
}
