# Midgard companion build expansion

This scoped catalog update adds 18 Midgard plans. It keeps all published plan
IDs and defaults in place, and raises each Midgard class to at least three
choices. Healer and Spiritmaster already had three or more plans. The point
totals below use the catalog's level-50, no-autotrain budgets: 1,494 points
(M10), 2,223 (M15), 2,979 (M20), and 3,253 (M22).

## Added plans

| Class | Plan ID | Endgame specializations | Points | Build rationale |
|---|---|---|---:|---|
| Berserker | `split` | Axe 39, Hammer 39, Left Axe 50, Parry 16 | 2,967/2,979 | Dual-type Left Axe variant; Uthgard players discuss this exact 39/39/50 pattern. |
| Berserker | `defensive` | Axe 39, Hammer 29, Left Axe 50, Parry 30 | 2,951/2,979 | Keeps the dual-type weapon option while diverting the remainder to Parry. |
| Bonedancer | `darkness` | Darkness 50, Suppression 20, Bone Army 4 | 1,492/1,494 | Direct-damage Darkness build with a small pet-line investment. |
| Bonedancer | `bonearmy` | Bone Army 48, Suppression 24, Darkness 6 | 1,494/1,494 | Pet-focused KeepDancer variant; the Suppression investment reaches the companion-pet spell line. |
| Hunter | `hybrid` | Beastcraft 42, Composite Bow 35, Stealth 36, Spear 39 | 2,975/2,979 | Bow, pet, stealth, and spear hybrid close to a firsthand Uthgard player template. |
| Runemaster | `runecarving` | Runecarving 48, Darkness 24, Suppression 6 | 1,494/1,494 | Bolt/rune damage with a secondary Darkness line. Matches a supported local BotSpec allocation and reaches the local rank-48 Rune of Destruction spell. |
| Savage | `hammer` | Hammer 39, Savagery 49, Parry 20 | 2,212/2,223 | Two-handed Hammer variant that retains the commonly recommended high Savagery investment. |
| Savage | `handtohand` | Hand to Hand 50, Savagery 42, Parry 9 | 2,220/2,223 | Hand-to-hand variant emphasizing the line's top style and multi-hit identity. |
| Shadowblade | `critblade` | Critical Strike 44, Axe 39, Stealth 38, Envenom 38 | 3,248/3,253 | Two-handed Critblade template with poison and stealth. |
| Shadowblade | `fivespec` | Critical Strike 34, Left Axe 39, Stealth 36, Axe 35, Envenom 33 | 3,227/3,253 | Five-spec assassin with both weapon lines, poison, stealth, and Critical Strike. |
| Shaman | `mending` | Mending 43, Augmentation 32, Subterranean 6 | 1,492/1,494 | Main-healer option. This is an existing, concrete local BotSpec profile rather than a newly verified forum template. |
| Skald | `songs` | Battlesongs 50, Hammer 39, Parry 18 | 2,223/2,223 | Song-first group build with a viable Hammer line. |
| Skald | `hammer` | Battlesongs 43, Hammer 50, Parry 2 | 2,221/2,223 | Weapon-first alternative; Uthgard discussions include the 43 song/50 weapon split. |
| Thane | `melee` | Hammer 50, Shields 42, Stormcalling 38, Parry 10 | 2,970/2,979 | Hammer/shield guard with strong melee and retained Stormcalling. |
| Thane | `twohanded` | Hammer 50, Stormcalling 50, Parry 28 | 2,953/2,979 | Two-handed melee and spell hybrid; no Shield line, so its group role is mapped to Attacker. |
| Warrior | `sword` | Sword 50, Shields 42, Hammer 39 | 2,955/2,979 | Dated Uthgard recommendation retained as the new shield-tank default. |
| Warrior | `hammer` | Hammer 50, Shields 50, Parry 28 | 2,953/2,979 | Shield/weapon maximum variant with Parry. |
| Warrior | `split` | Hammer 50, Shields 42, Sword 39 | 2,955/2,979 | Dated split-weapon shield-tank variant. |

The Warrior `sword` plan replaces the former “manual-only” blocker while
preserving the documented sword-and-shield concept. It is now the original
default ID, so existing persisted plan IDs remain stable.

## Source review

These are dated Uthgard community recommendations, used as evidence that the
archetypes are playable rather than as claims about current Eden balance. No
Eden-only skill or line was added.

- **Berserker:** [Axe versus Hammer discussion](https://www.uthgard.net/forum/viewtopic.php?f=61&t=37238) and [dwarf Berserker discussion](https://www.uthgard.net/forum/viewtopic.php?f=61&t=26823) describe 39 Axe, 39 Hammer, 50 Left Axe, with remaining points in Parry.
- **Bonedancer:** [Sheena's Bonedancer guide](https://www.uthgard.net/forum/viewtopic.php?t=38851) gives Darkness and KeepDancer variants; [spec discussion](https://uthgard.net/forum/viewtopic.php?t=24298) also recommends a Suppression-heavy general build. The guide's pet-count behavior is Uthgard-specific and is not assumed here.
- **Hunter:** [Hunter spec discussion](https://www.uthgard.net/forum/viewtopic.php?f=61&t=28737) gives a firsthand hybrid near 42 Beastcraft, 35 Bow, 36 Stealth, 39 Spear.
- **Runemaster:** [Runemaster RA spec discussion](https://www.uthgard.net/forum/viewtopic.php?f=61&t=13447) and [spec discussion](https://www.uthgard.net/forum/viewtopic.php?f=61&t=32297) cover Runecarving and Darkness/Suppression variants. A separate [Uthgard leveling discussion](https://www.uthgard.net/forum/viewtopic.php?f=118&t=39390) calls out a bolt issue on that shard; that warning is shard-specific. The catalog uses the existing local 48 Runecarving / 24 Darkness / 6 Suppression profile rather than copying the Uthgard 50 Suppression / 20 Runecarving proposal.
- **Savage:** [Savagery discussion](https://www.uthgard.net/forum/viewtopic.php?p=111609) recommends retaining high Savagery; another [Savage discussion](https://www.uthgard.net/forum/viewtopic.php?p=331007) describes Hand to Hand's multi-hit and positional identity.
- **Shadowblade:** [Final spec discussion](https://www.uthgard.net/forum/viewtopic.php?f=120&t=40985) and [Critblade discussion](https://www.uthgard.net/forum/viewtopic.php?f=120&t=44664) compare Critblade and Left Axe/five-spec directions. The local project profile independently contains the 44 Critical Strike, 39 weapon, 38 Stealth, 38 Envenom Critblade allocation.
- **Shaman:** the `mending` option is based on the existing [`ShamanBotSpec`](../source/server/GameServer/bots/specs/Midgard/Shaman.cs), which has the exact 43 Mending / 32 Augmentation / 6 Subterranean distribution. The linked [Midgard class guide](https://uthgard.blogspot.com/p/midgard-class-guide.html) supports the broader distinction between group Augmentation and solo Cave directions, but did not establish this exact Mending template.
- **Skald:** [weapon discussion](https://www.uthgard.net/forum/viewtopic.php?f=61&t=22105), [spec choices](https://www.uthgard.net/forum/viewtopic.php?f=61&t=28945), and [Skald spec discussion](https://www.uthgard.net/forum/viewtopic.php?f=122&t=36569) include song-first and 43 Battlesongs / 50 weapon variants.
- **Thane:** [Thane spec discussion](https://www.uthgard.net/forum/viewtopic.php?f=124&t=38066), [weapon/Stormcalling variants](https://www.uthgard.net/forum/viewtopic.php?p=115470), and a [PvE recommendation](https://www.uthgard.net/forum/viewtopic.php?f=61&t=17188) discuss shield, melee, and high Stormcalling templates.
- **Warrior:** [Hammer or Sword](https://www.uthgard.net/forum/viewtopic.php?t=11512), [various Warrior specs](https://www.uthgard.net/forum/viewtopic.php?f=125&t=36249), and [single-weapon-line discussion](https://www.uthgard.net/forum/viewtopic.php?f=125&t=39587) give shield-tank and cross-weapon examples. These posts are old; the catalog uses no-autotrain point budgets and does not assume auto-training.

## Static skill validation and implementation gaps

Validation used the installed game database through SQLite `mode=ro&immutable=1`
and queried only the static `ClassXSpecialization`, `Specialization`,
`SpellLine`, `LineXSpell`, `Spell`, and `Style` tables. The immutable read
avoids the mounted-volume I/O failure seen with ordinary read-only mode; it
does not consult WAL contents. Every specialization named in these ten
classes' candidate plans is present in that class's static career data.
Representative target-rank rows include:

- Bonedancer Bone Army 48: **Summon Bonerazer**; Suppression 24:
  **Summon Bonefixer**.
- Hunter Beastcraft 42: **Arachite's Chitin**. Spear 39 has the **Razor Edge**
  style. Composite Bow 35's Rapid Fire path depends on the old archery branch;
  companion use is not established by this table check.
- Runemaster Runecarving 48: **Rune of Destruction**; Darkness 24:
  **Rune of Pain**.
- Savage Savagery 49: **Savage Blows**; Hand to Hand 50:
  **Totemic Sacrifice**.
- Shadowblade Envenom 38: **Major Infectious Serum**; Critical Strike 44 has
  **Rib Separation** in style data.
- Shaman Mending 43: **Frigg's Balm**; Augmentation 32:
  **Greater Earth Invigoration**; Subterranean 6: **Bonding Creepers**.
- Skald Battlesongs 50: **Heavenly Song of Rest**; Hammer 50 has weapon styles.
- Thane Stormcalling 38: **Thor's Greater Lightning**; Shields 42 has
  **Slam**.
- Warrior Sword 50 has **Ragnarok**; Shields 42 has **Slam**.

This confirms static line membership and representative learned skill/style
rows; it does not run a companion or prove each skill is selected in combat.
Notable follow-up work for the separate skill-use/runtime pass:

| Plans | Required follow-up |
|---|---|
| Berserker `split`, `defensive`; Hunter `hybrid`; Skald `songs`, `hammer`; Thane `melee`, `twohanded`; Warrior `sword`, `hammer`, `split` | Existing BotSpec constructors can choose weapon types or profiles randomly. Persist the selected plan's weapon family and `Is2H`/shield setup so trained line, styles, and equipped weapon stay aligned. The Berserker dual-type plans need the chosen main hand to be explicit. |
| Savage `hammer`, `handtohand` | Pass a stable preferred weapon into `SavageBotSpec`; otherwise it may choose a different weapon family. The Hand to Hand choice also needs the paired dual-wield loadout. |
| Shadowblade `critblade`, `fivespec` | The Critblade needs its two-handed profile; the five-spec plan needs its Left Axe/offhand profile. `BotMeleeStylePolicy` filters stealth-required and positional-opening styles, so Critical Strike's learned high-rank style does not establish use of a stealth opener. |
| Bonedancer `darkness`, `bonearmy` | Map the selected plan to the corresponding primary specialization/pet behavior. `BonedancerBotSpec` currently selects one primary and a random secondary; its behavior is not equivalent to each precise plan ratio, especially the Bone Army pet branch. |
| Hunter `hybrid` | `HunterBotSpec` chooses a random melee weapon and spell ratios; make weapon and archery availability line up with the selected hybrid. Verify the `ALLOW_OLD_ARCHERY` setting before promising legacy bow actions. |
| Runemaster `runecarving` | The BotSpec must retain the selected Runecarving profile and its bolt/rune/ground-AoE cast priorities; static learned skills alone do not provide those priorities. |
| Shaman `mending` | `ShamanBotSpec` already has the exact allocation and `MendShaman` profile; verify healing selection/priority for the newly selectable plan. |

Group roles were adjusted only where the option needs a distinct role:
Shaman `mending` maps to Healer, Skald `songs` to Buffer, and Thane
`twohanded` to Attacker. Thane `melee` and all Warrior plans retain their
shield/guard direction. AoE target thresholds and caster ranged-AoE policy are
outside this catalog task.

No automated tests, server start, deployment, or gameplay checks were run.
