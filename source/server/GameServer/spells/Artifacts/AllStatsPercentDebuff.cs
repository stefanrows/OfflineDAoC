using System.Collections.Generic;

namespace DOL.GS.Spells
{
    /// <summary>
    /// All stats percent debuff spell handler ("Vision of The Malice").
    /// The amounts are computed from the target's current stats when the effect starts
    /// and kept per effect, so an area cast removes exactly what it applied on each target
    /// (upstream kept one set of fields per handler and removed the last target's values
    /// from everyone).
    /// </summary>
    [SpellHandler(eSpellType.AllStatsPercentDebuff)]
    public class AllStatsPercentDebuff : SpellHandler, IArtifactEffectHandler
    {
        private static readonly eProperty[] Stats =
        [
            eProperty.Strength,
            eProperty.Dexterity,
            eProperty.Constitution,
            eProperty.Empathy,
            eProperty.Quickness,
            eProperty.Intelligence,
            eProperty.Charisma,
            eProperty.Piety,
        ];

        private readonly Dictionary<ECSGameSpellEffect, int[]> _applied = new();

        public override string ShortDescription => $"Decreases the target's stats by {Spell.Value}%.";

        public AllStatsPercentDebuff(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

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
            GameLiving owner = effect.Owner;
            double percent = Spell.Value / 100;
            int[] amounts = new int[Stats.Length];

            for (int i = 0; i < Stats.Length; i++)
            {
                amounts[i] = (int) (owner.GetModified(Stats[i]) * percent);
                ArtifactSpells.AddDebuff(owner, Stats[i], amounts[i]);
            }

            lock (_applied)
                _applied[effect] = amounts;

            ArtifactSpells.RefreshStats(owner);
        }

        public void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            int[] amounts;

            lock (_applied)
            {
                if (!_applied.Remove(effect, out amounts))
                    return;
            }

            for (int i = 0; i < Stats.Length; i++)
                ArtifactSpells.AddDebuff(effect.Owner, Stats[i], -amounts[i]);

            ArtifactSpells.RefreshStats(effect.Owner);
        }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            base.ApplyEffectOnTarget(target);
            ArtifactSpells.MarkHostile(Caster, target);
        }
    }
}
