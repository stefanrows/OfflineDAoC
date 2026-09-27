# Albion Companion Build Expansion

## Scope and result

The Albion catalog now has three selectable plans for each of its 14 classes: 22 plans were present before this realm expansion and 20 were added. Existing plan IDs and class defaults remain in place. The new plans use specialization names and ranks present in this fork's static game data; the point targets fit the class's level-50 budget without autotrain.

Uthgard was the main external reference because its classic ruleset is closer to this fork's 1.65-era data than modern Eden changes. The Uthgard class guide provides class-by-class roles and common specialization patterns; player forum discussions supply alternatives where a class has more than one credible group or solo direction. The external templates are guidance, not copied blindly: target ranks were adjusted to this repository's no-autotrain allocation and checked against local static class/spec, style, and spell tables.

## Added plans

| Class | Plan key | Level-50 targets | Research basis and intent |
| --- | --- | --- | --- |
| Armsman | `onehanded` | Slash 50, Shields 42, Parry 39 | Adds a one-handed shield tank to the retained polearm and two-handed options. The class guide describes weapon and shield variants. |
| Cabalist | `spirit` | Spirit Magic 46, Body Magic 28 | A Spirit pet-support/debuff variant alongside the retained Body and Matter plans. Uthgard players discuss Spirit-focused PvE/RvR and tri-spec alternatives. |
| Cleric | `smite` | Smite 44, Rejuvenation 23, Enhancement 21 | Firsthand Uthgard Smite Cleric template, retained as an Attacker choice. It uses the entire 1,494-point M10 budget without autotrain. |
| Friar | `healer` | Rejuvenation 44, Enhancement 37, Staff 29, Parry 13 | Adds a more healing-oriented group support plan while retaining the melee-support and solo-staff plans. |
| Infiltrator | `slash` | Slash 50, Critical Strike 44, Stealth 36, Envenom 36, Dual Wield 14 | Slash weapon alternative to the stable Thrust assassin default. |
| Infiltrator | `highcrit` | Critical Strike 50, Thrust 35, Dual Wield 36, Stealth 33, Envenom 33 | Adapts a Uthgard high-Critical-Strike / dual-wield template to the local budget without autotrain. |
| Mercenary | `damage` | Dual Wield 50, Slash 50, Parry 28 | High weapon-style damage alternative to the retained shield-control build. |
| Mercenary | `split` | Dual Wield 50, Shields 42, Slash 27, Thrust 27, Parry 7 | Adds the community-discussed mixed weapon/shield allocation, with both weapon types retained. |
| Minstrel | `stealthgroup` | Instruments 50, Stealth 33, Thrust 27 | Adapts the guide's stealth-group direction to the available points. |
| Minstrel | `smallgroup` | Instruments 50, Thrust 33, Stealth 27 | Adapts the guide's small-group direction, trading some weapon and stealth ranks to fit the no-autotrain budget. |
| Necromancer | `deathsight` | Deathsight 49, Painworking 22 | First catalog choice; uses only the two specialization lines already represented in the companion profile. |
| Necromancer | `painworking` | Painworking 49, Deathsight 22 | Companion-profile line alternative to the retained Deathsight focus. |
| Necromancer | `hybrid` | Deathsight 38, Painworking 38 | Mixed allocation across those same two lines. |
| Paladin | `twohanded` | Two Handed 39, Shields 42, Chants 46, Slash 20 | A no-autotrain adaptation of the Uthgard hybrid pattern, retaining shield and chant support. |
| Paladin | `thrustguard` | Thrust 44, Shields 42, Chants 42, Parry 17 | Group guard / thrust option based on the Uthgard group template (42 Chants, 42 Shields, 44 Thrust, 22 Parry), adjusted to fit without autotrain. |
| Reaver | `parry` | Flexible 50, Shields 42, Soulrending 36, Parry 16 | Retains high Flexible and shield control while moving leftover points into defense. Uthgard players compare this Soulrending/Parry tradeoff. |
| Reaver | `soulrending` | Flexible 50, Soulrending 50, Shields 28, Parry 6 | Damage-leaning alternative adapted from the discussed 50 Flexible / 50 Soulrending pattern. |
| Scout | `hybrid` | Longbows 44, Shields 42, Stealth 35, Slash 29 | Middle ground between the retained bow and melee/shield plans; uses only Scout career lines. |
| Sorcerer | `matter` | Matter Magic 45, Body Magic 24, Mind Magic 17 | Adds Matter damage-over-time while retaining Body damage and a Mind utility investment. |
| Theurgist | `air` | Wind Magic 45, Earth Magic 28, Cold Magic 7 | Adds an Air/crowd-control variant to Earth/Wind support and Cold group damage. |

The Wizard already had Fire, Ice, and Earth choices, so its stable three-plan set did not need expansion.

## Point-budget and skill-table checks

For each added plan, the total cost to reach the listed level-50 ranks is below the class's full no-autotrain budget:

| Class multiplier | Added plan point-cost range | Level-50 budget |
| --- | ---: | ---: |
| 10x | 1,466–1,494 | 1,494 |
| 15x | 2,211–2,215 | 2,223 |
| 20x | 2,945–2,976 | 2,979 |
| 25x | 3,688–3,697 | 3,706 |

The cost for a target rank is the sum of ranks bought from 1 through that rank, matching the catalog's `CostToReach` calculation. A read-only check against the installed static tables confirmed every target line is in that class's specialization career and that each line with ranked spell or style data has entries at or below its selected target rank. Parry and Stealth are passive lines; Scout Longbows is treated as a career-only line by the catalog check. No save, account, or character rows were queried.

The Cleric Smite ranks were also checked against the installed static tables:
Smite 44 has ranked `Smiting` spells, Rejuvenation 23 includes `Minor
Resuscitation`, and Enhancement 21 includes `Regeneration`. Their point costs
are 989 + 275 + 230 = 1,494, exactly the no-autotrain M10 budget.

## Skill-use and weapon follow-up

Cataloging and learning these ranks does not itself align the companion's persisted weapon plan or prove that its combat AI uses every newly learned style/spell. These additions need the subsequent skill-use/weapon-profile pass to account for:

- Armsman `onehanded` wants a one-handed Slash weapon and shield; the current profile may choose another weapon type.
- Infiltrator `slash` needs Slash weapons. `highcrit` stays Thrust-compatible with the current default profile.
- Mercenary `damage` wants Slash weapons; `split` needs the profile to preserve its intended Slash/Thrust choice and shield access.
- Both Minstrel alternatives specify Thrust, while the current profile chooses Slash or Thrust. Stealth ranks describe a real PvP archetype, but companion AI currently does not provide stealth behavior.
- Paladin `twohanded` uses Two Handed/Slash, while `thrustguard` uses Thrust and shield. The current profile can choose incompatible weapon types.
- Reaver's Flexible styles require the matching weapon type; the current profile can choose Slash, Thrust, Crush, or Flexible.
- Scout `hybrid` uses Slash with Longbows and Shields, while the current profile can choose Slash or Thrust.

The three Necromancer plans allocate only Deathsight and Painworking. They are
not Death Servant farming builds and add no Death Servant ranks or Eden-only
abilities. Harmful servant `PetSpell` wrappers whose resolved payload is an
`ENEMY`-radius area spell use the saved ranged-AoE threshold and the same
bystander, mezz, and hostile-keep safety checks, centered on the servant.
Source paths for servant summon and commands exist, but the new builds' summon,
command, and pet-combat behavior has not been verified in a real client.

## References

- [Uthgard Albion class guide](https://uthgard.blogspot.com/p/albion-class-guide.html) — class roles and weapon/spec archetypes across Albion.
- [Uthgard Cleric specialization discussion](https://www.uthgard.net/forum/viewtopic.php?f=60&t=29332) — player reports on Enhancement/Rejuvenation balances and Smite utility.
- [Uthgard Smite Cleric build discussion](https://www.uthgard.net/forum/viewtopic.php?f=60&t=30809) — firsthand 44 Smite / 23 Rejuvenation / 21 Enhancement template.
- [Uthgard Cabalist specialization discussion](https://www.uthgard.net/forum/viewtopic.php?t=41486) — Spirit, Matter, and Body PvE/RvR alternatives.
- [Uthgard Infiltrator specialization discussion](https://www.uthgard.net/forum/viewtopic.php?f=60&t=30282) — Critical Strike, dual-wield, and assassin line tradeoffs.
- [Uthgard Mercenary specialization discussion](https://www.uthgard.net/forum/viewtopic.php?f=104&t=36280) and [mixed weapon/shield discussion](https://www.uthgard.net/forum/viewtopic.php?f=60&t=30288).
- [Uthgard Paladin specialization discussion](https://www.uthgard.net/forum/viewtopic.php?f=60&start=15&t=30610) — player comparisons of guard/thrust and two-handed hybrid templates.
- [Uthgard Reaver questions](https://www.uthgard.net/forum/viewtopic.php?f=60&t=29000) and [Reaver template discussion](https://www.uthgard.net/forum/viewtopic.php?t=23586) — Flexible, shield, Soulrending, and Parry allocation tradeoffs.
- [Uthgard classic rules/how-to](https://www.uthgard.net/howto) — realm ruleset context used when comparing external templates to this fork's local static skill data.
