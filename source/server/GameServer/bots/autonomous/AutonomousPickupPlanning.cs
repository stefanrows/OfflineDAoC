using System;

namespace DOL.GS;

/// <summary>Small, deterministic preferences; routes still require real world validation.</summary>
public static class AutonomousPickupPlanning
{
    public const double MaximumMeetupTravelMinutes = 20;
    public const double MaximumMixedRealmDetourMinutes = 5;

    public static bool WithinTravelBudget(double minutes) =>
        double.IsFinite(minutes) && minutes >= 0 && minutes <= MaximumMeetupTravelMinutes;

    public static bool PreferMixedParty(double mixedTravelMinutes, double localTravelMinutes) =>
        WithinTravelBudget(mixedTravelMinutes) &&
        (!double.IsFinite(localTravelMinutes) || mixedTravelMinutes <= localTravelMinutes + MaximumMixedRealmDetourMinutes);

    public static double CampScore(int targetLevel, int preferredLevel, int liveMobs, int population,
        bool recentlyEmpty, double averageTravelMinutes, bool familiar, int knownDeaths) =>
        Math.Abs(preferredLevel - targetLevel) * 3 +
        Math.Max(0, population) * 8d / Math.Max(1, liveMobs) +
        (recentlyEmpty ? 12 : 0) + Math.Max(0, averageTravelMinutes) +
        Math.Min(5, Math.Max(0, knownDeaths)) * 3 - (familiar ? 2 : 0);
}
