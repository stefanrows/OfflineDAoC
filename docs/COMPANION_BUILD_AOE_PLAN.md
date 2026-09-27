# Companion build skill use and ranged AoE plan

Updated 2026-09-27. Source implementation for researched companion builds and
ranged caster area damage is complete in 0.96.0; installation and real-client
acceptance remain pending. The catalog contains 118 static career/skill-checked
plans across all 39 Classic + SI classes, with at least three choices per
class. The original 33 `general-pve-v1-*` defaults remain stable. Static data
checks and successful Release compilation of CoreServer and the test
assembly do not prove that a companion uses a learned skill correctly in live
combat. The automated tests were not run.

## Build-to-combat binding

Each saved `TrainingPlanId` is the source of truth for the companion's selected
build. Automatic training applies its level-appropriate specialization ranks;
combat choices must then use skills the character actually learned and that are
available at its current level. Do not infer access from class name, plan text,
or another shard's skill list. Use local career and skill data, and omit any
ability that is absent from this runtime.

The source binds selected plans to these behavior families:

- Melee and hybrid builds select a persisted weapon/loadout compatible with
  their trained weapon line, then use only styles and weapon modes they know.
- Casters choose their damage, debuff, heal, and crowd-control spells from the
  learned spell list. The chosen plan can guide priority, but level, target,
  range, mana, reuse, interruption, and role safety checks still apply.
- Pet builds summon and command only the pets supported by their learned lines;
  pet type, upkeep, and commands must follow the selected build.
- Group roles reflect the chosen plan. Healer, buffer, attacker, tank, and
  control behavior must remain class-legal and preserve support behavior for
  support plans. BotBrain now lets only a persistent Bard following the
  automatic battle build with the matching Attacker role leave the grouped
  endurance-song support turn during combat; it stops twisting the pulse so
  the Bard can switch to melee. Support Bard builds keep their prior song and
  support path. Smite Cleric Attacker companions bypass the random offensive
  mana throttle, while support Clerics retain it. Bard battle uses its learned
  Blunt path when compatible Blunt gear is equipped; Nature Druid's offensive
  spell path is represented by its selected Attacker role. These behaviors
  still need real-client verification.

Preserve existing plan IDs and defaults. The first catalog entry remains the
default; the six formerly unsupported classes have stable selectable defaults.
Do not rewrite a saved manual allocation or unknown plan ID. Necromancer's
three current plans are Deathsight/Painworking choices, not Death Servant
farming builds. Source paths for Death Servant summon and commands exist, but
their behavior needs real-client verification before that archetype is offered.

Build-aligned loadouts preserve owner equipment. A fresh recruit gets starter
gear aligned with its selected plan. Switching an existing companion build only
activates an already-equipped compatible item; it does not replace owner-
selected items or override a manual equipment lock. If the companion has no
compatible weapon equipped, the owner must adjust its gear before expecting
the planned melee weapon type. Keep this behavior explicit in the gear UI and
in the real-client acceptance.

## Ranged area damage policy

Ranged AoE is a separate policy from the existing close-range bomb preference.
It applies to learned, harmful, ranged area spells with a positive radius; it
excludes PBAoE, cones, control spells, and abilities the companion cannot
currently cast. A saved per-companion threshold controls the switch from
single-target damage to ranged AoE:

| Preference | Behavior |
| --- | --- |
| `Off` | Never select ranged AoE through this policy. |
| `2+` through `8+` | Use ranged AoE when at least that many eligible hostile NPCs are in the selected spell's area. |
| New-record default `3+` | Wait for three eligible hostile NPCs. |

The companion manager exposes the setting and cycles the threshold; changing
it persists on that companion. Existing bomb settings and bomb thresholds stay
independent. Heals, control, protected mezzes, valid bomb use, and ordinary
cast legality retain their appropriate priority before an AoE attack.

The eligible target set includes the committed focus and other mobs actively
engaged by the owner or group. It also includes hostile keep guards belonging
to the same keep as a selected hostile guard, even when those guards are idle.
The policy must reject bystander mobs, guards from another keep, player-like
targets, illegal PvP targets, and NPCs protected by mezz. A cast that would
damage any rejected bystander is refused; the threshold is met only by the
remaining eligible NPCs. The spell's own range and radius are authoritative.

Necromancer servant area commands use a narrow exception to the normal
positive-range spell filter. The learned harmful `PetSpell` wrapper has range
zero, but its resolved `ENEMY` damage payload has an area radius centered on
the servant. The same saved threshold and target-safety checks apply around
that actual servant-centered blast, including same-hostile-keep guards and
refusing idle mobs, unrelated guards, players, and protected mezzes. This
special wrapper path does not turn ordinary PBAoE or cone spells into ranged
AoE choices, and it does not make the three Deathsight/Painworking plans
Death Servant farming builds.

## Source checks and real-client acceptance

Source-level coverage establishes that every class has three or more plans,
every default remains selectable, original default IDs stay unchanged, and
plans pass class-career, ranked-skill, point multiplier, and role-legality
checks. AoE policy coverage includes persisted choices, normalization and cycle
behavior, threshold boundaries, PBAoE/cone/control exclusion,
range/mana/reuse/interruption limits, committed mobs, same-hostile-keep guards,
and refusal when a bystander, player, illegal target, or protected mezz would
be hit. The automated test suite was not run.

After installing the source, real-client acceptance should verify:

1. Existing records keep their selected plan, allocation, and default behavior;
   new recruits can select all listed plans, and the six new class defaults work.
2. A fresh melee recruit gets starter gear aligned with its selected weapon
   line. For an existing companion, verify a compatible already-equipped item
   becomes active without changing owner-supplied gear or manual locks; if no
   compatible item is equipped, the owner can adjust gear and the companion
   then uses the learned style. Bard battle reaches Blunt combat after a Blunt
   weapon is equipped, while Nurture/Music support Bards continue their
   endurance song, heals, and control.
3. Representative pet builds use their selected summon and commands, and
   representative ranged casters use learned single-target spells below the
   threshold and ranged AoE at or above it.
4. The AoE preference survives relog/restart. Exercise each threshold with
   ordinary mobs and with guards from the same hostile keep; verify unrelated
   guards, idle bystanders, players, and protected mezzes are never hit. For a
   Necromancer servant area command, verify the count and safety checks around
   the servant's payload center rather than the companion's target center.
5. `Off` disables ranged AoE while leaving bomb behavior controlled by its
   separate preference. Confirm logs and observed casts identify no unavailable
   or Eden-only skill.

No server start, deployment, automated test run, or real-client gameplay check
was performed for this task. Source integration is complete in 0.96.0; track
installation and the acceptance steps above as pending until the owner verifies
them in a real client.
