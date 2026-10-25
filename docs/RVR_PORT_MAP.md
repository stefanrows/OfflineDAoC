# RVR_PORT_MAP: upstream RvR bot layer vs fork (for R4c)

**Status 2026-10-25 21:45.** S1-S3 and the R6 review fixes are merged as 0.342.0-0.345.0 (S1 0.342.0, S2 0.343.0,
S3 0.344.0, R6 fixes 0.345.0) and deployed on the owner's PC on a fresh level-50 RvR world; R4c is closed, R6 waits for
the live observation of the first sieges and the owner's real-client check. Camlann keeps the fork flow. Not ported
(S4 backlog, [RVR_MODE_PLAN.md](RVR_MODE_PLAN.md)): lord shield, no item loot from guards/gamebots/pets,
AutonomousGroupPace, battleground siege announcements, dashboard muster column, DF route + neutral raids (needs DF proofs
for our mesh), opt-in battleground goal (owner), the remaining raid refill/regroup fixes, `SupersedeAutomatic`, the
`BotBrain.SupportOffense` MinValue overflow. Owner decisions 2026-10-25 22:20: upstream relic rules in Realm-1 (R3b to
Camlann only, S4), fork frontier porters kept and adapted to RvR (S4), task mix / warband composition original by default,
PvP release immunity kept. What the RvR world is missing compared with Camlann (the
fork extensions gated off in Realm-1): [RVR_FORK_EXTENSIONS.md](RVR_FORK_EXTENSIONS.md). Sections 0-5 below are the read-only analysis of 0.341.0;
sections 6, 7, "S3 done" and "R6 fixes" record what was built.

Read-only analysis, 2026-10-25. Fork `main` 2b306524 (0.341.0). Upstream `upstream/main` 9da90910 (0.38c, 2026-10-20).
Merge base of both = fad5a027 (2026-09-16, upstream 0.3). **The fork never merged upstream after 0.3**: every upstream
RvR change of 0.31-0.38c (realm armies, staged sieges, retake, march column ...) is absent unless hand-ported.
v0.37b -> 0.38c delta in bots/keeps/rules: 258 lines (siege resurrection +41, event layer +33, DF route +31,
controller +61) -> port from `upstream/main`, v0.37b needs no separate pass.

Notation: U/F = lines in upstream / fork today. upD / fkD = change since the merge base (upstream / fork).

## 0. Live state of the RvR world (evidence)

| Fact | Value | Source |
|---|---|---|
| RvR boot | log line 2034756, 11:09:48, `World model: Realm-1`, GST_PvP + RvR rules | server-console.log |
| RvR event-layer log lines since boot | **0** (RVR_* only: 17 RVR_HUB_PEACE, 1 RVR_REALM_POPULATION) | log, 51,579 lines |
| World bots | 1,801 (600/600/601), **all level 1-19**, all online | offline_world_bots |
| Keeps | 51 realm-owned (45 portal keeps lvl 1, 6 relic keeps lvl 10); 32 "Frontier Wardens" neutral (21 frontier keeps with OriginalRealm 1/2/3, 11 OriginalRealm 0); 2 neutral unclaimed | keep table |
| Relics | 6 at home, KeepID 0 | Relic |
| relic_players_required_on_pad | 16 (upstream default 8, companions count) | serverproperty |
| bot_route_threat_awareness | False (upstream default **true**) | serverproperty |

Consequence: neither the fork nor the upstream siege flow can be observed in Realm-1 until bots reach 50 (upstream
gates every siege on average AND minimum level 50, event layer ChooseCore upstream line 300). The only live
"original" reference is the upstream 2026-10-23/24 run in RVR_MODE_ANALYSIS.md (6,487 RVR_BOT_KILLED, 455 siege
events, 2 keeps taken).

## 1. File map of the upstream RvR layer

Verdict: TAKE = upstream behaviour in Realm-1; KEEP = fork version stays everywhere; BOTH = upstream in Realm-1,
fork in Camlann (WorldMode branch or facade); MERGE = shared file, union of both, no branch needed.

### 1a Event layer and realm events
| Upstream file | U / F | upD / fkD | Player sees with upstream (vs fork) | Verdict |
|---|---|---|---|---|
| AutonomousRvrEventLayer.cs | 1523 / 1319 | +713-58 / +521-70 | One server-wide siege roll per minute (25 %/min rising when idle, 4 % with one siege, 1.2 % with two, max one attack per realm); muster 12+5 min (relic 25 + up to 6x10 while <120) at own portal outpost; march at 108 / relic 170 or 85 % of assigned (`MarchThreshold` l.575); battle opens on leader arrival or 60 % quorum (l.579); repel after 10 min absent; retake once after a player capture (l.598); third realm in 50 % of sieges after a delay (vs fork: per-army musters, attack plans single/double/opportunist, home reserve, siege cap, guild claims) | BOTH (facade) |
| AutonomousRvrEventLayer.Defense.cs | 328 / 267 | +126-10 / +73-18 | Defense rally 6+4 min, marches at 80 % (`DefenseWaveSize`), player attack raises a defense sized to the player's force (/raid 80 -> up to 240) | BOTH (facade) |
| RealmEventPolicy.cs | 66 / 35 | +31 / 0 | Caps 128/64/64 keep, 240/120/120 relic, `rvr_siege_defense_ratio` 0.5, `CanRespond` | TAKE (fork unchanged since base; additive) |
| RealmEventNotices.cs | 96 / 65 | +31 / 0 | Screen-centre announcements per realm ("Albion rallies to retake ...") | TAKE |
| RealmEventRecordStore / Records / Controls / Banter | 75/64, 77/78, 138/133, 44/43 | <=11 / <=2 | Launcher RvR history fields | MERGE |
| GameWideAnnouncements.cs | 68 / - | new | `rvr_battleground_announcements`, `pve_realm_event_announcements` switches | TAKE |
| AutonomousRvrDashboard.cs | 276 / 195 | +94-4 / +12-3 | Launcher RvR tab: "marching in 6m", attack window separate | MERGE |

### 1b March, staging, siege floor (controller side)
| Upstream file | U / F | upD / fkD | Player sees with upstream (vs fork) | Verdict |
|---|---|---|---|---|
| AutonomousWorldBotController.Rally.cs | 454 / 194 | +303-4 / +52-13 | Army marches as one column behind a march leader (main-body rule), waits for stragglers, members assist each other, `TryMarchWithArmy` | BOTH |
| AutonomousArmyPush.cs | 44 / - | new | Army stuck 4 min fighting -> leader stops fighting and pushes 2 min | TAKE |
| AutonomousArmyDetour.cs | 67 / - | new | March goes around enemy keeps/towers and the defenders' gather keep | TAKE |
| AutonomousRvrStaging.cs | 176 / 126 | +108-7 / +56-5 | `TryGetSiegeMuster`: portal outpost in target frontier, else nearer border keep (fork adds hub/release helpers) | MERGE |
| AutonomousWorldBotController.Siege.cs | 514 / 708 | +163-52 / +339-34 | Rams at outer gate then keep door, door order at keeps with >=10 doors, rams past wrecks, 2 rams + trebuchet per army, defenders' catapults/ballistas (vs fork siege doctrine) | BOTH |
| SiegePlacement.cs | 99 / - | new | Engine spots in front of gates / artillery rings | TAKE |
| AutonomousSiegeResurrection.cs | 150 / - | new | Army healers raise the fallen during battle, walk out to the dead, stay behind the line | TAKE (+ GameBot/BotBrain hooks) |
| AutonomousBotGroupCoordinator.SiegeParties.cs | 92 / - | new | Solo siege answerers merged into full 8-man parties | TAKE |
| AutonomousWorldBotController.KeepTravel.cs | 230 / 508 | +86-3 / +373-12 | Keep-travel stall handling (fork: guild-army march, Stefan's road lookahead) | BOTH (keep Stefan's 135/138 parts shared) |
| AutonomousKeepTravelStallPolicy.cs | 72 / - | new | Stalled keep travellers re-plan in legs | TAKE |
| AutonomousWorldBotController.FrontierThreat.cs | 202 / 145 | +124-7 / +84-24 | Army members help a member under attack, hold siege posts | BOTH |
| AutonomousRvrTravel.cs | 56 / 263 | +5-26 / +212-26 | Direct legs (fork: flank/cover route variants, 600-2,000-unit dog-legs, task 139 note) | BOTH |
| AutonomousFrontierTransport.cs + controller part | 264/683, 136/211 | +17-3,+11 / +452-19,+112-26 | Frontier porter boarding | KEEP fork + port upstream's 28 lines |
| AutonomousSiegeJobs.cs | 80 / 74 | +19-5 / +11-3 | Siege crew jobs | MERGE |
| AutonomousDefensivePull.cs | 328 / 234 | +103-8 / +4-3 | Group pulls defensively | TAKE (fork near base) |
| AutonomousBotRealmPointRewards.cs | 260 / 334 | +77-1 / +174-24 | Gamebots near a player kill get RPs (`PayGamebotsForPlayerKill`) | MERGE |
| AutonomousRvrKeepPolicy.cs | 17 / 62 | +3-1 / +48-1 | **Neutral keeps are never siege objectives outside BGs** (collision 4a) | BOTH |
| AutonomousRvrTargetPolicy.cs | 9 / 54 | 0 / +48-3 | Fork: Camlann grey-target restraint; upstream none | BOTH |
| AutonomousRealmBoundary.cs | 22 / 59 | 0 / +40-3 | Bots out of enemy homelands (R2 already = upstream rule + walk-out) | KEEP |
| Identical or <=7 lines either side: AutonomousRvrApproach, Director, ObjectiveState, Defense, Rally (AutonomousRvrRally), PlanningView, RvrKeepRoute, RvrPlanningNavigation, SiegeOwnership, SiegePolicy, FrontierBoardingQueue, RealmRaidBoardingQueue, RealmRaidStableTravel, RealmWarbandSupport, DefensivePost | - | - | none | no work |

### 1c Raids, battlegrounds, teleports, names, relics
| Upstream file | U / F | upD / fkD | Player sees with upstream (vs fork) | Verdict |
|---|---|---|---|---|
| AutonomousRealmRaid.cs | 1138 / 890 | +421-55 / +140-22 | Realm raids finish Caer Sidi/Olcasgean, switch idle raiders, healers (fork: mixed-realm battlegroup wrapper) | BOTH |
| RealmRaidDungeonRoute.cs (+ data/darkness_falls_navigation_proofs.json 724k lines, DF route/goal files) | 805 / 130 | +693-18 / 0 | DF raids reach bosses, failed searches shared | TAKE (heavy, session 3) |
| RealmRaidMuster / Formation / Staging / RecruitmentPolicy | 110/75, 54/39, 75/66, 68/59 | +42,+21,+9,+40 / 0,0,0,+30 | Rally timing (>=45 min after hub), refills | TAKE (Recruitment MERGE) |
| RealmRaidNeutralEvents.cs, RealmRaidHubEscape.cs | 129, 10 / - | new | Neutral raid encounter level cap | TAKE |
| AutonomousBotGroupCoordinator.ForcedRaid.cs | 202 / 160 | +96-6 / +56-8 | Forced raid start/route reason | BOTH |
| AutonomousWorldBotController.Battleground.cs + AutonomousBattlegroundWave.cs + world/BattlegroundBrackets.cs + scripts/teleporters/BattlegroundTeleportOptions.cs | 370+136+106+71 / - | new | "Battlegrounds %" goal: bots of 15-19/20-24/25-29/30-35 port to Abermenai/**Thidranki 252**/Murdaigean/Caledonia and storm the central keep in waves | Owner decision (collision 4f) |
| AutonomousWorldBotController.TownTeleport.cs + AutonomousTownTeleporters.cs | 289+258 / - | new | Bots use Master Visur & co. when quicker (`bot_use_town_teleporters`, upstream marks it non-1.65) | TAKE (R2 realm-only porters already in) |
| AutonomousNameMask.cs | 59 / - | new | Enemy gamebots shown by race + realm-rank title | KEEP fork R1b (RvrServerRules.GetNPCName/GetPlayerName), check broadcast `Masked()` parity |
| keeps/Relics/GameRelicPad.cs + property | +15-9 | - | 8 on the pad, companions count (fork 16, companions unverified) | TAKE (Realm-1 value) |
| keeps/Gameobjects/Guards/Lord.cs, ai/brain/Guards/GuardBrain.cs, GameSiegeRam.cs | +27, +13, +22-2 | - | Lord untouchable while gate/keep door stand; rams target keep door after gate | MERGE against R3a |
| serverrules/AbstractServerRules.cs | +11-1 | - | Guards, bots, bot pets drop no items | MERGE (check fork's own rule) |
| bots/CompanionRaidSiege.cs | 665 / - | new | /raid companions fight sieges | KEEP fork companions (candidate 3) |

### 1d Shared big files (hand-port hunks behind WorldMode.IsRvr)
| File | U / F | upstream changed lines since base (ignore ws) | of which RvR hunks |
|---|---|---|---|
| AutonomousWorldBotController.cs | 5388 / 6123 | 1,732 | ~580 |
| AutonomousBotGroupCoordinator.cs | 3061 / 3681 | 944 | ~250 (`FormSiegeParties`, `IsEventForce`) |
| bots/GameBot.cs | - | 436 | ~150 (`TrySiegeRelease`, `ArmyResurrectorNear`, `IsSiegeAttacker`) |
| bots/BotBrain.cs | - | 1,036 | ~176 (`TryResurrectSiegeComrade`, `ArmyUpkeep`, CompanionRaidSiege hooks) |

### 1e Properties and DB
- Upstream-only properties to add (Realm-1 effect): `rvr_siege_staged_assault` (true), `rvr_siege_defense_ratio` (0.5),
  `rvr_battleground_announcements`, `pve_realm_event_announcements`, `bot_use_town_teleporters` (true),
  `neutral_raid_encounter_level_cap`, `player_keep_defense_horn_sound`.
- Different defaults: `bot_route_threat_awareness` upstream true / fork false; `relic_players_required_on_pad` 8 / 16.
- Fork-only RvR properties ignored in Realm-1 after the port: `rvr_army_*` (7), `rvr_grudge_hunters_per_target`,
  `siege_column_quorum*`, `siege_army_keep_level_quorum`. `rvr_realm_active_target` stays (owner's 600/realm).
- DB: the upstream RvR layer adds **no table**; it uses Keep, Relic, KeepCaptureLog (same in fork) and its own
  SQLite side store (RealmEventRecordStore `Events`). Upstream `offline_world_bot_guild` (player invites bots into
  a guild) is not RvR-layer. **No schema change needed.**

## 2. Fork-only files: gate in Realm-1, do not delete

| Fork file(s) | Lines | Camlann depends? (what breaks if deleted) |
|---|---|---|
| AutonomousRvrEventLayer.{GuildArmy,Muster,SiegeCap,Claims,GuildKeepDefense}.cs | 310+229+66+52+35 | Yes: guild armies, bug 75 muster/march, task 48 siege cap 2b, guild claims, guild keep defense |
| AutonomousRvrEventLayer.RealmArmy.cs, AutonomousRealmArmyPolicy.cs, AutonomousRealmArmyState.cs (R4b) | 105+218+271 | RvR-only, but referenced from GuildArmy, SiegeCap and the controller -> compile break; R4b also changed both-world bits (human-killer grudges resolve, open BG units not split by keep guild) |
| AutonomousRvrSiegeMuster, AutonomousSiegeMarch, AutonomousSiegeDoctrine, AutonomousSiegeProperties | 128+53+114+21 | Yes: Camlann siege flow; SiegeMarch is called from GameBot, BotBrain, PetSupport, RouteThreats |
| AutonomousWorldBotController.{GuildArmy,GuildKeepDefense}.cs, AutonomousGuildKeepDefense, AutonomousGuildAssault, AutonomousGuildCohesion, AutonomousGuildEncounterMemory | 148+189+109+73+68+71 | Yes: guild = side in Camlann |
| AutonomousGuildGrudgeMemory (realm grudges R4a inside) | 472 | Yes: Camlann grudges/banter; RvR branch keyed by realm |
| AutonomousRealmGuildNames (R4a), AutonomousGuildSeeding | 119+187 | Seeding yes (both worlds' reset); names RvR-only |
| AutonomousRealmLoginBalancer (R4a, 148 vs upstream 32) | 148 | Called by population controller in both worlds |
| "Livelier RvR" waves: RvrDoctrine(+Runtime), RvrObserve(+controller), RvrSpeed, RvrStealthLoop(+controller), RvrGroupPause(+RvrPause), RvrSupportOffense, RvrCombatHabits, RvrDangerMemory, RvrHeat, RvrHotspots, RvrHubMuster, RvrLfg, RvrMobAvoidance(+controller), RvrRoutePolicy | 308+773+951+149+545+591+51+290+101+205+181+269+71+23+120+45+336+96+278 | Yes: Camlann roaming groups use all of them |
| AutonomousKeepGateGoals, AutonomousRouteGoalFix (Stefan 139) | 197+ | Both worlds; movement fix, keep shared |
| UT_RvrBotGuildsAndLogin, UT_RvrRealmArmies | - | Keep; mark RvR cases as "gated off" or retarget to the upstream flow |

Gate rule: the facade never calls the Camlann implementation in Realm-1, plus `WorldMode.IsRvr` guards at the
controller entry points of the waves above and of guild-army/claim/grudge-hunt code.

## 3. Added-value candidates (owner decides; default = upstream)

| Feature | Recommendation |
|---|---|
| Persistent companions, squads, battlegroups in RvR | **Keep fork** (core fork value; upstream /raid companions not taken) |
| ToA, Master Levels, bot ML, artifacts | **Keep** (owner: ToA stays) |
| Bot active realm abilities (fork Stage 5 vs upstream AutonomousBotRealmAbilities) | Keep fork (already built from upstream's ideas, saved cooldowns) |
| Realm Exchange (own realm only, R2) | Keep fork |
| Open BG window in Thidranki 252 (perrealm) | Keep, but it collides with upstream's 20-24 Thidranki bracket: decide (4f) |
| Fork battleground campaign vs upstream "Battlegrounds %" goal | Upstream for the level brackets, open BG stays |
| Bot guilds with 2003 names (R4a) | Keep cosmetic (names in /who, guild chat); armies by realm as upstream; no bot keep claims (upstream has none) |
| Guild keep claims by bots, level-1 keeps until claimed (R3a) | Upstream (keeps start at original level, no bot claims); player claims stay |
| Attack plans single/double/opportunist, home reserve, revenge trips, grudges (R4a/R4b) | Off (upstream siege roll + retake) |
| Danger memory, livelier-RvR waves (observe, pause, stealth loop, speed, doctrine, LFG, hub muster, mob avoidance, route variety) | Off in RvR by default; owner may re-enable single waves later, measured against upstream |
| Route threat awareness | On in RvR (upstream default) |
| Realm login target 600/realm | Keep (owner decision 9); selector is upstream's lowest-load rule anyway |

## 4. Collision points

a. **Keep start state (blocking).** Upstream `AutonomousRvrKeepPolicy.IsSiegeObjective` (upstream file l.11-14)
   rejects Realm None outside battlegrounds; upstream 0.36 resets "every keep, relic and battleground keep to its
   original owner and level". Our Realm-1 save has the 21 frontier keeps neutral (Frontier Wardens, owner
   decision 12). Ported as is -> **no frontier keep siege ever; only the 6 relic keeps are targets.** Upstream also
   sets `DefenderRealm = OwningRealm`, so neutral targets give no defender realm, no defense rally and no retake.
   Recommended: R5a reset seeds frontier keeps with `OriginalRealm` and original level, plus a one-time stopped
   migration of the current RvR save (21 rows). Needs the owner to revise decision 12.
b. **Friend/foe sites** (raw realm compares in upstream; in Realm-1 route via `PvpCombatant.AreAllied(WorldModelKind.Realm, ...)`
   / `AllianceRealm` so companions, pets, BG encounter bots and realm-None Wardens resolve right):
   FrontierThreat 29, 81, 115, 146, 159, 171; Siege 47, 90, 103-104, 145, 200, 228, 322, 402, 411; SiegeResurrection
   48, 68, 98, 108, 126; Battleground 44, 254, 266, 303, 309; Rally 26, 83, 103, 189, 317, 329, 437; ArmyDetour 61, 64;
   TownTeleporters 123, 154; NameMask 16. Keep ownership -> `KeepManager.IsEnemy` (R3a RvR branch). Attacker/defender
   bookkeeping (74 compares in the event layer) stays on raw realm. Extend UT_CamlannTier3HostilityAudit to the new sites.
c. Upstream `IsAllowedToAttack` calls (Rally 331, FrontierThreat 149, Battleground 104/269, DefensivePull 100) already
   land in RvrServerRules: fine.
d. Server-type branches in upstream keep code (AbstractGameKeep 237, Lord 51/77/390, MissionMaster 286, KeepManager 555)
   are already covered by the R1b audit (docs/RVR_RULES_AUDIT.md #26-28); merge upstream's Lord.cs +27 against R3a.
e. **Static state + tests:** the event layer is one static class whose tests clear private fields by name
   (RvrEventTestState). Two implementations cannot share the name -> facade (section 5).
f. **Thidranki 252** is the fork's open BG (OpenBattlegroundMode.cs:20, catalog "open BG" 1-50); upstream's bracket
   goal sends level 20-24 bots there. Choose: open BG stays on 252 and upstream's Thidranki bracket is dropped, or the
   open BG moves.
g. Relic pad: 16 vs 8, companions counted upstream (GameRelicPad `CountsOnPad`).
h. Level gate: upstream sieges need level 50; our bots are 1-19, so live checks of the port wait for levelling.
i. Teleporters: upstream bot town-teleport needs realm-only porters (R2 done) and the enemy-homeland rule (done).

## 5. Port plan (3 sessions + R6)

Mechanism:
1. Rename the fork's 8-part `AutonomousRvrEventLayer` to `CamlannRvrEventLayer` (mechanical, Camlann byte-for-byte).
2. Copy upstream `AutonomousRvrEventLayer.cs` + `.Defense.cs` as `RealmRvrEventLayer` (renamed, otherwise verbatim).
3. New `AutonomousRvrEventLayer` facade: shared records (Force, Plan, Intent, LiveObjective, RallyOrder, BattleNotice:
   union of fields with defaults); every public method dispatches on the startup-latched `WorldMode.IsRvr`; upstream-only
   calls no-op in Camlann, fork-only calls no-op in Realm-1.
4. Controller: copy upstream Siege/Rally/KeepTravel/FrontierThreat bodies into `AutonomousWorldBotController.Realm*.cs`
   with prefixed names; one branch per entry point. New upstream helper files copied verbatim (no name clash).
5. Files the fork never touched since base: take upstream verbatim (RealmEventPolicy/Notices, RealmRaid* small files,
   DungeonRoute, DefensivePull).

| Session | Content | Upstream tests to carry | Size | Status |
|---|---|---|---|---|
| S1 core | Keep-start reset (after owner yes), facade + rename, RealmRvrEventLayer + Defense, RealmEvent*, GameWideAnnouncements, KeepPolicy RvR branch, new properties | UT_AutonomousRvrEventLayer (159), UT_StagedSiegeOct6 (64), UT_SiegeSchedulingOct7 (22), UT_SiegeMusterAndRankOct6 (38), UT_RaidRallyTiming (34), UT_RaidRefillOct6 (20), UT_PlayerKeepDefense (138), UT_RealmEventPolicy (111), UT_RealmEventRecords (58), UT_RvrExpansion (416); RvrEventTestState split per world | ~2,000 copied, ~400 hand | **Done in source**, real-client check pending (section 6) |
| S2 march + siege floor | Rally column + march leader (reuse Stefan's `ChooseMainBodyLeader`), ArmyPush/Detour, Staging muster, Siege + SiegePlacement, SiegeResurrection + GameBot/BotBrain hooks, SiegeParties + coordinator hunks, KeepTravelStallPolicy, FrontierThreat, ForcedRaid, RP rewards; RvR guards on fork waves; friend/foe sites (4b) | UT_PhysicalSiegeRally (176), UT_SiegePlacement (82), UT_SiegeParties (41), UT_SiegeDoorNumber (18), UT_AutonomousArmyPush (36), UT_RvrLeaderStaging (53), UT_AutonomousSiegeEquipment (218), UT_EveningAuditFixes (detour/wave parts), Tier 3 audit extension | ~1,500 copied, ~800 hand | **Done in source**, real-client check pending (section 7); ForcedRaid and RP rewards moved to S3 |
| S3 periphery | Relic pad 8 + companions, battleground goal (after 4f), town teleporters, realm raids (RealmRaid +421, NeutralEvents, DungeonRoute + DF proofs), route threat on in RvR, road fallback (task 143) | UT_BattlegroundGoalsOct7 (89), UT_NeutralRaidEventsOct6 (113), UT_RelicPadAttackers (40), UT_EventRouteAudit (160), UT_RealmRaidRecruitment (269), UT_EnemyBotNamesOct7 (parity) | ~1,800 copied (DF data excluded), may spill into a 4th session | Open; plus UT_RaidRallyTiming / UT_RaidRefillOct6 from S1 |

Already covered by Stefan (do not redo): task 135 retarget while moving (= upstream "march without stopping at each
waypoint" for keep-travel/group legs); 136 main-body leader for group sessions (upstream applies the same rule to the
army march leader -> reuse it); 137/138 formation follow + GroupMotion (fork versions are supersets of upstream's
AutonomousGroupFormationFollow/GroupMotion: 353/255 vs 230/77 lines); 139 hotspot evidence + goal fixes (fork-only, keep);
Stage 6 route threats (ported, default off). Not covered: army column (Rally.cs), ArmyPush/Detour, 0.38 portal-keep door
fixes, AutonomousZoneRoads/Seams/Pockets and ZoneItinerary +620 (task 143 still open), AutonomousGroupPace.

## 6. S1 status (2026-10-25, port session S1; implemented in source, real-client check pending)

Worktree branch `rvr-port-s1` (not merged, no version). Server suite 4,994 passed / 7 failed (the bug 160 set, unchanged) /
6 skipped; launcher suite 328 passed / 2 environment-skipped. No live run.

**Mechanism as planned.** `AutonomousRvrEventLayer` is now a facade: the shared records (Force, LiveObjective, Plan, Intent,
BattleNotice with upstream's `MusterRemainingMilliseconds`, RallyOrder with the fork's `Side` and upstream's `HomeMuster`,
GuildArmyOrder, DefenseScale, SoloSiegeRoster) plus one dispatch per public call on `WorldMode.IsRvr`. Shared constant
names answer the Camlann value; RvR values are on `RealmRvrEventLayer` (relic cap 240, target cooldown 12 min).
A call only one layer knows answers a neutral default in the other world (no muster, null, false, empty).

| Part | S1 result |
|---|---|
| Fork 8-part layer | Renamed to `CamlannRvrEventLayer` (class name, `using static` of the facade, shared types moved out); code otherwise byte-for-byte. Tests that reflect on its private fields point at the new type. |
| Upstream `AutonomousRvrEventLayer.cs` + `.Defense.cs` | **Verbatim** as `RealmRvrEventLayer` (+ `.Defense`), LF. Local changes marked `Fork:`: shared types removed, RallyOrder also carries the side, a keep hit by a pet/companion/helper counts for its owner's realm (`PvpCombatant.AllianceRealm`), upstream's battleground siege announcement (needs BattlegroundBrackets, S3) and `/raid` companion siege hooks (CompanionRaidSiege, fork companions stay) left out. |
| RealmEventPolicy, RealmEventNotices, RealmEventRecordStore, RealmEventBanter, GameWideAnnouncements | **Upstream verbatim** (the fork never changed the first four since the base; additions only). New properties `rvr_siege_staged_assault`, `rvr_siege_defense_ratio`, `player_keep_defense_horn_sound`, `rvr_battleground_announcements`, `pve_realm_event_announcements`. RealmEventRecords and RealmEventControls stay fork (upstream's raid "stop" needs `AutonomousRealmRaid.Stop`, S3). |
| AutonomousRvrStaging | MERGE: upstream `TryGetSiegeMuster`, both border keeps per realm, `TryGetBorderKeepNear`; fork helpers unchanged. |
| AutonomousRvrKeepPolicy / TargetPolicy / Defense | BOTH: RvR world takes upstream (neutral keep a siege objective only in battlegrounds 250-253; no grey-target restraint; wall defenders hold their realm's keep, not their guild's). Camlann unchanged. |
| Controller (hand) | RvR only: upstream `HandleSiegeMuster` / `TryGetDefenseMuster` verbatim in `AutonomousWorldBotController.RealmMuster.cs`, called by `HandleSiegeRally` while the order is a home muster; Force as upstream (siege-ready = 4 members, average 35, siege supply; fixed 30 % roaming reserve); keep objectives report `UnderAttack` as upstream; no revenge trips; no bot keep claims. Coordinator: upstream's `PulseDefense` and `RealmWarbandSupport.Pulse` (per realm) run again in the RvR world. |
| Gated off in Realm-1 (code kept) | R4b realm armies (`AutonomousRealmArmyPolicy/State`, RealmArmy partial, home reserve, census), R4a/R4b grudge hunts and revenge trips, the fork's guild-army muster, siege cap, claim plans, guild keep defense (all reached only through the Camlann layer or gated in the controller). UT_RvrRealmArmies now calls `CamlannRvrEventLayer` directly so the kept R4b code stays tested. |
| Kept in Realm-1 (deviation from the S1 brief, map section 3) | Realm login balancer (owner decision 9: 600 per realm; its selector is upstream's lowest-load rule) and the 2003-style realm guild names (cosmetic). |
| Keep start | RvR world: the 21 frontier keeps stand for their original realm at upstream's release level (`RvrKeepStart`: Albion and Midgard 5; Hibernia Dun Crauchon 4, Crimthain/Bolg/Scathaig 5, nGed/Da Behnn/Ailinne 1; source: upstream `release-world-defaults.json`, "the original v0.32 world"). Written by the fresh RvR world (R5a `ResetKeeps`) and RESET KEEPS (`KeepRelicReset`); an existing Realm-1 save moves its Warden-held frontier keeps once at the next server start (`PvpKeepCampaign.FrontierKeepStart`, log `RVR_KEEP_ORIGINAL_START`). Portal, relic and battleground keeps unchanged. The R3a start rule then opens each unclaimed realm keep's steward for a claim. |
| Bot levels of a fresh RvR world | Upstream ships an empty world (`build_release_037.py`: "a bot would ship" assertion; PLAY.md "A new install starts with an empty world"); players create gamebots with the realm cards' "+ Lv.1" / "+ Lv.50" buttons, and only level-50 bots take RvR objectives or open sieges. Sieges within a day on a fresh upstream install therefore mean "+ Lv.50" bots. The fresh RvR world (CREATE / RESET WORLD) now creates its roster at level 50 with the class loadout (`WorldReset.FreshLevels`); an explicit world shape still wins; Camlann unchanged. An existing save keeps its bots' levels. |
| Tests carried | UT_AutonomousRvrEventLayer, UT_StagedSiegeOct6, UT_SiegeSchedulingOct7, UT_SiegeMusterAndRankOct6 (muster part; the pet-rank test is not RvR), UT_PlayerKeepDefense, UT_RvrExpansion as `UT_RealmRvr_*` against `RealmRvrEventLayer` (60 cases, one TargetPolicy test adapted to the fork's signature); UT_RealmEventRecords' DeleteAll test merged into the fork file. Not carried: UT_RealmEventPolicy (its upstream delta is raid-dungeon corner skipping), UT_RaidRallyTiming and UT_RaidRefillOct6 (need upstream's RealmRaidRecruitmentPolicy, S3). New: UT_RvrPortS1 (34 cases: dispatch, level-50 gate, one attack per realm, thresholds, response order, retake, repel, neutral defaults, keep start), three launcher tests. |

**What S2 must do.** The march itself is still the fork's walk to the rally posts: after the march order the army walks
warband by warband and the siege opens on the 60 % arrival quorum (upstream also opens it on the march leader's arrival,
`ReportArmyArrived`, which no controller calls yet). Port upstream Rally.cs `TryMarchWithArmy` (march leader, column,
regroup, ArmyPush, ArmyDetour), the siege floor (Siege.cs, SiegePlacement, door order, engines), SiegeResurrection and
the GameBot/BotBrain hooks (`TrySiegeRelease` is in the facade, unused), `FormSiegeParties` with `SoloSiegeRosters` /
`MergeSoloForces` (in the facade, unused), KeepTravel stall policy, FrontierThreat, the RvR guards on the livelier-RvR
waves (observe, pause, stealth loop, speed, doctrine, LFG, hub muster, danger memory, mob avoidance) and the friend/foe
sites of section 4b. Also check: the fork's ChooseRvrDestination still filters and orders the candidate objectives (upstream's
frontier commit and `rvr-roam-` anchors are not ported); the fork's RvR objective may reach bots below level 50 (its
controller has a low-level PvP planner for bots under 20; not measured), upstream gives RvR only at 50 (plus its battleground goal); the fork's `starting_keep_claim_level` 1 drops a claimed RvR keep
from 5 to 1 (upstream 5); the R3a capture still flips a keep to level 1 awaiting a claim. S3 adds the dashboard's muster
column (AutonomousRvrDashboard MERGE).

## 7. S2 status (2026-10-25, port session S2; implemented in source, real-client check pending)

Worktree branch `rvr-port-s2` (not merged, no version). Server suite 5,059 passed / 7 failed (the bug 160 set, unchanged
since S1) / 6 skipped; launcher not touched. No live run.

**Mechanism.** One branch per controller entry point on the startup-latched `WorldMode.IsRvr`: `ExecuteRvr` hands the RvR
world to `ExecuteRvrRealm` after the shared battleground prefix, `HandleSiegeRally` to `HandleSiegeRallyRealm`,
`TryEngageFrontierThreat` to `TryEngageFrontierThreatRealm`, `ChooseRvrDestinationTimed` to `ChooseRvrDestinationRealm`.
Battleground participants (assigned or inside a campaign region) keep the fork's turn in both worlds until S3 (4f).
`AutonomousRvrWaves.ActiveFor` is the one switch for the livelier-RvR waves. Copied code is upstream text with `Fork:`
marks for every local change. Camlann paths are unchanged (every new branch answers the old value outside Realm-1).

| Part | Verbatim / branched | S2 result |
|---|---|---|
| AutonomousArmyPush, AutonomousArmyDetour, SiegePlacement, AutonomousKeepTravelStallPolicy, AutonomousSiegeResurrection, AutonomousBotGroupCoordinator.SiegeParties | **Verbatim** new files | Detour and resurrection: friend or foe by the realm rule (`AutonomousRvrKeepPolicy.IsHostileTo`, `PvpCombatant.AreAllied/AllianceRealm`). SiegeParties runs in the coordinator's maintenance in Realm-1 only. |
| Rally.cs HandleSiegeRally, TryMarchWithArmy, TryArmyDetour, IssueVariedRvrPath | **Verbatim** in `AutonomousWorldBotController.RealmRally.cs` | Column behind the march leader (`MarchLeader`/`ReportMarchLeading`), regroup holds (45 s, 60 s for a fight behind), push after 4 min stalled, detour 6,000/12,000 around enemy keeps, leader waits up to 12 min at the camp edge for 60 % of the column, then `ReportArmyArrived`. Uses the fork's `TravelToDefensivePost` (friendly doors by the keep rule) and upstream's direct leg (`AutonomousRvrTravel.ChooseWaypoint`, added). |
| Siege.cs TryRunSiegeJob + SiegeDoor, SiegeDoorNumber, PosternLike, InnerDoorApproach, ChooseSiegePosition, LevelGround, SelectSiegeTarget, CarriedSiegeKind, AbandonedSiegeKind, IsSiegeOperatorThreat | **Verbatim** in `AutonomousWorldBotController.RealmSiege.cs` (`ChooseSiegePositionRealm`, `TryRunSiegeJobRealm`) | Synthetic engines on the spot (no shopping), rams at the outer gate then the keep door, posterns last, relic doors 10/11 by number, artillery rings, operators defend only against melee at their height. Shared fork helpers (ReleaseSiegeJob, SupplySiegeItem, TrySiegeRepair, ContinueSiegeMove, SiegeLog) reused. |
| AutonomousSiegeJobs | BOTH (world branch) | RvR: slots 1/3/5/6 at 4/8/24/64 present, attackers ram, ram, trebuchet, catapult, ballistas; defenders never a trebuchet; a carried kit takes its slot first; **one pool per realm** (the fork keyed it by guild, which split the R4a realm guilds). Camlann unchanged. |
| CreateSiegeWeapon, GameSiegeRam, BotSiegeRuntime.CanDamage | BOTH | RvR: heavy ram (reach 500), `Crew` = riders + friendly gamebots at a bot ram, upstream damage **without the fork's 10x test multiplier**; catapults and trebuchets need no line of sight, ballistas a clear shot or a target up on a wall. BotSiegeRuntime otherwise fork (its LegalEnemy is already realm-routed; the rest of the 44-line diff is not RvR-relevant). |
| ExecuteRvr, FindRvrTarget, ChooseRvrDestination, FindInteractableRelic, StageSoloRvrAtBorderKeep, siege back line, ReportSiegeCombatAttendance | **Verbatim** in `AutonomousWorldBotController.RealmRvr.cs` | Siege operators work as soon as the army arrives (before the battle opens), healers stay behind the line and raise, fighting at the keep counts as present, solo bots stage at the nearer border keep. Planner: frontier commit 20 min (+ up to 5), `rvr-roam-` anchors at every keep, clearings, enemy bots, relic carriers; no roam tastes, hotspots, heat, danger memory or revenge trips. Fork kept: `ClearUnrelatedFrontierRequest` and the porter-crossing guard (bug 76), R3b relic temple pads. |
| FrontierThreat.cs | **Verbatim** in `AutonomousWorldBotController.RealmFrontierThreat.cs` | Stalemate drop (90 s, ignored 5 min), unreachable wall target drop (2 min), ranged bots fire back at wall archers, siege posts held; `IgnoresTarget` also gates BotBrain's aggro. Battle regions = frontier + 250-253 (DF/raid regions follow in S3). Fork kept: no attack while the fork's PvP release immunity holds. |
| KeepTravel | BOTH | RvR: stall policy (re-plan after 60 s, three stalls back off 2 min and release the siege job), 4,000-unit legs on 6,000+ legs; no route variant, no mob bend, never abandons a keep route. Stefan's road look-ahead and precise last point stay shared. |
| GameBot, BotBrain | BOTH | RvR: a siege attacker waits 240 s for a raise when any army healer stands within 5,000 (others 90 s, none 20 s), then releases at its army's muster when that point is safe; army healers raise any fallen realm-mate (claims, 250 enemy clearance, quiet 5 s). Danger memory not recorded. |
| Coordinator, controller | BOTH | RvR: `IsEventForce` (muster + march + battle) at the meetup, siege-lock, regroup and assembly checks; solo siege recruits never drafted into random warbands; RvR warband leaders closest to their border keep, realms taking turns (`ClosestToStagingFirst`); the fork's hub muster off; solo forces dropped when the bot leaves RvR or joins a party; no bug-75 assembly guard. |
| Waves (map section 3) | Gated, code kept | Off in Realm-1 for world bots: doctrine (`AutonomousRvrDoctrineRuntime.Applies`, which switches off observe, retreat and group pause, stealth loop, speed habits and sprint, support offense, healer stun, combat habits, group-motion caps), PvP opportunity hunts, LFG wait, hub muster, danger memory, route variants, mob bends, roam tastes/hotspots/heat (planner). |
| Keep levels, RvR level gate | BOTH | RvR: a capture sets upstream's starting_keep_level **4**, a claim **5** (Camlann 1/1 from the save). No RvR task below level 20 (upstream goal table: "Levels 1-19 cannot have RvR goals"); sieges stay level 50 (S1). UT_RvrKeeps' two capture cases now expect 4. |

**Deviations kept in Realm-1 (fork value or fork infrastructure, owner may revisit):** the frontier transport (porters,
bug 76 fixes), the keep approach navigation (`ForBot`, doors by the keep rule), Stefan's movement port (135-139), the
fork's player types choosing tasks (shares differ from upstream's flat 20/40 %), the fork's warband composition
(healer/tank/speed weights, backfill) and remote meetup, the R3b relic pads, the PvP release immunity guard.

**Tests carried (65 new cases):** UT_SiegePlacement, UT_SiegeDoorNumber, UT_AutonomousArmyPush, UT_RvrLeaderStaging (verbatim);
UT_SiegeParties and UT_PhysicalSiegeRally as `UT_RealmRvr_*` against `RealmRvrEventLayer` (upstream's level-50 goal split
assertion dropped, the fork has no such roll); UT_EveningAuditFixes' stall, detour and quorum parts as
`UT_RealmRvr_EveningAuditFixes`. UT_AutonomousSiegeEquipment's upstream cases (heavy ram 500/475, slots 4/8/24) are in
the new UT_RvrPortS2 (33 cases: slots and engines both worlds, carried kit, heavy ram reach, ram damage, waves switch, keep
levels, level-20 gate, facade rosters, Tier 3 audit extension: a source scan that the ported files decide friend or foe
by the realm rule, plus the seam check). The fork's UT_AutonomousSiegeEquipment and UT_PhysicalSiegeRally stay as they are
(Camlann).

**What S3 must do.** Realm raids (AutonomousRealmRaid +421, ForcedRaid +96, NeutralEvents, DungeonRoute + DF proofs) and
then add the neutral raid regions to `IsBattleground` in RealmFrontierThreat; RP rewards (`PayGamebotsForPlayerKill`,
MERGE); battleground goal and brackets (4f; then BattlegroundBrackets replaces the 250-253 checks and the battleground
participants leave the fork's turn if the owner picks upstream); relic pad 8 + companions; town teleporters; route threat
on in RvR; dashboard muster column; upstream's AutonomousGroupPace and the 0.38 portal-keep door fixes. Then R6 live run
with level-50 bots: watch RVR_MARCH_PUSH, RVR_MARCH_DETOUR, RVR_KEEP_TRAVEL_STALL, RVR_SIEGE_PARTY_FORMED,
AUTONOMOUS_SIEGE_RESURRECTION and RVR_SIEGE deployed lines.

## Not verified
- No compile of upstream files against the fork; type differences in Force/LiveObjective/GameBot APIs are estimated.
- Hunk counts in the big shared files are keyword estimates (RvR hunks ~1,150 lines total).
- No live comparison in Realm-1 (0 RvR events, bots <= 19). Upstream seed bot levels: answered in section 6 (upstream ships no bots).
- S2 (section 7): no live run of the march, the siege floor or the raises; the RvR world's existing bots are below 20 and take no RvR task until they level.
- Whether R1b masks enemy bot names in broadcast messages the way upstream `Masked()` does.
- Whether the fork already pays gamebots RPs for player kills and drops no guard loot (AbstractServerRules merge).

## S3 done (2026-10-25, port session S3; implemented in source, real-client check pending)

Worktree branch `rvr-port-s3` (not merged, no version). No live run. Every switch below is RvR-world only
(`WorldMode.IsRvr` / `WorldModelKind.Realm`) unless the row says "both worlds".

| Item | Verbatim / branched | Player sees in Realm-1 | Camlann |
|---|---|---|---|
| Relic pad crowd | Branched. New key `rvr_relic_players_required_on_pad` (default 8, upstream's value) read by the relic pickup and the NF pillar pad through `RvrRelicRules.PlayersRequiredOnPad()`; the fork's pad count already counted players, gamebots, companions and `/spawn` helpers by alliance realm, so upstream's `CountsOnPad` rule needed no change. Relic capture/return/removal broadcasts follow the launcher's RvR announcement switch (upstream `GameWideAnnouncements`). | 8 enemies on the pad lift a relic (was 16); a player with seven companions is enough | `relic_players_required_on_pad` (16), broadcasts unchanged |
| Route threat awareness | Branched. `AutonomousRouteThreatPolicy.Enabled` is now a world-aware property: `rvr_bot_route_threat_awareness` (default true, upstream default) in Realm-1, the old key `bot_route_threat_awareness` (false) in Camlann. Call sites unchanged. A separate key because the RvR save already stores the old key as False. | Travelling PvE leaders pull, avoid or detour aggressive monsters on the way | off, as before |
| RealmRaidRecruitmentPolicy | Upstream functions added verbatim (`DepartHub(bool,int,long)`, `StagingDeadline`, 6-argument `StagingExpired`, `BattleExpired`, `NeedsReplacement`, late-start and hub-patience constants, `UpstreamReady`); the 4-argument `Ready` / `StagingExpired` answer upstream in Realm-1. Carried tests UT_RaidRallyTiming and UT_RaidRefillOct6 verbatim. | see next row | fork timing (45-minute forced preparation, 90-minute limit) |
| Realm raids by realm | Branched in `AutonomousRealmRaid` (`RaidsByRealm`): every expedition belongs to one realm, recruits only that realm, opens only that realm's dragon/epic dungeon, one automatic expedition per realm, never the same automatic event twice in a row (`MayStartAutomatic`); upstream rally timing (hub leaves after 30 min with 120, staging 45 min after leaving, late start at 75 min with 180, forced raids start at 200 staged and have no time limit); "raid is forming" screen line; final-boss announcement; `REALM_RAID_ENDED` log. `RealmRaidRecruitmentPolicy.AutonomousRaidsEnabled` is true in Realm-1 (upstream: level-50 Group PvE parties open and join raids); the fork's raid calendar stays off in both worlds (`ScheduledRaidsEnabled`). | Each realm raids Golestandt / Gjalpinulva / Cuuldurach and Caer Sidi / Tuscaran / Galladoria with its own bots, on its own | mixed-realm expeditions, launcher-forced only, unchanged |
| Battleground goal | Branched, default only. Upstream's "Battlegrounds %" goal is 0 % by default, so no upstream bot tours a bracket battleground unless the owner sets it. Realm-1 therefore stops the fork's automatic bracket campaign (`AutonomousBattlegroundParticipation.BracketCampaignRuns`): no new bracket assignments, bracket participants leave. The open BG crew (Thidranki 252, per realm) is untouched in both worlds. Collision 4f resolved: Thidranki stays the fork's open BG; upstream's Thidranki bracket goal is not active because its share is 0 %. | No bot in Abermenai/Thidranki/Murdaigean/Caledonia except the open BG crew | fork campaign unchanged |
| Town teleporters | Upstream `AutonomousTownTeleporters` and the controller partial `AutonomousWorldBotController.TownTeleport.cs` copied verbatim; `WorldMgr.GetTeleportLocations` and `BotReleaseBindPoints.IsNearBindstone` added verbatim; `world/BattlegroundBrackets.cs` verbatim (porter destinations). Fork: `IsEnabled` only in Realm-1; destinations also pass the fork's realm boundary (`AutonomousRealmBoundary.AllowsPoint`, kept as approved added value). Called from the four travel sites (handoff 1-4, applied). | bots walk to their own realm's town teleporter when that saves a minute and 30 % | off |
| Road fallback (task 143) | Both worlds (pure navigation). `AutonomousZoneRoads` verbatim plus fork safety: data embedded in `GameServer.dll` (`bots/autonomous/data/zone_roads.json`, so every deploy and release carries it; a `navmesh/roads.json` next to the server wins), a `meshes` sha256 map that drops a zone whose installed mesh differs (`ZONE_ROADS_MESH_MISMATCH`), `ZONE_ROADS_LOADED` at start, preload on the server-started event. `Pathfinder.TryRoadPath` + `CalculatePath(useRoads)` verbatim (any `GameBot`, partial path ending > 256 from a goal ≥ 1,024 away, never DF). `AutonomousZoneItinerary.HasDirectCorridor` / `CorridorLength` added; `HasCompleteCorridor` behaviour unchanged. `NavQueryProfile` verbatim (no NpcService hook). Tools `navdiff.py`, `navcomp.py`, `roadgraph.py` verbatim, UT_ZoneRoadBuild + mesh map. Data from **our** meshes: 63 zones of regions 1, 100, 200, 51, 151, 181; 65,208 anchors; 486,975 of 495,152 candidate legs proven (98.3 %); all 63 mesh hashes equal the installed `navmesh/zoneNNN.nav`. | Bots that dead-end on a long walk follow the proven road legs: probe on the installed meshes, Odin's Gate field (18,330 chord) and Emain Macha Albion portal keep approach (24,538 chord) now reach their goals in two path orders (walks 43,045 and 99,841), where the direct search stops after 2 nodes | same (both worlds) |

Tests: UT_RvrPortS3 (6), UT_ZoneRoadFallback (7, fake navmesh), UT_RaidRallyTiming (2), UT_RaidRefillOct6 (7);
explicit tools UT_ZoneRoadBuild and UT_ZoneRoadHotspotProbe (installed meshes, read-only). Server suite 5,016 passed /
7 failed (the bug 160 set, unchanged) / 6 skipped; CoreServer Release build 0 errors. Launcher untouched, not run.

**Handoff hunks 1-5 applied after the S2 merge (0.343.0):** town teleporters are live in Realm-1, and forced and refill raid parties reserve only the raid's realm.

### Not done in S3 (for S4)

| Item | Why | What it needs |
|---|---|---|
| Darkness Falls route (`RealmRaidDungeonRoute` +693, `AutonomousDarknessFallsNavigation` +422, GoalScope, RoutePlan, `AutonomousWorldBotDarknessFallsRoutes`, `DarknessFallsCharmPolicy`, Pathfinder DF certified-corridor gate) and the neutral raids (Summoner's Hall, DF expeditions per realm, `RealmRaidNeutralEvents`, multi-hub musters) | Upstream's 14.8 MB `darkness_falls_navigation_proofs.json` is bound to its DF mesh (`NavSha256` 41f1489c…); ours is `zone249.nav` d53d31cd… patched by the fork to e90e2f7a… (`darkness_falls_navigation_patch.json`). Upstream's own check would reject the proofs, so a verbatim copy does nothing. | Regenerate the certified DF proofs on our patched mesh with upstream's proof tooling, then port the route and the neutral events together. |
| Remaining upstream raid fixes: refill of short parties (`TryRefillRaidParty`, `ReleasePartyMember`, `AddReplacement`), release at the raid hub (`TryRaidRelease`), dungeon regroup point, trash respawn slow-down while a raid is inside, pooled forced parties (`ComposeForcedParty`), failed-post retry and pet-list cache | Call sites in GameBot / BotBrain / coordinator / GameNPC respawn (S2 files) | Port after S2 merges, RvR-branched |
| Upstream opt-in battleground goal (`ExecuteBattleground`, `AutonomousBattlegroundWave`, objective kind `Battleground`, launcher "Battlegrounds %") | The fork's bot goals are a type mix (Leveler … KeepWarrior), not upstream's per-level goal rows; there is nowhere to put a % without a new setting. Default upstream behaviour (0 %) is in. | Owner decision: is a battleground share wanted in Realm-1? If yes, one RvR-only setting (default 0) plus the upstream executor and the dispatch hunk. |
| UT_BattlegroundGoalsOct7, UT_NeutralRaidEventsOct6, UT_EventRouteAudit, upstream UT_RealmRaidRecruitment (269), UT_RelicPadAttackers (40) | Their code is in the rows above (S4) or the fork's pad rule differs in shape (`CountsOnPad` is not a public helper here; covered by UT_RvrPortS3) | carry with the S4 items |
| UT_EnemyBotNamesOct7 parity | R1b names are masked by `RvrServerRules`; broadcast parity not checked | separate check |

### S3 handoff to S2/merge (applied in the S3 branch after merging main 0.343.0)

Apply after S2's controller work; all calls are no-ops outside the RvR world (`AutonomousTownTeleporters.IsEnabled`
is false in Camlann). Anchors are today's `main` (90e523f3) lines of `AutonomousWorldBotController.cs`.

1. `TravelAcrossRegions` (after the "No legal region route" `AbandonCamp` block, before the first
   `if (TryRepairAuditedCrossingSource(bot, crossing))`, line 832):
   ```csharp
            if (TryTownTeleportAcross(bot, crossing, camp.RegionId, camp.X, camp.Y, camp.Z)) return true;
   ```
2. `TravelToTrainerRegion` (before its `if (TryRepairAuditedCrossingSource(bot, crossing))`, line 1143):
   ```csharp
            if (TryTownTeleportAcross(bot, crossing, trainer.CurrentRegionID, trainer.X, trainer.Y, trainer.Z)) return true;
   ```
3. Group rendezvous travel (after the "Replanning group rendezvous" `crossing == null` block, before
   `if (TryRepairAuditedCrossingSource(bot, crossing))`, line 1471):
   ```csharp
            if (TryTownTeleportAcross(bot, crossing, targetRegion, targetX, targetY, bot.Z)) return true;
   ```
4. `TryBeginFasterStableRoute(GameBot bot, Vector3 waypoint, string destinationName)` first statement (line 4520):
   ```csharp
            // Town teleporters first (upstream 0.38c, RvR world): taken only when clearly faster than walking and any
            // horse for the same goal (assembled parties, raid parties and warbands go together via their leader).
            if (TryTownTeleport(bot, bot.CurrentRegionID, waypoint,
                    Vector3.Distance(new(bot.X, bot.Y, bot.Z), waypoint) / Math.Max(150, (int)bot.MaxSpeed)))
                return true;
   ```
5. `AutonomousBotGroupCoordinator.ForcedRaid.cs` (asked S2 whether it owns the file; no answer during S3). Make the two
   existing realm overloads filter by realm in the RvR world; `AutonomousRealmRaid` already calls them there.
   - Rename `PlanWaitingRaidParty(int size)` to `PlanWaitingRaidPartyCore(int size, eRealm realm)`, add
     `(realm == eRealm.None || b.Realm == realm) &&` as the first condition after `AutonomousRealmRaid.IsEligible(b) &&`,
     and add `public static GameBot[] PlanWaitingRaidParty(int size) => PlanWaitingRaidPartyCore(size, eRealm.None);`
   - Rename `PlanForcedRaid()` to `PlanForcedRaidCore(eRealm realm)` with the same extra condition, and add
     `public static GameBot[][] PlanForcedRaid() => PlanForcedRaidCore(eRealm.None);`
   - Replace the two stubs with
   ```csharp
    // RvR world (upstream 0.38c): an expedition recruits only its own realm. Camlann: the realm is the
    // encounter's identity, not a recruitment team.
    public static GameBot[] PlanWaitingRaidParty(eRealm realm, int size) =>
        PlanWaitingRaidPartyCore(size, AutonomousRealmRaid.RaidsByRealm(WorldMode.Current) ? realm : eRealm.None);

    public static GameBot[][] PlanForcedRaid(eRealm realm) =>
        PlanForcedRaidCore(AutonomousRealmRaid.RaidsByRealm(WorldMode.Current) ? realm : eRealm.None);
   ```

## R6 fixes (2026-10-25, review R6; implemented in source, real-client check pending)

Worktree branch `rvr-r6fix` (not merged, no version). Rule: the RvR world (Realm-1) behaves like upstream 0.38c, Camlann
unchanged. Server suite 5,102 passed / 7 failed (the bug 160 set) / 6 skipped; launcher 338 passed / 0 failed / 2 skipped.
No live run.

| Item | Upstream check | Fix | Player sees in Realm-1 |
|---|---|---|---|
| 1. Event layer never expired on the live clock | Upstream 0.38c has the same latent bug: `_lastExpireTick = long.MinValue`, every caller passes `GameLoop.GameLoopTime` (a positive stopwatch clock there, `GameLoopTime += delta` from 0 here), so `nowTick - last` overflows negative and the 500 ms throttle returns on every live call. Upstream's tests step their own ticks and never hit the throttle. | `RealmRvrEventLayer.Expire` treats `long.MinValue` as "never ran" (`Fork:` mark). Tests drive the rally on the live clock: extension, march, battle, repel; rally failure at the one-hour timer; throttle still skips calls within 500 ms. | Musters march at 12 (+5) minutes, sieges are repelled after 10 minutes without attackers, defense musters march, four-hour timers end sieges (live 18:06 siege with no march by 18:26). |
| 2. Fork march restraint active in Realm-1 | Upstream has no such rule (the column fights; a quarter fighting at the rear halts the leader). | `AutonomousSiegeMarch.IsMarching` answers false in Realm-1 (`RestrainsMarch(world)`), which also lifts BotBrain's aggro refusal, the 20 s pursuit drop and the assist/pet-assist filters. Camlann unchanged. | Marching bots fight back and attack enemies they meet on the road. |
| 3. Forced realm raids never ended | Upstream: forced raids have no clock; the launcher's STOP EVENT (`RealmEventControls` action `stop`, `AutonomousRealmRaid.Stop`) ends them, outcome "Stopped by player", no cooldown. | Ported verbatim: `Stop`, `StoppedByPlayer`, no cooldown after a stop, the snapshot publishes -1 / "starts when 200 are staged" for a clockless forced raid, keep events refuse `stop`. Launcher: STOP EVENT… button (dragon and epic dungeon rows that are running, confirmation dialog), "(no time limit)" in the Stage / battle column, "Stopped by player" in the records filter, the forced-start dialog describes the world's rule (RvR: no time limit, STOP EVENT; Camlann: 45/90 minutes as before). The action row wraps only below about 950 px. Stop works in both worlds (an explicit player action; Camlann's timed raids are otherwise unchanged). | A forced expedition can be called off from the launcher; its 300 reserved bots go back to their own goals at once. |
| 4. starting_keep_level | Upstream default 4 (claim 5); Release, Reset and the capture realm points read it. | `PvpKeepCampaign.StartingLevel(world, property)`: Realm-1 4, Camlann the property (1). Used by `AbstractGameKeep.Release`/`Reset`, `AbstractServerRules.GetRealmPointsForKeep` (level - 4) and `/keep fastcreate`. The capture still runs the full level change when the level changed (RvR only). | A guild that releases an RvR keep leaves it at level 4, not 1; capture RPs count levels above 4. |
| 7. Stale keep-start texts | - | World picker prompt and `ScrubPlayerState` comment: frontier keeps start owned by their original realm at their original level, portal and relic keeps with their realm. | The RvR world's create/reset dialog no longer says "Keeps start neutral". |
| 8. Guild keep defense, orphaned comments | Upstream has no guild keep defense. | `AutonomousGuildKeepDefense.Applies(world)`: no alarms and no eligible bot in Realm-1 (BotBrain's call and the coordinator recall are skipped). Doc comments of `BotReleaseBindPoints.IsNearOwnBind` and `WorldMgr.GetTeleportLocation` moved back to their methods. | Bots of a guild are not pulled off their task to defend "their guild's" keep; the realm defense of the event layer answers. |

Tests: UT_RvrR6Fixes (19 cases), UT_RvrKeeps release cases (2), launcher RealmEventsPresentationTests (stop button, timer,
prompt; 10 cases) and a WorldPickerTests assertion.

Not done (S4 / owner decision, unchanged): lord shield, no item loot, AutonomousGroupPace, battleground siege announcements,
dashboard muster column, DF route, opt-in battleground goal, relic temple ownership, frontier porters, task mix, release
immunity. Also not ported: upstream's `SupersedeAutomatic` (a forced expedition ending the realm's unstarted automatic
rallies). Side finding, not fixed (Camlann wave, off in Realm-1): `BotBrain.SupportOffense` starts `_supportLastTurn` at
`long.MinValue`, so the first fight of a bot skips its per-fight roll (same overflow pattern).
