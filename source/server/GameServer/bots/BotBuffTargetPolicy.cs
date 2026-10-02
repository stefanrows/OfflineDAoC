using System;

namespace DOL.GS
{
    /// <summary>
    /// Which members a buffer spends a single-target class buff on. It reads
    /// only the target's class data, so every realm hands out the same rules
    /// (also against PvP opponents' groups).
    /// </summary>
    public static class BotBuffTargetPolicy
    {
        /// <param name="targetClass">The member's class; null (pets, NPCs) takes everything.</param>
        /// <param name="encumbered">The member carries more than it can move freely.</param>
        /// <param name="freeConcentrationAfterCast">Buffer's concentration left once this buff is paid.</param>
        /// <param name="highestWeaponSpec">The member's highest trained weapon specialization.</param>
        /// <param name="level">The member's level.</param>
        public static bool Wants(Spell spell, ICharacterClass targetClass, bool encumbered, int freeConcentrationAfterCast,
            int highestWeaponSpec = 0, int level = 50)
        {
            if (spell == null || targetClass == null)
                return true;

            return spell.SpellType switch
            {
                // StatCalculator adds acuity to the casting stat of list casters
                // only; hybrids, healers and tanks gain nothing from it.
                eSpellType.AcuityBuff => targetClass.ClassType == eClassType.ListCaster &&
                                         targetClass.ManaStat != eStat.UNDEFINED,
                // A caster's strength only moves its carrying capacity: it gets
                // the buff when it is overloaded, or when the buffer still keeps
                // enough concentration for another buff of the same size.
                eSpellType.StrengthBuff => !IsStrengthlessCaster(targetClass, highestWeaponSpec, level) || encumbered ||
                                           freeConcentrationAfterCast >= spell.Concentration,
                _ => true
            };
        }

        /// <summary>A healer with a weapon trained to half its level or more fights in melee.</summary>
        public static bool IsMeleeSpec(int highestWeaponSpec, int level) =>
            highestWeaponSpec > 1 && highestWeaponSpec * 2 >= Math.Max(2, level);

        // Casters, and healers that do not melee: whether a Cleric, Druid or
        // Bard fights is a spec choice (owner 2026-10-02). Valewalker and
        // Vampiir are list casters that fight in melee.
        private static bool IsStrengthlessCaster(ICharacterClass characterClass, int highestWeaponSpec, int level) =>
            characterClass.ClassType == eClassType.ListCaster &&
            characterClass.ID != (int)eCharacterClass.Valewalker &&
            characterClass.ID != (int)eCharacterClass.Vampiir ||
            ((eCharacterClass)characterClass.ID is eCharacterClass.Cleric or eCharacterClass.Healer or
                 eCharacterClass.Druid or eCharacterClass.Shaman or eCharacterClass.Bard) &&
            !IsMeleeSpec(highestWeaponSpec, level);
    }
}
