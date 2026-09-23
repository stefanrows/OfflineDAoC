# Companion Stage 2 decisions and implementation record

Updated: 2026-09-23. The owner selected the recommended Stage 2 choices below
and confirmed Stage 2 runtime acceptance passed. This record documents those
choices, implementation, and the acceptance result.

## What the current code establishes

- `/spawn` creates a temporary helper owned by the invoking character. It joins
  the current group, earns session XP, receives generated equipment, and is
  deleted when removed. It does not create a saved character or roster entry.
- The existing private class picker already offers a code precedent for a
  clickable owner-only menu. It can select classes from any realm and needs the
  player to be in a region; it does not require new NPC placements or a client
  patch.
- `bot_profiles` records a character owner, name, class, race, gender, level,
  and active state. It does not capture the progression state Stage 2 needs.
  Reusing it as the new roster would silently conflate two different save
  contracts.
- `BotInventory` can persist items under a string owner key. Any companion use
  needs a distinct namespace so the player's inventory and autonomous world
  bot inventories cannot alias it.
- The party already enforces its active member limit. The code has no policy
  for how many benched companions one character may retain.

## Confirmed owner decisions

| Decision | Confirmed choice | Reason |
| --- | --- | --- |
| Recruitment | Anywhere, free, level 1; maximum 78 stored records per character. Active capacity uses the existing group limit. | Owner selected the recommended option for each recruitment question. |
| Ownership | Per character. Each authored individual may be recruited once per owner; generated individuals are separate records. | Keeps same-class individuals distinct and follows existing character-scoped bot ownership. |
| Persistence | Companion-specific database records and a `playercompanion:` inventory owner-ID namespace. | Keeps companion rows and equipment separate from legacy profiles, player items, and autonomous bot state. |
| `/spawn` and saved bots | `/spawn` stays temporary. Persistent recruits use `/companions`; existing saved profiles are not converted. No separate GM test-helper command is added. | Preserves the existing helper contract and avoids inventing a migration mapping. |

The user selected “recommended” for each question after the earlier selection
window did not appear. The full questions and accepted answers are:

1. **Where, at what cost, and at what level should companions be recruited, and
   how many may one character store?** Anywhere, free, level 1, up to 78 stored
   companions per character; active slots use the normal group limit.
2. **Should companion ownership be per character or account-wide, and can an
   authored individual be recruited more than once by the same owner?** Per
   character; each authored individual once per owner.
3. **Should `/spawn` become persistent, and should existing saved bot profiles
   or temporary helpers be imported or have a separate GM test command?** Keep
   `/spawn` temporary, add the separate persistent `/companions` flow, do not
   import old profiles/helpers, and do not add a GM test-helper command.

## Implementation summary and remaining checks

Stage 2 adds stable IDs and the additive `player_companions` table, generated
recruit/list/invite/bench flows, owner-scoped persistence, a separate inventory
key, and login restoration. Active companions are saved and benched when removed
from a group or when their owner leaves. Temporary `/spawn` helpers and legacy
`bot_profiles` are not imported or changed.

The current code retains each recruit's generated level-1 starter loadout in
the new inventory namespace. XP catch-up and new training remain Stage 3; authored
recruits remain Stage 5. On 2026-09-23, the owner confirmed all Stage 2 runtime
acceptance checks passed in a separate Windows acceptance installation. The
invite flow was rechecked after fixing persistence of newly generated unique
starter-gear records. This closes the Stage 2 runtime gate; broader Stage 6
integration checks remain separate.
