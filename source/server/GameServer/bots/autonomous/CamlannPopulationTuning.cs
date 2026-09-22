using System;

namespace DOL.GS;

/// <summary>
/// The initial Camlann population profile. These values shape the autonomous
/// population without changing the rules that decide whether an action is
/// legal. Keep the choices deterministic and covered by unit tests so a later
/// owner playtest can adjust numbers without rebuilding the behavior model.
/// </summary>
public static class CamlannPopulationTuning
{
    /// <summary>Keep a visible independent-roamer reserve in the frontier.</summary>
    public const double SoloRvrReserveFraction = 0.25d;

    /// <summary>Probability bands for an RvR formation at its full capacity.</summary>
    public const double SoloWarbandFraction = 0.15d;
    public const double PairWarbandFraction = 0.30d;
    public const double SmallWarbandFraction = 0.20d;
    public const double FullWarbandFraction =
        1d - SoloWarbandFraction - PairWarbandFraction - SmallWarbandFraction;

    /// <summary>Cooldown before the same keep/relic objective can be selected again.</summary>
    public const long TargetCooldownMilliseconds = 30 * 60_000L;

    public static int MinimumSoloRvrReserve(int rosterCount)
    {
        rosterCount = Math.Max(0, rosterCount);
        return rosterCount < 4
            ? Math.Max(0, rosterCount - 2)
            : Math.Max(2, (int)Math.Ceiling(rosterCount * SoloRvrReserveFraction));
    }

    /// <summary>
    /// Chooses a Camlann-shaped RvR force: independent scouts, gank pairs,
    /// small roaming crews, and full eight-person groups. When a smaller
    /// number of slots is available, the requested shape is capped safely.
    /// </summary>
    public static int RollWarbandSize(int maximumSize, double roll)
    {
        int maximum = Math.Clamp(maximumSize, 1, AutonomousCrewManager.MaximumCrewSize);
        double bounded = Math.Clamp(roll, 0d, Math.BitDecrement(1d));
        if (maximum == 1 || bounded < SoloWarbandFraction)
            return 1;
        if (bounded < SoloWarbandFraction + PairWarbandFraction)
            return Math.Min(maximum, 2);
        if (bounded < SoloWarbandFraction + PairWarbandFraction + SmallWarbandFraction)
        {
            double smallRoll = (bounded - SoloWarbandFraction - PairWarbandFraction) / SmallWarbandFraction;
            return Math.Min(maximum, 3 + Math.Min(2, (int)(smallRoll * 3)));
        }

        return Math.Min(maximum, AutonomousCrewManager.MaximumCrewSize);
    }

    /// <summary>Low-level hunts prefer pairs and never exceed four members.</summary>
    public static int RollLowLevelPvpPartySize(int maximumSize, double roll)
    {
        int maximum = Math.Clamp(maximumSize, 1, 4);
        if (maximum == 1)
            return 1;
        double bounded = Math.Clamp(roll, 0d, Math.BitDecrement(1d));
        if (bounded < 0.70d)
            return 2;
        if (bounded < 0.90d)
            return Math.Min(3, maximum);
        return maximum;
    }
}
