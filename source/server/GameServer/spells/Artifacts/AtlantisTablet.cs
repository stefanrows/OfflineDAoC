namespace DOL.GS.Spells
{
    /// <summary>
    /// Atlantis Tablet shape change with an offensive proc.
    /// </summary>
    [SpellHandler(eSpellType.AtlantisTabletMorph)]
    public class AtlantisTabletMorph : ArtifactMorphProcSpellHandler
    {
        public AtlantisTabletMorph(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
}
