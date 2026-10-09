using System;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.Keeps;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    private AbstractGameKeep _guildDefenseKeep;

    /// <summary>Called before ordinary AI branches so combat, services, raid
    /// staging and existing ticket rides cannot postpone the emergency order.
    /// Immediate roadside defense temporarily owns movement, then recall resumes.</summary>
    public bool TryRunGuildKeepDefense(BotBrain brain)
    {
        GameBot bot = brain?.BotBody;
        AbstractGameKeep keep = AutonomousGuildKeepDefense.Target(bot);
        if (keep == null)
        {
            if (_guildDefenseKeep != null)
            {
                _guildDefenseKeep = null;
                ClearKeepObjective(bot);
                _rvrSharedEvent = false;
                _rvrIntent = AutonomousRvrEventLayer.Intent.Roam;
                bot.TempProperties.RemoveProperty("RvrWarbandIntent");
                bot.TempProperties.RemoveProperty("RvrDefendingKeep");
                bot.TempProperties.RemoveProperty("RvrEventForce");
                bot.TempProperties.RemoveProperty(AutonomousFrontierTransport.RequestKey);
                _camp = null;
                ResetRouteOrderState();
                _guildDefenseRouteRetry = 0;
                _observedGroup = null;
                _nextGroupPulseTick = _nextPlanTick = 0;
            }
            return false;
        }
        // Recall remains the destination, but a live attack is not a stale
        // PvE order. Run native defense before recall initialization can clear
        // aggro, including while serialized group withdrawal is pending.
        if (TryDefendDuringGuildRecall(brain, bot, keep)) return true;
        // Wait for serialized group withdrawal before any passage can board
        // members of a former mixed-guild raid alongside the recalled bot.
        if (!AutonomousGuildKeepDefense.IsPrepared(bot, keep)) return true;
        brain.AlreadyCheckedHeals = false;
        bot.EnsureAutonomousRecoveryTimers();
        if (_guildDefenseKeep != keep)
        {
            AutonomousGoalDiagnostics.End(bot, GoalAttemptEnd.Reassigned, "Guild keep under attack: immediate recall");
            ReleaseSiegeJob(bot);
            // Release deployed engines without destroying equipment. Cancel
            // this bot's old ride in place; never relocate it to the endpoint.
            bot.CompanionRam?.DismountCompanion(bot);
            if (bot.IsOnStableMasterRoute) bot.CompleteStableMasterRoute();
            ClearKeepObjective(bot);
            _guildDefenseKeep = keep;
            _camp = null;
            _groupDirective = null;
            _pendingStableChoice = null;
            _capitalTransit = null;
            _serviceNpc = null; _serviceKind = null; _trainingTrainer = null;
            _frontierPorter = null; _nextPorterSearch = 0;
            _dragonRallyPlanning = null; _dragonRallyRoute = null;
            _rvrSharedEvent = false;
            _nextMoveOrderTick = 0;
            _guildDefenseRouteRetry = 0;
            bot.TempProperties.RemoveProperty(AutonomousFrontierTransport.RequestKey);
            bot.TempProperties.RemoveProperty("RvrSupplying");
            brain.PrepareForTankPull();
            bot.StopCurrentSpellcast();
            ResetRouteOrderState();
            CancelDefensePetWork(bot);
        }
        _rvrDestination = new($"rvr-keep-{keep.KeepID}", keep.Name, keep.Name,
            keep.Region, keep.X, keep.Y, keep.Z, 1, false, true);
        _rvrIntent = AutonomousRvrEventLayer.Intent.DefendEvent;
        bot.TempProperties.SetProperty("RvrWarbandIntent", (int)_rvrIntent);
        bot.TempProperties.SetProperty("RvrDefendingKeep", keep.KeepID);
        bot.TempProperties.SetProperty("RvrEventForce", $"guild-defense-{bot.Guild.GuildID}-{keep.KeepID}");
        brain.ThinkInterval = 500;
        // Corpses retain the call. Native death/release timers and safe return
        // remain authoritative; the next living brain turn resumes the march.
        bot.HandOverReleaseReturnToGroup();
        if (!bot.IsAlive || bot.IsReturningAfterRelease) return false;
        if (bot.IsStunned || bot.IsMezzed) return true;

        bool atKeep = bot.CurrentRegionID == keep.Region &&
            Vector2.DistanceSquared(new(bot.X, bot.Y), new(keep.X, keep.Y)) <= 6500 * 6500;
        GameLiving petTarget = atKeep ? AutonomousPetSupport.ActiveOwnedPetCombatTarget(bot) : null;
        if (!brain.HasAggro && petTarget?.IsAlive == true && petTarget.CurrentRegionID == keep.Region &&
            Vector2.DistanceSquared(new(petTarget.X, petTarget.Y), new(keep.X, keep.Y)) <= 9000 * 9000 &&
            GameServer.ServerRules.IsAllowedToAttack(bot, petTarget, true))
        {
            bot.TargetObject = petTarget;
            brain.AddToAggroList(petTarget, Math.Max(100, petTarget.EffectiveLevel * 12));
            brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
        }
        // Combat is relevant again at the defended keep. Let the normal FSM
        // provide heals, CC, pets, target legality and wall/pursuit discipline.
        if (atKeep && (brain.HasAggro || bot.IsAttacking || TryEngageFrontierThreat(brain)))
        {
            if (bot.TargetObject is GameLiving target &&
                (target.CurrentRegionID != keep.Region ||
                 Vector2.DistanceSquared(new(target.X, target.Y), new(keep.X, keep.Y)) > 9000 * 9000))
                brain.PrepareForTankPull();
            else
            {
                AutonomousPetSupport.Maintain(bot, bot.TargetObject as GameLiving, ref _nextDefensePetTick, out _);
                brain.FSM.Think();
                return true;
            }
        }
        // Resume recall after current roadside defense. Stale aggro, pet
        // orders and optional casts cannot keep the guild at another objective.
        if (atKeep)
        {
            if (bot.IsCasting && !BotSongTwistPolicy.HasMobileSongCast(bot)) return true;
            bot.StopFollowing();
            if (brain.CheckHeals() || AutonomousPetSupport.Maintain(bot, null, ref _nextDefensePetTick, out _)) return true;
        }
        else
        {
            brain.PrepareForTankPull();
            bot.StopCurrentSpellcast();
            CancelDefensePetWork(bot);
        }
        bot.WakeRecoveryRest();
        if (GameLoop.GameLoopTime < _guildDefenseRouteRetry) return true;
        if (bot.CurrentRegionID != keep.Region)
            return TravelRvrObjective(bot, _rvrDestination);
        bot.TempProperties.RemoveProperty(AutonomousFrontierTransport.RequestKey);
        if (Vector2.DistanceSquared(new(bot.X, bot.Y), new(keep.X, keep.Y)) > 3500 * 3500)
            return TravelToDefensivePost(bot, keep, new(keep.X, keep.Y, keep.Z));
        return HoldDefensiveKeepPost(bot, keep);
    }

    private bool _guildRecallDefending;
    private long _guildRecallDefenseLogAfter;

    private bool TryDefendDuringGuildRecall(BotBrain brain, GameBot bot, AbstractGameKeep keep)
    {
        long now = GameLoop.GameLoopTime;
        bool atKeep = bot.CurrentRegionID == keep.Region &&
            Vector2.DistanceSquared(new(bot.X, bot.Y), new(keep.X, keep.Y)) <= 6500 * 6500;
        GameLiving attacker = bot.IsAlive && !atKeep ? brain.FindRecallDefenseTarget() : null;
        if (attacker == null)
        {
            _guildRecallDefending = false;
            return false;
        }
        if (!_guildRecallDefending)
        {
            bot.StopMovingOnPath(); bot.StopMoving();
            _guildRecallDefending = true;
        }
        if (now >= _guildRecallDefenseLogAfter)
        {
            _guildRecallDefenseLogAfter = now + 30_000;
            Log.Info($"GUILD_RECALL_DEFENSE bot={bot.Name} id={bot.DatabaseID} keep={keep.KeepID} " +
                $"attacker={attacker.Name} actor={bot.CurrentRegionID}:{bot.X},{bot.Y},{bot.Z} " +
                $"mezzed={bot.IsMezzed} stunned={bot.IsStunned}");
        }
        // Incapacitation still prevents action. Native combat owns healing,
        // class attacks, pets, crowd control, LOS and pursuit from here.
        if (bot.IsStunned || bot.IsMezzed) return true;
        bot.WakeRecoveryRest();
        brain.ThinkInterval = 500;
        brain.AlreadyCheckedHeals = false;
        if (brain.FSM.GetCurrentState()?.StateType != eFSMStateType.AGGRO)
            brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
        AutonomousPetSupport.Maintain(bot, attacker, ref _nextDefensePetTick, out _);
        brain.FSM.Think();
        return true;
    }

    private static void CancelDefensePetWork(GameBot bot)
    {
        if (bot.ControlledBrain is NecromancerPetBrain servant)
        {
            servant.ClearSpellQueue();
            servant.ClearAttackSpellQueue();
            servant.Body?.StopCurrentSpellcast();
        }
    }

    private long _nextDefensePetTick;
    private long _guildDefenseRouteRetry;
}
