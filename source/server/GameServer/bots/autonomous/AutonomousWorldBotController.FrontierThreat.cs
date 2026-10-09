using System;
using System.Collections.Generic;
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
        // The one-second actor cadence owns force, area and candidate work.
        // It must precede keep-plan/coordination lookups even on rejected turns;
        // incoming attack events still enter the brain's normal defense path.
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.IsPlayerLedGroup ||
            !bot.IsAlive || bot.ObjectState != GameObject.eObjectState.Active || bot.IsReturningAfterRelease ||
            bot.IsInvulnerableToAttack || bot.IsOnStableMasterRoute || !IsInFrontier(bot) ||
            brain.FSM.GetCurrentState()?.StateType == eFSMStateType.PASSIVE ||
            !_frontierThreat.Due(GameLoop.GameLoopTime, bot.DatabaseID)) return false;

        bool defending = AutonomousRvrDefense.IsCommittedDefender(bot);
        GameLiving previousEngine = defending ? bot.TargetObject as GameSiegeWeapon ?? _siegeWeapon?.TargetObject as GameSiegeWeapon : null;
        if ((brain.HasAggro || bot.InCombat || bot.IsAttacking) && previousEngine == null || IsSafeArea(bot)) return false;
        bool marching = AutonomousSiegeMarch.IsMarching(bot);
        bool siegeFighter = !marching && AutonomousRvrDefense.IsCommittedSiegeFighter(bot);

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
        // Capture the side once, rather than allocate/lock its group roster
        // for every ally, pet and opponent in the entire nearby crowd.
        GameLiving[] side = bot.Group?.GetMembersInTheGroup().Append(bot).Distinct().ToArray() ?? [bot];
        var sideMembers = new HashSet<GameLiving>(side);
        bool AttacksOurSide(GameLiving target) => target != null && (target.IsAttacking || target.IsCasting) &&
            target.TargetObject is GameLiving victim && sideMembers.Contains(PvpCombatant.Resolve(victim) ?? victim);
        bool InOurFight(GameLiving target)
        {
            GameLiving enemy = PvpCombatant.Resolve(target);
            return enemy != null && (AttacksOurSide(target) || enemy != target && AttacksOurSide(enemy) ||
                side.Any(member => (member.IsAttacking || member.IsCasting) &&
                    PvpCombatant.Resolve(member.TargetObject as GameLiving) == enemy));
        }
        bool recentPartyAttack = marching && side.Any(member => member?.IsAlive == true &&
            member.CurrentRegionID == bot.CurrentRegionID && member.IsWithinRadius(bot, 2500) &&
            member.LastAttackedByEnemyTickPvP > 0 && GameLoop.GameLoopTime - member.LastAttackedByEnemyTickPvP < 15_000);
        bool PartyThreat(GameLiving target) => AttacksOurSide(target) ||
            AttacksOurSide(PvpCombatant.Resolve(target)) || recentPartyAttack && InOurFight(target);
        bool BasicCandidate(GameLiving target) => target != null && target != bot && !target.IsStealthed && target.IsAlive &&
            target.ObjectState == GameObject.eObjectState.Active && target.CurrentRegionID == bot.CurrentRegionID &&
            IsInFrontier(target) && bot.IsWithinRadius(target, TargetSearchRadius) &&
            (target is GameSiegeWeapon or GameKeepGuard || AutonomousRvrTargetPolicy.IsEnemyCombatant(bot, target));
        bool Eligible(GameLiving target)
        {
            // Short-circuit hostility/death/range before safe-area or native
            // permissions. Passing booleans to IsEligible evaluated all of
            // those expensive checks even for hundreds of nearby allies.
            if (!BasicCandidate(target) || IsSafeArea(target) ||
                !GameServer.ServerRules.IsAllowedToAttack(bot, target, true)) return false;
            bool inOurFight = InOurFight(target);
            return (marching ? PartyThreat(target) : siegeFighter || inOurFight || target == decided ||
                    mayHunt && !observing && AutonomousPvpOpportunityPolicy.SuitableOpponent(bot, target)) &&
                AutonomousRvrTargetPolicy.ShouldEngageGrey(bot, target, inOurFight);
        }
        bool IsHeldByAlliedOperator(GameLiving target) => target.TargetObject is GameBot friendly &&
            PvpCombatant.AreAllied(bot, friendly) && bot.IsWithinRadius(friendly, 1000) &&
            BotSiegeRuntime.HoldingPosition(friendly);
        bool siegeAssigned = BotSiegeRuntime.Assigned(bot);
        GameLiving[] nearby = bot.GetPlayersInRadius(TargetSearchRadius).Cast<GameLiving>()
            .Concat(bot.GetNPCsInRadius(TargetSearchRadius)
                .Where(npc => npc is GameBot or GameSiegeWeapon ||
                    (siegeFighter ? AutonomousRvrDefense.IsCombatant(npc) :
                        npc.Brain is IControlledBrain pet && pet.GetLivingOwner() is IGamePlayer)))
            .Where(BasicCandidate)
            .Where(target => defending || !siegeAssigned || bot.IsWithinRadius(target, 450) && target is not GameSiegeWeapon)
            // Real attackers are first even when an ineligible crowd precedes
            // them by distance. The remaining policy and geometry is bounded.
            .OrderBy(target => IsHeldByAlliedOperator(target) ? 0 :
                AttacksOurSide(target) || AttacksOurSide(PvpCombatant.Resolve(target)) ? 1 : 2)
            .ThenBy(bot.GetDistanceTo).ToArray();
        bool Visible(GameLiving target) => nav.HasLineOfSight(bot.CurrentZone,
            new(bot.X, bot.Y, bot.Z + 48), new(target.X, target.Y, target.Z + 48), nav.BlockingDoorAvoidanceFilters);
        var visible = siegeFighter
            ? _frontierThreat.VisiblePriority(nearby.Where(AutonomousRvrDefense.IsCombatant).ToArray(),
                nearby.OfType<GameSiegeWeapon>().Cast<GameLiving>().ToArray(), Eligible, Visible)
            : _frontierThreat.Visible(nearby, Eligible, target => nav.HasLineOfSight(bot.CurrentZone,
                new(bot.X, bot.Y, bot.Z), new(target.X, target.Y, target.Z), nav.DefaultFilters));
        var visibleTargets = visible.ToArray();
        var operatorThreats = visibleTargets.Where(target => target.IsAttacking && IsHeldByAlliedOperator(target)).ToArray();
        // A stealth-doctrine assassin waits for a soft victim instead of the
        // nearest enemy; it still answers anyone already fighting it.
        GameLiving victim = null;
        bool stealthHunt = !(decided != null && Eligible(decided)) && operatorThreats.Length == 0 &&
            AutonomousRvrStealthLoop.TryPick(bot, visibleTargets, InOurFight, out victim);
        GameLiving enemy = decided != null && Eligible(decided) ? decided : stealthHunt ? victim :
            SelectDistributedRvrTarget(bot, operatorThreats.Length > 0 ? operatorThreats : visibleTargets);
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
