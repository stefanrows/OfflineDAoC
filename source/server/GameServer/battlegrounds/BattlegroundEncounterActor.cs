using System;
using System.Numerics;
using DOL.GS.ServerRules;

namespace DOL.GS
{
    /// <summary>Factory for temporary opponents; the battleground manager owns their lifetime.</summary>
    public static class BattlegroundEncounterActor
    {
        public static GameBot Spawn(ushort region, int x, int y, int z, byte classId,
            byte level, string name, Guild guild = null, Group group = null)
        {
            Zone zone = WorldMgr.GetRegion(region)?.GetZone(x, y);
            var nav = PathfindingProvider.Instance;
            Vector3 position = new(x, y, z);
            if (zone?.IsBG != true || !nav.IsAvailable || !nav.HasNavmesh(zone) ||
                !nav.TrySnapToMesh(zone, ref position, 100) || Math.Abs(position.Z - z) > 100)
                return null;
            GameBot bot = null;
            try
            {
                bot = new GameBot(classId, level, name)
                {
                    CurrentRegionID = region,
                    X = (int)position.X,
                    Y = (int)position.Y,
                    Z = (int)position.Z,
                    Guild = guild
                };
                bot.BattlegroundEncounter = new BattlegroundEncounterBrain(bot);
                bot.SynchronizePositionForLoginValidation();
                if (!bot.AddToWorld() || group != null && !group.AddMember(bot))
                {
                    bot.Delete();
                    return null;
                }
                return bot;
            }
            catch
            {
                bot?.Delete();
                throw;
            }
        }

        public static bool SetAllianceSponsor(GameBot bot, GameLiving sponsor)
        {
            GameLiving identity = PvpCombatant.Resolve(sponsor);
            if (bot?.IsBattlegroundEncounterBot != true || identity is GameBot { IsBattlegroundEncounterBot: true })
                return false;
            bot.BattlegroundAllianceSponsor = identity;
            return true;
        }

        public static bool SetGoal(GameBot bot, Point3D goal, GameLiving target = null) =>
            bot?.BattlegroundEncounter?.SetGoal(goal, target) == true;
    }
}
