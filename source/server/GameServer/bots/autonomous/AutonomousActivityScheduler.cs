using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DOL.GS;

/// <summary>Chooses a new task only at a durable assignment boundary.</summary>
public static class AutonomousActivityScheduler
{
    private const DateTimeStyles DateStyle = DateTimeStyles.RoundtripKind;
    public static readonly TimeSpan PvpDeathWindow = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan NoTargetWindow = TimeSpan.FromMinutes(10);

    public static eAutonomousObjectiveKind Choose(OfflineWorldBotRecord record, DateTime utcNow,
        double roll, bool mayRvr = true, bool excludeGroup = false, bool guildRaid = false,
        bool undergeared = false, bool outleveledByGuild = false,
        AutonomousLevelingDanger danger = AutonomousLevelingDanger.Authentic)
    {
        if (record == null) return eAutonomousObjectiveKind.SoloPve;
        if (guildRaid && !excludeGroup) return eAutonomousObjectiveKind.GroupPve;
        var type = AutonomousPlayerBehavior.TypeOf(record);
        int level = record.Level;
        bool blocked = IsPveBlocked(record, utcNow) || undergeared || outleveledByGuild;
        double rvr = type switch
        {
            AutonomousPlayerType.Hunter => level < 10 ? 0 : level < 20 ? .55 : .7,
            AutonomousPlayerType.Roamer => level < 15 ? 0 : level < 20 ? .2 : level < 35 ? .75 : .85,
            AutonomousPlayerType.KeepWarrior => level < 20 ? 0 : level < 35 ? .15 : level < 50 ? .65 : .9,
            AutonomousPlayerType.Hybrid => level < 20 ? 0 :
                AutonomousPlayerBehavior.IsPrimeTime(utcNow) ? level < 35 ? .2 : level < 50 ? .35 : .5 :
                level < 35 ? .06 : level < 50 ? .12 : .25,
            AutonomousPlayerType.Leveler => level < 35 ? 0 :
                AutonomousPlayerBehavior.IsPrimeTime(utcNow) && record.Aggression >= 70 ? .05 : 0,
            _ => 0,
        };
        rvr = rvr > 0
            ? Math.Clamp(rvr + (record.Aggression - 50) / 500d + (record.RiskTolerance - 50) / 700d, 0, .95)
            : 0;
        if (type == AutonomousPlayerType.Hunter)
            rvr = Math.Min(.95, rvr * AutonomousPlayerBehavior.HunterPatrolFactor(danger));
        if (blocked || !mayRvr) rvr = 0;
        roll = Math.Clamp(roll, 0, .999999);
        if (roll < rvr) return eAutonomousObjectiveKind.RvR;
        double group = type switch
        {
            AutonomousPlayerType.Casual => .12,
            AutonomousPlayerType.Leveler => level < 10 ? .45 : .78,
            AutonomousPlayerType.Hybrid => .58,
            AutonomousPlayerType.Hunter => .35,
            AutonomousPlayerType.Roamer => .75,
            AutonomousPlayerType.KeepWarrior => .7,
            _ => .5,
        };
        group = Math.Clamp(group + (record.Sociability - 50) / 300d, .05, .95);
        if (excludeGroup) group = 0;
        double remainingRoll = (roll - rvr) / Math.Max(.001, 1 - rvr);
        return remainingRoll < group ? eAutonomousObjectiveKind.GroupPve : eAutonomousObjectiveKind.SoloPve;
    }

    public static bool IsPveBlocked(OfflineWorldBotRecord record, DateTime now) =>
        record != null && DateTime.TryParse(record.PveBlockUntilUtc, null, DateStyle, out DateTime until) &&
        until.ToUniversalTime() > now.ToUniversalTime();

    public static bool IsUndergeared(int level, int bestEquippedItemLevel) =>
        level >= 20 && bestEquippedItemLevel < level - 12;

    public static bool IsUndergeared(int level, int bestEquippedItemLevel, IReadOnlyList<int> armorLevels) =>
        IsUndergeared(level, bestEquippedItemLevel) ||
        level >= 35 && armorLevels != null && armorLevels.Count(levelOfItem => levelOfItem >= level - 15) < 4;

    public static bool IsOutleveledByGuild(int level, IReadOnlyList<int> peerLevels) =>
        level >= 20 && level < 50 && peerLevels is { Count: > 0 } &&
        peerLevels.All(peer => peer > level + 5);

    public static bool RecordPvpDeath(OfflineWorldBotRecord record, DateTime now)
    {
        if (record == null) return false;
        if (!DateTime.TryParse(record.RecentPvpDeathWindowUtc, null, DateStyle, out DateTime window) ||
            now.ToUniversalTime() - window.ToUniversalTime() > PvpDeathWindow)
        {
            record.RecentPvpDeathWindowUtc = now.ToUniversalTime().ToString("O");
            record.RecentPvpDeaths = 0;
        }
        record.RecentPvpDeaths++;
        if (record.RecentPvpDeaths < 3) return true;
        BeginPveBlock(record, now, "three PvP deaths in twenty minutes");
        record.RecentPvpDeaths = 0;
        record.RecentPvpDeathWindowUtc = string.Empty;
        return true;
    }

    public static bool ObservePvpTarget(OfflineWorldBotRecord record, DateTime now, bool found)
    {
        if (record == null) return false;
        if (found)
        {
            if (string.IsNullOrWhiteSpace(record.NoTargetsSinceUtc)) return false;
            record.NoTargetsSinceUtc = string.Empty;
            return true;
        }
        if (!DateTime.TryParse(record.NoTargetsSinceUtc, null, DateStyle, out DateTime since))
        {
            record.NoTargetsSinceUtc = now.ToUniversalTime().ToString("O");
            return true;
        }
        if (now.ToUniversalTime() - since.ToUniversalTime() < NoTargetWindow || IsPveBlocked(record, now)) return false;
        BeginPveBlock(record, now, "no preferred PvP target for ten minutes");
        record.NoTargetsSinceUtc = string.Empty;
        return true;
    }

    public static bool BeginPveBlock(OfflineWorldBotRecord record, DateTime now, string reason)
    {
        if (record == null || IsPveBlocked(record, now)) return false;
        int minutes = 45 + Math.Clamp(record.Patience, 0, 100) * 45 / 100;
        record.PveBlockUntilUtc = now.ToUniversalTime().AddMinutes(minutes).ToString("O");
        record.PveBlockReason = reason ?? string.Empty;
        record.NoTargetsSinceUtc = string.Empty;
        return true;
    }
}
