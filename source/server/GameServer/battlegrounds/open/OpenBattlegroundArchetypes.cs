using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    public enum OpenBgSlotRole { Healer, AreaCc, Speed, Tank, Melee, Bomber, Caster, Assassin, Archer, Pet, Flex }

    public sealed record OpenBgSlot(OpenBgSlotRole Role, int Min, int Wish);

    public sealed record OpenBgArchetype(string Key, int Size, OpenBgSlot[] Slots);

    /// <summary>
    /// Role archetypes for open BG crews (docs/OPEN_BG_GROUP_PRESETS.md): slots of roles, each role a set of classes
    /// of all three realms. Cross-realm by construction; in Per realm mode the roster filters the sets to one realm.
    /// </summary>
    public static class OpenBattlegroundArchetypes
    {
        private static readonly Dictionary<OpenBgSlotRole, HashSet<eCharacterClass>> Roles = new()
        {
            [OpenBgSlotRole.Healer] = new() { eCharacterClass.Cleric, eCharacterClass.Healer, eCharacterClass.Druid, eCharacterClass.Friar, eCharacterClass.Shaman, eCharacterClass.Warden },
            [OpenBgSlotRole.AreaCc] = new() { eCharacterClass.Sorcerer, eCharacterClass.Minstrel, eCharacterClass.Healer, eCharacterClass.Bard, eCharacterClass.Eldritch, eCharacterClass.Mentalist },
            [OpenBgSlotRole.Speed] = new() { eCharacterClass.Minstrel, eCharacterClass.Skald, eCharacterClass.Bard },
            [OpenBgSlotRole.Tank] = new() { eCharacterClass.Armsman, eCharacterClass.Paladin, eCharacterClass.Warrior, eCharacterClass.Hero, eCharacterClass.Blademaster },
            [OpenBgSlotRole.Melee] = new() { eCharacterClass.Mercenary, eCharacterClass.Reaver, eCharacterClass.Armsman, eCharacterClass.Savage, eCharacterClass.Berserker, eCharacterClass.Blademaster, eCharacterClass.Hero, eCharacterClass.Champion, eCharacterClass.Valewalker },
            [OpenBgSlotRole.Bomber] = new() { eCharacterClass.Wizard, eCharacterClass.Spiritmaster, eCharacterClass.Eldritch, eCharacterClass.Enchanter },
            [OpenBgSlotRole.Caster] = new() { eCharacterClass.Cabalist, eCharacterClass.Theurgist, eCharacterClass.Sorcerer, eCharacterClass.Runemaster, eCharacterClass.Bonedancer, eCharacterClass.Mentalist, eCharacterClass.Eldritch },
            [OpenBgSlotRole.Assassin] = new() { eCharacterClass.Infiltrator, eCharacterClass.Shadowblade, eCharacterClass.Nightshade },
            [OpenBgSlotRole.Archer] = new() { eCharacterClass.Scout, eCharacterClass.Hunter, eCharacterClass.Ranger },
            [OpenBgSlotRole.Pet] = new() { eCharacterClass.Theurgist, eCharacterClass.Necromancer, eCharacterClass.Cabalist, eCharacterClass.Bonedancer, eCharacterClass.Spiritmaster, eCharacterClass.Animist, eCharacterClass.Enchanter },
            [OpenBgSlotRole.Flex] = new(),
        };

        private static OpenBgSlot S(OpenBgSlotRole role, int min, int wish) => new(role, min, wish);

        private static readonly Dictionary<string, OpenBgArchetype> Catalog = new(StringComparer.OrdinalIgnoreCase)
        {
            ["std8"] = new("std8", 8, new[] { S(OpenBgSlotRole.Healer, 2, 2), S(OpenBgSlotRole.AreaCc, 1, 1), S(OpenBgSlotRole.Speed, 1, 1), S(OpenBgSlotRole.Tank, 1, 2), S(OpenBgSlotRole.Melee, 2, 3) }),
            ["caster8"] = new("caster8", 8, new[] { S(OpenBgSlotRole.Healer, 2, 2), S(OpenBgSlotRole.AreaCc, 1, 2), S(OpenBgSlotRole.Tank, 1, 2), S(OpenBgSlotRole.Bomber, 2, 3), S(OpenBgSlotRole.Caster, 0, 1) }),
            ["raid8"] = new("raid8", 8, new[] { S(OpenBgSlotRole.Healer, 2, 2), S(OpenBgSlotRole.Speed, 1, 1), S(OpenBgSlotRole.Tank, 1, 1), S(OpenBgSlotRole.Melee, 1, 2), S(OpenBgSlotRole.Bomber, 1, 2), S(OpenBgSlotRole.Pet, 0, 1) }),
            ["stealth4"] = new("stealth4", 4, new[] { S(OpenBgSlotRole.Assassin, 2, 3), S(OpenBgSlotRole.Archer, 0, 1) }),
            ["small"] = new("small", 4, new[] { S(OpenBgSlotRole.Healer, 1, 1), S(OpenBgSlotRole.AreaCc, 1, 1), S(OpenBgSlotRole.Melee, 1, 2) }),
            ["duo"] = new("duo", 2, new[] { S(OpenBgSlotRole.Healer, 0, 1), S(OpenBgSlotRole.Melee, 0, 1) }),
        };

        public static OpenBgArchetype Get(string key) =>
            key != null && Catalog.TryGetValue(key.Trim(), out OpenBgArchetype archetype) ? archetype : null;

        public static IReadOnlySet<eCharacterClass> ClassesFor(OpenBgSlotRole role) => Roles[role];

        /// <summary>"std8:2,caster8:1": unknown keys and bad counts are skipped; empty or null gives no entries.</summary>
        public static IReadOnlyList<(OpenBgArchetype Archetype, int Count)> ParseMix(string text)
        {
            var result = new List<(OpenBgArchetype, int)>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] pair = part.Split(':', 2, StringSplitOptions.TrimEntries);
                OpenBgArchetype archetype = pair.Length == 2 ? Get(pair[0]) : null;
                if (archetype != null && int.TryParse(pair[1], out int count) && count > 0)
                    result.Add((archetype, Math.Min(count, 8)));
            }
            return result;
        }
    }
}
