namespace DOL.GS.Spells
{
    [SpellHandler(eSpellType.ShatterIllusions)]
	public class ShatterIllusions : SpellHandler
	{
        //Shatter Illusions 
        //(returns the enemy from their shapeshift forms 
        //causing 200 body damage to the enemy. Range: 1500) 
        public override void OnDirectEffect(GameLiving target)
        {
            base.OnDirectEffect(target);

            if (ArtifactSpells.EndMorphs(target) == 0)
                return;

            AttackData ad = CalculateDamageToTarget(target);
            ad.Damage = (int)Spell.Damage;
            SendEffectAnimation(target, 0, false, 1);
            SendDamageMessages(ad);
            DamageTarget(ad);
        }

        public virtual void DamageTarget(AttackData ad)
        {
            ad.AttackResult = eAttackResult.HitUnstyled;
            ad.Target.OnAttackedByEnemy(ad);
            ad.Attacker.DealDamage(ad);
            foreach (GamePlayer player in ad.Attacker.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
            {
                player.Out.SendCombatAnimation(null, ad.Target, 0, 0, 0, 0, 0x0A, ad.Target.HealthPercent);
            }
        }

        public ShatterIllusions(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
	}
}
