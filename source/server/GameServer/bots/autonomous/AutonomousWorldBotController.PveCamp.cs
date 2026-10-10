using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// Wave 7 (P12 "the camp, not the mob, is the unit of PvE", P13 "pull size
/// follows control capacity"): solo con and rest habits by archetype, the
/// pull style of autonomous PvE groups, and when a camp is given up.
/// Companions and player-led groups never reach this code.
/// </summary>
public sealed partial class AutonomousWorldBotController
{
    private const long PveCampCheckMilliseconds = 10_000;

    private ConColor _naturalSoloCeiling = ConColor.YELLOW;
    private ConColor _preferredSoloCon = ConColor.YELLOW;
    private AutonomousPveCampWatch _soloCampWatch;
    private long _nextPveCampCheckTick;
    private Vector3? _pveHoldSpot;

    private static bool KnowsRootOrSnare(GameBot bot) =>
        bot?.Spells?.Any(spell => spell != null && spell.Level <= bot.Level && spell.IsHarmful &&
            spell.SpellType is eSpellType.SpeedDecrease or eSpellType.DamageSpeedDecrease or
                eSpellType.DamageSpeedDecreaseNoVariance or eSpellType.UnbreakableSpeedDecrease) == true;

    /// <summary>Solo ceiling and preferred con for this bot's class, level and spells.</summary>
    private void RefreshSoloArchetype(GameBot bot)
    {
        if (bot?.CharacterClass == null)
        {
            _naturalSoloCeiling = ConColor.YELLOW;
            return;
        }
        var characterClass = (eCharacterClass)bot.CharacterClass.ID;
        bool hasRoot = KnowsRootOrSnare(bot);
        _naturalSoloCeiling = AutonomousPveArchetype.NaturalSoloCeiling(characterClass, hasRoot, bot.Level);
        _preferredSoloCon = AutonomousPveArchetype.PreferredCon(characterClass, bot.Level, hasRoot,
            MaximumTargetCon(1));
    }

    private bool ReadyForNextSoloPull(GameBot bot, bool usesPower)
    {
        if (bot?.CharacterClass == null)
            return AutonomousRestPolicy.IsFullyRecovered(bot.HealthPercent, bot.ManaPercent, bot.EndurancePercent, usesPower);
        PveRestThresholds thresholds = AutonomousPveArchetype.RestThresholds((eCharacterClass)bot.CharacterClass.ID,
            bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID);
        return AutonomousPveArchetype.ReadyToPull(thresholds, bot.HealthPercent, bot.ManaPercent,
            bot.EndurancePercent, usesPower);
    }

    private long _soloRestWakeTick;

    private void RecordSoloRestWake(GameBot bot)
    {
        long now = GameLoop.GameLoopTime;
        _soloRestWakeTick = now;
        AutonomousPveCampRuntime.RestStats.RecordWake(now, bot.MaxMana > 0 ? (int?)bot.ManaPercent : null, bot.HealthPercent);
        AutonomousPveCampRuntime.ReportIfDue(now);
    }

    private void RecordSoloPull(GameBot bot, bool routeThreat = false)
    {
        long now = GameLoop.GameLoopTime;
        // A pull right after the bot got up from a rest is a "rested pull";
        // one wake explains one pull.
        long? rested = _soloRestWakeTick > 0 && now - _soloRestWakeTick <= AutonomousPveRestStats.RestedPullWindowMilliseconds
            ? now - _soloRestWakeTick
            : null;
        _soloRestWakeTick = 0;
        AutonomousPveCampRuntime.RestStats.RecordPull(now, bot.MaxMana > 0 ? (int?)bot.ManaPercent : null,
            bot.HealthPercent, rested, routeThreat);
        AutonomousPveCampRuntime.ReportIfDue(now);
    }

    private static void RecordSoloRest()
    {
        long now = GameLoop.GameLoopTime;
        AutonomousPveCampRuntime.RestStats.RecordRest(now);
        AutonomousPveCampRuntime.ReportIfDue(now);
    }

    /// <summary>
    /// Runs at the top of a camp turn. True when the turn is spent (leaving
    /// the camp, moving away from enemy players, or holding there).
    /// </summary>
    private bool TryPveCampPlay(GameBot bot)
    {
        if (_camp == null || bot == null || AutonomousRealmRaid.GetView(bot.Group) != null)
            return false;
        if (_groupDirective?.IsDynamic == true)
        {
            if (_groupDirective.ObjectiveKind != eAutonomousObjectiveKind.GroupPve)
                return false;
            if (_groupDirective.Leader != bot)
            {
                // During an enemy hold nobody pulls; everyone stays on the leader.
                if (!AutonomousPveCampRuntime.EnemyHold(bot))
                    return false;
                FollowDynamicGroupLeader(bot, _groupDirective);
                return true;
            }
            return LeadPveCamp(bot);
        }
        return WatchSoloCamp(bot);
    }

    private bool WatchSoloCamp(GameBot bot)
    {
        long now = GameLoop.GameLoopTime;
        if (now < _nextPveCampCheckTick)
            return false;
        _nextPveCampCheckTick = now + PveCampCheckMilliseconds + bot.ObjectID % 2_000;
        if (_soloCampWatch == null || !string.Equals(_soloCampWatch.CampId, _camp.Id, StringComparison.Ordinal))
            _soloCampWatch = new AutonomousPveCampWatch(_camp.Id, bot.Level, now);
        ScanCamp(bot, [bot], bot.Level, out bool rival, out _);
        if (!_soloCampWatch.ObserveRival(now, rival))
            return false;
        LeavePveCamp(bot, "rival", $"bot:{bot.Name}");
        return true;
    }

    private bool LeadPveCamp(GameBot bot)
    {
        long now = GameLoop.GameLoopTime;
        GameBot[] members = bot.Group?.GetMembersInTheGroup().OfType<GameBot>().ToArray() ?? [bot];
        if (members.Length == 0)
            return false;
        AutonomousPveCampRuntime.Refresh(bot, _groupDirective.GroupId, now);
        int averageLevel = (int)Math.Round(members.Average(member => member.Level));
        AutonomousPveCampWatch watch = AutonomousPveCampRuntime.Watch(bot, _camp.Id, averageLevel, now);
        if (!watch.EnemyHold)
            _pveHoldSpot = null;

        if (now >= _nextPveCampCheckTick)
        {
            _nextPveCampCheckTick = now + PveCampCheckMilliseconds + bot.ObjectID % 2_000;
            int highest = members.Max(member => member.EffectiveLevel);
            if (AutonomousPveCampWatch.IsOutgrown(_camp.TargetLevel, watch.ArrivalAverageLevel, averageLevel,
                    highest, _groupDirective.WipePenalty))
            {
                LeavePveCamp(bot, "outgrown", _groupDirective.GroupId);
                return true;
            }

            bool fighting = members.Any(member => member.IsAlive &&
                (member.InCombat || member.IsAttacking || (member.Brain as BotBrain)?.HasAggro == true));
            ScanCamp(bot, members, averageLevel, out bool rival, out Vector3? enemyCentre);
            if (watch.ObserveRival(now, rival))
            {
                LeavePveCamp(bot, "rival", _groupDirective.GroupId);
                return true;
            }

            switch (watch.ObserveEnemy(now, enemyCentre.HasValue, fighting))
            {
                case PveEnemyDecision.Shift:
                    _pveHoldSpot = DefensiveSpot(bot, enemyCentre.Value);
                    AutonomousPveCampRuntime.SetEnemyHold(bot, true);
                    Log.Info($"PVE_CAMP_ENEMY group={_groupDirective.GroupId} camp={_camp.Id} action=shift " +
                             $"distance={(int)Vector2.Distance(new(bot.X, bot.Y), new(_pveHoldSpot.Value.X, _pveHoldSpot.Value.Y))}");
                    break;
                case PveEnemyDecision.Leave:
                    AutonomousPveCampRuntime.SetEnemyHold(bot, false);
                    LeavePveCamp(bot, "enemy", _groupDirective.GroupId);
                    return true;
                case PveEnemyDecision.Clear:
                    AutonomousPveCampRuntime.SetEnemyHold(bot, false);
                    _pveHoldSpot = null;
                    break;
            }
        }

        if (!watch.EnemyHold)
            return false;
        _pveHoldSpot ??= new Vector3(bot.X, bot.Y, bot.Z);
        Vector3 spot = _pveHoldSpot.Value;
        var heldCamp = _camp;
        if (Vector2.DistanceSquared(new(bot.X, bot.Y), new(spot.X, spot.Y)) > 80 * 80)
        {
            // Route recovery can abandon the camp synchronously. Keep its
            // recovery status instead of dereferencing the cleared objective.
            if (!IssuePath(bot, spot) || !ReferenceEquals(_camp, heldCamp))
                return true;
        }
        else
        {
            bot.StopMovingOnPath();
            bot.StopMoving();
        }
        SetStatus(bot, "Holding away from enemy players", GoalText(),
            "Enemy players are at the camp; the party moved together and waits before pulling again",
            _camp.MonsterName, _camp.ZoneName);
        return true;
    }

    /// <summary>
    /// A spot 300-600 units from the leader, away from the enemy, along the
    /// walkable surface (walls stop it early, like backing into a corner).
    /// </summary>
    private static Vector3 DefensiveSpot(GameBot bot, Vector3 enemyCentre)
    {
        Vector3 from = new(bot.X, bot.Y, bot.Z);
        Vector2 away = new(bot.X - enemyCentre.X, bot.Y - enemyCentre.Y);
        if (away.LengthSquared() < 1)
            away = new(1, 0);
        away = Vector2.Normalize(away) * AutonomousPveCampWatch.ShiftDistance(bot.ObjectID);
        Vector3 target = new(from.X + away.X, from.Y + away.Y, from.Z);
        Vector3? walkable = bot.CurrentZone == null ? null :
            PathfindingProvider.Instance.GetMoveAlongSurface(bot.CurrentZone, from, target,
                PathfindingProvider.Instance.DefaultFilters);
        return walkable ?? from;
    }

    private const int MaximumCampScanRadius = 3000;

    /// <summary>
    /// One bounded look (at most 3,000 units): another party of three or
    /// more fighting monsters within 1,500 of the camp centre (rival), and
    /// the centre of threatening enemy-realm players within 2,000 of any
    /// member (enemy): not allied, attackable, and at least blue to the
    /// group's average level. Stealthed actors are not seen.
    /// </summary>
    private void ScanCamp(GameBot bot, GameBot[] members, int averageLevel, out bool rival, out Vector3? enemyCentre)
    {
        rival = false;
        enemyCentre = null;
        if (bot.CurrentRegionID != _camp.RegionId)
            return;
        Vector2 centre = new(_camp.X, _camp.Y);
        // Soloers only look for rivals, so their scan stays at the rival radius.
        bool grouped = _groupDirective?.IsDynamic == true;
        float reach = Math.Max(Vector2.Distance(new(bot.X, bot.Y), centre) + AutonomousPveCampWatch.RivalRadius,
            grouped ? AutonomousPveCampWatch.EnemyRadius + 500 : 0);
        ushort radius = (ushort)Math.Min(MaximumCampScanRadius, reach);
        Vector2 enemySum = Vector2.Zero;
        int enemies = 0;
        bool foundRival = false;

        void Consider(GameLiving actor)
        {
            if (actor == null || actor == bot || !actor.IsAlive || actor.IsStealthed ||
                bot.Group != null && bot.Group.IsInTheGroup(actor))
                return;
            if (!foundRival && actor.Group != null &&
                actor.Group.MemberCount >= AutonomousPveCampWatch.RivalMinimumGroupSize &&
                (actor.InCombat || actor.IsAttacking) && actor.TargetObject is GameNPC fought &&
                !PvpCombatant.IsPlayerShaped(fought) &&
                Vector2.DistanceSquared(new(actor.X, actor.Y), centre) <=
                    AutonomousPveCampWatch.RivalRadius * AutonomousPveCampWatch.RivalRadius)
                foundRival = true;
            if (!grouped || actor.Realm == bot.Realm ||
                ConLevels.GetConColor(ConLevels.GetConLevel(averageLevel, actor.Level)) < ConColor.BLUE)
                return;
            bool nearMember = false;
            foreach (GameBot member in members)
            {
                if (member != null && member.IsAlive && member.CurrentRegionID == actor.CurrentRegionID &&
                    member.IsWithinRadius(actor, AutonomousPveCampWatch.EnemyRadius))
                {
                    nearMember = true;
                    break;
                }
            }
            if (!nearMember || PvpCombatant.AreAllied(bot, actor) ||
                !GameServer.ServerRules.IsAllowedToAttack(bot, actor, true))
                return;
            enemySum += new Vector2(actor.X, actor.Y);
            enemies++;
        }

        foreach (GamePlayer player in bot.GetPlayersInRadius(radius))
            Consider(player);
        foreach (GameNPC npc in bot.GetNPCsInRadius(radius))
        {
            if (npc is GameBot other)
                Consider(other);
        }
        rival = foundRival;
        if (enemies > 0)
            enemyCentre = new Vector3(enemySum / enemies, bot.Z);
    }

    /// <summary>
    /// Gives the camp up and remembers it for 15 minutes; the next turn runs
    /// the normal camp choice. The party keeps its task and clock.
    /// </summary>
    private void LeavePveCamp(GameBot bot, string reason, string who)
    {
        string campId = _camp.Id;
        long now = GameLoop.GameLoopTime;
        Log.Info($"PVE_CAMP_LEAVE group={who} reason={reason} camp={campId}");
        _rejectedDungeonCamps[campId] = now + AutonomousPveCampWatch.ReturnMemoryMilliseconds;
        if (_groupDirective?.IsDynamic == true)
            AutonomousBotGroupCoordinator.LeaveCampByChoice(bot, campId, reason,
                AutonomousPveCampWatch.ReturnMemoryMilliseconds);
        AutonomousGoalDiagnostics.End(bot, GoalAttemptEnd.Reassigned, $"Left the camp ({reason})");
        _camp = null;
        _campStartedTick = 0;
        _emptyCampSinceTick = 0;
        _reportedEmptySharedCampId = string.Empty;
        _patrolDestination = null;
        _pendingStableChoice = null;
        _pveHoldSpot = null;
        _soloCampWatch = null;
        _nextPlanTick = 0;
        ResetRouteOrderState();
        bot.StopMovingOnPath();
        bot.StopMoving();
        if (bot.PersistentRecord != null)
            bot.PersistentRecord.CurrentCampId = string.Empty;
        SetStatus(bot, "Choosing a new live goal", "Find a reachable level-appropriate XP camp",
            reason switch
            {
                "rival" => "Another group was working this spawn first; moving on",
                "outgrown" => "The camp no longer gives good experience at this level",
                "enemy" => "Enemy players stayed at the camp",
                _ => "Leaving the camp",
            });
    }

    /// <summary>
    /// Mass-pull (pet) groups: the puller's pull also brings idle monsters
    /// of the same kind clustered within 600 of the target, as a pet run
    /// would, up to the plan's size. Outdoors only, same floor (height within
    /// 150) and line of sight, never mobs nearer to another party than to
    /// our camp, and never while the party carries a wipe penalty.
    /// </summary>
    private int TryMassPull(GameBot puller, BotBrain brain, GameNPC target)
    {
        if (AutonomousPveCampRuntime.PlanFor(puller) is not { Style: PvePullStyle.MassPull } plan || plan.MaxPull < 2 ||
            (_groupDirective?.WipePenalty ?? 0) > 0 || _camp == null ||
            target.CurrentZone == null || target.CurrentZone.IsDungeon || target.CurrentRegion?.IsDungeon == true)
            return 0;
        ushort otherRadius = (ushort)(AutonomousPvePullStyle.MassPullClusterRadius + AutonomousPveCampWatch.RivalRadius);
        List<GameLiving> others = [];
        foreach (GamePlayer player in target.GetPlayersInRadius(otherRadius))
            if (player.IsAlive && (puller.Group == null || !puller.Group.IsInTheGroup(player)))
                others.Add(player);
        foreach (GameNPC npc in target.GetNPCsInRadius(otherRadius))
            if (npc is GameBot other && other.IsAlive && other.Group != null && other.Group != puller.Group)
                others.Add(other);
        Vector2 campCentre = new(_camp.X, _camp.Y);
        IPathfindingMgr nav = PathfindingProvider.Instance;
        Vector3 targetPoint = new(target.X, target.Y, target.Z + 50);

        bool OursToPull(GameNPC npc)
        {
            float toCamp = Vector2.Distance(new(npc.X, npc.Y), campCentre);
            foreach (GameLiving other in others)
            {
                float toOther = npc.GetDistanceTo(other);
                if (toOther <= AutonomousPveCampWatch.RivalRadius && toOther < toCamp)
                    return false;
            }
            return true;
        }

        int pulled = 0;
        foreach (GameNPC extra in target.GetNPCsInRadius((ushort)AutonomousPvePullStyle.MassPullClusterRadius)
                     .Where(npc => npc != target && npc.IsAlive && IsExperienceMonster(npc) && !npc.InCombat &&
                                   !npc.IsMezzed && npc.CurrentRegionID == target.CurrentRegionID &&
                                   string.Equals(npc.Name, target.Name, StringComparison.OrdinalIgnoreCase) &&
                                   Math.Abs(npc.Z - target.Z) <= 150 &&
                                   npc.EffectiveLevel <= target.EffectiveLevel + 1 &&
                                   GameServer.ServerRules.IsAllowedToAttack(puller, npc, true))
                     .OrderBy(npc => npc.GetDistanceTo(target))
                     .Take(plan.MaxPull - 1))
        {
            if (extra.Brain is not IOldAggressiveBrain mob || !OursToPull(extra) ||
                !nav.HasLineOfSight(target.CurrentZone, targetPoint, new(extra.X, extra.Y, extra.Z + 50), nav.DefaultFilters))
                continue;
            mob.AddToAggroList(puller, 1);
            brain.AddToAggroList(extra, 1);
            pulled++;
        }
        return pulled;
    }
}
