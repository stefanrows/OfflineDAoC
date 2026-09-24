using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// Records who struck first in each player-versus-player fight that involves an
/// autonomous bot. The per-minute summary names the code path that started the
/// fight so unexpected engagements can be traced without per-hit log lines.
/// </summary>
public static class AutonomousPvpEngagementTracker
{
    public const string Human = "human";
    public const string Pet = "pet";
    public const string Collateral = "collateral";
    public const string Untagged = "untagged";
    public const string Opportunity = "opportunity";
    public const string Grudge = "grudge";
    public const string SharedDungeon = "shared-dungeon";
    public const string FrontierThreat = "frontier-threat";
    public const string CrowdControl = "cc-sweep";
    public const string GroupAssist = "group-assist";
    public const string Protection = "protection";

    private const string ReasonProperty = "AutonomousPvpEngageReason";
    private const long FightMemoryMilliseconds = 30_000;
    private const long InitiatorMemoryMilliseconds = 300_000;
    private const long TagMemoryMilliseconds = 15_000;
    private const int SummaryEntries = 12;

    private sealed record Tagged(string Reason, long Tick);

    private static readonly ConcurrentDictionary<(GameLiving Attacker, GameLiving Victim), long> LastHit = new();
    private static readonly ConcurrentDictionary<(GameLiving Attacker, GameLiving Victim), long> Initiated = new();
    private static readonly ConcurrentDictionary<string, int> Engagements = new(StringComparer.Ordinal);
    private static int _pvpDeaths;
    private static int _pveDeaths;

    /// <summary>Marks the reason the actor is about to start a PvP fight.</summary>
    public static void Tag(GameLiving actor, string reason)
    {
        GameLiving identity = PvpCombatant.Resolve(actor);
        identity?.TempProperties?.SetProperty(ReasonProperty, new Tagged(reason, GameLoop.GameLoopTime));
    }

    /// <summary>Called for every received attack, including misses and resists.</summary>
    public static void ObserveAttack(GameLiving victim, AttackData ad)
    {
        if (ad?.Attacker == null || victim is not (GamePlayer or GameBot))
            return;
        GameLiving attacker = PvpCombatant.Resolve(ad.Attacker);
        if (attacker == null || attacker == victim || !InvolvesAutonomousBot(attacker, victim))
            return;

        long now = GameLoop.GameLoopTime;
        bool newFight = !IsRecent(LastHit, (attacker, victim), now, FightMemoryMilliseconds) &&
                        !IsRecent(LastHit, (victim, attacker), now, FightMemoryMilliseconds);
        LastHit[(attacker, victim)] = now;
        if (!newFight)
            return;

        Initiated[(attacker, victim)] = now;
        string key = $"{Classify(ad.Attacker, attacker, victim, now)}|{LevelBand(attacker.Level)}>" +
                     $"{LevelBand(victim.Level)}|{TypeName(attacker)}";
        Engagements.AddOrUpdate(key, 1, (_, count) => count + 1);
    }

    /// <summary>True when the victim struck the killer first in their latest fight.</summary>
    public static bool VictimStartedFight(GameLiving victim, GameLiving killer)
    {
        GameLiving victimIdentity = PvpCombatant.Resolve(victim);
        GameLiving killerIdentity = PvpCombatant.Resolve(killer);
        if (victimIdentity == null || killerIdentity == null)
            return false;
        long now = GameLoop.GameLoopTime;
        bool victimStarted = Initiated.TryGetValue((victimIdentity, killerIdentity), out long victimTick) &&
                             now - victimTick <= InitiatorMemoryMilliseconds;
        bool killerStarted = Initiated.TryGetValue((killerIdentity, victimIdentity), out long killerTick) &&
                             now - killerTick <= InitiatorMemoryMilliseconds;
        return victimStarted && (!killerStarted || victimTick > killerTick);
    }

    public static void RecordDeath(bool pvp)
    {
        if (pvp)
            Interlocked.Increment(ref _pvpDeaths);
        else
            Interlocked.Increment(ref _pveDeaths);
    }

    /// <summary>Returns and resets the minute's counters, and prunes old fight memory.</summary>
    public static (int PvpDeaths, int PveDeaths, int Fights, string Top) Drain()
    {
        long now = GameLoop.GameLoopTime;
        foreach (var entry in LastHit.Where(pair => now - pair.Value > FightMemoryMilliseconds).ToArray())
            LastHit.TryRemove(entry);
        foreach (var entry in Initiated.Where(pair => now - pair.Value > InitiatorMemoryMilliseconds).ToArray())
            Initiated.TryRemove(entry);

        var counts = Engagements.Keys.ToArray()
            .Select(key => (Key: key, Count: Engagements.TryRemove(key, out int count) ? count : 0))
            .Where(entry => entry.Count > 0)
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
        string top = string.Join(";", counts.Take(SummaryEntries).Select(entry => $"{entry.Key}:{entry.Count}"));
        return (Interlocked.Exchange(ref _pvpDeaths, 0), Interlocked.Exchange(ref _pveDeaths, 0),
            counts.Sum(entry => entry.Count), top);
    }

    public static string LevelBand(int level) => level switch
    {
        < 10 => "1-9",
        < 20 => "10-19",
        < 35 => "20-34",
        < 50 => "35-49",
        _ => "50"
    };

    public static string Classify(bool attackerIsHuman, bool viaPet, bool targetedVictim, string recentTag) =>
        attackerIsHuman ? Human :
        viaPet ? Pet :
        !targetedVictim ? Collateral :
        string.IsNullOrWhiteSpace(recentTag) ? Untagged : recentTag;

    private static string Classify(GameLiving rawAttacker, GameLiving attacker, GameLiving victim, long now)
    {
        Tagged tag = attacker.TempProperties?.GetProperty<Tagged>(ReasonProperty, null);
        string recentTag = tag != null && now - tag.Tick <= TagMemoryMilliseconds ? tag.Reason : null;
        bool targetedVictim = PvpCombatant.Resolve(attacker.TargetObject as GameLiving) == victim;
        return Classify(attacker is GamePlayer, rawAttacker != attacker, targetedVictim, recentTag);
    }

    private static bool InvolvesAutonomousBot(GameLiving attacker, GameLiving victim) =>
        attacker is GameBot { IsAutonomousWorldBot: true } || victim is GameBot { IsAutonomousWorldBot: true };

    private static bool IsRecent(ConcurrentDictionary<(GameLiving, GameLiving), long> map,
        (GameLiving, GameLiving) key, long now, long window) =>
        map.TryGetValue(key, out long tick) && now - tick <= window;

    private static string TypeName(GameLiving living) => living switch
    {
        GamePlayer => "Player",
        GameBot { IsAutonomousWorldBot: true } bot => AutonomousPlayerBehavior.TypeOf(bot.PersistentRecord).ToString(),
        _ => "Companion"
    };
}
