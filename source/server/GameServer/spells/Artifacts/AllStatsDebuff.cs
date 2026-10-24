namespace DOL.GS.Spells
{
    /// <summary>
    /// All stats debuff spell handler (Foppish Sleeves "Stupefy").
    /// Upstream defines this handler twice (here in DOL.GS.Spells.Atlantis and again in
    /// CommonArtifactSpells.cs); the registry would pick either at random, so this is the
    /// single merged copy.
    /// </summary>
    [SpellHandler(eSpellType.AllStatsDebuff)]
    public class AllStatsDebuff : SpellHandler, IArtifactEffectHandler
    {
        public override string ShortDescription => $"Decreases the target's stats by {Spell.Value}.";

        public AllStatsDebuff(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

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
            Apply(effect.Owner, (int) Spell.Value);
        }

        public void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            Apply(effect.Owner, -(int) Spell.Value);
        }

        private static void Apply(GameLiving owner, int value)
        {
            ArtifactSpells.AddDebuffToAll(owner, ArtifactSpells.AllStats, value);
            ArtifactSpells.AddDebuff(owner, eProperty.PhysicalAbsorption, value);
            ArtifactSpells.AddDebuff(owner, eProperty.MagicAbsorption, value);
            ArtifactSpells.RefreshStats(owner);
        }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            base.ApplyEffectOnTarget(target);
            ArtifactSpells.MarkHostile(Caster, target);
        }
    }
}
