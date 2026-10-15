using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    public enum OpenBgSlotRole { Healer, AreaCc, Speed, Tank, Melee, Bomber, Caster, Assassin, Archer, Pet, Flex }

    public sealed record OpenBgSlot(OpenBgSlotRole Role, int Min, int Wish);

    public sealed record OpenBgArchetype(string Key, int Size, OpenBgSlot[] Slots);

    /// <summary>The candidate indexes a fill picked (the forced one first) and the roles whose minimum could not be met.</summary>
    public sealed record OpenBgFill(int[] PickedIndexes, OpenBgSlotRole[] MissedMinimums);

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

        /// <summary>
        /// The archetype for the unit with this ordinal among the units of <paramref name="size"/>: the entries of that size
        /// expanded in listed order, each repeated by its count, indexed by ordinal modulo the length (never negative).
        /// Null when no listed archetype has that size.
        /// </summary>
        public static OpenBgArchetype PickForSize(IReadOnlyList<(OpenBgArchetype Archetype, int Count)> mix, int size, long ordinal)
        {
            var expanded = new List<OpenBgArchetype>();
            foreach ((OpenBgArchetype archetype, int count) in mix)
                if (archetype.Size == size)
                    for (int i = 0; i < count; i++) expanded.Add(archetype);
            if (expanded.Count == 0) return null;
            long index = ordinal % expanded.Count;
            if (index < 0) index += expanded.Count;
            return expanded[(int)index];
        }

        /// <summary>
        /// Slots first (min, then wish) in the listed order, each filled from the first unpicked candidate whose class is in
        /// the role; the group is then topped up to its size from what is left, in candidate order (the caller sorts by
        /// distance). <paramref name="forcedIndex"/> is a candidate that is in the group regardless (the seed leader): it is
        /// picked first and counts toward every slot whose role fits its class. <paramref name="mode"/> is reserved for
        /// the Per realm filter, which the roster applies by passing only candidates of one realm.
        /// </summary>
        public static OpenBgFill Fill(OpenBgArchetype archetype, IReadOnlyList<eCharacterClass> candidates, OpenBgMode mode, int forcedIndex = -1)
        {
            var picked = new List<int>();
            var missed = new List<OpenBgSlotRole>();
            if (forcedIndex >= 0 && forcedIndex < candidates.Count) picked.Add(forcedIndex);
            foreach (OpenBgSlot slot in archetype.Slots)
            {
                IReadOnlySet<eCharacterClass> classes = ClassesFor(slot.Role);
                // Members already picked for an earlier slot count here too (a Minstrel is area CC and speed at once).
                int have = picked.Count(index => classes.Contains(candidates[index]));
                for (int pass = have; pass < slot.Wish && picked.Count < archetype.Size; pass++)
                {
                    int index = -1;
                    for (int i = 0; i < candidates.Count; i++)
                        if (!picked.Contains(i) && classes.Contains(candidates[i])) { index = i; break; }
                    if (index < 0) break;
                    picked.Add(index);
                    have++;
                }
                if (have < slot.Min) missed.Add(slot.Role);
            }
            for (int i = 0; i < candidates.Count && picked.Count < archetype.Size; i++)
                if (!picked.Contains(i)) picked.Add(i);
            return new OpenBgFill(picked.ToArray(), missed.ToArray());
        }
    }
}
