# Autonomous bots: first live review of M0–M6 and improvement plan

Status: **review complete. Phases A and B implemented offline through 0.54.0; the owner run is pending. Phases C–D not started.**
Date: 2026-09-24. Build under review: 0.50.0 (M6), with earlier sessions on 0.49.x.

This is the first M7 input for [AUTONOMOUS_BOT_ROADMAP.md](AUTONOMOUS_BOT_ROADMAP.md).
It is based on a **read-only** look at the owner's running installation: the
server logs, a copy of the save's bot tables, `bot-goals.json`, and the server
properties. Nothing in the installation was changed. No names, IDs, or save
data are copied here beyond aggregate counts.

## 1. The run that was measured

| Item | Value |
|---|---|
| Population | 1,500 autonomous bots (500 per realm), **Fresh launch** (everyone started at level 1) |
| Mix | Custom: Leveler 25, Casual 18, Hybrid 30, Hunter 15, Roamer 12, Keep warrior 0; Danger Authentic |
| XP | `xp_rate` 3, `bot_xp_rate` 3 (the roadmap's F1 numbers assumed 10) |
| Uptime of this cohort | 300 bots created 14:30 local, 1,200 more at 17:10. Server sessions 14:29–15:27, 17:03–18:10 (0.49.x), and 19:45 onward (0.50.0). About **1 h 50 min** of real uptime for most bots and 2 h 45 min for the first 300. |
| Guilds | 15 managed guilds of about 100 members each (5 Hunting, 5 Leveling, 4 RvR, 1 Keep; no Social) |
| Server health | 1,500 bots: 5.1 GB working set, game-loop tick p95 11.6 ms (budget 33 ms). Only the 1,500 tier has a sample. |

## 2. Headline numbers

| Measure | Value | Roadmap target / expectation |
|---|---|---|
| Level spread | 1–8, mean 3.2. 318 bots still level 1 | A few hours at 3× should put active bots near 10 |
| Monster kills | About **2–3 per bot-hour** (≈230 XP per bot-hour in the 0.50.0 session) | A live-era solo player kills dozens per hour |
| Deaths | About **2.4 per bot-hour**, more deaths than kills overall (5,863 vs 5,580 in goal records) | Rare at levels 1–9 |
| Goal attempts that reached their camp | **825 of 9,957 (8%)** | Most |
| Attempts ending in death before arrival | 5,363 (54%), median 7.4 min after starting; 63% of all attempt time | — |
| Time split (0.50.0 session) | Fighting 19%, camp 17%, **traveling 36%**, meetup 8%, other 16%, dead 3%, town 2% | ≥ 50% fighting or at camp |
| Stable-horse boardings | About 3 per bot-hour | Few |
| Bots in a PvP "PvE block" | **432 (29%)**, all below level 10 | Near zero below 10 |

## 3. Findings

### R1: Bots below level 10 kill each other constantly (highest impact)

- In the 0.50.0 session the server logged **1,792 bot-on-bot kills in about 40
  minutes**, about 75% of all bot deaths in that window. This is a lower bound:
  the log line (`Long ReaperService.Tick`) appears only when a death is slow
  to process (the fastest logged was 26 ms). See R5 for why bot-on-bot deaths
  now almost always are.
- Every one of those kills was between bots of the **same realm** and different
  guilds. **1,326** victims were fighting a monster when they were killed.
- Killers were mostly bots on a **SoloPve** task (1,631 of 1,792) of every type:
  Hybrid 502, Casual 484, Leveler 459, Hunter 218. Hunters are not supposed to
  hunt before level 10, and Levelers and Casuals should avoid PvP.
- The kills cluster at **stable-route arrival points and release/bind points**,
  where many bots stand close together (for example the Castle Sauvage stable
  endpoint and the Albion and Mularn release points).
- The killing did not start with M6. PvE blocks with the reason "three PvP
  deaths in twenty minutes" were already being set during the 0.49.x session.
- Albion classes kill far more often per bot (Cabalist 7.2, Reaver 5.9,
  Mercenary 2.3, Paladin 2.2) than Midgard melee (Skald 0.1, Savage 0.2,
  Berserker 0.3). The class that starts a fight matters, which points at
  pets, AoE, or crowd-control paths rather than the RvR opportunity scan.

**Cause, confirmed in code:** the level-under-10 `/safety` protection exists
only for `GamePlayer`. `PvPServerRules.IsAllowedToAttack` checks
`PvpCombatant.IsSafetyProtected(GamePlayer…)`
([PvPServerRules.cs](../source/server/GameServer/serverrules/PvPServerRules.cs),
around lines 155–170). `GameBot` is a `GameNPC`, so an autonomous bot never has
safety. Roadmap §5.3 promised Levelers and Hunters "safety on" at 1–9; that was
never enforced.

**Cause, not yet proven:** how the first blow is struck. The RvR opportunity
scan (`TryEngageOpenWorldPvpOpportunity`) runs only for RvR tasks, so it is not
the source. Once any PvP opponent exists, these paths widen a fight:

1. The PvP crowd-control sweep (`BotBrain.PvpCrowdControl.cs`) chooses from every
   legal player-shaped enemy within 1,800 units, not only the ones fighting the
   bot or its group. A mez on a bystander starts a new fight.
2. Guild grudge targets (M6) bypass the grey, level-window, and "stronger party"
   filters in `AutonomousPvpOpportunityPolicy.Select` and
   `AutonomousRvrTargetPolicy.ShouldEngageGrey`, and so also in
   `BotBrain.CanAggroTarget`.
3. AoE spells and defensive pets hit or answer any attackable bystander.

No log line records who started a fight or through which path, so plan A4 adds
one before deeper changes.

### R2: A PvP death is treated as a PvE failure

Every death runs the PvE "death route replan". It lowers the bot's maximum
monster con to GREEN and picks a new, easier camp (`AUTONOMOUS_DEATH_ROUTE_REPLAN
… new_max_con=GREEN`; 6,679 replans and 3,362 forced fallbacks today). A bot
ganked at a stable therefore moves to a green camp and earns less XP, even
though the monster was never the problem. Some bots have 30–45 deaths while
still at level 2–5.

### R3: Solo leveling crosses the world instead of fighting

- **75% of solo PvE attempts (5,360 of 7,093) targeted a camp in another
  realm's lands.** Albion bots mostly leave Albion (Gotar, Connacht, Domnann,
  Vale of Mularn, and East Svealand are their most common targets). Level-3
  bots ride two or three stable routes to reach a level-3 camp. Their home
  starter zone has the same kind of camp.
- The cause is the M1 crowd weighting. `OutdoorCampWeight` scores a camp
  `100 / (1 + 2 × bots present)`, and travel time is deliberately not
  considered (`AutonomousBotDecisionEngine.cs`, `SelectWithinEnvironment` and
  `OutdoorCampWeight`). With about 500 fresh bots per realm, an empty camp
  in another realm easily outscores a home camp with two bots.
- Combined with R1 and R2, each death means release, a ride back, and a new
  long trip. Only 8% of attempts reach their camp.

### R4: Group PvE forms groups that never reach a camp

- In this cohort, 572 groups formed; 303 of them were duos. Of the 149 groups
  that finished, only 46 started their task. 67 ended with "No reachable
  non-grey group camp after lower-level fallbacks", and 25 ended because
  every invitee missed the meetup.
- **267 bots (18% of the population)** are "Recovering before travel", 186 of
  them already at full HP, power, and endurance, with a median of 14.5 minutes
  since their last progress. Their group itineraries show `Traveling together to ;` with an
  empty destination, or a leader still "establishing the regroup point".
- 8,237 `AUTONOMOUS_MATCHMAKING_BLOCKED` lines today: "No compatible local
  pickup member is currently available in this level/region cohort".
- Groups are formed before anyone checks that the cohort has a valid local
  camp (`CanUseMatchmakingCamp` requires the camp to be in the actor's own
  region). A group assembled in a capital after a stuck-recovery teleport has
  no camp to choose from.

### R5: M6 grudge memory stalls the game loop and feeds R1

- `AutonomousGuildGrudgeMemory.RememberKiller` runs inside `GameBot.ProcessDeath`
  (the reaper tick). It performs a **synchronous SQLite `AddObject`/`SaveObject`
  while holding the global `DatabaseWriteLock`**. In the 0.50.0 session, each
  bot-on-bot death took a median of **60 ms** of reaper time (p90 84 ms, worst 288 ms).
  The 0.49.x session had no such lines.
- It records every kill, including level-2 bots killing level-3 bots. All 15
  guilds hit the 16-entry KOS cap within 40 minutes, and the lists contain
  only level 1–8 bots.
- Grudge targets then skip the level, grey, and strength filters (R1, item 2),
  which closes a feedback loop: kill → KOS → more kills → more KOS.

### R6: PvP blocks and rerolls churn low-level tasks

"Three PvP deaths in twenty minutes" sets a 45–90 minute PvE block on 29% of
the population. For a bot that is already on a PvE task this only adds state
churn. In the goal records, 1,708 attempts were interrupted by `Reassigned`
and 955 by `GroupChanged`.

### R7: Smaller issues

- The stuck and 45-minute stall watchdogs return bots to their **capital**
  (for example Jordheim) rather than to their leveling area. That adds more
  trips and feeds R4. There were 120 stuck recoveries and 148 position
  recoveries today.
- Albion appears as `realm=_FirstPlayerRealm` in autonomous log lines (the enum
  alias for 1). This is cosmetic, but it makes the logs harder to read.
- The Keep warrior share is 0% in the mix, yet the Keep charter guild
  (Emberwake) still produced 21 Keep warriors. Charter mixes do not respect
  a 0% launcher type.
- Only the 1,500-bot benchmark tier has been sampled, so the launcher's
  recommended-size hint stays blank (M5 needs the 500 and 1,000 tiers as
  well).
- M6 chat is not logged, so tone, rate, and blocklist behavior can only be
  checked in the client. Existing bots keep their syllable names. The new name
  styles appear only on new bots, and the alt trickle is one every 72 hours.
- Startup error and warning noise (unknown immunity abilities, missing NPC
  templates, door navmesh registration) is unchanged between builds and
  unrelated to bot behavior.

### What is working

The per-minute activity summary, goal-attempt diagnostics, training, loot
upgrades, guild charters and names, and the grudge table all run without
errors. Server load at 1,500 bots is comfortable. The M1 soft meetups and
per-member no-shows work as designed; the group problem is camp availability,
not the meetup logic.

## 4. Improvement plan

Each phase is one task with its own version bump, offline tests, and a
separate owner run. Phase A changes gameplay, so it is a MINOR bump that loads
the existing save. After each phase, repeat §2 on a one-hour run with the same
population and compare.

### Phase A: stop low-level bot PvP and the M6 stall (do first)

1. **Bot safety below level 10.** Treat an autonomous bot below the PvP safety
   level (10) as safety-protected in `PvPServerRules.IsAllowedToAttack`, for
   attacker and defender alike, unless it has relinquished safety through
   `PvpCombatant.RelinquishOptionalSafety`. Use one helper so the human
   player's rules do not change. *Owner decision:* should Full Camlann danger
   let Hunters drop safety below 10? The default proposal is no.
2. **Asynchronous grudges.** Move the grudge write out of `ProcessDeath`: update
   memory immediately and queue the row for the existing status-persistence
   batch or a background writer. Never take `DatabaseWriteLock` on the reaper
   tick.
3. **Record only meaningful grudges.** Record a KOS only when the killer is a
   human player or the victim is level 10 or higher, the killer is not grey to
   the victim, and the kill was not a mutual brawl started by the victim.
   Grudge targets must still pass the level-window check for Hunters and the
   "stronger party" check, unless the guild sends a crew of comparable
   strength.
4. **Engagement diagnostics.** Log an aggregated `AUTONOMOUS_PVP_ENGAGE` summary
   per minute: counts by initiating path (opportunity scan, grudge, crowd-control
   sweep, AoE collateral, pet, defense), with attacker and victim level bands
   and player types. Add `pvpDeaths` and `pveDeaths` to
   `AUTONOMOUS_ACTIVITY_SUMMARY`.
5. **Narrow the crowd-control sweep.** In `TryPvpCrowdControl`, only consider
   enemies that are attacking the bot or its group, or that the bot or its
   group is attacking. Do not mez uninvolved bystanders.
6. **Keep PvP deaths out of PvE difficulty.** Skip the con downgrade and the
   `DeathsAtCurrentCamp` count when the killer was player-shaped. Apply the
   "three PvP deaths" PvE block only to bots whose task is RvR (or which are
   Hunters and Roamers at level 10 or higher).

**Implemented offline in 0.53.0.** Notes on the choices made:

- A1: safety applies to autonomous world bots only; player companions and
  temporary helpers follow their owner, as before. Hunters do not drop safety
  below 10 at any danger setting (the default proposal). A bot relinquishes it
  only through an RvR assignment, and then fights only other relinquished or
  level 10+ characters.
- A3: the "victim started the fight" check uses the new first-strike tracker
  (A4). It covers bots and humans in both directions. Revenge crews also skip
  targets that are grey to the hunter.
- A4: the summary key is `path|attacker band>victim band|attacker type`. Paths:
  `opportunity`, `grudge`, `shared-dungeon`, `frontier-threat`, `cc-sweep`,
  `group-assist`, `protection`, `pet`, `collateral` (the attacker was targeting
  someone else, as with AoE), `human`, and `untagged` (a path that still needs a
  tag). A fight that starts with a hit on a pet is counted only once its owner
  is hit.
- A5: RvR warbands and player companions keep the full pre-emptive
  crowd-control sweep. Other autonomous bots control only opponents in their
  fight and skip area mezzes.
- A6: a PvP death still ends the goal attempt and replans, but keeps the con
  ceiling and does not exclude the camp (`AUTONOMOUS_DEATH_PVP_REPLAN`).

Acceptance (1,500 bots, Fresh launch, one hour): no bot-on-bot kills below
level 10. Deaths at most 0.5 per bot-hour at levels 1–9. No
`Long ReaperService.Tick` lines from grudges. PvE blocks below 10 near zero.

### Phase B: make leveling efficient

1. **Travel-aware camp choice.** Multiply the crowd weight by a travel factor,
   for example `1 / (1 + estimated travel minutes / 5)`, and add a strong
   home-realm and current-region preference for solo bots below level 20.
   Keep D9 cross-realm leveling for pickup groups and for bots whose home
   realm has no suitable camp within about 10 minutes. Cap stable rides per
   task at 2 unless the destination is a planned event.
2. **Spread the crowd locally first.** Replace the flat crowd weight with a
   comparison across the camps a bot can reach in its current and neighboring
   zones. A fresh launch should fill every starter and second-tier zone of a
   realm before sending anyone abroad.
3. **Release close to the camp.** Check that release uses the nearest valid
   bind point in the bot's current leveling zone, not its original bind, and
   that the bot walks back instead of riding.
4. **Validate the camp before forming a group.** Form a pickup group only when
   the matchmaker already sees a valid local non-grey camp for the cohort. A
   group whose destination is empty, or whose leader cannot find a camp for 2
   minutes, dissolves at once and returns its members to solo play; they do
   not idle at full resources.
5. **Local stuck recovery.** Recover to the nearest safe point in the bot's
   current zone, or its leveling-area bind, before falling back to the capital.

**Implemented offline in 0.54.0.** Solo bots under 20 compare nearby
home-realm camps using travel time and local crowding. They may use a
cross-realm camp when no suitable home camp is within ten estimated minutes.
Stable tickets are limited to two planned hops per solo camp attempt, and a
bot walks back after a defeat. Release and watchdog recovery favor validated
bind points in the current zone. Pickup groups require a live local camp
before formation and dissolve if their later planner finds none. The owner
run must still measure the acceptance targets below; no real-client result is
claimed from the offline build.

Acceptance: at least 50% of time fighting or at camp. At least 70% of goal
attempts reach their camp. At least 20 monster kills per bot-hour at levels
1–10. Fewer than 0.5 stable rides per bot-hour. At the owner's current 3×,
the median bot should reach level 10 within about 3 hours of uptime (retune
this once real rates are measured).

### Phase C: verify M3/M6 behavior once bots pass level 10

Phase A and B runs will be the first with level-10+ bots. Then check:

- Hunters patrol leveling zones within about 5 levels, and grey ganks stay
  rare at the chosen danger.
- KOS crews form only against worthwhile targets, and guild chat announces them
  at the existing rate limits (client check).
- Roamer and Hybrid prime-time roams begin at 15–20.
- PvE groups still do not start unprovoked fights.

### Phase D: owner decisions and small fixes

- **Population shape:** a Fresh launch of 1,500 bots puts 500 level-1 bots per
  realm in the same starter zones at once. Consider Established live server,
  or a smaller Fresh start (for example 300 per realm) with the alt trickle
  raised.
- **Guild count:** 15 guilds of about 100 members make roughly 93% of all bots
  legal targets for any bot. Decide whether more, smaller guilds (about 30–40
  members) or alliances are wanted.
- A 0% player type should also remove that type from charter mixes.
- Log Albion as `Albion` instead of `_FirstPlayerRealm`.
- Run the 500 and 1,000 benchmark tiers so the launcher can recommend a size.

## 5. How to repeat this review

1. Copy `runtime/data/opendaoc.sqlite3.db` to a scratch folder. Never query
   the live file.
2. From `runtime/server/logs/server.log`, take the lines after the session's
   `Starting Server` line.
3. Measure: `AUTONOMOUS_ACTIVITY_SUMMARY` shares; `AUTONOMOUS_GOAL_ATTEMPT`
   outcomes, `arrivedUtc`, kills, XP, and deaths; `Long ReaperService.Tick`
   killer and victim pairs (after Phase A, the `AUTONOMOUS_PVP_ENGAGE`
   summary); `AUTONOMOUS_GROUP_OUTCOME` reasons; and from the database copy,
   level, `PlayerType`, `PveBlockReason`, `Activity`, and
   `offline_guild_grudges`.
