using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// In-battleground intent for admitted autonomous participants. Priority:
/// (1) defend the keep when the bot's guild holds it, (2) claim a defeated keep
/// at its steward with claim rank, (3) assault the keep (door, then lord) with a
/// group of four or more present, (4) otherwise roam between campaign camps.
/// In the open BG a crew unit first follows its order from
/// <see cref="OpenBattlegroundDirector"/> (claim, then its role's point) and falls
/// back to that priority only without an order or a route; the brackets never ask.
/// Followers assist their leader. Damage and class behaviour stay with the native
/// class brain, which BotBrain runs after this driver picks a goal.
/// </summary>
public static class AutonomousBattlegroundDriver
{
    public const int ScanMilliseconds = 1_500;
    public const int OpponentRange = 2_400;
    public const int SiegeRange = 280;
    public const int AssaultGroupMinimum = 4;
    public const int ArrivalDistance = 150;
    public const int FollowDistance = 300;
    private const int RouteIntervalMilliseconds = 3_000;
    private const int ClaimRetryMilliseconds = 5_000;
    private const int ProgressDistance = 96;
    // Detour returns at most 256 polygons per path, so a far camp is proved by chaining capped legs.
    public const int MaxRouteLegs = 8;
    // A partial leg ending this close to the goal has reached it.
    public const float LegReachDistance = 48;
    // A failed goal is not re-proved before this, so camp roaming does not repeat long proofs every turn.
    private const int UnreachableRetryMilliseconds = 15_000;
    /// <summary>open BG: this many goals in a row without a route make the driver test the bot's own position (island check).</summary>
    public const int IslandNoRouteStreak = 3;
    /// <summary>open BG: game time between two island checks of one bot, and between two of its route-failure log lines.</summary>
    public const int IslandCheckMilliseconds = 60_000;
    public const int RouteFailureLogMilliseconds = 60_000;
    /// <summary>open BG: after a release at its landing a crew member rests there to this share of health and power.</summary>
    public const int LandingRestPercent = 80;
    /// <summary>open BG: the landing rest never lasts longer than this (game time), e.g. while regeneration is suppressed.</summary>
    public const int LandingRestMaxMilliseconds = 2 * 60_000;
    /// <summary>
    /// open BG assault march: the floor point a unit marches to lies this far out from a closed outer gate, tried in this
    /// order. The keep centre lies behind the walls, so a march there failed with closed_gate or no_route (live run 0.242.0).
    /// </summary>
    public static readonly IReadOnlyList<int> GateApproachDistances = Array.AsReadOnly(new[] { 450, 300, 600 });
    /// <summary>Attackable doors within this distance (2D) of the farthest one from the keep centre are its outer gates.</summary>
    public const int OuterGateTolerance = 150;
    // A gate approach floor lies within this height of the gate.
    private const float GateFloorRange = 256;
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBattlegroundDriver));

    /// <summary>One native path query: its status and the position of its last node.</summary>
    public readonly record struct RouteLeg(PathfindingStatus Status, Vector3 End);

    /// <summary>Outcome of a chained route proof.</summary>
    public enum RouteChain { NoPath, DeadEnd, Capped, Reached }

    private sealed class State
    {
        // The intent chosen this turn, logged once as the first driver action of an entry.
        public string Action = "none";
        public long NextScan;
        public long NextClaim;
        public long NextAssault;
        public GameLiving AssaultTarget;
        public Point3D AssaultApproach;
        public int Camp;
        public Point3D Goal;
        public Point3D RouteGoal;
        public Vector3 RoutePosition;
        public bool RouteOk;
        public long RouteUntil;
        public bool HasPosition;
        public int ProgressX, ProgressY;
        // Set while a follower holds near a leader that is itself making real progress.
        public bool HoldProgress;
        // No camp had a proven route until this time; the bot stands rather than re-proving every camp each turn.
        public long CampsUnreachableUntil;
        // Goals whose route failure is already logged in this visit.
        public HashSet<(int, int, int)> FailedGoals;
        // Goals whose route proof failed, with the game time before which they are not proved again.
        public Dictionary<(int, int, int), long> UnreachableUntil;
        // open BG only (set each turn): island recovery, route-failure log limit and landing rest apply.
        public bool Open;
        // Goals in a row whose proof ended with no_route; the next island check; the next route-failure log line.
        public int NoRouteStreak;
        public long NextIslandCheck;
        public long NextRouteFailureLog;
        public int SuppressedRouteFailures;
        // Set by a release at the landing: rest there before the next order, until LandingRestUntil at the latest.
        public bool LandingRest;
        public long LandingRestUntil;
        public bool LandingRestLogged;
        // Self-defence turns during the landing rest: set while one lasts, counted per rest for the end line.
        public bool LandingRestDefending;
        public int LandingRestDefended;
        // open BG: the last order point (or gate approach) with a proven route; a unit whose order has none returns here.
        public Point3D LastOrderPoint;
    }

    /// <summary>A closed outer gate of a keep and its proven floor points outside, nearest distance first.</summary>
    private sealed record GateApproach(GameKeepDoor Door, Point3D[] Points);

    private enum GateMarch { NoGate, Walking, Unreachable }

    private static readonly ConditionalWeakTable<GameBot, State> States = new();
    // Gate approach floors per keep, resolved once on first use.
    private static readonly ConditionalWeakTable<AbstractGameKeep, GateApproach[]> GateApproaches = new();

    /// <summary>
    /// Runs one driver turn. Returns false when the bot takes no action this turn
    /// (dead, incapacitated, or it has just left the battleground).
    /// </summary>
    public static bool Turn(BotBrain brain, GameBot bot)
    {
        if (brain == null || bot == null) return false;
        // Each early return names its reason, so the live log shows why a participant idles.
        if (!bot.IsAlive) return Idle(bot, "not_alive");
        if (bot.IsIncapacitated)
        {
            // Mezzed or stunned in a fight is fighting, not standing stuck.
            if (bot.InCombat) AutonomousBattlegroundParticipation.NoteProgress(bot, eAutonomousProgressKind.Combat);
            return Idle(bot, "crowd_controlled");
        }
        // A relieved open-BG bot walks to its own portal keep first; every other leaver goes at once.
        bool walkingHome = AutonomousBattlegroundParticipation.TryGetReliefWalk(bot, out Point3D home);
        if (!walkingHome && AutonomousBattlegroundParticipation.IsLeaving(bot) && AutonomousBattlegroundParticipation.TryFinishLeaving(bot)) return Idle(bot, "left");
        BattlegroundDefinition definition = BattlegroundCampaignCatalog.Find(bot.CurrentRegionID);
        if (definition == null) return Idle(bot, "outside_battleground");

        State state = States.GetValue(bot, CreateState);
        long now = GameLoop.GameLoopTime;
        state.Open = definition.Kind == BattlegroundDefinitionKind.Open;
        // Set while the bot defends itself during its landing rest: it fights, but takes no order until the rest is over.
        bool restHold = false;
        if (state.LandingRest)
        {
            if (walkingHome || AutonomousBattlegroundParticipation.IsLeaving(bot))
                EndLandingRest(bot, state, "order");
            else if (RestAtLanding(brain, bot, state, now))
            {
                state.Action = "landing-rest";
                AutonomousBattlegroundParticipation.RecordDriverTurn(bot, bot.CurrentRegionID, state.Action);
                AutonomousBattlegroundParticipation.NoteProgress(bot, eAutonomousProgressKind.Recovery);
                return true;
            }
            else restHold = state.LandingRest;
        }
        if (now >= state.NextScan)
        {
            state.NextScan = now + ScanMilliseconds;
            // open BG: the crew leaves humans under level 30 alone unless they attacked (spec 2026-10-11 §6).
            GameLiving opponent = definition.Kind == BattlegroundDefinitionKind.Open
                ? BattlegroundCombatTargets.FindOpponent(bot, OpponentRange, target => MayAcquire(bot, target))
                : BattlegroundCombatTargets.FindOpponent(bot, OpponentRange);
            if (opponent != null) brain.Attack(opponent);
        }

        bool engaged = brain.HasAggro || bot.InCombat;
        state.Action = engaged ? "engaged" : "idle";
        state.HoldProgress = false;
        if (!engaged)
        {
            GameBot leader = bot.Group?.LivingLeader as GameBot;
            if (restHold)
            {
                state.Action = "landing-rest";
                Stand(bot);
            }
            else if (walkingHome)
            {
                state.Action = "relief-walk";
                if (!GoTo(bot, state, home, now)) AutonomousBattlegroundParticipation.EndReliefWalk(bot);
            }
            else if (AutonomousBattlegroundParticipation.IsLeaving(bot)) { state.Action = "leaving"; Stand(bot); }
            else if (leader != null && leader != bot && leader.IsAlive && leader.CurrentRegion == bot.CurrentRegion)
                Assist(brain, bot, leader, state, now);
            else Lead(brain, bot, definition, state, now);
        }
        AutonomousBattlegroundParticipation.RecordDriverTurn(bot, bot.CurrentRegionID, state.Action);
        Observe(bot, state, engaged);
        return true;
    }

    /// <summary>Drops the driver's per-visit state, so a re-entry starts with a fresh first turn.</summary>
    public static void ForgetState(GameBot bot)
    {
        if (bot != null) States.Remove(bot);
    }

    /// <summary>
    /// A participant released at its battleground landing: the driver state starts fresh (brackets: nothing else).
    /// An open-BG crew member then rests at the landing (<see cref="RestAtLanding"/>) before its next order, so it
    /// does not walk back into the fight with a third of its health.
    /// </summary>
    public static void OnLandingRelease(GameBot bot)
    {
        // A rest still running when the bot died again ends here, so every rest-start line has its end line.
        if (bot != null && States.TryGetValue(bot, out State previous) && previous.LandingRest) EndLandingRest(bot, previous, "died");
        ForgetState(bot);
        if (bot == null || !AutonomousBattlegroundParticipation.IsOpenCrew(bot)) return;
        State state = States.GetValue(bot, CreateState);
        state.LandingRest = true;
        state.LandingRestUntil = GameLoop.GameLoopTime + LandingRestMaxMilliseconds;
    }

    /// <summary>The landing rest ends once health (and power, for a caster) reach <see cref="LandingRestPercent"/>.</summary>
    public static bool LandingRestDone(int healthPercent, int powerPercent, bool usesPower) =>
        healthPercent >= LandingRestPercent && (!usesPower || powerPercent >= LandingRestPercent);

    /// <summary>
    /// Why the landing rest ends this turn: recovered (health and power reached <see cref="LandingRestPercent"/>), cap (its
    /// <see cref="LandingRestMaxMilliseconds"/> ran out first), or null while it goes on. A fight in between never ends it.
    /// </summary>
    public static string LandingRestEndReason(int healthPercent, int powerPercent, bool usesPower, long now, long until) =>
        LandingRestDone(healthPercent, powerPercent, usesPower) ? "recovered" : now >= until ? "cap" : null;

    /// <summary>
    /// One turn of the open-BG landing rest. True while the bot rests (or holds still until it may); false when the
    /// rest is over (recovered or timed out) or the bot is attacked, which it answers through the ordinary turn; the
    /// rest then resumes after the fight until it is over, and no order is taken before. Reuses the bots' recovery rest
    /// (fast regeneration while still and out of combat). Its start and its end are logged once per release.
    /// </summary>
    private static bool RestAtLanding(BotBrain brain, GameBot bot, State state, long now)
    {
        string end = LandingRestEndReason(bot.HealthPercent, bot.ManaPercent, bot.MaxMana > 0, now, state.LandingRestUntil);
        if (end != null)
        {
            EndLandingRest(bot, state, end);
            return false;
        }
        if (brain.HasAggro || bot.IsAttacking)
        {
            // Self-defence first; the rest resumes after the fight while its time lasts.
            if (bot.IsRecoveryResting) bot.WakeRecoveryRest();
            if (!state.LandingRestDefending) state.LandingRestDefended++;
            state.LandingRestDefending = true;
            return false;
        }
        state.LandingRestDefending = false;
        if (!state.LandingRestLogged)
        {
            state.LandingRestLogged = true;
            Log.Info($"AUTONOMOUS_BG_LANDING_REST region={bot.CurrentRegionID} bot=\"{bot.Name}\" health={bot.HealthPercent} power={bot.ManaPercent} " +
                $"until_pct={LandingRestPercent}");
        }
        bot.WakeRecoveryRestIfCombatBlocked();
        if (bot.IsRecoveryResting) return true;
        Stand(bot);
        // A cast in progress (a heal or rebuff) finishes first; the rest starts on a later turn.
        if (!bot.IsCasting && !BotRestRecovery.BlocksRest(bot)) bot.BeginRecoveryRest();
        return true;
    }

    // Ends the landing rest and logs its outcome: recovered, cap, order (relief walk or leaving), died (killed while resting).
    private static void EndLandingRest(GameBot bot, State state, string reason)
    {
        state.LandingRest = false;
        state.LandingRestDefending = false;
        if (bot.IsRecoveryResting) bot.WakeRecoveryRest();
        Log.Info($"AUTONOMOUS_BG_LANDING_REST_END region={bot.CurrentRegionID} bot=\"{bot.Name}\" health={bot.HealthPercent} power={bot.ManaPercent} " +
            $"reason={reason} defended={state.LandingRestDefended}");
    }

    private static bool Idle(GameBot bot, string reason)
    {
        AutonomousBattlegroundParticipation.RecordDriverIdle(bot, bot.CurrentRegionID, reason);
        return false;
    }

    private static State CreateState(GameBot bot) => new() { Camp = (int)(bot.DatabaseID & int.MaxValue) };

    private static void Lead(BotBrain brain, GameBot bot, BattlegroundDefinition definition, State state, long now)
    {
        AbstractGameKeep keep = BattlegroundCampaignCatalog.CentralKeep(definition);
        Point3D[] camps = BattlegroundCampaignManager.CampPositions(definition.RegionId);
        OpenBgRole? held = null;
        if (definition.Kind == BattlegroundDefinitionKind.Open && OpenBattlegroundDirector.TryGetOrder(bot, out OpenBgOrder order))
        {
            if (LeadOpen(brain, bot, keep, camps, order, state, now)) return;
            held = order.Role;
        }
        if (keep != null && FallbackAllows(held, LeadBranch.Defend) && bot.Guild != null && keep.Guild == bot.Guild)
        {
            DefendKeep(bot, state, keep, camps, now);
            return;
        }
        if (keep != null && FallbackAllows(held, LeadBranch.Claim) && TryClaim(bot, keep, state, now)) return;
        if (keep != null && FallbackAllows(held, LeadBranch.Assault) && CountPresent(bot) >= AssaultGroupMinimum &&
            TryAssault(brain, bot, keep, state, now)) return;
        if (!FallbackAllows(held, LeadBranch.Roam))
        {
            // Live run 0.242.0: every unreachable order sent its unit back to the arrival camps, where the crews and the
            // campaign patrols ground each other down. A unit with an order holds or returns to its last reachable point.
            HoldOrder(bot, state, now);
            return;
        }
        if (camps.Length == 0)
        {
            state.Action = "stand-no-camp";
            Stand(bot);
            return;
        }
        RoamCamps(bot, state, camps, now);
    }

    /// <summary>
    /// An open-BG crew unit's turn from its director order: a defeated keep is claimed first (the existing claim rule),
    /// then the keep's friends defend it and a full attacking group assaults it through the existing branches; every
    /// other role walks to its point (bridge, lurk spot, field point, approach or the way home). False when the order
    /// has no route; the caller then falls back only as far as the order allows (<see cref="FallbackAllows"/>), and the
    /// director is told once per unreachable window and gives a new point on a later tick.
    /// </summary>
    private static bool LeadOpen(BotBrain brain, GameBot bot, AbstractGameKeep keep, Point3D[] camps, OpenBgOrder order, State state, long now)
    {
        if (keep != null && TryClaim(bot, keep, state, now)) return true;
        if (order.AtKeep && keep != null)
        {
            if (order.Role == OpenBgRole.Defend)
            {
                DefendKeep(bot, state, keep, camps, now);
                return true;
            }
            if (CountPresent(bot) >= AssaultGroupMinimum && TryAssault(brain, bot, keep, state, now)) return true;
            // The keep centre lies behind the walls: march to the floor outside the nearest closed outer gate instead.
            // Only with every outer gate broken or open is the centre (the courtyard) the march goal again.
            GateMarch march = MarchToGate(bot, keep, state, now);
            if (march != GateMarch.NoGate) return march == GateMarch.Walking;
            state.Action = "march-keep";
            if (!GoTo(bot, state, order.Point, now)) return false;
            state.LastOrderPoint = order.Point;
            return true;
        }
        state.Action = OpenAction(order);
        if (order.Point == null) return false;
        // While the point sits in the unreachable cache GoTo refuses it without a proof; the director already knows.
        bool known = InUnreachableCache(state, order.Point, now);
        if (GoTo(bot, state, order.Point, now))
        {
            state.LastOrderPoint = order.Point;
            return true;
        }
        if (!known) OpenBattlegroundDirector.ReportUnreachable(order.Unit, order.Point);
        return false;
    }

    /// <summary>
    /// An open-BG unit whose order has no route: it walks back to the last order point it had a route to (its bridge, its
    /// last field point, the gate approach) or holds where it stands, until the director gives a new point. Never the camps.
    /// </summary>
    private static void HoldOrder(GameBot bot, State state, long now)
    {
        Point3D previous = state.LastOrderPoint;
        if (previous != null && bot.GetDistanceTo(previous) > ArrivalDistance)
        {
            state.Action = "return-order-point";
            if (GoTo(bot, state, previous, now)) return;
        }
        state.Action = "hold-order";
        Stand(bot);
    }

    /// <summary>
    /// The assault march: walks to the proven floor point outside the closed outer gate nearest the bot (its approach
    /// side). NoGate when the keep has no closed outer gate with a floor outside; Unreachable when no such point routes.
    /// </summary>
    private static GateMarch MarchToGate(GameBot bot, AbstractGameKeep keep, State state, long now)
    {
        GateApproach[] gates = GateApproaches.GetValue(keep, BuildGateApproaches)
            .Where(gate => gate.Points.Length > 0 && gate.Door.IsAlive && gate.Door.State == eDoorState.Closed)
            .OrderBy(gate => bot.GetDistanceTo(gate.Points[0])).ToArray();
        if (gates.Length == 0) return GateMarch.NoGate;
        state.Action = "march-gate";
        foreach (GateApproach gate in gates)
            foreach (Point3D point in gate.Points)
            {
                if (!GoTo(bot, state, point, now)) continue;
                state.LastOrderPoint = point;
                return GateMarch.Walking;
            }
        return GateMarch.Unreachable;
    }

    // Once per keep: its outer gates and, for each, the navmesh floor GateApproachDistances out from the keep centre.
    private static GateApproach[] BuildGateApproaches(AbstractGameKeep keep)
    {
        GameKeepDoor[] doors = keep.Doors.Values.Where(door => door != null && door.IsAttackableDoor).ToArray();
        Point3D centre = new(keep.X, keep.Y, keep.Z);
        var nav = PathfindingProvider.Instance;
        var gates = new List<GateApproach>();
        foreach (int index in OuterGates(centre, doors.Select(door => new Point3D(door.X, door.Y, door.Z)).ToArray()))
        {
            GameKeepDoor door = doors[index];
            Point3D gatePoint = new(door.X, door.Y, door.Z);
            var points = new List<Point3D>();
            foreach (int distance in GateApproachDistances)
            {
                Point3D raw = OutsideGate(centre, gatePoint, distance);
                Zone zone = raw == null ? null : door.CurrentRegion?.GetZone(raw.X, raw.Y);
                if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) continue;
                Vector3? floor = nav.GetClosestPoint(zone, new Vector3(raw.X, raw.Y, raw.Z), 64, 64, GateFloorRange, nav.DefaultFilters);
                if (!floor.HasValue || !float.IsFinite(floor.Value.Z) || Math.Abs(floor.Value.Z - door.Z) > GateFloorRange) continue;
                points.Add(new Point3D((int)Math.Round(floor.Value.X), (int)Math.Round(floor.Value.Y), (int)Math.Round(floor.Value.Z)));
            }
            gates.Add(new GateApproach(door, points.ToArray()));
            Log.Info($"AUTONOMOUS_BG_GATE_APPROACH region={keep.Region} keep={keep.KeepID} door={door.DoorId} gate={door.X},{door.Y},{door.Z} " +
                $"points={(points.Count == 0 ? "none" : string.Join(";", points.Select(point => $"{point.X},{point.Y},{point.Z}")))}");
        }
        return gates.ToArray();
    }

    /// <summary>
    /// The indexes of a keep's outer gates among its attackable doors: those within <see cref="OuterGateTolerance"/> (2D)
    /// of the door farthest from the keep centre. Inner doors (Thidranki Faste's keep door, 626 from the centre) are
    /// reached through the broken outer gate, not marched to.
    /// </summary>
    public static int[] OuterGates(Point3D keep, IReadOnlyList<Point3D> doors)
    {
        if (keep == null || doors == null || doors.Count == 0) return Array.Empty<int>();
        double[] distances = doors.Select(door => door == null ? -1 : Distance2D(keep, door)).ToArray();
        double farthest = distances.Max();
        if (farthest <= 0) return Array.Empty<int>();
        return Enumerable.Range(0, doors.Count).Where(index => distances[index] >= 0 && distances[index] >= farthest - OuterGateTolerance).ToArray();
    }

    /// <summary>The point <paramref name="distance"/> out from a gate, on the line from the keep centre through it (null at the centre).</summary>
    public static Point3D OutsideGate(Point3D keep, Point3D gate, int distance)
    {
        if (keep == null || gate == null) return null;
        double dx = gate.X - keep.X, dy = gate.Y - keep.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1) return null;
        return new Point3D((int)Math.Round(gate.X + dx / length * distance), (int)Math.Round(gate.Y + dy / length * distance), gate.Z);
    }

    private static double Distance2D(Point3D a, Point3D b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool InUnreachableCache(State state, Point3D goal, long now) =>
        state.UnreachableUntil != null && state.UnreachableUntil.TryGetValue((goal.X, goal.Y, goal.Z), out long retry) && now < retry;

    /// <summary>The bracket priority's branches below an open-BG order.</summary>
    public enum LeadBranch { Defend, Claim, Assault, Roam }

    /// <summary>
    /// Which bracket branch may run after an open-BG order found no route. Without an order (brackets, a unit not yet
    /// ordered) every branch runs as before. A unit holding an order defends only for Defend and assaults only for
    /// Assault; the claim was already tried by the order; so a group whose roam, lurk or bridge point is unreachable
    /// never sieges a door in Quiet or Push. No unit holding an order roams the camps: it holds or returns to its last
    /// reachable order point instead (live run 0.242.0, the arrival-camp grinder).
    /// </summary>
    public static bool FallbackAllows(OpenBgRole? heldOrder, LeadBranch branch) => heldOrder switch
    {
        null => true,
        OpenBgRole role => branch switch
        {
            LeadBranch.Defend => role == OpenBgRole.Defend,
            LeadBranch.Assault => role == OpenBgRole.Assault,
            _ => false,
        },
    };

    /// <summary>The driver action logged for an open-BG order that walks to a point.</summary>
    public static string OpenAction(OpenBgOrder order) => order.Role switch
    {
        OpenBgRole.BridgeWatch => "bridge-watch",
        OpenBgRole.Lurk => "lurk",
        OpenBgRole.Roam => "roam-field",
        OpenBgRole.Retreat => "retreat",
        OpenBgRole.Assault => order.Muster ? "muster-bridge" : "skirmish-keep",
        _ => "hold-approach",
    };

    // Defend: hold the keep, or its nearest camp when the keep has no proven route.
    private static void DefendKeep(GameBot bot, State state, AbstractGameKeep keep, Point3D[] camps, long now)
    {
        state.Action = "defend-keep";
        Point3D keepPoint = new(keep.X, keep.Y, keep.Z);
        if (!GoTo(bot, state, keepPoint, now) && camps.Length > 0)
            GoTo(bot, state, Nearest(camps, keepPoint), now);
    }

    // Assault: the next closed door, then the lord, from a proven standing point. False when there is nothing to do.
    private static bool TryAssault(BotBrain brain, GameBot bot, AbstractGameKeep keep, State state, long now)
    {
        // The target and its proven approach are re-proved on the route interval, not every turn.
        if (now >= state.NextAssault || state.AssaultTarget == null || !state.AssaultTarget.IsAlive)
        {
            state.NextAssault = now + RouteIntervalMilliseconds;
            state.AssaultTarget = BattlegroundCampaignManager.SiegeTarget(keep, bot, out Point3D approach);
            state.AssaultApproach = approach;
        }
        GameLiving target = state.AssaultTarget;
        if (target == null) return false;
        if (bot.IsWithinRadius(target, SiegeRange) && BotSiegeRuntime.Visible(bot, target))
        {
            state.Action = "assault-keep";
            brain.Attack(target);
            Stand(bot);
            return true;
        }
        if (state.AssaultApproach == null) return false;
        state.Action = "approach-keep";
        return GoTo(bot, state, state.AssaultApproach, now);
    }

    /// <summary>
    /// open BG target rule: a human (or a human's companion) below level 30 is only taken on by the 40-50 crew after
    /// it, its group or its companions attacked this bot or its group; everyone else is fair game as before.
    /// </summary>
    private static bool MayAcquire(GameBot bot, GameLiving target) => MayEngage(bot, target, openBattlegroundRule: true);

    /// <summary>
    /// The shared open-BG target rule (<see cref="OpenBattlegroundRoles.MayEngageInOpenBattleground"/>) for one target.
    /// The class brain's PvP crowd control passes <see cref="HoldsOpenBattlegroundRule"/>, so brackets and the world
    /// keep their sweep. Who attacked is only looked up for a protected target.
    /// </summary>
    public static bool MayEngage(GameBot bot, GameLiving target, bool openBattlegroundRule)
    {
        if (bot == null || target == null) return true;
        bool human = IsHumanSide(target);
        bool shielded = openBattlegroundRule && human && !OpenBattlegroundRoles.MayHuntHuman(bot.Level, target.Level, false);
        return OpenBattlegroundRoles.MayEngageInOpenBattleground(openBattlegroundRule, human, bot.Level, target.Level,
            shielded && AttackedUs(bot, target));
    }

    /// <summary>An open-BG crew bot standing in the open BG: the low-level protection binds it. Brackets and the world never.</summary>
    public static bool HoldsOpenBattlegroundRule(GameBot bot) =>
        bot is { IsAutonomousWorldBot: true } &&
        BattlegroundCampaignCatalog.Find(bot.CurrentRegionID)?.Kind == BattlegroundDefinitionKind.Open &&
        AutonomousBattlegroundParticipation.IsOpenCrew(bot);

    /// <summary>
    /// The class brain's single-target check uses <see cref="MayEngage"/>; an area spell (mez sweep, healer stun) is also
    /// refused when a spared target stands within its radius of the centre.
    /// </summary>
    public static bool CatchesSpared(IReadOnlyList<GameLiving> spared, GameLiving center, int radius) =>
        radius > 0 && center != null && spared != null && spared.Any(target => target != null && target.IsWithinRadius(center, radius));

    // A player, or a companion or helper of a player (never another autonomous or encounter bot).
    private static bool IsHumanSide(GameLiving target) =>
        target is GamePlayer || target is GameBot { IsAutonomousWorldBot: false, IsBattlegroundEncounterBot: false };

    private static bool AttackedUs(GameBot bot, GameLiving target)
    {
        HashSet<GameLiving> theirs = HumanSide(target);
        IEnumerable<GameLiving> ours = bot.Group == null ? [bot] : bot.Group.GetMembersInTheGroup();
        foreach (GameLiving member in ours)
            if (member?.attackComponent?.AttackerTracker is AttackerTracker tracker && AttackedBySide(tracker.Attackers, theirs)) return true;
        return false;
    }

    /// <summary>
    /// The target, its group, and for a companion its owner and the owner's group: a lowbie healer whose companions or
    /// group mates attack the crew is fair game.
    /// </summary>
    public static HashSet<GameLiving> HumanSide(GameLiving target)
    {
        var side = new HashSet<GameLiving>();
        if (target == null) return side;
        side.Add(target);
        GamePlayer owner = (target as GameBot)?.Owner;
        if (owner != null) side.Add(owner);
        foreach (Group group in new[] { target.Group, owner?.Group })
            if (group != null)
                foreach (GameLiving member in group.GetMembersInTheGroup())
                    if (member != null) side.Add(member);
        return side;
    }

    /// <summary>True when one of the attackers (a pet counts as its owner) belongs to the side.</summary>
    public static bool AttackedBySide(IEnumerable<GameLiving> attackers, ISet<GameLiving> side) =>
        attackers != null && side != null && attackers.Any(attacker =>
            attacker != null && (side.Contains(attacker) || PvpCombatant.Resolve(attacker) is GameLiving master && side.Contains(master)));

    /// <summary>
    /// Walks toward the first camp with a proven route, starting at the bot's current camp and skipping arrived
    /// or unreachable ones. When no camp is reachable the bot stands, and the stuck rule still removes it.
    /// </summary>
    private static void RoamCamps(GameBot bot, State state, Point3D[] camps, long now)
    {
        int count = camps.Length;
        int start = state.Camp % count;
        if (bot.GetDistanceTo(camps[start]) <= ArrivalDistance)
            start = ++state.Camp % count;
        state.Action = "roam-camp";
        if (now < state.CampsUnreachableUntil)
        {
            Stand(bot);
            return;
        }
        for (int offset = 0; offset < count; offset++)
        {
            int index = (start + offset) % count;
            if (!GoTo(bot, state, camps[index], now)) continue;
            state.Camp = index;
            state.CampsUnreachableUntil = 0;
            return;
        }
        state.CampsUnreachableUntil = now + RouteIntervalMilliseconds;
    }

    private static void Assist(BotBrain brain, GameBot bot, GameBot leader, State state, long now)
    {
        GameLiving target = leader.TargetObject as GameLiving;
        if (target != null && BattlegroundCombatTargets.IsLegal(bot, target, bot.CurrentZone) &&
            bot.IsWithinRadius(target, OpponentRange) && !BotPvpCrowdControl.Protected(bot, target) &&
            BotSiegeRuntime.Visible(bot, target))
        {
            state.Action = "assist-attack";
            brain.Attack(target);
            return;
        }
        Point3D follow = new(leader.X, leader.Y, leader.Z);
        if (bot.GetDistanceTo(follow) > FollowDistance)
        {
            state.Action = "follow-leader";
            GoTo(bot, state, follow, now);
        }
        else
        {
            state.Action = "hold-leader";
            state.Goal = follow;
            state.HoldProgress = AutonomousBattlegroundParticipation.IsProgressing(leader);
            Stand(bot);
        }
    }

    private static bool TryClaim(GameBot bot, AbstractGameKeep keep, State state, long now)
    {
        if (keep.Guild != null || keep.DBKeep == null || !keep.DBKeep.LordDefeated || !AutonomousRvrKeepPolicy.IsClaimableKeep(keep)) return false;
        if (bot.Guild?.HasRank(bot, Guild.eRank.Claim) != true || bot.InCombat || bot.Group != null && bot.Group.LivingLeader != bot) return false;
        PvpKeepCampaign.EnsureClaimPoint(keep);
        KeepClaimPoint steward = keep.ClaimPoint;
        if (steward == null) return false;
        state.Action = "claim-keep";
        if (bot.IsWithinRadius(steward, WorldMgr.INTERACT_DISTANCE))
        {
            if (now >= state.NextClaim)
            {
                state.NextClaim = now + ClaimRetryMilliseconds;
                steward.TryClaim(bot);
            }
            Stand(bot);
            return true;
        }
        return GoTo(bot, state, new Point3D(steward.X, steward.Y, steward.Z), now);
    }

    private static int CountPresent(GameBot bot) =>
        bot.Group == null ? 1 : bot.Group.GetMembersInTheGroup().Count(member => member.IsAlive && member.CurrentRegion == bot.CurrentRegion);

    private static Point3D Nearest(Point3D[] points, Point3D origin) =>
        points.OrderBy(point => (long)(point.X - origin.X) * (point.X - origin.X) + (long)(point.Y - origin.Y) * (point.Y - origin.Y)).First();

    /// <summary>Walks toward a goal along a proven route. False when no route exists.</summary>
    private static bool GoTo(GameBot bot, State state, Point3D goal, long now)
    {
        state.Goal = goal;
        if (bot.GetDistanceTo(goal) <= ArrivalDistance)
        {
            Stand(bot);
            return true;
        }
        (int, int, int) key = (goal.X, goal.Y, goal.Z);
        Point3D previous = state.RouteGoal;
        bool sameGoal = previous != null && previous.X == goal.X && previous.Y == goal.Y && previous.Z == goal.Z;
        if (state.UnreachableUntil != null && state.UnreachableUntil.TryGetValue(key, out long retry) && now < retry)
        {
            // A goal proven unreachable within the retry window: refuse it without
            // touching the route state, so a caller's fallback goal (defend-keep →
            // camp, approach → roam) keeps its own 3-s route cadence instead of
            // being re-proved every think. Only stop when this goal was being walked.
            if (sameGoal) Stand(bot);
            return false;
        }
        if (!sameGoal || now >= state.RouteUntil)
        {
            state.RouteGoal = goal;
            state.RouteUntil = now + RouteIntervalMilliseconds;
            state.RouteOk = PathExists(bot, goal, out state.RoutePosition, out string reason);
            if (state.RouteOk && !bot.IsCasting) bot.PathTo(state.RoutePosition, bot.MaxSpeed);
            state.NoRouteStreak = NextNoRouteStreak(state.NoRouteStreak, state.RouteOk, reason);
            if (!state.RouteOk)
            {
                RecordRouteFailure(bot, state, goal, reason, now);
                MarkUnreachable(state, key, now);
                if (state.Open && TryIslandRecovery(bot, state, now)) return false;
            }
        }
        if (!state.RouteOk)
        {
            Stand(bot);
            return false;
        }
        return true;
    }

    // Logged once per goal and visit: the cause of an unproven route, for the next live run. In the open BG at most
    // one line per bot per game minute: a follower's goal moves with its leader, so one stuck bot logged hundreds.
    private static void RecordRouteFailure(GameBot bot, State state, Point3D goal, string reason, long now)
    {
        state.FailedGoals ??= new HashSet<(int, int, int)>();
        if (!state.FailedGoals.Add((goal.X, goal.Y, goal.Z))) return;
        if (state.Open)
        {
            if (!MayLogRouteFailure(now, state.NextRouteFailureLog))
            {
                state.SuppressedRouteFailures++;
                return;
            }
            state.NextRouteFailureLog = now + RouteFailureLogMilliseconds;
        }
        AutonomousBattlegroundParticipation.RecordRouteFailure(bot, bot.CurrentRegionID, state.Action,
            $"{goal.X},{goal.Y},{goal.Z}", $"{bot.X},{bot.Y},{bot.Z}", reason, state.SuppressedRouteFailures);
        state.SuppressedRouteFailures = 0;
    }

    /// <summary>open BG: whether a bot's next route-failure line may be written now (one per <see cref="RouteFailureLogMilliseconds"/>).</summary>
    public static bool MayLogRouteFailure(long now, long nextAllowed) => now >= nextAllowed;

    /// <summary>Goals in a row that failed with no_route; a proven route or any other failure ends the streak.</summary>
    public static int NextNoRouteStreak(int streak, bool routeOk, string reason) =>
        !routeOk && reason == "no_route" ? streak + 1 : 0;

    /// <summary>
    /// An open-BG bot stands on a disconnected navmesh island when its last <see cref="IslandNoRouteStreak"/> goals
    /// had no route, it is not at its landing, and its own position cannot route to its landing either (the advisor
    /// found such islands 100-160 units below the walkable ground beside the portal-keep gates).
    /// </summary>
    public static bool NeedsIslandRecovery(int noRouteStreak, bool atLanding, string landingRouteReason) =>
        noRouteStreak >= IslandNoRouteStreak && !atLanding && landingRouteReason is "no_route" or "dead_end";

    // open BG island recovery: at most once per IslandCheckMilliseconds, prove the bot's way to its landing and, when
    // it has none, move it onto the landing; the stuck rule no longer has to eject it. True when the bot was moved.
    private static bool TryIslandRecovery(GameBot bot, State state, long now)
    {
        if (state.NoRouteStreak < IslandNoRouteStreak || now < state.NextIslandCheck) return false;
        state.NextIslandCheck = now + IslandCheckMilliseconds;
        BattlegroundDefinition definition = BattlegroundCampaignCatalog.Find(bot.CurrentRegionID);
        GameLocation landing = definition == null ? null : BattlegroundCampaignPolicy.GetLanding(bot, definition);
        if (landing == null || landing.RegionID != bot.CurrentRegionID) return false;
        Point3D landingPoint = new(landing.X, landing.Y, landing.Z);
        bool atLanding = bot.GetDistanceTo(landingPoint) <= ArrivalDistance;
        string landingReason = null;
        if (!atLanding && PathExists(bot, landingPoint, out _, out landingReason)) landingReason = null;
        if (!NeedsIslandRecovery(state.NoRouteStreak, atLanding, landingReason))
        {
            if (landingReason == null) state.NoRouteStreak = 0;
            return false;
        }
        string from = $"{bot.X},{bot.Y},{bot.Z}";
        int streak = state.NoRouteStreak;
        Stand(bot);
        if (!bot.MoveTo(landing.RegionID, landing.X, landing.Y, landing.Z, landing.Heading)) return false;
        state.NoRouteStreak = 0;
        state.RouteGoal = null;
        state.RouteOk = false;
        state.UnreachableUntil?.Clear();
        state.CampsUnreachableUntil = 0;
        state.HasPosition = false;
        Log.Info($"AUTONOMOUS_BG_ISLAND_RECOVERY region={bot.CurrentRegionID} bot=\"{bot.Name}\" from={from} " +
            $"to={landing.X},{landing.Y},{landing.Z} no_route_streak={streak} landing_route={landingReason}");
        AutonomousBattlegroundParticipation.NoteProgress(bot, eAutonomousProgressKind.Recovery);
        return true;
    }

    private static void MarkUnreachable(State state, (int, int, int) key, long now)
    {
        state.UnreachableUntil ??= new Dictionary<(int, int, int), long>();
        // Follow goals move with their leader; drop expired keys so the table stays small.
        if (state.UnreachableUntil.Count >= 32)
            foreach ((int, int, int) stale in state.UnreachableUntil.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToList())
                state.UnreachableUntil.Remove(stale);
        state.UnreachableUntil[key] = now + UnreachableRetryMilliseconds;
    }

    /// <summary>
    /// Proves a walkable route to the goal, reporting the first failed check in <paramref name="reason"/>.
    /// The blocking-door filter is tried first. Then the default filters are accepted when every closed gate on
    /// the route is one this bot may pass, which is the native mover's own rule: it plots with default filters and
    /// exempts friendly keep doors. Portal keeps hold the battleground landings and their gates stay closed, so
    /// the blocking-door filter alone cannot leave them. Both passes chain capped legs (<see cref="ChainRoute"/>);
    /// on success <paramref name="position"/> is the point to walk to now: the goal, or the end of the first leg.
    /// </summary>
    private static bool PathExists(GameBot bot, Point3D goal, out Vector3 position, out string reason)
    {
        position = default;
        reason = "no_navmesh";
        Zone zone = bot.CurrentZone;
        var nav = PathfindingProvider.Instance;
        if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
        position = new(goal.X, goal.Y, goal.Z);
        if (!nav.TrySnapToMesh(zone, ref position, 100))
        {
            reason = "off_mesh";
            return false;
        }
        if (Math.Abs(position.Z - goal.Z) > 100)
        {
            reason = "z_mismatch";
            return false;
        }
        Vector3 from = new(bot.X, bot.Y, bot.Z);
        Vector3 target = position;
        WrappedPathfindingNode[] nodes = System.Buffers.ArrayPool<WrappedPathfindingNode>.Shared.Rent(512);
        try
        {
            if (ChainRoute(from, target, MaxRouteLegs,
                    start => Leg(nav, zone, start, target, nav.BlockingDoorAvoidanceFilters, nodes, out _),
                    out Vector3 walkTo) == RouteChain.Reached)
            {
                position = walkTo;
                reason = null;
                return true;
            }
            bool closedGate = false;
            RouteChain open = ChainRoute(from, target, MaxRouteLegs, start =>
            {
                RouteLeg leg = Leg(nav, zone, start, target, nav.DefaultFilters, nodes, out int count);
                if (count == 0 || !CrossesBlockedDoor(bot, nodes, count)) return leg;
                closedGate = true;
                return new RouteLeg(PathfindingStatus.NoPathFound, start);
            }, out walkTo);
            if (open == RouteChain.Reached)
            {
                position = walkTo;
                reason = null;
                return true;
            }
            reason = closedGate ? "closed_gate" : open switch
            {
                RouteChain.Capped => "corridor_cap",
                RouteChain.DeadEnd => "dead_end",
                _ => "no_route",
            };
            return false;
        }
        finally
        {
            System.Buffers.ArrayPool<WrappedPathfindingNode>.Shared.Return(nodes);
        }
    }

    /// <summary>One native path query as a <see cref="ChainRoute"/> leg (the campaign's door approach proof chains these too).</summary>
    internal static RouteLeg QueryLeg(IPathfindingMgr nav, Zone zone, Vector3 start, Vector3 goal, EDtPolyFlags[] filters,
        WrappedPathfindingNode[] nodes) => Leg(nav, zone, start, goal, filters, nodes, out _);

    // One native query from start toward the goal; count is the number of route nodes left in the buffer.
    private static RouteLeg Leg(IPathfindingMgr nav, Zone zone, Vector3 start, Vector3 goal, EDtPolyFlags[] filters,
        WrappedPathfindingNode[] nodes, out int count)
    {
        PathfindingResult result = nav.GetPathStraight(zone, start, goal, filters, nodes);
        count = result.Status is PathfindingStatus.PathFound or PathfindingStatus.PartialPathFound &&
            result.NodeCount > 0 && result.NodeCount <= nodes.Length ? result.NodeCount : 0;
        return count == 0 ? new RouteLeg(PathfindingStatus.NoPathFound, start) : new RouteLeg(result.Status, nodes[count - 1].Position);
    }

    /// <summary>
    /// Chains corridor-capped Detour legs from <paramref name="from"/> toward <paramref name="goal"/>, each leg
    /// starting where the previous one ended, at most <paramref name="maxLegs"/> queries. A partial path proves a
    /// route only when a later leg reaches the goal; the bot then walks to the end of the first leg, which the
    /// native mover plots in one piece (it pauses persistent bots on a partial path). A leg that stops making
    /// progress is a dead end; a chain still progressing after the last leg is capped.
    /// </summary>
    public static RouteChain ChainRoute(Vector3 from, Vector3 goal, int maxLegs, Func<Vector3, RouteLeg> leg, out Vector3 walkTo)
    {
        walkTo = goal;
        Vector3 start = from;
        for (int index = 0; index < maxLegs; index++)
        {
            RouteLeg result = leg(start);
            if (result.Status == PathfindingStatus.PathFound ||
                result.Status == PathfindingStatus.PartialPathFound &&
                Vector3.DistanceSquared(result.End, goal) <= LegReachDistance * LegReachDistance)
                return RouteChain.Reached;
            if (result.Status != PathfindingStatus.PartialPathFound)
                return index == 0 ? RouteChain.NoPath : RouteChain.DeadEnd;
            if (Vector3.DistanceSquared(result.End, start) < 1)
                return RouteChain.DeadEnd;
            if (index == 0) walkTo = result.End;
            start = result.End;
        }
        return RouteChain.Capped;
    }

    /// <summary>True when a door near the route's door nodes blocks this bot, as in the native Pathfinder.</summary>
    private static bool CrossesBlockedDoor(GameBot bot, Span<WrappedPathfindingNode> nodes, int count)
    {
        if (bot.CurrentRegion == null) return false;
        var doors = new List<GameDoorBase>();
        for (int i = 0; i < count; i++)
        {
            if ((nodes[i].Flags & EDtPolyFlags.AnyDoor) == 0) continue;
            Vector3 point = nodes[i].Position;
            doors.Clear();
            bot.CurrentRegion.GetInRadius(new Point3D(point.X, point.Y, point.Z), eGameObjectType.DOOR, Pathfinder.DOOR_SEARCH_DISTANCE, doors);
            foreach (GameDoorBase door in doors)
                if (IsRouteBlockingDoor(door.State == eDoorState.Closed, door.CanBeOpenedViaInteraction,
                    door is GameKeepDoor keepDoor && Pathfinder.CanUseFriendlyKeepDoor(bot, keepDoor)))
                    return true;
        }
        return false;
    }

    /// <summary>The native mover's closed-door rule: a closed gate that interaction cannot open blocks unless the bot may pass it.</summary>
    public static bool IsRouteBlockingDoor(bool closed, bool openableByInteraction, bool friendlyKeepDoor) =>
        closed && !openableByInteraction && !friendlyKeepDoor;

    /// <summary>
    /// open BG: one line per crew death at the moment it happens. The world controller's death line is written only after
    /// the bot left the battleground, once, with its last killer (live run 0.242.0: 23 lines for 246 deaths).
    /// </summary>
    public static void OnParticipantDeath(GameBot bot, GameObject killer)
    {
        if (bot is not { IsAutonomousWorldBot: true } || !Log.IsInfoEnabled ||
            BattlegroundCampaignCatalog.Find(bot.CurrentRegionID)?.Kind != BattlegroundDefinitionKind.Open ||
            !AutonomousBattlegroundParticipation.IsOpenCrew(bot)) return;
        GameLiving raw = killer as GameLiving;
        GameLiving identity = PvpCombatant.Resolve(raw) ?? raw;
        Log.Info(DeathLine(bot.CurrentRegionID, bot.Name, bot.Realm, bot.Guild?.Name, identity?.Name ?? killer?.Name,
            identity?.Realm ?? eRealm.None, KillerKind(identity), GuildOf(identity)?.Name, bot.X, bot.Y, bot.Z));
    }

    /// <summary>The open-BG death line; same_realm only when the killer has the victim's (player) realm.</summary>
    public static string DeathLine(ushort region, string bot, eRealm realm, string guild, string killer, eRealm killerRealm,
        string killerKind, string killerGuild, int x, int y, int z) =>
        $"AUTONOMOUS_BG_DEATH region={region} bot=\"{bot}\" realm={GlobalConstants.RealmToName(realm)} killer=\"{killer ?? "none"}\" " +
        $"killer_realm={GlobalConstants.RealmToName(killerRealm)} " +
        $"same_realm={realm != eRealm.None && realm == killerRealm} position={x},{y},{z} killer_kind={killerKind} " +
        $"guild=\"{guild ?? string.Empty}\" killer_guild=\"{killerGuild ?? string.Empty}\"";

    // Who the killer is in open-BG terms: crew, the campaign or guest squads, the keep's guards, a human, or anything else.
    private static string KillerKind(GameLiving killer) => killer switch
    {
        null => "none",
        GamePlayer => "player",
        GameBot { IsBattlegroundEncounterBot: true } => "squad",
        GameBot { IsAutonomousWorldBot: true } bot => AutonomousBattlegroundParticipation.IsOpenCrew(bot) ? "crew" : "world_bot",
        GameBot => "companion",
        GameKeepGuard => "guard",
        _ => "npc",
    };

    private static Guild GuildOf(GameLiving living) => living switch
    {
        GamePlayer player => player.Guild,
        GameBot bot => bot.Guild,
        _ => null,
    };

    private static void Stand(GameBot bot)
    {
        bot.StopMovingOnPath();
        bot.StopMoving();
    }

    /// <summary>
    /// Real progress is combat, arrival, ground covered since the last recorded progress point, or a follower
    /// holding near a leader that is itself progressing.
    /// </summary>
    private static void Observe(GameBot bot, State state, bool engaged)
    {
        bool arrived = state.Goal != null && bot.GetDistanceTo(state.Goal) <= ArrivalDistance;
        bool moved = !state.HasPosition ||
            Math.Max(Math.Abs(bot.X - state.ProgressX), Math.Abs(bot.Y - state.ProgressY)) >= ProgressDistance;
        if (!engaged && !arrived && !moved && !state.HoldProgress) return;
        if (moved)
        {
            state.HasPosition = true;
            state.ProgressX = bot.X;
            state.ProgressY = bot.Y;
        }
        AutonomousBattlegroundParticipation.NoteProgress(bot, engaged ? eAutonomousProgressKind.Combat : eAutonomousProgressKind.Movement);
    }
}
