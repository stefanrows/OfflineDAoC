using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>Small, deterministic preferences; routes still require real world validation.</summary>
public static class AutonomousPickupPlanning
{
    public const double MaximumMeetupTravelMinutes = 20;
    // Group camps must leave room for slower party movement and route
    // detours inside the fixed thirty-minute travel window.
    public const double MaximumGroupCampTravelMinutes = 20;
    public const double MaximumMixedRealmDetourMinutes = 5;

    public static bool WithinTravelBudget(double minutes) =>
        double.IsFinite(minutes) && minutes >= 0 && minutes <= MaximumMeetupTravelMinutes;

    public static bool WithinGroupCampTravelBudget(double minutes) =>
        double.IsFinite(minutes) && minutes >= 0 && minutes <= MaximumGroupCampTravelMinutes;

    public static AutonomousBotDecisionEngine.Camp[] GroupCampsWithinTravelBudget(
        IEnumerable<AutonomousBotDecisionEngine.Camp> camps) =>
        camps?.Where(camp => camp != null && WithinGroupCampTravelBudget(camp.TravelMinutes)).ToArray() ?? [];

    public static AutonomousBotDecisionEngine.Camp[] GroupCampsWithVerifiedRoutes(
        IEnumerable<AutonomousBotDecisionEngine.Camp> camps,
        Func<AutonomousBotDecisionEngine.Camp, bool> canReach,
        int maximumCandidates,
        int maximumRouteChecks)
    {
        if (camps == null || canReach == null || maximumCandidates <= 0 || maximumRouteChecks <= 0)
            return [];

        List<AutonomousBotDecisionEngine.Camp> reachable = new(maximumCandidates);
        foreach (AutonomousBotDecisionEngine.Camp camp in camps
                     .OrderBy(camp => camp.TravelMinutes)
                     .ThenBy(camp => camp.Id, StringComparer.Ordinal)
                     .Take(maximumRouteChecks))
        {
            if (!canReach(camp))
                continue;

            reachable.Add(camp);
            if (reachable.Count == maximumCandidates)
                break;
        }

        return reachable.ToArray();
    }

    public static double SlowestMemberTravelMinutes(IEnumerable<double> memberTravelMinutes)
    {
        if (memberTravelMinutes == null)
            return double.PositiveInfinity;

        double slowest = 0;
        bool hasMember = false;
        foreach (double minutes in memberTravelMinutes)
        {
            if (!double.IsFinite(minutes) || minutes < 0)
                return double.PositiveInfinity;
            slowest = Math.Max(slowest, minutes);
            hasMember = true;
        }

        return hasMember ? slowest : double.PositiveInfinity;
    }

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
