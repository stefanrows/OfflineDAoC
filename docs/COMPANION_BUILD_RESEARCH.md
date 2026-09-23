# Companion build research, Stage 1

Status: Stage 1 research complete to proposal-or-gap standard for all 39
classes. Two classes still lack numeric endgame templates; three dated forum
variants remain unvalidated for automatic plans. Most level-by-level schedules
are explicit gaps, and local runtime checks remain a pre-activation gate.
Updated 2026-09-23.

This is research input for a future persistent companion system. It does not
change the temporary /spawn contract or add automatic build plans. The proposal
source is primarily community advice for Uthgard, whose official FAQ says it
targets patch 1.65. That makes these useful emulator recommendations, not proof
of retail-era popularity or historical 1.65 practice. The realm guide pages do
not show publication dates. Uthgard-specific balance and implementation claims
are identified below instead of being carried over as server facts.

## Point and skill checks

For a specialization trained to rank n, the server's point cost is
n(n+1)/2 - 1, since the line starts at rank 1. The level-50 budgets below were
calculated using the companion training loop in GameBot and each class's
SpecPointsMultiplier. They grant no autotrain points. The budgets are 1,494 for
multiplier 10, 2,223 for 15, 2,979 for 20, 3,253 for 22, and 3,706 for 25.

All class-line names in the 39-class catalog were cross-referenced with the
class career rows in the public OpenDAoC database dump, and all 108 ranked
lines in the 34 numeric proposals were cross-referenced with its career,
ability, spell-line, spell, and style tables. The named lines and active skill
tiers exist in that reference snapshot. Parry and Stealth entries are passive
lines, so they do not have a spell, style, or specialization-ability tier.
This is a static cross-check against the upstream public database, not the
fork's unpinned local runtime database. The local career and skill records still
need confirmation before these become automatic plans.

Three dated forum examples in the Blademaster, Hero, and Warrior rows below are
not included in the 108-ranked-line check. Their no-autotrain point totals are
calculated, but the exact skill tiers still need reference and local-runtime
validation before any of them can become an automatic plan.

The current class career and skill records are loaded from the server database
by SkillBase. The checkout has no clean, pinned database snapshot to use for a
local runtime comparison. The reference database is maintained separately by
OpenDAoC and described as an OpenDAoC database dump. Role labels below summarize
the cited build's stated focus; where the source does not name a role, the label
is an inference from its skill priorities.

## Leveling milestone coverage

The realm guides give class-specific leveling priorities for all 39 classes,
usually in relative terms such as keeping a weapon or support line near
character level. The dated Healer forum post is the only source in this pass
with a detailed level-by-level specialization route. The Minstrel guide
identifies a level-24 Instruments/autotrain charm milestone for human
characters; companions do not receive autotrain points, so that milestone is
context rather than a companion training step. The other 37 classes have no
sourced numeric level-by-level schedule in this source set. No missing schedule
has been interpolated. These gaps block an automatic per-level plan until
researched or replaced by an explicitly approved server-specific plan.

## Albion

| Class | Leveling focus and role | Endgame candidate and point check |
| --- | --- | --- |
| Armsman | Keep Polearm or Two Handed near level; a shield-and-weapon route is also offered. Tank and group guard. | 50 Polearm, 42 Shields, 39 Slash, 5 Parry: 2,969/2,979 (M20). Source allows 30–39 in the weapon line. |
| Cabalist | Put 3 in Spirit for pet power recycle, then focus Matter for pet-focus pulls and damage-over-time. Pet caster and farmer. | 34 Body Magic, 33 Spirit Magic, 25 Matter Magic: 1,478/1,494 (M10). |
| Cleric | Favor Enhancement for group buffs and add some Rejuvenation for healing. Healer and buffer. | 42 Enhancement, 33 Rejuvenation: 1,462/1,494 (M10). |
| Friar | Keep Staff close to level and invest remaining points in Enhancement. Melee support and healer. | Group candidate: 45 Enhancement, 29 Staff, 34 Rejuvenation, 17 Parry: 2,214/2,223 (M15). The source's remaining points go to Parry. |
| Infiltrator | Keep weapon and Envenom near level; use spare points in Dual Wield. The guide suggests player Stealth autotrain. Assassin. | 50 Thrust, 44 Critical Strike, 36 Stealth, 36 Envenom, 14 Dual Wield: 3,697/3,706 (M25). Slash is an alternate weapon choice. |
| Mercenary | Keep Dual Wield at level, then train the weapon line. Dual-wield damage with an optional shield stun. | 50 Dual Wield, 42 Shields, 36 Slash, 16 Parry: 2,976/2,979 (M20). Thrust or Crush can replace Slash. |
| Minstrel | Either train Instruments for early songs or follow the guide's autotrain route; its charm route calls out level 24. Song, charm, and group utility. | The guide's 50 Instruments/44 Thrust costs 2,263/2,223 without autotrain. Companion candidate: 50 Instruments, 43 Thrust, 2,219/2,223 (M15). |
| Necromancer | A 2014 Uthgard forum discussion recommends Deathsight for leveling and Death Servant 50 for farming. Pet caster and solo farmer. | A cited farm variant is 48–50 Deathsight, 9 Painworking, 18 Death Servant: 1,389–1,488/1,494 (M10). The current BotSpec choices omit Death Servant, so this is not yet an automatic-plan candidate. |
| Paladin | Keep a weapon line around level while adding Shields and Chants. Defensive tank and group support. | 46 Chants, 44 Thrust, 42 Shields: 2,971/2,979 (M20). |
| Reaver | The guide uses Flexible autotrain to afford its endgame split. Shield control and melee pressure. | Source: 50 Flexible, 42 Shields, 41 Soulrending, 6 Parry, which costs 3,056/2,979 without autotrain. Companion candidate: 50 Flexible, 42 Shields, 39 Soulrending, 5 Parry: 2,969/2,979 (M20). |
| Scout | Level with a melee weapon and Shield or focus Bow; the guide also describes player Bow autotrain. Ranged damage and scouting. | 50 Longbows, 42 Shields, 35 Stealth, 18 Slash: 2,975/2,979 (M20). Thrust is an alternate weapon line. |
| Sorcerer | Train enough Mind to charm the desired pet, then Matter for damage-over-time leveling. Crowd control, debuffs, and ranged damage. | 39 Body Magic, 37 Mind Magic: 1,481/1,494 (M10). |
| Theurgist | Full Ice is the guide's leveling focus. Pet interrupts and caster support. | 40 Earth Magic, 36 Wind Magic: 1,484/1,494 (M10). The guide says “Air”; the server career key is Wind Magic. |
| Wizard | Ice is suggested for group leveling and Fire for soloing. | The guide gives no numeric endgame allocation. Fire is its PvP direction, but its bolt-performance claims describe Uthgard and are not evidence about this server. Exact template is an open gap. |

## Midgard

| Class | Leveling focus and role | Endgame candidate and point check |
| --- | --- | --- |
| Berserker | Keep Left Axe at level, then train a weapon line. Dual-wield melee damage. | 50 Sword, 50 Left Axe, 28 Parry: 2,953/2,979 (M20). The source allows a different weapon choice. |
| Bonedancer | Suppression near level for the guide's pet, damage-over-time, and lifetap leveling loop. Pet caster with sustain. | 47 Suppression, 26 Darkness: 1,477/1,494 (M10). The guide's “one implemented pet” note is Uthgard-specific. |
| Healer | The 2013 forum source gives a point-by-point route: Mending early, then branch toward Pacification or Augmentation around level 40. Main healer and crowd control. | Pacification route: 38 Pacification, 33 Mending, 19 Augmentation: 1,489/1,494 (M10). A separate Augmentation route is also provided by the source. |
| Hunter | Keep either melee weapon or Bow near level and put remaining points in Beastcraft. Pet and ranged/melee hybrid. | 50 Beastcraft, 44 Spear, 36 Stealth, 9 Composite Bow: 2,972/2,979 (M20). |
| Runemaster | The guide favors Darkness for leveling and group damage; its bolt warning is Uthgard-specific. Ranged damage and spell utility. | Source's 47 Darkness/27 Suppression costs 1,504/1,494 in the server's no-autotrain budget. Companion candidate: 47 Darkness, 26 Suppression: 1,477/1,494 (M10). |
| Savage | Keep Hand to Hand at level and use the rest in Savagery. Melee damage with self-buffs. | 49 Savagery, 44 Hand to Hand: 2,213/2,223 (M15); remaining points favor Parry. |
| Shadowblade | Keep Left Axe at level and Envenom within five ranks of character level; the source suggests leaving Stealth untrained for player autotrain. Assassin. | 50 Left Axe, 35 Sword, 36 Stealth, 36 Envenom, 5 Critical Strike: 3,247/3,253 (M22). |
| Shaman | Higher Augmentation for groups; higher Cave for solo leveling. Buffer, healer, and ranged damage-over-time. | 46 Augmentation, 27 Subterranean, 8 Mending: 1,492/1,494 (M10). |
| Skald | Keep Battlesongs near level and favor Hammer for its early low-endurance style. Group speed and melee support. | 46 Battlesongs, 44 Hammer, 17 Parry: 2,221/2,223 (M15). |
| Spiritmaster | Summoning and pet focus for solo leveling; Suppression for group PBAoE leveling. Pet caster and group damage. | 47 Darkness, 26 Suppression: 1,477/1,494 (M10). |
| Thane | Keep Stormcalling high and add Hammer and Shield for melee and defense. Hybrid tank and support damage. | 46 Stormcalling, 42 Shields, 39 Hammer, 20 Parry: 2,970/2,979 (M20). |
| Warrior | Keep a weapon line at level; use remaining points in Shields and Parry. Defensive tank. | A dated forum option is 50 Sword, 42 Shields, 39 Hammer: 2,955/2,979 (M20). This human group variant needs role and ranked skill-tier validation. |

## Hibernia

| Class | Leveling focus and role | Endgame candidate and point check |
| --- | --- | --- |
| Animist | Creeping for PvE turret farms; the guide only suggests some Arboreal for a PvP turret. Turret caster and area damage. | No numeric endgame allocation is supplied. Exact template is an open gap. |
| Bard | Keep Nurture at level for group buffs, then add Regrowth and later Music for crowd control. Group support and healer. | 43 Nurture, 37 Music, 33 Regrowth: 2,207/2,223 (M15). |
| Blademaster | Keep a weapon line at level and put extra points into Celtic Dual. Dual-wield melee damage. | A dated forum option is 50 Celtic Dual, 42 Shields, 39 Piercing: 2,955/2,979 (M20). Role and ranked skill tiers need validation before an automatic plan. |
| Champion | Max Valor while leveling, then Large Weapons; Shield is another option. Hybrid tank, debuffs, and interrupts. | 50 Valor, 42 Shields, 39 Large Weapons: 2,955/2,979 (M20). |
| Druid | Nurture supports grouping; Nature is an alternate solo-leveling focus, with Regrowth for healing. Healer and buffer. | 42 Nurture, 33 Regrowth, 7 Nature: 1,489/1,494 (M10); or 40 Nurture, 35 Regrowth, 9 Nature: 1,492/1,494. |
| Eldritch | Light for single-target leveling; Mana for group PBAoE. Ranged damage, crowd control, and debuffs. | Light: 46 Light/28 Mana, 1,485/1,494; Mana: 50 Mana/20 Light, 1,483/1,494 (M10). |
| Enchanter | Keep Mana high for pet-focus leveling. Pet support and caster damage. | 49 Mana, 22 Light: 1,476/1,494 (M10). |
| Hero | Keep Large Weapons or Celtic Spear at level; use remaining points for Shield or Parry. Defensive tank and melee damage. | A dated forum option is 50 Shields, 44 Celtic Spear, 12 Blades, 35 Parry: 2,969/2,979 (M20). This human group variant needs role and ranked skill-tier validation. |
| Mentalist | Mana for group leveling, regeneration, and area damage-over-time. Healer support and ranged damage. | 46 Light, 28 Mentalism, 4 Mana: 1,494/1,494 (M10). |
| Nightshade | Keep weapon and Poison at level; spend leftovers in Celtic Dual. The guide suggests player Stealth autotrain. Assassin. | 44 Critical Strike, 36 Piercing, 35 Stealth, 35 Envenom, 25 Celtic Dual: 3,236/3,253 (M22). Blades is an alternate weapon line. |
| Ranger | Keep Pathfinding one to three ranks below level and the weapon near level; the guide says Bow can autotrain. Ranged/melee hybrid with self-buffs. | The guide's melee and hybrid templates cost 3,056 and 3,026 against a 2,979 no-autotrain budget (M20). Companion candidate: 42 Celtic Dual, 40 Pathfinding, 35 Piercing, 33 Stealth, 11 Recurve Bow: 2,975/2,979. |
| Valewalker | Keep Scythe at or one below level, then train Arboreal. Melee and spell hybrid. | 50 Scythe, 38 Arboreal Path, 20 Parry: 2,223/2,223 (M15). |
| Warden | Weapon at or near level for solo play; Nurture for group support. Defensive melee and group support. | 49 Nurture, 33 Regrowth, 25 Blades, 14 Parry: 2,212/2,223 (M15). |

## Findings and explicit planning gaps

- The realm guide pages cover 38 of 39 classes; the dated 2014 Uthgard Necromancer discussion supplies a separate Necromancer source. None of these community sources establishes retail-era popularity.
- Four source templates need companion-specific no-autotrain adjustments: Minstrel, Reaver, Runemaster, and Ranger. These are research candidates only; no BotSpec plan was changed.
- The Theurgist guide says Air while the server uses the career key Wind Magic. The allocation can be mapped to that key.
- Animist and Wizard have no numeric endgame template in the reviewed sources. Their automatic plans remain blocked by explicit research gaps.
- Dated forum discussions add numeric examples for Blademaster, Hero, and Warrior, but these are role-specific human PvP builds and are not part of the 34-proposal skill-tier check. Their automatic plans remain blocked until an AI role is selected and ranks are validated.
- Apart from the detailed Healer route, the source set does not provide complete per-level specialization schedules. The Minstrel level-24 autotrain/charm milestone is human-specific and does not transfer to companions.
- The public database cross-check is not pinned to this checkout and cannot prove that the local runtime loads the same career, spell, ability, and style rows. Confirm those with a disposable clean database before enabling any automatic plan.
- Proposals are not final build-plan decisions. Stage 2 roster ownership, recruitment, limits, and /spawn compatibility remain separate open gates in the roadmap.

This research satisfies Stage 1's proposal-or-gap acceptance. The listed numeric,
milestone, and local-runtime gaps remain explicit gates for any automatic plans;
no build plan is enabled by this document.

## Sources

- [Uthgard patch-level FAQ](https://www.uthgard.net/howto) — official statement that Uthgard targets patch 1.65; it establishes the emulator context, not the historic provenance of its guides.
- [Albion class guide](https://uthgard.blogspot.com/p/albion-class-guide.html), [Midgard class guide](https://uthgard.blogspot.com/p/midgard-class-guide.html), and [Hibernia class guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) — undated community recommendations; emulator-specific observations are not treated as retail facts.
- [Healer advice and leveling route](https://www.uthgard.net/forum/viewtopic.php?f=61&t=30306) — forum posts dated April 21, 2013; community advice with an explicit level-by-level Pacification/Augmentation path.
- [Necromancer specialization discussion](https://www.uthgard.net/forum/viewtopic.php?p=331300) — forum posts dated February–April 2014; community advice on Deathsight leveling and Death Servant farming.
- [Blademaster spec options](https://www.uthgard.net/forum/viewtopic.php?p=122426), [Hero group spec discussion](https://www.uthgard.net/forum/viewtopic.php?f=62&t=31454), and [Warrior spec discussion](https://www.uthgard.net/forum/viewtopic.php?t=11512) — dated Uthgard player recommendations from 2009–2013; candidate examples only, not verified companion plans.
- [OpenDAoC database dump](https://github.com/OpenDAoC/OpenDAoC-Database), especially ClassXSpecialization, SpecXAbility, SpellLine, LineXSpell, and Style — used as an upstream static reference, not a substitute for this fork's local runtime database.
- Server implementation references: [BotSpec.cs](../source/server/GameServer/bots/specs/BotSpec.cs), [GameBot.cs](../source/server/GameServer/bots/GameBot.cs), [SkillBase.cs](../source/server/GameServer/gameutils/SkillBase.cs), and the class multiplier definitions under [playerclasses](../source/server/GameServer/playerclasses/).
