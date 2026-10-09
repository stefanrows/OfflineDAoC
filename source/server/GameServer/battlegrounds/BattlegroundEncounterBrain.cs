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

        public bool SetGoal(Point3D goal, GameLiving target = null)
        {
            if (goal == null || _zone == null ||
                _bot.CurrentRegion?.GetZone(goal.X, goal.Y) != _zone ||
                target != null && target.CurrentZone != _zone)
                return false;
            var nav = PathfindingProvider.Instance;
            Vector3 position = new(goal.X, goal.Y, goal.Z);
            if (!nav.IsAvailable || !nav.HasNavmesh(_zone) ||
                !nav.TrySnapToMesh(_zone, ref position, 100) || Math.Abs(position.Z - goal.Z) > 100)
                return false;
            Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[512];
            var path = nav.GetPathStraight(_zone, new(_bot.X, _bot.Y, _bot.Z), position,
                nav.BlockingDoorAvoidanceFilters, nodes);
            if (path.Status != PathfindingStatus.PathFound)
                return false;
            _goal = position;
            _target = target;
            _nextPath = 0;
            return true;
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
                GameLiving opponent = _bot.GetPlayersInRadius(2400).Cast<GameLiving>()
                    .Concat(_bot.GetNPCsInRadius(2400).Where(npc => PvpCombatant.IsPlayerShaped(npc)))
                    .Where(target => Legal(target) && !target.IsStealthed && !BotPvpCrowdControl.Protected(_bot, target) &&
                        BotSiegeRuntime.Visible(_bot, target))
                    .OrderBy(_bot.GetDistanceTo).FirstOrDefault();
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

        private bool Legal(GameLiving target) => target != _bot && target.IsAlive &&
            target.ObjectState == GameObject.eObjectState.Active && target.CurrentZone == _zone &&
            GameServer.ServerRules.IsAllowedToAttack(_bot, target, true);
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
