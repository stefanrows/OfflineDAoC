# Frontier campaign — 0.72.0

## Warband formation — 0.73.0

Guild PvP recruitment can use validated town-teleporter routes across regions.
New parties have two minutes to fill spare seats before leaving assembly;
recruitment does not refill a party already fighting, grinding or recovering.
Hybrid and KeepWarrior leaders favor eight-member parties. Hunters retain
small-party behavior. New automatic assaults require a full party of eight
from one guild, healing support and siege supplies, and only open on keeps a
guild can claim (no relic keeps, base level 50). Smaller forces may reinforce
only their own guild's siege; a stranger guild contests it only with a whole
warband.
Eligible guildmates can coordinate their next task with a waiting PvP cohort,
without interrupting active tasks or mandatory PvE recovery. This improves
formation opportunities; it does not establish successful live keep captures
or repair every frontier route. Installation and gameplay validation are pending.
CoreServer and Windows launcher Release builds passed on 2026-09-26.
Automated tests were not run under the local project workflow; live acceptance
requires a separately authorized deployment and a new population observation.

## Autonomous siege flow (task 48, siege slice 1)

A committed warband's siege operators (tanks, melee fighters, Scouts and
Rangers) buy a ram at the border hub or at
home before marching, place it at the outer gate and operate it once they are
within 6,000 units of the keep (`RVR_SIEGE action=purchase|deployed|hit`).
Casters of the same group ride the ram (`action=ride|dismount`); each rider
adds damage and shortens the reload. A caster with no free seat on its
group's ram nukes the gate instead. Melee classes clear reachable guards,
then hit the outermost standing gate, then the inner gate. Healers stay free.
The lord becomes a target only after every gate is down; its death leaves the
claim steward, where the crew leader claims for its guild.
An automatic siege closes after 15 minutes without attacker progress, or
after 45 minutes without any attacker within 3,000 units of the keep
(`RVR_SIEGE_IDLE_CLOSED reason=no_progress|absent`). Only approach inside the
keep's region counts as progress. Source only; real-client and live-log
check pending.

## Siege balance (task 48, owner decisions of 2026-09-28)

Source only; real-client and live-log check pending.

- **Warden keeps stand like a 1.65 unclaimed keep.** At every server start a
  frontier keep held by the Frontier Wardens (base level 50, no relic keep) is
  set to keep level 1 (`FRONTIER_WARDEN_KEEP_LEVEL keep=... from=5 to=1` in the
  log; no line once it is at 1). Guild-claimed keeps and relic keeps are not
  touched. A released or reset keep also drops to `starting_keep_level`, now 1.
  Claiming still raises the keep to `starting_keep_claim_level` (5), the
  existing upgrade path; `/gc upgrade` stays disabled.

  | Keep | Level | Outer door HP | Guards | Lord |
  |---|---|---|---|---|
  | Warden-held (unclaimed) | 1 | 10,000 | 52 | 63 |
  | Guild-claimed | 5 | 50,000 | 59 | 70 |
  | Relic keep (unchanged) | 10 | 180,000 (relic gate) | 76–77 | 90 |

  Door HP = base level 50 × `keep_doors_base_health` 200 × keep level
  (`keep_doors_health_upgrade_modifier` 1). Guard level = 51 + 1.6 × keep
  level, lord 62 + 1.6 × keep level (`keep_guard_level_multiplier` 1.6,
  unchanged). All keep positions in the save sit at height 0 and the guards
  spawn from the mob table, so level 1 removes no guard or banner. Keep level
  1 does lower the wall height sent to the client (height 0 instead of 2) for
  the 17 Warden keeps that stood at level 5, and for Dun Crauchon (level 4,
  height 1); a real-client look at the walls and the wall guards on them is
  pending.
- **One automatic siege per guild, not per server.** A guild runs at most one
  automatic siege of its own; its other warbands may reinforce it or roam.
  Other guilds may open their own sieges at the same time, up to a
  server-wide safety cap of 6 attacking sieges (defense responses do not
  count). Guildless and launcher-forced sieges count per realm. An active
  relic-carrier event still pauses new sieges.
- **A guild may hold three keeps.** `guilds_claim_limit` is 3 (was 1 in the
  save); the steward, `/gc claim` and crew leaders use the same native limit
  check. A guild at its limit can still take a keep; it cannot claim it.
- **Damage spells hit doors, as since 1.46.** Single-target direct damage and
  bolts hurt a keep door at half effect after the door's level toughness
  (5 % less per keep level): a 400 nuke deals 190 to a level-1 door and 150 to
  a level-5 door. Damage over time, debuffs, crowd control, lifedrains, damage
  spells with a debuff rider, and area spells do not affect doors; walls stay
  siege-only. Players, companions and bots alike. Bots no longer cast
  non-damage spells at a door.
- **Unchanged:** the temporary ×10 Siege Ram (task 55); relic raids stay off
  (docs/TASKS.md, relic-raid mode idea).

The server applies both property changes itself on the next start: a startup
database update moves `starting_keep_level` 4 → 1 and `guilds_claim_limit`
1 → 3 only while the row still holds the old shipped value as both value and
default (`FRONTIER_BALANCE_PROPERTY key=... from=... to=...`). A value an
operator set later stays.

## World and capture rules

Frontier Wardens are stationary keep guards, not autonomous gamebots and not
part of the population slider. On loading a fresh/reset world, unclaimed
outdoor Old Frontier keeps receive this NPC guild. Existing human and autonomous
guild claims remain untouched. Portal keeps remain protected travel hubs.
Relic temples remain shrine raid objectives rather than claimable guild keeps;
their relics are initially held by the Wardens.

A living lord blocks claiming, including `/gc claim`. Killing it leaves a
peaceful **Keep Claim Steward** at its position. Interact with the steward or
use `/gc claim` while beside it. Guild claim rank and ownership limits still
apply, but Camlann claims have no group-size requirement. Autonomous crew
leaders approach the same steward and claim for
their guild; ordinary members of pure managed autonomous guilds receive native
claim permission. Human-managed guild ranks are not changed.

The defeated lord stays absent until a claim. The remaining garrison stops
fighting during this claimable interval. Claiming restores the lord and makes
the defenses belong to the winning guild. Unclaimed defeated keeps and claimed
keeps survive server restarts. A voluntary guild release does not itself unlock
a claim reward; another lord defeat is required. Capture RP has a persistent
30-minute cooldown per keep. The launcher's explicit world/keep reset clears
these states and restores garrisons on the next start.

Guard kills award 25 base RP through normal NPC contribution rules. Capturing
awards 1,500 base RP to nearby living members of the claiming group who belong
to its guild and are within the keep. Existing player RP-rate modifiers apply;
autonomous bots receive their normal direct RP award. Companion RP persistence
is unchanged and remains a separately tracked feature question.

Relic pickup requires owning a guild keep first. At a home shrine, defeat the
stationary combat guards; peaceful services are excluded. The required nearby
raid members can be mixed-realm humans, companions or autonomous bots. Existing
carry, inventory, mount-delay and return-to-shrine rules remain in effect.

## XP

Monster kills in Old Frontiers (including its supported frontier dungeons) and
Darkness Falls grant 1.5 times the base XP after the ordinary level/contribution
cap. This multiplies the selected player/companion XP rate or autonomous-bot XP
rate. Existing camp/group/item bonus and companion catch-up rules remain.
For example, 10x selected XP becomes 15x base monster XP in these zones. Player
kills, quests and unrelated zones do not receive this extra monster bonus.
Camlann human XP no longer replaces the configured rate with `rvr_zones_xp_rate`.

## Population restoration

A read-only world audit counted 1,071 ordinary mobs in the twelve outdoor Old
Frontier zones. The new immutable manifest selects 4,851 additional archived
rows. Selection requires a retained or previously reviewed monster of the same
name in the same zone, within three levels; exact retained spawn duplicates
are excluded. It preserves the archived row's coordinates, templates and loot.
The earlier two-to-three-monster-per-camp limit is deliberately removed.

This is a roster-backed restoration, not independent 1.65 map corroboration or
native-navigation proof of every archived coordinate. No meshes are replaced.
Normal autonomous route checks remain active. World visibility, density and
travel still require a real client.

| Zone | Existing ordinary mobs | Added archived rows |
|---|---:|---:|
| Forest Sauvage | 167 | 547 |
| Snowdonia | 71 | 273 |
| Pennine Mountains | 188 | 619 |
| Hadrian's Wall | 29 | 133 |
| Uppland | 159 | 899 |
| Yggdra Forest | 130 | 598 |
| Jamtland Mountains | 110 | 571 |
| Odin's Gate | 51 | 279 |
| Mount Collory | 44 | 192 |
| Cruachan Gorge | 26 | 219 |
| Breifine | 19 | 290 |
| Emain Macha | 77 | 231 |

Reproduce selection with
`source/server/tools/frontier_garrison_spawn_restoration.py --database <path> --ids <output> --report <output>`.
It opens SQLite read-only and emits world-spawn data only.

## Installation and verification

Source work does not deploy or start the server. The population changes require
the rebuilt **OfflineDaoc.Setup** tool's `--apply-frontier-garrison-spawns`
migration against a stopped installation with its consistent backup. Copying
server/launcher binaries alone does not restore the rows. The migration uses
exact IDs, preserves existing rows, records completion and is repeat-safe.
The scoped command only restores this manifest; it does not rerun Realm
Exchange setup or older migrations. The same manifest is also part of
fresh-world Setup and the complete `--apply-classic165-spawns` migration. Never run it against an active server.

Offline verification on 2026-09-26: CoreServer, Windows launcher and Setup
Release builds passed. Source review found no conflict markers, the version
pins agree, and the new spawn IDs do not overlap earlier manifests. Existing
server/Setup warnings remain; the launcher build was warning-free. Automated
tests were skipped under the project workflow. This does not establish a
real-client pass.

Owner verification after authorized installation: inspect camps and routes;
compare frontier/DF monster XP with the selected rate; raid and claim with
companions and an autonomous crew; verify guard/capture RP, duplicate-claim and
cooldown behavior; restart with a captured and an unclaimed defeated keep;
raid a relic temple; check both directions through friendly doors and blocked
hostile gates. No live save, credentials or database hashes belong in this
report.
