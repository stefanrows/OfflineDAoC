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
using DOL.Events;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Alvarus spell handler: underwater shape change, ends when the wearer leaves the water.
    /// Water breathing is a subspell.
    /// </summary>
    [SpellHandler(eSpellType.AlvarusMorph)]
    public class AlvarusMorph : Morph
    {
        public AlvarusMorph(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override void ApplyEffectOnTarget(GameLiving target)
        {
            if (target is not GamePlayer targetPlayer)
                return;

            if (!targetPlayer.IsUnderwater)
            {
                MessageToCaster("You must be under water to use this ability.", eChatType.CT_SpellResisted);
                return;
            }

            if (ArtifactSpells.HasOtherMorph(targetPlayer, Spell))
            {
                targetPlayer.Out.SendMessage("You already have an active morph!", eChatType.CT_SpellResisted, eChatLoc.CL_ChatWindow);
                return;
            }

            base.ApplyEffectOnTarget(target);
        }

        public override void OnArtifactEffectStart(ECSGameSpellEffect effect)
        {
            base.OnArtifactEffectStart(effect);

            if (effect.Owner is GamePlayer player)
                GameEventMgr.AddHandler(player, GamePlayerEvent.SwimmingStatus, new DOLEventHandler(SwimmingStatusChange));
        }

        public override void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            if (effect.Owner is GamePlayer player)
                GameEventMgr.RemoveHandler(player, GamePlayerEvent.SwimmingStatus, new DOLEventHandler(SwimmingStatusChange));

            base.OnArtifactEffectStop(effect);
        }

        private void SwimmingStatusChange(DOLEvent e, object sender, EventArgs args)
        {
            if (sender is not GamePlayer player || player.IsUnderwater)
                return;

            ArtifactSpells.FindEffect(player, this)?.End();
        }
    }
}
