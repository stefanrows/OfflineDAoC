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
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
	/// <summary>
	/// The scholars of Atlantis who awaken artifacts (author Aredhel, DOLSharp; rewritten
	/// data-driven for Offline DAoC, TOA_PLAN WP3.1b). Every scholar awakens every artifact
	/// that has a scroll set: our Artifact rows name no scholar (ScholarID is the numeric
	/// placeholder), so no artifact is tied to one NPC. The rules and every text live in
	/// <see cref="ArtifactAwakening"/>; docs/TOA_ARTIFACTS.md "Earning an artifact".
	/// The class name is stored in Mob.ClassType (pack 020) and must not change.
	/// </summary>
	public class Scholar : Researcher
	{
		public Scholar()
			: base() { }

		/// <summary>Whether this scholar awakens the artifacts that have no scroll set (Artifact Scholars).</summary>
		public virtual bool AwakensWithoutBook => false;

		public override bool Interact(GamePlayer player)
		{
			if (!base.Interact(player))
				return false;

			SayTo(player, eChatLoc.CL_PopupWindow, ArtifactAwakening.Greeting(player, this));
			return true;
		}

		public override bool WhisperReceive(GameLiving source, string text)
		{
			if (!base.WhisperReceive(source, text))
				return false;

			if (source is not GamePlayer player)
				return false;

			string reply = ArtifactAwakening.Whisper(player, this, text);

			if (reply != null)
				SayTo(player, eChatLoc.CL_PopupWindow, reply);

			return true;
		}

		public override bool ReceiveItem(GameLiving source, DbInventoryItem item)
		{
			if (source is GamePlayer player && item != null)
			{
				string reply = ArtifactAwakening.HandIn(player, this, item, out bool handled);

				if (reply != null)
					SayTo(player, eChatLoc.CL_PopupWindow, reply);

				if (handled)
					return true;
			}

			return base.ReceiveItem(source, item);
		}
	}
}
