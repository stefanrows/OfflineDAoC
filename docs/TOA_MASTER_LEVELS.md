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
   discipline. Companions in the group whose owner is enrolled join the trials
   with him (see [Bots](#bots)).
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
`MercenaryManager`, `GaherisLoot`). Companions and world bots: next section.

## Bots

Package WP2.4 (owner decision 2026-10-24 19:10). Implemented in source,
real-client check pending. Three kinds of bot, three answers:

| Kind | Master Levels |
|---|---|
| Persistent companion (`GameBot` with an owner, `player_companions`) | enrolled with the owner at the Arbiter, earns from the owner's kills, saved on its record |
| Autonomous world bot (`GameBot` without an owner, `offline_world_bots`) | rolls a Master Level once on load at level 40+, earns from its own kills, saved on its record |
| Temporary `/spawn` helper, battleground encounter bot | never |

**Companions.** When the Arbiter enrols a group, every companion in the
speaker's group within 2,000 units, level 40 or higher, whose owner is now
enrolled, starts at Master Level 1 with a discipline chosen by its role. The
owner reads "Your companions begin the trials with you: Name (Warlord), ...".
A companion recruited later joins on the owner's next word with the Arbiter.
Companions earn the same Master Level experience as their owner from every
kill the owner earned by the player rule (the owner is in the kill's earner
list whenever his companion fought): the owner alive and within experience
range, the companion too, the companion below Master Level 10. An owner at
Master Level 10 earns nothing more, but his companions keep earning.
The owner reads "Name has reached Master Level N!" on each level; there is no
message per kill.

**World bots.** A world bot of level 40 or higher that has never been on the
path rolls a Master Level when it loads, from the weights in
`bot_ml_distribution`, and takes the discipline of its class. The roll is
saved with its next state snapshot and never repeated. Level 40-49 bots are
capped at (level - 40) / 3, so Master Level 0-3. A world bot earns from every
kill it damaged (directly or through its pet) and from the kills of world bots
in its group, by the same kill rule as players. A world bot in a player's
group earns only from its own damage or its world-bot mates', not from the
players' kills.

**Discipline by role.** One table in `BotMasterLevels`:

| Discipline | Classes |
|---|---|
| Warlord (tank) | Armsman, Paladin, Reaver, Warrior, Thane, Valkyrie, Hero, Champion |
| Battlemaster (melee) | Mercenary, Heretic, Mauler, Berserker, Savage, Skald, Blademaster, Valewalker, Vampiir |
| Perfecter (healer) | Cleric, Friar, Healer, Shaman, Druid, Warden, Bard |
| Spymaster (stealther) | Infiltrator, Scout, Minstrel, Shadowblade, Hunter, Nightshade, Ranger |
| Convoker (pet caster) | Theurgist, Cabalist, Necromancer, Spiritmaster, Bonedancer, Enchanter, Animist |
| Stormlord (nuker) | Wizard, Sorcerer, Runemaster, Warlock, Eldritch, Mentalist, Bainshee |

A companion's chosen role goes first: a healing class set to Healer walks
Perfecter (a Paladin played as healer), a tank class set to Tank walks
Warlord. A bot's discipline is not changed later; there is no Star of Destiny
for bots.

**Abilities.** The chosen line enters the bot's specs like a player's: only
at character level 50, only ranks up to its Master Level, and the old line
and its spell line go when the discipline changes. Master Level spells stay
out of the class spell lists (the class spell choice selects by spell type
and knows none of them); the brain has one small step of its own in the
combat think, after the priority taunt (for healers in the support role,
right after their heals), and one out of combat while the bot stands (traps,
storms before contact). It presses at most one ability per think, with a
reaction chance and a reaction delay (WP2.6, "Pacing" below), never on every
frame:

| Ability | Discipline, rank | When a bot presses it | Chance per think |
|---|---|---|---|
| Guided Strike, Energizing Aura, Warguard | Warlord 5, 6, 10 | in reach of its target and the buff is not running | 25% |
| Bolstering Battlecry | Warlord 2 | a group member within 750 is below 60% health | 50% |
| Sapping Strike, Power Leak | Battlemaster 1, 3 | the target is a player or bot in range with 30%+ endurance / power | 20% |
| Purging Wave, Purify Vision | Perfecter 1, 2 | a group member in range is diseased / nearsighted | 60% |
| Battlewarder | Convoker 6 | in a fight: an enemy player or bot hits a group mate in melee within 2,200 while nobody hits the bot | 35% |
| Crystal Titan | Convoker 10 | in a fight: 3 or more enemy players or bots within 1,500 and no titan of its own up | 25% |
| Power Trap, Dissonance Trap | Convoker 3, 7 | out of combat: holding an RvR spot, no own trap within 500 | 25% |
| Tanglesnare, Poison Spike | Spymaster 5, 6 | the same as the Convoker traps | 25% |
| Siege Wrecker | Spymaster 8 | an enemy siege weapon within 300, no own Siege Wrecker beside it | 50% |
| Dazzling Array | Stormlord 1 | in a fight: 2 or more group mates fighting within 300 and nobody hit the bot for 8 seconds; out of combat: holding an RvR spot with 2 or more mates within 300 and enemies in sight (3,000) but none within 800 | 30% |
| Enervating Gas, Inebriating Fumes, Mental Siphon, Choking Vapors, Sense Dulling Cloud, Energy Tempest | Stormlord 3, 4, 5, 7, 8, 9 | out of combat: holding an RvR spot, 2 or more enemies in sight but none within 800; in a fight: 2 or more enemies clustered at the bot's feet and nobody hit it for 8 seconds. Never a second storm of the same kind or a third own storm within 700 | 30% |
| Vacuum Vortex | Stormlord 2 | one of its own hostile storms is within 1,000 of 2 or more clustered enemies it does not cover yet | 35% |

Every other ability is held but never pressed by the AI: Defending Martyr
(costs the caster 90% of its health), Cowering Bellow, Resilient Will,
Cleansing Aura, Leadership, the rest of Battlemaster (Grapple refuses NPC
targets, Throw Weapon needs a player caster, the essence debuffs are legacy
effects), Perfecter's fonts, Prescience Node and Speedwarp (their pulse
spells have no effect in this core), the item summons (wood, warcrystal,
Essence Flare), Brittle Guard and Summoning Mastery (player pets only),
Focusing Winds, Arcing Power, Decoy, Lookout and the Blanket.

**Pacing (WP2.6, owner rule 2026-10-24).** A bot looks like a player at the
keys, not a machine:

| Rule | Value |
|---|---|
| Noticing the moment | the chance per think in the table above, at most two abilities judged per think; an ability judged pointless rests 4-8 seconds before it is judged again |
| Reaction delay between deciding and pressing | 1-3 seconds, random; the situation is judged again at the press and the idea is dropped if it has passed or the bot stayed busy 4 seconds past it |
| Abilities per think | at most one, and one decision at a time |
| Pause after a press | the cast time plus 8-16 seconds before the next decision (3 seconds after a dropped idea) |
| Cooldowns | the spell's own recast timer, set by the spell handler as for players; never pressed while it runs or without the power |
| Battle Warder | the warder stays only while its caster neither moves nor casts, so the bot stands and watches for 8-15 seconds unless it is hit or below half health |
| Holding a spot | standing within 64 of one point for 6 seconds, the group leader not walking off; RvR regions and zones only |

**Ground targets.** A bot sets its ground target as a player does, only for
the two abilities that use one. Battlewarder goes at the feet of the
defended group mate, one step (60) toward the attacker. Vacuum Vortex goes
150 behind the bot's own storm on the side away from the enemy cluster, so
the push carries the storm into them. In one press out of five the point
lands 40-120 off, a player's aim. Every point is moved onto the walkable
floor (navmesh closest point, as the movement code does), must be in sight
of the bot (navmesh ray) and within the spell's range (2,300); otherwise the
ability is skipped. Fonts, traps, storms and the titan have the target Self
and range 0 in the data and appear at the caster's feet, for players as for
bots, so the bot places them by where it stands: before contact at the spot
it holds, in a fight only when the enemies are at its feet.

**Fonts, traps and storms for every caster (WP2.6).** These handlers ran on
the legacy `GameSpellEffect`, whose timers never start in this core: a font,
trap or storm pulsed once when it appeared and then stood in the world,
inert, until the server restarted, and the Crystal Titan never left. Now:

- A placed object lives on its own ECS effect: it pulses every spell
  frequency (3 seconds for storms and traps, 10 for Dazzling Array), ends
  after the spell duration (10-20 minutes), is deleted then, pauses while its
  caster is dead and ends when the caster leaves the world. A trap ends when
  it goes off.
- `GameFont` and `GameStorm` take any caster as owner, so companions and
  world bots can cast them; messages reach players only.
- Pulses hit players and bots alike. A friendly font helps only the caster's
  side; it used to help every player in range, enemies included. The hostile
  fonts (Dissonating Ward, Prescience Node) reach enemies only. Every living
  in range gets its own pulse: one shared sub-spell used to keep its first
  target for good.
- The storm stat effects (Dazzling Array's miss chance, the Dex/Qui, Str/Con
  and acuity debuffs) apply while a living stands in the storm and come off
  4-11 seconds after it leaves; the legacy path applied them once and never
  took them back. Endurance, power and damage storms count as attacks.
- Vacuum Vortex pushes every storm near its ground target 300-750 directly
  away from that point, as the Herald describes; it pushed only the caster's
  own storms around the caster, in a random direction, once per storm found.
- Battlewarder now checks the ground target's range (2,300). The warder and
  the Crystal Titan attack enemy bots as well as players and pets. The titan
  grows for 10 seconds, fights, and leaves after 5 minutes; its kills pay
  its caster like any pet's.
- Siege Wrecker reacts only to enemy siege within its own 350 radius (it
  searched the whole visibility range) and goes away after firing.

**Changes for players.** Master Level areas now take bots like players:
`MasterlevelHandling.SelectTargets` adds companions and world bots (not pets,
not ordinary NPCs) to every radius and ground-target selection, so a player
Warlord's battle cry also heals the companions beside him and a Banelord's
pulse also hits the enemy realm's bots. Bolstering Battlecry heals bots;
Sapping Strike no longer fails for a caster without a client. Defending
Martyr takes its 90% of the caster's health once per cast; it used to be
taken once and again for every healed target, so with companions in range a
player Warlord ended near 1 health.

**Blanket of Camouflage.** Companions and other bots in the caster's group are
hidden exactly like players: each has its own effect, is watched for moving,
attacking and casting, and is revealed when the Blanket ends. A bot with the
Stealth spec follows its normal stealth rules. The bot brain no longer drops
the stealth of an idle bot that carries a Blanket, stealther or not, so a
stealther companion stays hidden like a player stealther. Pets stay visible, as on
live.

**Saves.** New columns, added by the schema reconciler at startup:

| Table | Column | Old rows |
|---|---|---|
| `player_companions`, `offline_world_bots` | `MLLevel` (int) | 0 |
| | `MLExperience` (long) | 0 |
| | `MLLine` (text, nullable) | null: off the path |

`MLLine` holds the discipline name, not the 0-7 index: a new integer column
defaults to 0, which is Banelord.

**Server property.**

| Key | Category | Default | Meaning |
|---|---|---|---|
| `bot_ml_distribution` | `autonomous` | `28,16,13,11,9,7,5,4,3,2,2` | Weights for Master Level 0-10 of a level 50 world bot's one roll: about two thirds 0-3, about one in nine 7-10. An invalid list uses the default |

**Source.** `bots/BotMasterLevels.cs` (table, enrolment, roll, experience,
which abilities), `bots/GameBot.MasterLevels.cs` (state, save, line),
`bots/BotBrain.MasterLevels.cs` (the combat and standing steps),
`bots/BotMasterLevelCasting.cs` (pacing, placement and skip rules, the
pending press, the decision loop `BotMasterLevelStep`), `spells/Masterlevel/MasterLevelPlacement.cs` (the placed
object effect and the storm stat effect), plus hooks in
`MasterLevelProgression`, `GameBot.LoadClassSpecializations`,
`LiveMasterLevelsSpecialization`, `BotBrain.cs` (one call for the standing
step), `MasterLevelBase.cs`, `Convoker.cs`, `Stormlord.cs`, `Spymaster.cs` and
`Perfecter.cs`. Tests: `UT_BotMasterLevels.cs`, `UT_BotMasterLevelCasting.cs`
(WP2.6) and four Blanket tests in `UT_MasterLevelHolds.cs`.

## Not checked

- No server was started and no real client was used. Popups, the Master Level
  window, the skill window refresh and the clickable discipline words are
  unverified in game.
- The unsolicited Master Level window packet on enrolment, level-up and login
  is what gaheris sends; whether the client opens the window or only updates
  the bar is unmeasured.
- Pacing at our rates is unmeasured in play.
- Bots (WP2.4): no bot was seen enrolling, levelling, casting a Master Level
  ability or hiding under a Blanket in a running server. The Warlord buffs as
  stat effects on bots, the drains on bot targets and the Perfecter cures are
  unit-tested only as far as the choice; their handlers ran for players only
  before. CPU cost of the combat step is unmeasured (it returns at once for
  bots without a usable ability).
- Bots (WP2.6): no Convoker, Stormlord or Spymaster bot was watched casting.
  The placed-object effects, the storm stat effects, the Vortex push, the
  titan's life and the Battle Warder hold are unit-tested and reasoned from
  the effect service, not seen in a running server. Perfecter fonts now pulse
  for players too; whether each font's own pulse spell does what its tooltip
  says is unchecked. Storm cast times (20 seconds) make the bots cast them
  mostly before contact; how often that happens in real RvR is unmeasured.
