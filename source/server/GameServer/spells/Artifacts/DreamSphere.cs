using System;
using DOL.Events;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Dream Sphere self morph spell handler
    /// The DoT proc is a subspell, affects only caster
    /// </summary>
    
    //the self dream-morph doesnt break on damage/attacked by enemy only grp-target 1 does
    [SpellHandler(eSpellType.DreamMorph)]
    public class DreamMorph : ArtifactMorphProcSpellHandler
    {
        public DreamMorph(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
    }
    
    /// <summary>
    /// Dream Sphere group morph spell handler
    /// The DoT proc is a subspell, affects only caster
    /// </summary> 

    //http://support.darkageofcamelot.com/kb/article.php?id=745
    //- The Panther Form level 10 ability of the Dreamsphere artifact has been changed.
    //When a character in panther form is attacked, they revert to normal form and lose all associated bonuses. 
    //This change is specific to the Dreamsphere only and does not affect other shapechange forms

    //http://www.daoc-toa.net/img/dreamPrey.jpg
    //http://www.daoc-toa.net/img/dreamCat.jpg
    [SpellHandler(eSpellType.DreamGroupMorph)]
    public class DreamGroupMorph : DreamMorph
    {
        public DreamGroupMorph(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override void OnArtifactEffectStart(ECSGameSpellEffect effect)
        {
            base.OnArtifactEffectStart(effect);

            // Any wearer, so companions (GameBot) lose the form too.
            GameEventMgr.AddHandler(effect.Owner, GameLivingEvent.AttackedByEnemy, new DOLEventHandler(LivingAttacked));
        }

        public override void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            GameEventMgr.RemoveHandler(effect.Owner, GameLivingEvent.AttackedByEnemy, new DOLEventHandler(LivingAttacked));

            base.OnArtifactEffectStop(effect);
        }

        // Event: the wearer is attacked, the form breaks.
        private void LivingAttacked(DOLEvent e, object sender, EventArgs args)
        {
            if (sender is GameLiving living)
                ArtifactSpells.FindEffect(living, this)?.End();
        }
    }
}
