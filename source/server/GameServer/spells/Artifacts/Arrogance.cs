namespace DOL.GS.Spells.Atlantis
{
    /// <summary>
    /// Arrogance spell handler (Eirene's Hauberk): raises every stat for the duration,
    /// then lowers them by the same amount for ten seconds.
    /// </summary>
    [SpellHandler(eSpellType.Arrogance)]
    public class Arrogance : SpellHandler, IArtifactEffectHandler
    {
        private const int AFTERMATH_DURATION = 10000;

        public override string ShortDescription => $"Increases the target's stats by {Spell.Value}, then decreases them by the same amount for {AFTERMATH_DURATION / 1000} seconds.";

        public Arrogance(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ArtifactSpellEffect.Create(initParams);
        }

        public void OnArtifactEffectStart(ECSGameSpellEffect effect)
        {
            Buff(effect.Owner, (int) Spell.Value);
        }

        public void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            GameLiving owner = effect.Owner;
            int value = (int) Spell.Value;
            Buff(owner, -value);

            // A refresh is not the end of the buff; only the real expiry brings the slump.
            if (effect.IsBeingReplaced || owner is not GamePlayer player || !player.IsAlive)
                return;

            Slump(player, value);
            new ECSGameTimer(player, timer =>
            {
                Slump(player, -value);
                return 0;
            }, AFTERMATH_DURATION);
        }

        private static void Buff(GameLiving owner, int value)
        {
            ArtifactSpells.AddToAll(owner.BaseBuffBonusCategory, ArtifactSpells.AllStats, value);
            owner.BaseBuffBonusCategory[eProperty.PhysicalAbsorption] += value;
            ArtifactSpells.RefreshStats(owner);
        }

        private static void Slump(GameLiving owner, int value)
        {
            ArtifactSpells.AddDebuffToAll(owner, ArtifactSpells.AllStats, value);
            ArtifactSpells.AddDebuff(owner, eProperty.PhysicalAbsorption, value);
            ArtifactSpells.RefreshStats(owner);
        }
    }
}
