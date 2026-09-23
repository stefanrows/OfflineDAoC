# Companion Stage 3 decisions and implementation

Updated: 2026-09-23. The owner accepted the recommended Stage 3 policies. The
core PvE progression and manual-training paths are implemented; automatic build
plans and runtime acceptance remain pending.

## Relevant findings

- The additive player-companion record stores identity, level, XP, specialization
  levels, the bot runtime profile, unspent specialization points, and the last
  trained level. Roster actions save companion state and inventory.
- Persistent companions now receive eligible PvE XP through their active owner.
  Progress saves are coalesced and do not rewrite inventory for each NPC kill.
- Temporary `/spawn` helpers keep their prior damage, XP, realm-point, loot, and
  session-only behavior. Persistent autonomous bots keep their separate
  `BOT_XP_RATE` progression.
- Existing `BotSpec` profiles are not validated companion leveling plans.
  Research still lacks some sourced numeric schedules, and every plan needs a
  disposable local-database check before automatic use. No local runtime
  database is checked into this repository.

## Accepted behavior

### XP eligibility, support, pets, and catch-up

- Award XP only from eligible PvE NPC kills. An owner must be XP-enabled, active,
  and eligible for the NPC encounter. Active, in-range companions must also pass
  the grey-con check. Support companions need no damage when their owner gets a
  party award. Benched companions receive no XP.
- Give every eligible active companion a separate copy of the owner's normal
  party award, including support companions that did not deal damage. If a
  companion dealt damage but its owner's player share is zero, it may receive a
  separate damage-based PvE award. Persistent companion damage stays out of
  player damage percentages, group counts, and loot ownership, so it does not
  reduce player shares or add loot rolls.
- Attribute controlled-pet damage to its companion. Pets do not receive a
  second award. Companions receive no PvP XP or realm points.
- Use normal player XP scaling and the owner's NPC group and bonus calculation,
  with no extra catch-up multiplier. Cap companion XP at the owner's current
  absolute XP total after the owner receives the kill award. Stop awarding XP
  at that target until the owner advances; never lower saved companion XP.

### Automatic and manual training

- New recruits use manual mode unless their class has a validated automatic
  plan. No class currently has a plan that has passed the complete research and
  runtime-database validation bar, so all current recruits start in manual mode.
- Manual mode keeps earned specialization points unspent until the owner trains
  a career line. `/companions train <name> <line> <level>` uses the companion's
  class careers, standard point costs, level limit, and skill refresh path. A
  compatible class trainer is required unless `ALLOW_TRAIN_ANYWHERE` applies.
- `/companions mode <name> automatic` remains unavailable until a validated
  plan is added. `/companions plan <name>` reports this gate. Adding plans must
  validate career lines, total point budgets, per-level milestones, and runtime
  skills. Do not use existing `BotSpec` profiles as automatic plans without
  that validation.
- `/companions respec <name>` resets only that active companion's
  specializations. It uses the owner's standard full-skill respec eligibility,
  respects `FREE_RESPEC`, and requires a trainer for the companion's class. The
  companion keeps level, XP, inventory, and equipment. The confirmation dialog
  rechecks eligibility before applying the reset.

### Persistence and other rewards

- Save companion XP and specialization progress during play, when training or
  respecializing, and through the existing bench/logout saves. Save XP progress
  without rewriting inventory rows for every kill.
- Preserve existing owner XP and loot behavior. Realm-point, realm-rank, and
  realm-ability progression remain disabled for player companions.

## Remaining Stage 3 work

No automatic plans are enabled yet. Complete the class-by-class plan
validation and disposable-database checks before enabling an automatic mode.
The isolated server build passes, but real-client gameplay acceptance has not
been performed.
