using System;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;

namespace DOL.GS
{
    /// <summary>Bounded intent and legal targets; damage remains owned by the native class brain.</summary>
    public sealed class BattlegroundEncounterBrain
    {
        private readonly GameBot _bot;
        private readonly Zone _zone;
        private Vector3 _goal;
        private GameLiving _target;
        private long _nextScan, _nextPath;

        internal BattlegroundEncounterBrain(GameBot bot)
        {
            _bot = bot;
            _zone = bot.CurrentZone;
            _goal = new(bot.X, bot.Y, bot.Z);
        }

        public bool SetGoal(Point3D goal, GameLiving target = null) => TrySetGoal(goal, target) == null;

        // The first failing check of a goal, or null when it was accepted. The names reach the squad log
        // as detail=spawn:<step>, so a live run shows which check refused the actor's goal.
        internal string TrySetGoal(Point3D goal, GameLiving target)
        {
            if (goal == null) return "goal_null";
            if (_zone == null) return "brain_zone_null";
            if (_bot.CurrentRegion?.GetZone(goal.X, goal.Y) != _zone) return "goal_zone_mismatch";
            if (target != null && target.CurrentZone != _zone) return "target_zone_mismatch";
            string failure = ProveRouteFailure(_zone, new(_bot.X, _bot.Y, _bot.Z), goal, out Vector3 position);
            if (failure != null) return failure;
            _goal = position;
            _target = target;
            _nextPath = 0;
            return null;
        }

        // The navmesh proof for a goal: snap it to the mesh, then require a full straight path from the
        // actor's point. Shared with the squad director, which proves a route before building any actor.
        internal static string ProveRouteFailure(Zone zone, Vector3 from, Point3D goal, out Vector3 position)
        {
            var nav = PathfindingProvider.Instance;
            position = new(goal.X, goal.Y, goal.Z);
            if (!nav.IsAvailable || !nav.HasNavmesh(zone)) return "navmesh";
            if (!nav.TrySnapToMesh(zone, ref position, 100) || Math.Abs(position.Z - goal.Z) > 100) return "goal_snap";
            Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[512];
            var path = nav.GetPathStraight(zone, from, position, nav.BlockingDoorAvoidanceFilters, nodes);
            return path.Status == PathfindingStatus.PathFound ? null : "path";
        }

        internal bool PrepareTurn(BotBrain brain)
        {
            var nav = PathfindingProvider.Instance;
            if (_bot.CurrentZone != _zone || !nav.IsAvailable || !nav.HasNavmesh(_zone))
            {
                _bot.Delete();
                return false;
            }
            if (!_bot.IsAlive || _bot.IsIncapacitated)
                return false;
            long now = GameLoop.GameLoopTime;
            if (now >= _nextScan)
            {
                _nextScan = now + 1500;
                GameLiving opponent = BattlegroundCombatTargets.FindOpponent(_bot, 2400);
                if (opponent != null)
                    brain.Attack(opponent);
            }
            if (brain.HasAggro || _bot.InCombat)
                return true;
            if (_target != null && Legal(_target) && !BotPvpCrowdControl.Protected(_bot, _target))
            {
                int range = _target is GameKeepDoor or GuardLord ? 280 : 2000;
                if (_bot.IsWithinRadius(_target, range) && BotSiegeRuntime.Visible(_bot, _target))
                {
                    brain.Attack(_target);
                    return true;
                }
            }
            if (now >= _nextPath && !_bot.IsCasting && _bot.GetDistanceTo(new Point3D((int)_goal.X, (int)_goal.Y, (int)_goal.Z)) > 150)
            {
                _nextPath = now + 3000;
                Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[512];
                var path = nav.GetPathStraight(_zone, new(_bot.X, _bot.Y, _bot.Z), _goal,
                    nav.BlockingDoorAvoidanceFilters, nodes);
                if (path.Status == PathfindingStatus.PathFound)
                    _bot.PathTo(_goal, _bot.MaxSpeed);
                else
                {
                    _bot.StopMovingOnPath();
                    _bot.StopMoving();
                }
            }
            return true;
        }

        private bool Legal(GameLiving target) => BattlegroundCombatTargets.IsLegal(_bot, target, _zone);
    }
}

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        private void ThinkBattlegroundEncounter()
        {
            ThinkInterval = 750;
            if (BotBody.BattlegroundEncounter?.PrepareTurn(this) != true)
                return;
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
