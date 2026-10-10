using System;
using System.Numerics;
using DOL.GS.ServerRules;

namespace DOL.GS
{
    /// <summary>Factory for temporary opponents; the battleground manager owns their lifetime.</summary>
    public static class BattlegroundEncounterActor
    {
        public static GameBot Spawn(ushort region, int x, int y, int z, byte classId,
            byte level, string name, Guild guild = null, Group group = null) =>
            TrySpawn(region, x, y, z, classId, level, name, guild, group, out _);

        // On failure, failure names the first step that produced no actor: snap, add_to_world or group_join.
        public static GameBot TrySpawn(ushort region, int x, int y, int z, byte classId,
            byte level, string name, Guild guild, Group group, out string failure)
        {
            failure = null;
            Zone zone = WorldMgr.GetRegion(region)?.GetZone(x, y);
            var nav = PathfindingProvider.Instance;
            Vector3 position = new(x, y, z);
            if (zone?.IsBG != true || !nav.IsAvailable || !nav.HasNavmesh(zone) ||
                !nav.TrySnapToMesh(zone, ref position, 100) || Math.Abs(position.Z - z) > 100)
            {
                failure = "snap";
                return null;
            }
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
                AttachBrain(bot);
                if (!bot.AddToWorld())
                {
                    failure = "add_to_world";
                    bot.Delete();
                    return null;
                }
                if (group != null && !group.AddMember(bot))
                {
                    failure = "group_join";
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

        // The brain captures the actor's zone and start point when it is built. Before AddToWorld, GameNPC.X,
        // Y and Z read the movement component, which still holds (0, 0, 0) until its placement is synchronized,
        // so the synchronization must come first.
        public static BattlegroundEncounterBrain AttachBrain(GameBot bot)
        {
            bot.SynchronizePositionForLoginValidation();
            bot.BattlegroundEncounter = new BattlegroundEncounterBrain(bot);
            return bot.BattlegroundEncounter;
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
            SetGoalFailure(bot, goal, target) == null;

        // The first failing step of SetGoal, or null when the goal was accepted.
        public static string SetGoalFailure(GameBot bot, Point3D goal, GameLiving target = null) =>
            bot?.BattlegroundEncounter == null ? "no_brain" : bot.BattlegroundEncounter.TrySetGoal(goal, target);

        // Mirrors Spawn and SetGoal without building an actor, so a squad with no provable route creates no bots.
        // Returns the first failing step, or null when a squad may spawn at origin and walk to goal.
        public static string RouteFailure(ushort region, Point3D origin, Point3D goal)
        {
            if (goal == null) return "goal_null";
            Region reg = WorldMgr.GetRegion(region);
            Zone zone = reg?.GetZone(origin.X, origin.Y);
            var nav = PathfindingProvider.Instance;
            if (zone?.IsBG != true) return "origin_zone";
            if (!nav.IsAvailable || !nav.HasNavmesh(zone)) return "navmesh";
            Vector3 spawn = new(origin.X, origin.Y, origin.Z);
            if (!nav.TrySnapToMesh(zone, ref spawn, 100) || Math.Abs(spawn.Z - origin.Z) > 100) return "origin_snap";
            if (reg.GetZone(goal.X, goal.Y) != zone) return "goal_zone";
            // A spawned actor stands on the truncated snapped point, and its route starts there.
            Vector3 from = new((int)spawn.X, (int)spawn.Y, (int)spawn.Z);
            return BattlegroundEncounterBrain.ProveRouteFailure(zone, from, goal, out _);
        }
    }
}
