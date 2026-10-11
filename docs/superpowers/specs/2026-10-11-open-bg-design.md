# open BG: a permanently open Thidranki (design)

Date: 2026-10-11. Owner: Aaron. Status: approved in chat 2026-10-11 ~02:20,
built overnight by the orchestrating session in four stages.

## 1. Goal

A permanently open RvR battleground on the native Thidranki map (region 252)
for players of every level and realm, where something is always going on
but the map never floods: keep fights at the centre keep, group fights away
from the bridges, solo roamers and stealthers lurking at the bridges. It is
both a fun place to play and the proving ground for autonomous-bot
behaviour. Camlann rules apply: only group, guild, alliance and battlegroup
are friendly.

Sources (2003–2005): three portal keeps, three named bridges (King's Bridge
Alb, Od's Bridge Mid, Fagan's Bridge Hib), the Center Keep (CK); "Albions
gathered at the Midgard Bridge as usual" to stop Mids from reaching the CK;
three full Hib groups surprising the Albs at the CK; stealthers at and under
bridges, solo guides saying "stay off bridges"; keep holes repaired with wood.

## 2. Owner decisions (do not re-ask)

| Topic | Decision |
|---|---|
| Region | native Thidranki **252** (keep 11 "Thidranki Faste", portal keeps 12 Hib, 13 Mid, 14 Alb); the 20–24 bracket on 238 stays untouched |
| Entry | players level 1–50, no realm-rank limit, all realms; **`[open BG]` line in the porter NPC menu** (the porters players reach via `/tc`); bots use the same porters; no new player command |
| Bots | **own fixed roster** from the 40–50 band of the world roster, relieved in part every 20–30 min; **cap 72** (property); groups are formed before departure |
| Level gap | **hybrid**: no level change on persistent bots; while a player under 40 is inside, non-persistent **guest squads** on the player's level appear (encounter-actor mechanism) |
| Population setup | per realm: number of 8-man groups, small groups (2–4), solos |
| Rhythm | **random cycle 15–45 min**: quiet → push → keep fight → retreat; never one static setting |
| Player drift | **light**: roamers and solos prefer targets within sight of a player; the keep cycle runs independently |
| Keep | the conquering bot guild **holds it until the next capture**; hold rewards run |
| Quests | campaign commander contracts (already level-scaled) + **Pazz dailies opened to 1–50** with level-scaled rewards |
| Low levels | **own portal keep is a sanctuary, the field is FFA**; only guest squads hunt players under 30; 40–50 bots attack them only in self-defence |
| Setup UI | **launcher tab "open BG"** (writes server properties while the server is stopped) + GM command **`/bgsetup`** (writes the same properties and refreshes live) |
| Night scope | stages 1–4, each built → reviewed → deployed → measured 45–60 min → fixed → pushed; the loop may deploy and restart the local server freely |

## 3. Architecture: reuse the campaign framework, keep the bots separate

Region 252 is registered in `BattlegroundCampaignCatalog` as a definition of
kind **Open** (level 1–50, realm-rank cap 0). That single data entry makes
the existing region-keyed machinery work for it: keep build and garrison,
portal-keep services, landings, claim rules, hold rewards, commander quests,
porter entry. Nothing in Stefan's bracket campaign changes behaviour.

Everything specific to the open BG lives in new files under
`GameServer/battlegrounds/open/` (or `OpenBattleground*.cs` beside them):

| Unit | Responsibility | Depends on |
|---|---|---|
| `OpenBattlegroundProperties` | server properties `open_bg_*` with defaults | `ServerProperty` attribute loader (scans static classes, like `AutonomousSiegeProperties`) |
| `OpenBattlegroundRoster` | picks and keeps the bot crew per realm from the 40–50 band, forms groups before departure, relieves in part every 20–30 min, enforces the cap | `AutonomousPopulationController` roster, `AutonomousBattlegroundParticipation` admit/depart, `AutonomousBotGroupCoordinator` |
| `OpenBattlegroundDirector` | the phase machine (quiet → push → keep fight → retreat), random 15–45 min per cycle, picks the attacking realm, publishes phase and per-group roles | manager tick, keep ownership, driver |
| `OpenBattlegroundRoles` | pure role rules: bridge watch, roamer, solo/stealther, assault, defend; light player drift; low-level protection | positions, bridges, keep, players in region |
| `OpenBattlegroundGuestSquads` | non-persistent squads on a low player's level while such a player is inside | `BattlegroundEncounterActor` |
| `OpenBattlegroundSetupCommand` (`/bgsetup`) | GM command that writes the properties and refreshes them live | `ServerProperties.Refresh` path used by `/serverproperties refresh` |
| Launcher `OpenBgSettingsControl` | tab "open BG" with on/off and the numeric setup; stop-only writer | launcher server-property writer (`MainForm.cs:2047-2080`), modelled on `BotGoalsSettingsControl.cs` |

Touched existing code, each a few lines:

- `BattlegroundCampaignCatalog.cs:32-47`: add a `Kind` (Bracket | Open) to
  `BattlegroundDefinition`, the 252 definition, `ForLevel` skips Open,
  `FindOpen()`.
- `BattlegroundCampaignPolicy.cs:84-119`: `TryEnterOpen(player)` and
  `TryEnterBotOpen(bot)` that bypass the bracket; `CheckProgress` is a no-op
  for Open (no graduation, no tour end).
- `AllRealmsTeleporter.cs:32-49, 463-498`: menu line `[open BG]`, whisper
  case → `TryEnterOpen`. `OFTeleporters.cs:185-201`: bots departing for the
  open BG use the same medallion path.
- `AutonomousBattlegroundDriver.cs:131-178` (`Lead`): read the director's
  phase and the group's role before choosing defend/claim/assault/roam; new
  goals: a bridge point, a field point away from bridges.
- `AutonomousBattlegroundParticipation.cs:21-22, 110-131, 470-505`: cap and
  candidate rules are per definition kind (Open uses the roster, not the
  tour candidates); new `LeaveReason.Relieved`.
- `GamePlayer.cs:1334-1342` and `KeepManager.cs:348-358`: the legacy cap row
  252 (20–24) must read as 1–50 for release and keep rules (in memory, as
  `RegisterCaps` already does for the realm-rank cap).
- `BattlegroundKeepLayouts.cs:274-277`: add portal keeps 12–14 of 252 to the
  native list. `BattlegroundKeepZCorrection.cs:36-44` if keep 11 needs it.
- `ThidKeepCapture*.cs` / `ThidKillQuest*.cs` (6 files): min/max 1–50,
  rewards scaled by level.
- `tools/dev/build-battleground-nav.sh:72, 88`, `tools/dev/OfflineDAoC.Deploy.psm1:296`,
  `tools/release/build_fork_release.py:100`, `tools/dev/battleground-keeps.json`:
  zone 252 in the navmesh and packaging lists.
- `docs/CAMLANN.md`: the open BG section; `ALL SERVER COMMANDS.txt`: `/bgsetup`.

## 4. World 252 (stage 1)

- Navmesh: build zone 252 with the existing builder (client data at
  `playable/runtime/client-opendaoc/app/zones/zone252`); run the
  connectivity check for the three landings → keep 11 and the three bridges;
  exit code 3 for sealed interiors is acceptable only if the lord and the
  gates are reachable.
- Keep 11: 0 `KeepComponent` rows in the save. Build the components from the
  client fixture (`nfrontkeep2.nif` at 33280,38272; the keep row says 33089,
  191 units off, use the fixture position) the way `BattlegroundNativeKeepData`
  does for Murdaigean keep 139 (tag `offline-native-bg:252`); gates
  252000301/302 and posterns 303–306 must register in pathfinding (today the
  posterns fail). The saved lord stands on the tower top (Z 4592, +872): move
  him to the floor nearest the lord point like the 0.235.0 relocation; the 18
  saved guards stay; no Molvik wall-guard copy (`BattlegroundKeepWallGuards`
  must skip keep 11).
- Landings: the three portal keeps, snapped to the mesh; distances to keep 11
  are 14,700 (Alb), 24,850 (Hib), 24,980 (Mid): route proofs rely on the
  chained corridor (bug 127, 8 legs).
- Bridges (zone fixtures, world coordinates): 38656,36096 (E, toward Mid);
  30208,34816 (NW, toward Hib); 29056,41088 (SW, toward Alb). Names are
  cosmetic; log them as `bridge=E|NW|SW` until verified.
- The region needs no PvE monsters for stage 1 (0 today); guest squads and
  bots are the content.

## 5. Roster (stage 1)

- Setup per realm: `open_bg_groups8`, `open_bg_groups_small` (size 2–4,
  random), `open_bg_solos`; `open_bg_max_bots` (default 72); `open_bg_enabled`.
- Defaults: 1×8, 2×3, 4 solos per realm = 54 bots, under the cap.
- Candidates: autonomous world bots of that realm, level 40–50, not
  player-led, no relic, not in another battleground; prefer bots on an RvR
  tour, then idle. A bot assigned to the open BG keeps its persistent level,
  guild, realm points and gear; the assignment is in memory only.
- Groups are formed by the coordinator **before** departure (8 or 2–4
  members, a healer where possible), the leader boards first; members land
  with their leader (0.240.0 landing rule).
- Relief: every 20–30 min (random) one group or 25 % of the solos per realm
  is relieved (`LeaveReason.Relieved`), walks to its portal keep and leaves;
  the roster refills from candidates. A bot that died and released stays.
- Cap: hard limit per region; the roster never admits above it; the
  bracket caps (24/40) do not apply to the open BG.
- Log: `OPEN_BG_ROSTER realm= groups8= small= solos= present= cap=` every
  5 min; `OPEN_BG_RELIEF`.

## 6. Director and roles (stage 2)

Phase machine per cycle, each phase length drawn once per cycle so a full
cycle is 15–45 min:

| Phase | Share | What bots do |
|---|---|---|
| Quiet | 30 % | groups: one group per realm holds its own bridge (bridge watch), others roam the field away from bridges; solos/stealthers lurk 300–800 units off a bridge; no keep assault |
| Push | 20 % | the director picks the attacking realm (weighted: realm that does not own the keep, fewest recent wins); its groups muster at their bridge, then march to the keep; the other realms keep watch and may intercept |
| Keep fight | 35 % | attackers assault (hookpoint rams, doors, lord) with the existing driver assault; the owning realm's groups defend; third realm roams and picks fights near the keep |
| Retreat | 15 % | losers walk home, the winning guild claims (existing claim rule), hold rewards run; solos keep roaming |

- The director publishes `Phase`, `AttackingRealm` and a role per group;
  `AutonomousBattlegroundDriver.Lead` consults them first, then falls back to
  today's priority. Roles are pure functions of (phase, realm, keep owner,
  group size, bridge positions, player positions).
- Light player drift: when choosing a roam or lurk target, a point within
  sight range (6,000) of a human player in the region gets priority once per
  cycle per group; never a straight line to the player.
- Low-level protection: a persistent 40–50 bot does not acquire a human
  target below level 30 unless attacked by it; portal keeps are sanctuaries
  for everyone (existing rule).
- Log: `OPEN_BG_PHASE phase= attacker= cycle_s= owner=` on every change;
  `OPEN_BG_ROLE` on assignment; `OPEN_BG_TICK_SLOW` like the campaign tick.

## 7. Setup UI (stage 3)

- `/bgsetup` (GM, plvl 2+): opens an in-game window like `/companion`
  (`SendCustomTextWindow` pages): live state (phase, attacker, keep owner,
  every bot inside with name, level, role and group, guest squads, achieved
  world speed) and the setup values; `/bgsetup <key> <value>` writes the
  `ServerProperty` row and refreshes properties; the roster and director
  re-read on their next tick. Keys: enabled, groups8, small, solos, max_bots,
  cycle_min, cycle_max, drift. The window is callable at any time.
- All cycle and relief timers run in **game time** (`GameLoop.GameLoopTime`,
  scaled by the world speed): at 20× a 15–45 min cycle lasts 45 s to 2.25
  min, so an overnight test shows many cycles; log lines carry the achieved
  multiplier.
- Launcher tab "open BG": the same keys with a short help text per field;
  writes while the server is stopped (existing writer), shows the live state
  from the status snapshot when running (phase, present bots, keep owner).

## 8. Quests, guest squads, protection (stage 4)

- Pazz dailies: `ThidKeepCapture{Alb,Hib,Mid}.cs` and `ThidKillQuest*.cs`
  min 1 / max 50; XP reward stays "half a level", coin reward level×2 silver
  (already scales), realm points per the campaign contract.
- Guest squads: while a human player below 40 is inside, one non-persistent
  squad per realm (size 3–5, level = player level ±3, no loot, no progress)
  spawns at a landing and hunts in the player's half of the map; despawn 5
  min after the last such player left. Reuse `BattlegroundEncounterActor`
  and the squad spawner of `BattlegroundCampaignManager`.
- Protection as in §6.

## 9. Error handling and safety

- No navmesh / no landings → the open BG stays closed and logs
  `OPEN_BG_UNAVAILABLE reason=`; the porter line is hidden.
- Roster never changes a bot's persistent level, gear or guild; assignment
  state is not saved (as today's participation entries).
- Properties out of range are clamped and logged once.
- The director tolerates a missing keep owner (neutral garrison) and a human
  guild claim (humans may own the keep; the push targets it like any owner).
- Keep the 256-polygon chain limits; a camp or bridge that logs
  `corridor_cap` from every landing is a map data bug, not a code change.

## 10. Testing and acceptance

Unit tests (pure predicates): catalog kind/ForLevel skip; roster selection
and relief; phase lengths within 15–45 min and shares; attacking-realm
choice; role assignment; player-drift target choice; low-level target rule;
property clamping; Pazz level range.

Live acceptance per stage (installed, 45–60 min, world speed 1×):

| Stage | Accept when |
|---|---|
| 1 | `BATTLEGROUND_CAMPAIGN_READY region=252 lord=True`; `OPEN_BG_ROSTER present` ≥ 80 % of setup within 15 min; `AUTONOMOUS_BG_LEFT reason=stuck` < 5 % of entries; no `AUTONOMOUS_BG_ROUTE_FAILED reason=dead_end` for landings → keep; tick p95 < 250 ms |
| 2 | at least one full cycle logged with all four phases; a keep capture or a claim per 2 cycles; groups seen at bridges in quiet (`OPEN_BG_ROLE role=bridge_watch` with positions within 1,000 of a bridge); solos' deaths spread over the map |
| 3 | `/bgsetup` change visible in the next `OPEN_BG_ROSTER`; launcher writes the rows (unit test on the writer) |
| 4 | with a GM test character under 40 inside: guest squad spawned within 60 s on its level; Pazz quest accepted at level 50 |

Real-client checks stay with the owner: porter line, landing, keep render,
guest squads, `/bgsetup` output.

## 11. Out of scope

Realm peace inside the battleground, a second open BG, dynamic population
by player count, bridge names, bot stealth behaviour changes, PvE monsters
on the map.
