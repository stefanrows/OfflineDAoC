using System;

namespace DOL.GS;

/// <summary>
/// Looking for group at the portal keep, the 2003 way: healers, casters and
/// tanks who want a group wait at their realm's border keep with LFG on,
/// where a guild leader can pick them up. Stealthers, archers, Hunter-type
/// players and the impatient leave alone. A player gave up after a while and
/// went out solo or followed the army; so does a bot, by its Patience.
/// </summary>
public static class AutonomousRvrLfg
{
    public static bool SeeksGroup(eCharacterClass characterClass, AutonomousPlayerType type, int level)
    {
        if (level < 20 || type == AutonomousPlayerType.Hunter)
            return false;
        RvrClassTraits traits = AutonomousRvrDoctrine.TraitsOf(characterClass);
        return !traits.HasFlag(RvrClassTraits.Stealth) && !traits.HasFlag(RvrClassTraits.Archer);
    }

    /// <summary>How long a bot waits at the keep: 8 minutes when impatient, 20 when patient.</summary>
    public static int PatienceMilliseconds(int patience) =>
        (int)Math.Round((8 + Math.Clamp(patience, 0, 100) / 100d * 12) * 60_000);

    /// <summary>
    /// Leave with a viable group rather than waiting forever for a full eight:
    /// eight when available, four after a few minutes, three after a long wait.
    /// </summary>
    public static int ViableSize(int available, TimeSpan waited)
    {
        if (available >= 8) return 8;
        if (waited >= TimeSpan.FromMinutes(3) && available >= 4) return available;
        if (waited >= TimeSpan.FromMinutes(8) && available >= 3) return available;
        return 1;
    }

    /// <summary>
    /// A partial death is not a wipe: the living finish the fight and rez
    /// afterwards. Only when the group is effectively gone does it release
    /// and regroup at the keep.
    /// </summary>
    public static bool IsWipe(int alive, int total, bool rezzerAlive) =>
        total > 0 && (alive <= 1 || alive * 2 < total && !rezzerAlive);
}
