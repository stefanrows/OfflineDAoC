namespace DOL.GS.Spells
{
    /// <summary>
    /// Maddening Scalars shape change with an offensive proc
    /// (official model 102, taken from LifeDrainReturn).
    /// </summary>
    [SpellHandler(eSpellType.MaddeningScalars)]
    public class MaddeningScalars : ArtifactMorphProcSpellHandler
    {
        public MaddeningScalars(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
}
