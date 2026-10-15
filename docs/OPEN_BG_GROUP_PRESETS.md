# Open BG: group presets (cross-realm archetypes)

Research 2026-10-15 for the open BG window and crew builder (TASKS 116 and the
window spec `docs/superpowers/specs/2026-10-15-open-bg-window-design.md`).
Evidence: 30 forum threads (2003-2005) in the DAoC knowledge base, the bot code
at worktree commit 13cf0c2 and one read-only query on the save. Nothing here is
built.

**The open BG is cross-realm.** Groups and guilds are the parties, as on this
server; there is no realm alliance. So the presets below are *archetypes by
role*. Each role slot lists the classes of all three realms that fill it; one
group may mix realms. The per-realm setups from the sources are the raw
material for those slot options, not presets of their own.

Legend: H healer, CC mezzer, Sp speed, T tank/peeler (Guard), M melee damage,
C caster damage, St stealth, P pet. Confidence: ✅ named by three or more posts
in two or more threads, ☑ one or two posts, ❓ assumption only.

## 1. Archetypes

| Archetype | Size | Roles (min / wish) | Class options per role | Job in the BG | Conf. |
| --- | --- | --- | --- | --- | --- |
| **Standard 8** | 8 | H 2/2 · CC 1/1 · Sp 1/1 · T 1/2 · M 3/3 | H: Cleric, Healer, Druid (second: Friar, Shaman, Warden). CC: Sorcerer, Shaman/Healer (pacify), Bard, Eldritch. Sp: Minstrel, Skald, Bard. T: Paladin, Armsman, Warrior, Hero. M: Mercenary, Savage, Berserker, Blademaster, Champion | roamer, seeks 8v8 | ✅ |
| **Caster / bomb 8** | 8 | H 2/2 · CC 1/2 · Sp 1/1 · T 0/1 · C 3/4 | C: Wizard, Cabalist, Theurgist, Sorcerer (Albion); Spiritmaster, Runemaster, Bonedancer at most 1 (Midgard); Eldritch, Enchanter, Mentalist (Hibernia) | keep raid support, keep defense, against zergs; weak in the open field | ✅ |
| **Raider 8** | 8 | as Standard 8, T 1/2, M 3/4, one siege carrier | the Standard 8 options plus Animist (Hibernia) for keep fights | takes part in a keep raid, escorts rams | ☑ (derived from the Standard 8 and the keep-fight notes) |
| **Stealth gank** | 3-5 | St 2/4 · Sp 0/1 · CC 0/1 · bow 0/2 | Infiltrator, Scout, Minstrel (Albion); Shadowblade, Hunter (Midgard); Nightshade, Ranger (Hibernia) | ganks on bridges and roads, camps milegates | ☑ (anecdotal; no setup thread for Midgard and Hibernia) |
| **Small group** | 3-4 | H 1/1 · CC 1/1 · M 1/2 | H and CC as in the Standard 8, one speed class where possible | roamer against solos and duos; threes without CC are "gimpy" | ✅ |
| **Duo** | 2 | none fixed | healer + speed or melee; stealther pairs | stragglers and gank | ☑ (mostly PvE threads) |

Replacements when a class is missing: second healer ↔ third melee; Shaman has
no substitute in a Midgard group (stamina regeneration, "Shaman dead = group
dead"); Warrior ↔ Thane is disputed; Minstrel missing: Paladin endurance plus
sprint.

**Buffbots** were common but stood outside the group, parked somewhere. They
are no preset slot. Spec buffs at the porter are an idea only (❓).

## 2. What the code knows today

`AutonomousRvrDoctrine.TraitsOf` (`AutonomousRvrDoctrine.cs:99-132`) has the
traits Healer, AreaMez, AreaStun, PointBlankBomb, Caster, Pets, Melee, Guard,
Stealth, Archer and Speed. Every slot above can be written as a set of
`eCharacterClass` without new class logic. Missing traits (debuff, purge,
area root, endurance regeneration) are not needed while slots are defined by
class.

Findings that stand in the way, all verified in code:

1. **The healer pick contradicts itself.** `TryPickGroup`
   (`OpenBattlegroundRoster.cs:1140-1142`) takes two "healers" by
   `BotPartyRoles.IsHealingClass`, which counts Paladin but neither Bard nor
   Mentalist; `TraitsOf` does the opposite. An Albion 8-man can form with two
   Paladins and no Cleric. The rest of the group is simply "the nearest next"
   (line 1142), so speed and CC are never picked on purpose.
2. **`BombGroup` is reachable for Midgard only.** The `areaStun` condition needs
   the Healer class from level 32 (`AutonomousRvrDoctrine.cs:171,185`). Albion
   Wizards and Hibernia Eldritch/Enchanter groups are classed as PetCasters,
   AssistTrain or PickupGroup.
3. **The Minstrel has no `Stealth`** (line 116), although it had stealth in
   1.65 (confirmed by a Mythic reply, D9174).
4. **Size limits.** A small group is 2-4 members (`SmallGroupMin/Max`,
   `OpenBattlegroundRoster.cs:182-183`, random size, line 568): five-man
   presets do not fit; a duo is a small group of size 2 but not steerable.
   `GankSquad` requires a Hunter-type leader (`AutonomousRvrDoctrine.cs:179`).
5. **The class pool is enough.** Level 40-50 bots in the save: Albion 610,
   Midgard 598, Hibernia 599, every class 35-69 times. Not measured: how many of
   them stand within porter range at the same time.

## 3. Proposed build order

| # | File | What | Risk |
| --- | --- | --- | --- |
| 1 | new `battlegrounds/open/OpenBattlegroundPresets.cs` | catalog of archetypes: slots (class set, min, wish, priority) and BG role, with unit tests | low |
| 2 | `OpenBattlegroundRoster.cs:1113-1149` | `TryPickGroup` fills slots in priority order (healer, speed, CC, peeler, damage) with the nearest fitting bot, across realms; a missing minimum slot falls back to today's pick plus a log `OPEN_BG_PRESET_MISS`; the refill path (1078-1104) uses the same slots | medium: fewer groups if porter range makes the choice scarce |
| 3 | same file, line 1237 | the roster's own healer test (Cleric/Healer/Druid primary; Friar/Shaman/Warden secondary; not Paladin) | low |
| 4 | `AutonomousRvrDoctrine.cs:116,171,185` | Minstrel gets Stealth; `BombGroup` at two or more bombers plus AreaMez | medium to high: `Derive` is also used by the world RvR and Gothwaite PvP |
| 5 | roster property | stealth small group up to five, duo as its own size draw | low |

Cross-realm change: today's crew targets are per realm
(`open_bg_units_<realm>`). With archetypes as the unit, a target is "count per
archetype", and the realm only decides where a bot logs in and which porter it
uses. The window shows groups with their archetype, not realm blocks.

## 4. What could not be verified

- How common each setup really was in 2003: forum posts show wishes and
  boasting, not statistics.
- Stealth groups for Midgard and Hibernia: only observations from fight
  reports.
- RvR duos: nearly only PvE threads.
- Thidranki itself: live it was a level 20-24 battleground; there is no source
  for level 40-50 there.
- Mez types: D20737 says Bard and Eldritch (Light) mez in Hibernia and does not
  name the Mentalist, while `TraitsOf` gives AreaMez to the Mentalist and not to
  the Eldritch. Not checked.
- Whether the pool is enough while forming (log not evaluated).
- `GROUP_PLAY.md` sections 3 and 4.1 are mostly supported by the sources.
  Differences: Midgard runs three healers instead of two, and only one Shaman.

## 5. Sources

Knowledge base threads (web.archive.org copies of vnboards.ign.com).

| ID | Thread (date) |
| --- | --- |
| D54444, D54365 | Best Hib Gank Group Setup? (8/2003), pages 1 and 2 |
| D13922 | 8 Man Mid Gank Group (8/2003) |
| D29048 | Best Albion RvR Group? (10/2003) |
| D9026 | Midgard Gank Groups < The Problems > (1/2004) |
| D15600 | What is best group for RVR? (8vs8), Hib (1/2004) |
| D12530 | Optimal Albion RvR group (2/2004) |
| D20737 | Gank groups...where does albion fit in? (6/2004) |
| D9174 | So pete, anything being done about the Alb Stealth Groups? (1/2004) |
| D23225, D26032 | Minstrel and Infiltrator group threads (2004) |
| D18597, D19792, D20165 | Midgard and Hibernia stealth observations (2004) |
| D13255, D13236, D17615, D15014 | Hibernia 3-4 man and duo threads (8/2003-4/2004) |
| D32382, D22259, D9459 | Midgard trio and duo threads (2004) |
| D20644, D14253, D21023, D31234, D58575 | Albion duo, trio and 4-man threads (2004-2005) |
| D47349, D49670, D45404 | confirmation only (10-12/2004) |

D72694 ("Group setup", 2006 Gaheris co-op server) is a PvE thread and not used.
URLs: search the knowledge base by thread ID with
`/mnt/e/daoc-knowledge/tools/search.py`; the exact archive links are stored with
each document.
