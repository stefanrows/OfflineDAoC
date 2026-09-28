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

    // Task 47 package C: a 2003 party took the camp a few minutes from where it
    // met, not one 25 minutes across the zone. Camps within ten minutes count
    // double, and every minute of road lowers the draw like the solo local pool.
    // All candidates are already in the rendezvous region (or a dungeon entered
    // from it), so there is no separate same-region factor here.
    public const double PreferredGroupCampTravelMinutes = 10;

    public static double GroupCampLocalityWeight(double travelMinutes) =>
        !double.IsFinite(travelMinutes) || travelMinutes < 0
            ? 0
            : (travelMinutes <= PreferredGroupCampTravelMinutes ? 2d : 1d) / (1d + travelMinutes / 5d);

    /// <summary>Weighted outdoor draw for a local pickup party; crowding and depletion keep their weight.</summary>
    public static AutonomousBotDecisionEngine.Camp SelectNearbyGroupCamp(
        IEnumerable<AutonomousBotDecisionEngine.Camp> camps, Random random = null)
    {
        AutonomousBotDecisionEngine.Camp[] choices = camps?.Where(camp => camp != null && !camp.IsDungeon &&
            WithinGroupCampTravelBudget(camp.TravelMinutes)).ToArray() ?? [];
        if (choices.Length == 0)
            return null;
        random ??= Random.Shared;
        double[] weights = choices.Select(camp => AutonomousBotDecisionEngine.OutdoorCampWeight(camp) *
            GroupCampLocalityWeight(camp.TravelMinutes)).ToArray();
        double draw = random.NextDouble() * weights.Sum();
        for (int index = 0; index < choices.Length; index++)
        {
            draw -= weights[index];
            if (draw < 0)
                return choices[index];
        }
        return choices[^1];
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
