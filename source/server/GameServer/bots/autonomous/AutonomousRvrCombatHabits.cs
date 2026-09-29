using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>One visible enemy as a member weighs it.</summary>
public readonly record struct RvrTargetView(
    RvrClassTraits Traits,
    double Distance,
    int HealthPercent,
    bool IsCallerTarget,
    bool AttacksOurSupport,
    bool Grudge,
    bool IsPet = false,
    bool IsCasting = false);

/// <summary>
/// Who is choosing: the group's caller (main assist) leans harder on casters
/// and healers; a bot that can interrupt quickly (archer, melee in reach, an
/// instant spell) leans toward an enemy caster who is casting right now.
/// </summary>
public readonly record struct RvrChooser(bool IsCaller, bool CanInterrupt);

/// <summary>How a damage dealer answers the caller's current target.</summary>
public enum RvrCallResponse
{
    /// <summary>The rule does not apply (support, no own target, already on it).</summary>
    None,
    /// <summary>The /assist has not picked up the new target yet (1-2 s).</summary>
    Wait,
    /// <summary>Switch to the called target now.</summary>
    Switch,
    /// <summary>Own target is nearly dead: finish it first.</summary>
    Finish,
    /// <summary>This bot missed or ignored the call.</summary>
    Ignore,
}

/// <summary>
/// How an RvR bot picks whom to hit, the way a person does: stay on the
/// current target a while, follow the caller most of the time in a called
/// group, otherwise lean toward healers and mezzers, wounded enemies and
/// whoever is on our healers, with enough randomness that a group spreads a
/// little instead of acting as one machine.
/// </summary>
public static class AutonomousRvrCombatHabits
{
    /// <summary>The caller (MA) takes casters and healers first [D6422, O112].</summary>
    public const double CallerCasterBonus = 1.4;
    /// <summary>A casting enemy caster is the interrupt target [D14122, O205].</summary>
    public const double CastingBonus = 1.8;
    /// <summary>When a casting caster makes a bot look again, its current target still counts double.</summary>
    public const double CurrentTargetStickBonus = 2.0;
    /// <summary>/assist took 1-2 s to pick up a target [D4274, June 2003].</summary>
    public const int SwitchDelayMinMilliseconds = 1_000;
    public const int SwitchDelayMaxMilliseconds = 2_000;
    /// <summary>A damage dealer finishes its own target below this health before switching.</summary>
    public const int FinishOwnTargetPercent = 30;
    /// <summary>Chance to ignore a switch call: 15 % at Patience 15, 5 % at Patience 85.</summary>
    public const double IgnoreCallMaxChance = 0.15;
    public const double IgnoreCallMinChance = 0.05;

    /// <returns>The chosen candidate index, or -1 for none.</returns>
    public static int Choose(IReadOnlyList<RvrTargetView> candidates, int currentIndex, bool keepCurrent,
        RvrDoctrine doctrine, double callRoll, double pickRoll, RvrChooser chooser = default,
        bool reconsiderForCaster = false)
    {
        if (candidates == null || candidates.Count == 0)
            return -1;
        bool holding = keepCurrent && currentIndex >= 0 && currentIndex < candidates.Count;
        if (holding && !reconsiderForCaster)
            return currentIndex;

        if (!holding && doctrine?.HasCaller == true && callRoll < doctrine.FollowCall)
        {
            for (int index = 0; index < candidates.Count; index++)
                if (candidates[index].IsCallerTarget)
                    return index;
        }

        double total = 0;
        double[] weights = new double[candidates.Count];
        for (int index = 0; index < candidates.Count; index++)
        {
            weights[index] = Weight(candidates[index], doctrine, chooser);
            // A caster starting a spell makes a bot look again; it does not
            // drop its own target lightly.
            if (holding && index == currentIndex)
                weights[index] *= CurrentTargetStickBonus;
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

    public static double Weight(RvrTargetView view, RvrDoctrine doctrine, RvrChooser chooser = default)
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
        if (chooser.IsCaller && IsCasterClass(view.Traits)) weight *= CallerCasterBonus;
        if (chooser.CanInterrupt && view.IsCasting && IsCasterClass(view.Traits)) weight *= CastingBonus;
        // Pets are hit when they are in the way, not hunted.
        if (view.IsPet) weight *= 0.35;
        return Math.Max(0.01, weight);
    }

    /// <summary>Casters, healers and mezzers: the classes whose casts decide a fight.</summary>
    public static bool IsCasterClass(RvrClassTraits traits) =>
        (traits & (RvrClassTraits.Caster | RvrClassTraits.Healer | RvrClassTraits.AreaMez | RvrClassTraits.AreaStun)) != 0;

    /// <summary>
    /// Damage dealers ride the assist train; healers, CC casters and the speed
    /// classes do their own job first (P8) and are not bound to the call.
    /// </summary>
    public static bool IsDamageDealer(RvrClassTraits own) =>
        (own & (RvrClassTraits.Healer | RvrClassTraits.AreaMez | RvrClassTraits.AreaStun | RvrClassTraits.Speed)) == 0;

    /// <summary>1,000-2,000 ms for <paramref name="roll"/> in [0,1].</summary>
    public static int SwitchDelayMilliseconds(double roll) => SwitchDelayMinMilliseconds +
        (int)Math.Round(Math.Clamp(roll, 0, 1) * (SwitchDelayMaxMilliseconds - SwitchDelayMinMilliseconds));

    /// <summary>
    /// An impatient bot tunnels on its own target more often: 15 % at
    /// Patience 15 (or less), 5 % at 85 (or more), linear between.
    /// </summary>
    public static double IgnoreCallChance(int patience)
    {
        double share = (Math.Clamp(patience, 15, 85) - 15) / 70d;
        return IgnoreCallMaxChance - share * (IgnoreCallMaxChance - IgnoreCallMinChance);
    }

    /// <summary>What a member does about the caller's current target.</summary>
    public static RvrCallResponse RespondToCall(bool damageDealer, bool hasOwnTarget, bool ownIsCall,
        int ownTargetHealthPercent, long sinceCallMilliseconds, int delayMilliseconds, bool ignored)
    {
        if (!damageDealer || !hasOwnTarget || ownIsCall)
            return RvrCallResponse.None;
        if (ownTargetHealthPercent < FinishOwnTargetPercent)
            return RvrCallResponse.Finish;
        if (ignored)
            return RvrCallResponse.Ignore;
        return sinceCallMilliseconds < delayMilliseconds ? RvrCallResponse.Wait : RvrCallResponse.Switch;
    }

    /// <summary>The median of the samples, 0 for none.</summary>
    public static int Median(IReadOnlyCollection<int> samples)
    {
        if (samples == null || samples.Count == 0)
            return 0;
        int[] sorted = samples.OrderBy(sample => sample).ToArray();
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>
    /// CC discipline (P11): a DoT on a mezzed enemy, or an area spell that
    /// would catch a mezzed enemy, breaks the mez; only the assist target may
    /// be hit while mezzed.
    /// </summary>
    public static bool HoldsForMezz(bool areaSpell, bool damageOverTime, bool targetMezzed, bool targetIsAssist,
        int otherMezzedInArea) =>
        areaSpell ? otherMezzedInArea > 0 || targetMezzed && !targetIsAssist :
        damageOverTime && targetMezzed && !targetIsAssist;
}
