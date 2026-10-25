# RvR mode: rules audit (package R1b)

Date: 2026-10-25. Base: main 4220051 (0.332.0, R1a). Package R1b of
[RVR_MODE_PLAN.md](RVR_MODE_PLAN.md); spec
`docs/superpowers/specs/2026-10-24-rvr-mode-design.md` sections 2.3, 3 and 8.

Every server-type branch in `source/server/GameServer` was read and given one
decision for the RvR world (save marker `Realm-1`):

- **Unchanged:** world-independent, or already realm-correct because it goes
  through the server rules or `PvpCombatant.AreAllied`.
- **Gated:** the RvR world takes the realm (upstream Normal) behaviour. Gated
  sites call `WorldMode.RulesBranch(serverType)`, which answers `GST_Normal`
  for GameType=PvP in the RvR world and the unchanged type everywhere else,
  or `WorldMode.IsRvr`.
- **Deferred:** needs a later package; the note says which.

The Camlann world answers exactly as before at every site.
Line numbers are those of the R1b branch and drift.

## Counts

| Group | Sites | Unchanged | Gated | Deferred |
|---|---|---|---|---|
| `GST_PvP` branches (the spec's 35) | 35 | 15 | 19 | 1 |
| Rules-type checks (`is PvPServerRules`) | 14 | 10 | 1 | 3 |
| Other server-type branches with realm meaning (`GST_Normal`) | 5 | 0 | 4 | 1 |
| PvE-only branches (`GST_PvE`) | 10 | 10 | 0 | 0 |

The spec counted 8 rules-type checks; 14 exist. The six extra ones are
GameBot immunity timers (`GameBot.cs:3724`, `:3752`, `:3768`), siege-weapon
access and the relic examine text.

## GST_PvP branches

| # | Site | What it decides | RvR decision |
|---|---|---|---|
| 1 | `GameServer.cs:1017` | Refuses GameType other than PvP | Unchanged: both worlds run PvP |
| 2-5 | `GameServerConfiguration.cs:176, 182, 306, 382` | Config parse and default | Unchanged |
| 6 | `battlegrounds/open/OpenBattlegroundRoster.cs:1294` | Guild that owns the open BG keep, for crew splits | Deferred to R4b (realm armies); in RvR the keep's friends are a realm |
| 7 | `commands/playercommands/assist.cs:38` | `/assist` by name also finds bots | Unchanged: checks `IsSameRealm`, realm based in RvR |
| 8-9 | `assist.cs:86, :160` | PvP assist path | Unchanged: `HandlePvpAssist` uses `IsSameRealm` |
| 10 | `commands/playercommands/level.cs:12` | `/level` disabled | Unchanged: fork rule for both worlds; owner decision if RvR wants `/level` |
| 11-14 | `commands/playercommands/who.cs:258, 327, 368, 433` | Zone in `/who` lines, filter syntax | Gated: zone shown ("the Frontiers" in OF) and level filters, as Normal; the list already shows only the own realm through `IsSameRealm` |
| 15 | `gameobjects/GameDoor.cs:41` | Border-keep door interaction | Gated: own realm's doors only |
| 16 | `gameobjects/GamePlayer.cs:5946` | Death type after a PvP kill | Gated: RvR death as Normal, no PvP constitution loss (spec 3.5) |
| 17 | `gameobjects/GamePlayer.cs:10752` | Examine text | Gated: "member of your realm" or "enemy realm" text |
| 18-22 | `gameutils/Group.cs:149, 195, 244, 251, 307` | Dummy-guild TAB mask for pets and bots | Gated in R1a (`WorldMode.UsesGuildIdMask`) |
| 23 | `gameutils/Guild.cs:337` | Creates the dummy guild | Unchanged: harmless; the mask is off in RvR, so the dummy guild is never sent |
| 24 | `gameutils/Guild.cs:702` | Cross-realm guild member add | Gated: a new RvR check above it refuses another realm (`RvrServerRules.GuildAcceptsRealm`), whatever `allow_cross_realm_guilds` says |
| 25 | `housing/HouseTemplateMgr.cs:95` | Housing NPC shop list | Gated: own realm list, as Normal |
| 26 | `keeps/KeepManager.cs:569` | Keep hostility on PvP | Gated: RvR branch inside `IsEnemy` (see below); portal keeps unchanged until R3a |
| 27 | `keeps/Gameobjects/Guards/Lord.cs:376` | Respawn of a lord without a keep component | Unchanged: no such lords in the frontiers |
| 28 | `keeps/Gameobjects/Guards/MissionMaster.cs:287` | Respawn of a realm-less mission master | Unchanged |
| 29 | `keeps/Managers/Player Manager.cs:57` | Capture broadcast | Unchanged: dead code inside a comment |
| 30 | `packets/Client/168/DoorRequestHandler.cs:101` | Door use | Gated: own realm's doors (and `eRealm.Door`) only |
| 31 | `packets/Client/168/WorldInitRequestHandler.cs:144` | Dummy guild for a guildless viewer | Gated in R1a |
| 32 | `packets/Server/PacketLib1124.cs:315` | TAB mask for allied bots and pets | Gated in R1a |
| 33 | `serverrules/AbstractServerRules.cs:2418` | Bounty points and money for player kills | Gated: paid as Normal |
| 34 | `serverrules/PvPServerRules.cs:15` | Rules attribute | Unchanged: the RvR class is chosen by the marker |
| 35 | `world/CamlannWorldModel.cs:41` | Marker parse | Unchanged: world infrastructure |

## Rules-type checks

| Site | What it decides | RvR decision |
|---|---|---|
| `bots/GameBot.cs:1252, 1357, 1404, 3724, 3752, 3768` | PvP immunity after release, rez, login and zoning | Unchanged |
| `gameobjects/GamePlayer.cs:4300` | Danger XP bonus in OF and DF | Unchanged: fork feature for both worlds |
| `gameobjects/GamePlayer.cs:5737` | Who counts in the XP denominator | Unchanged: uses `AreAllied`, realm based in RvR |
| `gameobjects/GameSiegeWeapon.cs:157` | Siege-weapon access | Gated inside `SiegeWeaponAccess.MayUse`: in RvR the founder-guild fallback is off, so only allies of the live anchors or the weapon's realm may use it |
| `keeps/PvpKeepCampaign.cs:15` | Camlann keep campaign (Wardens, steward claims) | Unchanged: RvR keeps the campaign; realm ownership on top is R3a |
| `keeps/Managers/RelicMgr.cs:234` | Relic pickup | Deferred to R3b |
| `events/keep/RelicGuardsOnKeepTaken.cs:33` | Relic guards off | Deferred to R3b |
| `keeps/Relics/GameRelic.cs:205` | Relic examine text | Deferred to R3b |
| `serverrules/PvpDangerExperience.cs:6` | Danger XP multiplier | Unchanged |

## Other server-type branches

| Site | What it decides | RvR decision |
|---|---|---|
| `scripts/gameevents/PlayerEnterExit.cs:58, 103` | Realm tag on login and logout lines | Gated |
| `realmabilities_atlasOF/handlers/AtlasOF_Volley.cs:150` | No Volley inside enemy border keeps | Gated |
| `serverrules/AbstractServerRules.cs:2557` | Realm outpost bonus on kills near an own keep | Gated |
| `serverrules/DFEnterJumpPoint.cs:22` | Darkness Falls for the realm with most keeps | Deferred to R3a: needs realm keep ownership |
| PvE-only: `ai/brain/Guards/Lord.cs:42`, `GamePlayer.cs:12102`, `GameKeepComponent.cs:217`, `GameKeepGuard.cs:916`, `MissionMaster.cs:286`, `Lord.cs:32, 58, 375`, `GatewayPersonalBind.cs:29`, `PvEServerRules.cs:9` | PvE server behaviour | Unchanged: no meaning on GameType=PvP |

## Alliance core (sites outside the server-type branches)

| Site | RvR world |
|---|---|
| `PvpCombatant.AreAllied` | Same realm allied everywhere, another realm never. Group, guild, battlegroup, open BG rule and companion memory do not bridge realms. Two realm-less actors keep their Camlann ties. |
| `PvpCombatant.AllianceRealm` (new) | Pets take the owner's realm. Companions and `/spawn` helpers take the owner's realm. A battleground encounter bot takes its sponsor's realm, else its real guild's realm, else none. |
| `PvPServerRules.IsAllowedToAttack` | Realm NPCs (town guards, unflagged merchants) are friendly to their own realm only and attack enemy realms, as Normal. Allied refusal text: "You can't attack a member of your realm!" |
| `PvPServerRules.IsSameRealm` | Player and realm NPC compare realms both ways, as Normal |
| `KeepManager.IsEnemy` + `IsRvrKeepEnemy` (new) | Frontier Wardens hostile to all. A keep of a realm (the claim sets it) is friendly to that realm and hostile to others. A realm-less keep follows `pvp_unclaimed_keeps_enemy` when unheld. Portal keeps and a lord-defeated keep stay non-hostile until R3a. |
| `AutonomousHubDeparture.HubPeaceApplies` | Same-realm hub peace skipped (they are allies); the cross-realm open BG landing peace stays |
| `WorldMode.AllowsRealmPair` | Compares `AllianceRealm`, so trade, group and guild checks treat a companion as its owner's realm |
| `BattleGroup.AddBattlePlayer` + `AdmitsRealm` (new) | Every add path refuses a player of another realm |
| `RvrServerRules` | Enemy players show race name, realm rank title, no guild, no title. Enemy bots show race name and no guild in the NPC create and living-data packets. Bind stones and recipes by realm. Speech by owner realm for companions. Realm byte: allies the viewer's realm, a same-byte enemy another realm. |

## Open points for later packages

- **R2:** safe areas and release points per realm (spec 3.2). Portal keeps and border hubs still protect every realm.
- **R2, before the first RvR boot:** R2's per-realm safe areas and travel lock must land before the first RvR boot. Until then an enemy inside a portal-keep hub is safe from players but can kill the hub's non-PEACE merchants, healer and teleporter; only plain guards respond.
- **R2:** forced per-realm open BG rule and realm landing peace.
- **R2:** realm boundary for bots. Town guards now attack enemy-realm bots, so bots that still cross enemy homelands will meet guards until then.
- **R3a:** portal-keep guards hostile to other realms. Claims by the keep's realm, realm flip on lord kill, Darkness Falls owner.
- **R3b:** relics (pads, pickup, guards, examine).
- **R4a, highest risk:** bot crew guilds are created realm-less and mixed, and autonomous bot groups form without a realm check (`AutonomousBotGroupCoordinator` is Stefan's file and was not touched). In the RvR world neither a guild nor a group allies across realms, so a mixed crew or group fights itself until R4a makes guilds and pickup groups single-realm. Patrols of such a guild become neutral opponents. The RvR world must not boot with bots before R4a, or R5a must create single-realm crews.
- **R4b:** realm raid battlegroups and the open BG roster's keep owner.
- **Owner:** `/level` stays disabled in both worlds.
- **Not audited:** direct realm comparisons inside bot AI outside the Tier 3 seams.
- **Real client:** race names for enemy bots, colour handling 0 with the spoofed realm byte for hostile same-realm actors.
