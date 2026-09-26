using System;
using System.Collections.Generic;
using DOL.Database;

namespace DOL.GS
{
	/// <summary>
	/// Supplies standard AllRealmsTeleporter destinations when an installed database
	/// does not contain their default route rows.
	/// </summary>
	internal static class AllRealmsTeleportFallbacks
	{
		// Destination names exposed by AllRealmsTeleporter's capital, mainland,
		// and Shrouded Isles town menus. Keep dungeon and frontier routes out of
		// autonomous pickup-group travel.
		private static readonly string[] AlbionTownRoutes =
		[
			"Camelot", "Holtham", "Cotswold Village", "Prydwen Keep", "Caer Ulfwych",
			"Campacorentin Station", "Adribard's Retreat", "Cornwall Station", "Swanton Keep",
			"Lyonesse", "Dartmoor", "Caer Gothwaite", "Wearyall Village", "Fort Gwyntell", "Caer Diogel",
		];

		private static readonly string[] MidgardTownRoutes =
		[
			"Jordheim", "Hafheim", "Mularn", "Fort Veldon", "Audliten", "Huginfell", "Fort Atla",
			"Gna Faste", "Vindsaul Faste", "Raumarik", "Malmohus", "Aegirhamn", "Bjarken", "Hagall", "Knarr",
		];

		private static readonly string[] HiberniaTownRoutes =
		[
			"Tir na Nog", "Fintain", "Mag Mell", "Tir na mBeo", "Ardagh", "Howth", "Connla",
			"Innis Carthaig", "Druim Cain", "Cursed Forest", "Sheeroe Hills", "Domnann", "Droighaid",
			"Aalid Feie", "Necht",
		];

		// Mirrors the default, untyped route rows in DOL's public Teleport.json.
		private static readonly Dictionary<string, (string TeleportID, int RegionID, int X, int Y, int Z, int Heading)> Routes = new(StringComparer.OrdinalIgnoreCase)
		{
			["1:Camelot"] = ("Camelot", 10, 36230, 29846, 7970, 11),
			["1:Oceanus"] = ("Oceanus", 73, 271184, 539600, 8344, 645),
			["1:Darkness Falls"] = ("Darkness Falls", 1, 599451, 536187, 2960, 3076),
			["1:Forest Sauvage"] = ("Forest Sauvage", 1, 589500, 472400, 3350, 2815),
			["1:Castle Sauvage"] = ("Castle Sauvage", 1, 583913, 487012, 2184, 2048),
			["1:Snowdonia Fortress"] = ("Snowdonia Fortress", 1, 516801, 373238, 8208, 1757),
			["1:Holtham"] = ("Holtham", 27, 94940, 91818, 5024, 1803),
			["1:Cotswold Village"] = ("Cotswold Village", 1, 560467, 511652, 2344, 3398),
			["1:Prydwen Keep"] = ("Prydwen Keep", 1, 574199, 528948, 2863, 4076),
			["1:Caer Ulfwych"] = ("Caer Ulfwych", 1, 521253, 616481, 1785, 3607),
			["1:Campacorentin Station"] = ("Campacorentin Station", 1, 493679, 591770, 1819, 95),
			["1:Adribard's Retreat"] = ("Adribard's Retreat", 1, 472348, 629103, 1724, 2500),
			["1:Cornwall Station"] = ("Cornwall Station", 1, 408907, 652791, 4944, 1937),
			["1:Swanton Keep"] = ("Swanton Keep", 1, 512118, 381746, 7992, 1080),
			["1:Lyonesse"] = ("Lyonesse", 1, 348239, 667789, 5865, 957),
			["1:Dartmoor"] = ("Dartmoor", 1, 388710, 699379, 3280, 37),
			["1:Inconnu Crypt"] = ("Inconnu Crypt", 65, 32900, 35568, 16353, 31),
			["1:Tomb of Mithra"] = ("Tomb of Mithra", 21, 33149, 32721, 16480, 2043),
			["1:Keltoi Fogou"] = ("Keltoi Fogou", 22, 30120, 31216, 16521, 3102),
			["1:Tepok's Mine"] = ("Tepok's Mine", 24, 32588, 34776, 15179, 2182),
			["1:Catacombs of Cardova"] = ("Catacombs of Cardova", 23, 31121, 29955, 16239, 3048),
			["1:Stonehenge Barrows"] = ("Stonehenge Barrows", 20, 31215, 34298, 16495, 2059),
			["1:Krondon"] = ("Krondon", 61, 32772, 31379, 15725, 1017),
			["1:Avalon City"] = ("Avalon City", 50, 31138, 47311, 8313, 2026),
			["1:Caer Sidi"] = ("Caer Sidi", 60, 31666, 35986, 18639, 4084),
			["1:Caer Gothwaite"] = ("Caer Gothwaite", 51, 535158, 548589, 4800, 463),
			["1:Wearyall Village"] = ("Wearyall Village", 51, 434585, 493128, 3088, 1043),
			["1:Fort Gwyntell"] = ("Fort Gwyntell", 51, 426851, 416460, 5712, 1295),
			["1:Caer Diogel"] = ("Caer Diogel", 51, 403748, 503110, 4680, 1106),
			["1:Avalon Marsh"] = ("Avalon Marsh", 1, 462144, 633058, 1739, 1769),
			["1:Gothwaite"] = ("Gothwaite", 51, 535158, 548589, 4805, 463),
			["1:Wearyall"] = ("Wearyall", 51, 434585, 493128, 3088, 1043),
			["1:Gwyntell"] = ("Gwyntell", 51, 426851, 416460, 5712, 1295),
			["1:Diogel"] = ("Diogel", 51, 403748, 503110, 4680, 1106),

			["2:Jordheim"] = ("Jordheim", 101, 31986, 27564, 8800, 2048),
			["2:Oceanus"] = ("Oceanus", 30, 271184, 539600, 8344, 645),
			["2:Darkness Falls"] = ("Darkness Falls", 100, 760784, 700729, 6271, 1619),
			["2:Uppland"] = ("Uppland", 100, 766200, 663000, 5840, 1934),
			["2:Svasud Faste"] = ("Svasud Faste", 100, 765694, 673509, 5736, 1115),
			["2:Vindsaul Faste"] = ("Vindsaul Faste", 100, 704829, 738364, 5704, 773),
			["2:Hafheim"] = ("Hafheim", 27, 226050, 223025, 5056, 2146),
			["2:Mularn"] = ("Mularn", 100, 803612, 726671, 4743, 2659),
			["2:Fort Veldon"] = ("Fort Veldon", 100, 801046, 678588, 5299, 1036),
			["2:Audliten"] = ("Audliten", 100, 729152, 760225, 4573, 42),
			["2:Huginfell"] = ("Huginfell", 100, 712192, 783970, 4672, 3120),
			["2:Fort Atla"] = ("Fort Atla", 100, 749218, 817547, 4408, 2058),
			["2:Gna Faste"] = ("Gna Faste", 100, 787729, 903903, 4744, 517),
			["2:Raumarik"] = ("Raumarik", 100, 660618, 764955, 4613, 947),
			["2:Malmohus"] = ("Malmohus", 100, 730233, 971212, 4476, 651),
			["2:Kobold Undercity"] = ("Kobold Undercity", 243, 31320, 28930, 16398, 1741),
			["2:Nisse's Lair"] = ("Nisse's Lair", 129, 34693, 33173, 16467, 1019),
			["2:Cursed Tomb"] = ("Cursed Tomb", 128, 30156, 31233, 16517, 3068),
			["2:Vendo Caverns"] = ("Vendo Caverns", 126, 32776, 33062, 16618, 2024),
			["2:Varulvhamn"] = ("Varulvhamn", 127, 35267, 30852, 14995, 1043),
			["2:Spindelhalla"] = ("Spindelhalla", 125, 32151, 31813, 16371, 3067),
			["2:Iarnvidiur's Lair"] = ("Iarnvidiur's Lair", 161, 34797, 37123, 17043, 2043),
			["2:Trollheim"] = ("Trollheim", 150, 28325, 47589, 15999, 3075),
			["2:Tuscaren Glacier"] = ("Tuscaren Glacier", 160, 34860, 17945, 18826, 1263),
			["2:Aegirhamn"] = ("Aegirhamn", 151, 293910, 356255, 3488, 1199),
			["2:Bjarken"] = ("Bjarken", 151, 289954, 301796, 4160, 2660),
			["2:Hagall"] = ("Hagall", 151, 379380, 384696, 7752, 31),
			["2:Knarr"] = ("Knarr", 151, 302623, 433312, 3204, 3065),
			["2:Gotar"] = ("Gotar", 100, 771152, 836380, 4624, 364),

			["3:Tir na Nog"] = ("Tir na Nog", 201, 33415, 31336, 7999, 2048),
			["3:Oceanus"] = ("Oceanus", 130, 271184, 539600, 8344, 645),
			["3:Darkness Falls"] = ("Darkness Falls", 200, 325044, 433663, 6315, 3629),
			["3:Cruachan Gorge"] = ("Cruachan Gorge", 200, 338700, 412500, 5950, 2616),
			["3:Druim Ligen"] = ("Druim Ligen", 200, 334342, 419994, 5184, 2309),
			["3:Druim Cain"] = ("Druim Cain", 200, 421264, 486315, 1824, 2013),
			["3:Fintain"] = ("Fintain", 27, 357012, 353642, 5056, 1894),
			["3:Mag Mell"] = ("Mag Mell", 200, 347811, 490351, 5210, 50),
			["3:Tir na mBeo"] = ("Tir na mBeo", 200, 345698, 528897, 5448, 905),
			["3:Ardagh"] = ("Ardagh", 200, 350446, 553634, 5120, 2794),
			["3:Howth"] = ("Howth", 200, 343184, 592636, 5456, 1339),
			["3:Connla"] = ("Connla", 200, 295765, 642599, 4849, 2343),
			["3:Innis Carthaig"] = ("Innis Carthaig", 200, 334622, 720123, 4296, 1712),
			["3:Cursed Forest"] = ("Cursed Forest", 200, 446983, 525356, 6448, 3076),
			["3:Sheeroe Hills"] = ("Sheeroe Hills", 200, 358460, 710997, 4912, 2000),
			["3:Shar Labyrinth"] = ("Shar Labyrinth", 93, 24307, 27567, 17537, 4059),
			["3:Muire Tomb"] = ("Muire Tomb", 221, 31055, 29865, 16239, 4055),
			["3:Spraggon Den"] = ("Spraggon Den", 222, 32722, 34761, 15179, 1933),
			["3:Koalinth Caverns"] = ("Koalinth Caverns", 223, 27336, 32266, 17266, 3064),
			["3:Treibh Caillte"] = ("Treibh Caillte", 224, 35267, 30879, 14999, 1065),
			["3:Coruscating Mine"] = ("Coruscating Mine", 220, 33502, 33658, 16046, 1027),
			["3:Tur Suil"] = ("Tur Suil", 190, 38037, 27698, 13247, 5),
			["3:Fomor"] = ("Fomor", 180, 33745, 24206, 16073, 550),
			["3:Galladoria"] = ("Galladoria", 191, 32068, 29565, 17042, 57),
			["3:Domnann"] = ("Domnann", 181, 423089, 444756, 5959, 2060),
			["3:Droighaid"] = ("Droighaid", 181, 379081, 420975, 5528, 1159),
			["3:Aalid Feie"] = ("Aalid Feie", 181, 313726, 352686, 3592, 747),
			["3:Necht"] = ("Necht", 181, 428182, 320796, 3411, 3104),
			["3:Shannon Estuary"] = ("Shannon Estuary", 200, 309968, 645164, 4848, 1137),
		};

		public static DbTeleport Get(eRealm realm, string teleportID)
		{
			if (string.IsNullOrWhiteSpace(teleportID) ||
				!Routes.TryGetValue($"{(int)realm}:{teleportID.Trim()}", out var route))
				return null;

			return new DbTeleport
			{
				Type = string.Empty,
				TeleportID = route.TeleportID,
				Realm = (int)realm,
				RegionID = route.RegionID,
				X = route.X,
				Y = route.Y,
				Z = route.Z,
				Heading = route.Heading,
			};
		}

		/// <summary>
		/// Standard AllRealmsTeleporter town menu IDs, excluding dungeons and
		/// frontier destinations. Autonomous NPC travel uses this same route
		/// catalog and resolves each ID from the installed table before this fallback.
		/// </summary>
		public static IReadOnlyList<string> GetTownRouteIDs(eRealm realm) => realm switch
		{
			eRealm.Albion => AlbionTownRoutes,
			eRealm.Midgard => MidgardTownRoutes,
			eRealm.Hibernia => HiberniaTownRoutes,
			_ => Array.Empty<string>(),
		};
	}
}
