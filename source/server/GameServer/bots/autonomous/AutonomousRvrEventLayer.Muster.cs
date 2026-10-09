using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// Muster-and-march state of attacking warbands (bug 75, see
/// <see cref="AutonomousRvrSiegeMuster"/>), plus the keep-route memory that
/// keeps the automatic opener off keeps its warbands could not reach.
/// </summary>
public static partial class AutonomousRvrEventLayer
{
    private sealed class ForceMuster
    {
        public long StartedTick;
        public bool Departed;
        public long DepartedTick;
        public int LastAlive, LastPresent, Assigned;
        public long LastReportTick;
        public bool SuppliesReady = true;
    }

    private static readonly DOL.Logging.Logger MusterLog = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrSiegeMuster));

    private const long KeepRouteStrikeSpacingMilliseconds = 5 * 60_000L;

    /// <summary>Consecutive route failures per keep and when the opener may try it again.</summary>
    private static readonly Dictionary<string, (int Failures, long BlockedUntil, long LastStrike)> KeepRouteBlocks = new(StringComparer.Ordinal);

    /// <summary>
    /// Only an attacking force of a started, automatic, non-defence siege
    /// musters. Solo bots, defenders, player-led and defence-response events
    /// never do. Call under <c>Sync</c>.
    /// </summary>
    private static bool MustersLocked(ActiveEvent active, string forceId) =>
        active.BattleStarted && !active.DefenseReaction && string.IsNullOrEmpty(active.PlayerAccount) &&
        (active.Attackers.ContainsKey(forceId) || active.ThirdRealm.ContainsKey(forceId));

    private static bool AnyMustering(ActiveEvent active) => active.Musters.Any(pair => !pair.Value.Departed &&
        (active.Attackers.ContainsKey(pair.Key) || active.ThirdRealm.ContainsKey(pair.Key)));

    /// <summary>The muster phase of a force in a started siege; None when it does not muster.</summary>
    public static AutonomousRvrSiegeMuster.Phase MusterPhaseOf(string forceId)
    {
        if (string.IsNullOrWhiteSpace(forceId)) return AutonomousRvrSiegeMuster.Phase.None;
        using (EnterSync())
            foreach (var active in Events.Values)
                if (MustersLocked(active, forceId))
                    return active.Musters.TryGetValue(forceId, out var muster) && muster.Departed
                        ? AutonomousRvrSiegeMuster.Phase.Marching : AutonomousRvrSiegeMuster.Phase.Mustering;
        return AutonomousRvrSiegeMuster.Phase.None;
    }

    /// <summary>
    /// The force's leader reports who is at the muster. The force departs (and
    /// its idle clocks start) once <see cref="AutonomousRvrSiegeMuster.Decide"/>
    /// says so; a force that never comes together leaves the siege with
    /// "Rally failed: the warband never mustered".
    /// </summary>
    /// <param name="alive">Living members of the force.</param>
    /// <param name="present">Living members within the muster radius of the leader.</param>
    /// <param name="skip">The force already stands near the keep and needs no muster.</param>
    public static AutonomousRvrSiegeMuster.Phase ReportMuster(string forceId, int alive, int present, int assigned,
        bool skip, long nowTick, bool suppliesReady = true)
    {
        if (string.IsNullOrWhiteSpace(forceId)) return AutonomousRvrSiegeMuster.Phase.None;
        using (EnterSync())
        {
            foreach (var active in Events.Values.ToArray())
            {
                if (!MustersLocked(active, forceId)) continue;
                if (!active.Musters.TryGetValue(forceId, out var muster))
                    active.Musters[forceId] = muster = new ForceMuster { StartedTick = nowTick };
                if (muster.Departed) return AutonomousRvrSiegeMuster.Phase.Marching;
                muster.LastAlive = alive; muster.LastPresent = present; muster.Assigned = assigned;
                muster.LastReportTick = nowTick;
                muster.SuppliesReady = suppliesReady;
                // A nearby leader alone is not a nearby warband. Never let
                // the distance shortcut send a fragmented force into guards.
                skip = skip && present >= AutonomousRvrSiegeMuster.Quorum(assigned);
                long waited = Math.Max(0, nowTick - muster.StartedTick);
                var decision = waited < AutonomousRvrSiegeMuster.PreparationWindowMilliseconds
                    ? AutonomousRvrSiegeMuster.Decision.Wait
                    : !suppliesReady
                        ? waited >= AutonomousRvrSiegeMuster.MaximumWaitMilliseconds
                            ? AutonomousRvrSiegeMuster.Decision.Fail : AutonomousRvrSiegeMuster.Decision.Wait
                        : skip ? AutonomousRvrSiegeMuster.Decision.Depart :
                            AutonomousRvrSiegeMuster.Decide(alive, present, assigned, muster.StartedTick, nowTick);
                switch (decision)
                {
                    case AutonomousRvrSiegeMuster.Decision.Depart:
                        DepartLocked(active, forceId, muster, nowTick, skip ? "already near the keep" : "mustered");
                        return AutonomousRvrSiegeMuster.Phase.Marching;
                    case AutonomousRvrSiegeMuster.Decision.Fail:
                        FailMusterLocked(active, forceId, nowTick, !suppliesReady && present >= Math.Max(2, assigned / 2)
                            ? "Siege supply trip did not finish before departure" : "Rally failed: the warband never mustered");
                        return AutonomousRvrSiegeMuster.Phase.None;
                    default:
                        return AutonomousRvrSiegeMuster.Phase.Mustering;
                }
            }
        }
        return AutonomousRvrSiegeMuster.Phase.None;
    }

    /// <summary>Snapshot for the dashboard and logs: who is mustering at this siege.</summary>
    public static (int Mustering, int Marching) MusterCounts(string targetId)
    {
        using (EnterSync())
            return Events.TryGetValue(targetId, out var active)
                ? (active.Musters.Values.Count(muster => !muster.Departed), active.Musters.Values.Count(muster => muster.Departed))
                : (0, 0);
    }

    private static void DepartLocked(ActiveEvent active, string forceId, ForceMuster muster, long nowTick, string how)
    {
        muster.Departed = true;
        muster.DepartedTick = nowTick;
        // The idle (15 min) and absence (45 min) clocks count from the march,
        // not from the opening, which the muster may have outlasted.
        active.LastActivityTick = AutonomousRvrSiegeMuster.ClockOrigin(active.LastActivityTick, nowTick);
        active.LastAttackerPresenceTick = AutonomousRvrSiegeMuster.ClockOrigin(active.LastAttackerPresenceTick, nowTick);
        RealmEventRecords.Progress(active.TargetId, "Battle",
            $"A warband mustered ({muster.LastPresent} of {muster.LastAlive} living members) and marched on the keep together",
            active.Attackers.Values.Sum() + active.Defenders.Values.Sum() + active.ThirdRealm.Values.Sum(), muster.LastPresent);
        if (MusterLog.IsInfoEnabled) MusterLog.Info($"RVR_SIEGE_MUSTER_DEPARTED target={active.TargetId} force={forceId} " +
            $"how=\"{how}\" present={muster.LastPresent}/{muster.LastAlive}/{muster.Assigned} waitedMs={nowTick - muster.StartedTick}");
    }

    private static void FailMusterLocked(ActiveEvent active, string forceId, long nowTick,
        string reason = "Rally failed: the warband never mustered")
    {
        // A force that already left this siege (reassigned, abandoned) has nothing to fail:
        // drop the stale muster without releasing it from whatever it does now.
        if (!active.Attackers.ContainsKey(forceId) && !active.ThirdRealm.ContainsKey(forceId))
        {
            active.Musters.Remove(forceId);
            return;
        }
        var muster = active.Musters.GetValueOrDefault(forceId);
        if (MusterLog.IsInfoEnabled) MusterLog.Info($"RVR_SIEGE_MUSTER_FAILED target={active.TargetId} force={forceId} " +
            $"present={muster?.LastPresent}/{muster?.LastAlive}/{muster?.Assigned} suppliesReady={muster?.SuppliesReady} " +
            $"reportAgeMs={nowTick - (muster?.LastReportTick ?? nowTick)} waitedMs={nowTick - (muster?.StartedTick ?? nowTick)} reason=\"{reason}\"");
        active.Attackers.Remove(forceId);
        active.ThirdRealm.Remove(forceId);
        active.Slots.Remove(forceId);
        active.Musters.Remove(forceId);
        // This force may not come back to this keep for the usual twenty minutes.
        AbandonedTargets[(forceId, active.TargetId)] = nowTick + AbandonedTargetMilliseconds;
        ReleasedForces[forceId] = reason;
        if (active.Attackers.Count == 0 && active.ThirdRealm.Count == 0)
            EndEvent(active, nowTick, reason);
    }

    /// <summary>
    /// Expire's part of the muster: a force whose leader stopped reporting (dead,
    /// gone) is judged once the maximum wait and a grace period have passed.
    /// Also stops the idle clocks while an attacking force is still mustering.
    /// </summary>
    private static void ExpireMusters(ActiveEvent active, long nowTick)
    {
        foreach (var pair in active.Musters.Where(pair => !pair.Value.Departed).ToArray())
        {
            if (nowTick - pair.Value.StartedTick < AutonomousRvrSiegeMuster.MaximumWaitMilliseconds +
                    AutonomousRvrSiegeMuster.UnreportedGraceMilliseconds) continue;
            if (!Events.ContainsKey(active.TargetId)) return;
            FailMusterLocked(active, pair.Key, nowTick);
        }
    }

    // ---- Keep-route memory ---------------------------------------------

    /// <summary>
    /// A member's repeated "no connected exterior route" (three failures) shows
    /// the keep cannot be reached from where forces start. Keep 51 alone failed
    /// 386 times in a week. The automatic opener skips it for an hour, doubling
    /// with each consecutive failure up to eight hours; any attacker reaching
    /// the walls clears it.
    /// </summary>
    public static void NoteKeepRouteFailure(string targetId, long nowTick)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return;
        using (EnterSync())
        {
            KeepRouteBlocks.TryGetValue(targetId, out var previous);
            // Several members of one force report the same failure within moments: one strike.
            if (previous.Failures > 0 && nowTick - previous.LastStrike < KeepRouteStrikeSpacingMilliseconds) return;
            int failures = previous.Failures > 0 && nowTick - previous.BlockedUntil < AutonomousRvrSiegeMuster.KeepRouteBlockMaximumMilliseconds
                ? previous.Failures + 1 : 1;
            KeepRouteBlocks[targetId] = (failures, nowTick + AutonomousRvrSiegeMuster.RouteBlockMilliseconds(failures), nowTick);
        }
    }

    public static bool IsKeepRouteBlocked(string targetId, long nowTick)
    {
        using (EnterSync()) return KeepRouteBlockedLocked(targetId, nowTick);
    }

    private static bool KeepRouteBlockedLocked(string targetId, long nowTick) =>
        targetId != null && KeepRouteBlocks.Count > 0 && KeepRouteBlocks.TryGetValue(targetId, out var block) && block.BlockedUntil > nowTick;

    private static void ClearKeepRouteBlockLocked(string targetId)
    {
        if (KeepRouteBlocks.Count > 0) KeepRouteBlocks.Remove(targetId);
    }
}
