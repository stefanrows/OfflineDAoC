using System;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Health, power and endurance regeneration (Kalare's Necklace 30184 "Kalare Regeneration",
    /// Eternal Plant 30182 "Plant Regeneration"). Every pulse (Spell.Frequency) restores, of each
    /// pool, Spell.Value percent of its maximum when Value is negative (the DOL convention for
    /// percentage heals) or Value points when it is positive. Kalare's row, -5 every 5 seconds for
    /// 30 seconds, gives six pulses: 30 % over 30 seconds, as players described the necklace
    /// ("P/H/F regen ... used by Kalare's necklace as 30%"). No implementation of this type exists
    /// in DOL or OpenDAoC; the mob, pet and bot brains already pick it as a buff.
    /// </summary>
    [SpellHandler(eSpellType.PowerHealthEnduranceRegenBuff)]
    public class PowerHealthEnduranceRegenBuff : SpellHandler, IArtifactEffectHandler
    {
        public PowerHealthEnduranceRegenBuff(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override string ShortDescription => Spell.Value < 0
            ? $"Restores {-Spell.Value}% of the target's health, power and endurance every {Spell.Frequency / 1000.0:0.#} seconds."
            : $"Restores {Spell.Value} health, power and endurance every {Spell.Frequency / 1000.0:0.#} seconds.";

        public override bool HasPositiveEffect => true;

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ArtifactSpellEffect.Create(initParams);
        }

        public void OnArtifactEffectStart(ECSGameSpellEffect effect)
        {
            // Without a pulse interval the row would do nothing; restore once instead.
            if (Spell.Frequency <= 0)
                Restore(effect.Owner);
        }

        public void OnArtifactEffectStop(ECSGameSpellEffect effect) { }

        public void OnArtifactEffectPulse(ECSGameSpellEffect effect)
        {
            Restore(effect.Owner);
        }

        /// <summary>One pulse on <paramref name="target"/>.</summary>
        public void Restore(GameLiving target)
        {
            if (target == null || !target.IsAlive || target.ObjectState != GameObject.eObjectState.Active)
                return;

            target.Health += Math.Max(0, Math.Min(target.MaxHealth - target.Health, Amount(target.MaxHealth)));
            target.Mana += Math.Max(0, Math.Min(target.MaxMana - target.Mana, Amount(target.MaxMana)));
            target.Endurance += Math.Max(0, Math.Min(target.MaxEndurance - target.Endurance, Amount(target.MaxEndurance)));
        }

        public int Amount(int maximum)
        {
            return Spell.Value < 0 ? (int) (maximum * -Spell.Value * 0.01) : (int) Spell.Value;
        }
    }
}
