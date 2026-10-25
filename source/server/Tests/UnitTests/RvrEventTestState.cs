using System;
using System.Reflection;

namespace DOL.GS.Tests;

internal static class RvrEventTestState
{
    private static readonly string[] CamlannState =
        { "Events", "CarrierEvents", "CarrierTargets", "Cooldowns", "ReleasedForces", "SelectedKeeps", "SelectedRelics", "DefenseAlarms", "DefenseWarnings", "KeepCombatPressure", "AbandonedTargets", "KeepRouteBlocks" };

    private static readonly string[] RealmState =
        { "Events", "CarrierEvents", "CarrierTargets", "Cooldowns", "ReleasedForces", "SelectedKeeps", "SelectedRelics", "DefenseAlarms", "DefenseWarnings", "KeepCombatPressure" };

    // Production callers supply partial candidate lists. Tests must explicitly
    // isolate process-wide event state, not rely on missing rows cancelling
    // every event created by the previous fixture. Both implementations behind the
    // AutonomousRvrEventLayer facade are cleared; the RvR world's switches return
    // to their production defaults (staged assault and siege scheduling on).
    internal static void Clear()
    {
        ClearFields(typeof(CamlannRvrEventLayer), CamlannState);
        // Realm armies (R4b): per-realm plans, invader census and revenge leases.
        AutonomousRealmArmyPlans.Clear();
        AutonomousRealmArmyCensus.Clear();
        AutonomousRealmGrudgeHunts.Clear();
        ClearRealmState();
        RealmRvrEventLayer.StagedAssault = true;
        RealmRvrEventLayer.SiegeScheduling = true;
    }

    /// <summary>
    /// The upstream fixtures' reset (shadowofze/OfflineDAoC 0.38c RvrEventTestState.Clear) for the RvR world's layer:
    /// these fixtures document the continuous (unstaged) assault; UT_StagedSiegeOct6 covers staging.
    /// </summary>
    internal static void ClearRealm()
    {
        ClearFields(typeof(CamlannRvrEventLayer), CamlannState);
        ClearRealmState();
        RealmRvrEventLayer.StagedAssault = false;
        RealmRvrEventLayer.SiegeScheduling = false;
    }

    private static void ClearRealmState()
    {
        ClearFields(typeof(RealmRvrEventLayer), RealmState);
        Type realm = typeof(RealmRvrEventLayer);
        foreach (string name in new[] { "_nextSiegeRoll", "_lastSiegeActiveTick" })
            realm.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, 0L);
        realm.GetField("_siegeStartReady", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
        realm.GetField("_lastExpireTick", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, long.MinValue);
    }

    private static void ClearFields(Type layer, string[] names)
    {
        foreach (string name in names)
        {
            object value = layer.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            value.GetType().GetMethod("Clear").Invoke(value, null);
        }
        layer.GetField("_nextStragglerSweep", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, 0L);
        layer.GetField("_nextDefensePulse", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, 0L);
    }
}
