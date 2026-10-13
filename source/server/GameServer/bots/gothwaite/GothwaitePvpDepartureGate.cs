using System.Collections.Generic;

namespace DOL.GS;

/// <summary>
/// One departure waiting for its turn at the castle gate (docs/GOTHWAITE_PVP_MODE.md section 6.3): a group that is due, or a ready solo. DueSince is
/// the tick a group became due (full, or past its wait with its minimum), or the tick a solo became ready. BotId is the leader's id, or the solo's.
/// </summary>
public readonly record struct GothwaitePvpDeparture(long DueSince, bool Solo, long BotId);

/// <summary>
/// The stagger of castle departures: at most one departing group or solo every <see cref="IntervalMilliseconds"/>, across the whole former, so
/// the arena does not get a wave of groups at the gate at once. The earliest due departure leaves first; on a tie a group leaves before a solo,
/// then the lower bot id. The others stay ready in the castle and keep their place. The former runs it once per tick, in its service phase.
/// </summary>
public sealed class GothwaitePvpDepartureGate
{
    public const int IntervalMilliseconds = 4_000;

    private long _nextAt;

    /// <summary>The earliest time the next departure may leave.</summary>
    public long NextAt => _nextAt;

    /// <summary>True when a departure may leave at <paramref name="now"/>.</summary>
    public bool CanRelease(long now) => now >= _nextAt;

    /// <summary>
    /// Releases the earliest due candidate when the interval has passed, and starts the next interval. Returns the candidate's index, or -1 when
    /// there is none or the interval has not passed.
    /// </summary>
    public int Release(long now, IReadOnlyList<GothwaitePvpDeparture> candidates)
    {
        if (candidates == null || candidates.Count == 0 || !CanRelease(now))
            return -1;
        int best = 0;
        for (int index = 1; index < candidates.Count; index++)
            if (Earlier(candidates[index], candidates[best]))
                best = index;
        _nextAt = now + IntervalMilliseconds;
        return best;
    }

    /// <summary>The departure order: the earlier due tick first, then a group before a solo, then the lower bot id.</summary>
    public static bool Earlier(GothwaitePvpDeparture first, GothwaitePvpDeparture second)
    {
        if (first.DueSince != second.DueSince)
            return first.DueSince < second.DueSince;
        if (first.Solo != second.Solo)
            return !first.Solo;
        return first.BotId < second.BotId;
    }
}
