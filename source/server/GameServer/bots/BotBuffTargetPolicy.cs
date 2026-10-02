using System;

namespace DOL.GS
{
    /// <summary>
    /// Which members a buffer spends a single-target class buff on. It reads
    /// only the target's class data, so every realm hands out the same rules
    /// (also against PvP opponents' groups).
    /// <para>
    /// Owner 2026-10-02: optional buffs go out while concentration lasts; when
    /// a newcomer needs a required buff and concentration runs short, the
    /// buffer takes back an optional one from a bot. Real players always get
    /// every buff that has an effect, first, and never lose one.
    /// </para>
    /// </summary>
    public static class BotBuffTargetPolicy
    {
        public enum Need
        {
            /// <summary>The buff does nothing on this member.</summary>
            None,
            /// <summary>Nice to have; the first thing taken back for a newcomer.</summary>
            Optional,
            Required
        }

        /// <param name="targetClass">The member's class; null (pets, NPCs) takes everything.</param>
        /// <param name="isPlayer">A real player: every effective buff is required.</param>
        /// <param name="encumbered">The member carries more than it can move freely.</param>
        /// <param name="highestWeaponSpec">The member's highest trained weapon specialization.</param>
        /// <param name="level">The member's level.</param>
        public static Need NeedOf(Spell spell, ICharacterClass targetClass, bool isPlayer, bool encumbered,
            int highestWeaponSpec = 0, int level = 50)
        {
            if (spell == null || targetClass == null)
                return Need.Required;

            return spell.SpellType switch
            {
                // StatCalculator adds acuity to the casting stat of list casters
                // only; hybrids, healers and tanks gain nothing from it.
                eSpellType.AcuityBuff => targetClass.ClassType == eClassType.ListCaster &&
                                         targetClass.ManaStat != eStat.UNDEFINED
                    ? Need.Required
                    : Need.None,
                // A caster's strength only moves its carrying capacity.
                eSpellType.StrengthBuff => isPlayer || encumbered ||
                                           !IsStrengthlessCaster(targetClass, highestWeaponSpec, level)
                    ? Need.Required
                    : Need.Optional,
                _ => Need.Required
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
