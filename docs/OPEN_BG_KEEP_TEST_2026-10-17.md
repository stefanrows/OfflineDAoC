# Open BG keep fights: live test 2026-10-17

Local install, bots only (no client online), world speed 5x (about 4.4x achieved),
Thidranki Faste (keep 11, region 252). Builds 0.293.0 to 0.296.0. The night runs
were cut by two Windows Update restarts (02:29, 18:20/18:26).

## Runs

| Run | Build | Mode | Length | Raids | Captures | Defended | Guard kills |
|---|---|---|---|---|---|---|---|
| A | 0.293.1 | Server PvP | 25 min | 4 | 2 | 1 | 0 |
| B | 0.295.2 | Per realm | 40 min | 12 | 8 | 2 | 0 |
| C | 0.296.0 | Per realm | 30 min | 5 | 5 | 0 | 75 |
| D | 0.296.0 | Server PvP | 30 min | 6 | 2 by the raid, 2 by other guilds in the retreat | 2 | 65 |

Runs A and B are not representative: no keep guard ever thought (see problem 1).
Runs C and D are the first with active guards.

## Problems found, where, and solutions

1. **Keep guards never fought with only bots near (fixed, 0.296.0).**
   `ABrain.IsActive` lets a vicinity NPC think only near a client or a bot that
   wakes it (`GameNPC.OnAutonomousBotNearby`); `BotBrain.ThinkAutonomousBattleground`
   skipped `WakeNearbyNaturalAggroBrains`. Runs A/B: 0 guard kills, 0 guard
   aggro lines. Now woken; run C/D: 65-75 guard kills per 30 min, 200+ aggro.
   With a player at the keep the guards already thought before (the owner's
   in-game observation was the sight filter, fixed in 0.293.0).

2. **Per realm: the keep falls almost every cycle (open).** Run C: 5 of 5 raids
   took it, 1.5-3 min after the push. Defenders (`defend-gate`) and guards slow
   the raid but do not stop 16-25 bots.
   Candidate fixes (owner decision): more defender units for the keep's owner
   (the `Defend` share in `OpenBattlegroundRoles.Assign`), raid size capped
   relative to the defenders present, or stronger guards/lord for the open BG
   keep (`GuardKeepBaseLevel`, 0.287.0).

3. **Server PvP: other guilds claim the keep in the retreat (open, design).**
   Run D: 2 of 4 claims were by a guild outside the raid (`Night Stalkers`,
   `Night Stalkers 2`) after the raid's lord kill, during the retreat. This is
   the "vulture rule" item already open in `docs/OPEN_BG_MODES_RULINGS.md`.
   Fix if wanted: only the raid's guilds may claim within N minutes of the lord
   kill.

4. **Assault bots wait longer than the 30 s gate rule (open).** The longest
   `march-gate` wait was 85 s (run C) and 186 s (run D). After the wait the step
   is Siege, but `TryAssault` returns false when `SiegeTarget` gives no door and
   no proven approach, and the bot falls back to `MarchToGate`. Most raid time
   goes to `approach-keep` (334 of 765 actions in run D).
   Fix: log `SiegeTarget` failures, and let a bot at the gate attack the outer
   gate directly when no approach is proven.

5. **Guards see few bots (watch).** 993 of 1,432 bots near a central-keep guard
   were dropped as unseen (run C). The navmesh ray plus the `ShootsUpAt` foot test
   is conservative: bots behind other walls are right to drop, bots at a wall
   corner may not be. Fix only if the keep still falls too easily after 2.

6. **Guard kills at the portal landings (cosmetic).** 14 kills credited to the
   central keep's guards and lord happened at the Hibernia and Albion landings,
   i.e. damage over time that ran out after the bot broke off. No guard leaves
   its post (leash 3,000).

7. **Client crashes (Stefan and Aaron, analysis).** Three game.dll crashes on
   Aaron's PC (10-04, 10-16 21:53, 10-17 01:09), all on Stefan's server. The dumps
   show the stock client freeing a broken 3D model (`NiGeometryData` destructor,
   0x66c4c5); no fork patch code on the crashing stack; the 10-04 crash predates
   the OPEN BG window. Next: Stefan's server log and crash dumps, a test evening
   with the 0.276.0 client backup, and raising the launcher's dump count (3).

## Diagnostics left in the log

`OPEN_BG_KEEP_ACTION`, `OPEN_BG_GUARD_AGGRO`, `OPEN_BG_GUARD_SCAN`,
`OPEN_BG_GUARD_STATE` (all throttled).

## Not verified

Nothing here was checked in a real client. Runs at 5x speed; at 1x the phase
timings are the same in game time.
