using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>The guild identity the former compares: the guild id and the alliance id. Empty ids never match.</summary>
public readonly record struct GothwaitePvpGuild(string GuildId, string AllianceId);

/// <summary>One member of a forming group, as the former ranks it.</summary>
public readonly record struct GothwaitePvpMember(int Level, bool Healer, GothwaitePvpGuild Guild);

/// <summary>A ready bot that waits for a group. BotId and ReadyTick keep the order deterministic.</summary>
public readonly record struct GothwaitePvpCandidate(long BotId, GothwaitePvpMember Member, long ReadyTick);

/// <summary>What a forming group does on a tick: keep waiting, leave with its members, or disband.</summary>
public enum GothwaitePvpFormationStep { Wait, Depart, Disband }

/// <summary>
/// The castle rules as pure functions (docs/GOTHWAITE_PVP_MODE.md 6.2, 6.3, 6.6): the rest and buff ends, the departure
/// decision of a forming group, the level band, the guild and healer preference, and the grey/purple rule (G4).
/// </summary>
public static class GothwaitePvpCastleRules
{
    public const string RestRecovered = "recovered";
    public const string RestCap = "cap";

    /// <summary>Healers for the former's preference: every class the party code can heal with (BotPveGroupRole.Healer).</summary>
    public static bool IsHealer(eCharacterClass characterClass) => BotPartyRoles.IsHealingClass(characterClass);

    /// <summary>Rested: health reaches the rest level, and power too for a bot that casts (MaxMana above zero).</summary>
    public static bool RestDone(int healthPercent, int powerPercent, bool usesPower, int restPercent) =>
        healthPercent >= restPercent && (!usesPower || powerPercent >= restPercent);

    /// <summary>Why the castle rest ends this turn: recovered, cap (the rest ran out of time), or null while it goes on.</summary>
    public static string RestEndReason(int healthPercent, int powerPercent, bool usesPower, int restPercent, long now, long until) =>
        RestDone(healthPercent, powerPercent, usesPower, restPercent) ? RestRecovered : now >= until ? RestCap : null;

    /// <summary>The buff window ends once its time has passed and the bot is no longer casting.</summary>
    public static bool BuffWindowDone(long now, long until, bool casting) => now >= until && !casting;

    /// <summary>
    /// A released bot keeps its group only when it is a follower of a group that is still gathering in the castle and
    /// its leader is in the castle too. Otherwise it leaves the group and rests and regroups in the castle (section 6.6).
    /// </summary>
    public static bool KeepsGroupOnRelease(bool isLeader, bool departing, bool leaderInCastle) =>
        !isLeader && !departing && leaderInCastle;

    /// <summary>
    /// The departure decision of a forming group: a full group leaves; at its wait limit a group with at least its
    /// minimum leaves and a smaller one disbands; otherwise it keeps waiting.
    /// </summary>
    public static GothwaitePvpFormationStep Decide(long now, long expiresAt, int readyCount, int target, int minimum)
    {
        if (readyCount >= target)
            return GothwaitePvpFormationStep.Depart;
        if (now < expiresAt)
            return GothwaitePvpFormationStep.Wait;
        return readyCount >= minimum ? GothwaitePvpFormationStep.Depart : GothwaitePvpFormationStep.Disband;
    }

    /// <summary>Guild preference: 2 for the same guild, 1 for an allied guild (the same alliance), 0 for strangers.</summary>
    public static int GuildAffinity(GothwaitePvpGuild first, GothwaitePvpGuild second)
    {
        if (!string.IsNullOrEmpty(first.GuildId) && string.Equals(first.GuildId, second.GuildId, StringComparison.Ordinal))
            return 2;
        if (!string.IsNullOrEmpty(first.AllianceId) && string.Equals(first.AllianceId, second.AllianceId, StringComparison.Ordinal))
            return 1;
        return 0;
    }

    /// <summary>A candidate may join only within ±<paramref name="levelBand"/> levels of the leader (inclusive).</summary>
    public static bool InLevelBand(int leaderLevel, int candidateLevel, int levelBand) =>
        Math.Abs(leaderLevel - candidateLevel) <= levelBand;

    /// <summary>
    /// The candidates that may join a group, best first. Only candidates inside the leader's level band are ranked.
    /// While the group has no healer and already has a non-healer (so the next non-healer would be its second one of
    /// another role), a healer comes first. Then the same guild, then an allied guild, then strangers. Then the closer
    /// level, the longer wait, and the bot id.
    /// </summary>
    public static IReadOnlyList<GothwaitePvpCandidate> Rank(GothwaitePvpMember leader, IReadOnlyList<GothwaitePvpMember> members,
        IEnumerable<GothwaitePvpCandidate> candidates, int levelBand)
    {
        IReadOnlyList<GothwaitePvpMember> group = members ?? Array.Empty<GothwaitePvpMember>();
        bool needsHealer = !group.Any(member => member.Healer) && group.Any(member => !member.Healer);
        return (candidates ?? Array.Empty<GothwaitePvpCandidate>())
            .Where(candidate => InLevelBand(leader.Level, candidate.Member.Level, levelBand))
            .OrderBy(candidate => needsHealer && candidate.Member.Healer ? 0 : 1)
            .ThenByDescending(candidate => Affinity(candidate.Member.Guild, group))
            .ThenBy(candidate => Math.Abs(candidate.Member.Level - leader.Level))
            .ThenBy(candidate => candidate.ReadyTick)
            .ThenBy(candidate => candidate.BotId)
            .ToArray();
    }

    /// <summary>The best guild affinity of a candidate with any member of the group.</summary>
    private static int Affinity(GothwaitePvpGuild candidate, IReadOnlyList<GothwaitePvpMember> members) =>
        members.Count == 0 ? 0 : members.Max(member => GuildAffinity(candidate, member.Guild));

    /// <summary>
    /// G4: a bot opens a fight only on a target that cons green, blue, yellow, orange or red to it. Grey and purple are
    /// never opened on, in either direction.
    /// </summary>
    public static bool ConMayOpen(ConColor color) =>
        color is ConColor.GREEN or ConColor.BLUE or ConColor.YELLOW or ConColor.ORANGE or ConColor.RED;
}
