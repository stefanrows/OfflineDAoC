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
        /// Offline DAoC: the core's CastSubSpells hands the parent spell to its single-cast
        /// helper, whose "no chained sub spell" guard then returns at once, so a plain
        /// SubSpellID never casts. Cast it here: Shield of Khaos "Disoriented" (30125) brings
        /// the energy damage over time 30126. Like the core, a sub spell with its own sub spell
        /// is not cast (no chains).
        /// </summary>
        public override void CastSubSpells(GameLiving target)
        {
            if (target == null || Spell.SubSpellID <= 0)
                return;

            Spell subSpell = SkillBase.GetSpellByID(Spell.SubSpellID);

            if (subSpell == null || subSpell.SubSpellID != 0)
                return;

            if (Caster is GameNPC npc)
                subSpell = npc.GetScaledSpell(subSpell);

            CreateSubSpellHandler(subSpell)?.StartSpell(target);
        }

        /// <summary>The handler that casts the sub spell; the same spell line as the core uses.</summary>
        protected virtual ISpellHandler CreateSubSpellHandler(Spell subSpell)
        {
            return ScriptMgr.CreateSpellHandler(Caster, subSpell, SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells));
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