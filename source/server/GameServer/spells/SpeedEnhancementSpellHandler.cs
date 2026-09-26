using System;
using System.Collections.Generic;
using DOL.Database;
using DOL.GS.Effects;
using DOL.GS.Keeps;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
	/// <summary>
	/// Increases the target's movement speed.
	/// </summary>
	[SpellHandler(eSpellType.SpeedEnhancement)]
	public class SpeedEnhancementSpellHandler : SpellHandler
	{
		public override string ShortDescription => $"The target's speed is increased to {Spell.Value}% of normal.";

		public SpeedEnhancementSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

		private bool TryReportBlockedHastenerCast(GameLiving target)
		{
			if (Spell.ID != GameHastener.SPEEDOFTHEREALMID ||
				Caster is not GameHastener and not FrontierHastener || target is not GamePlayer player)
				return false;

			string translationKey = null;

			if (player.InCombat)
				translationKey = "GameHastener.SpeedBlockedCombat";
			else if (player.IsRiding)
				translationKey = "GameHastener.SpeedBlockedRiding";
			else if (player.EffectList.GetOfType<ChargeEffect>() != null ||
				player.TempProperties.GetProperty<bool>("Charging") ||
				player.EffectList.GetOfType<ArmsLengthEffect>() != null ||
				player.effectListComponent.ContainsEffectForEffectType(eEffect.SpeedOfSound))
				translationKey = "GameHastener.SpeedBlockedMovementEffect";
			else if (player.IsStealthed)
				translationKey = "GameHastener.SpeedBlockedStealthed";
			else if (player.effectListComponent.GetSpellEffects(eEffect.MovementSpeedBuff).Exists(effect =>
				effect.IsActive && !effect.IsEnding && effect.SpellHandler?.Spell != null &&
				effect.SpellHandler.Spell.Value * effect.Effectiveness >= Spell.Value * CasterEffectiveness))
				translationKey = "GameHastener.SpeedBlockedStronger";

			if (translationKey == null)
				return false;

			GameHastener.SendSpeedBlockMessage(player, translationKey);
			return true;
		}

		public override void FinishSpellCast(GameLiving target)
		{
			bool isHastenerCast = Spell.ID == GameHastener.SPEEDOFTHEREALMID &&
				Caster is GameHastener or FrontierHastener;

			if (isHastenerCast && TryReportBlockedHastenerCast(target))
				return;

			Caster.Mana -= PowerCost(target);
			base.FinishSpellCast(target);
		}

		public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
		{
			return ECSGameEffectFactory.Create(initParams, static (in i) => new SpeedEnhancementECSEffect(i));
		}

		protected override int CalculateEffectDuration(GameLiving target)
		{
			double duration = Spell.Duration;
			duration *= (1.0 + m_caster.GetModified(eProperty.SpellDuration) * 0.01);
			if (Spell.InstrumentRequirement != 0)
			{
				DbInventoryItem instrument = Caster.ActiveWeapon;
				if (instrument != null)
				{
					duration *= 1.0 + Math.Min(1.0, instrument.Level / (double)Caster.Level); // up to 200% duration for songs
					duration *= instrument.Quality * 0.01 * instrument.ConditionPercent * 0.01;
				}
			}
			
			if (duration < 1)
				duration = 1;
			else if (duration > (Spell.Duration * 4))
				duration = (Spell.Duration * 4);
			return (int)duration;
		}

		public override void ApplyEffectOnTarget(GameLiving target)
		{
			if (target.EffectList.GetOfType<ChargeEffect>() != null)
				return;

			if (target.TempProperties.GetProperty<bool>("Charging"))
				return;

			if (target.EffectList.GetOfType<ArmsLengthEffect>() != null)
				return;

			if (target.effectListComponent.ContainsEffectForEffectType(eEffect.SpeedOfSound))
				return;

			if (target is GamePlayer && (target as GamePlayer).IsRiding)
				return;

			if (target is Keeps.GameKeepGuard)
				return;

			// Graveen: archery speed shot
			if ((Spell.Pulse != 0 || Spell.CastTime != 0) && target.InCombat)
			{
				MessageToLiving(target, "You've been in combat recently, the spell has no effect on you!", eChatType.CT_SpellResisted);
				return;
			}
			base.ApplyEffectOnTarget(target);
		}

		/// <summary>
		/// Delve Info
		/// </summary>
		public override IList<string> DelveInfo
		{
			get
			{
				/*
				<Begin Info: Motivation Sng>
 
				The movement speed of the target is increased.
 
				Target: Group
				Range: 2000
				Duration: 30 sec
				Frequency: 6 sec
				Casting time:      3.0 sec
				
				This spell's effect will not take hold while the target is in combat.
				<End Info>
				*/
				IList<string> list = base.DelveInfo;

				list.Add(" "); //empty line
				list.Add("This spell's effect will not take hold while the target is in combat.");

				return list;
			}
		}
	}
}
