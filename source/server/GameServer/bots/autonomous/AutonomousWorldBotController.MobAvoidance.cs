using System;
using System.Numerics;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    private const int MobBypassScanMilliseconds = 3_000;
    private const int MobBypassLogMilliseconds = 60_000;
    private Vector3? _mobBypass;
    private Vector3 _mobBypassLeg;
    private long _nextMobBypassScan;
    private long _nextMobBypassLog;
    private long _mobBypassStarted;

    /// <summary>A failed or expired bend: forget it and let the ordinary road
    /// order (with its own failure handling) run for a while.</summary>
    private void DropMobBypass()
    {
        _mobBypass = null;
        _nextMobBypassScan = GameLoop.GameLoopTime + AutonomousRvrMobAvoidance.BendSuppressMilliseconds;
    }

    /// <summary>
    /// The point an RvR bot walks to next instead of <paramref name="leg"/>
    /// when a named/far-above monster or a dense camp stands on the next
    /// stretch: one bounded, navmesh-checked bend around it. Returns
    /// <paramref name="leg"/> unchanged when nothing blocks or no bend is
    /// walkable, so the route never stalls. Followers bend with their leader.
    /// </summary>
    private Vector3 AvoidDangerousMobs(GameBot bot, Vector3 leg)
    {
        if (bot?.CurrentZone == null || bot.CurrentZone.IsDungeon ||
            !AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR))
            return leg;
        Vector3 here = new(bot.X, bot.Y, bot.Z);
        long now = GameLoop.GameLoopTime;
        if (_mobBypass is Vector3 bypass)
        {
            bool sameLeg = Vector2.DistanceSquared(new(_mobBypassLeg.X, _mobBypassLeg.Y), new(leg.X, leg.Y)) <= 500 * 500;
            bool reached = Vector2.DistanceSquared(new(here.X, here.Y), new(bypass.X, bypass.Y)) <= 200 * 200;
            if (AutonomousRvrMobAvoidance.KeepBend(sameLeg, reached, _mobBypassStarted, now))
                return bypass;
            if (reached)
            {
                _mobBypass = null;
                _nextMobBypassScan = 0; // look again from the bend
            }
            else
            {
                DropMobBypass(); // leg changed or the bend took too long
                return leg;
            }
        }
        if (now < _nextMobBypassScan)
            return leg;
        _nextMobBypassScan = now + MobBypassScanMilliseconds + bot.ObjectID % 500;

        var mobs = AutonomousRvrMobAvoidance.ScanMobs(bot);
        if (mobs.Length == 0)
            return leg;
        var dangers = AutonomousRvrMobAvoidance.BuildDangers(bot.Level, mobs);
        if (dangers.Count == 0)
            return leg;
        var nav = PathfindingProvider.Instance;
        if (!nav.IsAvailable || !nav.HasNavmesh(bot.CurrentZone))
            return leg;
        Zone legZone = bot.CurrentRegion?.GetZone((int)leg.X, (int)leg.Y);
        foreach (Vector2 candidate in AutonomousRvrMobAvoidance.BypassCandidates(new(here.X, here.Y), new(leg.X, leg.Y), dangers))
        {
            if (bot.CurrentRegion.GetZone((int)candidate.X, (int)candidate.Y) != bot.CurrentZone)
                continue;
            Vector3? floor = nav.GetClosestPoint(bot.CurrentZone, new(candidate.X, candidate.Y, here.Z), 96, 96, 768, nav.DefaultFilters);
            if (!floor.HasValue || Vector2.DistanceSquared(new(floor.Value.X, floor.Value.Y), candidate) > 150 * 150 ||
                !AutonomousZoneItinerary.HasCompleteCorridor(nav, bot.CurrentZone, here, floor.Value) ||
                legZone == bot.CurrentZone && !AutonomousZoneItinerary.HasCompleteCorridor(nav, bot.CurrentZone, floor.Value, leg))
                continue;
            _mobBypass = floor.Value;
            _mobBypassLeg = leg;
            _mobBypassStarted = now;
            if (now >= _nextMobBypassLog && Log.IsInfoEnabled)
            {
                _nextMobBypassLog = now + MobBypassLogMilliseconds;
                var nearest = dangers[0];
                foreach (var danger in dangers)
                    if (Vector2.DistanceSquared(danger.Center, candidate) < Vector2.DistanceSquared(nearest.Center, candidate))
                        nearest = danger;
                Log.Info($"RVR_MOB_BYPASS bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} " +
                    $"mob=\"{nearest.Name}\" mob_level={nearest.Level} radius={(int)nearest.Radius} region={bot.CurrentRegionID} " +
                    $"zone=\"{bot.CurrentZone.Description}\" position={bot.X},{bot.Y},{bot.Z} bend={(int)candidate.X},{(int)candidate.Y}");
            }
            return floor.Value;
        }
        return leg;
    }
}
