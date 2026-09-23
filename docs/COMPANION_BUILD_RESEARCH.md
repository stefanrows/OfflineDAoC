# Companion build research and Stage 3 validation

Status: Stage 1 proposal-or-gap research covers all 39 classes. This Stage 3
record classifies all 39 candidates, records static endgame checks and available
level milestones, and lists remaining blockers. No automatic plan is available
for use.
Updated 2026-09-23.

These 1.65-era sources support review of companion build recommendations; they
do not establish retail-era popularity or historical practice. The source set
is primarily community advice for Uthgard, whose official FAQ says it targets
patch 1.65. Realm guide pages are undated. Uthgard-specific balance and
implementation claims are identified below instead of being treated as facts
about this fork.

## Candidate point and skill checks

For a specialization trained to rank n, the server's point cost is
n(n+1)/2 - 1, since the line starts at rank 1. The level-50 budgets below use
the companion training loop in GameBot and each class's SpecPointsMultiplier,
with no autotrain points: 1,494 for multiplier 10, 2,223 for 15, 2,979 for 20,
3,253 for 22, and 3,706 for 25.

The 39 class career catalogs and all 108 ranked lines in the 34 numeric
endgame candidates were cross-referenced with the public OpenDAoC database
dump's class career, ability, spell-line, spell, and style tables. The named
lines and active skill tiers exist in that snapshot; Parry and Stealth are
passive lines. Those 34 candidates' endgame point totals fit their no-autotrain
level-50 budgets. This is a static upstream-reference check. The three
Blademaster, Hero, and Warrior forum candidates have calculated point totals,
but their exact ranked skill tiers have not passed that reference check.
Animist and Wizard still lack numeric endgame candidates.

The class career and skill records used by the server are loaded by SkillBase.
No clean, pinned runtime database is present in this checkout, so local-runtime
career, skill, and plan checks are blocked for all 39 classes. The upstream
database snapshot does not substitute for a disposable copy of the fork's
runtime database. Role labels summarize the cited build's stated focus;
where a source does not name a role, the label is an inference from its skill
priorities.

## Leveling milestone coverage

The class guides give all 39 classes relative leveling priorities, but not
complete numeric schedules. The additional dated sources give partial routes
for Armsman, Sorcerer, Ranger, and Nightshade. The Healer route gives many
numeric milestones but depends on respecialization and half-level point
awards. The Minstrel's level-24 Instruments/autotrain charm point is specific
to human characters. The other 33 classes have no sourced numeric leveling
schedule in this source set. No missing schedule has been interpolated.

The source-route checks use the no-autotrain companion budget implemented in
GameBot. The Sorcerer route's listed allocations through level 32 fit its
budget; the source then calls for a level-40 respecialization and gives no
level-41–50 schedule. The Ranger guide's level-10, 20, and 30 examples fit,
while its level-40 allocation costs 1,661 points against a 1,624-point
companion budget. The Nightshade level-34 example costs 1,276/1,278 points,
but its advice assumes Stealth autotrain and respecializations at levels 20
and 40. The Armsman route gives a Crush-rank formula through level 40 and
then calls for a respec; it lacks a complete companion schedule.

The Healer route's supported whole-level milestones through level 41 fit the
current budget. Its level-44.5, 45.5, 47.5, and 49.5 checkpoints depend on
half-level points that persistent companions do not receive. Replayed at
whole levels, the listed allocations exceed the companion budget at levels
44, 45, and 47. It also calls for respecializations at levels 5, 20, and 40.
The current companion respec path is owner-eligible full respecialization,
not automatic line respec, so the cited route is not directly usable as an
automatic plan.

In every class row below, the validation column reports the static endgame and
level-route result. Local-runtime validation is blocked for all 39 classes, and
all candidates await owner review.

## Albion

| Class | Leveling focus and role | Endgame candidate and point check | Sources | Validation status |
| --- | --- | --- | --- | --- |
| Armsman | Keep Polearm or Two Handed near level; a shield-and-weapon route is also offered. Tank and group guard. | 50 Polearm, 42 Shields, 39 Slash, 5 Parry: 2,969/2,979 (M20). Source allows 30–39 in the weapon line. | [Albion guide](https://uthgard.blogspot.com/p/albion-class-guide.html); [leveling discussion](https://www.uthgard.net/forum/viewtopic.php?f=99&p=460916&t=44202) | Static endgame pass; Crush formula to 40 needs a respec and has no full route. |
| Cabalist | Put 3 in Spirit for pet power recycle, then focus Matter for pet-focus pulls and damage-over-time. Pet caster and farmer. | 34 Body Magic, 33 Spirit Magic, 25 Matter Magic: 1,478/1,494 (M10). | [Albion guide](https://uthgard.blogspot.com/p/albion-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Cleric | Favor Enhancement for group buffs and add some Rejuvenation for healing. Healer and buffer. | 42 Enhancement, 33 Rejuvenation: 1,462/1,494 (M10). | [Albion guide](https://uthgard.blogspot.com/p/albion-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Friar | Keep Staff close to level and invest remaining points in Enhancement. Melee support and healer. | Group candidate: 45 Enhancement, 29 Staff, 34 Rejuvenation, 17 Parry: 2,214/2,223 (M15). The source's remaining points go to Parry. | [Albion guide](https://uthgard.blogspot.com/p/albion-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Infiltrator | Keep weapon and Envenom near level; use spare points in Dual Wield. The guide suggests player Stealth autotrain. Assassin. | 50 Thrust, 44 Critical Strike, 36 Stealth, 36 Envenom, 14 Dual Wield: 3,697/3,706 (M25). Slash is an alternate weapon choice. | [Albion guide](https://uthgard.blogspot.com/p/albion-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Mercenary | Keep Dual Wield at level, then train the weapon line. Dual-wield damage with an optional shield stun. | 50 Dual Wield, 42 Shields, 36 Slash, 16 Parry: 2,976/2,979 (M20). Thrust or Crush can replace Slash. | [Albion guide](https://uthgard.blogspot.com/p/albion-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Minstrel | Either train Instruments for early songs or follow the guide's autotrain route; its charm route calls out level 24. Song, charm, and group utility. | The guide's 50 Instruments/44 Thrust costs 2,263/2,223 without autotrain. Companion candidate: 50 Instruments, 43 Thrust, 2,219/2,223 (M15). | [Albion guide](https://uthgard.blogspot.com/p/albion-class-guide.html) | Static endgame pass; level-24 autotrain/charm evidence is human-only. |
| Necromancer | A 2014 Uthgard forum discussion recommends Deathsight for leveling and Death Servant 50 for farming. Pet caster and solo farmer. | A cited farm variant is 48–50 Deathsight, 9 Painworking, 18 Death Servant: 1,389–1,488/1,494 (M10). The current BotSpec choices omit Death Servant, so this is not yet an automatic-plan candidate. | [Albion guide](https://uthgard.blogspot.com/p/albion-class-guide.html); [2014 discussion](https://www.uthgard.net/forum/viewtopic.php?p=331300) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Paladin | Keep a weapon line around level while adding Shields and Chants. Defensive tank and group support. | 46 Chants, 44 Thrust, 42 Shields: 2,971/2,979 (M20). | [Albion guide](https://uthgard.blogspot.com/p/albion-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Reaver | The guide uses Flexible autotrain to afford its endgame split. Shield control and melee pressure. | Source: 50 Flexible, 42 Shields, 41 Soulrending, 6 Parry, which costs 3,056/2,979 without autotrain. Companion candidate: 50 Flexible, 42 Shields, 39 Soulrending, 5 Parry: 2,969/2,979 (M20). | [Albion guide](https://uthgard.blogspot.com/p/albion-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Scout | Level with a melee weapon and Shield or focus Bow; the guide also describes player Bow autotrain. Ranged damage and scouting. | 50 Longbows, 42 Shields, 35 Stealth, 18 Slash: 2,975/2,979 (M20). Thrust is an alternate weapon line. | [Albion guide](https://uthgard.blogspot.com/p/albion-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Sorcerer | Train enough Mind to charm the desired pet, then Matter for damage-over-time leveling. Crowd control, debuffs, and ranged damage. | 39 Body Magic, 37 Mind Magic: 1,481/1,494 (M10). | [Albion guide](https://uthgard.blogspot.com/p/albion-class-guide.html); [leveling route](https://www.uthgard.net/forum/viewtopic.php?f=110&t=36619) | Static endgame pass; milestones to 32 fit; level-40 respec and post-40 route are unresolved. |
| Theurgist | Full Ice is the guide's leveling focus. Pet interrupts and caster support. | 40 Earth Magic, 36 Wind Magic: 1,484/1,494 (M10). The guide says “Air”; the server career key is Wind Magic. | [Albion guide](https://uthgard.blogspot.com/p/albion-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Wizard | Ice is suggested for group leveling and Fire for soloing. | The guide gives no numeric endgame allocation. Fire is its PvP direction, but its bolt-performance claims describe Uthgard and are not evidence about this server. Exact template is an open gap. | [Albion guide](https://uthgard.blogspot.com/p/albion-class-guide.html) | No numeric endgame candidate; line/rank and budget validation unavailable. |

## Midgard

| Class | Leveling focus and role | Endgame candidate and point check | Sources | Validation status |
| --- | --- | --- | --- | --- |
| Berserker | Keep Left Axe at level, then train a weapon line. Dual-wield melee damage. | 50 Sword, 50 Left Axe, 28 Parry: 2,953/2,979 (M20). The source allows a different weapon choice. | [Midgard guide](https://uthgard.blogspot.com/p/midgard-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Bonedancer | Suppression near level for the guide's pet, damage-over-time, and lifetap leveling loop. Pet caster with sustain. | 47 Suppression, 26 Darkness: 1,477/1,494 (M10). The guide's “one implemented pet” note is Uthgard-specific. | [Midgard guide](https://uthgard.blogspot.com/p/midgard-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Healer | The 2013 forum source gives a point-by-point route: Mending early, then branch toward Pacification or Augmentation around level 40. Main healer and crowd control. | Pacification route: 38 Pacification, 33 Mending, 19 Augmentation: 1,489/1,494 (M10). A separate Augmentation route is also provided by the source. | [Midgard guide](https://uthgard.blogspot.com/p/midgard-class-guide.html); [2013 leveling route](https://www.uthgard.net/forum/viewtopic.php?f=61&t=30306) | Static endgame pass; route requires respecs and half-level points; whole-level budget fails at 44, 45, 47. |
| Hunter | Keep either melee weapon or Bow near level and put remaining points in Beastcraft. Pet and ranged/melee hybrid. | 50 Beastcraft, 44 Spear, 36 Stealth, 9 Composite Bow: 2,972/2,979 (M20). | [Midgard guide](https://uthgard.blogspot.com/p/midgard-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Runemaster | The guide favors Darkness for leveling and group damage; its bolt warning is Uthgard-specific. Ranged damage and spell utility. | Source's 47 Darkness/27 Suppression costs 1,504/1,494 in the server's no-autotrain budget. Companion candidate: 47 Darkness, 26 Suppression: 1,477/1,494 (M10). | [Midgard guide](https://uthgard.blogspot.com/p/midgard-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Savage | Keep Hand to Hand at level and use the rest in Savagery. Melee damage with self-buffs. | 49 Savagery, 44 Hand to Hand: 2,213/2,223 (M15); remaining points favor Parry. | [Midgard guide](https://uthgard.blogspot.com/p/midgard-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Shadowblade | Keep Left Axe at level and Envenom within five ranks of character level; the source suggests leaving Stealth untrained for player autotrain. Assassin. | 50 Left Axe, 35 Sword, 36 Stealth, 36 Envenom, 5 Critical Strike: 3,247/3,253 (M22). | [Midgard guide](https://uthgard.blogspot.com/p/midgard-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Shaman | Higher Augmentation for groups; higher Cave for solo leveling. Buffer, healer, and ranged damage-over-time. | 46 Augmentation, 27 Subterranean, 8 Mending: 1,492/1,494 (M10). | [Midgard guide](https://uthgard.blogspot.com/p/midgard-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Skald | Keep Battlesongs near level and favor Hammer for its early low-endurance style. Group speed and melee support. | 46 Battlesongs, 44 Hammer, 17 Parry: 2,221/2,223 (M15). | [Midgard guide](https://uthgard.blogspot.com/p/midgard-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Spiritmaster | Summoning and pet focus for solo leveling; Suppression for group PBAoE leveling. Pet caster and group damage. | 47 Darkness, 26 Suppression: 1,477/1,494 (M10). | [Midgard guide](https://uthgard.blogspot.com/p/midgard-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Thane | Keep Stormcalling high and add Hammer and Shield for melee and defense. Hybrid tank and support damage. | 46 Stormcalling, 42 Shields, 39 Hammer, 20 Parry: 2,970/2,979 (M20). | [Midgard guide](https://uthgard.blogspot.com/p/midgard-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Warrior | Keep a weapon line at level; use remaining points in Shields and Parry. Defensive tank. | A dated forum option is 50 Sword, 42 Shields, 39 Hammer: 2,955/2,979 (M20). This human group variant needs role and ranked skill-tier validation. | [Midgard guide](https://uthgard.blogspot.com/p/midgard-class-guide.html); [forum option](https://www.uthgard.net/forum/viewtopic.php?t=11512) | Endgame point total fits; role and exact ranked skill tiers need validation. |

## Hibernia

| Class | Leveling focus and role | Endgame candidate and point check | Sources | Validation status |
| --- | --- | --- | --- | --- |
| Animist | Creeping for PvE turret farms; the guide only suggests some Arboreal for a PvP turret. Turret caster and area damage. | No numeric endgame allocation is supplied. Exact template is an open gap. | [Hibernia guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) | No numeric endgame candidate; line/rank and budget validation unavailable. |
| Bard | Keep Nurture at level for group buffs, then add Regrowth and later Music for crowd control. Group support and healer. | 43 Nurture, 37 Music, 33 Regrowth: 2,207/2,223 (M15). | [Hibernia guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Blademaster | Keep a weapon line at level and put extra points into Celtic Dual. Dual-wield melee damage. | A dated forum option is 50 Celtic Dual, 42 Shields, 39 Piercing: 2,955/2,979 (M20). Role and ranked skill tiers need validation before an automatic plan. | [Hibernia guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html); [forum option](https://www.uthgard.net/forum/viewtopic.php?p=122426) | Endgame point total fits; role and exact ranked skill tiers need validation. |
| Champion | Max Valor while leveling, then Large Weapons; Shield is another option. Hybrid tank, debuffs, and interrupts. | 50 Valor, 42 Shields, 39 Large Weapons: 2,955/2,979 (M20). | [Hibernia guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Druid | Nurture supports grouping; Nature is an alternate solo-leveling focus, with Regrowth for healing. Healer and buffer. | 42 Nurture, 33 Regrowth, 7 Nature: 1,489/1,494 (M10); or 40 Nurture, 35 Regrowth, 9 Nature: 1,492/1,494. | [Hibernia guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Eldritch | Light for single-target leveling; Mana for group PBAoE. Ranged damage, crowd control, and debuffs. | Light: 46 Light/28 Mana, 1,485/1,494; Mana: 50 Mana/20 Light, 1,483/1,494 (M10). | [Hibernia guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Enchanter | Keep Mana high for pet-focus leveling. Pet support and caster damage. | 49 Mana, 22 Light: 1,476/1,494 (M10). | [Hibernia guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Hero | Keep Large Weapons or Celtic Spear at level; use remaining points for Shield or Parry. Defensive tank and melee damage. | A dated forum option is 50 Shields, 44 Celtic Spear, 12 Blades, 35 Parry: 2,969/2,979 (M20). This human group variant needs role and ranked skill-tier validation. | [Hibernia guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html); [forum option](https://www.uthgard.net/forum/viewtopic.php?f=62&t=31454) | Endgame point total fits; role and exact ranked skill tiers need validation. |
| Mentalist | Mana for group leveling, regeneration, and area damage-over-time. Healer support and ranged damage. | 46 Light, 28 Mentalism, 4 Mana: 1,494/1,494 (M10). | [Hibernia guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Nightshade | Keep weapon and Poison at level; spend leftovers in Celtic Dual. The guide suggests player Stealth autotrain. Assassin. | 44 Critical Strike, 36 Piercing, 35 Stealth, 35 Envenom, 25 Celtic Dual: 3,236/3,253 (M22). Blades is an alternate weapon line. | [Hibernia guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html); [leveling discussion](https://uthgard.net/forum/viewtopic.php?t=36198) | Static endgame pass; level-34 sample fits; source assumes autotrain/respec and stops at 34. |
| Ranger | Keep Pathfinding one to three ranks below level and the weapon near level; the guide says Bow can autotrain. Ranged/melee hybrid with self-buffs. | The guide's melee and hybrid templates cost 3,056 and 3,026 against a 2,979 no-autotrain budget (M20). Companion candidate: 42 Celtic Dual, 40 Pathfinding, 35 Piercing, 33 Stealth, 11 Recurve Bow: 2,975/2,979. | [Hibernia guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html); [Ranger guide](https://www.uthgard.net/forum/viewtopic.php?f=137&t=38854) | Static endgame pass; level-40 source allocation is 1,661/1,624 points; route is incomplete. |
| Valewalker | Keep Scythe at or one below level, then train Arboreal. Melee and spell hybrid. | 50 Scythe, 38 Arboreal Path, 20 Parry: 2,223/2,223 (M15). | [Hibernia guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |
| Warden | Weapon at or near level for solo play; Nurture for group support. Defensive melee and group support. | 49 Nurture, 33 Regrowth, 25 Blades, 14 Parry: 2,212/2,223 (M15). | [Hibernia guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) | Static endgame career/rank and point-budget pass; numeric level route missing. |

## Findings and explicit planning gaps

- The three realm guides cover 38 classes; the 2014 Necromancer discussion supplies a separate source. These community sources are emulator recommendations, not evidence of retail-era popularity.
- Four source templates need companion-specific no-autotrain adjustments: Minstrel, Reaver, Runemaster, and Ranger. The 34 endgame candidates are research inputs; no BotSpec profile or build has been selected as an automatic plan.
- The Theurgist source calls its line Air; the server career key is Wind Magic.
- Animist and Wizard still lack numeric endgame candidates. Blademaster, Hero, and Warrior forum candidates need an AI role decision and exact ranked-skill validation.
- Sorcerer, Ranger, Nightshade, and Armsman have partial numeric leveling sources. Healer has a longer sourced route, but its respecialization and half-level steps do not map to the current companion progression path. Minstrel's level-24 note relies on human autotrain. Other 33 classes have only relative focus guidance.
- All 34 currently checked endgame candidates pass static career/rank and no-autotrain level-50 budget checks. The partial route audits and Healer's unsupported transitions are recorded above; no complete, runtime-validated per-level plan is available.
- The checkout has no clean local runtime database, and none was used. Local career, skill, and plan checks remain blocked for all 39 classes.
- Every candidate awaits owner review. Automatic training remains unavailable until a plan is selected and the runtime checks pass.

This record extends Stage 1's proposal-or-gap research with Stage 3 validation status. It does not enable any automatic plan.

## Sources

- [Uthgard patch-level FAQ](https://www.uthgard.net/howto) — official statement that Uthgard targets patch 1.65; it establishes the emulator context, not the historic provenance of its guides.
- [Albion class guide](https://uthgard.blogspot.com/p/albion-class-guide.html), [Midgard class guide](https://uthgard.blogspot.com/p/midgard-class-guide.html), and [Hibernia class guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) — undated community recommendations; emulator-specific observations are not treated as retail facts.
- [Healer advice and leveling route](https://www.uthgard.net/forum/viewtopic.php?f=61&t=30306) — forum posts dated April 21, 2013; community advice with an explicit level-by-level Pacification/Augmentation path.
- [Necromancer specialization discussion](https://www.uthgard.net/forum/viewtopic.php?p=331300) — forum posts dated February–April 2014; community advice on Deathsight leveling and Death Servant farming.
- [Blademaster spec options](https://www.uthgard.net/forum/viewtopic.php?p=122426), [Hero group spec discussion](https://www.uthgard.net/forum/viewtopic.php?f=62&t=31454), and [Warrior spec discussion](https://www.uthgard.net/forum/viewtopic.php?t=11512) — dated Uthgard player recommendations from 2009–2013; candidate examples only, not verified companion plans.
- [OpenDAoC database dump](https://github.com/OpenDAoC/OpenDAoC-Database), especially ClassXSpecialization, SpecXAbility, SpellLine, LineXSpell, and Style — used as an upstream static reference, not a substitute for this fork's local runtime database.
- [Sorcerer leveling route](https://www.uthgard.net/forum/viewtopic.php?f=110&t=36619) — a 2011 route reposted in 2016; numeric milestones through level 32, followed by a respec at 40.
- [Ranger guide from ZAM](https://www.uthgard.net/forum/viewtopic.php?f=137&t=38854) — copied to the Uthgard forum in 2017; gives selected level-10 through level-40 templates and notes that intermediate levels need a character planner.
- [Nightshade leveling discussion](https://uthgard.net/forum/viewtopic.php?t=36198) — 2016 player advice with a level-34 sample, autotrain assumptions, and respec suggestions.
- [Armsman leveling discussion](https://www.uthgard.net/forum/viewtopic.php?f=99&p=460916&t=44202) — 2017 player advice with a Crush-rank formula through level 40 and a respec transition.
- Server implementation references: [BotSpec.cs](../source/server/GameServer/bots/specs/BotSpec.cs), [GameBot.cs](../source/server/GameServer/bots/GameBot.cs), [SkillBase.cs](../source/server/GameServer/gameutils/SkillBase.cs), and the class multiplier definitions under [playerclasses](../source/server/GameServer/playerclasses/).
