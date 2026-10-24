namespace DOL.GS.Spells
{
    /// <summary>
    /// Scarab proc spell handler
    /// Snare and morph target. Cecity is a subspell.
    /// The snare is applied here as a MaxSpeed multiplier on the artifact effect, not as a
    /// MovementSpeedDebuff: the core breaks every MovementSpeedDebuff on damage except
    /// the UnbreakableSpeedDecrease type, and this one is meant to hold. Spell.Value is the
    /// snare percentage (0 = morph only).
    /// </summary>
    [SpellHandler(eSpellType.ScarabProc)]
    public class ScarabProc : UnbreakableSpeedDecreaseSpellHandler, IArtifactEffectHandler
    {
        public ScarabProc(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ArtifactSpellEffect.Create(initParams);
        }

        // SpeedDecreaseSpellHandler conflicts with everything because it relies on its own
        // effect bucket. Ours shares the eEffect.Unknown bucket with other artifact effects,
        // so only conflict with another Scarab curse.
        public override bool HasConflictingEffectWith(ISpellHandler compare)
        {
            return ArtifactSpells.ConflictsWith(this, compare);
        }

        public void OnArtifactEffectStart(ECSGameSpellEffect effect)
        {
            if (Spell.Value > 0)
            {
                effect.Owner.BuffBonusMultCategory1.Set((int) eProperty.MaxSpeed, effect, 1.0 - Spell.Value * 0.01);
                effect.Owner.OnMaxSpeedChange();
            }

            // Official model 1200.
            ArtifactSpells.SetMorphModel(effect.Owner as GamePlayer, Spell.LifeDrainReturn);
        }

        public void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            if (Spell.Value > 0)
            {
                effect.Owner.BuffBonusMultCategory1.Remove((int) eProperty.MaxSpeed, effect);
                effect.Owner.OnMaxSpeedChange();
            }

            if (Spell.LifeDrainReturn > 0)
                ArtifactSpells.RestoreModel(effect.Owner as GamePlayer);
        }
    }
}
