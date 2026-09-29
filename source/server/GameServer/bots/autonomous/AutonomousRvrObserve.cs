using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>How an observing leader ends the hold.</summary>
public enum RvrObserveDecision
{
    /// <summary>No decision yet: keep watching.</summary>
    Wait,
    /// <summary>Add on the watched party (counted rule or appetite).</summary>
    ThirdParty,
    /// <summary>An aggressive leader with a mezzer pushes into an engaged party.</summary>
    PushCc,
    /// <summary>A small doctrine picks off one straggler.</summary>
    Straggler,
    /// <summary>Retreat (wave-2 retreat run) and choose a new destination.</summary>
    Leave,
    /// <summary>Move on past the fight, keeping away from it.</summary>
    RoamOn,
    /// <summary>Somebody hit us first; ordinary combat takes over.</summary>
    Attacked,
}

/// <summary>Everything the trigger looks at, as plain values.</summary>
public readonly record struct RvrObserveTrigger(
    bool DoctrineApplies,
    RvrDoctrineKind Kind,
    int Parties,
    bool FightOngoing,
    bool MemberInCombat,
    bool BattleForce,
    bool RelicInGroup,
    bool SiegeEvent,
    bool Departing,
    bool Retreating,
    bool Pausing,
    bool CoolingDown);

/// <summary>What the leader sees at one re-evaluation, as plain values.</summary>
public readonly record struct RvrObserveSituation(
    RvrDoctrineKind Kind,
    RvrLeaderTraits Traits,
    int Ours,
    int TheirSize,
    int TheirAlive,
    int TheirDown,
    bool WatchedEngaged,
    bool AppetiteAccepts,
    bool HasMezzer,
    bool StragglerAvailable,
    long WatchedClosingMilliseconds,
    bool NewPartyNearOrBehind,
    bool Seen,
    bool SeenAccepted,
    long ElapsedMilliseconds,
    int CapSeconds,
    double ImperfectionRoll,
    bool MayEngage = true,
    bool SceneGone = false);

/// <summary>
/// Observe before engaging (C5, principle P2). A 1.65 group that saw two
/// groups fighting did not run in: it "let the battle develop a moment before
/// showing its hand", held outside caster reach and waited for the call. The
/// pug rule of thumb was counted: "wait until at least THREE of the enemy
/// group are down, then rush in" (two for a bolder crew, one for a clearly
/// bigger group). Stealthers took stragglers; a group that was seen, flanked
/// by a third party or charged by a bigger one left. Autonomous RvR leaders
/// (and solo RvR bots) only; companions and PvE parties are untouched.
/// </summary>
public static class AutonomousRvrObserve
{
    /// <summary>The leader sights parties and fights this far away.</summary>
    public const float SightRange = 5_000;
    /// <summary>Hold outside caster reach (Druid root 1,875; archers 1,500-2,000).</summary>
    public const float HoldDistance = 2_200;
    /// <summary>A step back aims 0 to this much beyond the hold distance (rolled per hold).</summary>
    public const float HoldMarginMaximum = 400;
    /// <summary>The leader picks a new hold point when an enemy comes this close to it.</summary>
    public const float HoldDriftRange = 1_800;
    /// <summary>Fight heat this close to our own recent fight is ours, not a fight to watch.</summary>
    public const float OwnFightRange = 1_500;
    public const int OwnFightMemoryMilliseconds = 90_000;
    /// <summary>Fight heat counts only this close to a sighted party.</summary>
    public const float HeatPartyRange = 1_500;
    /// <summary>At most this many (nearest) visible parties are examined per scan.</summary>
    public const int MaximumParties = 4;
    public const int LineOfSightChecksPerParty = 2;
    /// <summary>At most this many parties are tried per scan, hidden or not
    /// (bounds the raycasts when a zerg stands behind a hill).</summary>
    public const int MaximumPartyAttempts = 8;
    public const int TriggerCheckMilliseconds = 2_000;
    public const int ReevaluateMilliseconds = 3_000;
    /// <summary>A heat spot this young with hostiles near counts as a running fight.</summary>
    public const int FightHeatSeconds = 60;
    public const float StragglerDistance = 1_200;
    public const int StragglerHealthPercent = 30;
    /// <summary>The watched party must close in this long before it counts as turning on us.</summary>
    public const int ClosingMilliseconds = 6_000;
    /// <summary>Distance a party must gain per re-evaluation to count as closing.</summary>
    public const float ClosingStep = 100;
    /// <summary>The era's passive stealth-detection radius of a level 50.</summary>
    public const float SeenRange = 950;
    /// <summary>A new party this close (or anywhere behind us) makes the group leave.</summary>
    public const float NewPartyNearRange = 2_500;
    public const int BaseWaitSeconds = 60;
    public const int HardCapSeconds = 150;
    public const double ImperfectionChance = 0.15;
    public const int BoldAggression = 65;
    public const int PushAggression = 55;
    public const int CautiousRisk = 40;
    public const double OutnumberFactor = 1.5;
    /// <summary>Members farther than this from the leader neither count nor hold.</summary>
    public const float MemberRange = 3_000;
    /// <summary>An engage decision commits the group to that party or target this long.</summary>
    public const int CommitMilliseconds = 45_000;
    public const int EngageOrderMilliseconds = 10_000;

    // ------------------------------------------------------------ pure rules

    public static bool IsSmallDoctrine(RvrDoctrineKind kind) => kind is RvrDoctrineKind.SoloAssassin or
        RvrDoctrineKind.StealthPack or RvrDoctrineKind.CasterDuo or RvrDoctrineKind.SmallMan or RvrDoctrineKind.GankSquad;

    /// <summary>
    /// Whether a leader starts to observe: it sees two or more enemy parties
    /// or a running fight, nobody in the group is fighting, and the group is
    /// not a battle force, relic escort, siege crew, fresh from its hub,
    /// retreating, resting or just done with an earlier observation.
    /// </summary>
    /// <summary>
    /// A party the trigger counts: two or more living members (stealthed
    /// ones included), or anyone visibly fighting another player. A lone
    /// enemy walking by is left to the ordinary fight appetite.
    /// </summary>
    public static bool Qualifies(int aliveMembers, bool engaged) => aliveMembers >= 2 || engaged;

    /// <summary>Whether a fight heat spot is a fight to watch: near a sighted
    /// party and not our own recent fight.</summary>
    public static bool HeatIsTheirs(Vector2 heat, IReadOnlyList<Vector2> partyCentres, Vector2? ownFight) =>
        (!ownFight.HasValue || Vector2.Distance(heat, ownFight.Value) > OwnFightRange) &&
        partyCentres != null && partyCentres.Any(centre => Vector2.Distance(heat, centre) <= HeatPartyRange);

    /// <summary>The trigger counts only qualifying parties (see <see cref="Qualifies"/>).</summary>
    public static bool ShouldObserve(RvrObserveTrigger trigger) =>
        trigger.DoctrineApplies &&
        trigger.Kind is not (RvrDoctrineKind.KeepRaid or RvrDoctrineKind.KeepDefense) &&
        (trigger.Parties >= 2 || trigger.FightOngoing && trigger.Parties >= 1) &&
        !trigger.MemberInCombat && !trigger.BattleForce && !trigger.RelicInGroup && !trigger.SiegeEvent &&
        !trigger.Departing && !trigger.Retreating && !trigger.Pausing && !trigger.CoolingDown;

    /// <summary>
    /// How many of the watched party must be down before an ordinary leader
    /// adds: 3 of 8 (scaled as ceil(3/8 of their size), at least 1); a bold
    /// leader (Aggression above 65) adds at 2; a group at least 1.5 times the
    /// weaker side's living members adds at 1.
    /// </summary>
    public static int AddThreshold(int theirSize, int theirAlive, int ours, int aggression)
    {
        int normal = Math.Max(1, (int)Math.Ceiling(3d / 8d * Math.Max(1, theirSize)));
        if (theirAlive > 0 && ours >= OutnumberFactor * theirAlive)
            return 1;
        return aggression > BoldAggression ? Math.Min(2, normal) : normal;
    }

    /// <summary>
    /// The longest hold [prior: no source gives seconds]: 60 s scaled by
    /// Patience (0-100 gives x0.6 to x1.6) and a jitter roll (x0.85 to x1.15),
    /// never above 150 s.
    /// </summary>
    public static int CapSeconds(int patience, double jitterRoll) =>
        Math.Clamp((int)Math.Round(BaseWaitSeconds * (0.6 + Math.Clamp(patience, 0, 100) / 100d) *
            (0.85 + Math.Clamp(jitterRoll, 0, 1) * 0.3)), 1, HardCapSeconds);

    /// <summary>
    /// The decision at one re-evaluation. Leaving comes first (charged by a
    /// bigger party, a third party near or behind, seen by someone we would
    /// not fight); then the leader's once-per-hold imperfection roll; then
    /// stealth doctrines take a straggler; then the add rules (appetite, busy
    /// or not; the
    /// counted rule, push on CC); then stragglers for the other small
    /// doctrines; at the cap cautious leaders leave and others roam on.
    /// </summary>
    public static RvrObserveDecision Decide(RvrObserveSituation s)
    {
        if (s.SceneGone)
            return RvrObserveDecision.RoamOn;
        bool cautious = s.Traits.RiskTolerance < CautiousRisk;
        if (IsThreatLeave(s))
            return RvrObserveDecision.Leave;
        if (s.ImperfectionRoll < ImperfectionChance)
        {
            // A human does not always wait for the count: the daring add
            // early, the careful leave early.
            if (s.Traits.RiskTolerance < 50)
                return RvrObserveDecision.Leave;
            if (s.WatchedEngaged && s.MayEngage)
                return RvrObserveDecision.ThirdParty;
        }
        if (s.MayEngage)
        {
            if (AutonomousRvrRoutePolicy.IsStealthDoctrine(s.Kind) && s.StragglerAvailable)
                return RvrObserveDecision.Straggler;
            // A party we would take on anyway is engaged, busy or not.
            if (s.AppetiteAccepts)
                return RvrObserveDecision.ThirdParty;
            if (s.WatchedEngaged)
            {
                // The count, and never into survivors that outnumber us two to one.
                if (s.TheirDown >= AddThreshold(s.TheirSize, s.TheirAlive, s.Ours, s.Traits.Aggression) &&
                    s.TheirAlive <= 2 * Math.Max(1, s.Ours))
                    return RvrObserveDecision.ThirdParty;
                if (s.HasMezzer && s.Traits.Aggression > PushAggression)
                    return RvrObserveDecision.PushCc;
            }
            if (IsSmallDoctrine(s.Kind) && s.StragglerAvailable)
                return RvrObserveDecision.Straggler;
        }
        if (s.ElapsedMilliseconds >= s.CapSeconds * 1000L)
            return cautious ? RvrObserveDecision.Leave : RvrObserveDecision.RoamOn;
        return RvrObserveDecision.Wait;
    }

    /// <summary>
    /// Whether a leave comes from a real threat (charged, flanked, seen) and
    /// so goes into the danger memory; an early or patient leave does not.
    /// </summary>
    public static bool IsThreatLeave(RvrObserveSituation s) =>
        !s.SceneGone && (s.WatchedClosingMilliseconds >= ClosingMilliseconds && !s.AppetiteAccepts ||
            s.NewPartyNearOrBehind || s.Seen && !s.SeenAccepted);

    public static string Label(RvrObserveDecision decision) => decision switch
    {
        RvrObserveDecision.ThirdParty => "third_party",
        RvrObserveDecision.PushCc => "push_cc",
        RvrObserveDecision.Straggler => "straggler",
        RvrObserveDecision.Leave => "leave",
        RvrObserveDecision.RoamOn => "roam_on",
        RvrObserveDecision.Attacked => "attacked",
        _ => "wait",
    };

    public static bool IsEngage(RvrObserveDecision decision) =>
        decision is RvrObserveDecision.ThirdParty or RvrObserveDecision.PushCc or RvrObserveDecision.Straggler;

    /// <summary>The cooldown before the same leader observes again.</summary>
    public static int CooldownMilliseconds(RvrObserveDecision decision) => decision switch
    {
        RvrObserveDecision.RoamOn => 90_000,
        RvrObserveDecision.Leave => 60_000,
        _ => 30_000,
    };

    /// <summary>
    /// Where to stand: null when the nearest hostile is already at least the
    /// hold distance away, else a point straight away from it at the hold
    /// distance plus a margin.
    /// </summary>
    public static Vector3? StepBackPoint(Vector3 here, Vector3 nearestHostile, float margin = 150)
    {
        Vector2 away = new(here.X - nearestHostile.X, here.Y - nearestHostile.Y);
        float distance = away.Length();
        if (distance >= HoldDistance)
            return null;
        away = distance < 1 ? new Vector2(1, 0) : away / distance;
        float step = HoldDistance + Math.Clamp(margin, 0, HoldMarginMaximum) - distance;
        return new Vector3(here.X + away.X * step, here.Y + away.Y * step, here.Z);
    }

    /// <summary>Whether the straight leg from <paramref name="from"/> to
    /// <paramref name="to"/> passes within <paramref name="radius"/> of <paramref name="point"/>.</summary>
    public static bool PassesNear(Vector2 from, Vector2 to, Vector2 point, float radius)
    {
        Vector2 leg = to - from;
        float lengthSquared = leg.LengthSquared();
        float t = lengthSquared < 1 ? 0 : Math.Clamp(Vector2.Dot(point - from, leg) / lengthSquared, 0, 1);
        return Vector2.Distance(from + leg * t, point) <= radius;
    }

    /// <summary>A new party near us, or anywhere behind us relative to the watched party.</summary>
    public static bool IsNearOrBehind(Vector2 here, Vector2 watched, Vector2 party)
    {
        Vector2 toParty = party - here;
        if (toParty.Length() <= NewPartyNearRange)
            return true;
        Vector2 toWatched = watched - here;
        return toWatched.LengthSquared() >= 1 && Vector2.Dot(toParty, toWatched) < 0;
    }

    /// <summary>Straggler: below 30 % health, or more than 1,200 from the rest of its party.</summary>
    public static bool IsStraggler(int healthPercent, float distanceFromParty) =>
        healthPercent < StragglerHealthPercent || distanceFromParty > StragglerDistance;

    // ---------------------------------------------------------- live state

    private sealed class State
    {
        public bool Active;
        public long StartTick;
        public long NextCheck;
        public long LastEvalTick;
        public long CooldownUntil;
        public ushort Region;
        public bool HoldPending;
        public Vector3 HoldPoint;
        public float HoldMargin;
        public Vector3 NearestHostile;
        public Vector3 FightCentre;
        public int CapSeconds;
        public double Roll;
        public object WatchedKey;
        public int WatchedPeak;
        public readonly HashSet<GameLiving> DeadSeen = [];
        public float LastWatchedDistance;
        public long ClosingSince;
        public readonly HashSet<object> KnownParties = [];
        public Vector3? OwnFight;
        public long OwnFightTick;
        public GameLiving EngageTarget;
        public long EngageUntil;
        public object CommittedParty;
        public GameLiving CommittedTarget;
        public long CommittedUntil;
        public int OutcomeSerial;
        public RvrObserveDecision Outcome;
        public Vector3 OutcomeCentre;
    }

    private sealed class Party
    {
        public object Key;
        /// <summary>Visible, targetable living members, nearest first.</summary>
        public readonly List<GameLiving> Alive = [];
        /// <summary>Living members near the leader, stealthed ones included.</summary>
        public int AliveCount;
        /// <summary>Members near the leader lying dead.</summary>
        public readonly List<GameLiving> Dead = [];
        public int Size;
        public int AverageLevel;
        public bool Engaged;
        public Vector3 Centre;
        public float Distance;
        public bool Qualifies => AutonomousRvrObserve.Qualifies(AliveCount, Engaged);
    }

    private static readonly ConditionalWeakTable<Group, State> Groups = new();
    private static readonly ConditionalWeakTable<GameBot, State> Solos = new();
    private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrObserve));

    private static State Find(GameBot bot)
    {
        if (bot == null)
            return null;
        if (bot.Group != null)
            return Groups.TryGetValue(bot.Group, out State group) ? group : null;
        return Solos.TryGetValue(bot, out State solo) ? solo : null;
    }

    private static State Create(GameBot leader) =>
        leader.Group != null ? Groups.GetOrCreateValue(leader.Group) : Solos.GetOrCreateValue(leader);

    /// <summary>Whether this bot's group (or the solo bot) is holding to watch.</summary>
    public static bool IsObserving(GameBot bot) => IsObserving(bot, out _, out _, out _);

    /// <summary>Whether this bot's group is observing; the hold point once the
    /// leader has chosen it, the nearest enemy and this hold's step-back margin.</summary>
    public static bool IsObserving(GameBot bot, out Vector3? holdPoint, out Vector3 nearestHostile, out float margin)
    {
        holdPoint = null;
        nearestHostile = default;
        margin = 0;
        State state = Find(bot);
        if (state == null)
            return false;
        lock (state)
        {
            // A leader who stopped evaluating (died, changed task) cannot pin the group.
            if (!state.Active || bot.CurrentRegionID != state.Region ||
                GameLoop.GameLoopTime > state.LastEvalTick + 10_000)
                return false;
            holdPoint = state.HoldPending ? null : state.HoldPoint;
            nearestHostile = state.NearestHostile;
            margin = state.HoldMargin;
            return true;
        }
    }

    /// <summary>The leader's chosen hold point.</summary>
    public static void SetHoldPoint(GameBot leader, Vector3 point)
    {
        State state = Find(leader);
        if (state == null)
            return;
        lock (state)
        {
            state.HoldPoint = point;
            state.HoldPending = false;
        }
    }

    /// <summary>The target an engage decision chose, handed out once to the leader.</summary>
    public static GameLiving TakeEngageTarget(GameBot bot)
    {
        if (bot == null || bot.Group != null && bot.Group.LivingLeader != bot)
            return null;
        State state = Find(bot);
        if (state == null)
            return null;
        lock (state)
        {
            GameLiving target = state.EngageTarget;
            state.EngageTarget = null;
            return target != null && target.IsAlive && GameLoop.GameLoopTime < state.EngageUntil ? target : null;
        }
    }

    /// <summary>
    /// Whether an engage decision committed this bot's group to
    /// <paramref name="target"/> (its party for an add, the one target for a
    /// straggler), so members join the fight the leader chose.
    /// </summary>
    public static bool IsCommittedTarget(GameBot bot, GameLiving target)
    {
        State state = Find(bot);
        GameLiving identity = PvpCombatant.Resolve(target);
        if (state == null || identity == null)
            return false;
        lock (state)
        {
            if (GameLoop.GameLoopTime >= state.CommittedUntil)
                return false;
            return state.CommittedTarget != null
                ? ReferenceEquals(state.CommittedTarget, identity)
                : state.CommittedParty != null && (ReferenceEquals(state.CommittedParty, identity.Group) ||
                  ReferenceEquals(state.CommittedParty, identity));
        }
    }

    /// <summary>The latest finished observation for the controller: leave / roam on and the fight centre.</summary>
    public static bool TryGetOutcome(GameBot bot, out int serial, out RvrObserveDecision outcome, out Vector3 centre,
        out object owner)
    {
        serial = 0;
        outcome = RvrObserveDecision.Wait;
        centre = default;
        State state = Find(bot);
        owner = state;
        if (state == null)
            return false;
        lock (state)
        {
            serial = state.OutcomeSerial;
            outcome = state.Outcome;
            centre = state.OutcomeCentre;
        }
        return serial > 0;
    }

    private static GameLiving[] NearMembers(GameBot bot) => bot.Group?.GetMembersInTheGroup()
        .Where(member => member.CurrentRegionID == bot.CurrentRegionID && member.GetDistanceTo(bot) <= MemberRange)
        .ToArray() ?? [bot];

    /// <summary>
    /// The leader's (or solo bot's) per-think evaluation: start, keep or end
    /// the hold. Called with the retreat check, before the frontier scan can
    /// open a fight. Being hit ends it at once.
    /// </summary>
    public static void EvaluateLeader(GameBot bot)
    {
        if (!AutonomousRvrDoctrineRuntime.Applies(bot) || !bot.IsAlive || bot.CurrentZone == null ||
            bot.Group != null && bot.Group.LivingLeader != bot)
            return;
        long now = GameLoop.GameLoopTime;
        State state = Find(bot);
        if (state == null)
        {
            if (!AutonomousWorldBotController.IsFrontierForObserve(bot))
                return;
            state = Create(bot);
        }

        bool active;
        lock (state)
            active = state.Active;
        if (active)
        {
            // Self-defence first: checked every think while holding.
            GameLiving[] holding = NearMembers(bot);
            lock (state)
            {
                state.LastEvalTick = now;
                if (holding.Any(member => member.IsAlive && member.InCombatInLast(2_000)))
                {
                    End(bot, state, RvrObserveDecision.Attacked, now, 0, 0, 0, 0);
                    return;
                }
            }
        }

        lock (state)
        {
            if (now < state.NextCheck)
                return;
            // Staggered so leaders do not all scan in the same tick.
            state.NextCheck = now + (state.Active ? ReevaluateMilliseconds : TriggerCheckMilliseconds) +
                bot.ObjectID % 500;
        }

        GameLiving[] near = NearMembers(bot);
        if (near.Any(member => member.IsAlive && member.InCombatInLast(2_500)))
            lock (state)
            {
                state.OwnFight = new(bot.X, bot.Y, bot.Z);
                state.OwnFightTick = now;
            }

        if (!AutonomousWorldBotController.IsFrontierForObserve(bot) || PvpCombatant.IsSafeArea(bot))
        {
            lock (state)
                if (state.Active)
                    End(bot, state, RvrObserveDecision.RoamOn, now, 0, 0, 0, 0);
            return;
        }

        HashSet<GameLiving> ours = near.Append(bot).ToHashSet();
        if (!state.Active)
        {
            if (now < state.CooldownUntil)
                return;
            RvrDoctrine doctrine = AutonomousRvrDoctrineRuntime.For(bot);
            string forceId = ForceId(bot);
            var plan = AutonomousRvrEventLayer.KeepPlan(forceId, bot.Realm, now);
            RvrObserveTrigger trigger = new(
                doctrine != null, doctrine?.Kind ?? RvrDoctrineKind.LoneRoamer, 0, false,
                near.Any(member => member.IsAlive && member.InCombat),
                AutonomousRvrEventLayer.IsBattleForce(forceId, now),
                near.Any(GameRelic.IsPlayerCarryingRelic),
                AutonomousRvrDefense.IsCommittedSiegeFighter(bot) || AutonomousRvrDefense.IsCommittedDefender(bot) ||
                    BotSiegeRuntime.Assigned(bot) || AutonomousRvrEventLayer.GetRallyOrder(forceId, bot.Realm, now) != null ||
                    plan != null && plan.Intent is not (AutonomousRvrEventLayer.Intent.Roam or AutonomousRvrEventLayer.Intent.HuntEnemy),
                AutonomousHubDeparture.IsDeparting(bot, WorldSimulationClock.UtcNow),
                AutonomousRvrDoctrineRuntime.IsRetreating(bot, out _),
                AutonomousRvrGroupPause.IsPausing(bot, out _),
                false);
            // The cheap exclusions first; the sight scan only when they pass,
            // and it stops once two qualifying parties are in sight.
            if (!ShouldObserve(trigger with { Parties = 2 }))
                return;
            List<Party> sighted = Scan(bot, ours, stopAfterQualifying: 2);
            int qualifying = sighted.Count(party => party.Qualifies);
            bool fight = sighted.Any(party => party.Engaged) || qualifying > 0 && TheirFreshHeat(bot, state, sighted, now);
            if (!ShouldObserve(trigger with { Parties = qualifying, FightOngoing = fight }))
                return;
            Begin(bot, state, doctrine, sighted, fight, near, now);
            return;
        }

        Reevaluate(bot, state, Scan(bot, ours, stopAfterQualifying: int.MaxValue), near, now);
    }

    private static void Begin(GameBot leader, State state, RvrDoctrine doctrine, List<Party> parties, bool fightOngoing,
        GameLiving[] near, long now)
    {
        Party nearest = parties.OrderBy(party => party.Distance).First();
        Party watched = PickWatched(parties, null);
        GameLiving nearestHostile = parties.SelectMany(party => party.Alive).OrderBy(leader.GetDistanceTo).First();
        int patience = leader.PersistentRecord?.Patience ?? 50;
        lock (state)
        {
            state.Active = true;
            state.StartTick = now;
            state.LastEvalTick = now;
            state.NextCheck = now + ReevaluateMilliseconds + leader.ObjectID % 500;
            state.Region = leader.CurrentRegionID;
            state.HoldPending = true;
            state.HoldMargin = (float)(Random.Shared.NextDouble() * HoldMarginMaximum);
            state.NearestHostile = new(nearestHostile.X, nearestHostile.Y, nearestHostile.Z);
            state.FightCentre = FightCentre(parties, watched);
            state.CapSeconds = CapSeconds(patience, Random.Shared.NextDouble());
            state.Roll = Random.Shared.NextDouble();
            state.WatchedKey = watched.Key;
            state.WatchedPeak = watched.Size;
            state.DeadSeen.Clear();
            state.DeadSeen.UnionWith(watched.Dead);
            state.LastWatchedDistance = watched.Distance;
            state.ClosingSince = 0;
            state.KnownParties.Clear();
            state.KnownParties.UnionWith(parties.Select(party => party.Key));
            state.EngageTarget = null;
            state.CommittedParty = null;
            state.CommittedTarget = null;
            state.CommittedUntil = 0;
        }
        // Stealth doctrines watch from stealth.
        if (doctrine != null && AutonomousRvrRoutePolicy.IsStealthDoctrine(doctrine.Kind))
            foreach (GameBot member in near.OfType<GameBot>().Where(member => member.IsAlive && !member.IsStealthed &&
                         !member.InCombat && (member.GetSpecializationByName(Specs.Stealth)?.Level ?? 0) > 0))
                member.Stealth(true);
        if (Log.IsInfoEnabled)
            Log.Info($"RVR_OBSERVE group=\"{Label(leader)}\" doctrine={doctrine?.Kind.ToString() ?? "none"} " +
                $"parties={parties.Count(party => party.Qualifies)} fight_ongoing={(fightOngoing ? "true" : "false")} " +
                $"nearest={(int)nearest.Distance} ours={near.Count(member => member.IsAlive)}");
    }

    private static void Reevaluate(GameBot leader, State state, List<Party> parties, GameLiving[] near, long now)
    {
        RvrDoctrine doctrine = AutonomousRvrDoctrineRuntime.For(leader);
        RvrDoctrineKind kind = doctrine?.Kind ?? RvrDoctrineKind.LoneRoamer;
        RvrLeaderTraits traits = leader.PersistentRecord is { } record
            ? new(record.Aggression, record.RiskTolerance, record.Patience)
            : RvrLeaderTraits.Neutral;
        int ours = near.Count(member => member.IsAlive);
        Vector2 here = new(leader.X, leader.Y);

        object previousKey;
        lock (state)
            previousKey = state.WatchedKey;
        Party watched = parties.Count == 0 ? null : PickWatched(parties, previousKey);
        long elapsed = now - state.StartTick;
        // Only lone enemies walking by are left: nothing to watch any more.
        if (watched == null || !parties.Any(party => party.Qualifies))
        {
            lock (state)
                End(leader, state, Decide(new RvrObserveSituation(kind, traits,
                    ours, 0, 0, 0, false, false, false, false, 0, false, false, false, elapsed, state.CapSeconds, 1,
                    SceneGone: true)), now, 0, 0, ours, 0);
            return;
        }

        int size, down;
        long closingMs;
        bool newParty;
        lock (state)
        {
            if (!ReferenceEquals(state.WatchedKey, watched.Key))
            {
                state.WatchedKey = watched.Key;
                state.WatchedPeak = watched.Size;
                state.DeadSeen.Clear();
                state.LastWatchedDistance = watched.Distance;
                state.ClosingSince = 0;
            }
            // Down means seen dead: a stealthed or out-of-sight member is not
            // counted; one seen dead stays down after release until it stands
            // alive near us again (rezzed).
            state.DeadSeen.UnionWith(watched.Dead);
            state.DeadSeen.RemoveWhere(member => member.IsAlive && member.CurrentRegionID == leader.CurrentRegionID &&
                leader.GetDistanceTo(member) <= SightRange + 500);
            down = state.DeadSeen.Count;
            state.WatchedPeak = Math.Max(state.WatchedPeak, watched.Size);
            size = Math.Max(state.WatchedPeak, watched.AliveCount + down);
            if (watched.Distance < state.LastWatchedDistance - ClosingStep)
            {
                if (state.ClosingSince == 0)
                    state.ClosingSince = now - ReevaluateMilliseconds;
            }
            else
                state.ClosingSince = 0;
            state.LastWatchedDistance = watched.Distance;
            closingMs = state.ClosingSince == 0 ? 0 : now - state.ClosingSince;
            Vector2 watchedCentre = new(watched.Centre.X, watched.Centre.Y);
            newParty = parties.Any(party => party.Qualifies && !state.KnownParties.Contains(party.Key) &&
                IsNearOrBehind(here, watchedCentre, new(party.Centre.X, party.Centre.Y)));
            foreach (Party party in parties)
                state.KnownParties.Add(party.Key);
            state.FightCentre = FightCentre(parties, watched);
            GameLiving closest = parties.SelectMany(party => party.Alive).OrderBy(leader.GetDistanceTo).First();
            state.NearestHostile = new(closest.X, closest.Y, closest.Z);
            // The fight drifted toward the hold: the leader picks a new spot.
            if (!state.HoldPending && Vector2.Distance(new(state.HoldPoint.X, state.HoldPoint.Y),
                    new(closest.X, closest.Y)) < HoldDriftRange)
                state.HoldPending = true;
        }

        bool accepts = Accepts(leader, watched);
        GameLiving seen = parties.SelectMany(party => party.Alive)
            .Where(enemy => leader.GetDistanceTo(enemy) <= SeenRange && !(enemy.IsAttacking || enemy.IsCasting))
            .OrderBy(leader.GetDistanceTo).FirstOrDefault();
        Party seenParty = seen == null ? null : parties.First(party => party.Alive.Contains(seen));
        GameLiving straggler = FindStraggler(leader, parties);
        bool hasMezzer = near.OfType<GameBot>().Any(member => member.IsAlive && member.CanCastCrowdControlSpells &&
            member.CrowdControlSpells.Any(spell => spell.SpellType == eSpellType.Mesmerize && spell.Level <= member.Level));
        RvrObserveSituation situation = new(
            kind, traits, ours, size, watched.AliveCount, down,
            watched.Engaged, accepts, hasMezzer, straggler != null, closingMs, newParty,
            seen != null, seenParty != null && Accepts(leader, seenParty), elapsed, state.CapSeconds, state.Roll,
            AutonomousPvpOpportunityPolicy.MayHunt(leader));
        RvrObserveDecision decision = Decide(situation);
        if (decision == RvrObserveDecision.Wait)
            return;

        lock (state)
        {
            if (decision == RvrObserveDecision.Straggler)
            {
                state.CommittedTarget = PvpCombatant.Resolve(straggler) ?? straggler;
                state.CommittedParty = null;
                state.EngageTarget = straggler;
            }
            else if (IsEngage(decision))
            {
                state.CommittedTarget = null;
                state.CommittedParty = watched.Key;
                state.EngageTarget = AutonomousRvrDoctrineRuntime.Choose(leader, watched.Alive, null) ??
                    watched.Alive.First();
            }
            if (IsEngage(decision))
            {
                state.EngageUntil = now + EngageOrderMilliseconds;
                state.CommittedUntil = now + CommitMilliseconds;
            }
            End(leader, state, decision, now, down, size, ours, watched.AliveCount);
        }
        if (IsEngage(decision))
            AutonomousRvrGroupPause.EndPause(leader, "observe_engage");
        // Only a real threat goes into the danger memory; an early or
        // patient leave saw no fight of ours.
        if (decision == RvrObserveDecision.Leave)
            AutonomousRvrDoctrineRuntime.BeginObserveRetreat(leader, state.FightCentre, IsThreatLeave(situation));
    }

    /// <summary>Ends the hold and logs the decision. Caller holds the lock.</summary>
    private static void End(GameBot leader, State state, RvrObserveDecision decision, long now, int down, int size,
        int ours, int theirs)
    {
        long seconds = (now - state.StartTick) / 1000;
        state.Active = false;
        state.CooldownUntil = now + CooldownMilliseconds(decision);
        state.NextCheck = now + TriggerCheckMilliseconds + leader.ObjectID % 500;
        state.WatchedKey = null;
        state.DeadSeen.Clear();
        state.KnownParties.Clear();
        if (decision is RvrObserveDecision.Leave or RvrObserveDecision.RoamOn)
        {
            state.OutcomeSerial++;
            state.Outcome = decision;
            state.OutcomeCentre = state.FightCentre;
        }
        if (Log.IsInfoEnabled)
        {
            bool bold = (leader.PersistentRecord?.Aggression ?? 50) > BoldAggression;
            Log.Info($"RVR_OBSERVE_DECISION group=\"{Label(leader)}\" seconds={seconds} decision={Label(decision)} " +
                $"enemy_down={down}/{size} ours={ours} theirs={theirs} bold={(bold ? "true" : "false")}");
        }
    }

    // ------------------------------------------------------------ scanning

    private static string ForceId(GameBot bot) =>
        bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}";

    private static string Label(GameBot leader) => leader.Group != null ? ForceId(leader) : leader.Name;

    /// <summary>The ordinary fight appetite against a sighted party.</summary>
    private static bool Accepts(GameBot leader, Party party)
    {
        GameLiving enemy = party.Alive[0];
        (int ownCount, int ownLevel) = AutonomousPvpOpportunityPolicy.VisibleParty(leader);
        int count = Math.Max(1, party.AliveCount);
        return AutonomousRvrDoctrineRuntime.AcceptsFight(leader, ownCount, ownLevel, count, party.AverageLevel, enemy) ??
            !AutonomousPvpOpportunityPolicy.IsVisiblyStronger(ownCount, ownLevel, count, party.AverageLevel);
    }

    /// <summary>A heat spot younger than 60 s near a sighted party, not our own fight.</summary>
    private static bool TheirFreshHeat(GameBot leader, State state, List<Party> parties, long now)
    {
        Vector2? own;
        lock (state)
            own = state.OwnFight.HasValue && now - state.OwnFightTick <= OwnFightMemoryMilliseconds
                ? new Vector2(state.OwnFight.Value.X, state.OwnFight.Value.Y) : null;
        Vector2[] centres = parties.Select(party => new Vector2(party.Centre.X, party.Centre.Y)).ToArray();
        return AutonomousRvrHeat.Recent(leader.CurrentRegionID, WorldSimulationClock.UtcNow)
            .Any(spot => AutonomousRvrHeat.IsYoungerThan(spot, TimeSpan.FromSeconds(FightHeatSeconds)) &&
                HeatIsTheirs(new(spot.Position.X, spot.Position.Y), centres, own));
    }

    /// <summary>
    /// The nearest visible hostile parties (players and bots only, pets and
    /// monsters ignored; a party is its group, or the enemy alone), at most
    /// four, stopping early once <paramref name="stopAfterQualifying"/>
    /// qualifying parties are in sight. A party is visible when one of its two
    /// nearest members is in line of sight.
    /// </summary>
    private static List<Party> Scan(GameBot leader, HashSet<GameLiving> ours, int stopAfterQualifying)
    {
        // Cheap checks first; the server's attack rule once per party below.
        var candidates = new Dictionary<object, List<GameLiving>>();
        void Consider(GameLiving enemy)
        {
            if (enemy == null || !enemy.IsAlive || enemy.IsStealthed || ours.Contains(enemy) ||
                enemy.ObjectState != GameObject.eObjectState.Active || enemy.CurrentRegionID != leader.CurrentRegionID)
                return;
            object key = (object)enemy.Group ?? enemy;
            if (!candidates.TryGetValue(key, out List<GameLiving> list))
                candidates[key] = list = [];
            list.Add(enemy);
        }
        foreach (GamePlayer player in leader.GetPlayersInRadius((ushort)SightRange))
            Consider(player);
        foreach (GameNPC npc in leader.GetNPCsInRadius((ushort)SightRange))
            if (npc is GameBot other)
                Consider(other);

        var nav = PathfindingProvider.Instance;
        bool los = nav.IsAvailable && nav.HasNavmesh(leader.CurrentZone);
        Vector3 eye = new(leader.X, leader.Y, leader.Z + 48);
        var result = new List<Party>();
        int qualifying = 0;
        int attempts = 0;
        foreach (List<GameLiving> members in candidates.Values
                     .Select(list => list.OrderBy(leader.GetDistanceTo).ToList())
                     .OrderBy(list => leader.GetDistanceTo(list[0])))
        {
            if (result.Count >= MaximumParties || qualifying >= stopAfterQualifying ||
                attempts++ >= MaximumPartyAttempts)
                break;
            // Camlann: hostility follows group, guild and battlegroup, so the
            // nearest member speaks for its party; others are rechecked cheaply.
            if (PvpCombatant.IsSafeArea(members[0]) || !GameServer.ServerRules.IsAllowedToAttack(leader, members[0], true))
                continue;
            if (los && !members.Take(LineOfSightChecksPerParty).Any(enemy => nav.HasLineOfSight(leader.CurrentZone, eye,
                    new(enemy.X, enemy.Y, enemy.Z + 48), nav.DefaultFilters)))
                continue;
            Party party = new() { Key = (object)members[0].Group ?? members[0] };
            party.Alive.AddRange(members.Where(enemy => enemy == members[0] ||
                GameServer.ServerRules.IsAllowedToAttack(leader, enemy, true)));
            if (party.Key is Group group)
            {
                foreach (GameLiving member in group.GetMembersInTheGroup())
                {
                    if (member.CurrentRegionID != leader.CurrentRegionID ||
                        leader.GetDistanceTo(member) > SightRange + 500)
                        continue;
                    party.Size++;
                    if (member.IsAlive)
                        party.AliveCount++;
                    else
                        party.Dead.Add(member);
                }
                party.Size = Math.Max(party.Size, party.Alive.Count);
                party.AliveCount = Math.Max(party.AliveCount, party.Alive.Count);
            }
            else
            {
                party.Size = 1;
                party.AliveCount = 1;
            }
            party.AverageLevel = (int)Math.Round(party.Alive.Average(enemy => enemy.EffectiveLevel));
            party.Engaged = party.Alive.Any(enemy => FightsAnotherPlayer(enemy, party.Key, ours));
            party.Centre = AutonomousRvrDoctrineGeometry.Centroid(party.Alive.Select(enemy => new Vector3(enemy.X, enemy.Y, enemy.Z)));
            party.Distance = leader.GetDistanceTo(party.Alive[0]);
            result.Add(party);
            if (party.Qualifies)
                qualifying++;
        }
        return result;
    }

    /// <summary>
    /// PvP only: the enemy is attacking or casting at a player or bot (or its
    /// pet) outside its own party and outside ours, whom it may attack. Mob
    /// pulls and rebuffs do not count.
    /// </summary>
    private static bool FightsAnotherPlayer(GameLiving enemy, object partyKey, HashSet<GameLiving> ours)
    {
        if (!(enemy.IsAttacking || enemy.IsCasting) || enemy.TargetObject is not GameLiving target || !target.IsAlive)
            return false;
        GameLiving victim = PvpCombatant.Resolve(target);
        return victim != null && victim != enemy && !ours.Contains(victim) &&
            !ReferenceEquals((object)victim.Group ?? victim, partyKey) &&
            GameServer.ServerRules.IsAllowedToAttack(enemy, target, true);
    }

    /// <summary>Keep the previous party while it is still there and busy;
    /// otherwise the engaged party with the most losses, else the nearest
    /// qualifying party, else the nearest.</summary>
    private static Party PickWatched(List<Party> parties, object previousKey)
    {
        Party previous = previousKey == null ? null : parties.FirstOrDefault(party => ReferenceEquals(party.Key, previousKey));
        if (previous != null && (previous.Engaged || !parties.Any(party => party.Engaged)))
            return previous;
        return parties.Where(party => party.Engaged)
                   .OrderByDescending(party => party.Dead.Count / (double)Math.Max(1, party.Size))
                   .ThenBy(party => party.Distance).FirstOrDefault() ??
               parties.Where(party => party.Qualifies).OrderBy(party => party.Distance).FirstOrDefault() ??
               parties.OrderBy(party => party.Distance).First();
    }

    private static Vector3 FightCentre(List<Party> parties, Party watched)
    {
        Party[] engaged = parties.Where(party => party.Engaged).ToArray();
        return engaged.Length == 0 ? watched.Centre : AutonomousRvrDoctrineGeometry.Centroid(engaged.Select(party => party.Centre));
    }

    /// <summary>
    /// The nearest straggler we could really fight: not mezzed or otherwise
    /// protected, and a member trailing its party only when our appetite
    /// would take on that party.
    /// </summary>
    private static GameLiving FindStraggler(GameBot leader, List<Party> parties)
    {
        GameLiving best = null;
        float bestDistance = float.MaxValue;
        foreach (Party party in parties)
        {
            bool? accepted = null;
            foreach (GameLiving enemy in party.Alive)
            {
                if (enemy.IsMezzed || BotPvpCrowdControl.Protected(leader, enemy))
                    continue;
                float fromParty = party.Key is Group && party.Alive.Count > 1
                    ? Vector2.Distance(new(enemy.X, enemy.Y), CentreWithout(party, enemy))
                    : 0; // a lone enemy is a party of one: only low health makes it a straggler
                float distance = leader.GetDistanceTo(enemy);
                if (!IsStraggler(enemy.HealthPercent, fromParty) || distance >= bestDistance)
                    continue;
                if (enemy.HealthPercent >= StragglerHealthPercent && (accepted ??= Accepts(leader, party)) == false)
                    continue;
                best = enemy;
                bestDistance = distance;
            }
        }
        return best;
    }

    private static Vector2 CentreWithout(Party party, GameLiving member)
    {
        Vector2 sum = Vector2.Zero;
        int count = 0;
        foreach (GameLiving other in party.Alive)
            if (other != member)
            {
                sum += new Vector2(other.X, other.Y);
                count++;
            }
        return count == 0 ? new(member.X, member.Y) : sum / count;
    }
}
