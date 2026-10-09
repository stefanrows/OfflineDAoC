using System;
using DOL.AI.Brain;

namespace DOL.GS.Spells
{
	[SpellHandler(eSpellType.DirectDamage)]
	public class DirectDamageSpellHandler : SpellHandler
	{
        // Tooltip handlers have no spell line, so recognize the shipped aura IDs there too.
        // The Soulrending check also covers additional ranks without matching localized names.
        private bool IsReaverDamageAura =>
            Spell.SpellType is eSpellType.DirectDamage &&
            Spell.IsPBAoE && Spell.IsPulsing && Spell.DamageType is eDamageType.Spirit &&
            (SpellLine?.Spec == Specs.Soulrending || Spell.ID is >= 9641 and <= 9649);

        // Rank scaling keeps an old low-level aura from gaining a full-level tank's bonus.
        private long ReaverAuraBonusThreat => Math.Max(1, Spell.Level) * 4L;

        public override string ShortDescription => IsReaverDamageAura
            ? $"Inflicts {Spell.Damage} {Spell.DamageTypeToString()} damage to nearby enemies. " +
              "Each damaging pulse adds extra threat against NPCs and mobs, scaled by the spell's learned level. " +
              "Does not taunt players, playerbots or controlled pets. Taunt styles may still be needed to hold aggro."
            : $"Inflicts {Spell.Damage} {Spell.DamageTypeToString()} damage to the target.";

		public DirectDamageSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

		/// <summary>
		/// Execute direct damage spell
		/// </summary>
		/// <param name="target"></param>
		public override void FinishSpellCast(GameLiving target)
		{
			m_caster.Mana -= PowerCost(target);
			base.FinishSpellCast(target);
		}

		/// <summary>
		/// Calculates the base 100% spell damage which is then modified by damage variance factors
		/// </summary>
		/// <returns></returns>
		public override double CalculateDamageBase(GameLiving target)
		{
			GamePlayer player = Caster as GamePlayer;

			// % damage procs
			if (Spell.Damage < 0)
			{
				double spellDamage = 0;

				if (player != null)
				{
					// This equation is used to simulate live values - Tolakram
					spellDamage = (target.MaxHealth * -Spell.Damage * .01) / 2.5;
				}

				if (spellDamage < 0)
					spellDamage = 0;

				return spellDamage;
			}

			return base.CalculateDamageBase(target);
		}

		public override double DamageCap(double effectiveness)
		{
			if (Spell.Damage < 0)
			{
				return (Target.MaxHealth * -Spell.Damage * .01) * 3.0 * effectiveness;
			}

			return base.DamageCap(effectiveness);
		}

		public override void OnDirectEffect(GameLiving target)
		{
			if (target == null)
				return;

			// 1.65 compliance. No LoS check on PBAoE or AoE spells.
			if (Spell.Target is eSpellTarget.CONE)
			{
				if (!Caster.castingComponent.StartEndOfCastLosCheck(target, this))
					DealDamage(target);
			}
			else
				DealDamage(target);
		}

		public override void OnEndOfCastLosCheck(GameLiving target, LosCheckResponse response)
		{
			if (response is LosCheckResponse.True)
				DealDamage(target);
		}

        public override void DamageTarget(AttackData ad, bool showEffectAnimation, int attackResult)
        {
            base.DamageTarget(ad, showEffectAnimation, attackResult);

            if (!IsReaverDamageAura || !ad.GeneratesAggro || ad.IsSpellResisted ||
                (long) ad.Damage + ad.CriticalDamage <= 0 ||
                ad.Target is not GameNPC npc || npc is GameBot ||
                !npc.IsAlive || npc.ObjectState is not GameObject.eObjectState.Active ||
                npc.Brain is IControlledBrain || npc.Brain is not IOldAggressiveBrain brain)
                return;

            // Add to normal threat: no forced target switch or catch-up to the highest threat.
            // Use the brain API so Protect and existing encounter rules still apply.
            brain.AddToAggroList(Caster, ReaverAuraBonusThreat);
        }

		protected virtual void DealDamage(GameLiving target)
		{
			if (!target.IsAlive || target.ObjectState is not GameObject.eObjectState.Active)
				return;

			AttackData ad = CalculateDamageToTarget(target);
			SendDamageMessages(ad);
			DamageTarget(ad, true);
			target.StartInterruptTimer(target.SpellInterruptDuration, ad.AttackType, Caster);
		}
	}
}
