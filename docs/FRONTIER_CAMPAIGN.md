# Frontier campaign — 0.72.0

## World and capture rules

Frontier Wardens are stationary keep guards, not autonomous gamebots and not
part of the population slider. On loading a fresh/reset world, unclaimed
outdoor Old Frontier keeps receive this NPC guild. Existing human and autonomous
guild claims remain untouched. Portal keeps remain protected travel hubs.
Relic temples remain shrine raid objectives rather than claimable guild keeps;
their relics are initially held by the Wardens.

A living lord blocks claiming, including `/gc claim`. Killing it leaves a
peaceful **Keep Claim Steward** at its position. Interact with the steward or
use `/gc claim` while beside it. Native guild rank, group presence (normally
8 for a keep), and guild ownership limits still apply. Companions count toward
the group. Autonomous crew leaders approach the same steward and claim for
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
