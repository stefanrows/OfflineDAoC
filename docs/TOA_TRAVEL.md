# Trials of Atlantis: travel and loot

Packages WP1.2 (travel) and WP1.3 (random loot inside Atlantis) of
[TOA_PLAN.md](TOA_PLAN.md). Status: implemented in source, real-client check
pending. Nothing here has been seen in a game client yet.

## How a player gets in

Every porter with the full travel menu now shows one more line, as long as the
account may enter Atlantis (`atlantis_teleport_plvl`, see below):

```
[Atlantis] [Atlantis Havens] [Atlantis Dungeons]
```

That includes the capital porters next to the Realm Exchange that `/tc`
leads to, the town and frontier porters, and the new channelers inside
Atlantis. Technically it is every NPC of class `AllRealmsTeleporter` or a
subclass (`LiveTeleporter`, `InlandTeleporter`, `AlbionTeleporter`,
`MidgardTeleporter`, `HiberniaTeleporter`).

- **[Atlantis]** ports to the ruins city of the player's realm. The city holds
  the Arbiter (Master Levels), Demyphon, the scholars, class trainers, a vault
  keeper and a healer. The player lands at the arrival point of the city's
  entrance from Oceanus Hesperos, beside the Arbiter.
- **[Atlantis Havens]** lists the four havens of the realm's Atlantis copy.
- **[Atlantis Dungeons]** lists six dungeon entrances of that copy.

The destination always follows the character's realm, not the porter's realm.
In the Camlann Server PvP mode a character of any realm gets that realm's
copy. The older lines `[Albion Oceanus]`, `[Midgard Oceanus]` and
`[Hibernia Oceanus]` stay as they were. They are realm-open: anyone may pick
any copy, and they land at that copy's Haven of Oceanus.

The porter casts the usual portal spell, as for every other destination.
Companions follow their owner into Atlantis through the region-change hook,
as they do everywhere. Other real players in the group are not moved; that is
how every porter line works.

| Realm | Ruins city (region) | Overworld copy | Great Pyramid, City of Aerus |
|---|---|---|---|
| Albion | Ruins of Atlantis (70) | 73 | 88, 90 |
| Midgard | Ruinrar Atlantis (71) | 30 | 45, 47 |
| Hibernia | Scrios de Atlantis (72) | 130 | 145, 147 |

The realm of each copy comes from the zone points of pack 022. The doors of
each ruins city lead into the same realm's overworld copy.

## Travel inside Atlantis

| Destination | Where | Landing |
|---|---|---|
| Atlantis | ruins city | arrival of zone point 119 |
| Haven of Oceanus | Oceanus Hesperos | the DOL Oceanus landing, about 800 units from the channelers |
| Haven of Stygia | Stygian Delta | 80 units in front of the haven's porter |
| Haven of Volcanus | Oceanus Anatole, before Typhon's Reach | 80 units in front of the haven's porter |
| Haven of Aerus | Oceanus Notos, before the Green Glades | 80 units in front of the haven's porter |
| Cetus' Pit, Sobekite Eternal, Temple of Twilight, Halls of Ma'ati, Great Pyramid, City of Aerus | dungeon entrances | arrival of the dungeon's zone point (126, 122, 128, 131, 134, 140) |

Deep Volcanus and Celestius hold no mobs and are not offered.

Every haven and every ruins city has a porter with the full menu after pack
026:

| Place | Albion copy | Midgard copy | Hibernia copy |
|---|---|---|---|
| Ruins city | Channeler Aldith (new) | Channeler Ragna (new) | Channeler Muirne (new) |
| Haven of Oceanus | Channeler Raifaol, Channeler Khol (now a porter) | Channeler Jarosua, Channeler Xatryn | Channeler Hialysam, Channeler Lana (now porters) |
| Haven of Stygia | Master Visur and the other base-world porters | Channeler Bjarni (new) | Channeler Glasny and the other base-world porters |
| Haven of Volcanus | Channeler Osric (new) | Channeler Torvi (new) | Channeler Ailbe (new) |
| Haven of Aerus | Channeler Edyth (new) | Channeler Hallveig (new) | Channeler Siofra (new) |

**The way back.** `/tc` works inside Atlantis. It only needs the player alive
and off a horse route, and it lands at the capital's Realm Exchange. Every
porter in Atlantis also offers the capitals and the rest of the world menu.
The doors of the ruins city still lead into Oceanus Hesperos, and the zone
points of pack 022 work as before.

### How the landings and porters were placed

The three overworld copies and the dungeon copies are geometric clones. They
have the same zone offsets and the same four haven bind points. Of the 11,183
mobs of region 73, 98 % stand on exactly the same X, Y and Z in 30 and 130,
and the median height difference of near pairs is 0 (dev snapshot after all
packs). A spot where an NPC stands in one copy is therefore floor in the other
two.

- A new haven porter stands on the spot of an NPC of another copy. That spot
  is at least 150 units from every NPC of its own copy and near the haven's
  bind point. The porter faces the bind point.
- A haven landing lies 80 units in front of the porter, the way gaheris'
  teleporter offset its landings.
- A ruins city porter stands 120 units ahead of the arrival point. Every hall
  NPC stands at the arrival's height, so the hall floor is flat there. This is
  the only placement without a verified floor point; check it in the client.
- The ruins cities and havens have no aggressive mob within its aggro range
  plus 500 units of the landing. The dungeon landings are the points where a
  player walking in through the dungeon door arrives anyway. Two have
  aggressive mobs nearby:
  - Halls of Ma'ati: an anubite vusirnui (level 65) stands 339 units away and
    473 units lower, 582 units in a straight line. That is inside its aggro
    range of 800 if height counts. Several eternal lost (level 65) stand 730
    to 1,300 units away, 290 to 420 units lower.
  - City of Aerus: four Phoenix Ozos fly about 5,900 units higher.

### Why not the gaheris travel rows

Pack 021 holds gaheris' 13 Teleport rows (type `gaheris`, realm 0). They name
only the Albion copy, and nothing reads them; the gaheris portal NPC class
does not exist here. Nine of the 13 are anchored on monster spawns:

| gaheris row | Aggressive mobs in aggro range of the landing |
|---|---|
| Land of Atum | 94 |
| Arbor Glen | 9, next to Lord Tholos (level 65) |
| Ashen Isles, Green Glades | 5 each |
| Typhon's Reach | 4 |
| Mesothalassa, Oceanus Boreal | 3 each |
| City of Aerus, Great Pyramid | 2 and 1 |

The four haven rows were safe; the havens here use their towns. The gaheris
rows stay in the save unused.

## Server properties

A value in the `ServerProperty` table overrides the code default. The server
reads all properties before the data packs run. When a pack changes a
`ServerProperty` row, the server reloads all properties right after the packs,
so pack 027 takes effect on the first boot that applies it.

| Property | Code default | Pack 027 | Meaning |
|---|---|---|---|
| `atlantis_teleport_plvl` | 1 (was 2) | 4 or 2 becomes 1 | Minimum account privilege for the Atlantis lines and the Oceanus lines: 1 every player, 2 GM, 4 nobody |
| `rog_toa_item_chance` | 25 (was 0) | 0 becomes 25 | Chance in % that a bonus of a ToA random item is a ToA cap bonus |
| `toa_loot_in_atlantis` | true (new) | not written | Random loot from kills inside Atlantis is a ToA item |

The setup tool seeds new installs with 1 and 25. An owner's own value stays:
pack 027 changes `atlantis_teleport_plvl` only when it is above 1, and
`rog_toa_item_chance` only when it is 0. To close Atlantis again, set
`atlantis_teleport_plvl` to 4 with `/serverproperty`, the database, or the
launcher's XP Settings tab (block TRIALS OF ATLANTIS, `docs/TOA_LAUNCHER.md`),
which also edits `toa_loot_in_atlantis` and `rog_toa_item_chance`.

## Random loot inside Atlantis

Before this package no random item had ToA bonuses. The monster loot
generator (`ROGMobGenerator`, the only random generator in the save's
LootGenerator table) always built ordinary items, so `rog_toa_item_chance`
did nothing.

Now a kill inside an Atlantis region builds its random item as a Trials of
Atlantis item, while `toa_loot_in_atlantis` is on. The regions are the three
overworld copies (73, 30, 130), the ruins cities (70 to 72), the dungeons
(35 to 40, 78 to 83, 135 to 140, 45 to 47, 88 to 90, 145 to 147) and
Celestius (91). The region of the killed mob decides, not the loot owner's;
the loot owner may be a group leader elsewhere.

A ToA item differs in two ways:

- its bonuses are 15 % stronger;
- each bonus becomes a ToA cap bonus with the chance `rog_toa_item_chance`:
  power pool, power pool cap, the strength, dexterity, constitution,
  quickness and acuity caps, or the hit point cap.

At 25 % about two in three level-51 ToA items carry at least one cap bonus.
Kills outside Atlantis drop exactly the items they dropped before. The fixed
ToA loot of pack 040 is unchanged. A ToA item never takes a cap bonus once no
cap bonus fits the item any more; before, a 100 % chance could have failed the
roll on an empty list.

`rog_toa_item_chance` is now read on every roll, so a change with
`/serverproperty` takes effect at once.

## Bots

Autonomous world bots do not enter Atlantis. This was checked once in code and
data, without a running server:

- Every Atlantis region has expansion ToA (database `Expansion` 2, the server
  adds 1). The camp planning, the chat knowledge, town idle routing and the
  zone choice of `AutonomousWorldBotController` accept only Classic and
  Shrouded Isles regions (`IsClassicOrShroudedIslesExpansion`).
- Their porter travel uses only the town routes of
  `AllRealmsTeleportFallbacks`: Classic and Shrouded Isles towns. A bot never
  whispers an Atlantis line; the Atlantis lines answer only players.
- No Atlantis zone has a navigation mesh. The install has 99 navmeshes, none
  for zones 30 to 44, 70 to 87 or 130 to 147.

Companions follow their owner into Atlantis. Without a navmesh they move
there like they do in other zones without one.

## Data packs

| Pack | Content | Rows on the dev snapshot |
|---|---|---|
| `026-toa-travel-realms` | 33 Teleport rows of type `atlantis` (11 per realm), 10 porters (`PackageID = 'toa-travel'`), 3 Oceanus channelers become porters | 46 |
| `027-toa-open-atlantis` | `atlantis_teleport_plvl` 4 → 1, `rog_toa_item_chance` 0 → 25 | 2 |

Sources: `tools/toa/sql/026-toa-travel-realms.sql` and
`027-toa-open-atlantis.sql`, both hand-written. Pack 026 deletes its rows by
type and package and inserts them again; its UPDATEs only touch plain
`GameNPC` rows. Pack 027 changes nothing on a second run. Undo without a
restore:

```sql
DELETE FROM Teleport WHERE Type = 'atlantis';
DELETE FROM Mob WHERE PackageID = 'toa-travel';
```

## What the client check should cover

1. A level 1 and a level 50 character of each realm see the Atlantis line at
   the capital porter after `/tc`. They land in their own ruins city, beside
   the Arbiter, and not inside a wall.
2. The new channeler in each ruins city stands on the floor, about one step
   in front of the arrival point, and opens the full menu.
3. Each haven from `[Atlantis Havens]`, in all three copies: the landing is on
   open ground, the porter stands on the floor and is not attackable, and no
   monster attacks on arrival. Hibernia's Channeler Hialysam and Lana keep the
   look they had.
4. Each dungeon from `[Atlantis Dungeons]`: the arrival is the dungeon's normal
   entrance point. In the Halls of Ma'ati, watch whether the anubite below
   the entrance attacks on arrival.
5. `/tc` from inside Atlantis returns to the capital.
6. A character with companions: the companions arrive with their owner.
7. Some kills inside Atlantis and outside: random drops inside show ToA cap
   bonuses on some items, drops outside look as before.
8. The server log of the first boot names packs 026 and 027 and "refreshed 2
   cached ServerProperty rows".

## Not checked

- Nothing was seen in a client: positions, models, porter dress, the portal
  spell into Atlantis regions, swimming at the havens.
- The ruins city porter positions rest on a flat-floor assumption.
- The drop rate of random loot inside Atlantis is the ordinary rate; only the
  item kind changes. Whether that feels right at level 50+ is untested.
