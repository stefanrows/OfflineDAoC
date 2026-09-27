using System;

namespace DOL.GS
{
    /// <summary>
    /// Bot affordability shares caster damage, buff and pet-summon cost reductions with player spell costs.
    /// Only native settlement rolls free-cast effects, never AI evaluation.
    /// Quickcast is never used by a GameBot.
    /// </summary>
    public static class BotSpellPower
    {
        private const double OffensiveCasterDamageCostMultiplier = 0.3;
        private const double BuffAndPetSummonManaCostMultiplier = 0.1;

        public static bool IsOffensiveCaster(GameBot bot) => bot != null && IsOffensiveCaster(bot.CharacterClass);

        private static bool IsOffensiveCaster(ICharacterClass characterClass) =>
            characterClass?.ClassType == eClassType.ListCaster &&
            characterClass.ID is not (int)eCharacterClass.Valewalker and not (int)eCharacterClass.Vampiir;

        public static double ApplyBuffAndPetSummonCostReduction(Spell spell, double cost) =>
            HasNinetyPercentCostReduction(spell) ? cost * BuffAndPetSummonManaCostMultiplier : cost;

        private static bool HasNinetyPercentCostReduction(Spell spell) =>
            spell is { IsBuff: true } || IsPetSummoningSpell(spell);

        private static bool IsPetSummoningSpell(Spell spell) => spell?.SpellType is
            eSpellType.Bomber or
            eSpellType.Pet or
            eSpellType.SummonCommander or
            eSpellType.SummonTheurgistPet or
            eSpellType.Summon or
            eSpellType.SummonJuggernaut or
            eSpellType.SummonMinion or
            eSpellType.SummonSimulacrum or
            eSpellType.SummonUnderhill or
            eSpellType.SummonAnimistAmbusher or
            eSpellType.SummonAnimistPet or
            eSpellType.SummonDruidPet or
            eSpellType.SummonHealingElemental or
            eSpellType.SummonHunterPet or
            eSpellType.SummonAnimistFnF or
            eSpellType.SummonAnimistFnFCustom or
            eSpellType.SummonSpiritFighter or
            eSpellType.SummonNecroPet or
            eSpellType.SummonNoveltyPet or
            eSpellType.AstralPetSummon or
            eSpellType.SummonElemental or
            eSpellType.SummonTitan;

        public static double ApplyDamageCostReduction(ICharacterClass characterClass, Spell spell, double cost) =>
            !HasNinetyPercentCostReduction(spell) && IsOffensiveCaster(characterClass) &&
            spell is { IsHarmful: true } && BotCasterPriority.IsDamage(spell)
                ? cost * OffensiveCasterDamageCostMultiplier
                : cost;

        // A focus root channels until canceled, spending power while preventing
        // the aggressive caster rotation. Use ordinary roots/DoTs/nukes instead.
        // Human spell selection and damaging channels are unaffected.
        public static bool BlocksAttackerRotation(GameBot bot, Spell spell) =>
            IsOffensiveCaster(bot) && spell?.IsFocus == true &&
            spell.IsHarmful && spell.Damage <= 0;

        // A native channeled spell pays on each real pulse, not every AI tick.
        // Preserve the spell's native upkeep cost for bots and players alike.
        public static int PulseCost(GameLiving caster, Spell spell) => spell.PulsePower;

        public static int Cost(GameBot bot, Spell spell, SpellLine line)
        {
            if (spell == null) return 0;
            // These native handlers use health/endurance instead of mana.
            if (bot.CharacterClass?.ID == (int)eCharacterClass.Savage ||
                spell.SpellType is eSpellType.Archery)
                return 0;

            line = bot.ResolvePowerSpellLine(spell, line);
            double cost = spell.Power;
            if (cost < 0)
            {
                int basePool = bot.CharacterClass?.ManaStat is eStat stat && stat != eStat.UNDEFINED
                    ? bot.CalculateMaxMana(bot.Level, bot.GetBaseStat(stat))
                    : bot.MaxMana;
                cost *= basePool * -0.01;
            }

            if (bot.CharacterClass?.IsFocusCaster == true && line != null)
            {
                eProperty focusProperty = SkillBase.SpecToFocus(line.Spec);
                if (focusProperty != eProperty.Undefined)
                {
                    double focus = bot.GetModified(focusProperty) * 0.4;
                    if (spell.Level > 0) focus /= spell.Level;
                    focus = Math.Clamp(focus, 0, 0.4);
                    focus *= Math.Min(1, bot.GetModifiedSpecLevel(line.Spec) / (double)Math.Max(1, spell.Level));
                    cost *= 1.2 - focus;
                }
            }

            cost = ApplyBuffAndPetSummonCostReduction(spell, cost);
            cost = ApplyDamageCostReduction(bot.CharacterClass, spell, cost);

            return Math.Max(0, (int)cost);
        }
    }
}
