# Open BG together with the Gothwaite PvP mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** With the Gothwaite PvP world mode on, the open BG keeps working through a reserved set of world bots (configurable level band) while every other bot stays a Gothwaite mode bot; the crew level band becomes configurable in `/bgsetup`.

**Architecture:** One pure unit `GothwaitePvpOpenBgReserve` (in `bots/gothwaite/`) chooses the reserved set once at start; `GothwaitePvpMode.IsModeBot` excludes reserved bots, so the existing mode hooks leave them alone; a standby driver parks reserved bots at their border hub between deployments; guarded open-BG hooks recruit only reserved bots while the mode is on.

**Tech Stack:** C# (.NET 10, `source/server/GameServer`), NUnit in `source/server/Tests/UnitTests`, built from WSL with `tools/dev/winnet.sh`.

**Spec:** `docs/superpowers/specs/2026-10-15-open-bg-gothwaite-design.md` (read it first, sections 2-4 hold the decisions and the touchpoint list); contract: `docs/GOTHWAITE_PVP_MODE.md` sections 5 and 11.1.

## Global Constraints

- Mode off is bit-identical: every new predicate begins with `IsActive`; with the mode off `SuppressesOpenBattlegroundCrew`, `RefusesOpenUnit` and the standby are false and `MayJoinOpenBattleground` is true. Open BG without the mode behaves as before (bands default 40 / 50).
- Mode logic stays in `source/server/GameServer/bots/gothwaite/` (file prefix `GothwaitePvp`); existing files get only guarded one-line calls (contract section 5.4).
- The coordinator's early return in the mode stays; do not run the world objective reconcile for mode bots.
- Preserve each file's line endings; check `git diff --stat` for whole-file churn after every edit. New files LF.
- Version: one MINOR bump when finished (0.278.0 -> 0.279.0): CHANGELOG heading, `source/tools/OfflineDaoc.Launcher/MainForm.cs` (`DisplayVersion`), `source/tools/OfflineDaoc.Launcher.Tests/LauncherPresentationTests.cs`, `ALL SERVER COMMANDS.txt` header (CRLF file).
- Build only into the default output tree; never into the install. Run tests with filters named per task. Known baseline: none expected to fail in the filters used here.
- Commands from `C:\OfflineDAoC-wt-obgwindow`, `export OFFLINE_DAOC_ROOT=/mnt/c/OfflineDAoC/playable`. Build: `tools/dev/winnet.sh build source/server/Tests/Tests.csproj -c Release --no-restore 2>&1 | grep -E ' error |Fehler$'`. Test: `tools/dev/winnet.sh test source/server/Tests/Tests.csproj -c Release --no-build --no-restore --filter "<filter>" 2>&1 | grep -E 'Fehler|Bestanden'`.

## Review Focus

- A reserved set that is empty (small population, band above the bots' levels, open BG disabled) must leave the mode exactly as today and must not throw.
- `IsModeBot` is a hot path on brain threads: the set is immutable (`FrozenSet`/`HashSet` published with `Volatile`), no locks.
- A bot in the reserved set that is also a player companion, temporary helper, battleground encounter bot or player-led group member must never be treated as reserved (those keep their own behaviour).
- Mode on, reserved bot released after death: it must release at the open BG landing or its own hub (not the Gothwaite castle) and not be recalled to the castle.
- Mode turned off: every bot, reserved or not, is restored from the home snapshot exactly as today.
- `open_bg_band_min` above `open_bg_band_max`, outside 1..50, or non-numeric text never throws and never produces an empty or inverted band.

## File Structure

| File | Responsibility |
| --- | --- |
| `battlegrounds/open/OpenBattlegroundProperties.cs` | adds `open_bg_band_min`, `open_bg_band_max` and clamped readers `BandMin`, `BandMax` |
| `battlegrounds/open/OpenBattlegroundRoster.cs`, `OpenBattlegroundRoles.cs` | crew selection and low-level protection read the band instead of 40 / 50 |
| `commands/playercommands/OpenBgSetupCommand.cs` | `/bgsetup band_min|band_max` |
| `source/tools/OfflineDaoc.Launcher/OpenBgSettings*.cs` | launcher fields for the band |
| `bots/gothwaite/GothwaitePvpOpenBgReserve.cs` (new) | pure set choice, size, order; live predicate `IsOpenBgReserved`, `IsStandby` |
| `bots/gothwaite/GothwaitePvpMode.cs` | `IsModeBot` excludes reserved bots; `Reset` clears the set |
| `bots/gothwaite/GothwaitePvpHomeStore.cs` | builds the set in `ReconcileAtStartup`; hub placement branch in `TryPlaceAtCastle` |
| `bots/gothwaite/GothwaitePvpOpenBgStandby.cs`, `BotBrain.GothwaitePvpOpenBgStandby.cs` (new) | standby turn (stamp, leave group, park, rest) |
| `bots/gothwaite/GothwaitePvpRules.cs` | `SuppressesOpenBattlegroundCrew`, `MayJoinOpenBattleground`, `RefusesOpenUnit` |
| `battlegrounds/open/OpenBattlegroundRoster.cs` / `Director.cs`, `bots/autonomous/AutonomousBattlegroundParticipation.cs` | guarded calls into those rules |
| `Tests/UnitTests/UT_GothwaitePvpOpenBgReserve.cs` (new), `UT_GothwaitePvpModeOff.cs`, `UT_OpenBgSetupCommand.cs` | tests |

Paths under `source/server/GameServer/`; tests under `source/server/Tests/UnitTests/`.

---

### Task 1: Configurable crew level band

**Files:**
- Modify: `battlegrounds/open/OpenBattlegroundProperties.cs`, `battlegrounds/open/OpenBattlegroundRoster.cs` (`MinLevel` / `MaxLevel`, about line 178), `battlegrounds/open/OpenBattlegroundRoles.cs` (`CrewMinimumLevel`, line 38 and its use at about line 248), `commands/playercommands/OpenBgSetupCommand.cs`, `source/tools/OfflineDaoc.Launcher/OpenBgSettings.cs`, `OpenBgSettingsControl.cs`
- Test: `UT_OpenBattlegroundBand.cs` (new), `UT_OpenBgSetupCommand.cs`, `OfflineDaoc.Launcher.Tests/OpenBgSettingsTests.cs`

**Interfaces:**
- Produces: `OpenBattlegroundProperties.BandMin`, `BandMax` (int, clamped: both 1..50, min not above max: if min > max the pair falls back to the defaults 40 / 50); `OpenBattlegroundProperties.ResolveBand(int min, int max) -> (int Min, int Max)` (pure); `/bgsetup band_min <1-50>` and `band_max <1-50>` (reject non-numeric, out of range, and a value that would invert the band against the other current value).

- [ ] **Step 1: Failing tests.** `UT_OpenBattlegroundBand`: `ResolveBand(40, 50)` is (40, 50); (1, 50) ok; (0, 50) and (40, 51) clamp to (1..50) range limits (0 becomes 1, 51 becomes 50); (45, 30) falls back to (40, 50); negative values clamp to 1. `UT_OpenBgSetupCommand`: `band_min 30` and `band_max 45` store rows `open_bg_band_min` / `open_bg_band_max`; `band_min 51`, `band_min x`, `band_max 0` rejected with the usage line and the stored value unchanged; `band_min 46` while `band_max` is 45 rejected. The existing `EveryPropertyRowHasAKey` test must pass with the two new keys. Launcher `OpenBgSettingsTests`: round trip of `BandMin` / `BandMax`; an old config without the rows loads 40 / 50.
- [ ] **Step 2: Run to verify they fail** (build errors for the missing members).
- [ ] **Step 3: Implement.** Add the two properties (category `pvp`, defaults 40 and 50, description "Open BG crew level band, lowest/highest level of the world bots that may crew it (1-50)") and `BandMin` / `BandMax` readers via `ResolveBand`. Read where the roster takes its candidate band today (`MinLevel..MaxLevel`, about Roster.cs:178, and any other literal 40 / 50 in the open BG candidate and guest-squad code; grep `OpenBattlegroundRoster.MinLevel`, `MaxLevel`, `CrewMinimumLevel`) and make every crew-selection use the readers; `CrewMinimumLevel` in the protection rule becomes `BandMin`. Guest squads for players under the band keep their current logic (do not change). Add the `/bgsetup` keys following the existing key table, validation and live-refresh pattern, and the launcher fields following the existing numeric field pattern.
- [ ] **Step 4: Run the filtered suites** (`UT_OpenBg|UT_OpenBattleground|UT_Gothwaite`, launcher `OpenBg|Presentation`). Expected: all green.
- [ ] **Step 5: Commit** `feat(open-bg): configurable crew level band (open_bg_band_min, open_bg_band_max, /bgsetup band_min|band_max)`.

---

### Task 2: Reserved set and mode-bot exclusion

**Files:**
- Create: `bots/gothwaite/GothwaitePvpOpenBgReserve.cs`
- Modify: `bots/gothwaite/GothwaitePvpMode.cs` (`IsModeBot` ~lines 56-62, `Reset`), `bots/gothwaite/GothwaitePvpHomeStore.cs` (`ReconcileAtStartup`: build the set)
- Test: `UT_GothwaitePvpOpenBgReserve.cs` (new), `UT_GothwaitePvpHome.cs` (extend)

**Interfaces:**
- Consumes: `OpenBattlegroundProperties.BandMin/BandMax/MaxBots`, `Groups8For`, `SmallFor`, `SolosFor` (the per-realm setup readers of the roster; use the ones the roster already exposes), `OpenBattlegroundPolicy.IsEnabled`.
- Produces: `GothwaitePvpOpenBgReserve.ReserveSize(int seats, int maxBots) -> int` (= `ceil(2.5 * min(seats, ceil(maxBots / 3.0)))`); `GothwaitePvpOpenBgReserve.Order(long botId) -> ulong` (= `unchecked((ulong)botId * 2654435761UL) & 0xFFFFFFFF`); `GothwaitePvpOpenBgReserve.Choose(bool modeActive, bool openBgEnabled, int bandMin, int bandMax, int seatsPerRealm, int maxBots, IEnumerable<(long BotId, int Realm, int Level, bool Retired)> bots) -> IReadOnlySet<long>` (empty if the mode is off or the open BG disabled; per realm 1-3: candidates not retired with level inside the band, ordered by `Order` then BotId, take `ReserveSize`, capped at 30% of that realm's candidates, rounded up, at least 0); `GothwaitePvpOpenBgReserve.Set` (published with `Volatile`, immutable), `Install(IReadOnlySet<long>)`, `IsReserved(long botId)`; `GothwaitePvpOpenBgReserve.IsOpenBgReserved(GameLiving living)` (true only for a mode-on `GameBot { IsAutonomousWorldBot, !IsTemporaryGroupHelper, !IsBattlegroundEncounterBot, !IsPersistentPlayerCompanion }` whose `DatabaseID` is in the set); `GothwaitePvpMode.IsModeBot` now returns false for a reserved bot (add a pure overload parameter `reserved`, default false).

- [ ] **Step 1: Failing tests** (`UT_GothwaitePvpOpenBgReserve`): `ReserveSize(20, 72)` is 50; `ReserveSize(20, 24)` uses `min(20, 8)` => 20; `ReserveSize(0, 72)` is 0; `Order` is deterministic and differs for neighbouring ids; `Choose` returns an empty set when the mode is off or the open BG disabled; only realms 1-3, only levels in the band, retired excluded; the result does not depend on the input order; the 30% cap (10 candidates in a realm give at most 3); stable: choosing twice from the same input gives the same set; adding a bot outside the band changes nothing. `IsModeBot` with `reserved: true` is false while the mode is active; with the mode off it is false as before; a non-reserved world bot stays a mode bot. In `UT_GothwaitePvpHome`: `Plan` produces the same rows with and without a reserved set (reserved bots keep their snapshot row).
- [ ] **Step 2: Run to verify they fail** (missing types).
- [ ] **Step 3: Implement** the unit and the two edits. In `ReconcileAtStartup` call `Choose(...)` with the roster rows it already loads (BotId, Realm, Level, retired flag) and `Install`; log one line `GOTH_PVP_OPENBG_RESERVE reserve=<n> per_realm=<a>/<b>/<c> band=<min>-<max>` (one `Log.Info`, no per-bot lines). `GothwaitePvpMode.Reset` calls `GothwaitePvpOpenBgReserve.Install` with an empty set. Do not add any other hook yet.
- [ ] **Step 4: Run** `UT_GothwaitePvp|UT_OpenBattleground` filters. Expected: green, and the existing `UT_GothwaitePvpModeOff` still green.
- [ ] **Step 5: Commit** `feat(gothwaite): reserved world bot set for the open BG, excluded from the mode bots`.

---

### Task 3: Login at the border hub and standby driver

**Files:**
- Create: `bots/gothwaite/GothwaitePvpOpenBgStandby.cs`, `bots/BotBrain.GothwaitePvpOpenBgStandby.cs`
- Modify: `bots/BotBrain.cs` (one guarded line in `ThinkCore` after the open BG participant branch, about line 1548), `bots/gothwaite/GothwaitePvpHomeStore.cs` (`TryPlaceAtCastle` reserve branch)
- Test: `UT_GothwaitePvpOpenBgStandby.cs` (new)

**Interfaces:**
- Consumes: `GothwaitePvpOpenBgReserve.IsOpenBgReserved`, `AutonomousBattlegroundParticipation.IsInAnyBattleground` (the registered / travelling / inside test; use the existing member), `AutonomousBotGroupCoordinator.IsOpenBattlegroundParty(Group)`, `AutonomousRvrStaging.TrySafeRvrRelease(realm, true, ...)`, the objective fields of the RvR branch of `AutonomousObjectiveAssignments.Assign` (Objectives.cs ~458-498).
- Produces: pure `GothwaitePvpOpenBgStandby.IsStandby(bool modeActive, bool reserved, bool inAnyBattleground, bool playerLed) -> bool` (true only when mode active and reserved and not in any battleground and not player-led); `NeedsRvrStamp(string objectiveKind) -> bool` (true unless it is "RvR"); `LeavesGroup(bool groupIsBotOnly, bool isOpenBgParty) -> bool` (true for a bot-only group that is not an open-BG party); `NeedsPark(bool atOwnHub, bool alive, bool inCombat, bool returningAfterRelease) -> bool`; `Stamp(...)` writing exactly the RvR-branch fields (`ObjectiveKind="RvR"`, `ObjectiveAssignmentId="gothpvp-openbg-{botId}"`, assigned and expires from `RollRvrTenure()`, `ObjectivePveMode=""`, kills 0, `ObjectiveRvrEligibleUtc=""`, mark dirty, queue status persistence); live `BotBrain.ThinkOpenBgStandby()`.

- [ ] **Step 1: Failing tests** for the four pure predicates (truth tables: mode off => never standby; reserved but in a BG => not standby; player-led => not standby; park only when alive, not in combat, not returning and not at the hub; leave group only for bot-only non-open-BG groups) and for `Stamp`/`NeedsRvrStamp` against a plain record object that carries the objective fields (a small test double or the real `DbAutonomousBot` record if constructible without a world).
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement.** The standby turn in order: (1) stamp the RvR objective if `NeedsRvrStamp`; (2) leave the group if `LeavesGroup`; (3) if `NeedsPark`: move to the own border hub release point with a spread by bot id (`TrySafeRvrRelease(realm, true, ...)`; pattern of `TryExitBot` in `BattlegroundCampaignPolicy.cs` ~line 199), log `GOTH_PVP_OPENBG_PARK bot=... realm=...` at most once per bot per park; (4) at the hub: rest, buffs (`TryMaintainTravelAndClassBuffs`), `FSM.Think()`. The `ThinkCore` line: `if (GothwaitePvpOpenBgReserve.IsStandby(BotBody)) { ThinkOpenBgStandby(); return; }` exactly after the participant branch so that a reserved bot in the BG never reaches it. The `TryPlaceAtCastle` reserve branch: for a reserved bot place it at its own realm's border hub (spread by bot id, read-only) instead of the castle landing; keep `RepairInvalidLoginPosition` semantics.
- [ ] **Step 4: Run** `UT_GothwaitePvp|UT_OpenBattleground|UT_AutonomousBattleground` filters; green.
- [ ] **Step 5: Commit** `feat(gothwaite): reserved bots log in at their border hub and park there between open BG deployments`.

---

### Task 4: Open BG guards

**Files:**
- Modify: `bots/gothwaite/GothwaitePvpRules.cs`, `battlegrounds/open/OpenBattlegroundRoster.cs` (`Tick` ~line 636, `CandidatePools` loop ~line 1011), `battlegrounds/open/OpenBattlegroundDirector.cs` (`Tick` ~line 483), `bots/autonomous/AutonomousBattlegroundParticipation.cs` (`AssignOpenUnit` ~line 316)
- Test: `UT_GothwaitePvpModeOff.cs` (extend), `UT_GothwaitePvpOpenBgReserve.cs` (extend)

**Interfaces:**
- Produces: `GothwaitePvpRules.SuppressesOpenBattlegroundCrew(bool active, int reservedCount) -> bool` (= `active && reservedCount == 0`); `GothwaitePvpRules.MayJoinOpenBattleground(bool active, bool reserved) -> bool` (= `!active || reserved`); `GothwaitePvpRules.RefusesOpenUnit(bool active, bool allMembersReserved) -> bool` (= `active && !allMembersReserved`); live wrappers taking the bot / members.

- [ ] **Step 1: Failing tests:** truth tables of the three predicates; mode off => the three behave as today (`SuppressesOpenBattlegroundCrew(false, n)` false, `MayJoinOpenBattleground(false, any)` true, `RefusesOpenUnit(false, any)` false); mode on with an empty set suppresses the crew; mode on with a reserve lets reserved bots join and refuses a unit with a non-reserved member.
- [ ] **Step 2: Run to verify they fail.**
- [ ] **Step 3: Implement** the guarded calls: in `OpenBattlegroundRoster.Tick` and `OpenBattlegroundDirector.Tick` replace the existing `SuppressesWorldRecruitment` early return by `SuppressesOpenBattlegroundCrew(active, GothwaitePvpOpenBgReserve.Count)` (keep `SuppressesWorldRecruitment` for every other recruiter untouched); first line of the `CandidatePools` loop `if (!GothwaitePvpRules.MayJoinOpenBattleground(bot)) continue;`; `AssignOpenUnit` refuses via `RefusesOpenUnit`. The bracket `Admit` stays off. Read each site first and keep the change to the guard.
- [ ] **Step 4: Run** `UT_GothwaitePvp|UT_OpenBattleground|UT_OpenBg|UT_AutonomousBattleground`; green.
- [ ] **Step 5: Commit** `feat(gothwaite): the open BG recruits only the reserved set while the mode is on`.

---

### Task 5: Status, docs, launcher hint, version

**Files:**
- Modify: `commands/gmcommands` or wherever `/gothpvp` status is built (`GothwaitePvpStatus`), `docs/GOTHWAITE_PVP_MODE.md` (§2 decision G12, §5.4 and §11.1 rows), `docs/CAMLANN.md` (open BG: mode combination, band), `docs/TASKS.md` (item 116 / Gothwaite item), `ALL SERVER COMMANDS.txt`, launcher hint text near the world mode selector, `CHANGELOG.md` and the version pins
- Test: `UT_GothwaitePvpStatus` extension if a status test exists

- [ ] **Step 1:** `/gothpvp` prints one more line "open BG reserve N, standby a, crew b" (N = set size, a = reserved bots currently parked, b = reserved bots currently in the BG; read cheaply; with the mode off the command behaves as before); a unit test for the line format if the status builder is pure.
- [ ] **Step 2:** Launcher hint under the World mode selector: "Open BG runs with a reserved crew (level band in the open BG tab)". Test: launcher presentation tests stay green.
- [ ] **Step 3:** Docs as listed; `ALL SERVER COMMANDS.txt` (CRLF, binary-safe edit) gains `/bgsetup band_min|band_max` and the `/gothpvp` line; CHANGELOG `## [0.279.0] - 2026-10-15` with Added (open BG together with the Gothwaite PvP mode through a reserved bot set, configurable crew level band, `OPEN_BG`/`GOTH_PVP_OPENBG_*` logs), Changed (nothing else), Fixed (the open BG claim window now also holds for defend units; a keep change ends the push or fight; solos never break off — these three landed after 0.278.0 and belong in this release), Removed none; bump the four version pins.
- [ ] **Step 4: Run** server filter `UT_OpenBg|UT_OpenBattleground|UT_Gothwaite|UT_Rvr` and launcher filter `OpenBg|Presentation`; build the Launcher and CoreServer in Release (separate output tree). All green and 0 build errors.
- [ ] **Step 5: Commit** in logical commits (status+hint, docs+version).

---

## Self-Review

Spec coverage: reserved set (Task 2), size and order (Task 2), band (Task 1), park and login (Task 3), guards (Task 4), status/docs/version (Task 5), mode off contract (Tasks 2-4 tests), off/unchanged without the mode (global constraint). Not in this plan: sticky persisted reserve flag, bracket battlegrounds in the mode, the own Custom5 window.

Type consistency: `GothwaitePvpOpenBgReserve.IsOpenBgReserved/IsReserved/Count/Install/Set` are used by Tasks 3 and 4 as defined in Task 2; the rules in Task 4 take the same booleans.

Placeholders: the live call sites (hub placement, port, release) are read from code at the named lines; the pure rules are given in full above.
