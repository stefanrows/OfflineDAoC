# RvR mode: design specification (Phase 0)

Date: 2026-10-24. Base: main b82a5d0 (0.330.0). Author: Phase 0 spec agent.
Inputs: [RVR_MODE_ANALYSIS.md](../../RVR_MODE_ANALYSIS.md) (sections 1-5 and
"Further owner decisions"), [RVR_MODE_PLAN.md](../../RVR_MODE_PLAN.md) (packages
R0-R6), [CAMLANN.md](../../CAMLANN.md), [TOA_PLAN.md](../../TOA_PLAN.md),
[TOA_DATA_PACKS.md](../../TOA_DATA_PACKS.md), [OPEN_BG_MODES_RULINGS.md](../../OPEN_BG_MODES_RULINGS.md),
TASKS 135-139 and 147, the fork source and the upstream 0.37b source at
`C:\OfflineDAoC-upstream\playable\source`.

Nothing here is implemented. Line references are to main b82a5d0 and drift.

## 1. Goal, non-goals, owner rules

**Goal.** The fork gets a classic realm-vs-realm world as its default world.
Same realm is a friend and other realms are enemies everywhere: in the Old
Frontiers, in Atlantis, in the bracket battlegrounds and in the open BG. Bot
armies muster, march, siege and defend per realm, as close as the fork can get
to the upstream 0.37b behaviour tested on 2026-10-23/24. Every fork feature
stays: ToA, Master Levels, artifacts, companions, the autonomous bots and their
guilds, the open BG, the launcher. The Camlann full-PvP world remains
available as a second world behind a launcher button. Each save declares which
world it is, and the server refuses a save under the wrong rules.

**Non-goals.** No migration of the current Camlann save, its characters,
companions or bots; both worlds start fresh. No New Frontiers, no new maps, no
navmesh rebuild, no native client patch. No dual-mode flag on one save: the
mode belongs to the save, not to a property. No rework of Stefan's movement
port (tasks 135-139); the RvR work builds on it. Majestic Will 1.68 is a
separate package. Public multiplayer stays out of scope.

**Owner rules** (analysis section 5 and "Further owner decisions"; numbering
here is for this spec):

1. The default world is classic RvR. Camlann PvP is a second world the
   launcher starts with a button.
2. Clean start: a fresh save, new characters, new companions. Nothing is
   migrated. The current save may be wiped; backups stay.
3. Target behaviour is the upstream 0.37b RvR, as close as the fork can get,
   with all fork features kept. Patch 1.65 is a reference, mainly for the PvP
   world; the RvR behaviour may be newer (upstream style).
4. Realm rules apply everywhere: frontiers, Atlantis, the bracket
   battlegrounds per level band and the open BG.
5. Companions come from the owner's realm only; `/spawn` offers only it.
6. Bot guilds are single-realm. About 600 world bots per realm.
7. Travel is realm-bound: own capital, own portal keeps, own Atlantis copy and
   havens, own Realm Exchange. Enemy capitals and porters are closed.
8. Keeps start neutral, held by the Frontier Wardens, until a realm captures
   them. A capture stays in the save.
9. Guild claims: a solo player guild may claim as today; bot guilds of the
   keep's realm may claim too.
10. Relics sit on realm pads and give a realm-wide bonus.
11. Portal keeps are safe for their own realm only; their guards attack other
    realms.
12. The PvP button must work: the launcher offers the Camlann world as a
    second, fresh world. The old Camlann save is not kept.
13. Coordination: Stefan ports upstream RvR movement into the fork (tasks
    135-139, 0.311-0.328, merged into main at 4baae43). The RvR mode builds on
    that work and must not rewrite it.

## 2. World model

### 2.1 How a save declares its world

- The existing marker row `offline_local_options.WorldModel` carries the world.
  Values: `Camlann-1` (today) and `Realm-1` (new). No other value starts.
- `CamlannWorldModel` becomes a world-model helper with three answers:
  Camlann, Realm, or unsupported. It is read once at startup, before the
  server rules are created, and exposed as one static "current world" value.
- `ValidateWorldModel` (GameServer.cs:1003) accepts both markers with
  GameType=PvP and refuses everything else with the same error style. A save
  without a marker keeps being refused; only the launcher writes markers.
- The world is fixed for the life of the process. Nothing switches it at run
  time, and no server property can override it.

### 2.2 Files and the launcher

- **Two save files, one active.** The RvR world uses the existing path
  `runtime/data/opendaoc.sqlite3.db`, because it is the default world and the
  deploy, backup and tooling paths already protect that file. The Camlann world
  uses `runtime/data/opendaoc-camlann.sqlite3.db`.
- **Per-world side files.** Files the server writes per save get a Camlann
  twin: `realm-event-records.sqlite3`, `open-bg-status.json` and the Gothwaite
  switch `gothwaite-pvp.json` (Camlann only). The bot settings the launcher
  stores in the save stay in the save.
- **Selection.** Before every start the launcher writes the chosen save into
  the `DBConnectionString` of `serverconfig.xml` and keeps `GameType` at PvP
  (`CamlannServerConfig.EnsurePvP` stays). The server needs no new setting:
  the marker inside the chosen file decides the rules.
- **World picker.** The main window shows the active world ("Realm vs Realm"
  by default) and a "PvP world (Camlann)" button. Switching needs a fully
  stopped server; the launcher refuses while any component of this
  installation runs. The choice is remembered in the launcher settings.
- **Reset per world.** "Create new RvR world" and "Create new PvP world" each
  build a fresh save of that world (section 7). Each takes a full backup of the
  file it replaces first. A missing file means "not created yet", and the
  button offers to create it.
- **Import guard.** `ImportEngine.IsCamlannRuntime` (ImportEngine.cs:76)
  learns `Realm-1` too. Progress import stays disabled for both worlds; the
  message names the world.
- **Tool paths.** `tools/dev/deploy.sh` and the release pack must treat the
  Camlann save and its side files as protected save data, like the main save.

### 2.3 GameType: recommendation

**Recommendation: keep GameType=PvP for both worlds. Select the rules by the
world marker, with an RvR rules class derived from the fork's PvP rules.**

The server creates its rules at GameServer.cs:810, after the marker check at
line 371, so a marker-driven choice is possible. In the RvR world a new
`RvrServerRules` (a subclass of `PvPServerRules`) is created; in the Camlann
world `PvPServerRules` as today. A static "current world" helper serves the
places that are not rule methods.

Consequences of keeping PvP and the marker:

| Area | Effect in the RvR world |
|---|---|
| Client colour handling | `RvrServerRules` returns 0, the standard realm scheme upstream uses with GameType Normal: other-realm players red, own-realm NPCs friendly. Upstream proves that bots shown as NPCs work under 0. Mode 2 (same realm friendly, nearest friend/enemy keys work) is the fallback if the client check of 0 fails. |
| Realm byte | `GetLivingRealm` already returns the viewer's realm for allies and the real realm otherwise. With "same realm = ally" it sends the true realm everywhere; the spoof branch for same-realm hostiles never fires. No new code beyond the alliance rule. |
| Dummy-guild trick | PacketLib1124.cs:307 runs for GST_PvP. In the RvR world it is switched off by the world, as upstream never had it; own-realm bots are friendly through colour handling 0 and the realm byte. Group.cs (pet guild IDs) follows the same switch. |
| Guilds, groups, alliances | `RvrServerRules` refuses cross-realm groups, guild invites, trade and battlegroups, as NormalServerRules does. `AreAllied` additionally requires the same realm for group, guild and battlegroup, so a legacy object can never make an enemy an ally. Guild alliances (KeepManager.cs:609) are realm-only for the same reason. |
| Speech | `IsAllowedToUnderstand` follows NormalServerRules: other realms' speech is garbled, as upstream. |
| Characters per account | `IsAllowedCharsInAllRealms` stays true, so the owner keeps characters in every realm on one account (section 10 default). |
| Keep code | Every keep path stays on the fork's PvP branch (`IsPvpServer`, `PvpKeepCampaign.Applies`), which already knows bots as claimers, garrisons and stewards. The RvR world adds realm ownership on top (section 4). |
| Fork features | The open BG, battleground campaign, ToA hooks, companions, `PvpDangerExperience`, siege-weapon access and the eight `is PvPServerRules` checks keep working, because the RvR class is a PvP class. Each check is classified in R1b (section 8). |
| GST_PvP branches | The 35 `GST_PvP` sites (who, assist, doors, housing, guilds, lords, mission master, world init and others) stay on the PvP path. R1b audits each: where upstream Normal behaviour is the RvR target (for example `/who` showing only the own realm, assist only on own realm), the site asks the world. |

Why not GameType=Normal in the RvR world:

- The fork's NormalServerRules (NormalServerRules.cs:19-84) lack cross-realm
  companions' removal path, guild keeps, the open BG, the battleground
  campaign, bot claimers and the ToA hooks. Every fork feature would need a
  second implementation on the Normal branch, the real "dual mode" cost.
- `KeepManager.IsEnemy` falls back to plain realm comparison off PvP
  (KeepManager.cs:618), which ignores the Frontier Wardens and bot claims, so
  owner rule 8 would break.
- `RelicMgr`, `GameRelicPad`, `GameRelic`, `GameSiegeWeapon` and seven GameBot
  paths branch on the rules type. Normal would silently switch them to upstream
  code that the fork has not maintained since the Camlann conversion.
- Normal refuses characters in several realms per account by default.
- Upside of Normal, and why it does not outweigh: colour handling and the
  dummy-guild trick would be right without work. Both are a few lines in the
  RvR class.

What becomes more expensive later: each new fork feature that touches
hostility, keeps, travel or bots has to be tested in both worlds (section 9).
What stays unproven until the client check: colour handling 0 together with
GameType PvP in the world-init packet (WorldInitRequestHandler branches on
GST_PvP).

## 3. Rules

### 3.1 Friend and enemy

One seam decides: `PvpCombatant.AreAllied`. `IsAllowedToAttack`,
`IsSameRealm`, `GetLivingRealm`, keep hostility and every bot combat policy
already go through it (Tier 3). In the RvR world it answers "same realm"
first; group, guild and battlegroup only count within one realm.

| Pair | Camlann world | RvR world |
|---|---|---|
| Player vs player | Allied only by group, guild, battlegroup | Same realm = friend; other realm = enemy; duels between same-realm players as today |
| Player vs world bot | Same as players | Same realm = friend; other realm = enemy |
| Bot vs bot | Same as players; hub peace for same-realm bots near hubs | Same realm = friend; the hub peace becomes redundant and is skipped |
| Companion (`/spawn`, persistent) | Any realm; fights as its owner's ally | Owner's realm only (owner rule 5); fights as its owner's realm |
| Pets, turrets, summons | Resolve to their owner | Resolve to their owner (unchanged) |
| Encounter escorts | Inherit the sponsor's alliances | Unchanged; the sponsor is realm-bound |
| Keep guards and lords | Hostile unless the keep's guild is yours | Friendly to the keep's realm; Warden keeps hostile to all realms |
| Portal-keep guards | Never hostile (neutral hubs) | Hostile to other realms (owner rule 11) |
| Ordinary realm NPCs (town guards, merchants) | Peaceful to all player-shaped actors | As upstream Normal: friendly to their realm; enemy-realm visitors are rare because travel is closed |
| Same guild or battlegroup across realms | Allowed and allied | Impossible: creating it is refused, and a stale object never allies |
| Group, guild, trade across realms | Allowed | Refused (`RvrServerRules`) |

Low-level protection stays in both worlds: the `/safety` flag under level 10
and the implicit sub-10 rule for autonomous bots.

### 3.2 Safe areas and hubs

- **Capitals, housing, newbie dungeons** (PvpCombatant.cs:20): safe in both
  worlds. In the RvR world no enemy can reach them, so this changes nothing.
- **Portal keeps and border-hub circles** (decision 7 circles, 3,500 units
  plus the two landing circles): in the RvR world they protect only the hub's
  own realm. A defender of that realm inside the circle cannot be attacked; an
  attacker of that realm inside may attack an intruder; guards attack
  intruders. The fork's "attacker in a safe area cannot attack" rule therefore
  becomes "protected for its realm".
- **Release and bind points:** a released player or bot of the RvR world lands
  only at its own realm's points; `IsSafeReleasePoint` and
  `BotReleaseBindPoints` take the realm.
- **Hub peace** (AutonomousHubDeparture): only same-realm bots are in scope,
  and they are allies in the RvR world, so the rule never fires there. It is
  skipped by the world so the diagnostics stay clean.

### 3.3 Travel

| Path | RvR world |
|---|---|
| Capital and portal-keep porters (`AllRealmsTeleporter`) | Serve only their own realm; other realms get a refusal line. The menu lists only the own realm's destinations. |
| Frontier porters | Own realm's passage and landing only (upstream `AutonomousFrontierTransport.Destination`). |
| `/tc` | To the own capital only (as today for the realm of the character). |
| Atlantis | `[Atlantis]`, `[Atlantis Havens]`, `[Atlantis Dungeons]` already lead to the own realm's copy (TOA_TRAVEL.md). Unchanged; enemy copies stay unreachable. |
| Bracket battlegrounds | Each realm enters through its own entrance and lands at its own portal keep in the BG. Level bands and realm-rank caps of the campaign catalog stay. |
| Open BG | Entry from the own realm's porters, landing at the own portal keep (12 Hib, 13 Mid, 14 Alb). |
| Realm Exchange | The local broker of the own capital only. |
| Stable masters, horse routes | Unchanged; they are inside the own realm. |
| Bots | `AutonomousRealmBoundary` returns to the upstream rule: a bot may not enter another realm's homeland zones; frontiers, battlegrounds (by participation) and neutral dungeons stay open. |

### 3.4 Battlegrounds, open BG, Gothwaite

- **Bracket battlegrounds** (Thidranki and the others by level band): realm
  rules. Their central keep is realm-owned in the RvR world (the open BG
  `KeepIsRealmOwned` rule, generalised to every battleground keep). Campaign
  rewards, brackets and the RP caps stay.
- **Open BG:** in the RvR world the friend rule is per realm, always.
  `open_bg_mode` applies only to the Camlann world; in the RvR world the
  server ignores it with one log line at start. The landing peace defaults to
  `realm`. Crews per realm as in the perrealm mode today. Presets that mix
  realms are refused in the RvR world; single-realm presets stay.
- **Gothwaite PvP mode:** off in the RvR world. The server ignores the switch
  file there with one log line (`GOTH_PVP_MODE active=False reason=rvr-world`).
  The launcher hides the Gothwaite controls for the RvR world.

### 3.5 Kills and deaths

Target behaviour is upstream: enemy-realm kills give realm points and the
upstream experience share; same-realm kills cannot happen. The Camlann Tier 6
consequences (XP and RP for every player-shaped kill, con loss on PvP death)
stay Camlann-only. Realm rank, realm abilities and the RR nameplate suffix
stay in both worlds.

## 4. Keeps and relics

### 4.1 Ownership

- **Start:** every claimable Old Frontiers keep and tower starts neutral, held
  by the Frontier Wardens (keep level 1, as Camlann decision 10 1a). Portal
  keeps and relic keeps start with their original realm. Battleground central
  keeps start neutral, battleground portal keeps with their realm.
- **Model:** a keep has a realm and, optionally, a claiming guild of that
  realm. A Warden keep has realm None and the garrison guild.
- **Hostility** (`KeepManager.IsEnemy`, KeepManager.cs:569-618): in the RvR
  world, a Warden keep is an enemy of every realm; a realm-held keep is an
  enemy of the other realms and a friend of its own realm, whether a guild
  claimed it or not. Portal keeps are enemies of the other realms (today they
  are never hostile). Guild alliances play no part.
- **Capture:** when the lord falls, the keep flips to the killer's realm at
  once, as upstream (Normal `ResetKeep`). The guards respawn as that realm's
  guards and the keep stands unclaimed at level 1. The capture is saved and
  never reverts to the Wardens.
- **Claim:** afterwards a guild of the keep's realm may claim at the Keep
  Claim Steward, as today: a ranked member of a solo player guild, or a bot
  guild of that realm (owner rule 9). `CheckForClaim` adds "guild realm equals
  keep realm". A claim gives the guild emblem and runs the keep-level ladder
  (start 1, level 10 after 32 gameplay hours, as Camlann decision 10).
- **Capture reward:** realm points go to the nearby living members of the
  capturing realm's group or battlegroup, not only the claiming guild.

### 4.2 What the Camlann keep campaign code must do differently

| Piece | Camlann today | RvR world |
|---|---|---|
| `PvpKeepCampaign.Applies` | OF and campaign keeps, not portal keeps | Unchanged scope |
| `PvpKeepCampaign.Initialize` | Unowned keep goes to the Wardens | Unowned keep without a realm goes to the Wardens; a realm-held unclaimed keep keeps its realm and gets no garrison |
| `DefeatLord` | Lord down, steward appears, keep waits for a guild claim | Lord down flips the keep's realm to the killer's realm, restores guards for that realm, then places the steward for an optional guild claim |
| `CompleteClaim` | Guild of the claimer | Guild of the claimer, only if of the keep's realm |
| `IsEnemy` | Guild-based; portal keeps never hostile | Realm-based as above |
| `PVP_UNCLAIMED_KEEPS_ENEMY` | Decides unowned keeps | Not consulted in the RvR world |
| `KeepIsRealmOwned` (open BG perrealm) | Region 252 only | True for every keep in the RvR world |
| `ResetKeep` (PvPServerRules.cs:490) | Campaign keeps go through `DefeatLord` | Same, with the realm flip inside `DefeatLord` |
| Warden start level | Level 1 at every start | Level 1 for Warden keeps; realm-held unclaimed keeps also level 1 (section 10 default) |

### 4.3 Relics

- Relics live on the realm pads in the relic keeps, as upstream: each realm
  owns its two relics at start (Realm = OriginalRealm).
- **Bonus:** realm-wide. `RelicMgr.GetRelicBonusModifier` counts the relics of
  that type mounted on the living's realm's pads (the upstream code path at
  upstream RelicMgr.cs:208-215), with `RELIC_OWNING_BONUS` per relic.
- **Pickup and mount:** the Normal branch of `CanPickupRelicFromShrine`
  (RelicMgr.cs:234) and the realm path of `GameRelicPad` apply in the RvR
  world; the guild keep pad (`GameKeepRelicPad` guild mount) is Camlann-only.
- **Relic guards** (`RelicGuardsOnKeepTaken`): realm logic as upstream.
- **Relic sieges by bots:** upstream runs them (relic muster 25 minutes, march
  at 170). The fork keeps them off today (Camlann decision 10, 4a). In the RvR
  world they follow upstream (section 10 default).

## 5. Bots in RvR

### 5.1 Guilds and population

- **Roster:** about 600 world bots per realm, created by the launcher when the
  world is created (section 7). The level shape is the launcher's existing
  world shape.
- **Guilds:** the server's crew manager (AutonomousCrewManager) creates the
  bot guilds at startup, as today. In the RvR world it plans per realm: up to
  five size triplets (1:2:4) per realm, so 15 guilds per realm and 45 in
  total, each single-realm. `PlanAssignments` only offers a bot the guilds of
  its realm. Names come from per-realm charter lists so a guild name reads as
  Albion, Midgard or Hibernia.
- **Login balance:** `AutonomousRealmLoginBalancer` (the file still exists,
  unused) picks the realm with the fewest active and pending bots; the crew
  login balancer then picks within that realm by cohort and role.
- **Companions:** created only from the owner's realm. `/spawn` and the
  Companion Manager list only that realm.

### 5.2 The autonomous RvR layer per realm

The fork's event layer is already realm-shaped: an event has an attacking,
a defending and a third realm (AutonomousRvrEventLayer.cs:307-338). What makes
it Camlann is that armies and siege slots are keyed by guild
(`GuildArmy`, AutonomousRvrEventLayer.GuildArmy.cs:30; `SiegeSideKey`). In the
RvR world:

- **Army unit:** one army per attacking realm and event (`RealmArmy`), built
  from the realm's warbands of any guild. Guild warbands are its parties. The
  Camlann world keeps `GuildArmy`.
- **Siege slots:** the siege side key is the realm. Upstream rule: one attack
  per realm at a time, server-wide chance scheduling (section 5.3).
- **Muster:** at the realm's own portal outpost of the target frontier (the
  frontier porter's arrival point), else at its border keep, as upstream
  `TryGetSiegeMuster`. Each realm's second border keep (Snowdonia Fortress and
  its counterparts) is offered as in upstream.
- **Staging and march:** the army marches when it can start the battle on
  arrival (upstream threshold 108 for a keep, 170 for a relic keep, or 85% of
  the assigned when fewer), or at the muster deadline (12 + 5 minutes; relic
  25 + up to 6 x 10). On arrival it attacks once 60% of the marched army is at
  its posts.
- **Defence:** the whole defending realm answers, not only the claiming
  guild. `AutonomousGuildKeepDefense` recall becomes a realm recall in the RvR
  world. Upstream defence muster: 6 minutes plus one 4-minute extension, then
  in together at 80%.
- **Third realm:** intervenes in about half of the sieges, five minutes after
  the battle starts, with at most half the attacker cap.
- **Retreat:** a siege counts as repelled after the attackers have been away
  for 10 minutes; a stalled column pushes on (upstream `AutonomousArmyPush`).
- **Raids:** realm raids form battlegroups within one realm
  (`RealmRaidBattleGroup` already per raid); open BG raids per realm.
- **Grudges and banter:** grudges stay keyed by guild; since only enemy
  realms can be fought, they point at enemy realms by themselves. Realm event
  notices and banter address the realm audience, as upstream.

### 5.3 Upstream mechanism, fork counterpart, gap

| Upstream 0.37b mechanism | Fork counterpart (main b82a5d0) | Gap for the RvR world |
|---|---|---|
| Realm event with attacker, defender, third realm; 4 h lifetime; caps 128 keep / 240 relic | Same event shape; caps 128 / 192 | Raise the relic cap to 240 in the RvR world |
| Director: defend own keep under attack, assault a beatable enemy keep, hunt (`AutonomousRvrDirector`) | Same file and rules | Warden keeps (realm None) must count as attackable for every realm; upstream skips realm None outside battlegrounds |
| Home muster at the realm's portal outpost (`AutonomousRvrStaging.TryGetSiegeMuster`), second border keeps | Guild army camp 9,000 units from the keep, gather 2-15 minutes (`AutonomousGuildAssault`) | Port the realm muster; the guild camp stays for Camlann |
| March threshold 108 / 170, arrival quorum 60% | Guild army minimum 8, party quorum 6 | Port for `RealmArmy` |
| Siege start chance per minute (none running: 25% rising; one: 4%; two: 1.2%) | Two sieges per guild, server cap 6 (SiegeCap.cs) | Realm side key plus the upstream chance schedule in the RvR world |
| Defence muster 6 + 4 minutes; player defence 10 minutes; repel after 10 minutes | Defense.cs, guild keep defence recall | Realm recall, upstream muster timings |
| Retake: the losing realm makes one counterattack after a player capture | Missing | Port |
| Third realm 50% after 5 minutes, half cap | Third realm only as a whole one-guild warband | Realm-wide third-realm response |
| `AutonomousArmyPush` (stall 2,000 units in 4 minutes, push 2 minutes) | Missing | Port |
| `AutonomousArmyDetour` around enemy keeps | Missing | Port |
| `AutonomousSiegeResurrection` (any healer of the realm raises) | Missing | Port |
| Siege parties and ram placement (`SiegeParties`, `SiegePlacement`) | `AutonomousSiegeJobs`, `BotSiegeRuntime` | Compare in R4c; port only proven improvements |
| `AutonomousBattlegroundWave` (one realm wave into a BG) | Fork battleground campaign director | Keep the fork's; no port |
| Realm login balancer | File present, unused; crew balancer active | Use it in the RvR world |
| Realm boundary (no enemy homelands) | Open travel (battleground check only) | Upstream rule in the RvR world |
| Bot guilds are display-only, bots never claim | Real crew guilds that claim keeps | Fork feature kept (owner rule 9), per realm |
| Realm raids incl. neutral DF / Summoner's Hall expeditions (`RealmRaidNeutralEvents`) | Realm raids without the neutral events | Optional later port |
| Movement: retarget while moving, leader succession, formation follow, goal fixes | Ported by Stefan (tasks 135-138, 139 step 1 and goal fixes) | Road-graph fallback (139 step 2) still open; not part of the RvR mode |

## 6. ToA under RvR

- **Atlantis is already realm-separated:** each realm travels to its own copy
  (ruins cities 70/71/72, havens and dungeons per copy). No change.
- **Arbiter, Demyphon and the scholars** exist per copy; in the RvR world they
  serve their copy's realm. A character of another realm cannot reach them.
  No code change beyond the travel lock.
- **Enrolment and encounter credit:** the Arbiter enrols the group (level 40+,
  within 2,000 units) and encounter credit goes to the group or battlegroup.
  Groups and battlegroups are realm-only in the RvR world, so both are
  realm-only without code change.
- **Unchanged:** Master Level XP (mob level 45+ anywhere), artifact XP, ToA
  loot in Atlantis, the ToA-era realm abilities (pack 080), the launcher's ToA
  block.
- **Pack 025** (the three Atlas fight-club NPCs removed) matters in both worlds
  and stays.

## 7. Data

### 7.1 What a fresh world writes

Both worlds are built by one launcher reset routine with a world parameter.
It starts from the installed save (world definitions, item templates, spawns,
applied data packs) and runs the Camlann reset steps (`CamlannWorldReset`):
characters, bots, guilds, accounts except the owner's, inventories, Realm
Exchange, event records, keep claims and relics cleared, with a full backup
first. Then:

| Item | RvR world | Camlann world |
|---|---|---|
| Marker | `WorldModel = Realm-1` | `WorldModel = Camlann-1` |
| Ordinary OF keeps and towers | Realm 0, no guild (the server hands them to the Wardens at start) | Realm 0, no guild (as today) |
| Portal keeps, relic keeps | Realm = original realm | Realm 0 (as today) |
| Battleground keeps | Central keeps 0; BG portal keeps original realm | As today |
| Relics | On their home pads, Realm = original realm | Homed as today |
| Bot roster | About 600 per realm, created through the launcher's bot generator | The launcher's current default |
| Bot guilds | None written; the server's crew manager creates 15 per realm at first start | None written; crew manager as today |
| Server properties | None written | None written |

### 7.2 What stays in code, and why no data pack

- **World-dependent defaults live in code**, read through the world helper:
  open BG friend rule, landing peace default, Gothwaite off, colour handling,
  relic sieges, siege scheduling. Writing property rows would leave stale
  values behind when a save is copied or a default changes.
- **No RvR data pack.** Data packs are world data shared by every save, and
  they must not touch guilds or characters (TOA_DATA_PACKS.md, "Rules for pack
  content"). Keep ownership at reset is save state, so the reset routine
  writes it. Both worlds keep receiving the same packs.
- A reset creates a save that already carries the applied pack markers,
  because it starts from the installed save; no pack runs twice.

## 8. Implementation plan

Packages refine R1-R6 of RVR_MODE_PLAN.md. Each is about one session for one
developer plus one reviewer, in its own worktree from fresh main. Every
package ends with a version bump per AGENTS.md (MINOR for gameplay or
launcher behaviour, the world reset is a new save, still 0.x).

**Mapping onto RVR_MODE_PLAN.md:** R1 = R1a + R1b; R2 = R2; R3 = R3a + R3b;
R4 = R4a + R4b + R4c; R5 = R5a + R5b; R6 = R6. Each sub-package is one
developer agent plus one reviewer agent in its own worktree, merged before the
next one starts.

**Order for the night run:** R1a, R1b, R2, R3a, R3b, R5a, R5b, R4a, R4b, R4c,
R6. The PvP button (owner rule 12) is complete after R5b, the seventh package:
from then on the launcher offers the fresh RvR world by default and a fresh
Camlann world by button, both working, even if the bot packages slip to a
later session. If time runs short, R5a and R5b may move directly after R1a
(they need only the marker); the RvR world then boots with Camlann-like keeps
and bots until R1b-R3b land.

The rules, keep and launcher packages do not touch Stefan's movement files
(`AutonomousBotGroupCoordinator`, `NpcMovementComponent`,
`AutonomousGroupMotion`, `AutonomousGroupFormationFollow`,
`AutonomousRouteGoalFix`, `AutonomousKeepGateGoals`, `AutonomousRvrTravel`).
The bot packages R4b and R4c touch the event layer and siege files Stefan
changed in 0.328.0; they start from a fresh fetch of `origin/main` and
`origin/feat/rvr-movement-flow` (0 commits ahead of main on 2026-10-24) and
stop if his branch has moved, rather than rewriting his work.

| Package | Content | Main files | Tests | Acceptance |
|---|---|---|---|---|
| **R1a (R1) World marker and rules class** (1) | `Realm-1` marker; world helper; startup accepts both; `RvrServerRules` chosen by marker; colour handling 0; group, guild, trade, battlegroup and speech realm-only; chars in all realms kept | world/CamlannWorldModel.cs, GameServer.cs:371 and :810, serverrules/PvPServerRules.cs, new serverrules/RvrServerRules.cs, gameutils/ScriptMgr.cs | UT_CamlannWorldModel for both markers and refusals; rules-class selection; per-world colour handling | A `Realm-1` test save boots; a `Camlann-1` save behaves exactly as before; an unknown marker is refused |
| **R1b (R1) Alliance core and client** (1) | `AreAllied` same realm in RvR, group/guild/battlegroup within a realm only, companions as owner's realm; safe hubs per realm; hub peace skipped; dummy-guild trick and Group pet IDs off in RvR; audit of the 35 GST_PvP sites and 8 rules-type checks, each tagged keep / per world | serverrules/PvpCombatant.cs, PvPServerRules.cs, packets/Server/PacketLib1124.cs, gameutils/Group.cs, AutonomousHubDeparture.cs, plus the audit sites | Both-worlds alliance matrix (section 9); Tier 3 audit extended | Every pair of section 3.1 answers as the table says in both worlds; a list of the audited sites in the package notes |
| **R2 (R2) Travel and population rules** (1) | Porters and menus per realm, frontier passage per realm, `/tc`, Realm Exchange, BG and open BG entry per realm; bot realm boundary; `/spawn` and Companion Manager own realm; open BG rule per realm forced; Gothwaite off | AllRealmsTeleporter.cs, AutonomousFrontierTransport.cs, AutonomousRealmBoundary.cs, open BG mode and roster, GothwaitePvpMode.cs, companion spawn command | Travel menu per world; boundary rule; spawn realm filter; open BG rule ignored in RvR | An Albion character sees only Albion destinations; the Camlann world keeps "every porter for everyone" |
| **R3a (R3) Keeps** (1) | Realm ownership on top of the campaign: hostility by realm, portal keeps hostile to other realms, realm flip on lord kill, claims by the keep's realm, capture reward per realm, Wardens only for realm-less keeps | keeps/KeepManager.cs, keeps/PvpKeepCampaign.cs, keeps/AbstractGameKeep.cs, PvPServerRules.ResetKeep, keep guard code | Keep matrix per world; claim refusal for another realm; flip on lord kill; restart keeps the capture | A Warden keep taken by Midgard becomes Midgard's and stays so after a restart; a Hibernian guild cannot claim it |
| **R3b (R3) Relics** (1) | Realm pads, realm-wide bonus, pickup and mount by realm, relic guards by realm; relic siege switch by world | keeps/Managers/RelicMgr.cs, keeps/Relics/GameRelic.cs, GameRelicPad.cs, events/keep/RelicGuardsOnKeepTaken.cs | Relic bonus per world; pickup rules; Camlann guild relics unchanged | A captured relic on the capturer's pad raises that whole realm's bonus |
| **R5a (R5) World reset and files** (1) | Reset routine with a world parameter; keep and relic start state per section 7; per-world side files; roster creation of about 600 per realm; deploy and release protect the Camlann save | CamlannWorldReset.cs, MainForm.cs bot generator, tools/dev/deploy.sh, release pack script | CamlannWorldResetTests for both worlds (marker, keeps, relics, backup, no partial reset) | A fresh RvR save and a fresh Camlann save are produced from the installed save, each with a backup |
| **R5b (R5) Launcher world picker** (1) | Active world display, "PvP world" button, switch only when stopped, connection string writer, reset per world, import guard for both markers, Gothwaite and open BG controls per world | MainForm.cs, CamlannServerConfig.cs, ImportEngine.cs, OpenBgSettingsControl.cs | Launcher tests for the picker, config writer, import guard, version pins | The owner starts the RvR world by default and the Camlann world with the button; both start and stop from the launcher |
| **R4a (R4) Bot guilds and login per realm** (1) | Crew manager plans 15 guilds per realm, single-realm assignment, realm charter names; realm login balancer in front of the crew balancer | AutonomousCrewManager.cs, AutonomousPopulationController.cs, AutonomousRealmLoginBalancer.cs | Assignment never mixes realms; guild count per realm; login order balances realms | A fresh RvR world shows 45 single-realm bot guilds and an even realm login ramp |
| **R4b (R4) Realm armies** (2) | `RealmArmy` in the RvR world; realm siege side key; realm muster at the portal outpost and border keeps; realm defence recall; realm-wide third realm; relic cap 240 | AutonomousRvrEventLayer*.cs, AutonomousRvrStaging.cs, AutonomousRvrSiegeMuster.cs, AutonomousGuildKeepDefense.cs | Event-layer unit tests per world (army keying, side key, muster point, defence recall) | Live log at 10x shows `RVR_*` muster, march and siege lines per realm; no army mixes realms |
| **R4c (R4) Upstream siege flow** (2) | March threshold and arrival quorum, siege start chance schedule, defence muster timings, retake, army push, army detour, siege resurrection; compare siege parties and ram placement | Event layer, new ports of AutonomousArmyPush.cs, AutonomousArmyDetour.cs, AutonomousSiegeResurrection.cs | Ported upstream unit tests, adapted; both worlds | A two-hour 10x run shows keep sieges reaching the lord and at least one capture per realm side, compared with the upstream comparison log |
| **R6 (R6) Verification and docs** (1) | Both-worlds regression suite complete, load test with about 1,800 bots, log review, owner checklist handed over, CAMLANN.md contract amended ("two worlds, one build"), PLAY.md world picker, TASKS 147 to pending | Tests, docs | Full server and launcher suites | Reports offline checks and the open real-client checks separately |

**Effort total: about 12 sessions** (R1a 1, R1b 1, R2 1, R3a 1, R3b 1, R5a 1,
R5b 1, R4a 1, R4b 2, R4c 2, R6 1), plus this spec. The analysis estimated 10;
the difference is the upstream siege-flow port (R4c), which the owner's
"as close as upstream" target adds.

## 9. Tests and verification

### 9.1 Unit tests per package

Listed in the table of section 8. Every new world-dependent rule gets a pure
function with a world parameter, tested without a running server, plus one
integration test through the server rules where a fixture exists.

### 9.2 Both-worlds regression suite

- A test fixture runs the same cases under the Camlann and the RvR world.
  Cases: the section 3.1 matrix through `AreAllied`, `IsAllowedToAttack`,
  `IsSameRealm` and `GetLivingRealm`; keep hostility for Warden, realm-held,
  guild-claimed and portal keeps; relic bonus; travel menus; colour handling;
  hub safety; open BG friend rule; Gothwaite activation.
- The about 21 test files that construct `NormalServerRules` and the files
  that encode Camlann hostility are reviewed: Camlann expectations stay and
  get an RvR twin where the answer differs.
- The full server and launcher suites run in a separate output tree after
  every package; failures already tracked in bug 160 are reported, not hidden.

### 9.3 Tier 3 alliance test adjustment

- `UT_CamlannTier3HostilityAudit` checks that the combat policy seams take an
  explicit enemy signal. It stays as it is: the seams already get that signal
  from the server rules, which now know the world. Add one case per seam fed
  with the RvR answer for a same-realm stranger (not an enemy).
- `UT_CamlannPvpCombatant` encodes "same-realm stranger: enemy" and
  "mixed-realm group member: friend". Both stay for the Camlann world; the RvR
  twins assert "same-realm stranger: friend" and "a cross-realm group cannot
  form, and a stale one is not allied".
- The CAMLANN.md gate ("no bot combat filter uses realm inequality as the
  definition of enemy") stays true: the realm rule lives in the one alliance
  seam, never in a bot filter.

### 9.4 Owner's real-client checklist

1. RvR world: own-realm players and bots show friendly, other realms red;
   nearest-enemy and nearest-friend keys pick correctly; enemy bots can be
   targeted and attacked, own-realm bots cannot.
2. An own-realm bot's pet and an own companion are friendly; the companion
   menu offers only the own realm.
3. Porters show only own-realm destinations; an enemy portal keep's guards
   attack; the own portal keep is safe.
4. A Warden keep can be taken; it turns to the own realm, its guards turn
   friendly, and the own guild can claim it at the steward.
5. A relic on the own pad shows the realm bonus.
6. Bots muster at the own portal outpost, march as a realm army, and siege.
7. Atlantis: own ruins city, Arbiter enrolment, one encounter credit.
8. Open BG: same realm friendly, landing peace per realm.
9. PvP button: the Camlann world starts fresh and behaves as before (all
   players red, open travel, guild keeps).
10. Switching back: the RvR world loads with its characters intact.

## 10. Open questions for the owner

Settled by this spec as defaults, which the owner may override without
changing the plan: relic sieges by bots on, as upstream (section 4.3); a
realm-held keep without a guild claim stays at level 1 (section 4.2); open BG
portal-keep guards hostile to other realms, one rule everywhere (owner rule
11); death penalty as upstream, no Camlann con loss on an enemy-realm death
(section 3.5); characters of every realm on one account, as the fork allows
today (section 2.3).

Still open:

1. **Siege-flow port (R4c): Stefan or the night run?** Stefan's 0.328.0
   changed the same event layer. Recommendation: the night run does R4a and
   R4b; R4c waits for Stefan's agreement.
