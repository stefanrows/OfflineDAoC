# RVR_PORT_MAP: upstream RvR bot layer vs fork (for R4c)

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

| Session | Content | Upstream tests to carry | Size |
|---|---|---|---|
| S1 core | Keep-start reset (after owner yes), facade + rename, RealmRvrEventLayer + Defense, RealmEvent*, GameWideAnnouncements, KeepPolicy RvR branch, new properties | UT_AutonomousRvrEventLayer (159), UT_StagedSiegeOct6 (64), UT_SiegeSchedulingOct7 (22), UT_SiegeMusterAndRankOct6 (38), UT_RaidRallyTiming (34), UT_RaidRefillOct6 (20), UT_PlayerKeepDefense (138), UT_RealmEventPolicy (111), UT_RealmEventRecords (58), UT_RvrExpansion (416); RvrEventTestState split per world | ~2,000 copied, ~400 hand |
| S2 march + siege floor | Rally column + march leader (reuse Stefan's `ChooseMainBodyLeader`), ArmyPush/Detour, Staging muster, Siege + SiegePlacement, SiegeResurrection + GameBot/BotBrain hooks, SiegeParties + coordinator hunks, KeepTravelStallPolicy, FrontierThreat, ForcedRaid, RP rewards; RvR guards on fork waves; friend/foe sites (4b) | UT_PhysicalSiegeRally (176), UT_SiegePlacement (82), UT_SiegeParties (41), UT_SiegeDoorNumber (18), UT_AutonomousArmyPush (36), UT_RvrLeaderStaging (53), UT_AutonomousSiegeEquipment (218), UT_EveningAuditFixes (detour/wave parts), Tier 3 audit extension | ~1,500 copied, ~800 hand |
| S3 periphery | Relic pad 8 + companions, battleground goal (after 4f), town teleporters, realm raids (RealmRaid +421, NeutralEvents, DungeonRoute + DF proofs), route threat on in RvR, road fallback (task 143) | UT_BattlegroundGoalsOct7 (89), UT_NeutralRaidEventsOct6 (113), UT_RelicPadAttackers (40), UT_EventRouteAudit (160), UT_RealmRaidRecruitment (269), UT_EnemyBotNamesOct7 (parity) | ~1,800 copied (DF data excluded), may spill into a 4th session |

Already covered by Stefan (do not redo): task 135 retarget while moving (= upstream "march without stopping at each
waypoint" for keep-travel/group legs); 136 main-body leader for group sessions (upstream applies the same rule to the
army march leader -> reuse it); 137/138 formation follow + GroupMotion (fork versions are supersets of upstream's
AutonomousGroupFormationFollow/GroupMotion: 353/255 vs 230/77 lines); 139 hotspot evidence + goal fixes (fork-only, keep);
Stage 6 route threats (ported, default off). Not covered: army column (Rally.cs), ArmyPush/Detour, 0.38 portal-keep door
fixes, AutonomousZoneRoads/Seams/Pockets and ZoneItinerary +620 (task 143 still open), AutonomousGroupPace.

## Not verified
- No compile of upstream files against the fork; type differences in Force/LiveObjective/GameBot APIs are estimated.
- Hunk counts in the big shared files are keyword estimates (RvR hunks ~1,150 lines total).
- No live comparison in Realm-1 (0 RvR events, bots <= 19). Upstream seed bot levels unknown.
- Whether R1b masks enemy bot names in broadcast messages the way upstream `Masked()` does.
- Whether the fork already pays gamebots RPs for player kills and drops no guard loot (AbstractServerRules merge).
