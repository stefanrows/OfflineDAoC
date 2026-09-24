# Autonomous bot behaviour roadmap

Status: **M0–M6 implemented offline. M7 started: the first live review found blocking issues; see [AUTONOMOUS_BOT_M7_REVIEW.md](AUTONOMOUS_BOT_M7_REVIEW.md).**
Last updated: 2026-09-24.

This roadmap covers the **autonomous gamebots**: the persistent population that
levels, groups, and fights on its own. It does not cover the player's
companions (see [COMPANION_ROADMAP.md](COMPANION_ROADMAP.md)), except for the
companion XP fix in M0.

**Goal:** playing should feel like logging into a live Camlann or Mordred server
in 2003. Some people only level. Some guilds only hunt. Most players do a bit of
both. Guilds take keeps, argue in chat, and come back for revenge. Nobody should
feel like a machine running the same loop.

The review is based on the code at `c5e027d` (0.32.3) and on a **read-only**
look at the owner's local install on 2026-09-24: server logs from 2026-09-18 to
2026-09-24, the save's bot table, and its server properties. Nothing was
changed there, and no names or save data are copied into this document.

## 1. Reviewed baseline (0.32.3)

The table records the system before M0–M5. The implementation notes below and
the [feature guide](../FEATURES.md) describe the current behavior.

| Area | Current behaviour | Code |
|---|---|---|
| Population | The launcher's **ADD LV.1 CREW / ADD LV.50 CREW** buttons create bots per realm. All are online all the time. The owner runs 1,510 bots. | `OfflineDaoc.Launcher/MainForm.cs` (`RealmGenerateLevelButton`) |
| Guilds | Up to 15 managed mixed-realm guilds per 56 bots, sized 1:2:4 (at 1,510 bots: 173, 86, and 43 members). Names are `Camlann Crew <one of 10 words>`. The name prefix is also how the code recognises a managed guild. | `AutonomousCrewManager.cs` |
| Goals | Every bot has one of three objectives: **SoloPve**, **GroupPve**, **RvR**. Every 60 s, each (guild, level bracket) bucket is re-split by the percentages in `bot-goals.json`. A task lasts 45–120 minutes, then the bot re-rolls. Bots have no lasting identity. | `AutonomousObjectiveAssignments.cs`, `BotGoalSettings.cs` |
| Launcher sliders | Solo PvE % / Group PvE % / RvR % for levels 1–19, 20–49, and 50. The owner's current file is 10/90/0, 20/70/10, 20/40/40. | `BotGoalsSettingsControl.cs` |
| Group PvE | Same guild only, within 5 levels, **candidates from any region**. Members travel to a meetup point, often by stable horse. A 15-minute no-show ends the whole group. A member who cannot be resurrected ends the group if fewer than two would remain. | `AutonomousBotGroupCoordinator.cs` (`TryFormGroups`, `ExpelRendezvousNoShows`, `PveCorpseRecovery`) |
| Camp choice | Uniform random choice among reachable, non-grey camps in the bot's own realm lands. Outdoor camps ignore how many bots are already there. | `AutonomousBotDecisionEngine.cs` (`SelectUniformGrindCamp`, `SelectBalancedPveCamp`) |
| PvP under 20 | "RvR" bots hunt local rivals in leveling zones, mostly in pairs, never more than 4. | `ChooseLowLevelPvpDestination` |
| PvP 20+ | Frontier roams (solo 15%, pairs 30%, 3–5 players 20%, eight players 35%). Targets are **only other bots that are also on an RvR task**, plus keeps and relics. | `ChooseRvrDestination`, `CamlannPopulationTuning.cs` |
| PvE parties | Any PvE party of 2+ attacks any visible, not-stronger, non-allied player-shaped target whenever it is idle. | `TryEngageOpenWorldPvpOpportunity` (`AutonomousWorldBotController.cs:1719`) |
| Grey targets | Bots attack a player who cons grey to them on 3% of checks, or when that player attacked them first. | `AutonomousRvrTargetPolicy.cs` |
| Identity | Names are built from syllables per realm. Bots have no personality or player type. | `AutonomousBotIdentityGenerator.cs` |
| Chat | Polite, formal, and realm-framed ("How fares everyone across the realm?", "Any {realm} folk"). | `AutonomousBotChatCoordinator.cs`, `AutonomousBotChat.cs` |
| PvE XP | `AwardBotOnNpcKill` with the normal cap, camp, group, guild, and bring-a-friend (BAF) bonuses. | `AbstractServerRules.cs` |
| PvP XP / RP | `AutonomousBotRealmPointRewards` with a "challenge" bonus of +25% per level the victim is above you (at most +4 levels, i.e. 2×). | `AutonomousBotRealmPointRewards.cs` |

## 2. Findings

F1, F6, and F7 describe the reviewed 0.32.3 baseline; M0 fixed them in
0.40.0. The group and population findings remain work for later milestones.

### F1 — Fixed in M0: bots leveled at 1× after 2026-09-23

Commit `bd108b0` (0.24.0, companion Stage 3 progression) changed the `allowMultiply`
argument in `AwardBotOnNpcKill` from `true` to
`botToAward.IsPersistentPlayerCompanion`
([AbstractServerRules.cs](../source/server/GameServer/serverrules/AbstractServerRules.cs),
around line 1955). `GameBot.GainExperience` applies `BOT_XP_RATE` only when
`AllowMultiply` is true. So from the deploy at 2026-09-23 13:55:

- Autonomous bots get **1×** XP from mob kills, not the configured `bot_xp_rate`
  (10 on the owner's install). They also lose the zone bonus and the item XP
  bonus.
- Temporary `/spawn` helpers lose `xp_rate` the same way.
- This breaks the contract in [COMPANION_BOTS.md](COMPANION_BOTS.md) ("Persistent
  autonomous bots use `BOT_XP_RATE`"). `GameBot.ScaleAutonomousExperience` is
  unused code left over from the older path.

Log evidence: the XP a bot received for the kill that levelled it up (median).

| Level | Before the deploy | After the deploy | 1× table value (`XPForLiving`) |
|---|---|---|---|
| 7 | 2,493 | 488 | 640 |
| 8 | 6,709 | 1,357 | 1,280 |
| 9 | 13,335 | **2,560** | **2,560** |

### F2 — Why the wall is at levels 10–12

DAoC's curve needs about 5 same-level kills for level 2, but about 50–55 per
level from level 10 onward. At 10× that is about 5 kills per level; at 1× (F1)
it is 50+. Bots feel fast early and then hit a wall at exactly 10–12.

### F3 — Most bot time goes to organising groups, not fighting

On 2026-09-23 (1,510 bots, 90% of levels 1–19 set to Group PvE):

- 8,471 groups formed. **68% were duos**; only 57 were full groups of eight.
- 5,867 groups ended: **63%** because someone never reached the meetup,
  **29%** because one member died and could not be resurrected, and **13
  (0.2%)** because their task timer ran out normally.
- 29,942 stable-horse boardings; 1,961 resurrection waits timed out and 39
  succeeded.
- At shutdown, about **15%** of bots were fighting and **about half** were
  waiting at, travelling to, or riding towards a meetup.

For comparison, on 2026-09-18/19 about 150 bots mostly soloed. They spent a
median of about 10–27 minutes per level from level 8 to 15, and one reached
level 24 in a day. Since the population was raised on 2026-09-20, **no bot has
passed level 12**.

### F4 — Everyone is the same level in the same zones

All 1,510 bots were created at level 1 within two days. About 500 per realm are
now in the same starter zones (180 in East Svealand, about 130 each in Vale of
Mularn, Connacht, and Camelot Hills). Outdoor camp choice ignores crowding, so
bots queue for the same spawns.

### F5 — Levelers kill each other all day

The owner set PvP to 0% for levels 1–19. Even so, **about 31% of the 22,531 bot
deaths on 2026-09-23** were bots killed by other bots. The cause is the "PvE
parties" row in §1: every idle PvE party attacks any weaker non-guildmate nearby.
With 15 guilds, almost every neighbour is a legal target. Each death then ends
the group (F3). That is not how 2003 levelers behaved. PvE groups mostly left
equal-level strangers alone; the danger came from gank squads and grudges.

### F6 — Fixed in M0: PvP kills gave 1× XP

Both the player and bots receive PvP kill XP through `GainExperience(eXPSource.Player, …)`
with no rate multiplier. A PvP-focused guild would level about 10× slower than
a PvE guild. The existing challenge bonus is also counted in **levels, not con
colour**. At level 40 a victim only 4 levels higher (still orange) already
gives the full 2×.

### F7 — Fixed in M0: companion catch-up was far too fast

A persistent companion receives a copy of its **owner's** kill award. Then
`XP_RATE` is applied, and the only limit is the owner's total XP
(`AwardPersistentCompanionsOnNpcKill`; `GameBot.GainExperience`). A level-1
recruit in a level-10 group therefore jumps to level 6–7 from one blue kill.
On live, each group member's XP per kill was capped by **their own** level
(1.25× a same-level kill); anything above that was lost.

### F8 — No identity or intent

The objective allocator uses the same mix for every guild, and re-rolls each bot
every 45–120 minutes. So no guild is "the hunting guild", and no bot is "a
player who only levels". Guild names are generic and numbered.

### F9 — The sliders cannot express a real server

Three task kinds per level bracket cannot describe gank squads, 8-player roams,
keep takers, or hybrids. Setting 0% still produces PvP (F5). The owner's 10/90/0
setting pushes 90% of the population into the least efficient pipeline (F3).

### F10 — Smaller issues

- Hunters never look for levelers. The RvR target list only includes bots on an
  RvR task (`AutonomousWorldBotController.cs:1880`), but ganking levelers was
  the core Camlann hunter activity.
- Reavers: tens of thousands of "No usable Flexible weapon" warnings per day, so
  some Reavers probably fight without a weapon.
- The "pixie scout" mob caused 1,450 bot deaths in a day. Check its level and con
  data.
- The chat still speaks of realms as sides.

## 3. Research: Camlann and Mordred

Camlann (GOA, Europe) and Mordred (Mythic, US) ran the same full-PvP ruleset
(see [CAMLANN.md](CAMLANN.md)). Players' memories agree on the shape of the
server:

- **Guilds were the unit.** Named guilds (Camlann: Requiem, Fear, Horde,
  Public Enemy, Slayers & Legends; Mordred: BANDA, Torcan, Dizzy, Xploit,
  Sin Vada) held keeps and flew their banners. One guild held all six relics
  within weeks, and relic guilds "leveled in comfort". "Without a strong guild
  you were meat."
- **Players credit Mordred guilds with starting 8-player roams** in mid-SI. The
  main loop ran from Tir na Nog's north gate to Druim Ligen. "8 man was king on Mordred." On Camlann,
  fights clustered at the Hibernia loop, Aegir's Landing, Gothwaite, and
  Cotswold ("8man in goth screaming LOG JOO 50s").
- **Leveling was dangerous but possible.** "Once you hit level ten you can be
  killed anywhere except the big cities." Groups of four level-10s were killed
  repeatedly while travelling. Gankers waited at towns and trainers. People who
  levelled in remote spots were still found sometimes, and strong guilds
  squatted dungeons ("pay or get killed").
- **The typical arc:** "leveled, SC'd up, and then PvP'd" (level up, spellcraft
  gear, then PvP). Many said the best XP came from killing players, and PvP
  leveling in the 40s was fun until the 24-hour "worth" timer.
- **There was a bit of everything:** lone lowbie gankers, stealth pairs, 8-player
  guild groups, keep zergs, and odd fun guilds (a 45-member all-Bonedancer
  guild of sub-30s that raided Mag Mell).
- **What killed the servers:** griefing lowbies around the clock, relic stacking,
  and (after 1.65) ToA-era classes and items. Offline, we can keep the danger
  and tune down the grief.
- **Chat** was short and informal: `lfg`, `lf healer`, `inc`, `wts`/`wtb`,
  `pst`, `ding`, `gg`, `brb`, `oom`, `add`, trash talk after kills.

Sources are listed at the end. No videos were reviewed; the research is from
forums and wiki material.

## 4. Owner decisions (2026-09-24)

| # | Question | Decision |
|---|---|---|
| D1 | How is a bot's playstyle decided? | **Guild focus + personal style.** Each guild has a charter; each member also has a personal temperament, so there are exceptions. |
| D2 | What replaces the Solo/Group/RvR sliders? | **Player-type mix + presets.** Level-by-level behaviour is automatic. |
| D3 | World shape at start | **Choice in the launcher:** "Established live server" (spread levels, new alts over time) or "Fresh launch" (everyone at level 1). |
| D4 | Danger while leveling | **Authentic but survivable.** Gank squads sometimes patrol leveling spots and routes; most fights are within about 5 levels; high levels rarely gank much lower levels; remote spots are quieter. |
| D5 | PvP kill XP | **Same multiplier as mob kills.** Higher-con victims give more XP and RP, as on live (killing purples paid well), without overdoing it. |
| D6 | Companion catch-up | **Live cap + small boost:** XP per kill capped by the companion's own level, times the XP rate, ×1.5 while it is 5+ levels behind the owner. |
| D7 | Chat | **2003 style, rougher:** short, lowercase, abbreviations, profanity allowed, no slurs. |
| D8 | Population size | **Stays the owner's choice.** The owner expects to run about 1,000–1,500. Add a recommended size based on the PC's specs for slower machines. |
| D9 | Mixed-realm leveling | **Allow cross-realm leveling.** A local pickup party may level in another member's home realm. |
| D10 | Guild grudges | **Include the player.** A recent killer can be pursued if the target is worthwhile, legal, alive, and outside a safe area. |
| D11 | Guild names | **Invented names only.** Do not use historical Camlann or Mordred guild names. |
| D12 | Chat boundary | **Profanity allowed; no slurs.** Keep a maintained blocklist on authored autonomous chat and generated names. |

## 5. Target design

### 5.1 Player types

Every bot gets one persistent **player type** and a few **traits**. The type
says what the bot likes to do; its level and situation decide what it does
right now (5.3).

| Type | Who they are | Default group shape |
|---|---|---|
| **Leveler** | Plays for PvE: levels, dungeons, gear, raids. Avoids fights and retaliates when attacked. | Solo, pickup groups, guild PvE groups |
| **Casual** | Plays slowly: long town breaks, trading, crafting, chatting. Levels mostly solo. | Solo or duos |
| **Hybrid** | The typical player: mostly levels, takes easy fights, joins guild roams at prime time from about level 20. | Guild groups |
| **Hunter** | Gankers and stealth pairs. From level 10 they hunt levelers around their own level near hotspots and travel routes, and farm PvE when they hit a wall. | Solo, pairs, crews of up to 4 |
| **Roamer** | 8-player guild groups. They level partly through PvP, then run the frontier loops. | Full groups |
| **Keep warrior** | Siege and zerg guilds: claims, defence, relic raids. Mostly active from level 35. | Large forces |

**Traits** (persisted, small integer ranges): aggression, risk tolerance,
sociability, patience (how long they grind or wait), and chattiness. Traits
shift behaviour within a type. For example, an aggressive Leveler takes an easy
gank; a cautious Hunter picks only sure fights.

### 5.2 Guild charters

A guild has a **charter**, a size, a name style, a home area, and a prime time.
Members' player types are drawn from the charter's mix (D1). This gives, for
example, a hunting guild with a few Levelers in it.

| Charter | Member mix (starting point) | What the guild does |
|---|---|---|
| Hunting guild | 50% Hunter, 25% Roamer, 15% Hybrid, 10% Leveler | Ganks in leveling zones, small crews, keeps KOS (kill-on-sight) lists |
| RvR guild | 50% Roamer, 25% Hybrid, 15% Keep warrior, 10% Hunter | 8-player groups at prime time, PvP leveling from about level 20 |
| Keep guild | 45% Keep warrior, 25% Roamer, 20% Hybrid, 10% Leveler | Claims and defends keeps, runs relic raids |
| Leveling guild | 55% Leveler, 25% Hybrid, 15% Casual, 5% Roamer | Dungeon runs, guild XP groups, defends itself |
| Social guild | 40% Casual, 35% Leveler, 25% Hybrid | Trading, chatting, relaxed leveling |

**Names:** new, invented names in 2003 style that hint at the charter
(for example *Blood Oath*, *Night Stalkers*, *Iron Covenant*, *Hearth and
Blade*, *Tavern Rats*). Include a few jokey or lowercase ones, as real servers
had. There is no `Camlann Crew` prefix. By default we do **not** use the real
historical guild names; they belong to real communities. An optional tribute
list stays an open question (§7). Managed guilds need a real marker (a
database flag or table) instead of the name prefix.

### 5.3 What each type does as it levels ("the wall")

| Type | 1–9 | 10–19 | 20–34 | 35–49 | 50 |
|---|---|---|---|---|---|
| Leveler | Solo, pickups; `/safety` on | PvE groups; flees or retaliates | PvE groups, dungeons | Dungeons, guild XP; defends guild keeps if asked | Gear, dragons and raids; rare roam |
| Casual | Slow solo, town breaks | Same | Same | Same | Trading, raids, socialising |
| Hybrid | PvE | PvE; takes easy fights | PvE plus about 20% guild roams | About 35% PvP | About 50/50; gear farming when behind |
| Hunter | PvE, safety on | Drops safety at 10; pairs hunt within about 5 levels | Hunts leveling hotspots, routes, portal-keep exits | Hunts and joins roams; PvP leveling | Gank squads (D4 grey rules), frontier stealth |
| Roamer | PvE | Guild PvE groups; small roams from about 15 | 8-player PvP leveling (frontier, SI hubs) | 8-player frontier loops | 8v8 loops at prime time; raids and gear off-peak |
| Keep warrior | PvE | PvE | PvE; defends guild keep | Guild keep claims and defence | Keep and relic campaigns |

**Wall rules.** A PvP-focused bot switches to a **PvE block** (levelling or
gear farming, 45–90 minutes, persisted) when any of these holds:

1. **Outleveled:** for about 10 minutes, no legal target in its hunting grounds
   is within its preferred level range, or recent opponents are consistently
   higher.
2. **Losing:** 3 deaths in 20 minutes, or a poor kill/death record over its last
   N fights.
3. **Under-geared:** its equipment score is below the norm for its level (for
   example, empty slots or items well below its level). At 50, this is the
   "SC'd up" gear farming step before PvP.
4. **Catching up:** its guild's roaming group is out of its level range.

It returns to its preferred activity when the condition clears. Levelers use the
reverse rule: an aggressive Leveler may join a guild roam after a long PvE
stretch.

### 5.4 Launcher: "Server population" instead of the sliders (D2, D3, D8)

The current Bot Goals Setting tab becomes one screen:

- **Preset:** Camlann 2003 (default), Peaceful, Bloodbath, Keep Wars, Custom.
- **Player-type mix:** one slider per type from 5.1, totalling 100%. It sets
  the charter mix of new guilds and the types of newly created bots.
- **Danger in leveling zones:** Mild / Authentic (default) / Full Camlann. It
  sets how often Hunters patrol leveling zones, the grey-gank chance, and
  whether high-level ganks happen.
- **World shape (D3):** Established live server / Fresh launch. Applies when
  bots are created.
- **Population size (D8):** unchanged control, plus a "recommended for this PC"
  hint based on memory and CPU cores. The hint is advisory; the owner can
  override it.

Starting numbers, to tune during playtests:

| Preset | Leveler | Casual | Hybrid | Hunter | Roamer | Keep warrior | Danger |
|---|---|---|---|---|---|---|---|
| Camlann 2003 | 25 | 10 | 30 | 15 | 12 | 8 | Authentic |
| Peaceful | 45 | 20 | 25 | 3 | 5 | 2 | Mild |
| Bloodbath | 10 | 5 | 25 | 30 | 20 | 10 | Full Camlann |
| Keep Wars | 15 | 5 | 25 | 10 | 20 | 25 | Authentic |

`bot-goals.json` gets version 2. A version-1 file is recognised: the launcher
maps it to the nearest preset and says so before saving. Like today, settings
change only while the server is stopped and apply at the next start.

### 5.5 Rewards (D5, D6)

- **Mob kills:** restore `BOT_XP_RATE` for autonomous bots and `XP_RATE` for
  temporary helpers (F1). Remove the dead `ScaleAutonomousExperience` code.
- **PvP kills:** apply the same rate as mob kills (`xp_rate` for the player,
  `bot_xp_rate` for bots). Keep the per-kill cap (`XP_PVP_Cap_Percent`) and the
  repeat-kill "worth" timers.
- **Challenge bonus by con colour, not levels:** yellow or lower ×1.0, orange
  ×1.25, red ×1.5, purple ×2.0, for both XP and RP. At low levels, where each
  colour is one level wide, this is close to today's numbers. At high levels it
  is fairer: for a level-40 attacker, a level-44 victim is orange and pays ×1.25
  instead of today's full 2×.
- **Companion catch-up:** the award is the smaller of the owner's base award
  and the companion's own-level cap (`XP_Cap_Percent` of a same-level kill).
  That is multiplied by `XP_RATE`, then by ×1.5 while the companion is 5+ levels
  below its owner, and still clipped at the owner's total XP. At 10×, a new
  companion in a level-10 group gains about 1 level per kill for levels 1–4.
  After that it needs a few kills per level, slowing as it nears the owner's
  level.

### 5.6 Leveling flow (fixes F3–F5)

- **Local groups first.** Ordinary PvE groups form from bots already in the
  same region and level range. **Pickup groups across guilds** are allowed for
  PvE (grouping makes members allies, as on Camlann). Cross-region travel is
  kept for planned guild events: dungeon runs, roams, keep and relic
  campaigns.
- **Soft meetups.** A group starts once a minimum number has arrived; late
  members join on the way. A no-show leaves the group without ending it.
- **Groups survive deaths.** Low-level dead members release, run back, and
  rejoin, as real players did. A group ends only when it really cannot continue.
- **Crowd-aware camps.** Outdoor camp choice gets a soft penalty for bots
  already present and for recently emptied spawns. It spreads bots across every
  suitable reachable zone, including another realm's lands (D9).
- **No automatic ganking by PvE groups.** PvE parties fight other players only
  when attacked, when defending a guildmate, over a grudge (5.7), or when their
  type and traits allow it (Hybrids and aggressive Levelers taking an easy
  kill).
- **Check the deadliest mobs** (pixie scout) and **supply Reavers with flexible
  weapons**.

### 5.7 Realism layer

- **Hunters hunt levelers.** Hunter targets include every legal player-shaped
  target in their hunting grounds, including the player. Their hunting grounds
  are popular leveling zones, stable routes and zone lines, portal-keep exits,
  and dungeon entrances. The Danger setting (D4) limits how often and how far
  down in level they hunt.
- **Grudges and KOS.** A guild remembers who killed its members, the player
  included, for a few hours. It may send a revenge crew and announce it in guild
  chat ("KOS Xyz, seen in Emain"). Grudges fade and respect the worth timers.
- **Chat, 2003 style (D7).** Short and lowercase: `lfg`, `lf1m healer`,
  `inc 8 at hib loop`, `wts`, `ding`, `gg`, `lol`, `/em laughs`, and rough trash
  talk after fights. Tone follows type and traits: Hunters taunt, Casuals
  chatter, Levelers ask for groups. No realm-as-side phrases. A maintained
  blocklist keeps slurs out. The existing rate limits stay.
- **Names.** Keep the fantasy name generator, and add a share of names in other
  common styles (short names, puns, joke names), within DAoC's name rules.

### 5.8 Population shape and performance (D3, D8)

- **Established live server:** new bots are created across levels, for example
  15% 1–9, 15% 10–19, 20% 20–34, 25% 35–49, and 25% level 50. Each gets
  suitable equipment and specializations, and level-50s get realm points.
  **Fresh launch** keeps today's everyone-at-1 start.
- **Alts over time:** new level-1 characters join existing guilds at a slow,
  configurable rate, so starter zones always have some newcomers.
- **Measure before recommending.** Record memory per bot and game-loop tick time
  at 500, 1,000, and 1,500 bots, then turn that into the launcher's recommended
  size per memory and CPU tier. Fewer stable-horse trips (5.6) should also cut
  server load.

## 6. Milestones

Implement one milestone at a time. Each ends with offline tests and a separate
owner playtest; unit-test totals are never reported as a gameplay pass. Version
bumps follow `AGENTS.md`.

### M0 — XP fixes (can ship first)

Implemented in 0.40.0 (2026-09-24). Focused offline XP tests pass; the owner
check below still needs a real-client playtest. The PvP worth timers were left
unchanged.

1. Restore the mob-kill multiplier for autonomous bots and temporary helpers (F1).
2. Companion catch-up with its own-level cap and the ×1.5 boost (F7, D6).
3. PvP kill XP with the rate multiplier, and the con-based challenge bonus (F6, D5).
4. Tests: award amounts for each bot kind at 1× and 10×; a level-1 companion in
   a level-10 group; PvP awards by con colour; repeat-kill timers unchanged.

Owner check: bots pass levels 10–12 again, and a new companion gains about one
level per kill at first instead of five.

### M1 — Leveling flow

Local and pickup groups, soft meetups, groups that survive deaths, crowd-aware
camps, no automatic ganking by PvE groups, the deadly-mob check, and Reaver
weapons (5.6). Add a periodic `AUTONOMOUS_ACTIVITY_SUMMARY` log line with
fighting, travelling, meetup, dead, and town counts, so progress is measurable
without guessing.

Targets on a 1,500-bot run (starting points): at least 50% of bots fighting,
pulling, or resting at a camp; most groups ending by their timer rather than a
failed meetup; bot-on-bot deaths among levelers well below today's 31%.

Implemented offline in 0.41.0. Pickup PvE groups form from the local region
across guilds and realms. They depart when the leader and one other member
reach town, accept late followers, and remove no-shows individually. A single
death preserves the camp and the member can release and run back; multi-member
wipes retain the safe regroup. PvE groups no longer seek unprovoked open-world
PvP. Outdoor camp draws prefer less crowded and recently productive spawns.
The per-minute activity log reports mutually exclusive fighting, traveling,
meetup, dead, town, camp, and other counts. The 1,500-bot targets still require
the owner's real-client run.

The installed world data has 30 pixie scout placements using two level-8
templates, each with 500-unit aggression range and no configured spell or
style. No template error was established, so M1 leaves those world rows alone.
The installed Reaver career grants Flexible at level 5; level 1–4 Flexible
builds now use Slash and switch to Flexible when that ability unlocks.

### M2 — Identity: player types, traits, and charters

- Additive save changes: player type and traits on `offline_world_bots`, a
  charter table for managed guilds, and a managed-guild flag that replaces the
  name prefix. Existing bots keep their level, items, and money. Each is
  assigned a type deterministically from its class and guild. Existing guilds
  receive a charter and a new name; keep claims move with the rename (the
  existing `MappedKeepOwner` path).
- A per-bot activity scheduler (type + level phase + wall state + guild events)
  replaces the per-bucket allocator in `AutonomousObjectiveAssignments`. It keeps
  its durable timers and the between-task training, selling, and town-break
  services.

Implemented offline in 0.42.0. The server adds type, five traits, and PvE-block
state to each saved bot without resetting progress or possessions. Existing
generated guilds gain durable charter rows and invented names. The marker row,
not the final name, identifies a managed guild; interrupted renames retain the
old and new names until keep references are updated. Guilds containing human
characters stay protected during the automatic rename. The one-minute
assignment pass selects each bot's next Solo PvE, Group PvE, or RvR task from
its saved type, level phase, wall state, and raid reservation. Three PvP deaths
in twenty minutes or ten minutes without a target starts a 45–90 minute PvE block;
under-geared or outleveled bots also favor PvE at their next task boundary.
The old Bot Goals Setting exclusions remained constraints at M2; M4 replaced
that screen. Existing active task and between-task deadlines are retained.
The owner still needs to verify startup migration and behavior on a backed-up
real installation.

### M3 — Behaviour per type

Hunter hunting grounds and target rules (including the player), Roamer 8-player
loops and PvP leveling, Keep-warrior campaigns, Leveler and Casual routines,
Hybrid prime-time roams, the wall rules (5.3), and gear farming at 50. The
Danger setting drives Hunter patrol frequency and grey-gank chance.

Implemented offline in 0.43.0. Hunters from level 10 patrol level-appropriate
outdoor camps, with extra weight for active camps and nearby outdoor routes;
level-35+ Hunters also use frontier clearings. They consider the player and
autonomous bots through the same visible, legal target scan. Grey attacks use
a stable ten-minute chance, limited by the danger setting and level gap.
Roamers prefer full eight-person guild groups and follow frontier clearing
loops; Keep warriors can open or reinforce ordinary keep campaigns from level
35 when the group can supply siege. Hybrids favor evening roams, while Casuals
take town breaks more often and Levelers remain PvE-focused. The saved PvE wall
still handles losses, unavailable targets, weak gear, and guild level gaps;
level-50 gear farming checks armor as well as weapons and favors dungeons when
reachable. The server property `camlann_bot_leveling_danger` defaults to
Authentic (1); M4 added the corresponding launcher setting. Hunters do
not select dungeon entrances as patrol posts. Gameplay and danger tuning still
need the owner's real-client playtest.

### M4 — Launcher "Server population"

Presets, player-type mix, Danger, World shape, recommended size (5.4). This
includes `bot-goals.json` v2 with v1 migration, the server reading it at
startup, and the Active Population tab showing each bot's type and guild
charter. Launcher tests cover validation, presets, and migration.

Implemented offline in 0.44.0. The launcher presents the named presets, six
player-type percentages, leveling-zone danger, and world shape on one screen.
The mix must total 100%; editing a named preset switches it to Custom. The
existing Add crew buttons still set roster size, and a CPU/memory based hint
is advisory and unbenchmarked. Settings are saved atomically only while the
server is stopped, then read once at server startup. A version-1 goals file is
mapped to the nearest preset in memory; the launcher shows that mapping for
review before saving version 2. New autonomous guilds and unstamped bots use
the selected mix, while stamped identities and active tasks remain saved.
The Active Population tab shows each saved bot's type, guild, and charter.
Established world shape was stored for the M5 generator; M5 now applies it to
the Add crew buttons. The owner still needs a real-client playtest.

### M5 — Established-server generator and alts

Level-spread creation with suitable gear, specializations, and realm points;
the fresh-launch option; the alt trickle; measured size recommendations
(5.8).

Implemented offline in 0.45.0. Add crew creates level-1 bots for Fresh launch
or a stratified established batch across the 1–9, 10–19, 20–34, 35–49, and
50 bands (15/15/20/25/25 per 100 bots). The separate Add Lv.50 action remains
available. Existing characters are never re-leveled. Newly created bots get
the server's exact starting XP for their level. Levels 1–49 receive generated
class-appropriate armor, jewelry, and weapons at first login; level-50 bots
receive their prepared class loadout. Higher-level bots train their lifetime
specialization plan at first login. Level-50 creations also get realm points.
Creation rolls back if a complete level-50 loadout is unavailable. Higher-level
bots start in their realm capital and travel to normal activity; lower-level
bots use valid randomized Classic/SI starts.

The server can add one new level-1 character to a managed guild every 72 hours
by default, up to 5,000 non-retired bots; both values are configurable in Server
population, and setting the interval to zero disables the trickle. The existing
login ramp and total-roster meaning remain unchanged. The server records local
working memory and game-loop tick p95 only after five stable minutes at each of
500, 1,000, and 1,500 active bots. The launcher gives no numeric recommendation
until all three samples exist for the same CPU/memory tier; it then recommends
the highest consecutive tier within the measured memory and tick budget. To
calibrate this PC, the owner must run separate stable rosters at those sizes and
review `population-benchmarks.json` and `AUTONOMOUS_POPULATION_SAMPLE` logs.
The logged memory-per-bot figure includes the shared server baseline; the
recommendation uses total process working memory.
No server was started for the offline implementation, so no measured capacity
or real-client gameplay claim is made yet.

### M6 — Social realism (implemented offline in 0.50.0)

- Autonomous chat uses short player-type voices, LFG and trade lines, guild
  banter, existing rate limits, and the D12 blocklist. Profanity remains
  available for occasional fight taunts; authored text and generated names are
  checked before use.
- Guilds keep at most 16 recent KOS targets for three hours in an additive
  table. The list includes the player, remembers companion kills as the human
  owner, and is checked against target worth timers, current attack rules, and
  safe areas. Existing RvR crews can pursue reachable targets.
- New managed guilds receive invented charter-themed names. The character-name
  generator also uses a small share of common and joking handle styles.
- No server was started and no real-client gameplay was verified. M7 remains
  the owner playtest and tuning milestone.

### M7 — Owner playtest and tuning

A playtest checklist per preset: leveling a new character from 1 to 20,
roaming at 20–40, and level 50. Tune the numbers in this document from the
results. Record the outcome here.

First input (2026-09-24, 0.50.0, 1,500 bots, Fresh launch): see
[AUTONOMOUS_BOT_M7_REVIEW.md](AUTONOMOUS_BOT_M7_REVIEW.md). Bots below level 10
have no PvP safety and kill each other at stables and bind points. Solo camp
choice ignores travel time, so most solo trips cross realms. Many pickup
groups never find a camp. M6 grudge writes stall the reaper tick. After about
two hours, bots were level 1–8. The review's phases A–D are the next work.
Phase A (low-level bot safety, batched grudge writes, grudge and crowd-control
limits, PvP-death handling, engagement diagnostics) is implemented offline in
0.53.0. Phase B (local, travel-aware leveling and viable pickup groups) is
implemented offline in 0.54.0. The owner run for both phases is pending.

## 7. Open questions

The established-server level distribution in 5.8 still needs owner confirmation
or revision. The owner playtest in M7 should also record any tuning changes.

## Sources

- [Save Mordred Once & For All (FreddysHouse)](https://forums.freddyshouse.com/threads/save-mordred-once-for-all.244577/)
- [The Mordred Problem (FreddysHouse)](https://forums.freddyshouse.com/threads/the-mordred-problem-an-interesting-article.250061/)
- [PvP/Mordred server… Please (MMORPG.com)](https://forums.mmorpg.com/discussion/382123/pvp-mordred-server-please)
- [Mordred (PVP) Server (Honor Empire archive)](https://www.thehonorempire.org/web/forums/topic/1264-mordred-pvp-server/)
- [Serveur PvP (JeuxOnLine archive)](https://archives.jeuxonline.info/fils/97673.html)
- [Uthgard: Camlann Players / Guilds](https://www.uthgard.net/forum/viewtopic.php?f=32&t=39399&start=15)
- [Uthgard: When exactly did 8v8 become a thing](https://www.uthgard.net/forum/viewtopic.php?f=32&t=43310&start=15)
- [Camelot Herald: PvP Server Addendum](https://camelotherald.fandom.com/wiki/PvP_Server_Addendum)
- [Camelot Herald: PvP "hardcore" server rules](https://camelotherald.fandom.com/wiki/Where_can_I_find_information_about_the_PvP_%22hardcore%22_server,_rules,_features_and_commands%3F)
- [Experience in a group (disorder.dk)](https://disorder.dk/daoc/experience-in-a-group/)
- [Common Chat Terms (ZAM)](https://camelot.allakhazam.com/Chat_Terms.html)
