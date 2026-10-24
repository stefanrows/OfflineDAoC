using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Gem of Lost Memories, level 5 "Recovery" (spell 30187, instant, 15 minute reuse): cures the
    /// target of resurrection illness, the PvE and the RvR kind, as a healer NPC does.
    /// Live: "Recovery - removes res sickness (active) (instant)" (IGN Vault template post, 2004);
    /// the row says "Target recovers from the effects of resurrection". The cast is refused while
    /// there is no illness, so the charge is not spent for nothing. No implementation of this type
    /// exists in DOL or OpenDAoC.
    /// </summary>
    [SpellHandler(eSpellType.YouthIllness)]
    public class YouthIllness : SpellHandler
    {
        public YouthIllness(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override string ShortDescription => "Cures the target of resurrection illness.";

        public override bool HasPositiveEffect => true;

        public override bool CheckBeginCast(GameLiving selectedTarget, bool quiet)
        {
            GameLiving target = Spell.Target is eSpellTarget.SELF ? Caster : selectedTarget ?? Caster;

            if (!HasResurrectionIllness(target))
            {
                if (!quiet)
                    MessageToCaster("You are not suffering from resurrection illness.", eChatType.CT_SpellResisted);

                return false;
            }

            return base.CheckBeginCast(selectedTarget, quiet);
        }

        public override void OnDirectEffect(GameLiving target)
        {
            if (Cure(target) > 0)
                MessageToLiving(target, "You recover from the effects of resurrection.", eChatType.CT_Spell);
        }

        public static bool HasResurrectionIllness(GameLiving target)
        {
            if (target?.effectListComponent == null)
                return false;

            foreach (ECSGameEffect effect in target.effectListComponent.GetEffects())
            {
                if ((effect.EffectType is eEffect.ResurrectionIllness or eEffect.RvrResurrectionIllness) && !effect.IsEnding)
                    return true;
            }

            return false;
        }

        /// <summary>Ends every resurrection illness on <paramref name="target"/>; returns how many.</summary>
        public static int Cure(GameLiving target)
        {
            if (target?.effectListComponent == null)
                return 0;

            int cured = 0;

            foreach (ECSGameEffect effect in target.effectListComponent.GetEffects())
            {
                if ((effect.EffectType is eEffect.ResurrectionIllness or eEffect.RvrResurrectionIllness) && effect.End())
                    cured++;
            }

            return cured;
        }
    }
}
