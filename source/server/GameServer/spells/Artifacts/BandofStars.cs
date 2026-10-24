using System;

namespace DOL.GS.Spells
{
    [SpellHandler(eSpellType.StarsProc)]
    public class StarsProc : SpellHandler
    {
        public StarsProc(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override bool CheckBeginCast(GameLiving selectedTarget)
        {
            return base.CheckBeginCast(selectedTarget);
        }

        public override bool StartSpell(GameLiving target)
        {
            foreach (GameLiving targ in SelectTargets(target))
            {
                DealDamage(targ);
            }

            return true;
        }

        private void DealDamage(GameLiving target)
        {
            int ticksToTarget = m_caster.GetDistanceTo(target) * 100 / 85; // 85 units per 1/10s
            int delay = 1 + ticksToTarget / 100;
            foreach (GamePlayer player in target.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
            {
                player.Out.SendSpellEffectAnimation(m_caster, target, m_spell.ClientEffect, (ushort)(delay), false, 1);
            }
            BoltOnTargetAction bolt = new BoltOnTargetAction(Caster, target, this);
            bolt.Start(1 + ticksToTarget);
        }

        protected class BoltOnTargetAction : ECSGameTimerWrapperBase
        {
            protected readonly GameLiving m_boltTarget;
            protected readonly StarsProc m_handler;
            
            public BoltOnTargetAction(GameLiving actionSource, GameLiving boltTarget, StarsProc spellHandler) : base(actionSource)
            {
                if (boltTarget == null)
                    throw new ArgumentNullException("boltTarget");
                if (spellHandler == null)
                    throw new ArgumentNullException("spellHandler");
                m_boltTarget = boltTarget;
                m_handler = spellHandler;
            }

            protected override int OnTick(ECSGameTimer timer)
            {
                GameLiving target = m_boltTarget;
                GameLiving caster = (GameLiving) timer.Owner;

                if (target == null || !target.IsAlive || target.ObjectState != GameObject.eObjectState.Active || target.CurrentRegionID != caster.CurrentRegionID)
                    return 0;

                AttackData ad = m_handler.CalculateDamageToTarget(target);
                ad.Damage = (int)m_handler.Spell.Damage;
                m_handler.SendDamageMessages(ad);
                m_handler.DamageTarget(ad, false);

                //if (m_handler.Spell.SubSpellID != 0) Spell subspell = m_handler.SkillBase.GetSpellByID(m_handler.Spell.SubSpellID);
                if (m_handler.Spell.SubSpellID != 0 && SkillBase.GetSpellByID(m_handler.Spell.SubSpellID) != null)
                {
                    ISpellHandler spellhandler = ScriptMgr.CreateSpellHandler(caster, SkillBase.GetSpellByID(m_handler.Spell.SubSpellID), SkillBase.GetSpellLine("Mob Spells"));
                    spellhandler?.StartSpell(target);
                }

                target.StartInterruptTimer(target.SpellInterruptDuration, AttackData.eAttackType.Spell, caster);
                return 0;
            }
        }
    }

    /// <summary>
    /// Band of Stars "Baleful Shape Change": all stats debuff that also turns a player
    /// target into the model in LifeDrainReturn.
    /// </summary>
    [SpellHandler(eSpellType.StarsProc2)]
    public class StarsProc2 : SpellHandler, IArtifactEffectHandler
    {
        public override string ShortDescription => $"Turns the target into a lesser form and decreases its stats by {Spell.Value}.";

        public StarsProc2(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

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
            ArtifactSpells.SetMorphModel(effect.Owner as GamePlayer, Spell.LifeDrainReturn);
        }

        public void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            Apply(effect.Owner, -(int) Spell.Value);

            if (Spell.LifeDrainReturn > 0)
                ArtifactSpells.RestoreModel(effect.Owner as GamePlayer);
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
