using System;
using System.Linq;
using DOL.GS.ServerRules;

namespace DOL.GS
{
    /// <summary>Legal battleground opponents, shared by encounter actors and autonomous participants.</summary>
    public static class BattlegroundCombatTargets
    {
        public static GameLiving FindOpponent(GameBot bot, int range)
        {
            Zone zone = bot?.CurrentZone;
            if (zone == null) return null;
            ushort radius = (ushort)Math.Clamp(range, 0, ushort.MaxValue);
            return bot.GetPlayersInRadius(radius).Cast<GameLiving>()
                .Concat(bot.GetNPCsInRadius(radius).Where(npc => PvpCombatant.IsPlayerShaped(npc)))
                .Where(target => IsLegal(bot, target, zone) && !target.IsStealthed && !BotPvpCrowdControl.Protected(bot, target) &&
                    BotSiegeRuntime.Visible(bot, target))
                .OrderBy(bot.GetDistanceTo).FirstOrDefault();
        }

        public static bool IsLegal(GameBot bot, GameLiving target, Zone zone) => target != null && target != bot && target.IsAlive &&
            target.ObjectState == GameObject.eObjectState.Active && target.CurrentZone == zone &&
            GameServer.ServerRules.IsAllowedToAttack(bot, target, true);
    }
}
