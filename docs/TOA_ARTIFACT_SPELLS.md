# Trials of Atlantis: artifact spell handlers (WP3.0)

Package WP3.0 of [TOA_PLAN.md](TOA_PLAN.md), 2026-10-24. Implemented in source,
real-client check pending. Source: `OpenDAoC/OpenDAoC-Core` at `f0aaa1e`
(2026-09-26), `GameServer/spells/Artifacts` (30 files). Our copy:
`source/server/GameServer/spells/Artifacts/`, same file names, plus one shim file
`ArtifactSpellSupport.cs`. Tests: `Tests/UnitTests/UT_ArtifactSpellHandlers.cs`.

## Summary

- **43 spell types, 43 handlers, none existed before.** Every type was already in
  our `eSpellType`; no handler for any of them lived anywhere in our tree under
  another name. Upstream's commented-out `Ereine.cs` (types `Ereine`, `Ereine2`,
  `EreineProc`, absent from our enum) is kept inert.
- **Upstream's handlers mostly did nothing on an ECS server.** 27 of them hang
  their bonuses on the legacy `OnEffectStart(GameSpellEffect)` /
  `OnEffectExpires` hooks, which our ECS never calls, and every artifact type maps
  to `eEffect.Unknown`, so even the ECS-based bases (stat buff/debuff, proc,
  ablative) applied no bonus, fired no proc and absorbed nothing. The shim bridges
  them into the ECS; see below.
- **Our DB uses none of these types today.** 0 `Spell` rows. 73 of the 85 spell
  IDs referenced by our 197 artifact item templates are missing from `Spell`;
  68 of them are in gaheris `sql/90-spells.sql` (not in the WP0.3 migration list),
  5 exist nowhere (7206, 13135, 30115, 30125, 32223). gaheris 90 carries 45 rows
  of 31 of the 43 types.

## Handler table

"DB" = rows in our snapshot `Spell` table. "gaheris" = rows in gaheris
`90-spells.sql`. "Items" = our artifact item templates that reference one of
those gaheris spell IDs (directly or as SubSpell parent).

| Spell type | Handler (file) | DB | gaheris | Items | Notes |
|---|---|---|---|---|---|
| AllStatsDebuff | `AllStatsDebuff` (AllStatsDebuff.cs) | 0 | 1 | 7 Foppish Sleeves | upstream defined it twice; merged into one |
| AllStatsPercentDebuff | `AllStatsPercentDebuff` | 0 | 1 | 0 | amounts kept per effect (upstream: per handler) |
| AlvarusMorph | `AlvarusMorph` (AlvarusLeggings.cs) | 0 | 1 | 0 | needs underwater; ends on leaving the water |
| Arrogance | `Arrogance` (Atlantis ns) | 0 | 2 | 3 Eirene's Hauberk | +stats, then -stats 10 s after real expiry |
| AtensShield | `AtensShield` | 0 | 1 | 0 | summons trident; cleanup on quit |
| AtlantisTabletMorph | `AtlantisTabletMorph` | 0 | 0 | 0 | morph + offensive proc |
| StarsProc | `StarsProc` (BandofStars.cs) | 0 | 2 | 0 | bolt + subspell |
| StarsProc2 | `StarsProc2` (BandofStars.cs) | 0 | 1 | 1 Band of Stars | all-stats debuff + target morph |
| BeltOfMoon | `BeltOfMoon` | 0 | 1 | 0 | night only; check moved to CheckBeginCast |
| BeltOfSun | `BeltOfSun` | 0 | 0 | 0 | day only; check moved to CheckBeginCast |
| CeremonialBracerMezz | `CeremonialBracerMezzSpellHandler` | 0 | 1 | 7 Ceremonial Bracers | marker effect; core intercept dead (see gaps) |
| CeremonialBracerStun | `CeremonialBracerStunSpellHandler` | 0 | 1 | 5 Ceremonial Bracers | marker effect; intercept commented out upstream |
| CloudsongAura | `CloudsongAuraSpellHandler` | 0 | 0 | 0 | spell range + resist pierce |
| CloudsongFall | `CloudsongFallSpellHandler` | 0 | 2 | 5 Wings Dive | marker effect; fall-damage read dead (see gaps) |
| LoreDebuff | `LoreDebuff` (CommonArtifactSpells.cs) | 0 | 0 | 0 | spell damage + magic resists |
| StrengthConstitutionDrain | `StrengthConstitutionDrain` | 0 | 2 | 2 Oglidarsh's Belt, Scepter | target debuff, caster buff |
| ABSDamageShield | `ABSDamageShield` | 0 | 3 | 0 | ablative that reflects; now in the AblativeArmor bucket |
| Morph | `Morph` | 0 | 2 | 0 | model from LifeDrainReturn |
| ArcaneLeadership | `ArcaneLeadership` | 0 | 1 | 0 | pulsing realm aura, same bonuses as Cloudsong |
| EfficientHealing | `EfficientHealing` (CrownOfZahur.cs) | 0 | 0 | 0 | marker; no core reader, upstream too |
| EfficientEndurance | `EfficientEndurance` | 0 | 0 | 0 | marker; no core reader, upstream too |
| Powershield | `Powershield` | 0 | 0 | 0 | power to health below 50 % health |
| DreamMorph | `DreamMorph` (DreamSphere.cs) | 0 | 1 | 0 | morph + offensive proc |
| DreamGroupMorph | `DreamGroupMorph` | 0 | 1 | 1 Dream Sphere | breaks when attacked |
| ShatterIllusions | `ShatterIllusions` (Eternal Plant.cs) | 0 | 0 | 0 | ends artifact morphs, damages |
| HealFlask | `HealFlask` (Flask.cs) | 0 | 0 | 0 | marker; no core reader, upstream too |
| DeadFlask | `DeadFlask` | 0 | 0 | 0 | marker; no core reader, upstream too |
| GoldenSpearJavelin | `GoldenSpearJavelin` | 0 | 1 | 0 | summons javelin |
| HarpyFeatherCloak | `HarpyFeatherCloak` | 0 | 0 | 0 | to-hit debuff; spell-hit part has no core reader |
| HpPwrEndRegen | `HpPwrEndRegenSpellHandler` | 0 | 3 | 0 | 5 % per pulse; Spell.Value ignored as upstream |
| MaddeningScalars | `MaddeningScalars` | 0 | 1 | 0 | morph + offensive proc |
| RealmLore | `RealmLore` | 0 | 2 | 0 | info window on a player target |
| ScarabProc | `ScarabProc` (ScarabVest.cs) | 0 | 1 | 6 Golden Scarab Vest | morph + snare that does not break on damage |
| ShadesOfMist | `ShadeOfMistDefensiveProcSpellHandler` | 0 | 1 | 0 | shade form + 200 pt melee buffer proc |
| SnakeCharmer | `SnakeCharmer` | 0 | 1 | 4 Snakecharmer's weapons | lifedrain 50/30/20 % hp/power/endurance |
| StyleDmgAbs | `StyleDmgAbsSpellHandler` | 0 | 3 | 0 | absorbs part of style damage |
| Tartaros | `Tartaros` (TartarosGift.cs) | 0 | 1 | 0 | lifedrain 35/21/14 % |
| TraitorsDaggerProc | `TraitorsDaggerProc` | 0 | 2 | 6 Traitor's Dagger, Shades of Mist | shade form + offensive proc |
| DdtProcDd | `DdtProcDd` | 0 | 0 | 0 | damage, costs caster health |
| TraitorsDaggerSummon | `TraitorsDaggerSummon` | 0 | 2 | 5 Traitor's Dagger | pet with lifetap on hit |
| Traldor | `TraldorSpellHandler` | 0 | 1 | 2 Traldor's Oracle | spell damage + resist pierce |
| ZoSummon | `BracerOfZo` (bracerofzo.cs) | 0 | 1 | 1 Bracer of Zo'arkat | three flying pets for the duration |
| Bedazzlement | `ZoDebuffSpellHandler` | 0 | 1 | 1 Band of Stars | fumble + spell fumble debuff |

## Shims (all inside spells/Artifacts, no core change)

| Shim | Why |
|---|---|
| `IArtifactEffectHandler` + `ArtifactSpellEffect` | runs a handler's start/stop/pulse code from the ECS instead of the dead legacy hooks; sends Message1-4 like stock buffs |
| `ArtifactSpellEffect.ApplyProperties` | applies Property1-3 of property-changing handlers (Traldor, Cloudsong, Harpy, Zo, Str/Con drain); stock `StatBuffECSEffect` reads properties from the effect type, which is Unknown for artifacts |
| `ArtifactSpells.DebuffSign` / `AddDebuff` | every artifact debuff write goes through one sign rule. Stats, resists, spell damage and acuity are stored **negative** like stock ECS debuffs, because their calculators read `Math.Abs` of the sum; a positive artifact value would cancel a stock debuff (shaman -60 Str plus Foppish Sleeves 12 read 48, now 72). To-hit, fumble, spell fumble, spell range and physical/magic absorption stay **positive**, because their calculators subtract the raw value |
| `ArtifactSpells.ConflictsWith` | conflict rule for bridged effects: same spell type (or same effect group) only. They all share the `eEffect.Unknown` bucket and the effect list asks the existing effect's handler; `SingleStatDebuff` and `SpeedDecreaseSpellHandler` answer "always", so Harpy, Zo, Str/Con drain and Scarab would have blocked or wiped every other artifact effect on the target |
| `ArtifactProcEffect` | sets `EffectType = OffensiveProc`; `GameLiving.OnAttackEnemy` only fires procs found there |
| `ArtifactAblativeEffect` | sets `EffectType = AblativeArmor` for ABSDamageShield; the melee absorb code only reads that bucket |
| `ArtifactMorphProcSpellHandler` | shared base for the four morph-proc forms: refuses a second artifact morph, sets/restores the model (not for Necromancers) |
| `ArtifactSpells` helpers | ECS replacements for `EffectList.GetAllOfType<GameSpellEffect>()` / `FindEffectOnTarget` (upstream's morph check compared an enum to a string and was always false), morph list, summoned-item cleanup |
| ScarabProc snare | applied as a MaxSpeed multiplier on its own effect: as a `MovementSpeedDebuff` the core would break it on damage, and it is meant to hold |
| Dream Sphere group form | the break-on-attack handler is registered on any wearer, so companions (GameBot) lose the form as well |
| TraitorDaggerPet `OnAttackEnemy` | `GameLivingEvent.AttackFinished` is no longer raised for pets; the pet reports its hits to the summon handler |
| Belts of Sun/Moon | day/night check in `CheckBeginCast` (upstream checked in the constructor, which also runs on delve and let the cast through empty); one "power has left you" message instead of one per backpack item |
| `eChatType.CT_Items`, `GetFrequencyAndDurationSuffix()` | absent here; replaced by `CT_System` and a plain description |

Small upstream bugs fixed on the way: Snakecharmer/Tartaros endurance message
tested the health amount, Powershield drove power negative, Realm Lore printed
the list's type name into chat, Style absorb could subtract more than the hit.

## Gaps that need core changes (not done)

| Effect | Where the core reads it | Needed |
|---|---|---|
| Ceremonial Bracer mez intercept | `CCSpellHandler.cs:180` uses legacy `FindEffectOnTarget(target, "CeremonialBracerMezz")` | use `ArtifactSpells.FindEffect(target, eSpellType.CeremonialBracerMezz)` and `End()` it; stun intercept is commented out at `:297` |
| Cloudsong fall damage | `GamePlayer.cs:2516` uses legacy `FindEffectOnTarget(this, "CloudsongFall")` | same one-line swap |
| Efficient Healing / Endurance, Heal Flask, Dead Flask | nothing reads them, upstream neither | heal/endurance cost and death-save hooks |
| Harpy Feather Cloak spell-hit bonus | comment points at `SpellHandler.CalculateToHitChance`; nothing there | add a to-hit modifier for the target |

Artifact spell types our items use that have **no handler in either tree**:
`ZoAura` (Bracer of Zo'arkat, Phoebus Harp), `ZahurAura` (Cloudsong, Crown of
Zahur), `ZahurEnduProc` (Crown of Zahur), `CrocodileTearsProc` (Crocodile Tear
Ring), `UnyeldingProc` (Ring of Unyielding Will), `YouthIllness` (Lost
Memories), `EternalPlant` (Eternal Plant, row 30181 is in our DB).

## Data notes for WP3.1 / spell import

- gaheris 30205 "Great Shade Form." is `TraitorsDaggerProc` with `Value` 200; as a
  proc handler that would cast spell ID 200. It belongs on Shades of Mist and
  probably wants type `ShadesOfMist` (25034 is the proper row).
- gaheris 30155 "Lore Debuff" is typed `RealmLore`, so it opens the Realm Lore
  window instead of debuffing.
- gaheris 30136 "Curse of the Scarab" has `Value` 0: morph only, no snare.
- `HpPwrEndRegen` rows carry `Value` -5 / -35; the handler ignores it (5 % per
  pulse) as upstream does.

## Not checked

- No server start and no real client: models, messages, proc rates and the
  pets of Traitor's Dagger and Zo'arkat are unverified in game.
- Effect save/restore across logout for the bridged effects (the ECS saves the
  spell ID; restore runs the same start code) was read, not tested.
- **GM accounts can log back in morphed.** `GamePlayer.Model` persists
  `CurrentModel` to the character row for accounts with PrivLevel > 1, so an
  artifact form that is still active when a GM's character is saved may come back
  on the next login. Players are not affected. Workaround: let the form expire
  before logging out; a guard in `RestoreModel` or on quit is optional, not built.
- Unit tests (11) cover the registry, the absence of legacy hooks, the bonus
  bridge for AllStatsDebuff, Traldor, Harpy and Zo, stacking an artifact debuff on
  a stock `StatDebuffECSEffect` through the effect list, artifact debuffs not
  blocking each other through the effect list, and that proc and ablative effects
  come only from handlers the core can hard-cast. Other handlers compile but have
  no behaviour test.
