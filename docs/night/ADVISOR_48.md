# Advisor report, task 48 (real RvR with roaming groups), 2026-09-28 03:30

Evidence: log of the current run 23:54–03:19 (3.4 h, 0.115.0) and read-only
copies of the live save at 03:20–03:28 (dbquery.py refresh failed with
"disk I/O error" and its snapshot was overwritten mid-refresh). 1,210 bots
online; levels 11–32 or 50, nobody 33–49.

## State

RvR objective, online: 366 bots (<20: 19, 20–32: 69, 50: 278); Alb 112,
Mid 102, Hib 152.

| Place | Bots |
|---|---|
| Trapped in the enemy portal keeps in Odin's Gate (Hib 36, Alb 27+) | 76 (73 did not move in 7 min) |
| Border hubs Castle Sauvage / Svasud Faste / Druim Ligen | 110 (32 dead in the snapshot) |
| Frontier field, alive | 50 (Alb 11, Mid 17, Hib 22; Emain 16, Hadrian's 10, Uppland 8) |
| Home zones | 78 |
| Towns, dungeons, other; dead elsewhere | 36; 7 |

Field clusters (same region, within 1,500 units): 22 singles; one same-guild
cluster of 3; clusters of 5–7 hold 4–5 different guilds (brawls, not groups).

Group lifecycle (3.4 h): 134 RvR groups formed (size 2=57, 3=35, 4=28, 5=5,
6=3, 7=2, 8=4); 51 tasks started (~15/h); 71 ended (fewer than 2 members 21,
expired 17, missed the 45/15-minute meetup 19, leader missed the 20-minute
staging window 12, merged 2). Frontier teleports: 320 departures, 318 with
count=1; no group of 3+ ever boarded together; Odin Hib 83 + Odin Alb 74 = 49 %
of departures.

Fighting: keep captures ever 0 (KeepCaptureLog empty, every keep Frontier
Wardens). One siege (RVR_SIEGE_STARTED rvr-keep-75 at 00:03, present=0/0/0)
never ended. PvP engagement observations 99,860 (~29k/h), 65 % "collateral".
Deaths in the save: 749 in 5.6 min, 326 at Castle Sauvage and 265 at Svasud
Faste (82 %). AUTONOMOUS_DEATH_PVP_REPLAN 8,672, rising 1,317/h → 3,365/h;
worst victims die about once a minute at 585891,476614. RP ~430k/h from hub
farming.

## Root causes

- a) PROVEN: the border keeps are not safe. CAMLANN.md owner decision 7 makes
  Castle Sauvage, Svasud Faste and Druim Ligen neutral safe hubs, but
  PvpCombatant.IsSafeArea (serverrules/PvpCombatant.cs:97-114, SafeRegions
  :19-24) protects only capitals and portal keeps with BaseLevel>=100. Every
  RvR bot stages there (AutonomousRvrStaging.cs:21-23); released bots return
  to the nearest own-realm bindstone, the hub itself (GameBot.cs:1178). Death
  loop that eats groups before they leave.
- b) PROVEN: the keep-route planner treats portal-keep doors as hostile.
  AutonomousKeepApproachNavigation.ForRealm (:16-24) counts a door friendly
  only when keep.Realm == bot.Realm; portal keeps 20–25 have Realm=0. At
  runtime KeepManager.IsEnemy returns false for portal keeps under PvP
  (KeepManager.cs:573-576) and TryTraverse would let the bot through. Log:
  RVR_KEEP_ROUTE_FAILED 519× from 88 bots, all "No connected exterior route",
  512 targeting rvr-keep-75 (Bledmeer Faste) from the Hibernia (596055,581400)
  and Albion (596364,631509) portal keeps; 27 bots failed 8–17 times, several
  stuck 00:15–03:17.
- c) PROVEN: no give-up. FollowKeepTravel
  (AutonomousWorldBotController.KeepTravel.cs:95-107) retries forever with a
  30–120 s back-off; the siege stays open at 0 attendance.
- d) PROVEN: frontier transport is solo. BoardingParty
  (AutonomousFrontierTransport.cs:20-24, 178-181) boards a group only if every
  member is Ready (within radius, not in combat), which never holds at a
  brawling hub.
- e) SUSPECTED: 183 of 366 RvR records have an empty ObjectiveExpiresUtc;
  HasActiveRvrTenure is then false; tours may never rotate.
- f) SUSPECTED: released bots attack immediately at the bindstone, forfeiting
  release immunity.

## 1.65 versus now

In 1.65 groups formed at the portal keep, ported to a frontier keep and walked
the milegates and bridges (Emain bridge/Crauchon, Odin's Svasud/Bledmeer roads,
Hadrian's/Benowyc): 8-man roamers (2 tanks, 2 healers, CC, damage) resting at
milegates or lord rooms, zergs of 20–60 at keep takes, solo/duo stealthers at
milegate exits, presence shifting by prime time. Now: no roaming groups, no
keep fights, mass PvP only at hubs that should be safe, 20 % of the level-50
RvR force frozen in one portal keep.

## Build plan (most visible per effort first)

1. Make the three border hubs safe with a ~3,500-unit radius, per decision 7
   (PvpCombatant.cs; coordinates from AutonomousRvrStaging.cs). Low risk.
   Tests: PvpCombatant unit tests; live: hub death share collapses.
2. Planner door friendliness follows KeepManager.IsEnemy / TryTraverse instead
   of realm equality (AutonomousKeepApproachNavigation.cs:16-24). Medium risk
   (guild-owned keep doors). Tests: portal keep Realm=0 and own-guild keep;
   live: RVR_KEEP_ROUTE_FAILED toward 0, 76 bots leave Odin's Gate.
3. Give up after 3 route failures (KeepTravel.cs): drop the keep target and
   roam from the current spot or return to the hub; close sieges with 0
   attendance after ~15 min (AutonomousRvrEventLayer.cs). Low risk.
4. Board together: wait up to ~60 s for members at the porter; solo RvR bots at
   a safe hub join the hub LFG before boarding alone
   (AutonomousFrontierTransport.cs, recruitment). Medium risk (bug 35 timing).
5. Released bots do not start fights while immune; frontier PvP deaths release
   to the home hub (GameBot.cs:1178). Low risk.
6. Only then tune the 1.65 feel (doctrine tasks 27–29, resting at milegates,
   stealther pairs).

## Not verified

Navmesh not opened (cause b from the code path); killer identities at hubs
(no killer names logged); cause of the empty RvR expiry; real-client check;
1.65 behaviour from memory (Herald PvP FAQ returned HTTP 402).
