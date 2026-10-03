using System;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    private readonly AutonomousFrontierThreatPolicy _frontierThreat = new();

    // Called before optional upkeep and the world controller's rendezvous,
    // recovery and follow returns. Assignment/level do not restrict defense.
    // It starts real combat without replacing the bot's durable task or event.
    public bool TryEngageFrontierThreat(BotBrain brain)
    {
        using var profile = BotThinkProfiler.Measure(BotThinkPhase.FrontierThreat);
        GameBot bot = brain?.BotBody;
        // Note when an RvR bot leaves its border hub (departure truce).
        AutonomousHubDeparture.Observe(bot, WorldSimulationClock.UtcNow);
        bool defending = AutonomousRvrDefense.IsCommittedDefender(bot);
        bool marching = AutonomousSiegeMarch.IsMarching(bot);
        bool siegeFighter = !marching && AutonomousRvrDefense.IsCommittedSiegeFighter(bot);
        GameLiving previousEngine = defending ? bot.TargetObject as GameSiegeWeapon ?? _siegeWeapon?.TargetObject as GameSiegeWeapon : null;
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.IsPlayerLedGroup ||
            !bot.IsAlive || bot.ObjectState != GameObject.eObjectState.Active || bot.IsReturningAfterRelease ||
            bot.IsInvulnerableToAttack ||
            bot.IsOnStableMasterRoute || !IsInFrontier(bot) || IsSafeArea(bot) ||
            (brain.HasAggro || bot.InCombat || bot.IsAttacking) && previousEngine == null ||
            brain.FSM.GetCurrentState()?.StateType == eFSMStateType.PASSIVE ||
            !_frontierThreat.Due(GameLoop.GameLoopTime, bot.DatabaseID)) return false;

        var nav = PathfindingProvider.Instance;
        if (!nav.IsAvailable || !nav.HasNavmesh(bot.CurrentZone)) return false;
        // A retreating group only defends itself; it does not turn to chase.
        bool mayHunt = AutonomousPvpOpportunityPolicy.MayHunt(bot) &&
            !AutonomousRvrDoctrineRuntime.IsRetreating(bot, out _);
        // While the leader watches a fight (P2) nobody opens one on his own;
        // self-defence (InOurFight) still answers. The leader's decision hands
        // over the one target to open with.
        bool observing = AutonomousRvrObserve.IsObserving(bot);
        GameLiving decided = AutonomousRvrObserve.TakeEngageTarget(bot);
        bool InOurFight(GameLiving target) => BotPvpCrowdControl.IsInFightWith(bot, target, null);
        bool Eligible(GameLiving target) => target != bot && !target.IsStealthed &&
            (marching ? AutonomousSiegeMarch.IsPartyThreat(bot, target) :
                siegeFighter || InOurFight(target) || target == decided ||
                mayHunt && !observing && AutonomousPvpOpportunityPolicy.SuitableOpponent(bot, target)) &&
            target.ObjectState == GameObject.eObjectState.Active &&
            AutonomousRvrTargetPolicy.IsEligible(
                AutonomousRvrTargetPolicy.IsEnemyCombatant(bot, target) || target is GameSiegeWeapon or GameKeepGuard,
                target.IsAlive,
                target.CurrentRegionID == bot.CurrentRegionID, IsInFrontier(target), IsSafeArea(target),
                GameServer.ServerRules.IsAllowedToAttack(bot, target, true)) &&
            AutonomousRvrTargetPolicy.ShouldEngageGrey(bot, target, InOurFight(target));
        bool IsHeldByAlliedOperator(GameLiving target) => target.TargetObject is GameBot friendly &&
            PvpCombatant.AreAllied(bot, friendly) && bot.IsWithinRadius(friendly, 1000) &&
            BotSiegeRuntime.HoldingPosition(friendly);

        GameLiving[] nearby = bot.GetPlayersInRadius(TargetSearchRadius).Where(Eligible).Cast<GameLiving>()
            .Concat(bot.GetNPCsInRadius(TargetSearchRadius)
                .Where(npc => (npc is GameBot or GameSiegeWeapon ||
                    (siegeFighter ? AutonomousRvrDefense.IsCombatant(npc) :
                        npc.Brain is IControlledBrain pet && pet.GetLivingOwner() is IGamePlayer)) && Eligible(npc)))
            .Where(target => bot.GetDistanceTo(target) <= TargetSearchRadius)
            .Where(target => defending || !BotSiegeRuntime.Assigned(bot) || bot.IsWithinRadius(target, 450) && target is not GameSiegeWeapon)
            .OrderBy(target => IsHeldByAlliedOperator(target) ? 0 : 1)
            .ThenBy(bot.GetDistanceTo).ToArray();
        bool Visible(GameLiving target) => nav.HasLineOfSight(bot.CurrentZone,
            new(bot.X, bot.Y, bot.Z + 48), new(target.X, target.Y, target.Z + 48), nav.BlockingDoorAvoidanceFilters);
        var visible = siegeFighter
            ? _frontierThreat.VisiblePriority(nearby.Where(AutonomousRvrDefense.IsCombatant).ToArray(),
                nearby.OfType<GameSiegeWeapon>().Cast<GameLiving>().ToArray(), Visible)
            : _frontierThreat.Visible(nearby, target => nav.HasLineOfSight(bot.CurrentZone,
                new(bot.X, bot.Y, bot.Z), new(target.X, target.Y, target.Z), nav.DefaultFilters));
        var visibleTargets=visible.ToArray();
        var operatorThreats=visibleTargets.Where(target=>target.IsAttacking && IsHeldByAlliedOperator(target)).ToArray();
        // A stealth-doctrine assassin waits for a soft victim instead of the
        // nearest enemy; it still answers anyone already fighting it.
        GameLiving victim = null;
        bool stealthHunt = !(decided != null && Eligible(decided)) && operatorThreats.Length == 0 &&
            AutonomousRvrStealthLoop.TryPick(bot, visibleTargets, InOurFight, out victim);
        GameLiving enemy = decided != null && Eligible(decided) ? decided : stealthHunt ? victim :
            SelectDistributedRvrTarget(bot, operatorThreats.Length>0 ? operatorThreats : visibleTargets);
        if (enemy == null || !Eligible(enemy)) return false;
        if (previousEngine != null && enemy is GameSiegeWeapon) return false;

        if (defending)
        {
            // Combat wins over buying, deploying or operating a siege engine.
            // Release control without deleting the deployed equipment; normal
            // abandoned-engine reclamation remains available after combat.
            if (BotSiegeRuntime.Assigned(bot) || _siegeJobKeep != null) ReleaseSiegeJob(bot);
            _siegeNextAttempt = GameLoop.GameLoopTime + 10_000;
            if (previousEngine != null)
            {
                bot.StopAttack();
                brain.RemoveFromAggroList(previousEngine);
            }
            SetRvrStatus(bot, "Engaging keep attackers", _rvrDestination?.MonsterName ?? "Keep defense", enemy.Name);
        }

        bot.StopMovingOnPath();
        bot.StopMoving();
        bot.WakeRecoveryRest();
        AutonomousPvpEngagementTracker.Tag(bot, AutonomousPvpEngagementTracker.FrontierThreat);
        bot.TargetObject = enemy;
        brain.AddToAggroList(enemy, Math.Max(100, enemy.EffectiveLevel * 12));
        if (!brain.HasAggro) return false;
        AutonomousDefensivePull.OnThreat(bot, enemy);
        AutonomousBotGroupCoordinator.MarkCombatObserved(bot.Group);
        AutonomousRvrHeat.Record(bot);
        brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
        return true;
    }
}
