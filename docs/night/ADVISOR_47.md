# Advisor report, task 47 (world bots barely level), 2026-09-28 03:27

Evidence: log 23:54–03:21 (3.4 h, 0.115.0), read-only copy of the live save at
01:19Z compared with the 20:42Z snapshot. Population 1,210 bots online (not 600),
bot_xp_rate=10, xp_rate=10.

## State

| Realm | 10-19 | 20-29 | 30-49 | 50 |
|---|---|---|---|---|
| Alb | 81 | 117 | 0 | 212 |
| Mid | 87 | 112 | 1 | 200 |
| Hib | 60 | 138 | 2 | 200 |

Objectives among sub-50 bots: SoloPve 321, GroupPve 189, RvR 88. All 612
level-50 bots were created at 50. 556 of 598 sub-50 bots levelled between
20:42Z and 01:19Z, 1,373 levels, about 0.5 level per bot-hour; level-ups per
hour 384, 315, 283 and falling. Nobody has passed 32.

Activity (per-minute average 00:00–03:00, all bots): fighting 22 %, travelling
28 %, meetup 16 %, camp 6 %, dead 5 %, other/idle 20 %. Deaths per hour: PvP
4,300–5,400, PvE about 770.

| Goal attempts (sub-50) | solo | group |
|---|---|---|
| attempts that reached camp | 32 % | 12 % |
| hours at camp / hours in attempt | 387/614 | 61/419 |
| kills per camp-hour | 29 | 50 |
| deaths per bot-hour (level 20-29) | 3.2 | 0.7 |
| XP per hour (level 20-29) | 13.7M | 25.1M |

1,158 of the 1,650 solo attempts that never arrived ended "Defeated" on the way.
53 % of solo goals target another region; median same-region distance 39k units.
Median level-up kill earns 25–45 % of a same-level kill at 10x (green targets);
the 10x rate itself is applied correctly. Projection: level 20→30 takes 25–45 h
per bot, 30→40 over 100 h, 40→50 effectively never.

TASKS item 7 is NOT the same cause: the player curve is faithful to 1.65
(XPLevel in GamePlayer.cs:4059+); same-level kills per level at 10x are ~10 at
25, 13 at 30, 19 at 39, then 41 at 40 and 88 at 49. The wall is at 40.

## Root causes

- A. PROVEN: the solo con ceiling ratchets down and never recovers
  (AutonomousWorldBotController.cs:3252-3262, 3292-3298; `_deathDifficultySteps`
  only grows, reset only at line 3213 on the first observation). 323 of 462
  sub-50 bots sit at GREEN, 139 at BLUE.
- B. PROVEN counts, SUSPECTED mechanism: leveller PvP deaths. 4,700 PvP replans
  in 3.4 h, all in regions 1 and 100; engagement summary almost all
  "collateral" (20-34 hit by 50s: 16,401; 50s hit by 20-34: 10,070).
- C. PROVEN: solo travel. Level 20+ uses SelectWithinEnvironment instead of the
  local SelectLevelingCamp (AutonomousWorldBotController.cs:2931,
  `planningLevel < 20`).
- D. PROVEN: groups hardly camp. 12 % arrive, 252 end "Reassigned" before
  arrival; task clock runs during matchmaking (one 8-man started with 54 min
  left); 1,845 travel-hold group-minutes.
- E. SUSPECTED: PvE-counted deaths where a player-shaped enemy dealt the damage
  (GameBot.cs:1020) lower the ceiling further.

## Build plan

1. Con recovery (ObserveDeaths, MaximumTargetCon): decay one step after ~10
   kills without dying, on level-up and on a new task; count a death as PvP when
   a player-shaped enemy hit the bot in the last 30 s. Tests: decaying ceiling;
   gank does not lower the ceiling.
2. Local solo camps up to ~35: extend SelectLevelingCamp or weight camps by
   distance/region in AutonomousBotDecisionEngine.cs; keep the crowding check.
3. Instrument first (one log line per bot death: killer level/type/class,
   zone, area flag, classification), then fix collateral PvP in BotBrain.cs
   (skip area spells with non-target other-guild player-shaped enemies in the
   radius; level-50s do not chase grey stray hitters).
4. Group time at camp (AutonomousBotGroupCoordinator.cs,
   AutonomousObjectiveAssignments.cs): task clock starts at camp arrival,
   camps near the rendezvous, no reassignment in flight.

Do not raise XP multipliers.

## Not verified

Exact death locations/killers (no per-death log line); per-bot deaths since
20:42Z (snapshot overwritten); whether collateral hits are area spells, pets or
retaliation; 2003 /played figures (from memory); dbquery refresh fails with
"disk I/O error" on the live WAL.
