using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>
/// A 2003 group leader who had to wait for a straggler did not stand like a
/// post: they ran small circles at the meeting spot, so the group never lost
/// its running pace. Where an autonomous travel leader used to stop for a
/// member (siege-column hold, RvR roam hold, group travel hold), it now runs
/// a small loop of 4-6 navmesh-checked points, 300-450 units around the spot
/// where the hold began, at full run speed. Every existing hold timer, gap
/// threshold and give-up rule is unchanged; only the standing still is gone.
/// Without a valid loop the leader holds as before. Autonomous world bots only.
/// </summary>
public static class AutonomousLeaderLoop
{
    public const float MinimumRadius = 300;
    public const float MaximumRadius = 450;
    public const int MinimumPoints = 4;
    public const int MaximumPoints = 6;
    /// <summary>Fewer valid points than this is no loop: the leader holds.</summary>
    public const int MinimumValidPoints = 3;
    public const float ArrivalRadius = 70;
    /// <summary>A hold not refreshed for this long has ended; the next one starts a new loop.</summary>
    public const long NewHoldGapMilliseconds = 3_000;
    /// <summary>No loop this close to a keep or tower: courtyards and gates stay calm.</summary>
    public const float KeepClearance = 1_800;
    /// <summary>No loop this close to the next zone crossing, so the loop cannot zone the leader.</summary>
    public const float CrossingClearance = 1_000;
    /// <summary>No loop this close to the party's PvE camp, so the loop cannot pull the spawn.</summary>
    public const float CampClearance = 2_500;

    public readonly record struct Shape(float Radius, int Points, bool Clockwise, double StartRadians);

    // ------------------------------------------------------------ pure rules

    /// <summary>Whether a holding leader may loop instead of standing still.</summary>
    public static bool MayLoop(bool inCombat, bool nearKeep, bool operatingSiege, bool expedition,
        bool stealthed, bool dungeon, bool nearHazard = false) =>
        !inCombat && !nearKeep && !operatingSiege && !expedition && !stealthed && !dungeon && !nearHazard;

    /// <summary>
    /// The loop's shape: each leader keeps its own turning direction (a habit,
    /// from its id); radius, point count and start angle are rolled per hold.
    /// </summary>
    public static Shape ShapeFor(long leaderKey, double radiusRoll, double countRoll, double angleRoll) =>
        new(MinimumRadius + (MaximumRadius - MinimumRadius) * (float)Math.Clamp(radiusRoll, 0, 1),
            MinimumPoints + Math.Min(MaximumPoints - MinimumPoints, (int)(Math.Clamp(countRoll, 0, 1) * (MaximumPoints - MinimumPoints + 1))),
            (unchecked((ulong)leaderKey) * 2654435761UL >> 7 & 1) == 0,
            Math.Clamp(angleRoll, 0, 1) * Math.PI * 2);

    /// <summary>The raw loop points on a circle around <paramref name="centre"/>, in turning order.</summary>
    public static Vector3[] LoopPoints(Vector3 centre, Shape shape)
    {
        int count = Math.Clamp(shape.Points, MinimumPoints, MaximumPoints);
        var points = new Vector3[count];
        double step = Math.PI * 2 / count * (shape.Clockwise ? -1 : 1);
        for (int i = 0; i < count; i++)
        {
            double angle = shape.StartRadians + step * i;
            points[i] = new(centre.X + (float)(Math.Cos(angle) * shape.Radius),
                centre.Y + (float)(Math.Sin(angle) * shape.Radius), centre.Z);
        }
        return points;
    }

    /// <summary>
    /// The next loop point once the current one is reached, or once the
    /// leader stopped short of it after its order (a corner of the mesh).
    /// </summary>
    public static int NextIndex(int index, int count, float distanceToCurrent, bool stoppedAfterOrder = false) =>
        count <= 0 ? 0 : distanceToCurrent <= ArrivalRadius || stoppedAfterOrder ? (index + 1) % count : index % count;

    // ----------------------------------------------------------- live glue

    private sealed class State
    {
        public Vector3 Centre;
        public ushort Region;
        public Vector3[] Points;
        public int Index;
        public int IssuedIndex = -1;
        public long IssuedTick;
        public long LastTick = long.MinValue / 2;
    }

    private static readonly ConditionalWeakTable<GameBot, State> States = new();

    /// <summary>
    /// Runs one loop step for a holding <paramref name="leader"/>. False when
    /// the leader may not loop here or no loop point is reachable: the caller
    /// then holds exactly as before.
    /// </summary>
    public static bool TryLoop(GameBot leader, bool nearHazard = false)
    {
        if (leader?.IsAutonomousWorldBot != true || leader.IsPlayerLedGroup || !leader.IsAlive ||
            leader.CurrentRegion == null || leader.CurrentZone == null)
            return false;
        bool dungeon = leader.CurrentRegion.IsDungeon || leader.CurrentZone.IsDungeon;
        if (!MayLoop(leader.InCombat || leader.IsAttacking, NearKeep(leader), BotSiegeRuntime.Assigned(leader),
                AutonomousRealmRaid.GetView(leader.Group) != null, leader.IsStealthed, dungeon, nearHazard))
            return false;

        long now = GameLoop.GameLoopTime;
        State state = States.GetOrCreateValue(leader);
        Vector3 here = new(leader.X, leader.Y, leader.Z);
        Vector3 target;
        lock (state)
        {
            if (state.Points == null || now - state.LastTick > NewHoldGapMilliseconds ||
                state.Region != leader.CurrentRegionID)
            {
                state.Centre = here;
                state.Region = leader.CurrentRegionID;
                state.Points = BuildLoop(leader, here);
                state.Index = 0;
                state.IssuedIndex = -1;
                if (state.Points.Length > 0)
                    AutonomousMovePace.LoopStarted();
            }
            state.LastTick = now;
            if (state.Points.Length == 0)
                return false;
            bool stopped = state.IssuedIndex == state.Index && !leader.IsMoving && now - state.IssuedTick > 1_000;
            state.Index = NextIndex(state.Index, state.Points.Length,
                Vector3.Distance(here, state.Points[state.Index]), stopped);
            target = state.Points[state.Index];
            if (state.Index == state.IssuedIndex && leader.IsMoving)
            {
                AutonomousMovePace.NoteLooping(leader);
                return true;
            }
            state.IssuedIndex = state.Index;
            state.IssuedTick = now;
        }
        leader.WakeRecoveryRest();
        leader.PathTo(target, leader.MaxSpeed);
        AutonomousStuckWatchdog.MarkProgress(leader, eAutonomousProgressKind.Movement);
        AutonomousMovePace.NoteLooping(leader);
        return true;
    }

    /// <summary>The leader stands within <see cref="KeepClearance"/> of a keep or tower.</summary>
    private static bool NearKeep(GameBot leader)
    {
        ICollection<AbstractGameKeep> keeps = GameServer.KeepManager?.GetKeepsOfRegion(leader.CurrentRegionID);
        if (keeps == null)
            return false;
        Vector2 here = new(leader.X, leader.Y);
        foreach (AbstractGameKeep keep in keeps)
            if (Vector2.DistanceSquared(here, new(keep.X, keep.Y)) <= KeepClearance * KeepClearance)
                return true;
        return false;
    }

    /// <summary>Loop points on the leader's own zone and navmesh, each with a short real path from the centre.</summary>
    private static Vector3[] BuildLoop(GameBot leader, Vector3 centre)
    {
        IPathfindingMgr nav = PathfindingProvider.Instance;
        Zone zone = leader.CurrentZone;
        Region region = leader.CurrentRegion;
        if (nav == null || !nav.IsAvailable || zone == null || !nav.HasNavmesh(zone))
            return [];
        if (!AutonomousNavigationSurface.TryFloor(nav, zone, centre, out Vector3 floor))
            floor = centre;
        long key = leader.DatabaseID > 0 ? leader.DatabaseID : leader.ObjectID;
        Shape shape = ShapeFor(key, Random.Shared.NextDouble(), Random.Shared.NextDouble(), Random.Shared.NextDouble());
        var valid = new List<Vector3>(shape.Points);
        foreach (Vector3 raw in LoopPoints(floor, shape))
        {
            if (region.GetZone((int)raw.X, (int)raw.Y) != zone)
                continue;
            Vector3? point = nav.GetClosestPoint(zone, raw, 64, 64, 320, nav.DefaultFilters);
            if (point is not { } p || !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z) ||
                !AutonomousRvrTravel.HasPathWithin(nav, zone, floor, p, AutonomousRvrTravel.ViaPathDetourFactor))
                continue;
            valid.Add(p);
        }
        return valid.Count >= MinimumValidPoints ? valid.ToArray() : [];
    }
}
