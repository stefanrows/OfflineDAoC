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
        public static bool Wants(Spell spell, ICharacterClass targetClass, bool encumbered, int freeConcentrationAfterCast)
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
                eSpellType.StrengthBuff => !IsStrengthlessCaster(targetClass) || encumbered ||
                                           freeConcentrationAfterCast >= spell.Concentration,
                _ => true
            };
        }

        // Casters and the healers that do not melee in a group. Valewalker
        // and Vampiir are list casters that fight in melee.
        private static bool IsStrengthlessCaster(ICharacterClass characterClass) =>
            characterClass.ClassType == eClassType.ListCaster &&
            characterClass.ID != (int)eCharacterClass.Valewalker &&
            characterClass.ID != (int)eCharacterClass.Vampiir ||
            (eCharacterClass)characterClass.ID is eCharacterClass.Cleric or eCharacterClass.Healer or
                eCharacterClass.Druid or eCharacterClass.Shaman or eCharacterClass.Bard;
    }
}
