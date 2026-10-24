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
namespace DOL.GS
{
	/// <summary>
	/// The Artifact Scholars (Jarron and Heiron in Albion, Alaria and Erelacen in Midgard,
	/// Elmer in Hibernia). They awaken every artifact a scholar does, and also the three whose
	/// story was never written down (A Flask, Bruiser, Shield of Khaos), on encounter credit
	/// alone. DOLSharp used them for its version exchange (ArtifactTurnInQuest), which is not
	/// ported: it relied on DOL's "dmg;wep;stat" Version keys, ours are 0..15.
	/// The class name is stored in Mob.ClassType (pack 020) and must not change.
	/// </summary>
	public class ArtifactScholar : Scholar
	{
		public ArtifactScholar()
			: base() { }

		public override bool AwakensWithoutBook => true;
	}
}
