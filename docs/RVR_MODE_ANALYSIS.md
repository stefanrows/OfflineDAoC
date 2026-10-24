# RvR mode analysis (2026-10-24, main 7e28227)

Read-only analysis by the DAoC advisor agent for the owner's question "how do
we get back from the PvP mode to the RvR level of the original?". Nothing
here is implemented or authorised; decisions are listed at the end.

## 1. What "RvR mode" must change

| System | Today (Camlann) | RvR mode | Where |
|---|---|---|---|
| Startup | Refuses anything but GameType=PvP plus marker `Camlann-1` | Accept a second marker such as `Realm-1`; the save carries the mode | GameServer.cs:1003-1020, world/CamlannWorldModel.cs |
| Hostility | Allied = group, guild, battlegroup, or same realm only in region 252 under perrealm | Same realm is allied everywhere. Guild and battlegroup count only within one realm | serverrules/PvpCombatant.cs:55-97, battlegrounds/open/OpenBattlegroundMode.cs:52 |
| Hub peace, safety | Portal keeps and capitals are safe for every realm | Safe only for the owning realm; other realms meet the guards | PvpCombatant.IsSafeArea, AutonomousHubDeparture, PvPServerRules.cs:150-200 |
| Client | Color handling 1 (all players red), realm spoofing, dummy-guild trick | Color handling 0 or 2 and the real realm byte, as upstream did | PvPServerRules.cs:363-401, PacketLib1124.cs:307 |
| Keeps | Owned by guilds; unowned keeps held by the Frontier Wardens garrison; portal keeps never hostile | Owned by a realm with an optional guild claim from that realm; portal keeps hostile to other realms | KeepManager.cs:569-618, PvpKeepCampaign.cs:15, AbstractGameKeep.CheckForClaim |
| Relics | Mounted in a guild's keep; bonus to that guild only | Realm pads; bonus to the whole realm | RelicMgr.cs:215-262 |
| Travel | Any realm uses any porter or capital (decision 9) | Decision: open travel or realm-locked | AllRealmsTeleporter.cs, AutonomousRealmBoundary |
| Bots: RvR layer | Event layer is realm-shaped, but armies are grouped by guild, so a mixed guild marches as one army | Armies per realm, or per guild and realm | AutonomousRvrEventLayer.cs:581, .GuildArmy.cs:30 |
| Bots: other parts | Crew login balancer, grudges, banter | Realm login balancer (the file still exists), realm grudges | AutonomousPopulationController, AutonomousGuildGrudgeMemory |
| Open BG | `open_bg_mode=perrealm` | Becomes the default under RvR | OpenBattlegroundMode.cs |
| Gothwaite mode | Separate opt-in mode | Leave off or make it exclusive with RvR | GothwaitePvpRules |
| Companions | Any realm | Count as the owner's realm for alliance; otherwise owner-realm bots kill them | PvpCombatant.Resolve |
| Realm Exchange | Local broker in any capital | Own capital only if travel closes | BotBrain exchange |
| Launcher | Forces PvP, runs the Camlann reset, refuses Normal imports | Pick the world (Camlann or RvR) and reset per world | CamlannServerConfig.cs, CamlannWorldReset.cs, ImportEngine.cs:81 |

Hostility is cheap to flip. Tier 3 routed bot combat through the shared
alliance check, so one rule in PvpCombatant.AreAllied covers it. Only a
handful of places check the rules class type directly; most of them are
relics, keeps and GameBot.

## 2. FFA-specific state in the current save (snapshot 2026-10-24 18:14)

| Fact | Value |
|---|---|
| World bots | 1,810 in 15 guilds; every guild has all three realms, largest realm share 34-40% |
| Player guild North Bomb | Nova (Midgard 50) and Ked (Hibernia 50); they become enemies in RvR |
| Companions | 15 in total; 2 from another realm (Isolde with Nova, Elian with Ked) |
| Old Frontiers keeps | 27 non-portal keeps: 9 held by mixed bot guilds, 18 by the Frontier Wardens |
| Relics | All 6 at home, none mounted in a keep (KeepID 0) |
| Mode properties | `open_bg_mode=perrealm`, `pvp_unclaimed_keeps_enemy=False` |

## 3. Options with consequences

### A: play the upstream install for RvR; the fork stays Camlann
- **Behaviour:** real realm-vs-realm play today. The upstream log from the
  comparison run (23:41 to 14:07) shows 6,487 RVR_BOT_KILLED events, 2,719
  border-staging events, 455 siege events and 2 keeps taken.
- **Lost:** ToA, Master Levels, cross-realm companions, the open BG, the
  companion squads, and the fork characters Nova and Ked, who live in another
  save.
- **More expensive later:** two codebases drift apart, and every fork feature
  wanted in RvR needs a backport to upstream.
- **Unverified:** nothing new. It is the tested original.

### B: a server-wide RvR switch inside the fork
- **Behaviour:** same realm is a friend everywhere, keeps and relics belong to
  realms, bot armies fight per realm, and every fork feature stays. It reuses
  the open BG perrealm rule, which today covers only region 252
  (OpenBattlegroundMode.cs:52, PvpCombatant.cs:88).
- **Cost:** most bot hostility already goes through one alliance check
  (PvpCombatant.AreAllied, enforced by the Tier 3 regression test), so the rule
  core is small. Keeps, relics and the bot event layer are the real work.
- **More expensive later:** every new feature needs testing in two modes. The
  CAMLANN.md contract ("one mode, no dual-mode flag") has to be amended.
- **Unverified:** the client view across the whole world (color handling and
  realm byte for bots shown as NPCs), and the realm paths for keeps and relics.

### C: two saves or profiles in the launcher, one server build
- **Not possible on its own.** The fork refuses GameType Normal at startup
  (GameServer.cs:1009). Its Normal rules (NormalServerRules.cs:19-84) lack
  cross-realm companions, guild keeps, the open BG and the ToA hooks.
- **Therefore C is only the delivery mechanism for B:** each save carries its
  world marker and the launcher picks the world.

### Recommendation: B, delivered as C
- **Build the RvR switch in the fork, keyed to the save's world marker,** for
  example `Realm-1` beside `Camlann-1`, never as a flag on the Camlann save.
- **The RvR world is a second save.** It is either fresh or a migrated copy of
  the Camlann save, and the Camlann world stays untouched.
- **Why:** it is the only option that gives classic RvR together with ToA,
  companions and the open BG. A mode bound to the save also prevents a save
  from running under the wrong rules.
- **Effort:** about 10 sessions.

## 4. Phased plan for option B, delivered as C

### Owner decisions needed first
1. **Contract:** amend the "one mode" rule in CAMLANN.md. The open BG perrealm
   setting and the Gothwaite mode already bend it.
2. **Start of the RvR world:** a fresh world, or a migrated copy of the Camlann
   save that keeps characters, inventories and companions.
3. **Nova and Ked:** they are in different realms and the same guild, so they
   become enemies. Options: a new character, keeping them as enemies, or
   leaving them out of the RvR world.
4. **Cross-realm companions** (Isolde with Nova, Elian with Ked): allowed but
   fighting as the owner's realm, or forbidden.
5. **Bot guilds:** split the 15 mixed guilds into about 45 single-realm guilds,
   or recruit new ones.
6. **Keeps:** start at their original realm with a guild claim from that realm,
   or keep the Frontier Wardens.
7. **Relics:** realm pads with a realm-wide bonus, as in 1.65.
8. **Travel:** keep enemy capitals and porters open, or lock them as in 1.65.
9. **Portal keeps:** safe for their own realm only, with guards attacking
   other realms.

### Phases
- **Phase 0, about 0.5 sessions:** owner decisions, contract amendment, spec.
- **Phase 1, about 2 sessions: rules core.** Mode helper reads the world
  marker; startup accepts `Realm-1` (CamlannWorldModel.cs, GameServer.cs:1003).
  Alliance check adds "same realm" everywhere; guild and battlegroup count only
  within one realm; companions take their owner's realm (PvpCombatant.cs:55-97).
  Safe areas and hub peace per realm; color handling and realm byte change;
  dummy-guild trick off in RvR (PvPServerRules.cs:363-401, PacketLib1124.cs:307).
  Effect: own realm friendly, other realms enemies. Risk medium (client view).
- **Phase 2, about 2 sessions: keeps and relics.** Keep hostility by realm
  (generalise the open BG realm-owned keep rule, KeepManager.cs:569-618);
  claims need the keep's realm; Frontier Wardens become realm garrisons;
  portal-keep guards attack other realms; relics back on realm pads, RelicMgr
  checks the mode (RelicMgr.cs:215-262). Risk medium.
- **Phase 3, about 3 sessions: bots.** Armies per realm (or per guild and
  realm) in the event layer (AutonomousRvrEventLayer.cs:581, .GuildArmy.cs:30);
  realm login balancer returns; grudges and banter by realm; travel per
  decision 8; open BG perrealm default; Gothwaite excluded. Risk high (shared
  AI files).
- **Phase 4, about 2 sessions: save and launcher.** Migration tool on a copy
  or a fresh reset; world picker in the launcher; reset per world; import
  guard (ImportEngine.cs:81) and server config writer (CamlannServerConfig.cs).
  Risk medium (save data).
- **Phase 5, about 1 session:** tests for both modes, docs, real-client spike.
- **Total: about 10 sessions.**

### Risks
- Client view unproven (color handling 0 or 2 with bots shown as NPCs).
- Guild/alliance as a backdoor: any missed check makes an enemy realm an ally.
- Tests encode Camlann: the Tier 3 test that bans realm inequality must allow
  the RvR branch.
- File conflicts with ToA work (rules, BotBrain, event layer): phases 1 and 3
  need their own worktree and should follow the ToA merges.
- Long-term cost: every later feature needs testing in two modes.

### Could not determine
- The upstream save contents (its database was not opened).
- Whether open BG perrealm was ever checked in a real client (TASKS 116 pending).
- The guild grudge table (snapshot reported "malformed").
- The exact name of the Gothwaite mode switch.
- How many bot checks still compare realm directly outside the Tier 3 seams.
