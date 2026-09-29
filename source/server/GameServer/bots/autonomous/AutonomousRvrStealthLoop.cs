using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>Why a stealther picked this victim (P9).</summary>
public enum RvrStealthOpenReason { None, Lone, BackLine, Resting, LastInLine, Straggler }

/// <summary>Why a stealther broke off (P9: one kill, then leave).</summary>
public enum RvrStealthBreakReason { None, Kill, Friends, LowHp }

/// <summary>One visible enemy as a waiting stealther sees it.</summary>
public readonly record struct RvrStealthTargetView(
    RvrClassTraits Traits,
    double Distance,
    int HealthPercent,
    int PartySize,
    double NearestFriendDistance,
    bool AtPartyEdge,
    bool Resting,
    bool LastInLine);

/// <summary>
/// The stealther loop of autonomous RvR assassins (Infiltrator, Shadowblade,
/// Nightshade in a solo-assassin, stealth-pack or gank-squad doctrine): roam
/// and wait stealthed beside the road, open on a soft target (a caster or
/// healer at the back, a lone walker, a resting or rezzing enemy, the last of
/// a passing line), take one kill, and leave when the friends answer or the
/// fight turns: run 300-600, hide again once the combat timer allows, pick a
/// new direction. Evidence: MENTAL_MODELS P9 (D10941, D10949, D13053, D5653,
/// D8409, D6118). Companions and player-led groups never use this.
/// </summary>
public static class AutonomousRvrStealthLoop
{
    /// <summary>1.65 assassins could hide again about 6 s after an attack [D8409, O181].</summary>
    public const int AssassinRestealthMilliseconds = 6_000;
    /// <summary>Other stealthers about 10 s after combat [D6118, O182].</summary>
    public const int OtherRestealthMilliseconds = 10_000;
    public const int BreakRunMinimum = 300;
    public const int BreakRunMaximum = 600;
    public const int LowHealthPercent = 40;
    /// <summary>No friend within this: a lone walker.</summary>
    public const double LoneRadius = 1_500;
    /// <summary>Farther than this from the next friend: a straggler (as in observe-before-engaging).</summary>
    public const double StragglerGap = 1_200;
    public const int StragglerHealthPercent = 30;
    /// <summary>A soft target this far from its party's centre stands at the edge, not in the middle.</summary>
    public const double EdgeDistance = 250;
    /// <summary>The last walker of a line trails the one before it by at least this.</summary>
    public const double LineGap = 250;
    public const double FriendsRadius = 1_500;
    public const int BreakRunCapMilliseconds = 8_000;
    /// <summary>After a break, a new fight is only left again for low health for this long.</summary>
    public const int FriendsBreakCooldownMilliseconds = 30_000;
    /// <summary>A resting enemy inside a bigger group is only a victim at its edge.</summary>
    public const int RestingPartyLimit = 3;
    /// <summary>A grouped stealther drops stealth when its visible leader walks this far ahead.</summary>
    public const double TrailDistance = 300;
    /// <summary>Hiding gives up after this long and the stealther hunts again, hidden or not.</summary>
    public const int HidingTimeoutMilliseconds = 20_000;
    private const int AppliesCacheMilliseconds = 1_000;
    private const int StealthRetryMilliseconds = 2_000;
    private const int PickMemoryMilliseconds = 20_000;
    private const int EngagementLostMilliseconds = 15_000;
    private const int PartyRadius = 2_000;
    private const ushort DetectHiddenRadius = 2_048;

    // ------------------------------------------------------------ pure rules

    /// <summary>Casters and healers: the soft targets a stealther waits for.</summary>
    public static bool IsSoft(RvrClassTraits traits) => !traits.HasFlag(RvrClassTraits.Guard) &&
        (traits.HasFlag(RvrClassTraits.Healer) || traits.HasFlag(RvrClassTraits.AreaMez) ||
         traits.HasFlag(RvrClassTraits.AreaStun) ||
         traits.HasFlag(RvrClassTraits.Caster) && !traits.HasFlag(RvrClassTraits.Melee));

    /// <summary>
    /// Why this enemy is a good victim, or <see cref="RvrStealthOpenReason.None"/>
    /// when a stealther leaves it alone: anyone in the middle of a group that
    /// is not resting, not trailing and not a caster at the edge (never the
    /// full group's tank in the middle).
    /// </summary>
    public static RvrStealthOpenReason Classify(RvrStealthTargetView view)
    {
        bool lone = view.PartySize <= 1 || view.NearestFriendDistance > LoneRadius;
        if (view.Resting && (view.AtPartyEdge || view.PartySize <= RestingPartyLimit))
            return RvrStealthOpenReason.Resting;
        if (!lone && IsSoft(view.Traits) && view.AtPartyEdge) return RvrStealthOpenReason.BackLine;
        if (lone) return RvrStealthOpenReason.Lone;
        if (view.LastInLine) return RvrStealthOpenReason.LastInLine;
        if (view.NearestFriendDistance > StragglerGap || view.HealthPercent < StragglerHealthPercent)
            return RvrStealthOpenReason.Straggler;
        return RvrStealthOpenReason.None;
    }

    /// <summary>
    /// Soft targets first: a caster or healer at the back, then the slowest to
    /// get up, then a lone walker, then the last of a line, then a straggler;
    /// wounded and near ones a little higher. Zero for "leave it".
    /// </summary>
    public static double Score(RvrStealthTargetView view)
    {
        RvrStealthOpenReason reason = Classify(view);
        double score = reason switch
        {
            RvrStealthOpenReason.BackLine => 3.0,
            RvrStealthOpenReason.Resting => 3.0,
            RvrStealthOpenReason.Lone => 2.5,
            RvrStealthOpenReason.LastInLine => 2.0,
            RvrStealthOpenReason.Straggler => 1.5,
            _ => 0,
        };
        if (score <= 0)
            return 0;
        if (IsSoft(view.Traits)) score += 1.5;
        score += (100 - Math.Clamp(view.HealthPercent, 0, 100)) / 200d;
        score -= Math.Clamp(view.Distance, 0, 3_000) / 3_000d;
        return Math.Max(0.01, score);
    }

    /// <returns>The index of the best victim, or -1 when none is worth opening on.</returns>
    public static int Choose(IReadOnlyList<RvrStealthTargetView> views)
    {
        int best = -1;
        double bestScore = 0;
        for (int index = 0; views != null && index < views.Count; index++)
        {
            double score = Score(views[index]);
            if (score > bestScore)
            {
                best = index;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>
    /// One kill, then leave; leave earlier when a second enemy joins in or our
    /// health drops below 40 %. Within 30 s of the last break neither
    /// "friends" nor "low health" breaks again, and after a break a stealther
    /// with an enemy in melee range no longer runs for low health: it turns
    /// and fights (or the group retreat takes it) instead of hopping away
    /// until it dies.
    /// </summary>
    public static RvrStealthBreakReason BreakReason(bool targetDead, int otherAttackers, int healthPercent,
        bool recentBreak = false, bool brokeBefore = false, bool attackerInMelee = false)
    {
        if (targetDead) return RvrStealthBreakReason.Kill;
        if (healthPercent < LowHealthPercent && !recentBreak && !(brokeBefore && attackerInMelee))
            return RvrStealthBreakReason.LowHp;
        if (otherAttackers > 0 && !recentBreak) return RvrStealthBreakReason.Friends;
        return RvrStealthBreakReason.None;
    }

    /// <summary>
    /// A grouped stealther following a visible, walking leader more than 300
    /// away drops stealth so it keeps up with the column (stealth speed is
    /// about 60 %); a solo or leading stealther, or one following a hidden or
    /// standing leader, keeps it.
    /// </summary>
    public static bool ShouldTrailVisible(bool hasOtherLeader, bool leaderStealthed, bool leaderMoving,
        double leaderDistance) =>
        hasOtherLeader && !leaderStealthed && leaderMoving && leaderDistance > TrailDistance;

    /// <summary>
    /// How long after the last combat action a stealther hides again: the era
    /// value by class (6 s assassins, 10 s others), never sooner than the
    /// server's own "cannot stealth in combat" window.
    /// </summary>
    public static int RestealthDelayMilliseconds(eCharacterClass characterClass, int serverCombatWindowMilliseconds) =>
        Math.Max(BotPoisonSupply.IsAssassin(characterClass) ? AssassinRestealthMilliseconds : OtherRestealthMilliseconds,
            Math.Max(0, serverCombatWindowMilliseconds));

    public static float BreakRunDistance(double roll) =>
        BreakRunMinimum + (float)Math.Clamp(roll, 0, 1) * (BreakRunMaximum - BreakRunMinimum);

    /// <summary>
    /// The point of the break-off run: away from the threat, turned 20-60
    /// degrees to one side so the stealther does not simply back up the way
    /// it came.
    /// </summary>
    public static Vector3 BreakPoint(Vector3 here, Vector3 threat, double distanceRoll, double turnRoll)
    {
        Vector2 away = new(here.X - threat.X, here.Y - threat.Y);
        if (away.LengthSquared() < 1)
            away = new Vector2(0, 1);
        away = Vector2.Normalize(away);
        double roll = Math.Clamp(turnRoll, 0, 1);
        double sign = roll < 0.5 ? -1 : 1;
        double degrees = 20 + Math.Abs(roll - 0.5) * 2 * 40;
        double angle = sign * degrees * Math.PI / 180;
        Vector2 turned = new((float)(away.X * Math.Cos(angle) - away.Y * Math.Sin(angle)),
            (float)(away.X * Math.Sin(angle) + away.Y * Math.Cos(angle)));
        Vector2 point = new Vector2(here.X, here.Y) + turned * BreakRunDistance(distanceRoll);
        return new Vector3(point.X, point.Y, here.Z);
    }

    public static string Label(RvrStealthOpenReason reason) => reason switch
    {
        RvrStealthOpenReason.Lone => "lone",
        RvrStealthOpenReason.BackLine => "back_line",
        RvrStealthOpenReason.Resting => "resting",
        RvrStealthOpenReason.LastInLine => "last_in_line",
        RvrStealthOpenReason.Straggler => "straggler",
        _ => "none",
    };

    public static string Label(RvrStealthBreakReason reason) => reason switch
    {
        RvrStealthBreakReason.Kill => "kill",
        RvrStealthBreakReason.Friends => "friends",
        RvrStealthBreakReason.LowHp => "low_hp",
        _ => "none",
    };

    // --------------------------------------------------------------- runtime

    private enum Phase { Hunting, Engaged, Breaking, Hiding }

    private sealed class LoopState
    {
        public Phase Phase;
        public GameLiving Pick;
        public RvrStealthOpenReason PickReason;
        public int PickDistance;
        public long PickedAt;
        public GameLiving Target;
        public long EngagedAt;
        public Vector3 BreakPoint;
        public long BreakUntil;
        public long LastBreakAt = -FriendsBreakCooldownMilliseconds;
        public long NextStealthTry;
        public bool Replan;
        public long HidingSince;
        public int BreaksSinceHidden;
        public long AppliesUntil;
        public bool AppliesValue;
    }

    private static readonly ConditionalWeakTable<GameBot, LoopState> States = new();
    private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrStealthLoop));

    /// <summary>An autonomous RvR assassin in a stealth doctrine.</summary>
    public static bool Applies(GameBot bot)
    {
        if (bot?.CharacterClass == null || !bot.IsAutonomousWorldBot ||
            !BotPoisonSupply.IsAssassin((eCharacterClass)bot.CharacterClass.ID) ||
            (bot.GetSpecializationByName(Specs.Stealth)?.Level ?? 0) <= 0)
            return false;
        // The doctrine read derives (and allocates) for solo bots; once a second is enough.
        LoopState state = States.GetOrCreateValue(bot);
        long now = GameLoop.GameLoopTime;
        if (now >= state.AppliesUntil)
        {
            state.AppliesValue = AutonomousRvrDoctrineRuntime.For(bot) is { } doctrine &&
                AutonomousRvrRoutePolicy.IsStealthDoctrine(doctrine.Kind);
            state.AppliesUntil = now + AppliesCacheMilliseconds;
        }
        return state.AppliesValue;
    }

    /// <summary>
    /// Whether an idle bot keeps its stealth instead of dropping it: not a
    /// grouped stealther trailing a visible, walking leader.
    /// </summary>
    public static bool KeepsStealth(GameBot bot) => Applies(bot) && !TrailsVisibleLeader(bot);

    private static bool TrailsVisibleLeader(GameBot bot)
    {
        GameLiving leader = bot.Group?.LivingLeader;
        return leader != null && ShouldTrailVisible(leader != bot, leader.IsStealthed, leader.IsMoving,
            leader.CurrentRegionID == bot.CurrentRegionID ? bot.GetDistanceTo(leader) : double.PositiveInfinity);
    }

    /// <summary>The roaming leader should pick a new destination after a break-off.</summary>
    public static bool TakeReplan(GameBot bot)
    {
        if (bot == null || !States.TryGetValue(bot, out LoopState state) || !state.Replan)
            return false;
        state.Replan = false;
        return true;
    }

    /// <summary>
    /// Runs every think before the frontier scan. Returns true while the bot
    /// is on its break-off run and owns its movement.
    /// </summary>
    public static bool Update(BotBrain brain)
    {
        GameBot bot = brain?.BotBody;
        if (bot == null || !bot.IsAlive || !Applies(bot))
        {
            if (bot != null && States.TryGetValue(bot, out LoopState stale))
                stale.Phase = Phase.Hunting;
            return false;
        }
        LoopState state = States.GetOrCreateValue(bot);
        long now = GameLoop.GameLoopTime;
        switch (state.Phase)
        {
            case Phase.Hunting:
            case Phase.Hiding:
                if (FightTarget(bot) is { } fought)
                {
                    BeginEngagement(bot, state, fought, now);
                    return false;
                }
                if (bot.IsStealthed && !brain.HasAggro && TrailsVisibleLeader(bot))
                {
                    bot.Stealth(false);
                    return false;
                }
                if (state.Phase == Phase.Hiding && !bot.InCombat && MaintainStealth(bot, state, now))
                {
                    state.Phase = Phase.Hunting;
                    state.BreaksSinceHidden = 0;
                }
                else if (state.Phase == Phase.Hiding && now - state.HidingSince >= HidingTimeoutMilliseconds)
                    state.Phase = Phase.Hunting;
                else if (state.Phase == Phase.Hunting && !bot.InCombat)
                    MaintainStealth(bot, state, now);
                return false;
            case Phase.Engaged:
            {
                GameLiving target = state.Target;
                RvrStealthBreakReason reason = BreakReason(target == null || !target.IsAlive,
                    OtherAttackers(bot, target), bot.HealthPercent,
                    now - state.LastBreakAt < FriendsBreakCooldownMilliseconds, state.BreaksSinceHidden > 0,
                    AttackerInMelee(bot));
                if (reason != RvrStealthBreakReason.None)
                {
                    StartBreak(brain, bot, state, target, reason, now);
                    return true;
                }
                if (!bot.IsAttacking && !bot.InCombatInLast(EngagementLostMilliseconds))
                    state.Phase = Phase.Hunting;
                return false;
            }
            case Phase.Breaking:
            {
                Vector3 here = new(bot.X, bot.Y, bot.Z);
                if (now >= state.BreakUntil || bot.IsCrowdControlled ||
                    Vector2.Distance(new(here.X, here.Y), new(state.BreakPoint.X, state.BreakPoint.Y)) <= 100)
                {
                    state.Phase = Phase.Hiding;
                    state.HidingSince = now;
                    return false;
                }
                if (brain.HasAggro)
                    brain.ClearAggroList();
                if (bot.IsAttacking)
                    bot.StopAttack();
                bot.TargetObject = null;
                if (!bot.IsMoving)
                    bot.PathTo(state.BreakPoint, bot.MaxSpeed);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// For the frontier and opportunity scans: true when the stealther loop
    /// decides this hunt (nobody in view is fighting us), with the victim in
    /// <paramref name="victim"/> or null to keep waiting.
    /// </summary>
    public static bool TryPick(GameBot bot, IReadOnlyList<GameLiving> candidates, Func<GameLiving, bool> inOurFight,
        out GameLiving victim)
    {
        victim = null;
        if (!Applies(bot) || candidates == null)
            return false;
        if (inOurFight != null && candidates.Any(candidate => candidate != null && inOurFight(candidate)))
            return false;
        LoopState state = States.GetOrCreateValue(bot);
        if (state.Phase != Phase.Hunting)
            return true;

        List<GameLiving> pool = candidates
            .Where(candidate => candidate != null && candidate.IsAlive && PvpCombatant.Resolve(candidate) == candidate)
            .Distinct().ToList();
        if (pool.Count == 0)
            return true;
        RvrStealthTargetView[] views = pool.Select(target => View(bot, target)).ToArray();
        int index = Choose(views);
        if (index < 0)
            return true;
        victim = pool[index];
        state.Pick = victim;
        state.PickReason = Classify(views[index]);
        state.PickDistance = (int)views[index].Distance;
        state.PickedAt = GameLoop.GameLoopTime;
        return true;
    }

    private static RvrStealthTargetView View(GameBot bot, GameLiving target)
    {
        GameLiving[] party = target.Group?.GetMembersInTheGroup()
            .Where(member => member != null && member.IsAlive && member.CurrentRegionID == target.CurrentRegionID &&
                member.IsWithinRadius(target, PartyRadius))
            .ToArray() ?? [target];
        if (!party.Contains(target))
            party = [.. party, target];
        GameLiving[] friends = party.Where(member => member != target).ToArray();
        double nearestFriend = friends.Length == 0 ? double.PositiveInfinity : friends.Min(target.GetDistanceTo);

        bool atEdge = party.Length <= 2;
        if (!atEdge)
        {
            Vector2 centre = new((float)party.Average(member => member.X), (float)party.Average(member => member.Y));
            double own = Vector2.Distance(centre, new(target.X, target.Y));
            double median = party.Select(member => (double)Vector2.Distance(centre, new(member.X, member.Y)))
                .OrderBy(distance => distance).ElementAt(party.Length / 2);
            atEdge = own >= EdgeDistance && own >= median;
        }

        bool resting = target.IsSitting || target is GameBot { IsRecoveryResting: true } ||
            target.IsCasting && target.castingComponent?.SpellHandler?.Spell?.SpellType == eSpellType.Resurrect;

        // Groups walk in a column behind their leader: the last one is the
        // moving member farthest from a moving leader, trailing the next.
        bool lastInLine = false;
        GameLiving leader = target.Group?.LivingLeader;
        if (party.Length >= 3 && leader != null && leader != target && leader.IsMoving && target.IsMoving &&
            party.Contains(leader))
        {
            double own = leader.GetDistanceTo(target);
            double next = party.Where(member => member != target && member != leader && member.IsMoving)
                .Select(member => (double)leader.GetDistanceTo(member)).DefaultIfEmpty(0).Max();
            lastInLine = own - next >= LineGap;
        }

        return new RvrStealthTargetView(AutonomousRvrDoctrineRuntime.ClassTraits(target), bot.GetDistanceTo(target),
            target.HealthPercent, party.Length, nearestFriend, atEdge, resting, lastInLine);
    }

    /// <summary>The enemy player or bot this bot is actually attacking now.</summary>
    private static GameLiving FightTarget(GameBot bot)
    {
        if (bot.attackComponent?.AttackState != true || bot.TargetObject is not GameLiving target || !target.IsAlive)
            return null;
        GameLiving identity = PvpCombatant.Resolve(target);
        return identity != null && !PvpCombatant.AreAllied(bot, identity) ? identity : null;
    }

    private static void BeginEngagement(GameBot bot, LoopState state, GameLiving target, long now)
    {
        state.Phase = Phase.Engaged;
        state.Target = target;
        state.EngagedAt = now;
        bool ownPick = target == state.Pick && now - state.PickedAt <= PickMemoryMilliseconds;
        state.Pick = null;
        if (!ownPick || !Log.IsInfoEnabled)
            return;
        eCharacterClass targetClass = target switch
        {
            GamePlayer player => (eCharacterClass)(player.CharacterClass?.ID ?? 0),
            GameBot enemyBot => (eCharacterClass)(enemyBot.CharacterClass?.ID ?? 0),
            _ => 0,
        };
        Log.Info($"RVR_STEALTH_OPEN bot={bot.Name} doctrine={AutonomousRvrDoctrineRuntime.For(bot)?.Kind.ToString() ?? "none"} " +
            $"target={target.Name} target_class={targetClass} reason={Label(state.PickReason)} dist={state.PickDistance}");
    }

    /// <summary>A living enemy that recently hit us stands within melee reach.</summary>
    private static bool AttackerInMelee(GameBot bot)
    {
        ICollection<GameLiving> attackers = bot.attackComponent?.AttackerTracker?.Attackers;
        if (attackers == null)
            return false;
        foreach (GameLiving attacker in attackers)
            if (attacker?.IsAlive == true && attacker.CurrentRegionID == bot.CurrentRegionID &&
                bot.IsWithinRadius(attacker, bot.MeleeAttackRange + 100) &&
                !PvpCombatant.AreAllied(bot, attacker))
                return true;
        return false;
    }

    /// <summary>Living enemies other than our victim (or its pets) that hit us recently.</summary>
    private static int OtherAttackers(GameBot bot, GameLiving target)
    {
        ICollection<GameLiving> attackers = bot.attackComponent?.AttackerTracker?.Attackers;
        if (attackers == null || attackers.Count == 0)
            return 0;
        GameLiving victim = PvpCombatant.Resolve(target) ?? target;
        int count = 0;
        foreach (GameLiving attacker in attackers)
        {
            GameLiving identity = PvpCombatant.Resolve(attacker);
            if (identity == null || identity == victim || !attacker.IsAlive ||
                attacker.CurrentRegionID != bot.CurrentRegionID || !bot.IsWithinRadius(attacker, (int)FriendsRadius) ||
                PvpCombatant.AreAllied(bot, identity))
                continue;
            count++;
        }
        return count;
    }

    private static void StartBreak(BotBrain brain, GameBot bot, LoopState state, GameLiving target,
        RvrStealthBreakReason reason, long now)
    {
        Vector3 here = new(bot.X, bot.Y, bot.Z);
        List<Vector3> threats = [];
        if (target?.IsAlive == true)
            threats.Add(new(target.X, target.Y, target.Z));
        ICollection<GameLiving> attackers = bot.attackComponent?.AttackerTracker?.Attackers;
        if (attackers != null)
            foreach (GameLiving attacker in attackers)
                if (attacker?.IsAlive == true && attacker.CurrentRegionID == bot.CurrentRegionID &&
                    bot.IsWithinRadius(attacker, (int)FriendsRadius))
                    threats.Add(new(attacker.X, attacker.Y, attacker.Z));
        if (threats.Count == 0 && target != null)
            threats.Add(new(target.X, target.Y, target.Z));
        Vector3 threat = threats.Count == 0 ? here : AutonomousRvrDoctrineGeometry.Centroid(threats);
        Vector3 raw = BreakPoint(here, threat, Random.Shared.NextDouble(), Random.Shared.NextDouble());
        Vector3 point = bot.CurrentZone == null ? raw :
            PathfindingProvider.Instance.GetMoveAlongSurface(bot.CurrentZone, here, raw,
                PathfindingProvider.Instance.DefaultFilters) ?? raw;

        if (Log.IsInfoEnabled)
            Log.Info($"RVR_STEALTH_BREAK bot={bot.Name} reason={Label(reason)} seconds={(now - state.EngagedAt) / 1000}");
        state.Phase = Phase.Breaking;
        state.Target = null;
        state.BreakPoint = point;
        state.BreakUntil = now + BreakRunCapMilliseconds;
        state.LastBreakAt = now;
        state.BreaksSinceHidden++;
        state.Replan = true;
        if (brain.HasAggro)
            brain.ClearAggroList();
        bot.StopAttack();
        if (bot.IsCasting)
            bot.StopCurrentSpellcast();
        bot.TargetObject = null;
        bot.PathTo(point, bot.MaxSpeed);
    }

    /// <summary>
    /// Hides again when a player could: out of combat for the restealth delay,
    /// not attacking, casting, controlled, mounted or carrying a relic, and no
    /// enemy close enough to see it hide (the server's stealth radius rule).
    /// A stealther in a group travels hidden only while its leader is hidden
    /// too, so it does not fall behind an open column.
    /// </summary>
    private static bool MaintainStealth(GameBot bot, LoopState state, long now)
    {
        if (bot.IsStealthed)
            return true;
        if (now < state.NextStealthTry)
            return false;
        state.NextStealthTry = now + StealthRetryMilliseconds;
        int delay = RestealthDelayMilliseconds((eCharacterClass)bot.CharacterClass.ID, GameLiving.IN_COMBAT_DURATION);
        if (bot.IsOnHorse || bot.IsOnStableMasterRoute || bot.IsReturningAfterRelease || bot.IsCasting ||
            bot.IsCrowdControlled || bot.attackComponent?.AttackState == true || bot.InCombatInLast(delay) ||
            bot.IsRecoveryResting || GameRelic.IsPlayerCarryingRelic(bot) || PvpCombatant.IsSafeArea(bot))
            return false;
        GameLiving leader = bot.Group?.LivingLeader;
        if (leader != null && leader != bot && !leader.IsStealthed && leader.IsMoving)
            return false;
        if (EnemyTooClose(bot))
            return false;
        bot.Stealth(true);
        return bot.IsStealthed;
    }

    /// <summary>The server's "too close to an enemy to hide" radius (StealthSpecHandler).</summary>
    private static bool EnemyTooClose(GameBot bot)
    {
        float stealth = Math.Min(50, bot.GetModifiedSpecLevel(Specs.Stealth));
        // The widest radius the rule can give is 2,048 (Detect Hidden, spec 0).
        foreach (GameLiving enemy in bot.GetPlayersInRadius(DetectHiddenRadius).Cast<GameLiving>()
                     .Concat(bot.GetNPCsInRadius(DetectHiddenRadius)))
        {
            if (enemy == bot || !enemy.IsAlive || enemy.ObjectState != GameObject.eObjectState.Active ||
                !GameServer.ServerRules.IsAllowedToAttack(enemy, bot, true))
                continue;
            float level = Math.Max(1f, enemy.Level);
            float radius = enemy is GamePlayer player && player.HasAbility(Abilities.DetectHidden)
                ? 2048f - 1792f * stealth / level
                : 1024f - 896f * stealth / level;
            if (radius > 0 && enemy.IsWithinRadius(bot, (int)radius))
                return true;
        }
        return false;
    }
}
