using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// Where a guild (or a guildless group) recently lost people in RvR (C2,
/// principles P5 and P7). A 1.65 group that got wiped at a bridge stayed away
/// from it for about an hour ("refuse to leave the pk for a good hour"), and
/// an aggressive caller came back "with twice the numbers". Cells are 1,500
/// units wide; an entry fades linearly to nothing over 60 minutes. In memory
/// only: a restart forgets, like the fight heat.
/// </summary>
public static class AutonomousRvrDangerMemory
{
    public const float CellSize = 1_500;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(60);
    public const int MaximumCellsPerKey = 24;
    /// <summary>A retreat marks a place as half as bad as a death.</summary>
    public const double RetreatWeight = 0.5;
    public static readonly TimeSpan LogThrottle = TimeSpan.FromMinutes(1);

    public readonly record struct CellKey(ushort RegionId, int X, int Y);

    /// <summary>A remembered cell as seen at one moment.</summary>
    public readonly record struct Danger(CellKey Cell, int Deaths, int Retreats, int GroupSize, DateTime LastUtc,
        double Intensity, Vector3 Centre);

    private sealed class Entry
    {
        public CellKey Cell;
        public int Deaths;
        public int Retreats;
        public int GroupSize;
        public DateTime LastUtc;
        public DateTime LastLoggedUtc;
        public float Z;
    }

    private static readonly object Sync = new();
    private static readonly Dictionary<string, List<Entry>> Keys = new();

    // ------------------------------------------------------------ pure rules

    public static CellKey CellOf(ushort regionId, float x, float y) =>
        new(regionId, (int)Math.Floor(x / CellSize), (int)Math.Floor(y / CellSize));

    /// <summary>1 when fresh, falling linearly to 0 at 60 minutes.</summary>
    public static double Freshness(DateTime lastUtc, DateTime nowUtc) =>
        Math.Clamp(1 - (nowUtc - lastUtc).TotalMilliseconds / Lifetime.TotalMilliseconds, 0, 1);

    /// <summary>Losses at a cell, faded by age: deaths count 1, retreats 0.5.</summary>
    public static double Intensity(int deaths, int retreats, DateTime lastUtc, DateTime nowUtc) =>
        (deaths + retreats * RetreatWeight) * Freshness(lastUtc, nowUtc);

    public static bool IsBold(RvrLeaderTraits traits) => traits.Aggression > 65 && traits.RiskTolerance >= 45;

    public static bool IsCautious(RvrLeaderTraits traits, double retreatBias) =>
        traits.RiskTolerance < 45 || retreatBias >= 0.25;

    /// <summary>
    /// How much a roaming leader still wants a place where its crew lost
    /// people. 1 when nothing is remembered. A cautious leader (RiskTolerance
    /// below 45 or doctrine RetreatBias 0.25+) avoids it hard, 0.5 after one
    /// loss down to 0.2 after four; a bold one (Aggression above 65) goes back
    /// only bigger: 1.3 when its group is at least one larger than the group
    /// that died there, else 0.7; everyone else 0.6. The effect fades with the
    /// memory (full at intensity 1 and above).
    /// </summary>
    public static double DangerFactor(RvrLeaderTraits traits, double retreatBias, int ownGroupSize,
        int lostGroupSize, double intensity)
    {
        if (intensity <= 0)
            return 1;
        double full = IsBold(traits) ? ownGroupSize >= lostGroupSize + 1 ? 1.3 : 0.7 :
            IsCautious(traits, retreatBias) ? Math.Clamp(0.6 - 0.1 * intensity, 0.2, 0.5) :
            0.6;
        return 1 + (full - 1) * Math.Min(1, intensity);
    }

    /// <summary>A revenge trip starts only with at least two thirds of the
    /// group size that was lost (no remembered loss: no gate).</summary>
    public static bool AllowsRevenge(int ownGroupSize, int lostGroupSize) =>
        lostGroupSize <= 0 || ownGroupSize * 3 >= lostGroupSize * 2;

    /// <summary>
    /// Which point a Cover route keeps to the other side of: the newest heat
    /// spot, unless a remembered danger cell is fresher than it.
    /// <paramref name="heat"/> is the heat spot's 0-1 heat value (1 = now,
    /// 0 = 12 minutes old).
    /// </summary>
    public static Vector3? CoverThreat(Vector3? heatPosition, double heat, Vector3? dangerPosition, TimeSpan dangerAge)
    {
        if (!heatPosition.HasValue)
            return dangerPosition;
        if (!dangerPosition.HasValue)
            return heatPosition;
        TimeSpan heatAge = TimeSpan.FromMinutes((1 - Math.Clamp(heat, 0, 1)) * 12);
        return dangerAge < heatAge ? dangerPosition : heatPosition;
    }

    // --------------------------------------------------------------- storage

    /// <summary>
    /// The crew that remembers: the group's leader (the bot itself when solo).
    /// Deaths and reads use the same leader, so a guildless member of a guild
    /// group or a mixed pickup group feeds the memory its leader reads.
    /// </summary>
    public static GameBot MemoryOwner(GameBot bot) => bot?.Group?.LivingLeader as GameBot ?? bot;

    /// <summary>Memory key: the leader's guild, else the leader itself (stable
    /// across a re-formed group, unlike the group object).</summary>
    public static string KeyFor(GameBot bot)
    {
        GameBot owner = MemoryOwner(bot);
        return owner == null ? null :
            owner.Guild != null ? $"guild:{owner.Guild.GuildID}" :
            $"leader:{(owner.DatabaseID > 0 ? owner.DatabaseID : owner.ObjectID)}";
    }

    /// <summary>Stores one loss. Returns the cell as it stands now and whether
    /// its log line is due (at most once a minute per cell).</summary>
    public static Danger Record(string key, ushort regionId, Vector3 position, int groupSize, bool death,
        DateTime nowUtc, out bool log)
    {
        log = false;
        if (key == null)
            return default;
        CellKey cell = CellOf(regionId, position.X, position.Y);
        lock (Sync)
        {
            foreach (string stale in Keys.Where(pair =>
                         { pair.Value.RemoveAll(entry => Freshness(entry.LastUtc, nowUtc) <= 0); return pair.Value.Count == 0; })
                         .Select(pair => pair.Key).ToArray())
                Keys.Remove(stale);
            if (!Keys.TryGetValue(key, out List<Entry> entries))
                Keys[key] = entries = [];
            Entry entry = entries.FirstOrDefault(candidate => candidate.Cell == cell);
            if (entry == null)
            {
                // Stamped before the cap check, so the new place is never the "oldest".
                entries.Add(entry = new Entry { Cell = cell, LastUtc = nowUtc });
                if (entries.Count > MaximumCellsPerKey)
                    entries.Remove(entries.OrderBy(candidate => candidate.LastUtc).First());
            }
            if (death) entry.Deaths++;
            else entry.Retreats++;
            entry.GroupSize = Math.Max(entry.GroupSize, Math.Max(1, groupSize));
            entry.LastUtc = nowUtc;
            entry.Z = position.Z;
            if (nowUtc - entry.LastLoggedUtc >= LogThrottle)
            {
                entry.LastLoggedUtc = nowUtc;
                log = true;
            }
            return View(entry, nowUtc);
        }
    }

    /// <summary>The remembered cell containing the point, if any is still fresh.</summary>
    public static Danger? At(string key, ushort regionId, float x, float y, DateTime nowUtc)
    {
        if (key == null)
            return null;
        CellKey cell = CellOf(regionId, x, y);
        lock (Sync)
        {
            if (!Keys.TryGetValue(key, out List<Entry> entries))
                return null;
            Entry entry = entries.FirstOrDefault(candidate => candidate.Cell == cell);
            return entry == null || Freshness(entry.LastUtc, nowUtc) <= 0 ? null : View(entry, nowUtc);
        }
    }

    /// <summary>The worst fresh cell within <paramref name="range"/> of a point.</summary>
    public static Danger? Worst(string key, ushort regionId, Vector2 near, float range, DateTime nowUtc)
    {
        if (key == null)
            return null;
        lock (Sync)
        {
            if (!Keys.TryGetValue(key, out List<Entry> entries))
                return null;
            Danger? worst = null;
            foreach (Entry entry in entries)
            {
                if (entry.Cell.RegionId != regionId)
                    continue;
                Danger view = View(entry, nowUtc);
                if (view.Intensity <= 0 || Vector2.Distance(near, new(view.Centre.X, view.Centre.Y)) > range)
                    continue;
                if (worst == null || view.Intensity > worst.Value.Intensity)
                    worst = view;
            }
            return worst;
        }
    }

    /// <summary>The biggest group this key lost people from in the last hour (0 = none).</summary>
    public static int LostGroupSize(string key, DateTime nowUtc)
    {
        if (key == null)
            return 0;
        lock (Sync)
            return Keys.TryGetValue(key, out List<Entry> entries)
                ? entries.Where(entry => entry.Deaths > 0 && Freshness(entry.LastUtc, nowUtc) > 0)
                    .Select(entry => entry.GroupSize).DefaultIfEmpty(0).Max()
                : 0;
    }

    private static Danger View(Entry entry, DateTime nowUtc) => new(entry.Cell, entry.Deaths, entry.Retreats,
        entry.GroupSize, entry.LastUtc, Intensity(entry.Deaths, entry.Retreats, entry.LastUtc, nowUtc),
        new((entry.Cell.X + 0.5f) * CellSize, (entry.Cell.Y + 0.5f) * CellSize, entry.Z));

    internal static void ResetForTests()
    {
        lock (Sync)
            Keys.Clear();
    }

    // ------------------------------------------------------------ live hooks

    private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrDangerMemory));

    /// <summary>An autonomous RvR bot died to a player-shaped enemy.</summary>
    public static void RecordDeath(GameBot victim, DateTime nowUtc)
    {
        if (victim?.CurrentRegion == null || !victim.IsAutonomousWorldBot || victim.IsTemporaryGroupHelper ||
            victim.IsPlayerLedGroup || !AutonomousObjectiveAssignments.Is(victim, eAutonomousObjectiveKind.RvR))
            return;
        RecordFor(victim, new(victim.X, victim.Y, victim.Z), true, nowUtc);
    }

    /// <summary>A group leader called a retreat at <paramref name="position"/>.</summary>
    public static void RecordRetreat(GameBot leader, Vector3 position, DateTime nowUtc)
    {
        if (leader?.CurrentRegion != null)
            RecordFor(leader, position, false, nowUtc);
    }

    private static void RecordFor(GameBot bot, Vector3 position, bool death, DateTime nowUtc)
    {
        int groupSize = bot.Group?.MemberCount ?? 1;
        Danger danger = Record(KeyFor(bot), bot.CurrentRegionID, position, groupSize, death, nowUtc, out bool log);
        if (log && Log.IsInfoEnabled)
            Log.Info($"RVR_DANGER_RECORD guild={MemoryOwner(bot).Guild?.GuildID ?? "none"} " +
                $"cell={danger.Cell.RegionId}:{danger.Cell.X},{danger.Cell.Y} deaths={danger.Deaths} " +
                $"group_size={danger.GroupSize} retreats={danger.Retreats} source={(death ? "death" : "retreat")}");
    }

    /// <summary>The danger factor for a roaming pick at a point, for this leader.</summary>
    public static double FactorFor(GameBot planner, ushort regionId, float x, float y, DateTime nowUtc)
    {
        Danger? danger = At(KeyFor(planner), regionId, x, y, nowUtc);
        if (danger == null)
            return 1;
        GameBot leader = MemoryOwner(planner);
        RvrLeaderTraits traits = leader.PersistentRecord is { } record
            ? new(record.Aggression, record.RiskTolerance, record.Patience)
            : RvrLeaderTraits.Neutral;
        double retreatBias = AutonomousRvrDoctrineRuntime.For(planner)?.RetreatBias ?? 0;
        return DangerFactor(traits, retreatBias, LivingGroupSize(planner), danger.Value.GroupSize, danger.Value.Intensity);
    }

    public static int LivingGroupSize(GameBot bot) =>
        bot.Group?.GetMembersInTheGroup().Count(member => member.IsAlive) ?? 1;
}
