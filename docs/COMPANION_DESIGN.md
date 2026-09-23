# Companion Stage 1: code and interaction audit

Status: Stage 1 audit complete, updated 2026-09-23. These findings describe the
current checkout. They are design input, not a shipped-feature promise or an
approved schema decision.

## Supported class catalog

The current Classic + Shrouded Isles catalog is defined by
`AutonomousBotIdentityGenerator.GetEraClasses` and used by
`TemporaryGroupClassCatalog`. `BotSpec.GetSpec` has a corresponding spec
factory for every class in this list.

| Realm | Base game classes | Shrouded Isles additions | Runtime total |
| --- | --- | --- | ---: |
| Albion | Armsman, Cabalist, Cleric, Friar, Infiltrator, Mercenary, Minstrel, Paladin, Scout, Sorcerer, Theurgist, Wizard (12) | Necromancer, Reaver (2) | 14 |
| Midgard | Berserker, Healer, Hunter, Runemaster, Shadowblade, Shaman, Skald, Spiritmaster, Thane, Warrior (10) | Bonedancer, Savage (2) | 12 |
| Hibernia | Bard, Blademaster, Champion, Druid, Eldritch, Enchanter, Hero, Mentalist, Nightshade, Ranger, Warden (11) | Animist, Valewalker (2) | 13 |
| **Total** | **33** | **6** | **39** |

This reconciles the runtime catalog to the original game's 33 classes plus the
six Shrouded Isles classes. It confirms the scope roster, not that every class
has a researched companion build. The wider eCharacterClass enum also
contains later classes that the current era catalog omits.

`BotSpec.GetSpec` is evidence that the current bot system can instantiate a
class spec object; it is not evidence that each class has a verified 1.65
build. GetSpecializationChoices only exposes alternate choices for some
classes, and the spec factory's built-in plans still need point-budget and
skill checks.

### Historical source triage

The official [patch notes archive](https://www.darkageofcamelot.com/patch-notes/)
dates version 1.56, "Introducing Shrouded Isles," to 2002-12-03; version 1.65
to 2003-10-08; and version 1.66, "Trials of Atlantis," to 2003-10-28. The
agreed 1.65 target therefore includes Shrouded Isles and ends before Trials of
Atlantis.

The [2001 Prima Official Strategy Guide](https://books.google.com/books?id=v_yTj2ATHGUC)
is a dated, pre-Shrouded Isles source with realm and class-path chapters. A
contemporary [Shrouded Isles preview dated 2002-11-26](https://www.gamespot.com/articles/dark-age-of-camelot-shrouded-isles-updated-preview/1100-2898630/)
reports two new classes per realm and names all six: Albion's Necromancer and
Reaver, Midgard's Bonedancer and Savage, and Hibernia's Animist and Valewalker.
The 2001 guide is an era source for the original class set; the preview is
contemporaneous reporting, not a Mythic patch note. Together with the official
patch chronology and the code's realm catalogs, these sources support the
33-plus-6 reconciliation above. They do not establish build popularity or
validate specialization allocations. No dated, first-party 1.65 document
listing all 39 class names was located in this pass.

Uthgard's [project description](https://www2.uthgard.net/howto) says its
relaunch targets an authentic 1.65 experience. Its three community class guides
are useful candidate sources for a 1.65-like server, but the pages have no
visible publication dates and include Uthgard-specific implementation and
balance observations. They are recommendations for review, not retail-era
evidence or proof of popularity.

### Per-class build research and validation

The [companion build research](COMPANION_BUILD_RESEARCH.md) records a sourced
leveling direction or an explicit planning gap for all 39 classes. The three
realm guides cover 38; a dated 2014 Uthgard forum discussion supplements the
missing Necromancer. Animist and Wizard still lack numeric endgame templates.
Dated forums offer numeric Blademaster, Hero, and Warrior variants, but their
role assumptions and ranked skill tiers remain unvalidated. The guides mostly
give line priorities rather than per-level training schedules; the Healer forum
discussion is the only source in this pass with a detailed leveling route.

A static cross-check found every candidate specialization line in all 39 class
careers and checked 108 ranked lines across 34 numeric proposals against the
public OpenDAoC reference database's career, ability, spell, and style tables.
All 34 candidate allocations fit the server's level-50 companion point budgets
after no-autotrain adjustments for Minstrel, Reaver, Runemaster, and Ranger.
Three additional forum variants have point checks but are not included in that
ranked-skill pass; see the research file for the validation gate. Parry and
Stealth are passive lines and do not provide spell, style, or
specialization-ability tiers.

This check uses the upstream public database snapshot, not the fork's local
runtime database, which is not pinned in this checkout. Confirm the actual
runtime career and skill rows against a disposable clean database before
enabling any plan. The source-era distinction and remaining exact-template
gaps are recorded in the linked research file.

## Persistence and ownership findings

- `bots/database/BotProfile` and `BotDatabase` are an older owner-character
  profile path. A profile stores owner character ID, name, class, race, gender,
  level, and active state. `BotSettings` stores a few behavior settings. This
  path does not itself store earned XP, trained specs, build-plan history, or a
  companion-owned inventory.
- `OfflineWorldBotRecord` in `bots/autonomous/AutonomousBotEconomy.cs` is the
  durable state for autonomous world bots. It stores level, XP, specs, build
  plan, unspent points, world state, and other autonomous fields. Loading it
  sets `IsAutonomousWorldBot`; that population is ownerless and managed by
  autonomous controllers. An owned companion must not be represented as one of
  these actors or become eligible for autonomous login, goals, rewards, or
  world roaming.
- `BotInventory` can load and save `DbInventoryItem` rows under a string owner
  ID. Autonomous bots use a persistent ID derived from their bot record;
  temporary `/spawn` helpers use a nonpersistent inventory. This is a reuse
  candidate for companion items, but a companion-specific owner-ID namespace,
  transactional item movement, and save-failure recovery still need design and
  verification.
- `/spawn` helpers receive a new runtime GUID, owner `GamePlayer` reference,
  class/race/gender, and current session progression. Their quit handler deletes
  them. Persistence therefore needs a stable companion identity and an explicit
  owner key. The roadmap has not decided whether ownership is per character or
  account; no migration from `BotProfile` or temporary helpers is assumed.

### Candidate save boundary

Keep a companion record separate from both the legacy `bot_profiles` rows and
`offline_world_bots`. The record will need a stable ID, the selected owner key,
authored/generated identity, class and race, level and XP, trained specs and
unspent points, selected build plan, lifecycle/bench state, and a schema/state
version. Persist companion inventory against a distinct companion owner ID;
never alias the player's character inventory owner ID. Load only through the
owning player's roster flow. Bench and logout should save and detach the
companion without deleting its identity; autonomous controllers should never
discover it. This is a proposed interface, not an approved schema: ownership,
schema compatibility, and failure-recovery details remain open.

The additive-table approach appears safer than converting legacy bot rows, but
existing-save compatibility still needs disposable-database verification. Do
not reinterpret an old profile or auto-convert a temporary helper without an
explicit mapping and migration decision.

### Stage 2 ownership and roster interface recommendation

Recommend character-scoped ownership as the starting design: it matches the
existing `BotProfile` owner-character relationship and keeps companion identity
and equipment local to the character that recruited them. The owner has not
chosen between character and account scope; record that decision before Stage 2
implementation. Keep each companion's stable ID independent of name and class.

Use a new additive companion record and a companion-specific `BotInventory`
owner-ID namespace. A conceptual roster service should list, recruit, invite, and
bench only records belonging to the current owner. Menu labels should resolve
through an owner-scoped server-side mapping to stable IDs, never from a name or
client-supplied database key. Bench and logout persist and detach; they do not
delete. Additive schema setup must leave legacy `bot_profiles`, existing player
items, and autonomous-world-bot records unchanged. Do not migrate old profiles
or temporary `/spawn` helpers automatically.

This interface and compatibility policy are design recommendations, not a
committed schema or product decision. Recruitment rules, account-versus-character
scope, roster limits, old `/spawn` meaning, and temporary-helper policy remain
Stage 2 gates.

## Reward, progression, and equipment findings

The current `/spawn` contract is documented in [COMPANION_BOTS.md](COMPANION_BOTS.md).
Direct helper damage can earn session XP while the human owner retains the
existing group credit and party divisor. Companion-controlled pet damage keeps
the human-owner attribution. Helpers do not receive realm points or loot and
are not persisted. These paths are separated in `AbstractServerRules` by
`IsTemporaryGroupHelper`; autonomous reward and persistence paths use
`IsAutonomousWorldBot`.

Temporary helpers choose a current `BotSpec` plan at creation, load class
specializations, and spend available points automatically. Autonomous bots
restore serialized specs, an encoded lifetime build plan, and unspent points
from their own record. These are useful mechanics to audit, but they do not
provide companion-controlled manual training or a researched class build plan.
The source proposals were checked against the 39 career line sets, static
reference skill tables, and companion point budgets. This is not a check against
the fork's local runtime database; do that against a disposable clean database
before enabling a build plan.

Temporary helpers get generated equipment in a nonpersistent inventory.
Autonomous bots use persistent `BotInventory` rows and separate autonomous
equipment/reward code. No companion gear transfer or drop policy is inferred
from either system; the Stage 4 decisions remain open, including capacity,
overflow, replacement recovery, and protection of manual choices.

## Client interaction findings

The current `/spawn` class picker provides a no-native-patch precedent:
`TemporaryGroupSpawnMenu` creates a short-lived NPC for the requesting player,
sends bracketed clickable choices in the popup window, receives a selection
through `WhisperReceive`, and reopens the menu. The menu is private to its owner.
This could support roster and companion-selection pages, with visible choice
labels mapped to stable companion IDs on the server. It is still an ephemeral
NPC conversation and does not prove that a complete roster, inventory, or
training interface is practical.

`SendCustomDialog` and the 1.68 `DialogResponseHandler` provide a callback with
a response byte for a simple confirmation choice. They are suitable for
confirmations, not a multi-entry roster editor. The existing clickable popup
and chat commands are the best code-backed candidates for the first roster
interface. Real-client behavior, pagination, cancellation, and repeated menu
interactions remain unverified; no native client patch is assumed.

## Stage 1 acceptance record

| Work | Status |
| --- | --- |
| Audit current persistence, ownership, reward, inventory, and menu paths | Complete; boundaries distinguish temporary helpers, owned companions, and autonomous bots |
| Confirm the 39-class catalog against dated 1.65-era class sources | Complete: 33 base classes plus six dated Shrouded Isles additions; no single first-party 1.65 roster document found |
| Research leveling and level-50 build proposals for each class | Complete to proposal-or-gap standard; two classes lack numeric templates, three forum variants await ranked-skill validation, and most schedules block affected automatic plans |
| Check allocations against careers, abilities, skills, and point budgets | Complete for 34 proposals: all 39 career sets and 108 ranked lines; three additional forum variants have point checks but need rank validation |
| Record source era, historical evidence, recommendations, and server deviations | Complete; Uthgard recommendations are separated from dated forum advice and retail-era evidence |
| Document owner key, compatibility policy, and roster/save interface | Complete as a recommendation; owner scope and roster rules remain Stage 2 gates |
| Verify client menu behavior in a real client | Pending a separately authorized client session |

The static research, design boundary, and code-based menu assessment meet Stage
1 acceptance. Local runtime database comparison remains a gate before enabling
automatic plans; real-client menu behavior remains unverified. Neither is
reported as a successful runtime or client check.

Stage 2 implementation remains gated by the open decisions in
[COMPANION_ROADMAP.md](COMPANION_ROADMAP.md). No companion schema or gameplay
behavior is implemented by these notes.

## Source files reviewed

- `source/server/GameServer/bots/autonomous/AutonomousBotIdentityGenerator.cs`
- `source/server/GameServer/commands/playercommands/TemporaryGroupCommands.cs`
- `source/server/GameServer/bots/database/BotProfile.cs`
- `source/server/GameServer/bots/database/BotDatabase.cs`
- `source/server/GameServer/bots/database/BotSettings.cs`
- `source/server/GameServer/bots/autonomous/AutonomousBotEconomy.cs`
- `source/server/GameServer/bots/autonomous/AutonomousBotStatusPersistence.cs`
- `source/server/GameServer/bots/BotInventory.cs`
- `source/server/GameServer/bots/GameBot.cs`
- `source/server/GameServer/bots/specs/BotSpec.cs`
- `source/server/GameServer/serverrules/AbstractServerRules.cs`
- `source/server/GameServer/packets/Server/PacketLib168.cs`
- `source/server/GameServer/packets/Client/168/DialogResponseHandler.cs`
