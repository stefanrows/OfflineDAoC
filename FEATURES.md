# Offline DAoC features

This file describes the playable behavior maintained by this fork. It is a
feature guide rather than a release log; see [CHANGELOG.md](CHANGELOG.md) for
version-by-version changes and [docs/CAMLANN.md](docs/CAMLANN.md) for the design
and verification history of the full-PvP conversion.

## Camlann 1.65 world

- One Old Frontiers, pre-ToA, full-PvP world where realm is character identity,
  not a permanent team.
- New characters begin as the class selected during creation, with that class's
  configured starter equipment. The server no longer rewrites them to a base class.
- Players, autonomous bots, persistent and temporary companions, and controlled pets use the
  same group, guild, battlegroup, safe-area, immunity, and `/safety` legality
  rules.
- Same-realm strangers can fight. Mixed-realm groupmates and guildmates remain
  allied. Player-led groups retain their cross-guild invitation behavior.
- PvP kills award XP and realm points. Base RP is the higher of five times
  victim level or the existing high-level curve, plus realm level. Beating a
  higher-level opponent adds 25% XP/RP per level difference (up to 100%),
  including reward caps. Damage sharing and repeat-kill protection still apply;
  a never-killed character is not blocked by the repeat-kill timer.
- Inland, Live, and realm-specific town teleporters offer all three realms'
  destinations through the same menu, regardless of the player's realm.
- Keeps are claimed by guilds. Relics are physically carried and mounted at a
  claimed guild keep, and their bonuses apply to that guild.
- Capitals, housing, protected starter dungeons, portal keeps, release immunity,
  stable travel, and the native client-patch hash guards remain protected.

## Persistent autonomous population

- Autonomous bots are persistent world actors with saved level, experience,
  equipment, inventory, money, location, objective, and recovery state.
- The launcher's World Speed control can run the live world at 1×, 2×, or 3×
  while no game client is connected. Travel, fights, cooldowns, recovery,
  respawns, bot schedules, and world deadlines advance through real server
  ticks; XP and loot per event are unchanged. A client connection restores 1×,
  and the selected speed resumes five seconds after the last disconnect.
  The launcher reports the speed actually achieved if the PC cannot keep up.
- The managed population is consolidated before login into no more than fifteen
  mixed-realm guilds: up to five small/medium/large triplets with 1:2:4 target
  membership weights. One triplet is used per 56 managed bots, rounded up, and
  the guild count never exceeds the bot count.
- Realm identities, level bands, and class roles are balanced deterministically.
  Existing survivor memberships stay fixed; newly generated bots fill current
  deficits.
- Player guilds and generated guilds containing human characters are protected
  from consolidation and do not count against the managed-guild cap.
- Consolidation stores a restart-safe source-to-survivor mapping in
  `offline_crew_consolidation`. Obsolete keep claims and alliance references are
  transferred before empty guilds are removed. Mounted relic locations and all
  bot progression/economy data are left intact.
- A failed reconciliation blocks autonomous login and writes an actionable
  error instead of admitting a partially migrated population.
- The existing startup population ramp and bounded spawn work are preserved.
  Login selection rotates fairly between guilds and favors compatible active
  cohorts by region, level, healing, and frontline needs.

## Autonomous parties and PvE

- Ordinary autonomous PvE parties recruit compatible bots in the same region
  across guilds and realms. Matchmaking runs every five seconds and prioritizes
  the longest-waiting candidates; bots in separate home regions do not travel
  across realms just to seek a pickup group.
- Their meetup has its own deadline. After choosing a camp, the party has up
  to 30 simulated minutes to arrive; the shared 45–120-minute task starts at
  the camp and continues through later recovery and camp replanning. A party
  that misses the camp travel window returns to solo PvE first.
- Parties can depart with two arrivals and accept late followers. A released
  member can return after death without dissolving the surviving party.
- Ordinary PvE parties can start with any size from two through eight. After a
  permanent loss, roles and content are reassessed between fights, and the
  party dissolves below two members.
- Healing and frontline classes are preferred. Content difficulty uses the
  actual party size; a party missing either capability will not choose a target
  above its average level.
- Pulling, dungeon staging, recovery, resurrection timeouts, and session
  validation support every ordinary party size. Dedicated realm expeditions and
  raids keep their existing exact-size requirements.
- Existing encounter locations, loot, currency, Realm Exchange behavior,
  navmeshes, training, selling, town recovery, and stable routes are retained.
- Outdoor camp choice considers crowding and recent productivity without
  excluding otherwise valid camps. Ordinary PvE parties do not initiate
  unrelated PvP fights.

## Autonomous PvP

- Each bot has a saved player type, traits, and activity state. Levelers favor
  PvE, Hunters patrol leveling areas, Roamers loop through frontier clearings,
  and Keep warriors pursue campaigns from level 35. Hybrid and Casual behavior
  changes with local time and recovery needs.
- The launcher offers named population presets and a six-type mix totaling
  100%, plus leveling-zone danger and Fresh launch or Established world shape.
- Low-level PvP groups prefer pairs and never exceed four members. They hunt in
  reachable, non-safe leveling areas near the party's level instead of being
  sent to keep and relic objectives early.
- Mature bots retain frontier roaming, keep, relic, siege, and event behavior.
- PvE parties can retaliate but do not start unrelated open-world fights.
- New fights prefer opponents within five levels, preserve grey-target restraint,
  and avoid visibly stronger parties. Retaliation is still legal.
- Autonomous bots below level 10 carry the same implicit `/safety` protection
  as a flagged player: they cannot attack or be attacked by other characters.
  Only an RvR assignment relinquishes it, and safe areas and release immunity
  still apply.
- Guild kill-on-sight lists record human killers and kills of level 10+
  members, but never a killer who is grey to the victim or a fight the victim
  started. A KOS target moves up the hunt order but still has to pass the level,
  grey, and party-strength checks.
- Outside RvR tasks, bot crowd control targets only opponents already in the
  fight and skips area mezzes. Being ganked does not lower a bot's PvE target
  difficulty, and only RvR tasks or level 10+ PvP-minded types hit the
  three-PvP-deaths wall.
- Solo PvP hunters also engage local legal rivals and change hunting grounds
  instead of repeatedly selecting the same spot. Party strength counts nearby
  living members rather than guildmates elsewhere in the region.
- Queued group-PvE bots level at outdoor camps in their current region until
  matched; the new party's rendezvous then supersedes their individual camp.
- Formation delay and matchmaking blocks are logged separately from travel time.

## Population growth and performance

- Fresh launch creates level-1 bots. Established batches spread levels across
  five bands (15/15/20/25/25 per 100 bots), with matching XP, equipment, and
  first-login training; level-50 creations also receive realm points. Existing
  bots keep their progress. A separate Add Lv.50 action remains available.
- Managed guilds can gain one new level-1 alt every 72 hours by default, up
  to a 5,000-bot total roster cap. Both values can be changed while the server
  is stopped; zero hours disables new alts. The population number means the
  total roster, with the existing login behavior.
- The launcher shows a numeric PC-specific population recommendation only
  after stable local samples at 500, 1,000, and 1,500 active bots. Owner
  calibration and real-client acceptance remain pending.

## Player companions

- `/companions` opens the Companion Manager window (Roster, Recruit, and
  Overview, Training & Tactics, and Gear details) with a client extension;
  `find`, `list`, `recruit`, `invite`, and `bench` remain command fallbacks. Each character can store up to 78
  persistent recruits, including generated and authored individuals. Saved
  identities, XP, training, equipment, and inventories survive benching and
  attempt to restore active companions at login.
- Active companions earn eligible adventuring XP and can catch up toward their
  owner's level. Benched companions retain progress without earning passive XP.
  Automatic training offers 57 named builds for 35 classes, chosen in the
  manager's Training & Tactics tab and recruit panel or with `/companions build`;
  four classes require manual training. Each build sets its group role. A
  Crowd control companion mezzes extra monsters in PvE, and the other companions
  leave mezzed monsters alone. The manager's Group orders row sets the group
  order and offers pull, invite all, bench all, and grind.
  The menu offers short gear pages and a native external-inventory view for the
  companion backpack. The manager's Gear tab lists every worn slot; clicking a
  slot shows the bag items that fit it, best first, with one-click equip. Client acceptance of the updated UI remains pending.
- `/spawn` still creates temporary, player-owned helpers, separate from both the
  persistent recruit roster and autonomous population.
- Companion PvP supports legal human, autonomous-bot, and controlled-pet targets.
  Explicit player or pet orders take priority, followed by active threats to the
  party.
- Missed and blocked hostile attempts are remembered. Crowd-control threat
  memory does not shorten or bypass the control effect.
- Damage companions share focus while respecting allied crowd control. Existing
  healing, cure, resurrection, role, and target-reservation systems remain in
  use.
- Targets are revalidated as safety, alliance, ownership, region, and group
  membership change. Pursuit stays inside the existing 2,000-unit
  leader-centered defense envelope, with tighter limits in defensive mode.
- Temporary helper lifetime, loyalty, XP, loot, and regrouping behavior are
  unchanged.

## Launcher and local operation

- The Windows launcher manages the local server and client, active autonomous
  population, Server population settings, generated crews, realm events, save reset,
  Realm Exchange display, and operational diagnostics.
- Save/reset and progress-import restrictions are Camlann-aware. Personal saves,
  account data, profiles, credentials, logs, and databases are not published.
- Development builds and deployments must use the guarded scripts in `tools/dev`.
  Never deploy over a running server, and back up a real save before testing a
  gameplay update.

## Verification status

For the integrated 0.45.0 source on 2026-09-24, the Release server and Windows
launcher builds passed with zero errors. Automated suites were skipped under
this fork's fast shipping workflow. Earlier focused autonomous checks passed
during development; they do not replace a real-client playtest of M0–M5 or
the newer companion UI.

Runtime acceptance on 2026-09-23: the owner confirmed all Stage 2 checks passed
in a separate Windows acceptance installation, including invites after
generated starter-gear persistence was fixed. These results cover Stage 2; the
broader Stage 6 integration scenarios remain pending.
