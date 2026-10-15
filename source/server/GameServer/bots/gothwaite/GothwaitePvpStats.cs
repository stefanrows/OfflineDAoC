using System;
using System.Collections.Generic;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.GS;

/// <summary>The kind of fighter a stats line names (the log's killer_kind and victim_kind).</summary>
public enum GothwaitePvpKind { Other, ModeBot, Player, Companion }

/// <summary>One fighter of a recorded fight, as the log and the level bands see it.</summary>
public readonly record struct GothwaitePvpFighter(string Name, int Level, GothwaitePvpKind Kind);

/// <summary>
/// One fight event in the arena (docs/GOTHWAITE_PVP_MODE.md section 8, stage 3): a PvP kill, or the death of a mode bot.
/// <see cref="PlayerKill"/> is true when the killer is player-shaped (a player, a companion, or a pet resolved to its owner).
/// </summary>
public readonly record struct GothwaitePvpFight(GothwaitePvpFighter Killer, GothwaitePvpFighter Victim, bool PlayerKill,
    int X, int Y, double CastleDistance);

/// <summary>Kills and deaths of one level band (1-9, 10-19, 20-29, 30-39, 40-50).</summary>
public readonly record struct GothwaitePvpBandCount(string Label, int Kills, int Deaths);

/// <summary>Kills counted by the killer's con colour of its victim (GREY to PURPLE).</summary>
public readonly record struct GothwaitePvpConCount(ConColor Color, int Kills);

/// <summary>
/// Stage 6: how often the player tactics acted since the last reset: a tank took the lead of a departing group, a leader or solo
/// refused an opening because the enemy party was too big, broke off a fight, rested and gathered after one, walked to a fight it
/// heard, or opened from stealth.
/// </summary>
public readonly record struct GothwaitePvpTacticsCount(int MainAssists, int RefusedOpenings, int Retreats, int Regroups, int Adds, int Ambushes);

/// <summary>
/// A read-only copy of the mode's fight statistics for the stage 4 status command. The window runs from the last reset
/// (server start) to <see cref="AsOfGameTime"/>; heat looks back <see cref="HeatWindowMilliseconds"/>.
/// </summary>
public sealed record GothwaitePvpStatsSnapshot(long WindowStartGameTime, long AsOfGameTime, int HeatWindowMilliseconds,
    int TotalKills, int TotalDeaths, IReadOnlyList<GothwaitePvpBandCount> Bands, IReadOnlyList<GothwaitePvpConCount> KillsByCon)
{
    /// <summary>
    /// Stage 4: the kills made by a mode bot (the killer side is a mode bot, a pet counted as its owner). Players and companions
    /// are not bound by the con rule, so their kills are left out. <see cref="TotalKills"/> counts every player-shaped killer.
    /// </summary>
    public int ModeKills { get; init; }
    /// <summary>Stage 4: the deaths of a mode bot. <see cref="TotalDeaths"/> also counts the players a mode bot killed.</summary>
    public int ModeDeaths { get; init; }
    /// <summary>Stage 4: per band, the mode bot kills (by the killer's band) and the mode bot deaths (by the victim's band).</summary>
    public IReadOnlyList<GothwaitePvpBandCount> ModeBands { get; init; } = Array.Empty<GothwaitePvpBandCount>();
    /// <summary>Stage 4: the mode bot kills by the killer's con colour of its victim (self-defence included), GREY to PURPLE.</summary>
    public IReadOnlyList<GothwaitePvpConCount> ModeKillsByCon { get; init; } = Array.Empty<GothwaitePvpConCount>();
    /// <summary>Stage 6: the tactics counters.</summary>
    public GothwaitePvpTacticsCount Tactics { get; init; }
}

/// <summary>
/// Mode fight statistics (docs/GOTHWAITE_PVP_MODE.md sections 6.4 and 8): kills and deaths per level band and per con
/// colour, and recent fight positions ("heat") that the roaming leader prefers. A fight is a PvP kill in the arena, or the
/// death of a mode bot there. Each counted fight writes one GOTH_PVP_KILL line. Kills come from many threads, so all state
/// sits behind one lock; the log line is written outside it. Nothing counts while the mode is off.
/// </summary>
public static class GothwaitePvpStats
{
    /// <summary>The ring buffer keeps this many recent fights; the oldest is overwritten first.</summary>
    public const int RingCapacity = 512;
    /// <summary>A fight counts toward the heat of a point within this distance (2-D, game units).</summary>
    public const int HeatRadius = 2_500;
    /// <summary>A fight's heat weight falls linearly from 1 (just now) to 0 over this long (game time).</summary>
    public const int HeatWindowMilliseconds = 600_000;

    private const int BandCount = 5;
    private const int ConColourCount = 7;
    private static readonly string[] BandLabels = { "1-9", "10-19", "20-29", "30-39", "40-50" };

    private static readonly Logger Log = LoggerManager.Create(typeof(GothwaitePvpStats));
    private static readonly object Sync = new();
    private static readonly int[] KillsByBand = new int[BandCount];
    private static readonly int[] DeathsByBand = new int[BandCount];
    private static readonly int[] KillsByColour = new int[ConColourCount];
    private static readonly Position[] Ring = new Position[RingCapacity];
    private static int _totalKills;
    private static int _totalDeaths;
    private static int _ringNext;
    private static int _ringCount;
    private static long _windowStart;
    // Stage 4: the same counts for the mode bots alone (kills by a mode bot, deaths of a mode bot), under the same lock.
    private static readonly int[] ModeKillsByBand = new int[BandCount];
    private static readonly int[] ModeDeathsByBand = new int[BandCount];
    private static readonly int[] ModeKillsByColour = new int[ConColourCount];
    private static int _modeKills;
    private static int _modeDeaths;
    // Stage 6 tactics counters: bumped from many bot turns, so they are Interlocked rather than locked.
    private static int _mainAssists, _refusedOpenings, _retreats, _regroups, _adds, _ambushes;

    private readonly record struct Position(int X, int Y, long Time);

    public static void CountMainAssist() => System.Threading.Interlocked.Increment(ref _mainAssists);
    public static void CountRefusedOpening() => System.Threading.Interlocked.Increment(ref _refusedOpenings);
    public static void CountRetreat() => System.Threading.Interlocked.Increment(ref _retreats);
    public static void CountRegroup() => System.Threading.Interlocked.Increment(ref _regroups);
    public static void CountAdd() => System.Threading.Interlocked.Increment(ref _adds);
    public static void CountAmbush() => System.Threading.Interlocked.Increment(ref _ambushes);

    /// <summary>The stage 6 tactics counters now.</summary>
    public static GothwaitePvpTacticsCount Tactics => new(_mainAssists, _refusedOpenings, _retreats, _regroups, _adds, _ambushes);

    /// <summary>
    /// The most recent fight (a kill) within <paramref name="radius"/> of a point, at least <paramref name="minDistance"/> away (the group's own
    /// kills are closer), and no older than <paramref name="maxAge"/>, for the stage 6 add draw. False when there is none. The ring is read
    /// under the lock, like the heat.
    /// </summary>
    public static bool TryRecentFightNear(int x, int y, int radius, int maxAge, long now, out int fightX, out int fightY, int minDistance = 0)
    {
        fightX = 0;
        fightY = 0;
        long newest = long.MinValue;
        lock (Sync)
        {
            for (int index = 0; index < _ringCount; index++)
            {
                Position fight = Ring[index];
                if (now - fight.Time > maxAge || fight.Time <= newest)
                    continue;
                double dx = fight.X - x, dy = fight.Y - y;
                double squared = dx * dx + dy * dy;
                if (squared > (double)radius * radius || squared < (double)minDistance * minDistance)
                    continue;
                newest = fight.Time;
                fightX = fight.X;
                fightY = fight.Y;
            }
        }
        return newest != long.MinValue;
    }

    /// <summary>
    /// Records one fight from the server's death hooks (an NPC or a player died with its killer). It does nothing while the
    /// mode is off, when the victim is not a player or a bot (a pet's death is no player's death), when neither side is a
    /// mode bot, or when the death is outside the arena region. A pet killer counts as its owner.
    /// </summary>
    public static void RecordKill(GameLiving killer, GameLiving victim)
    {
        if (!GothwaitePvpMode.IsActive || killer == null || victim == null || killer == victim)
            return;
        if (victim is not (GamePlayer or GameBot))
            return;
        GameLiving killerSide = PvpCombatant.Resolve(killer);
        GameLiving named = killerSide ?? killer;
        bool inArena = victim.CurrentRegionID == GothwaitePvpMode.Arena.RegionId;
        if (!ShouldCount(GothwaitePvpMode.IsActive, true, true, GothwaitePvpMode.IsModeBot(killerSide),
            GothwaitePvpMode.IsModeBot(victim), inArena))
            return;
        Record(new GothwaitePvpFight(
                new GothwaitePvpFighter(named.Name, named.Level, KindOf(named)),
                new GothwaitePvpFighter(victim.Name, victim.Level, KindOf(victim)),
                killerSide != null, victim.X, victim.Y, GothwaitePvpMode.Arena.CastleDistance(victim.X, victim.Y)),
            GameLoop.GameLoopTime);
        // Stage 4: a mode bot that killed a player-shaped target in the arena may say a line (GothwaitePvpKillChat).
        if (killerSide is GameBot killerBot && GothwaitePvpMode.IsModeBot(killerBot))
            GothwaitePvpKillChat.OnKill(killerBot, victim.Name);
    }

    /// <summary>How hot the area around a patrol point is at <paramref name="now"/>: the summed heat weight of the recent fights within <see cref="HeatRadius"/>. 0 when none.</summary>
    public static double Heat(GothwaitePvpSpot point, long now)
    {
        double heat = 0;
        lock (Sync)
        {
            for (int index = 0; index < _ringCount; index++)
            {
                Position fight = Ring[index];
                double weight = Weight(fight.Time, now);
                if (weight <= 0)
                    continue;
                double dx = fight.X - point.X, dy = fight.Y - point.Y;
                if (dx * dx + dy * dy <= (double)HeatRadius * HeatRadius)
                    heat += weight;
            }
        }
        return heat;
    }

    /// <summary>Counts one fight, keeps its position for the heat and writes its GOTH_PVP_KILL line. Nothing counts while the mode is off.</summary>
    public static void Record(GothwaitePvpFight fight, long now)
    {
        if (!GothwaitePvpMode.IsActive)
            return;
        ConColor killerToVictim = ConOf(fight.Killer.Level, fight.Victim.Level);
        ConColor victimToKiller = ConOf(fight.Victim.Level, fight.Killer.Level);
        int killBand = BandOf(fight.Killer.Level);
        int deathBand = BandOf(fight.Victim.Level);
        lock (Sync)
        {
            if (fight.PlayerKill)
            {
                _totalKills++;
                if (killBand >= 0)
                    KillsByBand[killBand]++;
                int colour = ConIndex(killerToVictim);
                if (colour >= 0)
                    KillsByColour[colour]++;
                if (fight.Killer.Kind == GothwaitePvpKind.ModeBot)
                {
                    _modeKills++;
                    if (killBand >= 0)
                        ModeKillsByBand[killBand]++;
                    if (colour >= 0)
                        ModeKillsByColour[colour]++;
                }
            }
            _totalDeaths++;
            if (deathBand >= 0)
                DeathsByBand[deathBand]++;
            if (fight.Victim.Kind == GothwaitePvpKind.ModeBot)
            {
                _modeDeaths++;
                if (deathBand >= 0)
                    ModeDeathsByBand[deathBand]++;
            }
            Ring[_ringNext] = new Position(fight.X, fight.Y, now);
            _ringNext = (_ringNext + 1) % RingCapacity;
            if (_ringCount < RingCapacity)
                _ringCount++;
        }
        Log.Info(KillLine(fight, killerToVictim, victimToKiller));
    }

    /// <summary>A read-only copy of the counts for the status command. The window is the time since the last reset.</summary>
    public static GothwaitePvpStatsSnapshot Snapshot()
    {
        long now = GameLoop.GameLoopTime;
        var bands = new GothwaitePvpBandCount[BandCount];
        var colours = new GothwaitePvpConCount[ConColourCount];
        var modeBands = new GothwaitePvpBandCount[BandCount];
        var modeColours = new GothwaitePvpConCount[ConColourCount];
        lock (Sync)
        {
            for (int band = 0; band < BandCount; band++)
            {
                bands[band] = new GothwaitePvpBandCount(BandLabels[band], KillsByBand[band], DeathsByBand[band]);
                modeBands[band] = new GothwaitePvpBandCount(BandLabels[band], ModeKillsByBand[band], ModeDeathsByBand[band]);
            }
            for (int colour = 0; colour < ConColourCount; colour++)
            {
                colours[colour] = new GothwaitePvpConCount((ConColor)(colour + (int)ConColor.GREY), KillsByColour[colour]);
                modeColours[colour] = new GothwaitePvpConCount((ConColor)(colour + (int)ConColor.GREY), ModeKillsByColour[colour]);
            }
            return new GothwaitePvpStatsSnapshot(_windowStart, now, HeatWindowMilliseconds, _totalKills, _totalDeaths,
                bands, colours)
            {
                ModeKills = _modeKills,
                ModeDeaths = _modeDeaths,
                ModeBands = modeBands,
                ModeKillsByCon = modeColours,
                Tactics = Tactics,
            };
        }
    }

    /// <summary>Clears every count and the heat history, and starts a new window now. Tests use it; the server never does.</summary>
    public static void Reset()
    {
        lock (Sync)
        {
            Array.Clear(KillsByBand);
            Array.Clear(DeathsByBand);
            Array.Clear(KillsByColour);
            Array.Clear(ModeKillsByBand);
            Array.Clear(ModeDeathsByBand);
            Array.Clear(ModeKillsByColour);
            Array.Clear(Ring);
            _totalKills = 0;
            _totalDeaths = 0;
            _modeKills = 0;
            _modeDeaths = 0;
            _ringNext = 0;
            _ringCount = 0;
            _windowStart = GameLoop.GameLoopTime;
            _mainAssists = 0;
            _refusedOpenings = 0;
            _retreats = 0;
            _regroups = 0;
            _adds = 0;
            _ambushes = 0;
        }
    }

    /// <summary>The level band index of a level: 0 for 1-9, 1 for 10-19, 2 for 20-29, 3 for 30-39, 4 for 40 and up. -1 below level 1.</summary>
    public static int BandOf(int level) => level < 1 ? -1 : Math.Min(BandCount - 1, level / 10);

    /// <summary>The label of a band index (1-9, 10-19, ...), or an empty string for an invalid index.</summary>
    public static string BandLabel(int band) => band >= 0 && band < BandCount ? BandLabels[band] : string.Empty;

    /// <summary>The heat weight of a fight at <paramref name="fightTime"/> seen at <paramref name="now"/>: linear from 1 to 0 over the heat window.</summary>
    public static double Weight(long fightTime, long now)
    {
        long age = Math.Max(0, now - fightTime);
        return age >= HeatWindowMilliseconds ? 0 : 1.0 - (double)age / HeatWindowMilliseconds;
    }

    /// <summary>
    /// The counting rule (pure): a fight counts only while the mode is on, the killer exists, the victim is a player-shaped
    /// fighter, at least one side is a mode bot, and the death is inside the arena region.
    /// </summary>
    public static bool ShouldCount(bool active, bool hasKiller, bool victimPlayerShaped, bool modeKiller, bool modeVictim, bool inArena) =>
        active && hasKiller && victimPlayerShaped && (modeKiller || modeVictim) && inArena;

    /// <summary>The con colour a fighter at <paramref name="fromLevel"/> sees on one at <paramref name="toLevel"/>.</summary>
    public static ConColor ConOf(int fromLevel, int toLevel) => ConLevels.GetConColor(ConLevels.GetConLevel(fromLevel, toLevel));

    /// <summary>The log's name of a fighter kind.</summary>
    public static string KindName(GothwaitePvpKind kind) => kind switch
    {
        GothwaitePvpKind.ModeBot => "mode_bot",
        GothwaitePvpKind.Player => "player",
        GothwaitePvpKind.Companion => "companion",
        _ => "other",
    };

    /// <summary>The GOTH_PVP_KILL line of one fight (docs/GOTHWAITE_PVP_MODE.md section 5.7).</summary>
    public static string KillLine(GothwaitePvpFight fight, ConColor killerToVictim, ConColor victimToKiller) =>
        $"GOTH_PVP_KILL killer=\"{fight.Killer.Name}\" killer_level={fight.Killer.Level} killer_kind={KindName(fight.Killer.Kind)} " +
        $"victim=\"{fight.Victim.Name}\" victim_level={fight.Victim.Level} victim_kind={KindName(fight.Victim.Kind)} " +
        $"con_killer_to_victim={killerToVictim} con_victim_to_killer={victimToKiller} x={fight.X} y={fight.Y} " +
        $"distance={Math.Round(fight.CastleDistance)}";

    private static GothwaitePvpKind KindOf(GameLiving living)
    {
        if (GothwaitePvpMode.IsModeBot(living))
            return GothwaitePvpKind.ModeBot;
        if (living is GamePlayer)
            return GothwaitePvpKind.Player;
        if (living is GameBot bot && (bot.IsPersistentPlayerCompanion || bot.IsTemporaryGroupHelper || bot.IsPlayerLedGroup))
            return GothwaitePvpKind.Companion;
        return GothwaitePvpKind.Other;
    }

    private static int ConIndex(ConColor colour)
    {
        int index = (int)colour - (int)ConColor.GREY;
        return index >= 0 && index < ConColourCount ? index : -1;
    }
}
