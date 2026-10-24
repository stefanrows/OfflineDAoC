namespace DOL.GS.Spells
{
    // The AllStatsDebuff handler that upstream also defines in this file lives in
    // AllStatsDebuff.cs; a second [SpellHandler] for the same type would make the
    // registry's pick undefined.

    /// <summary>
    /// Lore debuff spell handler (magic resist and spell damage debuff)
    /// </summary>
    [SpellHandler(eSpellType.LoreDebuff)]
    public class LoreDebuff : SpellHandler, IArtifactEffectHandler
    {
        private static readonly eProperty[] Properties =
        [
            eProperty.SpellDamage,
            eProperty.Resist_Heat,
            eProperty.Resist_Cold,
            eProperty.Resist_Matter,
            eProperty.Resist_Spirit,
            eProperty.Resist_Energy,
        ];

        public override string ShortDescription => $"Decreases the target's spell damage and magic resistances by {Spell.Value}.";

        public LoreDebuff(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ArtifactSpellEffect.Create(initParams);
        }

        public void OnArtifactEffectStart(ECSGameSpellEffect effect)
        {
            ArtifactSpells.AddDebuffToAll(effect.Owner, Properties, (int) Spell.Value);
            ArtifactSpells.RefreshStats(effect.Owner, resists: true);
        }

        public void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            ArtifactSpells.AddDebuffToAll(effect.Owner, Properties, -(int) Spell.Value);
            ArtifactSpells.RefreshStats(effect.Owner, resists: true);
        }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            base.ApplyEffectOnTarget(target);
            ArtifactSpells.MarkHostile(Caster, target);
        }
    }

    /// <summary>
    /// Strength/Constitution drain spell handler: debuffs the target's strength and
    /// constitution and gives the same amount to the caster for the duration.
    /// </summary>
    [SpellHandler(eSpellType.StrengthConstitutionDrain)]
    public class StrengthConstitutionDrain : StrengthConDebuff, IArtifactEffectHandler
    {
        public override string ShortDescription => $"Drains {Spell.Value} strength and constitution from the target to the caster.";

        public StrengthConstitutionDrain(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        // Bridged artifact effects share the eEffect.Unknown bucket; the stock debuff base
        // conflicts with everything there. Only another spell of the same type conflicts.
        public override bool HasConflictingEffectWith(ISpellHandler compare)
        {
            return ArtifactSpells.ConflictsWith(this, compare);
        }

        public override double CalculateSpellResistChance(GameLiving target)
        {
            return 0;
        }

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ArtifactSpellEffect.Create(initParams);
        }

        public void OnArtifactEffectStart(ECSGameSpellEffect effect)
        {
            ArtifactSpellEffect.ApplyProperties(this, effect.Owner, (int) (Spell.Value * effect.Effectiveness), false);
            Caster.BaseBuffBonusCategory[eProperty.Strength] += (int) Spell.Value;
            Caster.BaseBuffBonusCategory[eProperty.Constitution] += (int) Spell.Value;
            ArtifactSpells.RefreshStats(effect.Owner);
            ArtifactSpells.RefreshStats(Caster);
        }

        public void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            ArtifactSpellEffect.ApplyProperties(this, effect.Owner, (int) (Spell.Value * effect.Effectiveness), true);
            Caster.BaseBuffBonusCategory[eProperty.Strength] -= (int) Spell.Value;
            Caster.BaseBuffBonusCategory[eProperty.Constitution] -= (int) Spell.Value;
            ArtifactSpells.RefreshStats(effect.Owner);
            ArtifactSpells.RefreshStats(Caster);
        }
    }

    /// <summary>
    /// ABS Damage shield spell handler: a melee ablative that returns the absorbed damage
    /// to the attacker.
    /// </summary>
    [SpellHandler(eSpellType.ABSDamageShield)]
    public class ABSDamageShield : AblativeArmorSpellHandler
    {
        public ABSDamageShield(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ECSGameEffectFactory.Create(initParams, static (in i) => new ArtifactAblativeEffect(i));
        }

        public override void OnDamageAbsorbed(AttackData ad, int DamageAmount)
        {
            if (DamageAmount <= 0 || ad.Attacker == null || !ad.Attacker.IsAlive)
                return;

            AttackData newad = new AttackData();
            newad.Attacker = ad.Target;
            newad.Target = ad.Attacker;
            newad.Damage = DamageAmount;
            newad.DamageType = Spell.DamageType;
            newad.AttackType = AttackData.eAttackType.Spell;
            newad.AttackResult = eAttackResult.HitUnstyled;
            newad.SpellHandler = this;
            newad.Target.OnAttackedByEnemy(newad);
            newad.Attacker.DealDamage(newad);
        }
    }

    /// <summary>
    /// Morph spell handler: self shape change to the model in LifeDrainReturn.
    /// </summary>
    [SpellHandler(eSpellType.Morph)]
    public class Morph : SpellHandler, IArtifactEffectHandler
    {
        public Morph(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ArtifactSpellEffect.Create(initParams);
        }

        public virtual void OnArtifactEffectStart(ECSGameSpellEffect effect)
        {
            ArtifactSpells.SetMorphModel(effect.Owner as GamePlayer, Spell.LifeDrainReturn);
        }

        public virtual void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            ArtifactSpells.RestoreModel(effect.Owner as GamePlayer);
        }
    }

    /// <summary>
    /// Arcane leadership spell handler (range + resist pierce)
    /// </summary>
    [SpellHandler(eSpellType.ArcaneLeadership)]
    public class ArcaneLeadership : CloudsongAuraSpellHandler
    {
        public ArcaneLeadership(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
}
