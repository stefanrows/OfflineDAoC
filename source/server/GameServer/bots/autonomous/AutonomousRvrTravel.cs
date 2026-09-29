using System;
using System.Numerics;
using System.Linq;
using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>Route variation is a connected waypoint, not steering noise.</summary>
public static class AutonomousRvrTravel
{
    /// <summary>A keep door this bot may use: any keep the runtime door rule
    /// (<see cref="GameKeepDoor.TryTraverse"/>) would let it through, or a
    /// realm door that is not part of a keep.</summary>
    public static bool IsFriendlyDoor(GameBot bot, GameDoorBase door)
    {
        if (door is GameKeepDoor keepDoor && keepDoor.Component?.Keep is { } keep)
            return CanPassKeep(bot, keep);
        return door != null && bot != null && door.Realm == bot.Realm;
    }

    /// <summary>
    /// The one keep-door hostility rule shared by the route planner, the mover
    /// and the actual door traversal: <c>!KeepManager.IsEnemy(keep, bot)</c>.
    /// Under Camlann that makes the Realm=0 portal keeps passable for everyone
    /// and guild keeps passable for their own guild, group and alliance only.
    /// Without a configured server (unit tests, tools) it falls back to
    /// realm identity, the rule IsEnemy itself uses outside PvP.
    /// </summary>
    public static bool CanPassKeep(GameBot bot, AbstractGameKeep keep, IKeepManager manager = null)
    {
        if (bot == null || keep == null) return false;
        manager ??= GameServer.Instance?.Configuration != null ? GameServer.KeepManager : null;
        return manager != null ? !manager.IsEnemy(keep, bot) : bot.Realm != eRealm.None && keep.Realm == bot.Realm;
    }

    public static bool TraverseFriendlyDoor(GameBot bot, Vector3 destination)
    {
        if (!AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR) ||
            bot.TempProperties.GetProperty<long>("RvrDoorPassUntil") > GameLoop.GameLoopTime) return false;
        foreach (GameKeepDoor door in GameServer.KeepManager.GetKeepsOfRegion(bot.CurrentRegionID)
                     .Where(keep => CanPassKeep(bot, keep)).SelectMany(keep => keep.Doors.Values))
        {
            if (!bot.IsWithinRadius(door, WorldMgr.INTERACT_DISTANCE) || Math.Abs(door.Z - bot.Z) > 160) continue;
            Vector2 toDoor = new(door.X - bot.X, door.Y - bot.Y);
            Vector2 toGoal = new(destination.X - bot.X, destination.Y - bot.Y);
            if (toGoal.LengthSquared() < toDoor.LengthSquared() || Vector2.Dot(toDoor, toGoal) <= 0) continue;
            // Only use a doorway lying along the current travel leg, never
            // hop sideways through an unrelated nearby wall/door.
            float cross = Math.Abs(toDoor.X * toGoal.Y - toDoor.Y * toGoal.X) / Math.Max(1, toGoal.Length());
            if (cross > 100 || !door.TryTraverse(bot)) continue;
            bot.TempProperties.SetProperty("RvrDoorPassUntil", GameLoop.GameLoopTime + 2000);
            return true;
        }
        return false;
    }
    private static readonly int[] BorderDoors =
        [11020501, 11020502, 12000101, 12000102, 102093501, 102093502,
         111161301, 111161302, 206016801, 206016802, 207156901, 207156902];

    public static bool OpenNearbyBorderDoors(GameBot bot, Vector3 destination)
    {
        if (bot?.IsAutonomousWorldBot != true ||
            !AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR)) return false;
        bool opened = false;
        foreach (int id in BorderDoors)
        {
            GameDoorBase door = DoorMgr.GetDoorByID(id);
            if (door == null || door.CurrentRegion != bot.CurrentRegion || door.Locked ||
                door.State != eDoorState.Closed || door.Realm != bot.Realm && door.Realm != eRealm.Door ||
                !bot.IsWithinRadius(door, ServerProperties.Properties.WORLD_PICKUP_DISTANCE * 3)) continue;
            Vector2 heading = new(destination.X - bot.X, destination.Y - bot.Y);
            Vector2 toDoor = new(door.X - bot.X, door.Y - bot.Y);
            if (Vector2.Dot(heading, toDoor) < 0 || Math.Abs(door.Z - bot.Z) > 400) continue;
            // This is the same authoritative operation sent by the player's
            // lever/door request. The native five-second closure remains intact.
            door.Open(bot);
            opened |= door.State == eDoorState.Open;
        }
        return opened;
    }

    /// <summary>
    /// The next waypoint of a roaming leg for <paramref name="variant"/> (see
    /// <see cref="AutonomousRvrRoutePolicy.ChooseRoute"/>), checked against the
    /// bot's own zone and navmesh. Without navigation or on a short leg the
    /// destination itself is returned.
    /// </summary>
    public static RvrRouteChoice ChooseRoute(GameBot bot, Vector3 destination, RvrRouteVariant variant,
        Random random = null, Vector3? heat = null, Vector3? hubCentre = null,
        float hubSafeRadius = AutonomousRvrRoutePolicy.KeepSafeRadius)
    {
        Vector3 start = new(bot.X, bot.Y, bot.Z);
        return AutonomousRvrRoutePolicy.ChooseRoute(start, destination, variant, Probe(bot, destination),
            random ?? Random.Shared, heat, hubCentre, hubSafeRadius);
    }

    /// <summary>The bot's own zone and navmesh as route checks; null without navigation.</summary>
    private static RvrRouteProbe Probe(GameBot bot, Vector3 destination)
    {
        Zone zone = bot.CurrentZone;
        var nav = PathfindingProvider.Instance;
        return zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone) ? null : new(
            (raw, range) =>
            {
                if (range <= 128)
                    return AutonomousNavigationSurface.TryFloor(nav, zone, raw, out Vector3 floor) ? floor : null;
                Vector3? wide = nav.GetClosestPoint(zone, raw, 64, 64, range, nav.DefaultFilters);
                return wide is { } w && float.IsFinite(w.X) && float.IsFinite(w.Y) && float.IsFinite(w.Z) ? w : null;
            },
            raw => bot.CurrentRegion.GetZone((int)raw.X, (int)raw.Y) == zone,
            (from, to) => AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, from, to),
            bot.CurrentRegion.GetZone((int)destination.X, (int)destination.Y) == zone);
    }

    /// <summary>
    /// The first candidate rest spot that has a walkable floor in the bot's
    /// zone (hills allowed, like a Flank via-point) and a complete path from
    /// the bot, or null: the group then rests where it stands.
    /// </summary>
    public static Vector3? FirstReachableOffRoad(GameBot bot, Vector3[] candidates)
    {
        Vector3 start = new(bot.X, bot.Y, bot.Z);
        RvrRouteProbe probe = Probe(bot, start);
        if (probe == null || candidates == null)
            return null;
        foreach (Vector3 raw in candidates)
        {
            if (!probe.SameZone(raw))
                continue;
            Vector3? floor = probe.Floor(raw, AutonomousRvrRoutePolicy.OffRoadFloorRange);
            if (floor.HasValue && Math.Abs(floor.Value.Z - raw.Z) <= AutonomousRvrRoutePolicy.OffRoadFloorRange &&
                probe.Corridor(start, floor.Value))
                return floor.Value;
        }
        return null;
    }

    /// <summary>
    /// Whether the bot stands in its own realm's safe border hub: the keep
    /// circle or one of the hub's outer bindstone landings, with that circle.
    /// </summary>
    public static bool IsInOwnSafeHub(GameLiving living, out AutonomousHubDeparture.SafeAnchor anchor)
    {
        anchor = default;
        return living != null && IsInOwnSafeHub(living.Realm, living.CurrentRegionID, living.X, living.Y, out anchor);
    }

    public static bool IsInOwnSafeHub(eRealm realm, ushort regionId, int x, int y, out AutonomousHubDeparture.SafeAnchor anchor) =>
        AutonomousHubDeparture.TryGetSafeAnchor(realm, regionId, x, y, out anchor);
}
