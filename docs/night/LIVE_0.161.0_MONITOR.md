# Live monitor 0.161.0, 2026-09-30 (handoff for a new chat)

Observation of the installed server `D:\Games\OfflineDAoC` running 0.161.0
(commit `c083caf`: raid battlegroup = bug 74, siege muster-and-march = bug 75).
Read-only log and DB-copy analysis; no code changed during monitoring.

## Setup the owner used

- Server started 16:21:26 (session start line in `server.2026-09-30.0.log`;
  the log rotated at 100 MB at 17:05, continue in `server.log`).
- Server population preset "Custom": **Keep warrior 98 %, Leveler 2 %**, all
  other types 0 %. Danger "Authentic", world shape "Established live server",
  roster ≈ 1,860, ≈ 1,800 active bots. This mix makes almost every bot an RvR
  bot, so death counts are **not comparable** with the 2026-09-29 night report.
- World speed selected 3×; ran at 2.4–2.8× achieved with no client, 1× after
  the owner connected at ≈ 17:25. Tick P95 14–17 ms (healthy).

## TL;DR

1. **Raid battlegroup (bug 74) works.** While the Hibernia epic raid was active,
   only 3 raider-on-raider kills happened, all within ~60 s of the killer's
   party being committed (fights already running). Before the fix it was
   ~150 per raid. (Careful: most same-roster kills in the log are *after* the
   raid ended at 17:11:40 — filter by raid window.)
2. **The raid still failed: "Staging failed: 0 adventurers arrived".** Cause is
   now the hub threshold, not friendly fire (details below).
3. **Siege muster works** (20 `RVR_SIEGE_MUSTER_DEPARTED`, 1 `..._FAILED`;
   most depart 8/8 immediately, the rest 5–6/8 after ~4 min or at 10 min).
   Warbands now travel and die *together* instead of one by one.
4. **Still no keep taken, no ram ever placed.** Attackers do reach the target
   keeps now (≈ 146 deaths to "Renegade" keep guards, incl. at the siege
   targets Caer Berkstead 51 and Caer Benowyc 50), but zero
   `RVR_SIEGE action=deployed/placement_blocked/hit`. Ram carriers arrive and
   die fighting the guards.
5. **Enemy portal-keep landings are killing fields.** ≈ 1,600 of 8,168 deaths
   (20 %) happened within 5,000 units of the enemy-realm porter landings
   (Hadrian Hib/Mid, Odin Hib/Alb). Warbands are wiped right after porting.
6. **Keep-route block memory is lost on restart.** The first siege after start
   went to Caer Berkstead (keep 51), the keep with 386 "No connected exterior
   route" failures last week; the new block list is in-memory only.

## 1. Raids (bug 74 verification + new cause)

Raid `epic-hibernia` (Caer Sidi-style epic dungeon, hub "Dalniver's service
settlement (World's End)", region 181):

- 16:29:41 announced, 16:38:35 `RAID_CALENDAR_START signedUp=238 detached=223
  parties=28 bots=220`; 17:11:40 all 28 parties ended with
  "Staging failed: 0 adventurers arrived" (≈ 33 real min = 90 game min at ~2.7×).
- Deaths of committed raiders during the raid: 296 (138 by other bots, 158 by
  mobs); hot zones Hall of the Corrupt (102), Odin's Gate, Hadrian's Wall,
  Dodens Gruva, Uppland.
- Raider-on-raider kills while the raid was active: **3** (bug 74 fixed).
- Member snapshot at the end (`AUTONOMOUS_GROUP_OUTCOME` 17:11:40):
  region 181 (hub region): 83; region 1: 43; region 276: 33; region 246: 28;
  others ≈ 30. Activities: "Formed up at rendezvous" 82,
  "Holding before dungeon threat" 55, stable-horse travel ≈ 33,
  "Expedition hub route failed" 6.
- **Why it failed:** the hub never departed. Scheduled raids need
  `ScheduledMinimumPresent(signedUp) = max(24, signedUp*3/5)` =
  **142 of 238** at the hub (`RealmRaidRecruitmentPolicy.cs`) before leaving for
  staging; only ~83 were even in the hub region. Staging presence stays 0 until
  the hub departs, so the end reason "0 adventurers arrived" is misleading
  (it is the staging count, not the hub count).
- Contributing: 55 raiders "Holding before dungeon threat" in other dungeons
  (regions 246/276) — they were recruited while inside a dungeon and never
  left; `REALM_RAID_HUB_ROUTE_FAILED` 559× ("formation route exhausted
  collision-safe recovery", bug 61 family).
- Suggested fixes: log hub presence periodically (`RAID_HUB_PRESENCE
  present=/needed=`); lower/scale the hub quorum (e.g. depart at a fraction of
  those *reachable*, or after a bounded wait with ≥ 24–40); don't recruit bots
  that are inside dungeons, or send them out first; make the end reason say
  "hub never gathered N/M".

## 2. Keep sieges (bug 75 verification)

Sieges opened: keep 51 Caer Berkstead (16:26), 79 Glenlock Faste (16:28),
103 Dun nGed (16:33), 50 Caer Benowyc (16:34). None closed by 17:30 (no idle
close, no capture). `RVR_SIEGE` actions: response_equipment 48, purchase 33,
repair_restock 33, supply_timeout 4 — **never** deployed / placement_blocked /
hit / ride.

Muster departures (target, how, present/alive/total, wait):
8/8/8 immediately ×6 for keep 51 etc.; partial departures 5/8, 6/8 after
~250 s or 600 s (quorum rules working). One failure: keep 51 force `…-096`
present 1/8 after 600 s.

Example warband "Moonlit Fang" (`3cd42a03-…-57b137-089`, keep 51):
mustered 8/8 at Jordheim 16:27, boarded "Hadrian Mid" together (5 then 6
members), then was **wiped as a group at the Midgard Portal Keep landing in
Hadrian's Wall** at 16:31 and again at 16:33–16:36. The two Hibernia-born
members respawn in Hibernia (Connacht) and end up separated.

Ram carriers reaching targets: Isaenisanne (ram for keep 51) killed by a
Renegade Guardian at 584481,389734 = Caer Berkstead; Ederenwell and Harerisred
(rams for keep 50) killed by a Renegade Armswoman at 651503,346593 = Caer
Benowyc; none logged any siege action at the keep. Their groups shrank 8→4.

**Likely cause of "no ram placed" (unconfirmed, needs a diagnostic):**
`TryRunSiegeJob` (`AutonomousWorldBotController.Siege.cs`) has silent early
exits. Prime suspect: "Prioritize immediate personal defense" — it returns
false whenever any NPC within 450 units attacks the bot; with level 59–76
Renegade guards engaged, the operator always falls through to
`FindRvrTarget` and melees the guards instead of placing the ram. Other silent
exits: `TryAcquire` false, keep > 6,000 away, `_siegeNextAttempt`, target null.
**Next step:** add a throttled `RVR_SIEGE action=skip reason=…` log for each
exit, ship, and re-observe; then decide (e.g. operator places the ram first
out of guard aggro range ~2,000–2,500 units from the gate, the rest of the
warband tanks/peels the guards; keep-guard pull logic for the warband).

Guard levels: unclaimed keeps have "Renegade" guards/commanders level 59–76
(Renegade Huscarl 76, Renegade Champion Commander 76, Nottmoor Jarl 66).
A 4–8 bot group loses that fight; consider whether guard levels are right for
unclaimed keeps in this fork (`KEEP_GUARD_LEVEL_MULTIPLIER`, keep level).

## 3. Portal-keep landings (new finding)

Enemy-realm porter landings are the portal keeps in each frontier (DB `Keep`:
22 Hibernia PK 605589,293789 and 23 Midgard PK 655269,293142 in region 1;
20/21 in region 100; 24/25 in region 200). Deaths within 5,000 units this
session: Hadrian Hib 564, Hadrian Mid 554, Odin Hib 372, Odin Alb 54,
Mid PK in Hib frontier 41, Alb PK in Hib frontier 10 (of 8,168 total; 96 % of
the Hadrian's Wall deaths are by bots). `PvpCombatant.IsSafeHubLanding` only
covers Castle Sauvage and Svasud Faste outer bindstones; these landings are
not protected. With 0.161.0 all members of a warband land together
(leader-realm passage), so whole warbands are wiped on arrival.
Options: safe circle (arrival peace) around each portal-keep landing like
`SafeHubLandings`, a short post-teleport grace, or grudge/roam targeting that
excludes fresh arrivals.

## 4. Other observations

- Death rate: ≈ 7,500/h (8,168 in 65 min), 76 % RvR — driven by the 98 %
  keep-warrior mix; not comparable to earlier nights.
- Keep-route block memory (`KeepRouteBlocks`) is static in-memory; persist it
  or seed it from a static list of known-unreachable keeps (51, 57, 102, 105,
  106 had repeated failures on 2026-09-23..30).
- Four pre-existing failing server tests (unrelated, hub-peace/decision
  engine): Choose_OrdinarySafeTownDecisionRemainsBiasedTowardGrinding,
  AutonomousSafetyEndsAtLevelTenAndSkipsCompanions,
  AttackPermissionConsultsThePeaceAndCountsIt,
  AttackPermissionCountsTheDepartureRuleSeparately.

## Final state at 17:44 (monitoring stopped by owner)

- Still 4 sieges open (keeps 51, 79, 103, 50, all opened 16:26–16:35), none
  captured or closed; still zero ram deployed / gate hit.
- Muster: 20 departed, 2 failed (second failure 17:28:41 keep 79, force
  `…-57b137-159`, 1/2 present after 600 s). Note that force had *departed*
  at 16:49 (3/5) and was mustering again 40 min later — a re-muster after
  departure should be checked (possible regression: a departed force should
  not go back to Mustering, or if it does, the failure should not release it).
- Siege actions: response_equipment 48, purchase 35, repair_restock 35,
  supply_timeout 8. Deaths to Renegade keep guards: 156.
- No second raid started after `epic-hibernia`.

## Suggested next tasks (priority order)

1. Siege job diagnostics (`RVR_SIEGE action=skip reason=…`), then fix ram
   placement under guard aggro (bug 75 follow-up).
2. Safe arrival circle / grace at enemy portal-keep landings.
3. Raid hub quorum + hub-presence logging + no recruitment from inside
   dungeons (bug 74 follow-up; bug 61 route failures).
4. Persist or seed keep-route blocks (keeps 51, 57, 102, 105, 106).
5. Check the re-muster-after-departure case above.

## How to re-check quickly

```
L=/mnt/d/Games/OfflineDAoC/runtime/server/logs
grep -a -E "RVR_SIEGE_(STARTED|MUSTER_DEPARTED|MUSTER_FAILED|IDLE_CLOSED)|RVR_EVENT_ENDED|RAID_CALENDAR_START|RVR_SIEGE action=(deployed|hit|placement_blocked)|KEEP_CLAIM_ATTEMPT" $L/server.log
grep -a AUTONOMOUS_BOT_DEATH $L/server.log | grep -c 'killer="Renegade'
```
Raid friendly-fire check: map `REALM_RAID_FORCED_PARTY … members=` to the
raid window (start `RAID_CALENDAR_START`, end the `Staging failed`/raid end
`AUTONOMOUS_GROUP_TASK_ENDED` time) and count `AUTONOMOUS_BOT_DEATH` where
victim and killer are both committed raiders inside that window.
