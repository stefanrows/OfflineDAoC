# Gothwaite PvP mode: plan

**Status:** in progress. Stage 0 (audit) done 2026-10-13 (0.261.2), results
in section 11. Stage 1 built in 0.262.0, stage 2 in 0.263.0, stage 3 in 0.264.0
(section 12). Stage 4 follows.

**This is a separate world mode.** It is not a population preset, it is not
part of the Camlann world bot work, and it does not change that work. With the
mode switched off (the default), the server must behave exactly as it does
without this code: same bot goals, same routes, same safe areas, same saved
positions. Section 5 lists the rules that guarantee this. Anyone working on
the normal autonomous bots (`GameServer/bots/autonomous/`) can ignore this
document. Anyone working on this mode must not tune, rename or rewrite normal
bot code to make the mode work. Add a guarded hook instead.

## 1. What the mode is

When the owner starts the server in **Gothwaite PvP** mode, every autonomous
world bot of every level lives on the Isle of Glass around Caer Gothwaite and
does only PvP:

- **Caer Gothwaite is a safe area.** The bindstone is there. Bots bind,
  release, rest, buff and form groups inside it, then head out.
- **Everything outside the castle is the PvP arena.** Bots roam it solo or in
  groups and kill other players and bots. They do not level on mobs, run
  dungeons, siege keeps, join battlegrounds or raid.
- **Con rule.** A bot never attacks a target that cons grey or purple to it.
  Greys and purples leave each other alone, in both directions.
- **PvP from level 1.** The normal sub-10 protection for autonomous bots is
  lifted for mode bots, so a level 1 bot fights and can be killed.
- Bots still fight by Camlann rules: group, guild and alliance mates are
  friends, everyone else is a target, whatever their realm.

Normal world mode stays the default. The mode is chosen in the launcher before
the server starts. Switching needs a restart.

## 2. Owner decisions (2026-10-13)

| # | Decision |
|---|---|
| G1 | The mode is called **Gothwaite PvP** and is completely separate from the current bot behaviour. |
| G2 | **All autonomous bots of all levels** take part. |
| G3 | **Caer Gothwaite (the bindstone castle) is a safe area.** Bots bind, form groups and buff there before heading out. |
| G4 | **Greys never attack purples, and purples never attack greys.** |
| G5 | Bots know the area and its surroundings and cruise around it, only killing each other. |
| G6 | **PvP from level 1.** The sub-10 PvP protection does not apply to mode bots. |
| G7 (O1) | XP, realm points and levels earned in the mode **carry over** to normal mode. Only position and bind are restored. |
| G8 (O2) | **No constitution loss** for mode bots in the mode. |
| G9 (O3) | **Alt trickle paused** while the mode is on. |
| G10 (O4) | Mode bots **may attack the owner's character and companions**, under the same con rule. The castle stays safe for everyone. |
| G11 (O5) | **Gothwaite Harbor is not a safe area.** The castle is the only safe hub. |

All open questions are decided (section 9).

## 3. The area (measured 2026-10-13)

Source: a read-only query of the installed world database backup (region 51
rows) and the installed navmesh folder. Re-check these numbers in stage 0
against the live database layout before you rely on them.

- **Region 51 (Albion Shrouded Isles), zone 51 "Isle of Glass"**, zone offset
  60,60 (units of 8192): X and Y run from 491,520 to 557,056.
- **Caer Gothwaite**: area circle `Caer Gothwaite` at 534,958 / 548,813,
  radius 4,000. Two bindstones at about 535,000 / 548,790 (Z 4,800). The
  all-realms teleporter lands at 535,158 / 548,589
  (`AllRealmsTeleportFallbacks.cs`, `1:Gothwaite`). The castle has 5 guards, 3
  merchants, trainers and a hastener.
- **Gothwaite Harbor**: area circle at 525,023 / 542,126, radius 4,000, about
  12,000 units west of the castle. It has its own bindstone (527,058 / 542,845),
  15 merchants, 4 guards and a `LiveTeleporter`. The harbor is **inside the
  arena** (the classic Camlann "goth" fights were at the harbor).
- **Edges**: the castle sits about 8,500 units from the zone's south edge and
  22,000 from its east edge. The arena opens to the north and west. Avalon Isle
  (zone 52) lies to the north-west.
- **Navmesh**: `zone051.nav` through `zone057.nav` are installed. The
  castle-to-harbor walk has a known seam that is covered by the navigation
  test `UT_SeptemberFourteenInstalledNavigation` ("Gothwaite seam").
- **Neutral mobs by distance from the castle** (realm 0):

  | Distance | Mobs | Aggressive | Levels (median / 90% / max) |
  |---|---|---|---|
  | 0–6,000 | 0 | 0 | none |
  | 6,000–10,000 | 11 | 0 | 5 / 7 / 7 |
  | 10,000–15,000 | 66 | 0 | 1 / 5 / 5 |
  | 15,000–20,000 | 134 | 6 | 3 / 6 / 7 |
  | 20,000–30,000 | 293 | 131 | 8 / 12 / 17 |
  | 30,000–45,000 | 303 | 291 | 13 / 18 / 31 |

  Within 20,000 units the mobs are low and almost all passive, which suits
  low-level bots. Beyond 20,000 most mobs are aggressive and up to level 31.
  That is a danger only for low levels, and it is the main reason the arena
  has an edge (section 6.5).

## 4. Why Gothwaite (compared to the alternatives)

| Candidate | Fit for "all levels, all bots, only PvP" |
|---|---|
| **Isle of Glass / Caer Gothwaite** | **Recommended.** A castle with bindstones is a natural safe hub. Low, mostly passive mobs near town suit levels 1–20, and high levels ignore them. There is open terrain, a harbor with a teleporter, and an installed navmesh. It is an authentic Camlann hotspot. No keeps, battleground or open-BG code runs there, so the mode does not touch other systems. |
| Open BG (Thidranki, region 252) | Already exists for levels 1–50 and every realm. But it is one zone built around keep 11, it uses a curated 72-bot crew, and it is still being tuned on the live server (TASKS 116). Putting the whole population there would collide with that feature. That breaks rule G1. |
| Bracket battlegrounds | Level-capped (e.g. Thidranki 20–24) and small. They can't hold all levels or 1,000+ bots. |
| Old Frontiers / Emain Macha | Keeps, guild claims, sieges and keep guards are all live systems there. Bots would fight guards and keeps, not each other. It's too big to concentrate fights. |
| Darkness Falls (region 249) | Indoors, with mobs of level 35 to 60+ and its own entry and navigation rules. Low levels can't survive there. |
| Aegir's Landing / Grove of Domnann | The Midgard and Hibernia SI hubs, the same shape as Gothwaite. Possible later **alternative arenas** if the arena is defined as data (section 6.4). They aren't needed for the first version. |

## 5. Separation contract (hard rules)

1. **One switch, off by default.** `GothwaitePvpMode.IsActive` is read once at
   server start from its own settings file. Changing it requires a restart. When
   it is false, every hook below returns at once and does nothing.
2. **Own code, own folder.** All mode logic goes in
   `source/server/GameServer/bots/gothwaite/` (namespace `DOL.GS`, class
   prefix `GothwaitePvp`). Its tests are `Tests/UnitTests/UT_GothwaitePvp*.cs`.
   Do not put mode logic inside `bots/autonomous/` files.
3. **Own settings.** A separate file `gothwaite-pvp.json` in the server folder,
   written by the launcher while the server is stopped. It does **not** go into
   `BotGoalSettings`, `PopulationPreset` or the type-mix control, and it does not
   change them.
4. **Guarded touchpoints only.** Existing files get at most a one-line guarded
   call into the mode. The expected touchpoints (confirm and complete the list
   in stage 0):
   - `bots/BotBrain.cs` `ThinkCore`: one branch, the same pattern as
     `AutonomousBattlegroundParticipation.IsParticipant` →
     `ThinkAutonomousBattleground()`. A mode bot runs `ThinkGothwaitePvp()`
     and returns, and the world controller never runs for it.
   - `serverrules/PvpCombatant.cs` `IsSafeArea` / `IsSafeReleasePoint`: the
     castle circle counts as safe only while the mode is active.
   - `serverrules/PvpCombatant.cs` `IsSafetyProtected(GameLiving)`: the
     sub-10 rule for autonomous world bots (`bot.Level < safetyLevel`, which
     also feeds `BlocksLowLevelAutonomousPvp` and `BlocksAutonomousPvp`)
     returns false for a mode bot while the mode is active (G6). A real
     player's own `/safety` flag is not changed. It stays the player's choice.
   - Constitution loss on death: skipped for mode bots while the mode is
     active (G8).
   - Every director or scheduler that **recruits** world bots (RvR director
     and event layer, sieges, guild armies, realm raids and their schedule,
     battleground participation including the open BG roster, Darkness Falls,
     crew/group coordinator, alt trickle) does not recruit while the mode is
     active.
   - `AutonomousPopulationController` login placement: while the mode is
     active, a bot logs in at the castle.
   - Position and bind persistence: section 6.7.
   - Launcher: one mode selector (section 7).
5. **Off means identical.** Stage 1 adds a regression test class that
   asserts, with the mode off: the castle is not a safe area, sub-10
   autonomous bots keep their PvP protection, no bot is
   dispatched to the mode, no director is suppressed, and login placement is
   unchanged.
6. **Mode-specific rules stay inside the mode.** The grey/purple rule, the
   arena edge and the mode's group formation apply only to mode bots. Normal
   Camlann grey restraint (`CAMLANN_BOT_GREY_ENGAGE_CHANCE`), the level window
   in `AutonomousPvpOpportunityPolicy`, and the open BG keep their current
   values.
7. **Own log prefix**: `GOTH_PVP_` (for example `GOTH_PVP_ENTER`,
   `GOTH_PVP_GROUP_FORMED`, `GOTH_PVP_KILL`, `GOTH_PVP_RESTORE`), so mode runs
   can be measured without mixing with world-mode logs.
8. **Who takes part.** Only autonomous world bots. The owner's companion bots
   keep their companion behaviour and follow the owner. Battleground
   encounter bots are untouched. Real players are free and are not bound by
   the bots' con rule.

## 6. Behaviour design

### 6.1 Lifecycle of a mode bot

```
server start, mode on
  -> snapshot home (position, bind, camp, itinerary) once, if not already held
  -> log in at Caer Gothwaite, bind there
  CASTLE (safe): rest to full -> buff -> join or form a group -> leave
  ARENA: roam patrol points -> engage legal targets -> chase within limits
         -> regroup -> back to castle when low, out of power or the group wiped
  DEATH: release -> castle -> CASTLE
server start, mode off (after a mode run)
  -> restore each bot's home position and bind from the snapshot, then normal world
```

### 6.2 Target rule (G4)

A mode bot may open a fight only against a target that cons **green, blue,
yellow, orange or red** to it. Grey and purple targets are never opened on. This holds from level 1 (G6):
at low levels the con window is narrow, so a level 1 bot fights only the
few levels around it.
Implement this as a mode-only filter applied on top of
`AutonomousPvpOpportunityPolicy.Suitable` candidates. Don't change that
policy, and keep the 0% chance as a mode rule rather than a property change.

- **Self-defence:** if a real player (who isn't bound by the rule) attacks a
  mode bot from outside its window, the bot may fight back (the existing
  `retaliation` path). Bots don't break the rule against each other, because
  con is symmetric: a bot that sees a grey is seen as purple by it.
- Targets: other mode bots, real players and their companions, inside the
  arena only, never inside the castle circle.

### 6.3 Castle phase and groups (G3)

- **Bind and rest** at the castle bindstone. Rest to a set share of health and
  power before leaving (open BG precedent: `LandingRestPercent = 80`).
- **Buffs:** the normal class buff routine (`TryMaintainTravelAndClassBuffs`)
  and group buffs, run inside the castle before departure.
- **Group formation** is the mode's own simple former, not the world group
  coordinator:
  - It only groups bots that are in the castle and in the same **level band**,
    so members share legal targets. Starting band: ±5 levels, the same as
    `PreferredLevelDifference`.
  - Same guild or allied guild first, then strangers.
  - Mix of shapes: solos, duos, small groups (3–5) and full 8-man groups, in
    shares set in the settings file. Fill a group with a healer before a
    second of any other role when one is available.
  - The group leaves when it is full, or when a wait limit runs out with at
    least its minimum.
- The castle guards and merchants stay as they are (Camlann: city guards and
  PEACE NPCs are unattackable). Stage 0 checks that the castle and harbor
  guards don't attack bots of the other two realms.

### 6.4 Arena and patrol points (G5)

- The arena is **data**: one arena definition (region, safe circle, arena
  edge, patrol points, harbor and other named spots). Gothwaite is the first
  and only definition. More arenas (section 4) can be added later without
  code changes.
- **Patrol points** are hand-picked spots around the Isle of Glass: harbor,
  roads, crossings, hills and the approaches to the castle gate. Every point is
  proved reachable from the castle on the navmesh by a test, the same way
  `AutonomousBattlegroundDriver` proves its camps (chained route legs,
  `MaxRouteLegs`).
- Roaming: the leader picks a patrol point, prefers points where fights
  happened recently (heat), and moves on after a short dwell.
- **Engage:** scan for legal targets within the scan range (start from
  `AutonomousBattlegroundDriver.OpponentRange`, 2,400). Followers assist the
  leader, and class behaviour stays with the native class brain, as in the
  battleground driver.
- **Chase limit:** never follow a target into the castle circle or past the
  arena edge.

### 6.5 Low levels and mobs

- The arena edge is per level band. Levels 1–19 stay within about 20,000 units
  of the castle, where mobs are low and passive (section 3). Levels 20 and up
  may use the whole arena, about 30,000 units out. Stage 0 checks these
  numbers against patrol point reachability.
- Mode bots avoid aggressive mobs on their routes (reuse the mob-avoidance
  checks, read-only) and only fight a mob that attacks them.

### 6.6 Death, release and constitution

- Release goes to the castle bindstone. Group healers may resurrect in the
  field as usual.
- Release immunity is as on Camlann today (no fight right after release).
- **No constitution loss** for mode bots (G8). Other death handling (release
  immunity, resurrection) stays as in normal Camlann play.

### 6.7 Saves

- **Before** the first bot is moved, store each bot's home (region, X, Y, Z,
  heading, bind, current camp, itinerary) in a new table, e.g.
  `offline_gothwaite_pvp_home`. Adding a table doesn't break older saves. Never
  overwrite a stored home while the mode stays on.
- While the mode is active, the normal position and bind saves write the
  Gothwaite position. On the first start with the mode off, each bot is
  restored from its stored home, its snapshot row is cleared, and one
  `GOTH_PVP_RESTORE` line is logged per bot. After that, normal mode runs from
  the bots' old places.
- The launcher backs up the save database before the first switch into the
  mode.
- Never touch real player characters, inventories, loot, coins or the realm
  exchange.

### 6.8 Performance

All bots in one zone means far more targets per scan than in the spread-out
world. Stage 4 measures tick time at 300, 600 and 1,000+ bots with the
existing profiler (`BotThinkProfiler`) and the AI budget
(`AutonomousAiBudget`). It may add a recommended population cap for the mode,
shown in the launcher.

## 7. Settings and launcher

`gothwaite-pvp.json` (written by the launcher, read once at start):

| Key | Default | Meaning |
|---|---|---|
| `enabled` | `false` | The mode switch. |
| `arena` | `"gothwaite"` | The arena definition. |
| `safeRadius` | `4000` | Castle safe circle (the existing area radius). |
| `lowBandMaxLevel` / `lowBandEdge` / `edge` | `19` / `20000` / `30000` | Arena edges (section 6.5). |
| `groupShares` | solo 15, duo 15, small 35, eight 35 | Group shape mix (%). |
| `levelBand` | `5` | ± levels inside one group. |
| `restPercent` | `80` | Rest level before leaving the castle. |

Launcher: a **World mode** selector (Normal / Gothwaite PvP), editable only
while the server is stopped. It is separate from the population preset and
type-mix controls, which stay as they are and are simply ignored while the
mode is on. When Gothwaite PvP is selected, a line explains that bots don't
level, raid or siege and that positions are restored when the mode is
switched off.

## 8. Stages

Each stage is a separate task with its own version bump. Stages 1–5 change
gameplay or launcher behaviour and keep the existing save, so they are MINOR
bumps. Automated tests run only when the owner asks. Real-client checks
belong to the owner.

**Stage 0: audit (docs only, PATCH).**
- List every director, scheduler and login path that moves or recruits world
  bots, and record the exact touchpoints for section 5.4.
- Check that the castle and harbor guards don't attack bots of other realms.
- Pick 20–40 patrol points. Prove reachability from the castle with a
  navmesh probe like `UT_SeptemberFourteenInstalledNavigation`.
- Re-check the mob table and arena edges.

**Stage 1: switch and separation.**
- Settings file and `GothwaitePvpMode`, launcher selector, the `ThinkCore`
  branch with a stub driver (bots idle in the castle), director guards, the
  castle safe circle, the home snapshot and restore table.
- Acceptance: the mode-off regression tests (section 5.5), including that
  sub-10 autonomous bots stay protected with the mode off. With the mode on,
  every autonomous bot logs in at the castle and nothing recruits it. With the
  mode off again, every bot is back at its stored home.

**Stage 2: castle life.**
- Bind, release to the castle, rest, buff, the level-band group former,
  departure.
- Acceptance: groups form within their band. Nobody fights inside the circle.
  Released bots come back to the castle.

**Stage 3: arena PvP.**
- Patrol roaming, the engage filter (section 6.2), chase limits, the per-band
  arena edge, mob avoidance, return to the castle when low or wiped.
- Acceptance: kills happen across all bands, including levels 1–9. Grey and
  purple are never opened on (`GOTH_PVP_KILL` logs attacker and victim con). No bot stays outside the
  edge, and none is stuck.

**Stage 4: performance and polish.**
- Load measurement and a recommended cap, a `/gothpvp status` command (bots per
  band, groups, kills, deaths), kill chat through the existing chat system.

**Stage 5: live check and handover.**
- A mode run and switch-back on the owner's server, then a world-mode run to
  confirm nothing changed there.

## 9. Decisions on the open questions (owner, 2026-10-13)

| # | Question | Decision |
|---|---|---|
| O1 | Do XP, realm points and levels earned in the mode carry over to normal mode? | **Yes** (G7). Only position and bind are restored, so this is not a full sandbox. |
| O2 | Constitution loss for mode bots? | **No** (G8). |
| O3 | Do new alts appear while the mode is on (alt trickle)? | **No, paused** (G9). |
| O4 | May mode bots attack the owner's own character and companions? | **Yes**, under the same con rule (G10). |
| O5 | Is Gothwaite Harbor a second safe area? | **No** (G11). |
| — | Minimum PvP level in the mode? | **Level 1** (G6). |

## 10. Risks

- **Load:** see 6.8. This is the biggest unknown.
- **Thin level bands:** with a small population, some bands may have too few
  bots for fights. The status command shows it, and the owner can raise the
  population.
- **Navmesh gaps** around the Isle of Glass: stage 0 proves every patrol point.
  Don't rebuild the navmesh globally to fix one route (`AGENTS.md`).
- **Leaks into normal mode:** guarded by the separation contract and the
  mode-off regression tests. Any new touchpoint must be added to section 5.4.

## 11. Stage 0 audit results (2026-10-13)

Read-only audit of the source and of a copy of the installed world database
(`before-classic165-spawns-20260926-115731`). No navmesh probe yet; stage 3
proves the patrol points with an installed-mesh test.

### 11.1 Touchpoints (completes section 5.4)

| Hook | Place | Mode-on effect |
|---|---|---|
| Brain branch | `BotBrain.ThinkCore`, before the battleground participant branch | Mode bots run `ThinkGothwaitePvp()`. This alone cuts off the world controller, keep defense, frontier, RvR events, siege, guild armies, Darkness Falls camps, seal shopping, capital transit and the stuck watchdog. |
| Coordinator | `AutonomousBotGroupCoordinator.PrepareCoordinatorTick` | Returns at once: keep-defense pulse, realm event controls, realm raids and forced parties, raid schedule, objective reconcile and world group formation stop. |
| Open BG | `OpenBattlegroundRoster.Tick`, `OpenBattlegroundDirector.Tick`, `AutonomousBattlegroundParticipation.DirectorCap` and its admission | No autonomous bot is recruited or admitted. Real players' battleground access is unchanged. |
| Attack observers | `AutonomousRvrEventLayer.ObserveKeepAttack`, `AutonomousGuildKeepDefense.ObserveAttack`, `AutonomousRealmRaid.ClaimNearbyDefenseBroadcast` | No defense calls; arena fights would otherwise pull bots away on every hit. |
| Alt trickle | `AutonomousPopulationController.TryCreateAlt` | Paused (G9). |
| Login | `AutonomousPopulationController.SpawnOnGameLoop`, after the stored/fresh placement | Mode bots are placed at a castle landing. `RepairInvalidLoginPosition` still runs and accepts it. |
| Home snapshot | `AutonomousPopulationController.OnServerStarted`, before the roster loads | Snapshot (mode on) or restore (mode off) of `offline_gothwaite_pvp_home`. |
| Release | `GameBot.ReleaseToNearestBindAndReturn`, after `SpreadSafe` | Mode bots release at a castle landing. |
| Safe area | `PvpCombatant.IsSafeArea`, `IsSafeReleasePoint` | Castle circle is safe. |
| Sub-10 rule | `PvpCombatant.IsSafetyProtected(GameLiving)` bot arm | Lifted for mode bots (G6). `BlocksLowLevelAutonomousPvp` and `BlocksAutonomousPvp` follow without edits. |
| Constitution | `AutonomousBotRealmPointRewards.Award`, the `PVP_DEATH_CON_LOSS` write | Skipped for mode bots (G8). Bots only count lost constitution; nothing applies it to their stats today. |
| Switch | `GameServer.Start`, next to `AutonomousBotGoalPolicy.Initialize` | Reads `gothwaite-pvp.json` once. |

Findings that shaped the design:

- No director needs its own guard beyond the table: `AutonomousRvrDirector` is
  test-only, `AutonomousRealmLoginBalancer.SelectNext` has no callers, and
  realm raids are already hard-off (`RealmRaidRecruitmentPolicy`).
- `OpenBattlegroundPolicy.IsEnabled` must not be used as the switch; it also
  stops player battlegrounds.
- `offline_world_bots` stores region, X, Y, Z, zone, bind, camp and itinerary
  but **no heading**, so the snapshot cannot restore heading.
- A bot's stored bind is read only at death release; the mode does not rewrite
  it, because mode bots always release at the castle.
- New `[DataTable]` classes are created at database init, so the home table
  needs no registration and leaves older saves alone.
- Autonomous bots in a group led by a real player keep their normal
  behaviour (they follow that player).

### 11.2 Guards and NPCs

- Town guards are `GameGuard` with `GuardBrain`; `PvPServerRules` never lets a
  realm NPC that is not a keep guard attack a realm-bearing target. The 5
  castle and 4 harbor guards (all Albion) therefore leave Midgard and Hibernia
  bots alone, and bots cannot attack them or PEACE NPCs.
- Castle bindstones: 534,978 / 548,786 and 535,083 / 548,794 (Z 4,800). The
  `Caer Gothwaite` area row has Z 4,755. Teleport rows to Gothwaite exist only
  for Albion; mode bots are placed directly, so this does not matter.

### 11.3 Area re-check

- Zone 51 bounds confirmed. Zones 52–57 are the other islands of region 51.
- The mob table in section 3 reproduces exactly when distance is measured from
  the teleporter landing (535,158 / 548,589). From the area centre the counts
  differ by at most 2 per band (64 / 132 / 295 in the 10–30k bands). The edges
  in section 6.5 stand: no aggressive mob within 15,000 and only 6 within
  20,000.
- The castle is 8,243 units from the zone's south edge. South of y 551,000 is
  coast; the playable land lies north and west of the castle.
- 30 candidate patrol points were picked (18 low band within 20,000, 12 high
  band up to 26,500), at least 2,000 apart, away from water, doors and
  aggressive mobs for the low band. Their Z values are estimates; stage 3 snaps
  them to the navmesh and drops any that do not route from the castle.

## 12. Built so far

### Stage 1 (0.262.0)

- Code: `source/server/GameServer/bots/gothwaite/` (`GothwaitePvpSettings`,
  `GothwaitePvpMode`, `GothwaitePvpArena`, `GothwaitePvpRules`,
  `GothwaitePvpHomeRecord`, `GothwaitePvpHomeStore`, `GothwaitePvpDriver`,
  `BotBrain.GothwaitePvp`). Touchpoints as in section 11.1.
- Launcher: World mode row on the Server population tab
  (`BotGoalsSettingsControl`), saved with the population settings. The first
  switch into the mode backs up the save with `SaveDatabaseBackup` to
  `data/gothwaite-pvp-backups/`.
- Deviations from section 5.4, decided in review:
  - `AutonomousBattlegroundParticipation.DirectorCap` is not zeroed: it also
    sizes the campaign battlegrounds' NPC director squads that real players
    fight. Admission (`Admit`, `AssignOpenUnit`) is guarded instead, so no
    autonomous bot enters.
  - The coordinator guard also pauses realm event notices and the GM realm
    event controls while the mode is on.
  - Heading is not stored (no column); a bot logging in at the castle faces
    the castle centre.
- Logs: `GOTH_PVP_MODE`, `GOTH_PVP_SNAPSHOT`, `GOTH_PVP_RESTORE`,
  `GOTH_PVP_ENTER`, plus `GOTH_PVP_*_FAILED` lines on errors.
- Tests: `UT_GothwaitePvpModeOff`, `UT_GothwaitePvpRules`,
  `UT_GothwaitePvpHome`; launcher `GothwaitePvpSettingsTests`.

### Stage 2 (0.263.0)

- Driver phases: Castle (rest → buff window → ready), Departing, Arena.
  `GothwaitePvpDriver`, `GothwaitePvpGroupFormer`, `GothwaitePvpShapes`,
  `GothwaitePvpCastleRules`, `GothwaitePvpNavigation`, `GothwaitePvpLines`.
- Rest: the bots' recovery rest to `restPercent`, capped at 2 minutes; then an
  8-second buff window while the class routine buffs.
- Former: ticked every 2 s from `NpcService.Tick` (service phase, one thread;
  one guarded line). Shapes drawn per bot from `groupShares`; joiners within
  ±`levelBand` of the leader; same guild (2), same alliance (1), then
  strangers; a healer (`BotPartyRoles.IsHealingClass`) before a second
  non-healer. Full → depart; after 90 s, at least the minimum (duo 2, small 3,
  eight 5) → depart, else disband.
- Departure: the leader walks to the castle gate (`castle approach N`, floor
  Z 3,898); followers follow beyond 300 units and assist the leader's target
  only if it is legal for them (green to red, outside the castle). In stage 2
  groups hold at the gate.
- Release: the bot rests again in the castle; it leaves its group unless that
  group is still gathering in the castle with its leader there.
- Group removal by existing code was audited: only a player's `/invite` takes
  a mode bot out of its group, and that bot then leaves the mode (player-led).
- New logs: `GOTH_PVP_REST`, `GOTH_PVP_REST_END`, `GOTH_PVP_GROUP_FORMED`,
  `GOTH_PVP_GROUP_DISBANDED`, `GOTH_PVP_GROUP_DISSOLVED`, `GOTH_PVP_DEPART`,
  `GOTH_PVP_ARRIVE`, `GOTH_PVP_RETURN`, `GOTH_PVP_ROUTE_FAILED`.
- Navmesh probe: all 30 candidate patrol points snap and route from the
  castle landing in one leg with the blocking-door filters.

### Stage 3 (0.264.0)

- Patrol points: 29 points in `GothwaitePvpArena.PatrolPoints`, snapped to the
  installed navmesh and asserted to route from the castle by
  `UT_GothwaitePvpArenaInstalledNavigation` (explicit; needs
  `OFFLINE_DAOC_NAV_ROOT` and Windows Detour). A bot uses the points within
  its edge (`lowBandEdge` up to `lowBandMaxLevel`, else `edge`); a group uses
  its lowest living member's edge.
- Roaming (`GothwaitePvpArenaRules`): weight (1 + heat) / (1 + distance /
  10,000), never the point just left, stalled points skipped for 5 minutes;
  dwell 15–40 s. Heat = kills within 2,500 units, fading over 10 minutes.
- Opening rule: `GothwaitePvpTargetRules.MayOpen` (player-shaped, green to
  red to the bot and, for a pet, to its owner; outside the castle; inside the
  edge; not an alliance mate; server-legal). Scan every 1.5 s at 2,400 units.
- Class brain: `GothwaitePvpSelfDefence` filters aggro adds, the next target,
  proactive aggro, retaliation, guild-mate calls, crowd control, PvE add
  control, servant payloads, FNF turrets and every harmful area cast
  (`GameLiving.CastSpell`): a mode bot fights only `MayOpen` targets or those
  that attacked it, its group or its pet. Area spells are refused when a
  living thing it may not fight (a monster included) stands in the area.
- Chase limit: castle, target beyond the edge, or the bot beyond edge +
  1,000 → the fight is dropped (`GOTH_PVP_CHASE_LIMIT`).
- Returns: health < 35%, caster power < 20%, leader lost (no living member
  within 2,400), group wiped. A returning leader hands the group to the first
  living arena member (`GOTH_PVP_GROUP_LEADER`) or the group dissolves.
- Stuck: 96 units of progress per 45 s; two repicks, then home
  (`GOTH_PVP_STUCK`).
- Kills: `AbstractServerRules.OnNpcKilled` / `OnPlayerKilled` call
  `GothwaitePvpStats.RecordKill` (`GOTH_PVP_KILL` with levels, kinds and both
  con colours). A mode bot killed by a monster is logged as `killer_kind=other`.
