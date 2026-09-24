using System;
using System.Linq;
using DOL.Database;
using DOL.Database.Attributes;
using OfflineDaoc.Configuration;

namespace DOL.GS;

public enum AutonomousPlayerType { Leveler, Casual, Hybrid, Hunter, Roamer, KeepWarrior }
public enum AutonomousGuildCharter { Hunting, Rvr, Keep, Leveling, Social }

/// <summary>Stable identity defaults. No runtime random draw or name-based guild ownership.</summary>
public static class AutonomousBotIdentity
{
    private static readonly int[][] CharterWeights =
    [
        [10, 0, 15, 50, 25, 0], // Hunting
        [0, 0, 15, 10, 50, 25], // RvR
        [10, 0, 20, 0, 25, 45], // Keep
        [55, 15, 25, 0, 5, 0], // Leveling
        [35, 40, 25, 0, 0, 0], // Social
    ];

    public static AutonomousGuildCharter CharterForOrdinal(int ordinal) =>
        (AutonomousGuildCharter)(Math.Max(0, ordinal) % CharterWeights.Length);

    public static AutonomousGuildCharter CharterForOrdinal(int ordinal, PlayerTypeMix mix)
    {
        if (mix == null) return CharterForOrdinal(ordinal);
        int[] scores =
        [
            mix.Hunter + mix.Hybrid / 4,
            mix.Roamer + mix.Hybrid / 2,
            mix.KeepWarrior + mix.Roamer / 4,
            mix.Leveler + mix.Hybrid / 4,
            mix.Casual + mix.Leveler / 4,
        ];
        int draw = (int)((uint)Math.Max(0, ordinal) * 37u % (uint)scores.Sum());
        for (int index = 0; index < scores.Length; index++)
        {
            if (draw < scores[index]) return (AutonomousGuildCharter)index;
            draw -= scores[index];
        }
        return AutonomousGuildCharter.Leveling;
    }

    public static AutonomousPlayerType TypeFor(long botId, int classId, string guildId,
        AutonomousGuildCharter charter, PlayerTypeMix mix = null)
    {
        if (!Enum.IsDefined(charter)) charter = AutonomousGuildCharter.Leveling;
        int[] weights = (int[])CharterWeights[(int)charter].Clone();
        eCharacterClass characterClass = (eCharacterClass)classId;
        if (characterClass is eCharacterClass.Infiltrator or eCharacterClass.Shadowblade or
            eCharacterClass.Nightshade or eCharacterClass.Scout or eCharacterClass.Hunter or eCharacterClass.Ranger)
        {
            int shift = Math.Min(10, weights[(int)AutonomousPlayerType.Hybrid]);
            weights[(int)AutonomousPlayerType.Hybrid] -= shift;
            weights[(int)AutonomousPlayerType.Hunter] += shift;
        }
        else if (BotPartyRoles.IsHealingClass(characterClass))
        {
            int shift = Math.Min(10, weights[(int)AutonomousPlayerType.Hunter]);
            weights[(int)AutonomousPlayerType.Hunter] -= shift;
            weights[(int)AutonomousPlayerType.Leveler] += shift;
        }
        if (mix != null)
        {
            int[] global = mix.Values;
            for (int index = 0; index < weights.Length; index++)
                weights[index] = global[index] * (50 + weights[index]);
        }
        int draw = (int)(StableHash(botId, classId, guildId, 0) % (uint)weights.Sum());
        for (int index = 0; index < weights.Length; index++)
        {
            if (draw < weights[index]) return (AutonomousPlayerType)index;
            draw -= weights[index];
        }
        return AutonomousPlayerType.Hybrid;
    }

    public static bool Ensure(OfflineWorldBotRecord record, AutonomousGuildCharter charter, PlayerTypeMix mix = null)
    {
        if (record == null || Enum.TryParse(record.PlayerType, out AutonomousPlayerType parsed) && Enum.IsDefined(parsed))
            return false;
        record.PlayerType = TypeFor(record.BotId, record.ClassId, record.GuildId, charter, mix).ToString();
        record.Aggression = Trait(record, 1);
        record.RiskTolerance = Trait(record, 2);
        record.Sociability = Trait(record, 3);
        record.Patience = Trait(record, 4);
        record.Chattiness = Trait(record, 5);
        record.Dirty = true;
        return true;
    }

    public static int Trait(OfflineWorldBotRecord record, int trait) =>
        15 + (int)(StableHash(record.BotId, record.ClassId, record.GuildId, trait) % 71);

    private static uint StableHash(long botId, int classId, string guildId, int salt)
    {
        uint value = 2166136261;
        foreach (char c in guildId ?? string.Empty) value = (value ^ c) * 16777619;
        for (int offset = 0; offset < 64; offset += 8) value = (value ^ (byte)(botId >> offset)) * 16777619;
        for (int offset = 0; offset < 32; offset += 8) value = (value ^ (byte)(classId >> offset)) * 16777619;
        return (value ^ (uint)salt) * 16777619;
    }
}

[DataTable(TableName = "offline_managed_guild_charters")]
public sealed class AutonomousGuildCharterRecord : DataObject
{
    [PrimaryKey] public string GuildId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public bool IsManaged { get; set; } = true;
    [DataElement(AllowDbNull = false)] public string Charter { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int Ordinal { get; set; }
    [DataElement(AllowDbNull = false)] public string DisplayName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string PendingOldName { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string PendingNewName { get; set; } = string.Empty;
}
