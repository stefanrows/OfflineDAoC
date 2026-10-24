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

Artifact spell types our items use that had **no handler in either tree**
(`ZoAura`, `ZahurAura`, `ZahurEnduProc`, `CrocodileTearsProc`, `UnyeldingProc`,
`YouthIllness`, `PowerHealthEnduranceRegenBuff`) got one in WP3.0b, below.
`EternalPlant` (Eternal Plant, row 30181 "Change Shapeshift", "Cancel target
transformation") still has none; the `ShatterIllusions` handler already does what
that row describes, so registering it for `EternalPlant` too (or retyping the row
in a new pack) is the follow-up.

## WP3.0b: the types no project implemented

Package WP3.0b of [TOA_PLAN.md](TOA_PLAN.md), 2026-10-24. Implemented in source,
real-client check pending. DOLSharp, OpenDAoC-Core and every other GitHub tree
name these types only in their `eSpellType` enum, so the behaviour comes from the
pack 012 rows and the live descriptions: the DAoC knowledge base (IGN Vault
boards 2003-2005, Camelot Herald patch notes) and the Camelot Herald item
database (`tools.camelotherald.com`). Where live is unclear the simplest reading
of the row text is built; the "Guessed" column says what that was.

| Type (spell, artifact) | Handler (file) | Behaviour | Source | Guessed |
|---|---|---|---|---|
| CrocodileTearsProc (30154, Crocodile Tear Ring level 6, 10 min) | `CrocodileTearsProc` (CrocodileTearRing.cs) | `Value` % (5) of every hit the wearer takes, melee, archery, spells and DoT ticks, is taken off the hit and restores the same amount of power and of endurance. Hooks `AttackedByEnemy`, which fires before the damage is dealt | IGN: "converts 5% of damage received into pow/end"; Herald 1.68: converted damage updates the bars, DoT damage converts too | both pools get the full amount; the row's `Frequency` 10 is ignored (live had no chance roll) |
| PowerHealthEnduranceRegenBuff (30184 Kalare's Necklace; 30182 Eternal Plant) | `PowerHealthEnduranceRegenBuff` (KalaresNecklace.cs) | every pulse (`Frequency`, 5 s) restores `-Value` % (5) of max health, power and endurance; a positive `Value` is points. Six pulses in 30 s = 30 % | IGN: "P/H/F regen ... used by Kalare's necklace as 30% and Eternal Plant as 50%" | Eternal Plant's row (-10) gives 60 %, not live's 50 %; data, not changed |
| UnyeldingProc (30162, Ring of Unyielding Will level 10, 10 min) | `UnyeldingProc` (RingOfUnyieldingWill.cs), derives `DefensiveProcSpellHandler` | defensive proc: a melee or archery hit on the wearer has `Frequency/100` % (20 %) to cast both the DoT in `Damage` (30161, 40 spirit) on the attacker and the HoT in `Value` (30160) on the wearer, from one roll | IGN 2004: "a 10 minute defensive proc buff that procs a 40 delve Spirit DoT and also casts a reverse HoT proc on you" | proc chance from the row |
| YouthIllness (30187, Gem of Lost Memories level 5) | `YouthIllness` (GemOfLostMemories.cs) | instant: ends PvE and RvR resurrection illness on the wearer, like a healer NPC; refused (charge not spent) when there is none | IGN template post: "Recovery - removes res sickness (active) (instant)" | the refusal |
| ZahurAura (30151, Crown of Zahur level 10 and Cloudsong) | `ZahurAura` (ZahurAndZoAuras.cs), derives `CloudsongAuraSpellHandler` | pulsing realm aura, radius 1500: +`Value` % (5) spell range and resist pierce | IGN: "5% spell range/5% spell pierce like cloudsong"; "aura on Zahurs is the same as on Cloudsong" | the 1.68 cap of 20 players is not built |
| ZahurEnduProc (30152, Crown of Zahur level 5, 10 min) | `ZahurEnduProc` (ZahurAndZoAuras.cs) | every heal spell through the stock heal handler that lands on the wearer also restores `Value` % (10) of the heal as endurance | row: "A portion of all heals cast on the target restore endurance instead"; DOL's commented-out reader in `HealSpellHandler` ("600 / 10 = 60end") | the heal is not reduced ("instead" read as DOL did); spread heals and heal-over-time ticks do not count |
| ZoAura (30165, Bracer of Zo'arkat level 10; Phoebus' Harp level 3) | `ZoAuraSpellHandler` (ZahurAndZoAuras.cs), derives the Bedazzlement handler | area Bedazzlement: enemies within 700 get +`Value` % (25) fumble and spell fumble chance for 30 s; does not stack with the single-target Bedazzlement (`ArtifactSpells.Family`) | IGN: "the bedazzle 25% miss rate" | Phoebus' Harp carrying the Bracer's spell is gaheris data, not checked |

Core touches (small, outside `spells/Artifacts`):

- `DefensiveProcSpellHandler.EventHandler(AttackData)` is now `virtual`, so the
  ring casts both spells from one roll; `ArtifactDefensiveProcEffect` (shim)
  puts the effect in the `eEffect.DefensiveProc` bucket the core reads.
- `HealSpellHandler.HealTarget` calls `ZahurEnduProc.OnHealReceived` after the
  heal; it scans only the target's `eEffect.Unknown` effects.
- `AfHitsBuff.cs` (stock, dead): registered as `AfHitsBuff`, which no row can
  reach because the case-insensitive enum parse resolves "AfHitsBuff" to the
  earlier `AFHitsBuff`, and built on the legacy hooks. Now it answers
  `AFHitsBuff` and runs through the ECS bridge: armor factor (`OtherBonus`) and
  max health (`AbilityBonus`) by `-Value` % of the target's armor and health, or
  by `Value` points. The amounts are kept on the target per spell with a count
  of the effects holding them, because a pulsing aura's refresh swaps the effect
  instance without a stop or start, and refreshing an ending effect starts the
  new instance before the old one stops: the first start adds, the last stop
  removes. Pack 013's Boon of Kings (30115) uses it. Accepted approximation: the
  percentage base is the worn armor pieces plus the modified armor factor, so
  -5 gives about 5.8 % of the effective armor factor for players (armor AF / 5
  per slot) and about 9 % for a GameBot (`GameLiving.GetArmorAF`).
- `HarpyFeatherCloak` overrides `CastSubSpells`. The core's `CastSubSpells`
  hands the parent spell to its single-cast helper, whose "no chained sub spell"
  guard then returns at once, so a plain `SubSpellID` never casts in this core
  (every working type overrides it). The override casts the sub spell on the
  spell's target, so pack 013's Disoriented (30125) brings its energy DoT 30126.
  Limit: 30126 has range 0 and radius 700, so the core treats it as point-blank
  around the Shield of Khaos wearer, not around the disoriented target as live.
- **Stock `DexterityConstitutionDebuff` (inhibiting poisons 25648-25651).** It
  mapped to `eEffect.Unknown`: `StatDebuffECSEffect` found no properties for
  that effect type and applied nothing, and its always-true conflict answer let
  a poisoned target block or wipe every artifact effect. It now has its own
  `eEffect.DexConDebuff` (appended at the end of the enum) with Dexterity and
  Constitution and the stat/status refresh of the other Con debuffs. Nothing
  else read the old conflict; two inhibiting poisons still compete. **Gameplay
  change:** inhibiting poisons now really lower Dexterity and Constitution
  (50-108 for 60 s), as their rows say.

Found on the way, not fixed: the stock `ToHitDebuff` has the same dead mapping
(`eEffect.Unknown`, nothing applied). Two artifact rows use it, 30178
"Disoriented" and 30200 "Wind disorientation". A stock fix needs the debuff
sign handled (the stock effect stores debuffs negative, `ToHitBonusCalculator`
subtracts the raw value), so it is left for a later package. Pack 013's 30125
uses the artifact to-hit debuff (`HarpyFeatherCloak`) instead.

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
- WP3.0b adds 10 tests (21 in the fixture): Disoriented casting its sub spell on
  the target (once, through the DoT handler, no chains), the conversion of a hit while the ring
  lasts and not after, Kalare's pulse amounts and cap, the ring as a defensive
  proc the core can fire, Recovery curing and refusing, the Zahur aura bonuses,
  Efficient Endurance per heal, ZoAura's bonuses and its conflict with
  Bedazzlement, the AFHitsBuff row type and its counted application across a
  pulse refresh, and the inhibiting poison next to artifact effects in both
  orders. Not tested: the ring's actual proc roll and spell casts (needs the
  spell table), a percentage Boon of Kings on a real player's armor, and any of
  it in a client.
