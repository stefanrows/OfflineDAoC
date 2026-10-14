# Gothwaite PvP mode: plan

**Status:** in progress. Stage 0 (audit) done 2026-10-13 (0.261.2), results
in section 11. Stages 1–4 built in 0.262.0–0.265.0 (section 12), stage 6
(player tactics) in 0.268.0 with review fixes in 0.269.0 (section 13). Open: the owner's load run (section
12, stage 4), stage 5, the live check, and the stage 6 live check (13.4).

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
| `gateRing` | `1500` | No-fight ring outside the safe circle (0.266.0): no openings within `safeRadius + gateRing`; self-defence allowed. |

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
| Spell impact (0.267.0) | `SpellHandler.DamageTarget(ad, show, result)` | A bolt, archery spell or DoT tick between player-shaped sides deals no damage while either side stands in the castle circle (`GothwaitePvpRules.BlocksSafeImpact`). |
| Sub-10 rule | `PvpCombatant.IsSafetyProtected(GameLiving)` bot arm | Lifted for mode bots (G6). `BlocksLowLevelAutonomousPvp` and `BlocksAutonomousPvp` follow without edits. |
| Constitution | `AutonomousBotRealmPointRewards.Award`, the `PVP_DEATH_CON_LOSS` write | Skipped for mode bots (G8). Bots only count lost constitution; nothing applies it to their stats today. |
| Switch | `GameServer.Start`, next to `AutonomousBotGoalPolicy.Initialize` | Reads `gothwaite-pvp.json` once. |
| Kill order (0.268.0) | `BotBrain.ApplyRvrCombatHabits`, first line | A mode bot's next target comes from `GothwaitePvpCombat.ChooseTarget` (section 13); null keeps the threat order. |
| Stealth gates (0.268.0) | `BotBrain.TryEnterStealthWhileIdle`, `BotBrain.TryStartStealthOpener`, `BotRvrAmbush.CanUseStealthOpener` | A mode hunter (`GothwaitePvpStealth.Applies`) keeps stealth while idle and may use its stealth opener. |

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

### Stage 4 (0.265.0)

- `/gothpvp` or `/gothpvp status` (GM): mode bots by band and phase, groups,
  mode bot kills and deaths per band, kills by con (self-defence included, so
  grey and purple can appear there only from fights the other side opened),
  and the turn load. `GothwaitePvpStatus`, `GothwaitePvpCommand`.
- Load: `GOTH_PVP_LOAD` once per real minute while the mode is on
  (`GothwaitePvpLoadMeter`: bots, turns, turnAvgMs, turnP95MsAtMost,
  turnMaxMs, scans, scanAvgMs, formers, formerAvgMs, formerMaxMs).
  `BOT_THINK_PROFILE` also gets the phases `GothwaitePvpDriver`,
  `GothwaitePvpScan` and `GothwaitePvpFormer`, but it lists only its top 16
  phases, so `GOTH_PVP_LOAD` is the reliable source.
- Kill chat (`GothwaitePvpKillChat`): 15% of a mode bot's arena kills, at most
  once per bot per 2 minutes and once per arena per 30 seconds, local say only
  when a player is within 512 units. The existing bot PvP taunt
  (`AutonomousBotChatCoordinator.OnAutonomousBotPvpKill`) can still speak for
  the same kill.
- No population cap is built: it waits for measured numbers.

#### Measuring the load (owner)

1. Stop the server. Set World mode to Gothwaite PvP and the bot population to
   300 on the Server population tab, world speed 1×. Start.
2. Wait 15–20 minutes, then check `/gothpvp`.
3. Collect about 10 minutes of `GOTH_PVP_LOAD` lines from
   `runtime/logs/server-console.log`; note turnAvgMs, turnP95MsAtMost,
   turnMaxMs, scanAvgMs and formerMaxMs. Cross-check `BOT_THINK_PROFILE` and
   any `Long NpcService.Tick` warnings.
4. CPU estimate: bots × 1.33 turns/s × turnAvgMs. Turn p95 must stay far
   below the 750 ms think interval.
5. Repeat at 600 and 1,000 bots, then choose a recommended cap.

### First live run fixes (0.266.0)

Owner's run, 300 fresh level-1 bots: all 71 kills were 4,000–4,300 units from
the centre (at the north gate); 83 groups departed, 10 reached the gate point,
50 dissolved (31 leader lost). Some bots logged in at Camelot (an off-floor
spread landing failed the login check) and some routes from the courtyard to
the gate were dead ends. Load was 0.09 ms per bot turn at 138 bots.

- No-fight ring: `MayOpen` refuses when the bot or the target is within
  `safeRadius + gateRing` (5,500); the scan does not run there; an opened fight
  that drifts into the ring ends (attackers are still fought back). Patrol
  goals must lie beyond the ring + 500 (6,000), so GP01 is no longer a goal.
- The gate is a waypoint: a leader roams as soon as it reaches the gate or
  leaves the ring. Its first pick is uniform over the allowed points; later
  picks use heat.
- Departure gate (`GothwaitePvpDepartureGate`): one group or solo leaves per
  4 s, earliest due first.
- Castle landings: 16 courtyard spots CL01–CL16 (sampled on a 60-unit grid,
  232 of 239 candidates passed), each snapped tight on the plateau and routed
  to the gate and to CL01 by the explicit probe; a bot gets one by id with at
  most 15 units of spread.

### Second live run fixes (0.267.0)

Owner's run, 300 bots at levels 7–19, observed 2026-10-13 21:11–21:23 with a
read-only position sampler on the save database (every 15 s):

- On average 213 of the 300 bots stood in the castle, 31 between the safe
  circle and the ring, 54 between 5,500 and 7,000 units, and fewer than one
  beyond 7,000. Only 6 position samples in 12 minutes were within 4,000 units
  of the harbor. The arena bots stood in one line at y 543,000–543,500, x
  532,500–538,000: the ring's edge below the north gate.
- 778 of 892 kills were 5,000–6,000 units from the centre. The ring dropped
  916 fights in 10 minutes (`GOTH_PVP_CHASE_LIMIT reason=ring`).
- Cause: every group crosses the ring at the same spot and may open fights at
  once, so it fights the next group out of the gate. A target that steps
  back into the ring ends the fight, and the leader then walked to the
  *nearest* patrol point, next to the gate. Fight heat was uncapped (about
  100 near the gate against 1 elsewhere), so later picks also went back
  there.
- Departures were held to one per 4 s, but the former ticks every 2 s, so
  only 15 groups or solos left per minute (230 of 531 were solos). Bots died
  within a minute of leaving, so the rest queued in the castle.

Changes:

- **Outbound leg.** Out of the gate the leader picks its first patrol point
  evenly over the allowed points and walks there. Until it arrives, or for at
  most 3 minutes, the group opens no fight: `GothwaitePvpTargetRules.MayOpen`
  refuses, the scan does not run, and followers copy their leader's state. An
  outbound group still fights back as a group: a follower assists its
  leader's target when `GothwaitePvpSelfDefence.MayFight` allows it, so a
  legal opening or an attacker of the group.
- **Ring and castle drops go outward.** A fight dropped at the ring or the
  castle sends an arrived leader on a new outbound leg to a freshly picked
  point. An edge drop still walks to the nearest point inside the edge.
- **Spread.** Heat counts up to 3 (`HeatCap`). Every pick divides a point's
  weight by 1 + the number of other living leaders and solos that have
  claimed it.
- **Dwell** 6–15 s (was 15–40 s).
- **Departures** one per second on average, two per former tick
  (`GothwaitePvpDepartureGate`, `CatchUpMilliseconds`). An idle gate builds
  no burst.
- **Castle deaths.** 73 of 1,084 kills in this run happened at the
  victim's castle landing or release point. All the killers whose class
  showed in the log fight with bolts, arrows or DoTs: their damage lands
  after the cast and did not check the safe area again, while melee and bot
  bow shots do. A guarded hook in `SpellHandler.DamageTarget` (the one call
  that bolts, archery spells and DoT ticks share) now drops that damage while
  either player-shaped side stands in the castle circle. A few kills by
  Scouts (bow shots, which do check at impact) also logged the landing as
  the death position. Their cause is not found yet; a later run with the
  kill position taken from the killing hit would show it.
- Logs: `GOTH_PVP_OUTBOUND bot spot distance reason` (gate, ring, castle,
  repick) and `GOTH_PVP_OUTBOUND_END bot reason distance` (arrived, time).

Live check: positions spread over the island and the harbor, kills spread
by distance rather than piled at 5,000–6,000, far fewer than 900
`reason=ring` drops per 10 minutes, and departures above 15 per minute when
many bots are ready.

## 13. Stage 6: player tactics (0.268.0)

Built 2026-10-14. The question was: how did DAoC players actually fight, and
what of that was missing from the mode bots? The answer came from the sources
in 13.1 and from the log of the third live run (0.266.0, 2026-10-13
21:04–21:59, about 300 bots): 2,538 kills, 1,061 departures (473 solos, 237
eights), 753 groups dissolved as `leader_lost` (the leader died first), 505
returns for `low_power` or `low_health` straight after a fight, and 2,348
`reason=ring` chase drops. The bots opened on the nearest legal target
whatever its party, fought to the death, and the group fell apart as soon as
its (randomly chosen) leader fell.

### 13.1 What players did (evidence)

Read 2026-10-14. Forum threads are player accounts of their time, not
systematic data; they agree on the habits below.

| Habit | Source |
|---|---|
| The assist train: one main assist, everyone `/assist`s it; the MA is a melee with Charge and Determination (Berserker, Savage, Warrior with the 9 s slam); "stick to the stickleader, who is the only one running". | [DAoC Wiki: Warrior](https://camelotherald.fandom.com/wiki/Warrior); Uthgard "[RvR] A brief guide to group RvR" (search excerpts, the page itself refuses fetches). |
| Kill order: healers and mezzers first; "kill casters by keeping them locked down with CC/interrupts until the DPS gets around to them"; "ASSIST, ASSIST, ASSIST"; "the absolutely worst thing you can do when pushing into a group is to over-commit 2–3 classes to a single target". | [DAoC Wiki: Healer](https://camelotherald.fandom.com/wiki/Healer), [Spiritmaster](https://camelotherald.fandom.com/wiki/Spiritmaster); Uthgard tank guide excerpt. |
| Peel and don't overextend: "Am I more valuable hitting someone right now, or stopping someone from hitting my teammates?"; "don't overextend to the point of being cut off from your healers"; "do I have a realistic chance to kill this target?". | [Blackthorn: Mercenary guide](https://blackthorn-daoc.com/wiki/mercenary-guide); [Eden: Friars in 8v8](https://eden-daoc.net/viewtopic.php?t=97). |
| Pick your fights: a small-man hunts solos, duos and small-mans and avoids full groups; "this ability to pick your fights is one of the strongest mechanics in DAoC PvP"; "if not, they vanish and move on". | [Blackthorn: Infiltrator guide](https://blackthorn-daoc.com/wiki/infiltrator-guide); [docs/RVR_GROUP_DOCTRINE.md](RVR_GROUP_DOCTRINE.md) (the fork's own evidence base, D-numbered). |
| Retreat: "back off when the healer dies", "retreat at 2:1", "hide immediately when you see groups, or stay with the group and have group purge ready", "DoT them, snare them, and peg it", "retreat to the nearest safe zone". | Uthgard Druid RvR healing guide excerpt; [Freddy's House: Reflections of a Thidranki Infiltrator](https://forums.freddyshouse.com/threads/reflections-of-a-thidranki-infiltrator.105399/). |
| Spacing: "there is nothing inviting mezzes and rootspam like two healers standing within 350 locs of each other. Keep your distance from your group mates"; healers behind the tanks. | Uthgard Healer guide excerpt; RVR_GROUP_DOCTRINE formation notes. |
| Rest and rez after a fight instead of releasing: "come back and let you guys rez, that way you don't have to release"; the rez cycle with few healers "dragged on". | [Freddy's House: lessons from Thidranki](https://forums.freddyshouse.com/threads/yawns-lessons-from-thidranki.102553/). |
| Adds and zergs: early Camlann "became crowded with adds"; Thidranki groups "zerged by more than twice our number", groups "attacked from both sides"; pickup groups "follow bigger fights". | [Freddy's House: Save Mordred](https://forums.freddyshouse.com/threads/save-mordred-once-for-all.244577/); lessons from Thidranki (above). |
| Stealthers: hunt the road between the hubs, take solo hunters and casters ("very rarely will you struggle to kill these from stealth, if they're solo"), leave "targets with a group around them", wait for the drop ("line yourself up so they walk into your PA"), "pick the weakest one in the crowd", run after one kill. | Reflections of a Thidranki Infiltrator (above); Blackthorn Infiltrator guide. |
| Camping the safe spot when there is nothing to fight: "when there is no one to fight they run to the PK to look for a fight"; defenders "run immediately back to the PK". | Lessons from Thidranki (above). This one the mode already has: the castle is the PK, and the no-fight ring keeps the camp from becoming the whole game (0.266.0–0.267.0). |

Not found: any account of Gothwaite itself on Camlann or Mordred beyond the
general "goth fights at the harbor" memory the plan already records; video
commentary with usable tactics (the search results were event footage without
narration).

### 13.2 What the mode does now

All of it lives in `bots/gothwaite/` (`GothwaitePvpTactics` pure rules,
`GothwaitePvpCombat` live glue, `GothwaitePvpStealth`, driver and former
changes) and applies to mode bots only. The shared world-bot code is read
(`AutonomousRvrDoctrine.Derive`, `ShouldRetreat`, `StickMilliseconds`,
`AutonomousRvrCombatHabits.Choose` and `Weight`, `AutonomousRvrStealthLoop`'s
pure scoring, `AutonomousRvrDoctrineGeometry.TravelSlot`), never changed.

1. **Doctrine.** When a group departs, its doctrine is read from its members
   the way the world warbands read theirs (small-man, assist train, melee
   train, bomb group, caster duo, stealth pack, gank squad, archer group,
   pickup, lone roamer, solo assassin), tuned by the leader's Aggression,
   RiskTolerance and Patience. `GOTH_PVP_DEPART ... doctrine=SmallMan`.
2. **A tank leads.** `GothwaitePvpTactics.LeaderRank`: guard tank 0, melee
   1, archer 2, caster or stealther 3, healer 5. At departure the best-ranked
   ready member takes the lead (`GOTH_PVP_GROUP_LEADER reason=main_assist`),
   and a promotion after the leader's death takes the best-ranked living
   member in the arena.
3. **Kill order.** `BotBrain.ApplyRvrCombatHabits` has one guarded line that
   returns `GothwaitePvpCombat.ChooseTarget` for a mode bot: stickiness by
   doctrine and Patience, the leader's call for damage dealers (1–2 s delay,
   5–15 % ignored), interrupt awareness (archers, melee in reach, instant
   casters look again when an enemy caster casts), and the weights of
   `AutonomousRvrCombatHabits.Weight` (healers 3×, mezzers, wounded, whoever
   hits our healer or caster 1.6×, pets 0.35×).
4. **Picking fights.** The scan (`GothwaitePvpCombat.FindOpening`) collects
   every legal opening within 2,400, judges each by
   `GothwaitePvpTactics.TakesFight` (own living members near the bot ×
   appetite × level factor against the target's living party within 2,000;
   a solo always takes a solo of its con; a dare roll stable per minute lets
   an eager group try a bigger one), then draws by the kill-order weights.
   Refusals count once per bot per minute in `/gothpvp`.
5. **Retreat.** A leader weighs its fight every 3 s with the doctrine's
   `ShouldRetreat` (healer dead +0.45, half down +0.4, outnumbered 1.5:1
   +0.35, the doctrine's bias, a 15 % stay roll); a solo flees with 2+
   player-shaped attackers on it or below 40 % health against an enemy above
   60 %, by its RiskTolerance. The run lasts 20–35 s toward the castle exit
   with sprint; `GothwaitePvpSelfDefence.MayFight` is false meanwhile, so a
   running bot fights nothing and followers run behind the leader. At the
   end a leader with 60 %+ health starts a new outbound leg; below that it
   goes home. `GOTH_PVP_RETREAT bot reason health power`,
   `GOTH_PVP_RETREAT_END bot reason=resume|home health`.
6. **Regroup.** When an arrived leader's fight ends, the group holds: the
   leader and the followers at their slots sit (the recovery rest, woken by
   any hit) until every living member is within 600 and the lowest health is
   70 % and power 50 %, or 45 s pass. No scan runs meanwhile; self-defence
   does. Low health and power do not send a member home during the regroup;
   a lost leader or a wipe does. `GOTH_PVP_REGROUP bot reason=start|recovered|time|fight`.
7. **Formation.** A follower's goal is its travel slot
   (`AutonomousRvrDoctrineGeometry.TravelSlot` with the leader's facing):
   column for small-man, assist train, melee train; loose fan for stealth
   doctrines; the leader's spot for clump doctrines. Order: guards, speed,
   melee, healers, casters and archers, stealthers wide. A slot the navmesh
   refuses falls back to the leader's spot.
8. **Chase discipline.** `GOTH_PVP_CHASE_LIMIT reason=overextended` (a
   follower's target beyond 1,500 of the leader) and `reason=far` (a leader
   2,500 from where its fight began), unless the target is below 25 % or is
   still attacking the group.
9. **Adds.** Every 10 s a roaming leader (not a hunter, not outbound) looks
   for the newest kill within 5,000 in the last 45 s, outside the roam floor
   and inside its edge, and walks there with chance 0.25 + Aggression/200.
   `GOTH_PVP_ADD bot fight=x,y distance`.
10. **Hunting stealthers.** `GothwaitePvpStealth.Applies`: an assassin or
    archer (not a Minstrel) with the Stealth skill, alone or in a stealth
    doctrine. It stealths outside the castle whenever it is not fighting
    (the brain's idle gate and opener gate accept it through one guarded
    condition each), dwells 30–90 s at a point, opens only by
    `StealtherMayOpen` (lone, a soft class in a duo, or below 50 %), scores
    victims with the stealth loop's rules, approaches stealthed and uses its
    stealth opener, then breaks away on a 20 s outbound leg.
    `GOTH_PVP_AMBUSH bot target party health`.

### 13.3 Mode-off contract

Four guarded touchpoints were added, all answering "no change" with the
mode off (`UT_GothwaitePvpModeOff.TheStageSixTacticsHooksAnswerNoChangeWhileTheModeIsOff`):
`BotBrain.ApplyRvrCombatHabits` (mode bot → mode kill order),
`BotBrain.TryEnterStealthWhileIdle` and `TryStartStealthOpener`,
`BotRvrAmbush.CanUseStealthOpener` (mode hunter → stealth allowed). They are
listed in the touchpoint table of section 11.1.

### 13.4 Live check (owner)

Run 300 bots for 15–20 minutes and compare with the 0.266.0 run above:

- `GOTH_PVP_GROUP_DISSOLVED reason=leader_lost` well below 753 per hour, and
  `GOTH_PVP_GROUP_LEADER reason=main_assist` on most multi-class departures.
- `GOTH_PVP_RETREAT` and `GOTH_PVP_RETREAT_END reason=resume` present;
  returns for `low_health`/`low_power` well below 505 per hour.
- `GOTH_PVP_REGROUP reason=recovered` more often than `reason=time`.
- `GOTH_PVP_AMBUSH` lines from solo assassins and archers, with `party=1`
  dominant; `GOTH_PVP_ADD` lines from groups.
- Kills per departure above 2.4 (2,538 / 1,061), and the `/gothpvp` tactics
  line counting all six kinds.
- Watch for a regression: bots sitting (regroup) when a player walks up
  should stand and fight when hit; a retreating group should not stop at the
  ring with nothing to do (it starts a new leg or goes home).

### 13.5 Review fixes (0.269.0)

An Opus review of 0.268.0 (2026-10-14) found one blocker and seven defects in
the per-turn flow, all fixed the same day: a follower promoted to leader
mid-run kept an open-ended run (followers now copy the leader's run clock,
`RetreatEndOf`, and a promoted leader's open clock ends at once); run and
regroup state leaked into the next outing (`ClearTactics` on every return
path and in the castle, sprint off with it); a running bot never fought back
even when caught (`CaughtWhileRetreating`: crowd-controlled, run goal
reached, or an attacker in melee range; the self-defence rule allows the
fight and the run ends with `reason=caught`); followers still went home for
low health or power during the leader's regroup; followers flapped between
assist and the overextension drop (`AssistLeader` now refuses such a target);
the "far" chase was measured from the leader's spot (now from the target's
position when the fight opened); the add draw walked back to the group's own
last kill (fights within `AddOwnRange` 1,000 are skipped); and an off-mesh
formation slot was proved twice per turn (`SlotRetryMilliseconds` 5 s). Open
nits: the former's lock is taken several times per follower turn (march
slots could be cached per tick), stealthed hunters are invisible to every
scan (no stealther-to-stealther detection), and no test drives the live
retreat or regroup flow (the rules are pure-tested only).
