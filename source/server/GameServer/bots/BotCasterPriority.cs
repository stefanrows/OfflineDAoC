using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    public static class BotCasterPriority
    {
        public static bool IsDamage(Spell spell) => spell != null &&
            (spell.Damage > 0 || spell.SpellType is eSpellType.DirectDamage or eSpellType.Lifedrain or
                eSpellType.Bolt or eSpellType.DirectDamageWithDebuff or eSpellType.DamageSpeedDecrease);

        public static bool AllowInstant(Spell spell, bool rangedCaster, bool meleePressure) =>
            !rangedCaster || IsDamage(spell) || meleePressure;

        public static bool IsDurationDamageOrDebuff(Spell spell) => spell != null && spell.Duration > 0 &&
            (IsDamage(spell) || spell.SpellType == eSpellType.Disease ||
             spell.SpellType.ToString().Contains("Debuff", StringComparison.OrdinalIgnoreCase));

        /// <summary>Applies an eligible damage-over-time/debuff effect once before rotating direct damage.</summary>
        public static Spell ChooseDurationApplication(IEnumerable<Spell> candidates, Func<Spell, bool> hasEffect) =>
            candidates?.Where(IsDurationDamageOrDebuff)
                .Where(spell => hasEffect == null || !hasEffect(spell))
                .OrderByDescending(spell => spell.Level)
                .ThenByDescending(spell => spell.Damage)
                .FirstOrDefault();

        /// <summary>
        /// Selects among the currently legal damage options in proportion to
        /// the selected build's learned spell-line ranks. A line without a
        /// listed allocation retains a small fallback weight so secondary
        /// skills remain usable. Call only after higher-level CC, bomb, and
        /// selected ranged-AoE decisions have been made.
        /// </summary>
        public static Spell ChoosePlanDamageSpell(
            IEnumerable<Spell> candidates,
            Func<Spell, string> resolveSpec,
            IEnumerable<CompanionBuildRank> allocations,
            Random random = null)
        {
            if (candidates == null || resolveSpec == null || allocations == null)
                return null;

            Dictionary<string, int> rankBySpec = allocations
                .Where(rank => rank != null && !string.IsNullOrWhiteSpace(rank.Specialization) && rank.Level > 0)
                .GroupBy(rank => rank.Specialization, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Max(rank => rank.Level),
                    StringComparer.OrdinalIgnoreCase);

            var groups = candidates.Where(IsDamage)
                .Select((spell, index) => new
                {
                    Spell = spell,
                    Index = index,
                    Spec = resolveSpec(spell),
                })
                .GroupBy(entry => string.IsNullOrWhiteSpace(entry.Spec)
                    ? $"#spell:{entry.Spell.ID}:{entry.Index}"
                    : entry.Spec,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    Spec = group.First().Spec,
                    FirstIndex = group.Min(entry => entry.Index),
                    Weight = !string.IsNullOrWhiteSpace(group.First().Spec) &&
                        rankBySpec.TryGetValue(group.First().Spec, out int rank) ? rank : 1,
                    Spells = group.Select(entry => entry.Spell).ToArray(),
                })
                .OrderByDescending(group => group.Weight)
                .ThenBy(group => group.FirstIndex)
                .ToArray();

            if (groups.Length == 0)
                return null;

            int totalWeight = groups.Sum(group => group.Weight);
            int roll = (random ?? Random.Shared).Next(totalWeight);
            foreach (var group in groups)
            {
                if (roll < group.Weight)
                    return group.Spells.OrderByDescending(spell => spell.Level)
                        .ThenByDescending(spell => spell.Damage).First();
                roll -= group.Weight;
            }

            return null;
        }
    }
}
