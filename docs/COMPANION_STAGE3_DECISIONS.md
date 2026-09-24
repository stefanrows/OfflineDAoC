# Companion Stage 3 decisions and implementation

Updated: 2026-09-24. The owner accepted the Stage 3 policies and delegated
selection of general-adventuring plans for every supported class. Progression,
manual training, and automatic training for 33 validated plans are implemented.
Six classes remain manual-only. The owner marked Stage 3 real-client gameplay
acceptance complete on 2026-09-24; the detailed observations were not supplied.

## Relevant findings

- The additive player-companion record stores identity, level, XP, specialization
  levels, the bot runtime profile, unspent specialization points, and the last
  trained level. Roster actions save companion state and inventory.
- Persistent companions now receive eligible PvE XP through their active owner.
  Progress saves are coalesced and do not rewrite inventory for each NPC kill.
- Temporary `/spawn` helpers keep their prior damage, XP, realm-point, loot, and
  session-only behavior. Persistent autonomous bots keep their separate
  `BOT_XP_RATE` progression.
- Existing `BotSpec` profiles are not companion leveling plans. The 33 enabled
  schedules are project recommendations derived from selected endgame targets,
  not historical level-by-level templates. Six unsupported classes remain
  manual-only for explicit research or combat-profile blockers.
- A consistent read of the local acceptance installation's skill data produced
  a disposable fixture outside Git with the required career, specialization,
  ability, spell, and style rows plus synthetic character and inventory records.
  All enabled plan targets matched runtime class careers; 90 targets had ranked
  skill data and 15 were career-only lines. The fixture is not committed.

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

- Existing companions remain in manual mode. New recruits start automatic only
  for a class whose versioned plan passes runtime validation and whose runtime
  specialization multiplier matches; unsupported classes start manual.
- Manual mode keeps earned specialization points unspent until the owner trains
  a career line. `/companions train <name> <line> <level>` uses the companion's
  class careers, standard point costs, level limit, and skill refresh path. A
  compatible class trainer is required unless `ALLOW_TRAIN_ANYWHERE` applies.
- `/companions mode <name> automatic` uses the same service as the menu. It
  checks current runtime data, saved allocations, and available points. If an
  allocation is above schedule or points are insufficient, it preserves the
  current mode/build and directs the owner to the existing explicit respec
  flow. Switching back to manual preserves allocations. `/companions plan`
  reports role, level-50 targets, validation status, and saved-plan mismatches.
  Do not use existing `BotSpec` profiles as automatic plans.
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

## Offline validation and owner acceptance

The focused offline suite simulates every enabled plan at every level from 1 to
50, checking point budgets, monotonic allocations, no overlevel ranks, and all
level-50 targets. Runtime fixture checks cover the actual career and ranked
ability/spell/style tables. Automatic mode metadata is additive and separate
from the serialized build profile; missing or changed plan IDs do not rewrite
allocations.

Animist and Wizard lack numeric endgame targets. Blademaster, Hero, and Warrior
need source-backed role or ranked-skill review. Necromancer's available numeric
candidate relies on the unsupported Death Servant companion combat profile.
These six classes remain manual-only. The owner marked real-client XP pacing,
trainer behavior, automatic training, multi-level gains, and restart persistence
accepted on 2026-09-24; detailed observations were not supplied.
