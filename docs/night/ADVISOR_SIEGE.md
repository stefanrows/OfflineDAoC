# Advisor report: why world bots never capture a keep, 2026-09-28 19:05

Evidence: run 04:41–18:18 (0.125.0, 13 h) and a read-only save copy.
World bots have had no siege behaviour since 2026-09-20: the ram/door code
exists, but its only call was removed in Camlann Tier 4 and never restored.

## What happens today

| Stage | Count |
|---|---|
| Automatic sieges opened, all present=0/0/0 | 4 (keeps 58, 77, 51, 101) |
| Ended by the 4-hour timer / idle-closed | 3 / 0 |
| Warband groups per siege (average size) | 10 / 7 / 17 / 2 (4.0–6.5) |
| Member snapshots when a group ended during a siege | 193 |
| Of those within 1,500 / 4,000 units of the keep | 0 / 3 (120 over 10k away, 70 in another region) |
| Route failures to siege keeps ("No connected exterior route", 25 bots) | 89, 25 abandoned |
| RVR_SIEGE purchase/deploy/hit lines in the whole log | 0 |
| Bot deaths within 2,500 units of a Warden keep (of 72,806) | 189, killers Renegade guards level 59/76, 123 solo bots |
| LordDefeated rows; keeps not held by Wardens | 0; 0 |

A full group opens a siege, smaller groups join, most keep porting, dying
and returning to a hub. The few that arrive run "At keep assault approach —
advancing through gates only after a real breach" (Controller.cs ~1700) and
fight only guards in line of sight. Nobody damages a door, the lord is never
exposed, the siege waits out its 4-hour timer.

## Root causes

- a) PROVEN: the siege job is dead code. `TryRunSiegeJob` (Controller.Siege.cs:70)
  has no caller; commit 3f4d963 replaced the call with "siege-kit work begins
  in Tier 5" (Controller.cs:1512-1513) and it was never restored.
- b) PROVEN: no damage path to a door without a ram. `FindRvrTarget`
  (Controller.cs:1731-1765) returns guards only and filters the lord while a
  door is closed. Door hit points (MaxHealthCalculator.cs:93-94): level-5 keep
  50,000; relic keep 120,000; relic gate 180,000.
- c) PROVEN: empty sieges never close. `ReportMarch` (EventLayer.cs:70-98)
  counts any region change and off-region movement, so the porter carousel
  keeps every siege alive (idle-close fired 0 times).
- d) PROVEN: only one siege server-wide. `ChooseOrJoin` returns ReservePlan
  while any non-defense event exists (EventLayer.cs ~433); each empty 4-hour
  siege blocks all other assaults (3 sieges in 13 h).
- e) PROVEN: the first target could never be claimed. Castle Myrddin (58) is a
  relic keep (SkinType 99, BaseLevel 60); `CheckForClaim` rejects
  BaseLevel != 50 while `allow_bg_claim` is False. Six relic keeps are in the
  automatic pool.
- f) STRONGLY SUSPECTED: garrison too strong for level-50 bots
  (AbstractGameKeep.cs:912-947, multiplier 1.6): level-5 keeps guards 59, lord
  70; relic keeps guards 76–77, lord 90. Guard kills are not logged.
- g) SUSPECTED: forces too small and mixed (join needs average level 35, siege
  groups average 4–6; a claim needs 8 grouped members of one guild).
- Not causes: supplies (all 293 RvR bots 35+ hold ≥ 10,000 g, ram kit 50 g,
  111 operator classes, siege merchant at each hub); claim limit
  (`guilds_claim_limit` 1, crews own 0 keeps). KeepCaptureLog is empty because
  `log_keep_captures` is False; LordDefeated = 0 is the proof.

## 1.65 and the minimum here

8–30 players bought rams, rammed the outer gate (max 3 engines per door since
1.60), meleed the door, killed wall guards and archers, then inner door, lord
room, lord kill, claim by a guild leader with a full group; 10–40 minutes.
Minimum here: a one-guild warband of 8 at a claimable (BaseLevel 50) keep; one
operator places a ram, the rest clear guards and melee the door; breach, inner
door, lord; leader claims at the Keep Claim Steward (Controller.cs:1446-1459,
PvpKeepCampaign.TryClaim exists). With the temporary ×10 ram (task 55): 1,250
per hit, 14 s reload, one ram breaks a level-5 outer door in about 9 minutes.

## Build plan

Slice 1 (first real capture): 1. re-wire `TryRunSiegeJob(bot)` in ExecuteRvr
before FindRvrTarget (Controller.cs:1512); 2. door melee fallback in
FindRvrTarget (closed enemy door within ~400 units when no guard is
targetable); 3. keep relic keeps (IsRelic or BaseLevel != 50) out of automatic
AssaultKeep (Controller.cs:2142-2154); 4. idle-close: march credit only for
approaching in the target region, close after 45–60 min with nobody within
3,000 units (EventLayer.cs:70-98, 820-835). Live check: RVR_SIEGE_IDLE_CLOSED
> 0, LordDefeated = 1, ClaimedGuildName becomes a crew guild.
Slice 2 (arrival): 5. only whole guild warbands of 8 open or join sieges;
suppress the porter carousel for committed siege forces; 6. offline navmesh
check of TryGateApproach for all 21 claimable keeps (89 exterior-route
failures).
Slice 3 (balance, Aaron's decision): 7. Warden garrison strength (guards
~52–55, lord ~60–65 via keep level 1 or a Warden multiplier; or keep and
require 16+ bots); 8. one siege per realm or guild; 9. remove the ×10 ram.

## Not verified

Why exterior routes fail (navmesh not opened); whether bot melee damages doors
in game; guard kill counts and the in-game lord level; exact 1.65 guard levels;
real-client check. Sources: Camelot Herald patch notes 1.65 and 1.60.
