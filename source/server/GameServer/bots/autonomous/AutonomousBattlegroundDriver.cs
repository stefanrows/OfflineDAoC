using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// In-battleground intent for admitted autonomous participants. Priority:
/// (1) defend the keep when the bot's guild holds it, (2) claim a defeated keep
/// at its steward with claim rank, (3) assault the keep (door, then lord) with a
/// group of four or more present, (4) otherwise roam between campaign camps.
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
    }

    private static readonly ConditionalWeakTable<GameBot, State> States = new();

    /// <summary>
    /// Runs one driver turn. Returns false when the bot takes no action this turn
    /// (dead, incapacitated, or it has just left the battleground).
    /// </summary>
    public static bool Turn(BotBrain brain, GameBot bot)
    {
        if (brain == null || bot == null) return false;
        // Each early return names its reason, so the live log shows why a participant idles.
        if (!bot.IsAlive) return Idle(bot, "not_alive");
        if (bot.IsIncapacitated) return Idle(bot, "crowd_controlled");
        if (AutonomousBattlegroundParticipation.IsLeaving(bot) && AutonomousBattlegroundParticipation.TryFinishLeaving(bot)) return Idle(bot, "left");
        BattlegroundDefinition definition = BattlegroundCampaignCatalog.Find(bot.CurrentRegionID);
        if (definition == null) return Idle(bot, "outside_battleground");

        State state = States.GetValue(bot, CreateState);
        long now = GameLoop.GameLoopTime;
        if (now >= state.NextScan)
        {
            state.NextScan = now + ScanMilliseconds;
            GameLiving opponent = BattlegroundCombatTargets.FindOpponent(bot, OpponentRange);
            if (opponent != null) brain.Attack(opponent);
        }

        bool engaged = brain.HasAggro || bot.InCombat;
        state.Action = engaged ? "engaged" : "idle";
        state.HoldProgress = false;
        if (!engaged)
        {
            GameBot leader = bot.Group?.LivingLeader as GameBot;
            if (AutonomousBattlegroundParticipation.IsLeaving(bot)) { state.Action = "leaving"; Stand(bot); }
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
        if (keep != null && bot.Guild != null && keep.Guild == bot.Guild)
        {
            // Defend: hold the keep, or its nearest camp when the keep has no proven route.
            state.Action = "defend-keep";
            Point3D keepPoint = new(keep.X, keep.Y, keep.Z);
            if (!GoTo(bot, state, keepPoint, now) && camps.Length > 0)
                GoTo(bot, state, Nearest(camps, keepPoint), now);
            return;
        }
        if (keep != null && TryClaim(bot, keep, state, now)) return;
        if (keep != null && CountPresent(bot) >= AssaultGroupMinimum)
        {
            // The target and its proven approach are re-proved on the route interval, not every turn.
            if (now >= state.NextAssault || state.AssaultTarget == null || !state.AssaultTarget.IsAlive)
            {
                state.NextAssault = now + RouteIntervalMilliseconds;
                state.AssaultTarget = BattlegroundCampaignManager.SiegeTarget(keep, bot, out Point3D approach);
                state.AssaultApproach = approach;
            }
            GameLiving target = state.AssaultTarget;
            if (target != null)
            {
                if (bot.IsWithinRadius(target, SiegeRange) && BotSiegeRuntime.Visible(bot, target))
                {
                    state.Action = "assault-keep";
                    brain.Attack(target);
                    Stand(bot);
                    return;
                }
                if (state.AssaultApproach != null)
                {
                    state.Action = "approach-keep";
                    if (GoTo(bot, state, state.AssaultApproach, now)) return;
                }
            }
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
        Point3D previous = state.RouteGoal;
        bool sameGoal = previous != null && previous.X == goal.X && previous.Y == goal.Y && previous.Z == goal.Z;
        if (!sameGoal || now >= state.RouteUntil)
        {
            state.RouteGoal = goal;
            state.RouteUntil = now + RouteIntervalMilliseconds;
            state.RouteOk = PathExists(bot, goal, out state.RoutePosition, out string reason);
            if (state.RouteOk && !bot.IsCasting) bot.PathTo(state.RoutePosition, bot.MaxSpeed);
            if (!state.RouteOk) RecordRouteFailure(bot, state, goal, reason);
        }
        if (!state.RouteOk)
        {
            Stand(bot);
            return false;
        }
        return true;
    }

    // Logged once per goal and visit: the cause of an unproven route, for the next live run.
    private static void RecordRouteFailure(GameBot bot, State state, Point3D goal, string reason)
    {
        state.FailedGoals ??= new HashSet<(int, int, int)>();
        if (!state.FailedGoals.Add((goal.X, goal.Y, goal.Z))) return;
        AutonomousBattlegroundParticipation.RecordRouteFailure(bot, bot.CurrentRegionID, state.Action,
            $"{goal.X},{goal.Y},{goal.Z}", $"{bot.X},{bot.Y},{bot.Z}", reason);
    }

    /// <summary>
    /// Proves a walkable route to the goal, reporting the first failed check in <paramref name="reason"/>.
    /// The blocking-door filter is tried first. Then the default filters are accepted when every closed gate on
    /// the route is one this bot may pass, which is the native mover's own rule: it plots with default filters and
    /// exempts friendly keep doors. Portal keeps hold the battleground landings and their gates stay closed, so
    /// the blocking-door filter alone cannot leave them.
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
        Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[512];
        if (nav.GetPathStraight(zone, from, position, nav.BlockingDoorAvoidanceFilters, nodes).Status == PathfindingStatus.PathFound)
        {
            reason = null;
            return true;
        }
        PathfindingResult open = nav.GetPathStraight(zone, from, position, nav.DefaultFilters, nodes);
        if (open.Status != PathfindingStatus.PathFound)
        {
            reason = "no_route";
            return false;
        }
        if (CrossesBlockedDoor(bot, nodes, open.NodeCount))
        {
            reason = "closed_gate";
            return false;
        }
        reason = null;
        return true;
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
