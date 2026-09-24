using System;

namespace DOL.GS;

public enum AutonomousLevelingDanger { Mild, Authentic, FullCamlann }

/// <summary>Type-specific decisions at task, patrol, and group boundaries.</summary>
public static class AutonomousPlayerBehavior
{
    public static AutonomousPlayerType TypeOf(OfflineWorldBotRecord record) =>
        Enum.TryParse(record?.PlayerType, out AutonomousPlayerType type) && Enum.IsDefined(type)
            ? type : AutonomousPlayerType.Leveler;

    public static AutonomousLevelingDanger Danger(int setting) =>
        (AutonomousLevelingDanger)Math.Clamp(setting, 0, 2);

    public static bool IsPrimeTime(DateTime utcNow)
    {
        int hour = utcNow.ToLocalTime().Hour;
        return hour >= 19 && hour < 23;
    }

    public static double HunterPatrolFactor(AutonomousLevelingDanger danger) => danger switch
    {
        AutonomousLevelingDanger.Mild => .35,
        AutonomousLevelingDanger.FullCamlann => 1.35,
        _ => 1,
    };

    public static int GreyEngageChance(AutonomousPlayerType type, int actorLevel, int targetLevel,
        int aggression, AutonomousLevelingDanger danger, int configuredChance)
    {
        if (danger == AutonomousLevelingDanger.Mild || configuredChance <= 0 ||
            actorLevel < 10 || type != AutonomousPlayerType.Hunter)
            return 0;
        int difference = actorLevel - targetLevel;
        if (difference > 20 && danger != AutonomousLevelingDanger.FullCamlann)
            return 0;
        int chance = Math.Clamp(configuredChance, 0, 100);
        if (danger == AutonomousLevelingDanger.FullCamlann) chance = Math.Min(100, chance * 4);
        chance = Math.Clamp(chance + (aggression - 50) / 25, 0, 100);
        return difference > 20 ? Math.Min(chance, 1) : chance;
    }

    public static bool HunterPrefers(int actorLevel, int targetLevel) =>
        Math.Abs(actorLevel - targetLevel) <= AutonomousPvpOpportunityPolicy.PreferredLevelDifference;

    public static int MaximumRvrGroupSize(AutonomousPlayerType type, int level) => type switch
    {
        AutonomousPlayerType.Hunter => 4,
        AutonomousPlayerType.Leveler => 4,
        AutonomousPlayerType.Casual => 2,
        _ => level < 20 ? 4 : 8,
    };

    public static int ChooseRvrGroupSize(AutonomousPlayerType type, int level, int compatibleMaximum,
        double roll, TimeSpan waited)
    {
        int maximum = Math.Min(MaximumRvrGroupSize(type, level), compatibleMaximum);
        if (maximum < 2) return 1;
        if (type == AutonomousPlayerType.Roamer && level >= 20)
        {
            if (maximum >= 8) return 8;
            if (waited < TimeSpan.FromMinutes(10)) return 1;
            return maximum >= 6 ? maximum : 1;
        }
        if (type == AutonomousPlayerType.KeepWarrior && level >= 35)
            return maximum >= 4 ? maximum : 1;
        if (type == AutonomousPlayerType.Hunter)
            return roll < .2 ? 1 : Math.Min(maximum, roll < .55 ? 2 : 4);
        return level < 20
            ? CamlannPopulationTuning.RollLowLevelPvpPartySize(maximum, roll)
            : AutonomousRvrStaging.RollWarbandSize(maximum, roll);
    }

    public static double TownBreakChance(AutonomousPlayerType type, int patience) => type switch
    {
        AutonomousPlayerType.Casual => .35 + (100 - Math.Clamp(patience, 0, 100)) / 500d,
        AutonomousPlayerType.Leveler => .12,
        _ => AutonomousTownDowntime.StartChancePerEligibilityCheck,
    };

    public static bool CanStartCampaign(AutonomousPlayerType type, int minimumLevel, int size) =>
        type == AutonomousPlayerType.KeepWarrior && minimumLevel >= 35 && size >= 4;

    public static int NextLoopIndex(int count, int previousIndex, long seed) =>
        count <= 0 ? -1 : previousIndex >= 0 && previousIndex < count
            ? (previousIndex + 1) % count
            : (int)((ulong)seed % (ulong)count);

    public static int BestEquippedWeaponLevel(GameBot bot) => Math.Max(
        bot?.Inventory?.GetItem(eInventorySlot.RightHandWeapon)?.Level ?? 0,
        Math.Max(bot?.Inventory?.GetItem(eInventorySlot.TwoHandWeapon)?.Level ?? 0,
            bot?.Inventory?.GetItem(eInventorySlot.DistanceWeapon)?.Level ?? 0));

    public static int[] EquippedArmorLevels(GameBot bot) =>
    [
        bot?.Inventory?.GetItem(eInventorySlot.HeadArmor)?.Level ?? 0,
        bot?.Inventory?.GetItem(eInventorySlot.TorsoArmor)?.Level ?? 0,
        bot?.Inventory?.GetItem(eInventorySlot.ArmsArmor)?.Level ?? 0,
        bot?.Inventory?.GetItem(eInventorySlot.LegsArmor)?.Level ?? 0,
        bot?.Inventory?.GetItem(eInventorySlot.HandsArmor)?.Level ?? 0,
        bot?.Inventory?.GetItem(eInventorySlot.FeetArmor)?.Level ?? 0,
    ];
}
