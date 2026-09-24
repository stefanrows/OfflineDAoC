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

- Ordinary autonomous parties recruit only compatible bots from the same guild,
  across all three realms. Matchmaking runs every five seconds, prioritizes the
  longest-waiting candidates, and limits route-validation work per pass.
- Same-region candidates are preferred. If they cannot form a party, compatible
  guildmates from other regions can join and travel to the existing rendezvous.
- Ordinary PvE parties can start with any size from two through eight. Their
  roster is locked for the outing; after a permanent loss, roles and content are
  reassessed between fights, and the party dissolves below two members.
- Healing and frontline classes are preferred. Content difficulty uses the
  actual party size; a party missing either capability will not choose a target
  above its average level.
- Pulling, dungeon staging, recovery, resurrection timeouts, and session
  validation support every ordinary party size. Dedicated realm expeditions and
  raids keep their existing exact-size requirements.
- Existing encounter locations, loot, currency, Realm Exchange behavior,
  navmeshes, training, selling, town recovery, and stable routes are retained.

## Autonomous PvP

- Default activity targets are 45% solo PvE, 40% group PvE, and 15% PvP for
  levels 1–19; 30/45/25 for levels 20–49; and 15/35/50 at level 50. The launcher
  can save other valid 100% distributions, including low-level PvP.
- Low-level PvP groups prefer pairs and never exceed four members. They hunt in
  reachable, non-safe leveling areas near the party's level instead of being
  sent to keep and relic objectives early.
- Mature bots retain frontier roaming, keep, relic, siege, and event behavior.
- PvE parties can retaliate and can opportunistically engage a visible legal
  rival from level 1. They do not initiate an extra fight during combat or
  recovery.
- New fights prefer opponents within five levels, preserve grey-target restraint,
  and avoid visibly stronger parties. Retaliation is still legal.
- PvP assignments explicitly relinquish optional low-level safety for autonomous
  actors while preserving safe areas and release immunity.
- Solo PvP hunters also engage local legal rivals and change hunting grounds
  instead of repeatedly selecting the same spot. Party strength counts nearby
  living members rather than guildmates elsewhere in the region.
- Queued group-PvE bots level at outdoor camps in their current region until
  matched; the new party's rendezvous then supersedes their individual camp.
- Formation delay and matchmaking blocks are logged separately from travel time.

## Player companions

- `/companions` opens the Companion Manager window (Roster, Recruit, and
  Overview, Training & Tactics, and Gear details) with a client extension;
  `find`, `list`, `recruit`, `invite`, and `bench` remain command fallbacks. Each character can store up to 78
  persistent recruits, including generated and authored individuals. Saved
  identities, XP, training, equipment, and inventories survive benching and
  attempt to restore active companions at login.
- Active companions earn eligible adventuring XP and can catch up toward their
  owner's level. Benched companions retain progress without earning passive XP.
  Automatic training offers 57 named builds for 35 classes (`/companions build`);
  four classes require manual training.
  The menu offers short gear pages and a native external-inventory view for the
  companion backpack. Client acceptance of the updated UI remains pending.
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
  population, goal-weight settings, generated crews, realm events, save reset,
  Realm Exchange display, and operational diagnostics.
- Save/reset and progress-import restrictions are Camlann-aware. Personal saves,
  account data, profiles, credentials, logs, and databases are not published.
- Development builds and deployments must use the guarded scripts in `tools/dev`.
  Never deploy over a running server, and back up a real save before testing a
  gameplay update.

## Verification status

For 0.30.0 on 2026-09-23, the Release server and Windows launcher builds passed
with zero errors. Automated tests were skipped under this fork's fast shipping
workflow. Earlier stage checks passed the full server suite (1,975 tests) and
Windows launcher suite (114 tests); those results do not cover the latest UI or
character-creation changes.

Runtime acceptance on 2026-09-23: the owner confirmed all Stage 2 checks passed
in a separate Windows acceptance installation, including invites after
generated starter-gear persistence was fixed. These results cover Stage 2; the
broader Stage 6 integration scenarios remain pending. The container image
build could not be run because the local Docker engine was unavailable.
