# Hibernia companion build expansion

Status: Hibernia now has three selectable level-50 build plans for each of its
13 classes (39 total). This adds 23 plans to the 16 existing Hibernia plans.
Existing build IDs and defaults are preserved. Blademaster and Hero now have
three choices each; their first catalog entries establish their initial
defaults. Every added plan received a static career, ranked-skill, and point-
budget check. Updated 2026-09-27.

## Research and validation

The main template source is the community [Uthgard Hibernia class guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html),
which discusses leveling priorities and endgame line combinations for every
Hibernian class. Uthgard describes a 1.65 ruleset; its guide is undated, so its
shard-specific balance and bug claims are not treated as facts about this
server. Class-specific comparisons also use firsthand [Blademaster](https://www.uthgard.net/forum/viewtopic.php?p=122426),
[Hero](https://www.uthgard.net/forum/viewtopic.php?f=134&t=43002),
[Champion](https://www.uthgard.net/forum/viewtopic.php?p=377143),
[Nightshade](https://www.uthgard.net/forum/viewtopic.php?p=384405),
and [Valewalker](https://www.uthgard.net/forum/viewtopic.php?t=40418)
discussions. For examples beyond Uthgard, an [Eden Hibernia group discussion](https://www.reddit.com/r/daoc/comments/1ifz7ga/trio_on_eden/)
mentions a 46 Void / 28 Mana Eldritch, Mana and Mentalism Mentalists, and a
hybrid Warden. Those examples support the broad build choices only; Eden
mechanics and skills were not copied into this catalog.

All 23 additions were checked against level-50 specialization budgets without
autotrain and the installed runtime's static class-career and ranked
ability/style/spell data. Every allocated line belongs to the class career;
each active line has an available ranked skill at or below its target. Career
Parry, Stealth, and Ranger Recurve Bow use the runtime's career-skill path
without a ranked spell/style prerequisite. Point totals are listed as spent /
available below. This was
a read-only check of static skill tables in the installed SQLite database; no
save or character data was read or copied. These checks validate learned-skill
availability, not live combat use.

## Added level-50 choices

| Class | Added plan | Specialization targets | Points | Evidence and rationale |
| --- | --- | --- | ---: | --- |
| Animist | `creepfarm` — Creeping farm | Creeping Path 50, Arboreal Path 20 | 1,483 / 1,494 | The [guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) favors Creeping for PvE turret farming; the lower Arboreal investment supports the secondary pet line. |
| Bard | `battle` — Battle support | Nurture 43, Music 37, Regrowth 16, Blunt 29 | 2,216 / 2,223 | A battlebard is a less common Uthgard guide path, but the guide lists Blunt as a Bard career line. It keeps the core group songs and adds a credible melee option. |
| Blademaster | `group` — Celtic Dual and shield | Celtic Dual 50, Shields 42, Blades 39 | 2,955 / 2,979 | A [Uthgard spec comparison](https://www.uthgard.net/forum/viewtopic.php?p=122426) contrasts this shield-control split with a high-weapon damage spec. |
| Blademaster | `damage` — Dual-wield damage | Celtic Dual 50, Blades 50, Parry 28 | 2,953 / 2,979 | [Uthgard spec discussion](https://www.uthgard.net/forum/viewtopic.php?p=122426) describes a 50 Celtic Dual / 50 weapon route with remaining points in Parry. |
| Blademaster | `guard` — Shield guard | Shields 50, Celtic Dual 42, Blades 39 | 2,955 / 2,979 | Defensive companion option derived from the [Uthgard shield and damage split discussion](https://www.uthgard.net/forum/viewtopic.php?p=122426). |
| Champion | `large` — Large Weapons group damage | Large Weapons 50, Shields 42, Valor 39 | 2,955 / 2,979 | The [Champion spec discussion](https://www.uthgard.net/forum/viewtopic.php?p=377143) gives this as a group damage split beside the high-Valor shield route. |
| Champion | `damage` — Valor and Large Weapons | Valor 50, Large Weapons 50, Parry 28 | 2,953 / 2,979 | Keeps both core hybrid lines high and trades the shield investment for Parry. The [guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) describes Valor debuffs and Large Weapons as central Champion lines. |
| Druid | `nature` — Nature solo | Nature 50, Nurture 20 | 1,483 / 1,494 | The [guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) identifies Nature as the Druid solo line; Nurture retains a modest group-buff contribution. |
| Eldritch | `void` — Void bolts and debuffs | Void 46, Mana 28 | 1,485 / 1,494 | The Uthgard guide describes Void bolts and resistance debuffs. An [Eden group example](https://www.reddit.com/r/daoc/comments/1ifz7ga/trio_on_eden/) independently uses 46 Void / 28 Mana; its shard-specific balance is not assumed here. |
| Enchanter | `light` — Light ranged damage | Light 45, Mana 27, Enchantments 12 | 1,488 / 1,494 | The [guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) supports Light damage with Mana utility; a small Enchantments investment retains pet support. |
| Enchanter | `enchantment` — Enchantment pet focus | Enchantments 50, Mana 20, Light 4 | 1,492 / 1,494 | The [guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) treats Enchantments as the pet line; this offers a pet-focused option alongside the existing Mana default. |
| Hero | `shield` — Celtic Spear and shield | Celtic Spear 50, Shields 50, Blades 25, Parry 14 | 2,976 / 2,979 | This shield-and-spear template is discussed in [Uthgard Hero specs](https://www.uthgard.net/forum/viewtopic.php?p=130079). |
| Hero | `largeweapons` — Large Weapons hybrid | Large Weapons 50, Shields 42, Celtic Spear 38, Blades 10 | 2,970 / 2,979 | Uses the documented [Large Weapons / shield / Celtic Spear split](https://www.uthgard.net/forum/viewtopic.php?f=134&t=43002). |
| Hero | `celticspear` — Celtic Spear hybrid | Celtic Spear 50, Shields 42, Large Weapons 38, Blades 10 | 2,970 / 2,979 | Weapon-line alternative to the above hybrid, following the [Uthgard Hero discussion](https://www.uthgard.net/forum/viewtopic.php?f=134&t=43002). |
| Mentalist | `mana` — Mana support | Mana 50, Light 20 | 1,483 / 1,494 | The [guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) associates Mana with area damage-over-time and group support; the [Eden discussion](https://www.reddit.com/r/daoc/comments/1ifz7ga/trio_on_eden/) provides another shard's Mana-oriented example. |
| Mentalist | `mentalism` — Mentalism healing and control | Mentalism 50, Light 20, Mana 4 | 1,492 / 1,494 | The [guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) describes Mentalism's healing and control spells; this is a control/healing alternative to the existing Light/Mentalism plan. |
| Nightshade | `dualfocus` — Celtic Dual focus | Celtic Dual 44, Piercing 36, Stealth 35, Envenom 35, Critical Strike 24 | 3,211 / 3,253 | A [Uthgard Nightshade discussion](https://www.uthgard.net/forum/viewtopic.php?p=384405) compares weapon, poison, stealth, and dual-wield emphasis. This companion allocation avoids relying on player-only autotrain points. |
| Nightshade | `blades` — Blades assassin | Critical Strike 44, Blades 36, Stealth 35, Envenom 35, Celtic Dual 25 | 3,236 / 3,253 | Uses the [guide's](https://uthgard.blogspot.com/p/hibernia-class-guide.html) documented alternate weapon line while retaining its standard assassin core. |
| Ranger | `bowfocus` — Recurve Bow focus | Recurve Bow 45, Pathfinding 40, Piercing 24, Celtic Dual 19, Stealth 35 | 2,970 / 2,979 | The [guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) presents bow, Pathfinding, stealth, and melee as a hybrid; this raises the bow line while keeping self-buffs and melee backup. |
| Valewalker | `pve` — Scythe PvE focus | Scythe 50, Arboreal Path 43, Parry 2 | 2,221 / 2,223 | The [Uthgard VW guide](https://www.uthgard.net/forum/viewtopic.php?t=40418) describes common 50 Scythe / 38 Arboreal variants and allows the split to vary. Higher Arboreal is the companion's PvE spell-support option. |
| Valewalker | `arboreal` — Arboreal spell focus | Arboreal Path 50, Scythe 39, Parry 18 | 2,223 / 2,223 | Trades weapon rank for Arboreal spells while retaining a viable Scythe line, based on the [guide's flexible line emphasis](https://www.uthgard.net/forum/viewtopic.php?t=40418). |
| Warden | `battle` — Battle Warden | Nurture 49, Blades 39, Regrowth 16, Parry 10 | 2,192 / 2,223 | The [Hibernia guide](https://uthgard.blogspot.com/p/hibernia-class-guide.html) describes the Warden's support, weapon, and defensive lines; an [Eden group example](https://www.reddit.com/r/daoc/comments/1ifz7ga/trio_on_eden/) also includes a hybrid Warden. |
| Warden | `regrowth` — Regrowth healer | Nurture 45, Regrowth 47, Parry 9 | 2,205 / 2,223 | Follows the [guide's](https://uthgard.blogspot.com/p/hibernia-class-guide.html) pairing of group buffs and healing while providing an alternative to the existing support default. |

## Runtime behavior still needed

The catalog validates allocations but does not yet bind each saved build to a
matching combat profile. The following work is needed for the choices above to
change actual combat behavior:

- `BotSpec.ChoosePersistentSpecialization` and the Hibernia `BotSpec` profiles
  choose class abilities and weapon modes independently of the saved
  `TrainingPlanId`. Bard's battle profile, Champion's shield/two-hand modes,
  Hero's weapon mode, Nightshade's weapon and style profile, and the class
  profiles with random rank variance need to consume the selected build.
- Ranger's bow plans allocate Recurve Bow, but its profile does not currently
  explicitly select a Recurve Bow as the equipped weapon; the bow plan needs a
  corresponding persisted weapon plan and bow attack behavior.
- Animist Creeping/Arboreal and Enchanter Enchantment builds require pet
  behavior, spell choice, and pet priorities to follow their learned lines.
- Eldritch Void, Mana-oriented Mentalist, and other ranged casters need learned
  ranged area spells considered by the enemy-count AoE threshold. That should
  respect target guards and keep single-target spells for smaller groups.
- Build role metadata only sets the primary party role and a Bard CC duty.
  Bard battle is Attacker plus CC; Mentalist Mentalism is CrowdControl; Warden
  builds map to Buffer, Attacker, or Healer. Hibernian shield-capable melee
  plans do not currently have a tank/guard role mapping. BotBrain now releases
  a Bard following the automatic battle plan with its matching Attacker role
  from the grouped endurance-song support turn, allowing melee combat; the
  selected Blunt profile aligns fresh starter gear, while an existing Bard
  uses a compatible equipped item and requires owner adjustment if its gear is
  locked or incompatible. Druid Nature now maps
  to Attacker, but its pet, Nature spells, and damage priorities still depend
  on the selected build reaching the combat profile; the role alone does not
  make those learned skills active.

These are static code findings. No Hibernia gameplay verification was run as
part of this expansion.

The cross-realm plan for wiring learned build skills and ranged caster AoE into
combat, with the real-client acceptance checks, is in
[COMPANION_BUILD_AOE_PLAN.md](COMPANION_BUILD_AOE_PLAN.md).
