# Changelog

All notable changes to this fork are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this fork uses [MAJOR.MINOR.PATCH](https://semver.org/). See `AGENTS.md`
for when to bump each number.

The launcher pin `DisplayVersion` must match the latest dated heading below.
The upstream playable download remains GitHub **v0.3**; that is the runtime
package, not this fork's version.

## [Unreleased]

## [0.71.0] - 2026-09-26

### Added

- None.

### Changed

- None.

### Fixed

- Effect expiration and replacement no longer acquire effect-state and effect-list locks in opposite order, preventing the observed game-loop deadlock.
- Neutral world and frontier hasteners use the server's alliance rules and show success only when their speed effect takes hold; blocked casts explain why.
- Legacy saved tank companions align a missing build plan with their trained weapon specialization without replacing saved equipment or valid plans.
- Autonomous PvE groups choose camps within a bounded party travel estimate and verify candidate routes before committing their fixed 30-minute travel window.

### Removed

- None.

## [0.70.0] - 2026-09-26

### Added

- Added a live launcher action to rebalance the full non-retired saved autonomous-bot roster, including offline bots, with pending/applied/failed status and safe-boundary handling for active bots.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.69.0] - 2026-09-26

### Added

- None.

### Changed

- Grouped Bards with endurance songs maintain their instrument pulse during combat and continue their group-support actions; mana and speed songs wait until after combat.

### Fixed

- None.

### Removed

- None.

## [0.68.0] - 2026-09-26

### Added

- None.

### Changed

- Replaced the Active Groups card stack with a sortable, realm-filterable table and selected-group roster details, reducing per-refresh control construction while retaining live group timers.

### Fixed

- None.

### Removed

- None.

## [0.67.0] - 2026-09-26

### Added

- Active Population shows sortable Realm Points for autonomous bots, using live values when available and saved values otherwise.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.66.1] - 2026-09-25

### Added

- Recorded the live effect-lock server deadlock and client disconnect for investigation.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.66.0] - 2026-09-25

### Added

- None.

### Changed

- Realm frontier travel now leads to Old Frontiers mob areas and no longer offers Agramon.

### Fixed

- The keep Chief claim prompt accepts eligible guilds at unclaimed Camlann keeps, and eight-member claims use each group member's actual position at the keep.
- Legacy New Frontiers teleport rows can no longer send players to region 163 through the shared teleporter.

### Removed

- None.

## [0.65.0] - 2026-09-25

### Added

- Player-led casters set to Bomb can use PBAoE against at least two PvP opponents already fighting their group, while avoiding idle and mezzed players.

### Changed

- Ready bombers prefer their highest learned PBAoE rank. Focus casters prioritize staves by the focus levels covering their learned spell lines, including all-lines focus, before ordinary item value.

### Fixed

- A staff with unrelated focus no longer displaces a staff covering the caster's spell lines solely because its item level is higher.

### Removed

- None.

## [0.64.0] - 2026-09-25

### Added

- Owned companions accept `/gc invite` immediately; persistent companions keep guild membership after being benched or reloaded and show the guild emblem on equipped cloaks and shields.

### Changed

- `/gc form` permits a solo founder at a registrar and asks only other human group members to approve the guild.

### Fixed

- Guild founding no longer stalls when companion bots occupy group slots.

### Removed

- None.

## [0.63.0] - 2026-09-25

### Added

- Nearby player-led companions board the player's siege ram in available seats and leave when the player dismounts. Companion seats count toward ram damage and reload timing.
- Grouped player-led Healers use learned area stuns against clustered enemies already fighting the group. A bomb caster with a learned PBAoE spell draws stun placement toward its pull, with healing still taking priority when a group member is below 65% health.

### Changed

- Defensive companions follow a player's active attack on a closed enemy keep door beyond the normal defensive radius. Pet classes command their pets to that door, and Theurgists repeatedly summon pets while their normal cast and power rules allow it.

### Fixed

- Keep doors with a grey con no longer block an explicit player-led companion attack or pet order.

### Removed

- None.

## [0.62.1] - 2026-09-25

### Added

- A tracked task and implementation checklist for applying population-type
  mixes to existing autonomous bots while the server runs, with safe task
  transitions, progress preservation and restart persistence acceptance checks.

## [0.62.0] - 2026-09-25

### Changed

- Expanded all six population-type tooltips with practical PvE/PvP guidance,
  including Hunter crews versus Roamer groups, level thresholds, and the fact
  that slider weights apply to new bots rather than changing saved types.
- The Danger tooltip now explains that it affects existing Hunters after a
  restart, including the increased grey-target aggression of Full Camlann.

## [0.61.0] - 2026-09-25

### Added

- Darkness Falls destinations for autonomous XP parties and Hunters, backed by
  1,417 entrance-specific round-trip navigation proofs. A hash-checked cached
  repair connects the nine entrance stairs while preserving all other mesh
  tiles and the installed navigation file.
- An offline DF audit/patch-packaging tool and a Camlann activity review with
  read-only live-session findings and pending real-client checks.

### Changed

- Hunters choose dungeons on 30% of eligible destination draws. Darkness Falls
  gets twice the destination weight within the dungeon share for XP and hunts;
  ordinary pickup groups can select proved camps in connected dungeons.
- Launcher charter/activity labels and population-setting explanations use PvP
  while retaining compatible saved charter and objective identifiers.

### Fixed

- Frontier first-strike scans respect PvE assignments, recovery and opposing
  party strength while retaining real defense and committed siege combat.
- Hunter patrol dwell time starts on arrival rather than departure; dungeon
  arrival checks include height, and generic hunt scans reject stealthed targets.
- Gamebot path smoothing retains DF stair-link endpoints, including companions
  following over the previously disconnected Midgard entrance stairs.

### Removed

- The separate shared-dungeon aggression scan that bypassed assigned PvE work.

## [0.60.0] - 2026-09-25

### Added

- A client-only Join Friend launcher mode (`--join` and a Windows shortcut) for
  connecting to a host's Tailscale IPv4 address without starting the local
  server or opening the local world save. Source implementation awaits a real
  two-home client check.

### Changed

- Launcher deployment now installs `Join Friend.cmd` with backup and rollback
  handling for the new file.

### Fixed

- None.

### Removed

- None.

## [0.59.2] - 2026-09-25

### Added

- A fast implementation plan for private two-home Tailscale co-op, with a
  client-only guest launcher path and live verification in both hosting directions.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.59.1] - 2026-09-25

### Added

- A dedicated `docs/TASKS.md` for rapid-fire tasks and ideas, with open,
  verification-pending, and finished states and explicit Done records.
- Agent tracking rules and README links for the bug and task lists.

### Changed

- Moved the autonomous-bot dungeon review from the Tasks section in
  `docs/BUGS.md` to `docs/TASKS.md`, preserving its scope and open status.

## [0.59.0] - 2026-09-25

### Added

- Autonomous PvE pickup groups can match compatible bots across all three realms
  and prefer a viable mixed-realm party. Remote members travel through an active
  `AllRealmsTeleporter` to the leader's town rendezvous; the Active Groups card
  shows their 45-minute simulated-time meetup deadline, which starts when the
  remote group forms.

### Changed

- Pickup groups compare a bounded shortlist of live camps across up to three
  regions, then elect a nearby leader and stage in a connected town. Camp
  suitability, crowding, travel, familiar locations, and known deaths affect the
  choice. Mixed-realm parties are preferred within a 20-minute estimated meetup
  journey and a five-minute detour over a viable nearby party.
- Remote members start traveling while the leader stages. Productive, healthy
  ordinary PvE parties can continue together for one additional task when no
  member needs training or inventory services; camps are reconsidered at renewal.
- PvE visitors stay where their activities leave them, including during town
  downtime and after save reload. Explicit post-RvR returns still go home, and
  completing the journey preserves the separate requirement to finish PvE.

### Fixed

- Remote invitations validate the complete porter approach and onward town
  journey, including intervening zone crossings. Arrival towns are chosen near
  the actual rendezvous; unreachable nearby porters and local applicants no
  longer prevent usable alternatives from joining. Travel requires real porter
  proximity, life, and no combat, without a catch-up teleport.
- Post-group location no longer depends on mutable activity text. Removing a
  no-show immediately refreshes party roles and preserves prior attendance when
  formation slots shift at the expired shared deadline. Remote meetups use a
  shared 45-minute simulated-time deadline; local-only meetups remain 15 minutes and the leader
  staging limit remains 20 minutes. The shared 45–120-minute task still starts
  on camp arrival, with its separate 30-minute camp travel window.

### Removed

- None.

## [0.58.0] - 2026-09-25

### Added

- A separate 30-minute simulated-time travel window for autonomous PvE parties after choosing a camp. The Active Groups card shows its remaining travel time.

### Changed

- The shared 45–120-minute PvE task starts when a party reaches its camp instead of when the leader chooses the camp. Subsequent recovery and camp replanning still consume the original task duration.

### Fixed

- Long outbound trips no longer exhaust a group's fighting time before it reaches the camp. A party that misses its travel window sends its members to solo PvE before they can seek another group.

### Removed

- None.

## [0.57.0] - 2026-09-25

### Added

- World Speed control in the launcher for 1×, 2×, and 3× live-world simulation while no game client is connected, with selected, effective, and achieved speed status.

### Changed

- Gameplay clocks and saved deadlines advance with completed world ticks at the selected speed. A connecting client restores 1×; the selected speed resumes after the last client disconnects. Each server start selects 1×.
- Simulated time is checkpointed with the local save and resumes through server downtime at 1×.

### Fixed

- Launcher bot-task and bot-auction countdowns use simulated time during and after accelerated sessions.

### Removed

- None.

## [0.56.0] - 2026-09-24

### Added

- None.

### Changed

- Bound starter equipment on saved companions is refreshed as they level while earned and manually equipped items remain intact.
- Tanks prioritize attackers of healers, then bomb casters, then group leaders when peeling adds.
- Mixed-realm group equipment drops use one selected member's class and realm; autonomous group and raid drops go first to members who can equip an upgrade.

### Fixed

- New persistent companions and loaded autonomous bots fill missing armor and appropriate shield slots even when the normal item tables are sparse.
- PvP NPC creation and group membership changes maintain allied gamebot name colors and Tab targeting status without marking hostile gamebots friendly.
- An urgent peel can interrupt a tank's offensive cast against a different target.

### Removed

- None.

## [0.55.0] - 2026-09-24

### Added

- None.

### Changed

- Pickup-group formation now applies the same role-adjusted monster-level limit as its camp planner, so incomplete parties do not form for camps they cannot use.
- Solo death recovery below level 20 keeps the safest non-grey target con while favoring nearby home-realm camps.

### Fixed

- Autonomous world bots below level 10 stay protected from player-shaped attacks even after a stale RvR safety opt-in. Direct attack and damage paths also protect the bot and its controlled pets; monster combat and player companions keep their existing rules.

### Removed

- None.

## [0.54.0] - 2026-09-24

### Added

- Pickup groups check the live local camp catalog before forming.

### Changed

- Solo bots below level 20 favor camps within ten estimated travel minutes in
  their home realm, weighing travel and crowding together across nearby zones.
  Ordinary leveling trips use at most two stable hops; bots walk to the next
  camp after release.
- Autonomous release prefers a validated bind point in the death zone, and
  movement or goal-stall recovery prefers a bind in the current leveling zone
  before falling back to the capital.

### Fixed

- A group with no usable camp dissolves after its planner exhausts the level
  fallbacks. Recovery no longer keeps a fully rested, targetless group idle.

### Removed

- None.

## [0.53.0] - 2026-09-24

### Added

- `AUTONOMOUS_PVP_ENGAGE_SUMMARY`: a per-minute count of new PvP fights that
  involve autonomous bots, grouped by how the first blow was struck and by the
  attacker's level band and player type.
- `pvpDeaths` and `pveDeaths` on `AUTONOMOUS_ACTIVITY_SUMMARY`.

### Changed

- Autonomous bots below level 10 have the same implicit PvP safety as a
  flagged player until an RvR assignment relinquishes it (review phase A1).
- Guild kill-on-sight lists skip grey killers, fights the victim started, and
  bot-on-bot kills below level 10. A KOS target is hunted first but still has to
  pass the level, grey, and party-strength checks.
- Outside RvR tasks, bot crowd control targets only opponents already in the
  fight and does not use area mezzes.
- A PvP death no longer lowers a bot's PvE target difficulty. The
  three-PvP-deaths wall applies only to RvR tasks and to level 10+ types other
  than Leveler and Casual.

### Fixed

- Guild kill-on-sight rows are written in batches on a timer thread instead
  of synchronously during bot death processing, which stalled the reaper
  tick for 30–80 ms on each bot-on-bot kill.

### Removed

- None.

## [0.52.1] - 2026-09-24

### Added

- `docs/AUTONOMOUS_BOT_M7_REVIEW.md`: first live review of the autonomous bot
  roadmap on a 1,500-bot fresh launch, with a phased improvement plan.

### Changed

- The autonomous bot roadmap links the review as the first M7 input.

### Fixed

- None.

### Removed

- None.

## [0.52.0] - 2026-09-24

### Added

- None.

### Changed

- Companion buff coverage now checks active buffs from other group members on the target; a human player's known spell alone no longer suppresses a base buff.
- Guard and Protect assignments honor their native effect ranges, including existing effects from outside the managed companion group.
- Bombing's tank-aggro grace period restarts when the focused pull target changes.

### Fixed

- Idle player-led companions no longer repeat the same pet summon without an owner-level increase that improves the pet.

### Removed

- None.

## [0.51.0] - 2026-09-24

### Added

- None.

### Changed

- None.

### Fixed

- Player-led companions keep an idle pet summoned by the preferred spell until they learn a stronger summon or gain enough levels to improve the pet.

### Removed

- None.

## [0.50.0] - 2026-09-24

### Added

- M6 autonomous social behavior: player-type chat, guild banter, post-fight
  taunts, trade and LFG lines, invented charter-themed guild names, and
  common/joking character-name styles.
- Guild KOS memories that include the player, expire after three hours, respect
  worth and safe-area rules, and direct existing RvR crews toward reachable
  targets.
- A blocklist for slurs in authored autonomous chat and generated names while
  leaving ordinary profanity available.

### Changed

- Autonomous guild and faction conversations use existing chat rate limits and
  only speak to audiences with permission to hear the channel.

### Fixed

- Exchange advertisements use channel-appropriate wording, and generated bot
  replies pass through the chat safety policy.

### Removed

- None.

## [0.49.0] - 2026-09-24

### Added

- Plain-language tooltips for server population presets, player-type mix,
  leveling-zone danger, and world-shape controls.
- A saved per-companion Auto/Bomb/Off preference for PBAoE spell use.

### Changed

- Companion buff maintenance recognizes stronger learned ranks, prioritizes
  specialization buffs, and assigns Guard and Protect across group members.
- Idle player-led companions upgrade to stronger summons; Enchanters prefer
  Underhill Ally when available.
- Bomb-capable companions prioritize PBAoE on sufficiently large focused pulls
  and allow tanks a brief aggro window before bombing.

### Fixed

- Mob BAF resolves player-led companions and controlled pets to their group,
  counts bot members for add selection, and retains the related experience bonus.

### Removed

- None.

## [0.48.0] - 2026-09-24

### Added

- Expanded `docs/BUGS.md` with the reported companion, dungeon, and server-population issues.

### Changed

- Ignore generated server build output and its NuGet/MSBuild cache files.

### Fixed

- Restored 2,310 archived monster rows across all Classic realm dungeons and the supported Old Frontiers dungeons through an additive Setup migration.

### Removed

- None.

## [0.47.0] - 2026-09-24

### Added

- None.

### Changed

- None.

### Fixed

- All-realm travel uses standard fallback routes when an advertised
  destination is missing from the installed teleport table. Menu labels
  with full Shrouded Isles names and trailing spaces now resolve correctly.

### Removed

- None.

## [0.46.1] - 2026-09-24

### Added

- `docs/BUGS.md` starts a list of confirmed, unresolved bugs.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.46.0] - 2026-09-24

### Added

- `/companions reset [name]` recreates active persistent companions at the
  owner's current location, saving their roster progress and gear first.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.45.0] - 2026-09-24

### Added

- Established-server bot creation across five level bands with level-matched
  starting experience, class equipment, first-login specialization training,
  and realm points for new level-50 bots.
- Configurable level-1 guild alts, limited by a total-roster cap.
- Local population samples at stable 500, 1,000, and 1,500 active bots for a
  measured launcher size recommendation after all three runs.

### Changed

- Add crew follows Fresh launch or Established world shape. The explicit Add
  Lv.50 action remains available, and existing bot progress is retained.
- The launcher withholds a numeric roster recommendation until this PC has
  all three measured memory and game-loop tick samples.

### Fixed

- New level-1–49 bots receive class-appropriate generated starting gear without
  depending on the world's sparse lower-level item templates. A missing
  level-50 class loadout still rolls back its creation batch.

### Removed

- The earlier unbenchmarked numeric population-size estimate.

## [0.44.0] - 2026-09-24

### Added

- A Server population launcher screen with named presets, six player-type
  percentages, leveling-zone danger, world shape, and an advisory roster-size
  suggestion based on CPU cores and memory.
- Player type, guild, and guild charter columns in Active Population.

### Changed

- `bot-goals.json` version 2 stores the population mix, danger, and world shape.
  The server reads it at startup; new autonomous identities and guild charters
  use the selected mix. Version-1 goal weights map to the nearest preset for
  launcher review before saving.

### Fixed

- Population settings cannot be saved with a mix other than 100%, while the
  server is running, or over a valid file after a failed write.

### Removed

- The old Bot Goals Setting sliders and their objective-exclusion behavior.

## [0.43.0] - 2026-09-24

### Added

- Type-specific Hunter patrols, Roamer frontier loops and group sizes, Keep
  warrior campaigns from level 35, and a server danger setting for leveling
  zones.

### Changed

- Hybrid roams favor local evening hours; Casuals take town breaks more often.
  Level-50 bots with weak weapons or armor favor gear farming and dungeons.
- Hunter patrols favor occupied, level-appropriate outdoor camps and nearby
  routes while retaining occasional quiet-area visits.

### Fixed

- Rare grey-target engagement now uses one stable chance per target and
  ten-minute window instead of rerolling every combat scan.

### Removed

- None.

## [0.42.0] - 2026-09-24

### Added

- Persistent player type, five temperament traits, PvE wall state, and managed
  guild charter rows for autonomous bots.

### Changed

- Generated guilds receive invented names and durable managed markers. New
  autonomous tasks follow each bot's type, level phase, wall state, and guild
  raid reservation while existing task timers continue.

### Fixed

- Guild consolidation and rename paths persist keep ownership and alliance
  reference updates before deleting or renaming generated guilds.

### Removed

- Random per-guild level-bucket task allocation and the visible `Camlann Crew`
  prefix on managed guilds.

## [0.41.0] - 2026-09-24

### Added

- Per-minute autonomous activity counts and live outdoor camp pressure signals.

### Changed

- Ordinary PvE bots form local pickup groups across guilds and realms. Groups
  depart with two arrivals, accept late followers, and preserve the party and
  camp after a single death while the released member returns.
- Outdoor camp selection softly favors less crowded and recently productive
  spawns, while keeping every valid destination eligible.

### Fixed

- Level 1–4 Flexible-build Reavers use Slash until their Flexible ability
  unlocks at level 5, then equip their planned weapon.

### Removed

- Automatic open-world ganking by ordinary PvE parties and whole-party
  disbanding when only some meetup members fail to arrive.

## [0.40.0] - 2026-09-24

### Added

- Focused XP checks for autonomous bots, temporary helpers, companion catch-up,
  and PvP con-color rewards.

### Changed

- PvP kill XP now uses the player's `XP_RATE` or the autonomous bot's
  `BOT_XP_RATE`. Challenge XP and realm-point bonuses follow the victim's con
  color: orange 1.25×, red 1.5×, purple 2×.
- A companion's NPC kill XP uses the smaller of its owner's base award and its
  own-level cap, then `XP_RATE` and a 1.5× catch-up boost when five or more
  levels behind. The owner's total XP remains the final ceiling.
- Updated the autonomous roadmap and companion reward guide; synchronized the
  launcher, launcher-test, and command-reference version pins.

### Fixed

- Autonomous bots and temporary `/spawn` helpers again receive their configured
  XP multiplier on mob kills. Autonomous bots also regain zone and item XP
  bonuses.

### Removed

- Unused autonomous XP scaling method.

## [0.39.0] - 2026-09-24

### Added

- A slot sheet in the Companion Manager's Gear tab. It lists all 19 worn slots
  in character-sheet order, including empty ones, and marks slots where the
  companion's bag holds items that fit (`2 fit`) or a better item
  (`upgrade in bag`).
- Clicking a slot opens it: the worn item with its stats, and every bag item
  the companion can equip there, best first, with its score change.
  **[Equip + lock]** equips the selected item in that slot (a ring or bracer on
  the side you clicked); **[Unequip]**, **[Lock slot]**, and **[Keep]** act on
  the worn item. Click the slot again to close it.
- A test for the slot sheet order and paired ring and wrist matching.

### Changed

- The slot sheet replaces the Gear tab's list of worn items. The backpack
  list below it keeps its **[Equip + lock]**, **[Return to me]**, and
  **[Keep]** actions.
- Benched companions show all worn slots, including empty ones.
- Updated the Companion Manager roadmap, the integration handoff, and the
  command references.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- The worn slots at positions 51-69 of the companion bag window (0.38.0). The
  bag window shows only the 40 backpack positions again, and the drag-to-equip
  and drag-to-unequip rules that came with it are gone.

## [0.38.0] - 2026-09-24

### Added

- Worn slots in the companion bag window (M3). The bag window now also shows
  the companion's worn equipment at positions 51-69, two per row: helm, chest,
  arms, gloves, legs, boots, cloak, neck, jewel, belt, left and right wrist,
  left and right ring, right hand, left hand, two-handed, ranged, and mythical.
  Positions 1-40 are still the backpack.
- Drag and drop to equip: drop a companion bag item on a worn position to
  equip and lock it (like **[Equip + lock]**). The item goes to its own slot;
  a ring or bracer goes to the side it was dropped on. Drag a worn item to an
  empty companion bag slot to unequip it there (like **[Unequip]**).
- A test for the vault layout: backpack and worn positions, no overlap, and
  paired rings and wrists in one row.

### Changed

- The Gear tab and the **[Open bag]** message name the worn positions.
- The equip message now names the slot the item went to.
- Updated the Companion Manager roadmap, the integration handoff, and the
  command references.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- None.

## [0.37.0] - 2026-09-24

### Added

- Group orders in the Companion Manager (M2). A **Group orders** row leads the
  roster list. It sets the group order (Aggressive, Defensive, Passive, or
  saved stances) and shows each grouped companion's effective stance, with
  its saved stance when the order overrides it.
- Group actions in that row: **[Pull]** (like `/pull`), **[Invite all]**,
  **[Bench all]**, and **[Grind]**/**[Stop grind]** (like `/grind`).
  **[Invite all]** invites the benched companions shown in the list, top to
  bottom, until the group is full, so search and filters choose who comes.
  **[Bench all]** benches every active companion.
- A test for the group row: order links, per-companion override text, and
  the pull, invite-all, and grind refusal messages.

### Changed

- `/aggressive`, `/defensive`, `/passive`, `/companions group default`, and
  `/pull` now share their code with the window; their behaviour and messages
  are unchanged. `/grind` still accepts only temporary `/spawn` helpers.
- Updated the Companion Manager roadmap, the integration handoff, and the
  command references.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- None.

## [0.36.0] - 2026-09-24

### Added

- Crowd control role for companions (M1c). Healer, Sorcerer, Bard, Mentalist,
  and Spiritmaster companions can take it with `/companions role <name> cc`
  or in the manager's Training & Tactics tab.
- PvE add control. Once the group is fighting a monster, a companion with
  crowd control duty mezzes extra monsters that are attacking the group,
  with non-tanks' attackers first. It never mezzes the group's target or the
  owner's target, and it skips monsters that are immune, below 75% health,
  or taking damage over time. Two companions never mezz the same add, and a
  mezz is recast after it wears off.
- Mezz protection in player-led groups. Companions leave a mezzed monster alone
  while any other enemy is left, and skip area spells that would hit it. If
  the owner attacks the mezzed monster, companions attack it too.
- Each build sets a role when it is chosen or used at recruitment, following
  the owner's Healer mapping: Tri-spec is Healer and also controls adds,
  Mending is Healer, Augmentation is Buffer, and Pacification is Crowd
  control. Sorcerer Body and Mind and Bard Music are Crowd control. Bard
  Nurture and Shaman Augmentation are Buffer, Friar Group support is Healer,
  and Armsman Two-handed is Attacker. The other builds keep the class default.
- Tests for the role values, the build-to-role mapping, and the `cc` role
  command.

### Changed

- Build lists in the window and in `/companions build` say which role each
  build sets. Role names show as "Crowd control" rather than the saved value.
- Class role labels include "Crowd control" for the five classes that can
  fill it.
- A Pacification Healer in the Crowd control role keeps healing when nothing
  needs control.
- Updated the Companion Manager roadmap, the integration handoff, and the
  command references.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- A support companion no longer cancels its own crowd-control cast while
  holding back from melee.

### Removed

- None.

## [0.35.0] - 2026-09-24

### Added

- Build list in the Companion Manager (M1b). The Training & Tactics tab lists
  every build for the selected companion with its role and marks the current
  one. Selecting a build shows its level-50 targets. **[Use build]** switches
  to it with the M1a rules: free, no trainer, reset and retrain to the current
  level. It works for active and benched companions.
- Build choice in the manager's recruit flow. Selecting a story companion or a
  class lists its builds with the class default preselected; **[Recruit]** or
  **[Create]** uses the selected build. Manual-only classes say so.
- `/companions recruit authored <name> [build]` recruits a story companion
  with a chosen build.
- Integration test for the window's build list, **[Use build]**, and build
  choice during recruitment.

### Changed

- The manager's Overview tab names the build that automatic training follows.
- Updated the Companion Manager roadmap, the integration handoff, and the
  command references.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- The Training & Tactics tab no longer tells players to switch builds with a
  command. `/companions build` still works.

## [0.34.0] - 2026-09-24

### Added

- Build choice for persistent companions (Companion Manager M1a). The catalog
  now holds 57 named, validated builds for 35 classes, for example Healer
  Tri-spec, Mending (healer), Augmentation (buffer), and Pacification (crowd
  control), or Spiritmaster Darkness (bomb), Suppression, and Summoning (pet).
- `/companions build <name>` lists a companion's builds and marks the current
  one. `/companions build <name> <build>` switches builds for active or benched
  companions: it is free, needs no trainer or respec eligibility, resets that
  companion's specializations, and retrains the new build to its level.
- `/companions recruit <class> [build]` recruits with a chosen build.
- Automatic builds for Wizard (Fire, Ice, Earth) and Animist (Creeping,
  Arboreal), which were manual-only.
- Research record for the 24 added builds, each labelled sourced, adjusted, or
  project recommendation, and tests for every build through level 50, build
  switching, and rejected build choices.

### Changed

- Automatic level-up training follows the companion's saved build instead of
  only the class default. The 33 original `general-pve-v1` plan IDs are
  unchanged and remain each class's default build, so existing saves need no
  migration.
- `/companions mode <name> automatic` keeps a companion's valid saved build.
- `/companions plan <name>`, `/companions list`, profiles, and the manager's
  Training & Tactics tab show build names and the build keys.
- Updated the Companion Manager roadmap: the M1 switch-cost and trainer
  decisions are recorded, and M1 is split into M1a, M1b, and M1c.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- None.

## [0.33.0] - 2026-09-24

### Added

- A separate, hash-guarded raid click-to-target client patch: a stage builder
  (`build_raid_click_fix_client.py`), an offline emulation test, and
  `tools/dev/Install-RaidClickFix.ps1` (dry run by default, backup, rollback,
  restore). It changes only the four raid window XML files and never
  `game.dll`.
- Companion Manager labels 130 and 131 for the detail `[Up]` and `[Down]`
  links, in a rebuilt manager `game.dll`.

### Changed

- The detail `[Up]` and `[Down]` links in the Companion Manager now appear
  only in the direction that can scroll. A 0.32.1 client ignores the new labels
  and keeps its static links.
- Recorded that the companion bag's "House Vault 1" caption comes from a fixed
  client string; the server can change only the number.
- Updated the Companion Manager roadmap and integration handoff. The M0 items
  passed offline checks and wait for the owner's real-client check.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- Clicking a member in the 40- or 80-person raid window did nothing. The raid
  XML used event names that the client's parser ignores; both raid builders
  and the new patch now use the numeric IDs `1536`–`1615`, which the existing
  raid handler turns into target selection.

### Removed

- None.

## [0.32.4] - 2026-09-24

### Added

- An autonomous bot behaviour roadmap (`docs/AUTONOMOUS_BOT_ROADMAP.md`): a
  review of the current population system, findings on the slowdown around
  levels 10–12, Camlann and Mordred research, the owner's design decisions,
  and milestones for player types, guild charters, the launcher's population
  settings, rewards, and chat.

### Changed

- Linked the roadmap from the README and the Camlann plan.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None. The roadmap records, but does not yet fix, two XP bugs: autonomous bots
  and `/spawn` helpers have received 1× XP from mob kills since 0.24.0, and a
  low-level persistent companion receives its owner's full kill award.

### Removed

- None.

## [0.32.3] - 2026-09-24

### Added

- None.

### Changed

- Recorded the owner's acceptance of persistent companion Stage 3, Stage 5,
  and companion-window checks. Stage 4 gear and Stage 6 integration checks
  remain open.
- Moved the completed development setup plan to `docs/completed/DEV-SETUP.md`
  and updated its references.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- None.

## [0.32.2] - 2026-09-24

### Added

- None.

### Changed

- Recorded the owner's build-selection decisions in the Companion Manager
  roadmap: research the popular 1.65 builds per class, switch builds with an
  automatic respec and retrain, let builds set roles (including a new crowd
  control role), and choose a build when recruiting.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- None.

## [0.32.1] - 2026-09-24

### Added

- The client patch test now emulates the client's own `OnClickEvent` parser on
  every window click value and checks which parser function reads it.
- A Companion Manager roadmap: close-out checks, the raid click fix, build
  selection with automatic training, behaviour controls, and a companion
  equipment slot layout.
- The owner's real-client gate result: clicking and search work in 0.32.1.

### Changed

- The Companion Manager client patch no longer hooks the `ControlId` name
  mapper; the raid's patch bytes there are left unchanged.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- Companion Manager buttons did nothing in the real client. The window now uses
  numeric click event IDs, which the client's XML parser accepts, instead of
  custom event names, which it silently ignored.

### Removed

- None.

## [0.32.0] - 2026-09-24

### Added

- A native Companion Manager window (`Custom8`): Roster and Recruit lists with
  realm and role filters, name/class search, and scrolling; Overview, Training
  & Tactics, and Gear details; invite, bench, recruit, tactics, training,
  respec, gear actions, and the native companion bag.
- `/companions find <name or class>` search. The window's **[Search]** link
  opens the chat line with it prefilled.
- A hash-guarded client patch builder, offline x86 emulation test, and dry-run
  installer with rollback (`tools/dev/Install-CompanionManager.ps1`). Nothing
  is installed automatically.

### Changed

- Manager clicks travel through the client's own slash-command path, and search
  uses the ordinary chat line. This replaces the probe's failed action packet
  and edit box. Every click is resolved against a server-held session and
  current roster; stale clicks are refused.
- Bare `/companions` opens the manager and prints one line of command guidance
  when no patched client answers. All subcommands are unchanged.
- Companion gear operations moved into a shared helper used by the manager and
  the legacy menu. The respec start is shared with the manager.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- None.

## [0.31.1] - 2026-09-23

### Added

- A Companion Manager integration handoff recording verified client controls,
  the failed action/search gate, server reuse, safe packaging, and acceptance.

### Changed

- Linked the handoff from the companion roadmap and synchronized version pins.

### Fixed

- None.

### Removed

- None.

## [0.31.0] - 2026-09-23

### Added

- A passive companion group order and saved individual stance to drop combat,
  recall pets, and regroup without attacking.

### Changed

- Aggressive and defensive companions break pursuit when more than 2100 units
  from their leader and return within 650 units before fighting again.
- Bare `/companions` now gives concise command guidance while the native
  window action and search controls remain unresolved.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- Companions no longer keep pursuing distant fights after their leader leaves.

### Removed

- None.

## [0.30.1] - 2026-09-23

### Added

- A hash-guarded, staged Custom8 client-control probe and offline x86 hook
  checks, with a review brief for the required in-client gate.

### Changed

- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- None.

## [0.30.0] - 2026-09-23

### Added

- None.

### Changed

- New player characters retain the class selected during character creation
  from level 1, with starter equipment assigned for that class when configured.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- None.

### Removed

- The startup class rewrite. A saved `start_as_base_class` property is left in
  the database but no longer changes newly created characters.

## [0.29.0] - 2026-09-23

### Added

- A native external-inventory view for an active companion's backpack. Item
  inspection and drag/drop transfers use the existing protected companion
  ownership and atomic save path.
- Owner-reported Companion Step 2 UI findings and real-client retest tasks in
  the roadmap and acceptance brief.

### Changed

- Split the companion gear menu into short equipment and backpack pages, moved
  slot actions to item detail, and clarified generated versus authored recruits.
- Reopen the NPC conversation for each page and keep older visible choices
  bound to their original actions during the menu session.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- Removed hexadecimal action tokens from visible companion menu links.

### Removed

- None.

## [0.28.0] - 2026-09-23

### Added

- Focused disposable-SQLite Stage 6 integration coverage and an owner-run
  companion acceptance brief.

### Changed

- Recorded the existing GameBot death-recovery policy and temporary-helper-only
  `/raid 40|80` policy in the companion roadmap and player guide.
- Synchronized launcher, launcher-test, and command-reference version pins.

### Fixed

- Kept remembered defensive pull targets range-gated, so companions wait until
  distant PvE and PvP targets enter the 350-unit defensive radius.

### Removed

- None.

## [0.27.0] - 2026-09-23

### Added

- Two authored companions for each of the 39 Classic + SI classes, with stable
  identities, appearances, backgrounds, personalities, and dialogue.
- Persistent individual role and engagement preferences, authored-cast browsing,
  character profiles, and private menu and command controls.

### Changed

- Personality supplies initial engagement behavior; direct player orders and
  temporary group orders take precedence. Existing recruits retain their saved
  identity and class behavior.
- Updated companion roadmap, decision record, player commands, and version pins.

### Fixed

- Group engagement orders now apply to persistent companions as well as
  temporary helpers, while autonomous bots remain separate.

### Removed

- None.

## [0.26.0] - 2026-09-23

### Added

- Persistent automatic companion training with 33 runtime-validated, versioned
  project-recommended builds; six unsupported classes remain manual-only.
- Personal class-legal PvE/PvP companion gear rewards and a private clickable
  roster, training, equipment, and inventory menu.

### Changed

- Companion inventory now tracks starter, earned, and player-supplied gear,
  manual slot locks, keep flags, safe transfers, and positive-value surplus sales.
- Equipment changes, item ownership, sale removal, and owner coin proceeds use
  atomic database transactions. Updated companion roadmap, contract, research,
  decision records, and command help.

### Fixed

- Companion rewards and automatic upgrades preserve displaced items, respect
  manual locks and protected provenance, and roll back inventory and coin state
  together when persistence fails.
- Personal reward rolls no longer depend on whether the owner's XP bar advances;
  player loot shares and autonomous-bot reward handling retain their own paths.
- Persistent companions now follow accepted owner teleports and refresh their
  displayed group level as soon as they level up.
- Equipment saves use a consistent lock order. Manual equipping accepts legal
  weaker items, and tied accessory slots keep one choice through validation.

### Removed

- None.

## [0.25.0] - 2026-09-23

### Added

- None.

### Changed

- Launcher, command-reference, and launcher-test version pins identify 0.25.0.

### Fixed

- Reward-eligible autonomous-bot PvP deaths now generate the same guaranteed
  class-appropriate gear drop as human-player deaths, retaining existing
  contributor eligibility, repeat-kill protection, and automatic pickup.

### Removed

- None.

## [0.24.2] - 2026-09-23

### Added

- Project-specific fast local R&D shipping rules: direct fork merge/push and
  server/launcher deployment to the owner's local game installation.

### Changed

- Shipping builds both components and pre-authorizes stopping the target
  installation before deployment, retaining backups and save protections.
- Development guidance skips PR, CI, Docker, automated test, and monitoring
  gates during R&D; gameplay verification remains local and owner-driven.
- Launcher, command-reference, and launcher-test version pins identify 0.24.2.

### Fixed

- Generic merge guidance no longer makes unused upstream Docker tooling a
  prerequisite for this project's Windows development loop.

### Removed

- None.

## [0.24.1] - 2026-09-23

### Added

- Class-by-class Stage 3 build research and validation status for all 39
  companion classes, including static career/skill checks, no-autotrain point
  budgets, sourced milestone routes, and explicit blockers.

### Changed

- Companion plan commands now explain that static candidates await disposable
  runtime validation and owner selection. The roadmap and Stage 3 decision
  record distinguish static research from runtime and real-client acceptance.
- Launcher, command-reference, and launcher-test version pins now identify
  0.24.1.

### Fixed

- None.

### Removed

- None.

## [0.24.0] - 2026-09-23

### Added

- Persistent companions earn eligible NPC PvE experience and can spend saved
  specialization points through `/companions train` or reset them with
  `/companions respec`.

### Changed

- Companion damage and controlled-pet damage use a separate reward path that
  preserves player XP shares, group counts, and loot ownership. Companion XP
  progress saves through a coalesced record-only queue.
- The companion roadmap and command reference now describe Stage 3 policies and
  the automatic-plan validation gate. Launcher and command-reference pins now
  identify 0.24.0; the launcher presentation test pin matches.

### Fixed

- Persistent companion NPC rewards no longer flow through PvP XP or realm-point
  paths, and companion pet damage remains attributed to the companion.

### Removed

- None.

## [0.23.2] - 2026-09-23

### Added

- Companion Stage 3 code audit and progression/training decision brief with
  proposed XP, catch-up, respecialization, and realm-point policies.

### Changed

- Companion roadmap records Stage 3 decision prep; owner policy selections
  remain open. Launcher, command-reference, and test version pins now identify
  0.23.2. This documentation change does not alter gameplay.

### Fixed

- None.

### Removed

- None.

## [0.23.1] - 2026-09-23

### Added

- None.

### Changed

- Companion roadmap, decision brief, and feature verification notes now record
  owner-confirmed Stage 2 runtime acceptance and current offline checks.
- Launcher, command-reference, and test version pins now identify 0.23.1.

### Fixed

- None.

### Removed

- None.

## [0.23.0] - 2026-09-23

### Added

- None.

### Changed

- None.

### Fixed

- Persistent companion invites now save newly generated unique gear templates
  with the companion inventory, allowing recruits to join and retain their gear.

### Removed

- None.

## [0.22.0] - 2026-09-23

### Added

- Companion roster invite and bench commands accept companion names, and the
  roster lists names grouped by realm without showing internal IDs.

### Changed

- `/companions recruit <class>` resolves a class from any realm; `/classes`
  lists class names grouped by realm without role descriptions.
- Launcher, command-reference, and test version pins now identify 0.22.0.

### Fixed

- Albion class listings now show “Albion” instead of the `_FirstPlayerRealm`
  enum alias.

### Removed

- None.

## [0.21.0] - 2026-09-23

### Added

- Persistent per-character companion records with stable IDs, saved identity,
  specialization/build state, and namespaced companion inventories.
- `/companions list`, `recruit`, `invite`, and `bench` flows for free level-1
  Classic + SI recruits, with 78 stored roster slots per character.
- Active persistent companions restore on login; failed invitations keep the
  roster entry, and full groups leave companions benched.

### Changed

- Player group departure, explicit removal, and group disband save and bench
  persistent companions. `/spawn` remains temporary, and legacy saved bot
  profiles are left untouched without conversion.
- Launcher, command-reference, and test version pins now identify 0.21.0.

### Fixed

- Companion equipment is saved under its own inventory owner ID, preventing
  roster gear from sharing a player's or autonomous bot's inventory key.

### Removed

- None.

## [0.20.7] - 2026-09-23

### Added

- Stage 2 companion decision brief grounded in current roster, menu, and
  persistence code, with recommendations and unresolved owner choices.

### Changed

- Companion roadmap now records Stage 2 decision preparation; gameplay
  and save behavior remain unchanged. Launcher, command-reference, and
  test version pins now identify 0.20.7.

### Fixed

- None.

### Removed

- None.

## [0.20.6] - 2026-09-23

### Added

- Companion Stage 1 acceptance record with per-character ownership and additive
  save-boundary recommendations, compatibility limits, and menu design notes.
- Level-by-level source coverage, the nonportable Minstrel milestone, two
  classes without numeric templates, and three unvalidated forum build variants.

### Changed

- Companion roadmap Stage 1 is complete to its documented proposal-or-gap
  criteria; runtime database and real-client checks remain separate gates.
- Launcher, command-reference, and test version pins now identify 0.20.6.
  This documentation task does not change gameplay.

### Fixed

- None.

### Removed

- None.

## [0.20.5] - 2026-09-23

### Added

- First-pass sourced leveling and endgame build proposals or explicit research
  gaps for all 39 companion classes, with dated Healer and Necromancer forum
  supplements.
- Static career, skill-table, and point-budget cross-checks for the candidate
  builds, including four no-autotrain companion adjustments.

### Changed

- Companion Stage 1 remains in progress: five exact endgame templates, most
  per-level milestones, local runtime database confirmation, and Stage 2 roster
  decisions remain open.
- Launcher, command-reference, and test version pins now identify 0.20.5.
  This documentation task does not change gameplay.

### Fixed

- Mapped the Theurgist guide's Air line to the server career key Wind Magic.

### Removed

- None.

## [0.20.4] - 2026-09-22

### Added

- Dated companion class-roster reconciliation: 33 base classes plus six
  Shrouded Isles additions, matching the 39-class runtime catalog.

### Changed

- Companion Stage 1 class-era roster checkpoint is complete; per-class build
  proposals and server allocation validation remain open.
- Launcher, command-reference, and test version pins now identify 0.20.4.
  This documentation task does not change gameplay.

### Fixed

- None.

### Removed

- None.

## [0.20.3] - 2026-09-22

### Added

- A first-pass audit of 1.65 class-era sources and companion build-guide
  coverage, including the missing Necromancer entry and source limitations.

### Changed

- Companion Stage 1 remains in progress; dated per-class proposals and server
  allocation validation are still required.
- Launcher, command-reference, and test version pins now identify `0.20.3`.
  This documentation task does not change gameplay.

### Fixed

- Corrected the companion design notes: the three Uthgard realm guides cover
  38 of the 39 runtime classes, not all 39.

### Removed

- None.

## [0.20.2] - 2026-09-22

### Added

- Stage 1 companion design notes with a provisional 39-class catalog and an
  audit of existing persistence, reward, inventory, and client-menu boundaries,
  plus an initial assessment of 1.65-era build-research sources.

### Changed

- Companion roadmap Stage 1 is in progress; sourced class-build research and
  final roster decisions remain pending.
- Launcher, command-reference, and test version pins now identify `0.20.2`.
  This documentation task does not change gameplay.

### Fixed

- None.

### Removed

- None.

## [0.20.1] - 2026-09-22

### Added

- Persistent companion roadmap with agreed goals, six implementation stages,
  decision gates, and separate offline and real-client acceptance checks.

### Changed

- README and current companion documentation link to the proposed roadmap.
- Launcher, command-reference, and test version pins now identify `0.20.1`.
  This documentation task does not change gameplay.

### Fixed

- None.

### Removed

- None.

## [0.20.0] - 2026-09-22

### Added

- Reward-eligible player PvP kills now produce one guaranteed class-appropriate
  gear drop for the credited killer; autonomous bots collect it and equip usable
  upgrades.

### Changed

- Launcher, command-reference, and test version pins now identify `0.20.0`.

### Fixed

- None.

### Removed

- None.

## [0.19.0] - 2026-09-22

### Added

- Higher-level PvP kill bonuses: +25% XP and RP per level above the winner,
  capped at +100%, with the reward caps raised by the same bonus.

### Changed

- Low-level player and persistent-bot RP values now rise with level; fractional
  eligible shared kills retain at least one RP before server rate modifiers.
- Group-PvE bots hunt outdoor camps in their region while queued for guildmates.
- Solo PvP hunters acquire nearby legal rivals and move between hunting grounds.
- Launcher, command-reference, and test version pins now identify `0.19.0`.

### Fixed

- Inland, Live, and realm-specific town teleporters use the shared cross-realm
  menus and destination handling, including Midgard travel to foreign towns.
- Distant group members no longer inflate locally observed PvP party strength.

### Removed

- Stationary group-matchmaking waits and repeated selection of the same
  low-level PvP hunting destination when alternatives exist.

## [0.18.0] - 2026-09-22

### Added

- Cross-region matchmaking for ordinary same-guild PvE parties; nearby members
  remain preferred and remote members use the existing rendezvous travel.

### Changed

- Launcher, command-reference, and test version pins now identify `0.18.0`.

### Fixed

- Group-PvE bots no longer wait for the 20-minute fallback solely because their
  compatible guildmates are in other regions.
- Never-killed characters are no longer treated as recently killed just because
  their played time is shorter than the repeat-kill window.

### Removed

- None.

## [0.17.0] - 2026-09-22

### Added

- Restart-safe generated-guild consolidation with persisted source-to-survivor
  mappings, weighted 1:2:4 guild targets, protected human memberships, and
  keep/alliance reference reconciliation before autonomous login.
- Local low-level PvP hunts, opportunistic legal rival engagement for PvE
  parties, explicit autonomous PvP safety opt-in, and stronger-party avoidance.
- Regression coverage for guild caps and weighting, login cohorts, every
  ordinary party size, matchmaking cadence and navigation budgets, low-level
  PvP policy, Camlann alliances, and companion threat handling.
- A maintained `FEATURES.md` guide covering the fork's playable world,
  autonomous population, party, PvP, companion, launcher, and safety behavior.

### Changed

- Autonomous populations now use at most fifteen managed mixed-realm guilds,
  with deterministic realm, level-band, and class-role balancing; player guilds
  and generated guilds containing humans remain untouched.
- Matchmaking now runs every five seconds, rotates longest-waiting candidates,
  forms ordinary PvE parties with 2–8 compatible guildmates, and reassesses
  roles and content after permanent losses without changing exact raid sizes.
- Level 1–19 activity defaults are now 45% solo PvE, 40% group PvE, and 15%
  PvP; low-level PvP parties prefer pairs and are capped at four members.
- Temporary companions can focus legal human, autonomous-bot, and controlled-
  pet targets and remember hostile attempts that miss or are blocked.
- Launcher, command-reference, and test version pins now identify `0.17.0`.

### Fixed

- Guild consolidation failures now block autonomous login with an actionable
  error instead of allowing a partially reconciled population to enter.
- PvE content selection now uses the live party size and will not target above
  the party average when either healing or frontline capability is absent.
- Autonomous PvP acquisition now revalidates shared Camlann legality, safe
  areas, release immunity, grey restraint, visibility, and party strength.

### Removed

- Random matchmaking leader rejection, the low-level PvP allocation ban, and
  ordinary-PvE assumptions that every party must contain exactly eight bots.

## [0.16.1] - 2026-09-22

### Added

- None.

### Changed

- Repository Git guidance now permits intentional direct synchronization and
  local integration of the fork's `main` branch.
- Launcher, command-reference, and test version pins now identify this patch
  release as `0.16.1`.

### Fixed

- None.

### Removed

- PR-only and no-local-fast-forward restrictions from the repository agent
  instructions.

## [0.16.0] - 2026-09-22

### Added

- Camlann Tier 9 player-facing launcher copy for the single full-PvP world,
  autonomous crew generation, Active Population, guild-owned keeps, and
  guild-only relics.
- Focused command and play guidance for `/safety off`, `/gc form`, cross-realm
  companions, dangerous leveling zones, and safe Camlann hubs.

### Changed

- The launcher now identifies the Old Frontiers Camlann world directly and
  labels realm generation controls as adding bots to crews.
- The progress importer now refuses Camlann destinations and Camlann sources;
  the existing one-time fresh-world reset remains the only supported conversion.
- The roadmap now treats Tier 9 as the last numbered tier without making it an
  automatic `1.0.0` release; versioning stays on the current 0.x line until the
  owner explicitly calls for release 1.0.

### Fixed

- Removed stale player documentation that described realm cards as factions or
  suggested importing Normal progress into the Camlann world.

### Removed

- Normal-save progress import into the Camlann world.

## [0.15.0] - 2026-09-22

### Added

- Camlann Tier 8 population tuning for level-band activity mixes and
  deterministic autonomous crew-size distribution.
- Regression coverage for Camlann roamer reserves, pair/small-crew/eight-man
  formation bands, and the keep/relic objective cooldown.

### Changed

- Autonomous bots now default to 55/45 PvE activity below level 20,
  30/45/25 PvE/RvR activity from levels 20–49, and 15/35/50 at level 50.
- RvR formation planning reserves 25% of mature actors as independent roamers,
  favors gank pairs and full eight-man crews, and waits 30 minutes before
  selecting the same keep or relic objective again.

### Fixed

- Small autonomous populations no longer lose all independent frontier actors
  when the RvR grouping pass forms crews.

### Removed

- None.

## [0.14.0] - 2026-09-20

### Added

- Tier 7 mixed-realm PvE expedition recruitment and PvP-context regression
  coverage for grinding, loot, and Realm Exchange behavior.

### Changed

- Autonomous dragon and epic-dungeon expeditions use their encounter realm
  for world location and presentation only; class-, level-, and crew-eligible
  adventurers may form a shared PvE expedition across realms.
- PvE loot, currency sharing, equipment rules, Realm Exchange access, and
  existing navigation/client patch boundaries remain unchanged.

### Fixed

- Raid recruitment and expedition pet support no longer reject eligible
  cross-realm members or their controlled pets under Camlann PvP rules.
- PvE, economy, and Exchange regression tests now exercise `PvPServerRules`.

### Removed

- None.

## [0.13.1] - 2026-09-20

### Added

- CoreServer builds now use the tracked example server configuration as their
  output config when a clean checkout has no local runtime configuration.

### Changed

- Development documentation records the local server configuration fallback
  and preserves the launcher, command reference, and changelog version pin at
  `0.13.1`.

### Fixed

- A fresh checkout can build the server solution without an ignored
  `serverconfig.xml` copied from another installation.

### Removed

- None.

## [0.13.0] - 2026-09-20

### Added

- Full Camlann PvP consequence coverage for Old Frontiers safety scope,
  player-shaped death immunity, constitution loss, and XP/realm-point kills.

### Changed

- PvP player kills no longer award legacy bounty points or coin, and autonomous
  bots retain the player-kill immunity timer after release or resurrection.
- `/level` is disabled on the shipped Camlann PvP server regardless of mutable
  slash-level settings.

### Fixed

- Autonomous GameBot kills now classify human deaths as PvP for release
  immunity, and sub-10 `/safety` no longer protects actors inside Old Frontiers.

### Removed

- Atlas bounty-point generation from the PvP quest reward compatibility path.

## [0.12.0] - 2026-09-20

### Added

- Camlann guild-owned keep claims with bot-aware ranks, companion-aware claim
  counts, a three-keep guild limit, claim timestamps, and dynamic relic mounts.
- Relic keep persistence through `Relic.KeepID`, guild-only uncapped bonuses,
  claim-delay enforcement, and guild-aware autonomous keep/relic objectives.

### Changed

- Fresh and launcher-reset frontier keeps now use `Realm=0` until claimed;
  unclaimed keeps are hostile to every guild and portal keeps remain safe.
- Keep capture/reset, guard ownership, broadcasts, and relic pickup/mounting
  use guild ownership while realm remains only a cosmetic client display.

### Fixed

- GameBot and bot-owned pet keep kills now reset and display keeps correctly.
- Relics dropped from a keep return to their temple shrine, and the launcher
  clears old mounted-keep and claim-timestamp state without touching characters.

### Removed

- Realm-owned relic bonus gating and the PvP seal-mob lord respawn branch for
  Camlann keeps.

## [0.11.1] - 2026-09-20

### Added

- Dedicated documentation for `/spawn` companion-bot XP, attribution,
  progression, persistence, and future tuning points.

### Changed

- None.

### Fixed

- None.

### Removed

- None.

## [0.11.0] - 2026-09-20

### Added

- `/spawn` companion bots now earn PvE experience from their own combat
  contribution while they are active.

### Changed

- Temporary companions use the normal player XP rate and can level and train
  their temporary in-memory class progression without changing the owner's
  existing XP or loot treatment.

### Fixed

- `/spawn` helpers no longer discard their valid XP contribution while still
  remaining excluded from realm-point rewards and real-party XP divisors.

### Removed

- None.

## [0.10.0] - 2026-09-20

### Added

- Camlann Tier 4 autonomous crews with real `DbGuild` identity, persisted bot
  membership/rank, mixed-realm guild rosters, and player invitations for live
  autonomous bots.
- Crew-based login balancing, mixed-realm group formation, and guild-aware
  roster/rank handling for autonomous actors.

### Changed

- Autonomous frontier behavior now roams and hunts unallied actors; keep and
  relic contesting remains deferred to Tier 5.
- Cross-realm guild membership is enabled for the PvP ruleset while character
  realm identity remains unchanged.

### Fixed

- Existing `offline_world_bots` databases receive additive `GuildId` and
  `GuildRank` columns with safe defaults during setup/migration.

### Removed

- Realm-quota login and realm-local autonomous RvR group formation from the
  active Camlann population path.

## [0.9.0] - 2026-09-20

### Added

- Camlann Tier 3 hostility resolution through the player-shaped combatant
  helper, including same-realm stranger targeting across frontier and dungeon
  autonomous behavior.
- A tunable `camlann_bot_grey_engage_chance` property, with retaliation and
  crew-defense exceptions for grey player-shaped targets.

### Changed

- Companion engagement, stealth ambushes, crowd-control reservations, siege
  legality, shared-dungeon scans, and autonomous frontier target selection now
  use group/guild/battlegroup alliance instead of realm as the combat boundary.
- Autonomous target tests and bind recovery no longer treat foreign realm
  identity as an enemy-combat rule.

### Fixed

- Same-realm ungrouped bots are no longer filtered out of Camlann autonomous
  combat, while allied mixed-realm crews remain protected from friendly fire.
- Grey-target restraint now applies through the shared bot aggro gate instead
  of only the specialized frontier and dungeon scans.

### Removed

- Realm-inequality hostility filters from the Tier 3 autonomous combat paths.
- The obsolete relocation of autonomous bots saved at a foreign-realm bindstone.

## [0.8.0] - 2026-09-19

### Added

- Camlann Tier 2 cross-realm `/spawn` and `/classes` choices, including
  realm-correct companion identities and equipment.
- All-realm capital and Classic/SI leveling-town teleporter menus, with
  battleground destinations excluded.
- Offline tests for foreign-capital Realm Exchange listings and all-realm
  teleporter coverage.

### Changed

- Mixed-realm companions, group pulls, healing/buffs, autonomous town routes,
  stable travel, frontier portal travel, and service-NPC routing no longer use
  realm as an access boundary; PvP alliance rules still govern hostility and
  support.
- Realm Exchange access is available at any capital broker while each
  character's market remains partitioned by their own realm, preserving real
  items, coin, and proceeds.
- Bot name lookup is realm-agnostic, and autonomous chat knowledge covers
  open Classic/SI travel services.

### Fixed

- Battleground travel is closed by default across teleporters, medallions, and
  autonomous movement.
- Foreign-realm portal-keep teleporters can board autonomous bots carrying the
  correct ticket for their own identity.
- Server-owned dummy guild creation is restart-safe, so an existing save with
  duplicate legacy dummy-guild rows no longer prevents startup.

### Removed

- Same-realm restrictions from player-led companion grouping and temporary
  companion class selection.

## [0.7.0] - 2026-09-19

### Fixed

- Realm-point value of victims below level 20 is floored at `1 + RealmLevel`.
  The pre-1.81 `(level - 20)^2` formula grew again below 20, so a level-1
  kill paid 361 RP (more than a level-35 victim), for both player and bot
  victims.

### Changed

- `docs/CAMLANN.md` records the passed Tier 1 real-client spike and marks
  Tier 1 complete; `AGENTS.md` no longer calls the conversion unimplemented.

## [0.6.0] - 2026-09-19

### Added

- Tier 1 Camlann player-shaped combatant resolution, bot PvP immunity, and
  focused hostility/safe-zone coverage.

### Changed

- PvP attack, heal, alliance, safety, and client presentation decisions now
  treat humans, GameBots, and their controlled pets as player-shaped actors.
- Grouped GameBots and their pets receive the friendly client guild-ID update;
  temporary companions retain protection for the group they joined.
- Player-shaped PvP kills now use non-allied participation for XP/RP, including
  autonomous bot killers and bot-victim constitution-loss bookkeeping; `/assist`
  and `/who` use the same PvP alliance decision.

### Fixed

- Same-realm strangers and hostile GameBots are no longer treated as friendly
  NPCs or granted the dummy-guild packet hack, and bot-owned pets resolve to
  their living bot owner.

### Removed

- The obsolete region-163-only safety exception from PvP hostility decisions.

## [0.5.0] - 2026-09-19

### Added

- Tier 0 Camlann bootstrap: a typed `WorldModel` marker, launcher-owned
  one-time world reset with a timestamped database backup, and synthetic reset
  fixtures covering rollback, idempotency, and server-running refusal.

### Changed

- New server configurations default to PvP, and startup now fails closed unless
  the database carries the completed `Camlann-1` world marker.

### Fixed

- Keep and relic state is reset to neutral/homed values as part of the new-world
  transaction without changing world definitions, item templates, or meshes.
- Reset cleanup includes backup-character inventory rows and safely handles
  empty stopped-state SQLite sidecars while still rejecting an uncheckpointed WAL.

### Removed

- The launcher no longer exposes the legacy realm-owned keep/relic reset panel
  while the Camlann guild-claim implementation is still pending.

## [0.4.5] - 2026-09-19

### Changed

- `docs/completed/DEV-SETUP.md`: Tier 5 (first real deploy, in-game smoke test,
  restore, redeploy) and Tier 6 (hand-off to Camlann) recorded as done. The
  pre-Camlann save backup is deferred to the start of Camlann Tier 0.
- `docs/DEVELOPMENT.md`: the WSL2 + Windows loop is marked verified end to
  end against the real install.

## [0.4.4] - 2026-09-19

### Added

- `tools/dev/winnet.sh`: WSL wrapper for the complete-install bundled Windows
  SDK (CLI home, NuGet packages and HTTP cache under `OfflineDAoC-dev\state`,
  passed to `dotnet.exe` through `WSLENV`).
- `tools/dev/Deploy-OfflineDAoC.ps1`, `Restore-OfflineDAoC.ps1`,
  `OfflineDAoC.Deploy.psm1`, `deploy.sh`, and `Test-DeployOfflineDAoC.ps1`:
  parameterized dry-run/apply deploy and restore with process checks, path
  guards, protected-save hashing, verified rollback on any failure,
  resumable restore, an owner-approval `-IncludeThirdParty` switch,
  warnings for build-only DLLs and missing PDBs, and a fake-tree self-check
  that only deletes its own marked scratch folder.
- Root `.gitattributes` (`* -text`) so Git does not rewrite the mixed
  LF/CRLF tree.

### Changed

- `docs/completed/DEV-SETUP.md`: Tier 1–4 gates recorded; Tier 2 baseline counts added.
- `docs/DEVELOPMENT.md`: short WSL2 + Windows loop commands pointing at the
  new wrappers.
- `AGENTS.md`: preserve each file's existing line endings.

## [0.4.3] - 2026-09-19

### Added

- `docs/completed/DEV-SETUP.md`: tiered WSL2 + Windows development setup plan. It covers
  the install location, toolchains, baseline tests, line-ending guard,
  parameterized deploy/restore scripts, first client smoke test, and hand-off
  to the Camlann work.
- `AGENTS.md` rule: all GitHub work targets the fork
  `stefanrows/OfflineDAoC` only (never the upstream `shadowofze` repo), and
  every change lands through a pull request.

## [0.4.2] - 2026-09-19

### Changed

- Revised `docs/CAMLANN.md`. It now targets Camlann 1.65 on the existing Old
  Frontiers world (pre-ToA) and records owner decisions: player-founded
  guilds, uncapped guild relics, a one-time launcher world reset,
  any-realm companions, restrained grey-target ganking, safe portal-keep hubs,
  and XP + RP for player kills.
- The plan now covers gaps found in the code audit: the world is Old
  Frontiers, not New Frontiers, so safety checks are zone-level;
  `PvPServerRules` lacks fork guards; the client packet hack marks bots as
  friendly; bot guilds are not persisted; keep claims and relics are realm-
  and `GamePlayer`-only; and the Atlas PvP branches need an audit. Not
  implemented yet.

## [0.4.1] - 2026-09-19

### Added

- Tiered Camlann conversion plan in `docs/CAMLANN.md` (full-PvP only, fresh
  save, no dual Normal/PvP mode). Not implemented yet.

## [0.4.0] - 2026-09-19

### Fixed

- The launcher no longer overwrites Windowed mode on every Enter Realm launch.
  A new AppData profile still defaults to borderless fullscreen; an existing
  `user.dat` display choice and resolution are left alone.

## [0.3.1] - 2026-09-19

### Added

- Fork changelog and MAJOR.MINOR.PATCH versioning rules in `AGENTS.md`.
- Launcher pin set to `0.3.1` so this fork is distinct from upstream v0.3
  and from the original author's private 0.4 label.

## [0.3.0] - 2026-09-15

Upstream Offline DAoC v0.3 public baseline. No reconstructed author history.
The playable runtime, world data, and navigation meshes still come from that
GitHub release.
