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
namespace DOL.GS.Spells
{
    /// <summary>
    ///[Freya] Nidel: Harpy Cloak
    /// They have less chance of landing melee attacks, and spells have a greater chance of affecting them.
    /// Note: Spell to hit code is located in CalculateToHitChance method in SpellHandler.cs
    /// </summary>
    [SpellHandler(eSpellType.HarpyFeatherCloak)]
    public class HarpyFeatherCloak : SingleStatDebuff, IArtifactEffectHandler
    {
        public HarpyFeatherCloak(GameLiving caster, Spell spell, SpellLine line)
            : base(caster, spell, line)
        {
        }

        // Bridged artifact effects share the eEffect.Unknown bucket; the stock debuff base
        // conflicts with everything there. Only another spell of the same type conflicts.
        public override bool HasConflictingEffectWith(ISpellHandler compare)
        {
            return ArtifactSpells.ConflictsWith(this, compare);
        }

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ArtifactSpellEffect.Create(initParams);
        }

        public virtual void OnArtifactEffectStart(ECSGameSpellEffect effect)
        {
            ArtifactSpellEffect.ApplyProperties(this, effect.Owner, (int) (Spell.Value * effect.Effectiveness), false);
            ArtifactSpells.RefreshStats(effect.Owner, resists: true);
        }

        public virtual void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            ArtifactSpellEffect.ApplyProperties(this, effect.Owner, (int) (Spell.Value * effect.Effectiveness), true);
            ArtifactSpells.RefreshStats(effect.Owner, resists: true);
        }

        /// <summary>
        /// This is melee to hit penalty
        /// </summary>
        public override eProperty Property1
        {
            get { return eProperty.ToHitBonus; }
        }
    }
}