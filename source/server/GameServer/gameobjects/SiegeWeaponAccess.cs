using System.Collections.Generic;
using DOL.GS.ServerRules;

namespace DOL.GS
{
	/// <summary>
	/// Who may control, push, fire and board a siege weapon (bug 151). On
	/// Camlann a group, guild, or battlegroup mixes birth realms, so the old
	/// "same realm as the weapon" rule locked allies out of their own ram.
	/// Under PvP rules anyone allied (<see cref="PvpCombatant.AreAllied"/>) with
	/// the weapon's live anchors (its founder and its latest controller) may use
	/// it, regardless of realm, and everyone else is locked out. Other server
	/// rules keep the plain realm comparison. The weapon's own
	/// <see cref="GameLiving.Realm"/> is untouched: packets and keep door logic
	/// still read it.
	/// </summary>
	public static class SiegeWeaponAccess
	{
		/// <param name="pvpRules">True under PvPServerRules (Camlann).</param>
		/// <param name="weaponRealm">The weapon's own Realm.</param>
		/// <param name="candidate">The would-be operator or rider.</param>
		/// <param name="liveAnchors">Founder and latest controller that are still in the world.</param>
		/// <param name="anchorGuildId">Guild of the founder when the weapon was founded.</param>
		/// <param name="anchorGuildName">Name of that guild (the pet dummy guild is never an alliance).</param>
		public static bool MayUse(bool pvpRules, eRealm weaponRealm, GameLiving candidate,
			IReadOnlyCollection<GameLiving> liveAnchors, string anchorGuildId, string anchorGuildName)
		{
			if (candidate == null)
				return false;

			if (!pvpRules)
				return candidate.Realm == weaponRealm;

			if (liveAnchors != null)
				foreach (GameLiving anchor in liveAnchors)
					if (anchor != null && (candidate == anchor || PvpCombatant.AreAllied(candidate, anchor)))
						return true;

			// Somebody who owns the weapon is around and the candidate is not
			// allied with them: locked out, even if of the same birth realm.
			if (liveAnchors != null && liveAnchors.Count > 0)
				return false;

			// Nobody owns it any more (founder logged out or never recorded):
			// the founder's guild, then the weapon's realm, may use it.
			Guild guild = PvpCombatant.GuildOf(candidate);
			if (PvpCombatant.AreGuildIdsAllied(anchorGuildId, anchorGuildName, guild?.GuildID, guild?.Name))
				return true;

			return candidate.Realm == weaponRealm;
		}
	}
}
