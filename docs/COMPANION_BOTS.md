# `/spawn` Companion Bots

This document describes the intended behavior of temporary companion bots so
their reward and progression rules can be tuned without confusing them with
persistent autonomous world bots or real players.

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

## XP contract

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

The kill-credit flow is in
`source/server/GameServer/serverrules/AbstractServerRules.cs`:

1. `ProcessXpGainers` keeps a direct temporary helper in the bot contribution
   map, while also crediting its damage to the human owner with no additional
   group-member count.
2. The helper is therefore eligible for `AwardBotOnNpcKill`, but remains out of
   the real-player XP divisor and loot-owner selection.
3. `AwardBotOnNpcKill` grants the helper XP but records autonomous objective
   progress only for persistent autonomous bots.

`ResolveRootRewardOwner` deliberately preserves direct temporary helpers while
continuing to resolve their controlled pets to the human owner. If that policy
changes, update the attribution tests in
`source/server/Tests/UnitTests/UT_AutonomousLootFlow.cs` at the same time.

## Leveling and tuning points

`GameBot.GainExperience` is shared with persistent autonomous bots, so changes
must preserve the `IsAutonomousWorldBot` branch. Temporary helpers use the
normal player rate, start with the XP floor for their current level, and spend
new specialization points immediately when they level. Their stat, spell, and
style refresh is in-memory only; no autonomous status save is queued for them.

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
