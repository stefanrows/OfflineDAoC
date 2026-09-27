using System;
using System.Collections.Generic;

namespace DOL.GS;

/// <summary>One visible enemy as a member weighs it.</summary>
public readonly record struct RvrTargetView(
    RvrClassTraits Traits,
    double Distance,
    int HealthPercent,
    bool IsCallerTarget,
    bool AttacksOurSupport,
    bool Grudge,
    bool IsPet = false);

/// <summary>
/// How an RvR bot picks whom to hit, the way a person does: stay on the
/// current target a while, follow the caller most of the time in a called
/// group, otherwise lean toward healers and mezzers, wounded enemies and
/// whoever is on our healers, with enough randomness that a group spreads a
/// little instead of acting as one machine.
/// </summary>
public static class AutonomousRvrCombatHabits
{
    /// <returns>The chosen candidate index, or -1 for none.</returns>
    public static int Choose(IReadOnlyList<RvrTargetView> candidates, int currentIndex, bool keepCurrent,
        RvrDoctrine doctrine, double callRoll, double pickRoll)
    {
        if (candidates == null || candidates.Count == 0)
            return -1;
        if (keepCurrent && currentIndex >= 0 && currentIndex < candidates.Count)
            return currentIndex;

        if (doctrine?.HasCaller == true && callRoll < doctrine.FollowCall)
        {
            for (int index = 0; index < candidates.Count; index++)
                if (candidates[index].IsCallerTarget)
                    return index;
        }

        double total = 0;
        double[] weights = new double[candidates.Count];
        for (int index = 0; index < candidates.Count; index++)
        {
            weights[index] = Weight(candidates[index], doctrine);
            total += weights[index];
        }
        if (total <= 0)
            return 0;

        double pick = Math.Clamp(pickRoll, 0, 0.999999) * total;
        for (int index = 0; index < weights.Length; index++)
        {
            pick -= weights[index];
            if (pick < 0)
                return index;
        }
        return weights.Length - 1;
    }

    public static double Weight(RvrTargetView view, RvrDoctrine doctrine)
    {
        double support = doctrine?.SupportPriority ?? 0.8;
        double weight = 1 + (AutonomousRvrDoctrine.TargetPriority(view.Traits) - 1) * support;
        // Nearer enemies are easier to reach and more likely noticed.
        weight *= 1 / (1 + Math.Max(0, view.Distance) / 700d);
        // A wounded enemy looks like a kill.
        weight *= 1 + (100 - Math.Clamp(view.HealthPercent, 0, 100)) / 150d;
        if (view.AttacksOurSupport) weight *= 1.6;
        if (view.Grudge) weight *= 1.5;
        if (view.IsCallerTarget) weight *= 1.3;
        // Pets are hit when they are in the way, not hunted.
        if (view.IsPet) weight *= 0.35;
        return Math.Max(0.01, weight);
    }
}
