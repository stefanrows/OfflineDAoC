# Trials of Atlantis: Master Level progression (WP2.2)

Package WP2.2 of [TOA_PLAN.md](TOA_PLAN.md), adapted from gaheris
`scripts/progression/MasterLevels.cs` and `scripts/toa/MasterLevelsMerchant.cs`
(fit report: [TOA_GAHERIS_FIT.md](TOA_GAHERIS_FIT.md)). Ordinary GameServer
source, no boot-compiled scripts. Status: implemented in source, real-client
check pending.

Owner rules (2026-10-24): Master Level experience comes from any NPC of level
45 or higher, anywhere, not only in Atlantis. Each kill pays its level x
`ml_xp_per_level`. A Master Level costs 32,000. The Arbiter enrols a group
together. Demyphon sells credits and the respec token for bounty points.

## What a player does

1. **Get on the path.** Speak to the Arbiter at level 40 or higher (live: the
   trials began at 40). He enrols the speaker and every group member within
   2,000 units who is level 40 or higher. Enrolment sets Master Level 1 and no
   discipline. Companions are not players and are never enrolled.
2. **Choose a discipline.** While none is chosen, the Arbiter lists the
   disciplines of the player's class career as clickable words. Clicking or
   whispering one, for example `Convoker`, takes it up. A discipline is
   exclusive: once one is walked, the Arbiter refuses another and points to
   Demyphon's Star of Destiny.
3. **Earn Master Level experience.** Every NPC kill of level 45 or higher pays
   level x `ml_xp_per_level` to every enrolled player who earned the kill:
   every player attacker, the owners of attacking pets and companions, and
   their group members in experience range. Dead players, players out of
   experience range, and players at Master Level 10 get nothing. Each payment
   shows "You gain N Master Level experience."
4. **Level up in the field.** When the 32,000 bar is full, the next Master
   Level is granted at once with "You have reached Master Level N!". The
   overshoot carries over. The Master Level line is refreshed and the skill
   window updated without relogging. gaheris made the player walk back to the
   Arbiter for every level; live granted the level on finishing the trial, so
   the field grant is closer to live. The Arbiter still settles a full bar if
   one is left over from an older save.
5. **Use the abilities.** Only the chosen line enters the spec list, and only
   at character level 50, because the career rows grant the Master Level specs
   at level 50. The line holds every ability whose rank is at most the Master
   Level. Master Level lines are career specialisations: they are never
   trainable with spec points and never appear in the trainer window.
6. **Buy levels or respec at Demyphon.** He sells the list "Master Level
   Credits" for bounty points. Handing back the credit for the next Master
   Level grants that level and keeps the experience already earned. A credit
   for another level is refused and stays in the bag. Handing back the Star of
   Destiny sets the discipline aside: the line's abilities go, Master Levels
   and experience stay, and the Arbiter offers the choice again. The line's
   spell line is removed with its spec, so passive effects that look the line
   up by name end at once: Perfecter's +20% concentration at Master Level 4
   and Spymaster's poison charge saver.
7. **Title.** The Master Level title names the chosen discipline. It appears
   when a discipline is chosen and goes with a Star of Destiny, without a
   relog. An enrolled player without a discipline has no title.
8. **Check the state.** `/ml` shows the Master Level, experience toward the
   next level with the percentage, what a kill pays, the discipline and the
   abilities held.

Kills that pay nothing: NPCs below level 45, every `GameBot` victim
(companions, helpers, autonomous world bots), and every NPC the normal kill
rewards refuse (`RewardStatus` not Eligible, the same gate as
`AbstractServerRules.OnNpcKilled`): controlled pets, pets released from a
charm within the last minute, Convoker Battle Warders and moving objects.
Player kills never pay Master Level experience.

## Pacing

| Kill | Master Level experience | Kills per Master Level |
|---|---|---|
| level 45 | 450 | 72 |
| level 50 | 500 | 64 |
| level 60 | 600 | 54 |

With the default of 10, Master Level 1 to 10 takes about 580 kills of level 50
creatures per player after the free first level. A group of eight earns in
parallel: every member in range is paid the full amount.

## Server property

| Key | Category | Default | Meaning |
|---|---|---|---|
| `ml_xp_per_level` | `progression` | 10 | Master Level experience per level of a killed NPC of level 45 or higher. 0 turns kill experience off; credits still work |

The property is declared on `MasterLevelProgression` and is found by the
normal server property loader.

## GM commands

| Command | Behaviour |
|---|---|
| `/player startml` | Enrols the target like the Arbiter: Master Level 1, no discipline. No level gate |
| `/player mlxp <amount>` | Adds Master Level experience through the same path as kills, so a full bar grants levels. Refused for a player who has not started |
| `/player setml <level>` | Sets the Master Level and clears the experience, then refreshes the line |
| `/player setmlline <discipline\|0-7\|none>` | Sets the discipline by name or index, `none` clears it |
| `/player setmlstep` | Unchanged; trial steps are not used by this progression |

## Discipline index

`MLLine` in the save stores the discipline's position in this fixed list, not
its position in the class career. The career is built from database row order,
so a career index could name another discipline after a data reload. 255 means
no discipline.

| MLLine | Discipline |
|---|---|
| 0 | Banelord |
| 1 | Battlemaster |
| 2 | Convoker |
| 3 | Perfecter |
| 4 | Sojourner |
| 5 | Spymaster |
| 6 | Stormlord |
| 7 | Warlord |
| 255 | none chosen |

A character that got `MLGranted` from the old GM command keeps Master Level 0
and counts as having no discipline until it chooses one. Before this package no
code set a discipline, so no save holds a meaningful old index.

## What the data packs must contain

| Data | Source | State in the dev snapshot |
|---|---|---|
| `specialization` rows for the eight lines with Implementation `DOL.GS.LiveMasterLevelsSpecialization` | stock | present |
| `classxspecialization`: the eight lines for every class at level 50 | gaheris sql/39 (WP2.1) | missing. Without them the Arbiter says no discipline is open to the class |
| `spellline` rows for the eight lines | stock | present |
| `spell` and `linexspell` rows, spell level = Master Level rank | gaheris sql/12, 42, 43 (WP2.1) | missing; 73 of 80 ranks exist in gaheris |
| Arbiter mob rows with ClassType `DOL.GS.Scripts.GaherisArbiter` or `DOL.GS.MasterLevelArbiter` | gaheris sql/13 and 35 | not placed. A row that stays `DOL.GS.Arbiter` keeps the lore-only Arbiter |
| Demyphon mob rows with ClassType `DOL.GS.Scripts.MasterLevelsMerchant` | gaheris sql/13 and 35 | not placed. A row without a list gets "Master Level Credits" |
| Merchant list "Master Level Credits" and the eleven `master_level_*_bptoken*` templates | stock | present, prices 1,500 to 5,000 bounty points |

The class names a gaheris row uses all resolve: `DOL.GS.Scripts.GaherisArbiter`
is a thin subclass of `DOL.GS.MasterLevelArbiter`, and
`DOL.GS.Scripts.MasterLevelsMerchant` keeps the gaheris name. The plain
`DOL.GS.Arbiter`, `DOL.GS.Researcher` and `DOL.GS.HeadScholar` are unchanged.

The credit for Master Level 1 has no use, because enrolment already grants
Master Level 1. Demyphon refuses it and the player keeps the token. A data pack
may drop it from the list.

## Source

| File | Role |
|---|---|
| `GameServer/gameutils/MasterLevelProgression.cs` | enrolment, choice, respec, experience, kill listener, login refresh, property |
| `GameServer/gameobjects/Atlantis/MasterLevelArbiter.cs` | the Arbiter NPC and the `GaherisArbiter` alias |
| `GameServer/gameobjects/Atlantis/MasterLevelsMerchant.cs` | Demyphon |
| `GameServer/gameobjects/Atlantis/Arbiter.cs` | one hook so the Master Level Arbiter sends one popup instead of three |
| `GameServer/gameobjects/GamePlayer.cs` | `LoadClassSpecializations` keeps only the chosen line, by fixed index, and removes the spell line of a dropped one |
| `GameServer/playertitles/titles/level/MasterlevelTitle.cs` | title from the chosen discipline, dropped on respec |
| `GameServer/commands/playercommands/MasterLevelCommand.cs` | `/ml` |
| `GameServer/commands/gmcommands/Player.cs` | GM commands above |
| `Tests/UnitTests/UT_MasterLevelProgression.cs` | 14 tests with a synthetic career and synthetic spells, one through the real `ProcessDeath` and Dying notify |

Dropped from gaheris: every mercenary reference (`GameMercenary`,
`MercenaryManager`, `GaherisLoot`). Companions and bots and Master Levels are
WP2.4. `GameBot.LoadClassSpecializations` already skips every Master Level
line.

## Not checked

- No server was started and no real client was used. Popups, the Master Level
  window, the skill window refresh and the clickable discipline words are
  unverified in game.
- The unsolicited Master Level window packet on enrolment, level-up and login
  is what gaheris sends; whether the client opens the window or only updates
  the bar is unmeasured.
- Pacing at our rates is unmeasured in play.
