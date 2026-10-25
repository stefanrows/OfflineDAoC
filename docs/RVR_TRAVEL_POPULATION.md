# RvR world: travel and population rules (package R2)

What a player, a companion and a world bot can reach in each world. The
Camlann PvP world (`Camlann-1`) is unchanged: everything stays open. The RvR
world (`Realm-1`, see [RVR_MODE_PLAN.md](RVR_MODE_PLAN.md)) binds travel and
population to the character's realm, as on live 1.65 (owner decisions 7, 8 and
18 in [RVR_MODE_ANALYSIS.md](RVR_MODE_ANALYSIS.md)). Status: implemented in
source, real-client check pending.

The rules live in two helpers with a world parameter for the both-worlds tests:
`world/RealmTravel.cs` (porters, destinations, brokers, companion classes,
parties) and `world/RealmPlaces.cs` (which realm owns a place: homeland,
capital, portal keep, border hub, hub landing). Tests:
`Tests/UnitTests/UT_RvrTravelPopulation.cs`.

## Players

| Path | Camlann world | RvR world |
|---|---|---|
| Capital and town porters (`AllRealmsTeleporter` and its Live, Inland and realm subclasses) | Every porter serves every realm; the menu lists all three realms | A porter of another realm refuses ("You are no citizen of ..."); the menu lists only the own capital, frontier, Darkness Falls entrance, mainland, dungeons, Shrouded Isles and Oceanus; every whispered line of another realm (menus and towns alike) is refused before any arrival text |
| `[Battlegrounds]`, `[open BG]` | Player groups land at a hashed camp | Every realm lands at its own portal keep (also after a release in the battleground) |
| `[Atlantis]`, `[Atlantis Havens]`, `[Atlantis Dungeons]` | Own realm copy (TOA_TRAVEL.md) | Unchanged: own realm copy; enemy copies unreachable |
| `[Housing]`, personal, guild, hearth | Own realm | Unchanged |
| Frontier porters (`OFTeleporter`) | Every realm may port at every frontier porter (bug 74) | Only the own realm's porters port a player; a player with a medallion at an enemy porter is told so |
| `/tc` | Own capital's Realm Exchange | Unchanged (it never left the own realm) |
| Realm Exchange broker | Any broker sells the player's own realm market (listings are keyed by the lister's realm) | Only the own capital's broker opens; an enemy capital's broker refuses |
| Safe hubs (portal keeps, border hubs, hub landings) | Safe for every realm | Safe only for the hub's realm; a defender inside may attack an intruder inside the same hub, never someone outside it |
| Bind stones | Any | Own realm (R1b); a stored bind in another realm's lands (homeland, portal keep, border hub or hub landing) is reset to the realm's start on the next release (`RVR_ENEMY_BIND_RESET` log line), so a release always wakes in the own realm |

## Companions and helpers

| Path | Camlann world | RvR world |
|---|---|---|
| `/spawn`, the `/spawn` class menu, `/classes` | All three realms | Own realm only; `/spawn <realm> <class>` of another realm is refused |
| `/companions recruit`, story cast, Companion Manager recruit tab, persistent companion menu | All three realms | Own realm only; a recruit of another realm is refused with nothing created |
| Inviting a stored companion of another realm | Allowed | Refused; the roster record is kept |

## World bots

| Path | Camlann world | RvR world |
|---|---|---|
| Homelands and capitals | Every realm may enter every homeland (Tier 2) | Upstream rule back: no bot enters another realm's homeland or capital (camp choice, town idling, merchants, trainers, Realm Exchange, release, movement); frontier zones, shared dungeons and Darkness Falls stay open; a bot that already stands in an enemy homeland may walk out, and a saved position there is sent to the own capital at login |
| Town porters (`AutonomousBotTownTravel`) | Any porter | Own realm's porters only, and no landing in another realm's homeland |
| Town teleporters as a travel shortcut (upstream 0.38c `AutonomousTownTeleporters`, port S3) | Off | Own realm's town teleporter when it saves a minute and 30% against walking or a horse (`bot_use_town_teleporters`, default on); destinations also pass the realm boundary above. |
| Frontier porters | A warband may board its leader's realm's porter | Own realm's porter only (nearest own porter first) |
| Pickup PvE groups, RvR pickup groups, warband backfill and merge | Mixed realms, preferred for PvE (0.59.0) | Same realm only (filters in the group coordinator) |
| Any party (backstop) | Allowed | `Group.AddMember` refuses a member whose alliance realm differs from the party's (staff and neutral encounter bots never block) |
| Atlantis | Off for bots | Off for bots |
| Battlegrounds | Admitted participants only | Open BG crew only: the fork's automatic level-bracket campaign is off, as upstream 0.38c sends no bot to a bracket battleground by default (port S3) |

## Modes

| Switch | Camlann world | RvR world |
|---|---|---|
| `open_bg_mode` | `serverpvp` or `perrealm` as set | Always `perrealm`; a stored other value is ignored with one start line `OPEN_BG_MODE ignored=... reason=rvr-world`, and `/bgsetup` shows `perrealm (ignored: rvr-world; stored ...)` |
| `open_bg_landing_peace` | As set (`all` default in Server PvP) | Always `realm` (the cross-realm `all` peace is a Server PvP rule) |
| Open BG presets that merge realms | Merged pools in Server PvP | Never merged (per realm pools) |
| Gothwaite PvP mode | Switch file | Off; an enabled switch is ignored with `GOTH_PVP_MODE active=False reason=rvr-world` |

## Open

- **Safe hubs per realm (wired).** In the RvR world a portal keep, border hub
  or hub landing protects only its own realm (`PvpCombatant.IsSafeArea` asks
  `RealmPlaces.HubProtects` with the R1b alliance realm), and only capitals,
  housing and newbie dungeons still stop an attacker
  (`PvpCombatant.BlocksAttackFromSafeArea`): a defender inside its own keep or
  hub may fight an intruder inside that hub but cannot attack out of cover
  at anyone outside it; an intruder there is fair game. **Window until
  R3a:** portal-keep guards stay passive, so the defence of a hub is the
  realm's players and bots, not its guards.
- Mixed bot guilds (crews) stay until R4a; they no longer form cross-realm
  parties, and R1b already makes them fight by realm.
- Shrouded Isles porters (`AlbionSITeleporter` and the others) and
  `SimpleTeleporter` are not gated: they stand inside their own realm, which no
  enemy reaches by porter any more.
- A stored companion of another realm that is restored as active at login is
  not checked (fresh RvR worlds have none).
