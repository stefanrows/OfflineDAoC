using DOL.GS;
using DOL.GS.Keeps;

namespace DOL.AI.Brain
{
    public class KeepGuardBrain : StandardMobBrain
    {
        protected GameKeepGuard _keepGuardBody;

        public override GameNPC Body
        {
            get => _keepGuardBody ?? base.Body;
            set
            {
                _keepGuardBody = value as GameKeepGuard;
                base.Body = value;
            }
        }

        public override int ThinkInterval => 500;

        /// <summary>
        /// Bug 146: a keep guard that chased a target this far from its post
        /// drops the fight and walks back, as 2003 keep guards did.
        /// </summary>
        public const int LEASH_RANGE = 3000;

        public KeepGuardBrain() : base()
        {
            FSM.Add(new GuardState_AGGRO(this));
            FSM.Add(new GuardState_RETURN_TO_SPAWN(this));
        }

        /// <summary>True when a guard in combat is farther than the leash from its spawn.</summary>
        public static bool ShouldLeash(double distanceFromSpawn, int threshold) =>
            threshold > 0 && distanceFromSpawn > threshold;

        public void SetAggression(int aggroLevel, int aggroRange)
        {
            AggroLevel = aggroLevel;
            AggroRange = aggroRange;
        }

        protected override void CheckPlayerAggro()
        {
            foreach (GamePlayer player in Body.GetPlayersInRadius((ushort) AggroRange))
            {
                if (!CanAggroTarget(player))
                    continue;

                if (Body is not GuardStealther && player.IsStealthed)
                    continue;

                if (player.effectListComponent.ContainsEffectForEffectType(eEffect.Shade))
                    continue;

                WarMapMgr.AddGroup((byte) player.CurrentZone.ID, player.X, player.Y, player.Name, (byte) player.Realm);
                SendAggroLosCheck(player, player);
                // We don't know if the LoS check will be positive, so we have to ask other players
            }
        }

        /// <summary>Open BG wall archers and casters shoot at bow range (owner decision 2026-10-17).</summary>
        public const int OpenBgShooterRange = 1500;

        private bool IsOpenBgShooter => Body is GuardArcher or GuardCaster && !(_keepGuardBody?.IsPortalKeepGuard ?? true) &&
            Body.CurrentRegionID == OpenBattlegroundMode.OpenBgRegion;

        /// <summary>The range at which this guard takes an autonomous bot (and a bot wakes it).</summary>
        public int BotAggroRange => IsOpenBgShooter ? System.Math.Max(AggroRange, OpenBgShooterRange) : AggroRange;

        private static readonly DOL.Logging.Logger OpenBgLog = DOL.Logging.LoggerManager.Create(typeof(KeepGuardBrain));
        private long _nextOpenBgAggroLog;

        // OPEN_BG_GUARD_AGGRO: an open BG keep guard took a bot, at most every 30 game seconds per guard (live check 2026-10-17).
        private void LogOpenBgAggro(GameNPC npc)
        {
            if (Body.CurrentRegionID != OpenBattlegroundMode.OpenBgRegion || GameLoop.GameLoopTime < _nextOpenBgAggroLog) return;
            _nextOpenBgAggroLog = GameLoop.GameLoopTime + 30_000;
            OpenBgLog.Info($"OPEN_BG_GUARD_AGGRO guard=\"{Body.Name}\" type={Body.GetType().Name} target=\"{npc.Name}\" " +
                $"dist={(int)Body.GetDistanceTo(npc)} rise={Body.Z - npc.Z} range={AggroRange}");
        }

        private long _nextOpenBgScanLog;
        private long _nextOpenBgStateLog;

        public override void Think()
        {
            base.Think();
            LogOpenBgState();
        }

        // OPEN_BG_GUARD_STATE: once a game minute per central open BG keep guard, its fight state (live check 2026-10-17: no
        // guard ever reached its aggro scan with a bot near, and none killed a bot).
        private void LogOpenBgState()
        {
            if (Body.CurrentRegionID != OpenBattlegroundMode.OpenBgRegion || (_keepGuardBody?.IsPortalKeepGuard ?? true) ||
                GameLoop.GameLoopTime < _nextOpenBgStateLog) return;
            _nextOpenBgStateLog = GameLoop.GameLoopTime + 60_000;
            int bots = 0;
            foreach (GameNPC npc in Body.GetNPCsInRadius(OpenBgShooterRange))
                if (npc is GameBot && npc.IsAlive) bots++;
            GameObject target = Body.TargetObject;
            OpenBgLog.Info($"OPEN_BG_GUARD_STATE guard=\"{Body.Name}\" type={Body.GetType().Name} alive={Body.IsAlive} " +
                $"state={FSM.GetCurrentState()?.StateType} aggro={AggroList.Count} target=\"{target?.Name}\" " +
                $"target_dist={(target == null ? -1 : (int)Body.GetDistanceTo(target))} attack_state={Body.attackComponent?.AttackState} " +
                $"casting={Body.IsCasting} in_combat={Body.InCombat} near_spawn={Body.IsNearSpawn} bots_1500={bots} z={Body.Z}");
        }

        // OPEN_BG_GUARD_SCAN: once a game minute per central-keep guard with bots near, where the bots fell out of its aggro check.
        private void LogOpenBgScan(int near, int friendly, int outOfRange, int unseen, int taken)
        {
            if (near == 0 || Body.CurrentRegionID != OpenBattlegroundMode.OpenBgRegion || (_keepGuardBody?.IsPortalKeepGuard ?? true) ||
                GameLoop.GameLoopTime < _nextOpenBgScanLog) return;
            _nextOpenBgScanLog = GameLoop.GameLoopTime + 60_000;
            OpenBgLog.Info($"OPEN_BG_GUARD_SCAN guard=\"{Body.Name}\" type={Body.GetType().Name} z={Body.Z} range={AggroRange}/{BotAggroRange} " +
                $"near={near} friendly={friendly} out_of_range={outOfRange} unseen={unseen} taken={taken}");
        }

        protected override void CheckNpcAggro()
        {
            int near = 0, friendly = 0, outOfRange = 0, unseen = 0, taken = 0;
            foreach (GameNPC npc in Body.GetNPCsInRadius((ushort)BotAggroRange))
            {
                GameBot autonomous = npc as GameBot ?? (npc.Brain as IControlledBrain)?.GetLivingOwner() as GameBot;
                if (autonomous != null)
                {
                    if (!npc.IsAlive || npc.IsStealthed) continue;
                    near++;
                    if (!CanAggroTarget(npc)) { friendly++; continue; }
                    if (!npc.IsWithinRadius(Body, AggroRange) && !IsOpenBgShooter) { outOfRange++; continue; }
                    // The navmesh ray is a walkability test: a guard up on the wall also takes a bot standing below it
                    // (owner report 2026-10-17: bots stood right in front of the open BG keep unharmed).
                    if (!BotSiegeRuntime.Visible(npc, Body) && !BotSiegeRuntime.ShootsUpAt(npc, Body)) { unseen++; continue; }
                    taken++;
                    AddToAggroList(npc);
                    LogOpenBgAggro(npc);
                    continue;
                }
                // Non-pet NPCs are ignored.
                if (npc is GameKeepGuard || npc.Brain == null || npc.Brain is not IControlledBrain npcBrain)
                    continue;

                GamePlayer player = npcBrain.GetPlayerOwner();

                if (player == null)
                    continue;

                if (!CanAggroTarget(npc))
                    continue;

                WarMapMgr.AddGroup((byte) player.CurrentZone.ID, player.X, player.Y, player.Name, (byte) player.Realm);
                SendAggroLosCheck(player, npc);
                // We don't know if the LoS check will be positive, so we have to ask other players.
            }
            LogOpenBgScan(near, friendly, outOfRange, unseen, taken);
        }

        public override bool CanAggroTarget(GameLiving target)
        {
            if (AggroLevel <= 0 || !GameServer.ServerRules.IsAllowedToAttack(Body, target, true))
                return false;

            GameBot autonomous = target as GameBot ?? ((target as GameNPC)?.Brain as IControlledBrain)?.GetLivingOwner() as GameBot;
            if (autonomous != null)
                return GameServer.KeepManager.IsEnemy(_keepGuardBody, autonomous, true);

            GamePlayer checkPlayer = null;

            if (target is GameNPC targetNpc && targetNpc.Brain is IControlledBrain targetBrain)
                checkPlayer = targetBrain.GetPlayerOwner();
            else if (target is GamePlayer targetPlayer)
                checkPlayer = targetPlayer;

            if (checkPlayer == null || !GameServer.KeepManager.IsEnemy(_keepGuardBody, checkPlayer, true))
                return false;

            return true;
        }
    }
}
