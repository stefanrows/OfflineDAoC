# Companion Bots: `/spawn` and Persistent Roster

For the staged design and remaining work, see the [companion roadmap](COMPANION_ROADMAP.md).

This document records the behavior and persistence boundaries for owned
companions so they stay distinct from autonomous world bots and real players.

## Identity and lifetime

`/spawn` creates a `GameBot` with `IsTemporaryGroupHelper = true` and
`IsAutonomousWorldBot = false`. The helper is owned by the invoking
`GamePlayer`, joins that player's group, is fully equipped for the session, and
is removed with the group. It is not saved as an autonomous bot and does not
create a persistent character or database record.

The creation path is
`source/server/GameServer/commands/playercommands/TemporaryGroupCommands.cs`.
Keep the temporary-helper flag distinct from `IsAutonomousWorldBot`; the two
bot types intentionally have different persistence, reward, and tuning rules.

## Persistent roster

Bare `/companions` opens a temporary owner-private clickable menu. The explicit
`/companions list`, `/companions recruit <class>`,
`/companions invite <name>`, and `/companions bench <name>` manage generated,
persistent recruits. Class-only recruitment searches all realms, so commands
such as `/companions recruit Warden` and `/companions recruit Healer` work
without a realm argument. Use `/companions recruit <realm> <class>` when a class
name is shared by multiple realms. Type `/classes` for class names grouped by
realm. A character can recruit anywhere for free, starts each recruit at level
1, and can store up to 78 companions. `/companions list` shows names grouped by
realm without exposing internal IDs. Ownership is per character. Each recruit
has a stable ID, saved identity/build state, and an independent inventory
stored under a `playercompanion:` owner ID. Same-class recruits therefore
remain separate.

An active, living recruit follows its owner through an accepted portal or
region teleport, including same-region moves. Only that owner's companion in
the current group relocates; ordinary group members keep their own position.

The roster lives in the additive `player_companions` table. Existing `bot_profiles`
are not converted. Active companions attempt to rejoin at owner login when group
space is available. Removing a companion, leaving the group, or disbanding saves
and benches that companion. `/spawn` still creates a temporary helper and has
not been redirected to the persistent roster.

The Stage 5 cast adds two authored people for each of the 39 Classic + SI
classes. Each has a stable key, name, eligible race and gender, size,
background, personality, and dialogue. Browse them in the private menu or with
`/companions cast [realm] [page]`; recruit each once per owner with
`/companions recruit authored <name>`. Generated recruitment by class remains
available. New generated recruits receive a saved personality template. Existing
records keep their previous identity and class behavior. The catalog's preferred
build is the validated class plan where available; six classes remain manual-only.

`/companions profile <name>` shows identity, background, preferred build, role,
stance, and a short line of dialogue. `/companions role <name> tank|healer|buffer|attacker` saves a class-legal
party job;
`/companions stance <name> aggressive|defensive` saves an individual engagement
preference. Personality sets the first stance, and direct player attacks and
`/pull` take precedence. `/aggressive` and `/defensive` issue a group order for
helpers and persistent companions; `/companions group default` returns to
individual stances. Dialogue appears on recruit, invite, bench, and requested
profile views; it does not trigger during combat. Autonomous bots do not read
these preferences. See [Stage 5 decisions](COMPANION_STAGE5_DECISIONS.md).

The roster preserves the existing generated level-1 equipment as a one-time,
protected starter loadout. Persistent inventory is saved across benching and
restart. Persistent recruits earn PvE progression while actively adventuring
with an eligible owner; benched companions do not gain catch-up XP. New recruits
use automatic training only when their class has an enabled runtime-validated
plan; all pre-existing records stay manual. The current catalog has 33
project-recommended plans and six manual-only classes. Authored characters and
their once-per-owner recruitment rules are Stage 5.

## Death recovery and raids

Persistent companions use the existing GameBot corpse-recovery behavior. Their
corpse and group are retained while dead; recovery follows the general GameBot
timers (20 seconds when no viable resurrector is present, 90 seconds when one
is present), then the existing bind/release behavior. They do not use the
`/spawn` helper's release-to-owner path. Death recovery is runtime GameBot state;
this policy adds no companion death fields or save migration.

`/raid 40` and `/raid 80` remain raids for the owner's temporary `/spawn`
helpers. The owner counts toward the selected capacity. Persistent companions
and autonomous world bots are not eligible raid helpers; invite persistent
companions to an ordinary party instead. Offline policy coverage does not replace
the owner-run client check listed in the [Stage 6 acceptance brief](COMPANION_STAGE6_ACCEPTANCE.md).

## Temporary `/spawn` XP contract

The current contract is:

| Area | Temporary companion behavior |
| --- | --- |
| Eligible damage | Direct damage dealt by the companion earns companion XP. |
| Controlled pets | Damage from a pet controlled by a companion keeps the existing human-owner attribution. |
| Human owner XP | The owner still receives the same full owner/group credit as before. |
| Party divisor | A companion does not increase the real-player group count or reduce the owner's XP share. |
| XP rate | Companion XP uses `ServerProperties.Properties.XP_RATE`, the normal player rate. Persistent autonomous bots use `BOT_XP_RATE`. |
| Bonuses and cap | The normal bot award path still applies damage percentage, XP cap, camp, group, guild, and BAF calculations. |
| Realm points | Companions do not receive realm points. |
| Loot | Companions do not become loot owners or receive extra loot rolls. |
| Persistence | XP and levels exist only for the current helper session. |

The temporary-helper kill-credit flow is in
`source/server/GameServer/serverrules/AbstractServerRules.cs`:

1. `ProcessXpGainers` keeps a direct temporary helper in the bot contribution
   map, while also crediting its damage to the human owner with no additional
   group-member count.
2. The helper is therefore eligible for `AwardBotOnNpcKill`, but remains out of
   the real-player XP divisor and loot-owner selection.
3. `AwardBotOnNpcKill` grants the helper XP but records autonomous objective
   progress only for persistent autonomous bots.

`ResolveNpcRewardOwner` preserves direct temporary helpers and persistent
companions while resolving controlled pets to their appropriate reward actor.
If that policy changes, update the attribution tests in
`source/server/Tests/UnitTests/UT_AutonomousLootFlow.cs` at the same time.

## Leveling and tuning points

`GameBot.GainExperience` handles autonomous bots, temporary helpers, and
persistent player companions. Keep the `IsAutonomousWorldBot` branches separate:
autonomous bots use `BOT_XP_RATE`, temporary helpers use `XP_RATE` and spend
points on level-up, and persistent companions use `XP_RATE` with separate
automatic/manual training metadata. Automatic plans apply each gained level,
including multi-level gains; manual companions hold earned points. Persistent
companions save XP progress through a coalescing record-only queue; their
inventory is not rewritten for each kill.
Level gains refresh the group window so the displayed companion level matches
the level used by combat and training immediately.

Persistent companions receive the owner's normal NPC party reward when the
owner gains XP. Active companions must also be in range and pass the grey-con
check; support companions qualify without dealing damage. A damage-dealing
companion can also receive a separate damage-based NPC award when its eligible
owner has no player damage share. Companion and controlled-pet damage stays out
of player damage percentages, group divisors, and loot-owner selection. The
companion's XP is clipped at the owner's current absolute XP total, preserving
any higher saved companion XP. Only NPC kill rewards are accepted, so companion
PvP XP and realm points remain disabled.

The `/companions train` command accepts only a specialization from the
companion's class career and applies normal point costs and level limits. It
requires a compatible trainer unless `ALLOW_TRAIN_ANYWHERE` is enabled. The
menu exposes the same training, mode, plan, and respec services.
`/companions respec` requires owner full-skill respec eligibility, honors
`FREE_RESPEC`, confirms before resetting, and resets only the selected
companion. The 33 enabled automatic plans carry stable versioned IDs and are
checked against the local runtime career and skill tables. A changed or missing
plan never changes saved allocations; earned points stay manual until a valid
plan is explicitly selected.

## Personal gear and inventory

Each eligible active companion independently rolls a personal PvE item at
`min(100%, 25% × XP_RATE)`. PvP reward-eligible deaths award one item per
eligible companion, including support companions in the eligible owner's party.
Owner XP caps and maximum level do not suppress the gear roll. Player loot and
autonomous-bot handling remain separate.

Personal items use the companion's existing inventory namespace and are marked
as earned, starter, or player-supplied in additive companion metadata. Starter
items stay with their original companion; player-supplied items remain
recoverable; legacy items without provenance stay protected. Manual equipment
locks the affected slot. Automatic upgrades require a strictly greater
class-legal score, respect hand conflicts and locks, and keep displaced items in
the backpack. If space is needed, only the lowest-scoring positive-value earned
backpack item may be sold; equipped, kept, starter, supplied, and unclassified
items are excluded. Sale proceeds use normal merchant appraisal and go to the
owner.

Transfers require an active nearby companion and idle inventory state. Only
persisted, tradable, droppable ordinary items can move; quest, relic, siege, and
other restricted items are rejected. Full-bag transfers, upgrades, and rewards
save item ownership, companion metadata, sale removal, and owner proceeds in one
database transaction. The menu validates each choice against current owner,
companion, item, slot, and menu-generation state.

The main tuning points are:

- `XP_RATE` and `BOT_XP_RATE` in server properties control the two bot classes'
  base multipliers.
- `AwardBotOnNpcKill` controls damage-share, level-cap, and bonus calculations.
- `ProcessXpGainers` controls owner credit, party divisor behavior, and loot
  ownership.
- The temporary-helper constructor path controls the companion's starting
  level, equipment, and initial XP floor.

When adjusting companion rewards, test direct companion damage separately from
companion-controlled pets, and verify that autonomous bots, real players,
loot, realm points, and saved autonomous progress remain unchanged unless the
new behavior explicitly intends otherwise.
