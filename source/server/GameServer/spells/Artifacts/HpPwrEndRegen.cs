/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */

using System;

namespace DOL.GS.Spells
{
	/// <summary>
	/// Health, power and endurance regeneration over time: every pulse restores 5 % of
	/// each pool (Spell.Frequency sets the pulse interval).
	/// </summary>
	[SpellHandler(eSpellType.HpPwrEndRegen)]
	public class HpPwrEndRegenSpellHandler : SpellHandler, IArtifactEffectHandler
	{
		private const double TICK_FRACTION = 0.05;

		public override string ShortDescription => "Regenerates 5% of the target's health, power and endurance at every pulse.";

		public HpPwrEndRegenSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

		public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
		{
			return ArtifactSpellEffect.Create(initParams);
		}

		public void OnArtifactEffectStart(ECSGameSpellEffect effect) { }

		public void OnArtifactEffectStop(ECSGameSpellEffect effect) { }

		public void OnArtifactEffectPulse(ECSGameSpellEffect effect)
		{
			OnDirectEffect(effect.Owner);
		}

		public override void OnDirectEffect(GameLiving target)
		{
			if (target.ObjectState != GameObject.eObjectState.Active) return;
			if (target.IsAlive == false) return;

			target.Endurance += Math.Min(target.MaxEndurance - target.Endurance, (int)(target.MaxEndurance * TICK_FRACTION));
			target.Health += Math.Min(target.MaxHealth - target.Health, (int)(target.MaxHealth * TICK_FRACTION));
			target.Mana += Math.Min(target.MaxMana - target.Mana, (int)(target.MaxMana * TICK_FRACTION));
		}
	}
}
