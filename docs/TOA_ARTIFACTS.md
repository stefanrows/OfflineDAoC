# Trials of Atlantis: artifact core (WP3.1)

Package WP3.1 of [TOA_PLAN.md](TOA_PLAN.md), 2026-10-24. Implemented in source,
real-client check pending. Sources: gaheris `scripts/toa` (`ArtifactMgr.cs`,
`InventoryArtifact.cs`, `ArtifactCompat.cs`, `ArtifactExperience.cs`,
`ArtifactEncounter.cs`, `ArtifactLog.cs`) at `10d0f84`, itself a port of
DOLSharp's artifact code (author Aredhel). Background:
[TOA_GAHERIS_FIT.md](TOA_GAHERIS_FIT.md), spell handlers:
[TOA_ARTIFACT_SPELLS.md](TOA_ARTIFACT_SPELLS.md).

Our code lives in `source/server/GameServer/gameutils/Atlantis/`:

| File | Role |
|---|---|
| `ArtifactMgr.cs` | catalog of the three artifact tables, experience, levels, encounter credit, scrolls/books |
| `InventoryArtifact.cs` | the inventory item class of every artifact (`DOL.GS.InventoryArtifact`) |
| `ArtifactEncounter.cs` | encounter credit (`DOL.GS.Quests.Atlantis.ArtifactEncounter`) |
| `ArtifactPlayerExtensions.cs` | `CanReceiveArtifact`, `Salutation`, `ActiveQuests`, `FinishedQuests` (was gaheris `ArtifactCompat`) |

`ArtifactLog.cs` is dropped (our `Logging.LoggerManager`). gaheris
`ArtifactExperience.cs` (a `Dying` hook) is replaced by the WP2.0
`GainedExperience` event. Tests: `Tests/UnitTests/UT_ArtifactCore.cs`.

## How an artifact activates

1. `GameServer.StartUp` calls `ArtifactMgr.Init()` right after the world data
   packs, before the market cache, the world, bots and any login can build an
   artifact item (one built against an empty catalog would stay ungated). It loads
   `Artifact`, `ArtifactXItem` and `ArtifactBonus` into an immutable catalog and
   registers the experience listener. A load error is logged and leaves an
   empty catalog; the boot continues and artifacts act as plain items.
2. Every artifact item template must carry `ClassType = 'DOL.GS.InventoryArtifact'`.
   `GameInventoryItem.Create` builds the item class from that column on login,
   in the market cache and for every new copy. House and account vaults and
   companion inventories load raw rows; they now pass them through
   `GameInventoryItem.WrapLoaded` (the login wrap: same ObjectId, owner and
   IsPersisted, fresh save snapshot), so an artifact taken out of a vault or
   handed back by a companion is still an `InventoryArtifact`. Only rows whose
   template names a ClassType are wrapped. Our 197 artifact templates ship with
   `ClassType` NULL; `tools/toa/sql/050-artifact-item-classtype.sql` sets it
   (below). **Without it an artifact loads as a plain item with every bonus
   active at level 0 and never gains experience.**
3. `InventoryArtifact` gates its bonus slots. `Bonus1`-`Bonus10` (amount and
   type), `SpellID`, `SpellID1`, `ProcSpellID` and `ProcSpellID1` read 0 until
   the artifact reaches the slot's `ArtifactBonus.Level`. A slot without an
   `ArtifactBonus` row is a level 0 slot. Equip, unequip, the stat windows,
   item use, procs and the delve all read these properties, so they all see
   the same state.

**Why not upstream's way.** DOL and gaheris clone the item template and zero the
locked bonuses in the clone. Our inventory row saves its template relation
(`DbInventoryItem.ITWrapper`): the clone is dirty and unpersisted, so the
database layer would try to insert it over the shared `ItemTemplate` row
(primary key `Id_nb`) on the next save. The getters never write the template.
gaheris also set `CanUseEvery` from `Artifact.ReuseTimer`; that writes the shared
template, and our rows carry the placeholder `1` (second) for 56 of 57
artifacts, so it is not applied. Reuse comes from the spells' recast delays.

## How an artifact levels

- **Source:** `GamePlayerEvent.GainedExperience`, raised by
  `GamePlayer.GainExperience` with the arguments it received, before the server
  XP rates (WP2.0). Only `eXPSource.NPC` and `eXPSource.Player` count: NPC
  kills (a persistent companion's kill is booked to its owner), player kills
  and autonomous-bot kills. Quests, missions, tasks, praying, GM grants
  (`/player xp`) and zone bonuses (`Other`) do not. A praying player gains
  nothing. `/player xpa` grants `NPC` experience on purpose, for GM tests. A
  behaviour quest step (`GiveXPAction`) also books `NPC` experience and so
  counts; no artifact quest uses it.
- **Amount per kill:** `(base + camp + group + outpost) × Artifact.XPRate / artifact_xp_rate`,
  as in DOL. Guild and BAF bonuses do not count. The guild artifact buff adds
  `guild_buff_artifact_xp` % (default 5). `artifact_xp_rate` 0 or below stops
  artifact experience.
- **Who gains:** every equipped artifact, including the weapon slots that are
  not the active weapon (DOL behaviour). Backpack and vault items gain nothing.
- **Levels:** ten, at 50 M artifact experience each (level 10 at 500 M).
  Experience is stored in `Inventory.Experience` and stops at 500 M. The level is
  derived from it, so a reload, a trade, a vault trip or a consignment sale keeps
  it.
- **Unlock while worn:** when the level changes on an equipped artifact, the
  newly unlocked stat slots go straight into the wearer's item bonuses
  (`GamePlayer.ApplyItemBonusChange`, which refreshes the stat and resist
  windows). A level change in the backpack touches nothing; the next equip adds
  the current state. A GM lowering the experience takes the bonuses back.
- **Messages:** "Your X has gained experience." per artifact and kill;
  "has gained a level!" per level; "has gained a new ability!" when a slot that
  carries something unlocks at that level.

**Rates on our data.** All 57 rows have `XPRate` 5; the property default is
350, so an artifact gains one seventieth of the kill's experience. A level 50
character killing a level 50 mob alone gets 41.9 M base experience (cap 52.4 M
at `XP_Cap_Percent` 125), worth about 0.6 M to each artifact: roughly 84 such
kills per artifact level, 840 to level 10, fewer with camp and group bonus.
The server's `xp_rate` (10 on the owner's save) does **not** speed artifacts
up; lower `artifact_xp_rate` for that (35 is ten times faster). Pacing is DOL's
default and unmeasured in game.

## The ClassType SQL

`tools/toa/sql/050-artifact-item-classtype.sql`, SQLite:

```sql
UPDATE ItemTemplate
SET ClassType = 'DOL.GS.InventoryArtifact'
WHERE Id_nb IN (SELECT ItemID FROM ArtifactXItem)
  AND (ClassType IS NULL OR ClassType = '');
```

- Verified on a repaired copy of the dev snapshot (`.toa-probe/`, never the live
  save): first run 197 rows, second run 0, no template outside `ArtifactXItem`
  touched, `quick_check` ok afterwards.
- 3 of the 200 `ArtifactXItem` rows name no template (`Enyalios_Boots_Studded`,
  `GOV_Leather`, `Lost_Memories_Archer`; the last is probably a typo for
  `Lost_Memories_Archers`). They are ignored here; fix them with the WP3.1b data.
- `ItemTemplate` is pre-cached at server start: run it while the server is
  stopped. The data-pack mechanism (WP1.1a) ships it.
- Existing copies need nothing else: after the update a player's artifact
  reloads as `InventoryArtifact` at the level its stored experience gives (0 for
  every artifact in circulation today), so its higher-level bonuses lock.

## Our placeholder rows

Our `Artifact` rows carry `ArtifactID` `1001`..`1057`, `ScholarID` = the same
number, `EncounterID` = `QuestID` = `Credit` = a template name such as
`Alvarus_Leggings`, `Zone` `0`, and `ArtifactXItem.Version` `0`..`15`. The core
tolerates all of it:

- `GetQuestType` returns null for anything that is not an `AbstractQuest` type
  (no exception for empty or odd names), so `GetEncounterType` is null for
  every row.
- `GetScholars` returns the placeholder; nothing matches it to an NPC.
- `GetArtifactVersions` keys versions by the `Version` text as stored.
- `GetArtifactIDFromCredit` accepts the `Credit` value or the credit item ID
  (`lower(Credit) + "_credit"`, as our 82 credit items are named) in any case.
- Unknown IDs return null or false everywhere; nothing throws on a missing key.

## Extension points for WP3.1b, WP3.2 and WP3.3

| Need | Use | Notes |
|---|---|---|
| Grant credit after an encounter (WP3.3) | `ArtifactMgr.GrantArtifactCredit(player, artifactID)` | refuses an unknown artifact, a class/realm without a version, an existing credit, or a finished DOL-style quest in `QuestID`. `ArtifactEncounterCredit` calls it for each earner of a mapped kill (see "Encounter credit") |
| Credit from a credit item or bounty merchant (WP3.1b) | `ArtifactMgr.GrantArtifactBountyCredit(player, creditOrItemId)` | matches `Credit` or `<credit>_credit` |
| "Has the player done the encounter?" (scholar) | `ArtifactMgr.HasEncounterCredit(player, artifactID)` | per artifact ID, not per type |
| Scholar hands the artifact over | `ArtifactMgr.ConsumeEncounterCredit(player, artifactID)`, then `GetArtifactVersions(artifactID, class, realm)` and `GameInventoryItem.Create(template)` | `Create` returns an `InventoryArtifact` once the ClassType SQL ran |
| "Has the player already received it?" | `ArtifactMgr.HasFinishedArtifactQuest` | a finished `ArtifactQuest` with the artifact ID (WP3.1b), or a DOL-style quest type named in `QuestID`; the single place `GrantArtifactCredit` asks |
| Readable artifact name for a scholar | `ArtifactMgr.GetArtifactName` | first version's item name, else Credit with spaces (WP3.1b) |
| Scroll combine | `ArtifactMgr.TryCombineScrolls` (backpack drop), `CanCombine` / `CombineScrolls` / `HasBook` | wired in WP3.1b, see "Earning an artifact"; `artifact_scroll` is optional |
| Active quests list (port of Scholar, ArtifactScholar, ArtifactTurnInQuest) | `player.ActiveQuests()` | renamed from gaheris `QuestList()`, which collides with our `GamePlayer.QuestList` property. It is a snapshot: remove with `player.QuestList.TryRemove` as `AbstractQuest.FinishQuest` does |
| Reload the catalog after a data pack | `ArtifactMgr.LoadArtifacts()` or `ArtifactMgr.Load(rows...)` | existing items keep the requirements they were built with until the next login |

**Encounter credit format.** A credit is one `ArtifactEncounter` quest row,
`Step` -1, custom property `artifactID=<id>`, kept only in the player's finished
list (never in the journal). One class serves every artifact. A DOL-style
subclass per artifact still works if `EncounterID` names it.

**The step mismatch, fixed.** DOL marked finished quests -1, and
`GamePlayer.RemoveEncounterCredit` consumed only -1. Our
`AbstractQuest.FinishQuest` writes -2, sends a journal remove for index 0 (the
credit was never in the journal, so the player's first quest line would blank)
and refuses a second finished quest of the same type (which would allow only one
artifact credit ever with a shared class). `ArtifactEncounter.FinishQuest` is
overridden: Step -1, into the finished list, no packet. `RemoveEncounterCredit`
now accepts any finished step (-1 or -2). For credits of the generic class use
`ConsumeEncounterCredit(player, artifactID)`: `RemoveEncounterCredit(type)` would
remove the first credit of that type, whatever its artifact.

## Core fixes from WP3.0

| Where | Before | Now |
|---|---|---|
| Ceremonial Bracer mez intercept, `CCSpellHandler.cs` (Mesmerize `CheckSpellResist`) | legacy `FindEffectOnTarget(target, "CeremonialBracerMezz")`, never found the ECS effect | `ArtifactSpells.FindEffect(target, eSpellType.CeremonialBracerMezz)`, then `End()`: the charge is used up and the mez is negated. Only a mez that would land uses it; one the target is immune to (or already mezzed) leaves the charge |
| Cloudsong (Wings Dive) fall damage, `GamePlayer.CalcFallDamage` | legacy `FindEffectOnTarget(this, "CloudsongFall")` | `ArtifactSpells.FindEffect(this, eSpellType.CloudsongFall)`; reduces each fall by `Spell.Value` % for the effect's duration and is not used up (DOL behaviour) |

The stun intercept stays commented out as upstream. The stock
`DexterityConstitutionDebuff`'s always-on conflict (TOA_PLAN WP3.0 follow-ups)
was fixed in WP3.0b with its own effect type `eEffect.DexConDebuff`
([TOA_ARTIFACT_SPELLS.md](TOA_ARTIFACT_SPELLS.md), "WP3.0b"); implemented in
source, real-client check pending.

## Items that must not be rebuilt

- **Dye, effect and reskin NPCs** (`EffectNPC`, `ItemModel`) rebuild an item as a
  new unique template with a new `Id_nb` and the copied ClassType. An artifact
  would come back as an `InventoryArtifact` without an artifact ID: every bonus
  unlocked, experience 0. Both NPCs now refuse an artifact when it is handed to
  them ("I dare not work on an artifact. Its power is bound to its form as it
  is."). The GM `/item` unique-copy command has the same effect and is left as
  a GM tool.
- **Bot gear.** `BotEquipment` no longer rolls artifact templates for bots
  (high-level rolls could match the 40 realm-bound artifact templates): a bot never
  gains artifact experience, so the item would stay at level 0 for good.

## Delve

An artifact delves as "Artifact (Current level: N)", the percentage towards the
next level, the next level that unlocks something, "(Earns exp: Slaying enemies
and monsters found anywhere.)", then "Locked abilities:" with a `[Ln]` tag per
locked stat, spell or proc, then the normal item delve (which shows only what is
unlocked). GMs see artifact ID, level and experience before the technical block.

## Earning an artifact (WP3.1b)

Implemented in source, real-client check pending. Code:
`gameutils/Atlantis/ArtifactAwakening.cs` (rules and every text),
`gameutils/Atlantis/ArtifactQuest.cs` (version choice and the "awakened"
record), `gameobjects/Atlantis/Scholar.cs`, `ArtifactScholar.cs` and
`ArtifactCreditMerchant.cs` (thin NPC classes), the scroll hook in
`GamePlayerInventory.CombineItems`. Data: packs 051, 052 and 061
([TOA_DATA_PACKS.md](TOA_DATA_PACKS.md)). Tests:
`Tests/UnitTests/UT_ArtifactAwakening.cs`.

### The player's way to an artifact

1. **Encounter credit.** Killing the artifact's guardian grants it (WP3.3, see
   "Encounter credit" below). It can also be
   bought: Aphaestia (Albion 70, Midgard 71, Hibernia 72) sells one credit token
   per artifact for bounty points (289 to 1,040 BP, the template prices). Hand
   the token to her or to any scholar: the credit is recorded and the token is
   used up. She does not sell a token that would be useless (credit already
   held, an unredeemed token of that artifact already carried, artifact already
   awakened or carried, no version the class can use), and sells one at a
   time. A useless token handed in stays with the player.
2. **The story.** Each of the 54 artifacts with a scroll set has three pages.
   They drop from Atlantis creatures (pack 040, db-public, 15 %; every page of
   53 sets drops in all three realm copies, "Ring of Fire, Scroll 3 of 3"
   drops nowhere) and are sold by the binders Zosyne and Mnosus (pack 023, 300
   BP each). Dropping one page onto another in the backpack combines them:
   two pages give "<story>, parts 1 and 2 of 3", the third gives the book
   "<story>, complete". Combining is optional: a scholar also takes the three
   pages (or a pair and a page) when one of them is handed over and the rest
   is in the backpack.
3. **The scholar.** Every Scholar (26 NPCs, Loremasters and Loremistresses in
   Midgard) and every Artifact Scholar (5) awakens every artifact that has a
   scroll set; our rows name no scholar per artifact (ScholarID is the
   placeholder). Hibernia's city (72) has no plain Scholar in the gaheris
   spawns; Artifact Scholar Elmer serves every artifact there. Hand over the
   book or a page. The scholar checks, in this
   order: not awakened before, not carried already, a version for the class
   and realm, level 45 or more, encounter credit, the whole story. A refusal
   names the artifact and what is missing (the missing page names, or where
   credit comes from); nothing is taken.
4. **The version.** The versions offered are the `ArtifactXItem` rows of the
   player's realm (or realm 0) whose template allows the class, narrowed to
   what the character can wear or wield. If that leaves none, the scholar
   refuses as for a class without a version (logged); nothing is taken. One
   version: the scholar hands it over at once. Several: the scholar keeps the
   book and asks, for example "[Cloth], [Leather], [Plate]"; the player
   whispers one (or clicks it). The open choice is a journal entry ("Awakening
   Alvarus's Leggings") that survives a relog; abandoning it gives the book back.
   Labels come from the template: armor class, shield size, weapon kind with
   damage type where that varies ("Two-Handed (Crush)"), else the last word of
   the item ID ("Caster", "Melee", "Constitution").
5. **Hand-over.** The artifact goes into the backpack (`GameInventoryItem.Create`,
   an `InventoryArtifact` at level 0). Then the book, the encounter credit and
   the open choice are used up and a finished `ArtifactQuest` with the artifact
   ID is stored. With a full backpack nothing changes and the scholar asks for
   room.
6. **Once per character.** The finished record makes
   `ArtifactMgr.HasFinishedArtifactQuest` true for that artifact: no second
   copy from a scholar, no new credit (encounter or token), even after the item
   was destroyed, vaulted or traded.

**The three artifacts without a scroll set** (A Flask, Bruiser, Shield of
Khaos; BookID empty since pack 051) are awakened only by an Artifact Scholar
(Jarron and Heiron in 70, Alaria and Erelacen in 71, Elmer in 72), on credit
alone: the player names the artifact, the version choice works as above. A
plain Scholar points to the Artifact Scholars. WP3.2's "generated sets" for
them are not needed.

**Conversation.** A click gives the greeting, an open version choice first, the
bookless artifacts at an Artifact Scholar, and two keywords: `[artifacts]`
(every artifact this scholar can awaken for the class) and `[progress]` (per
artifact begun: awakened, credit, pages held). Whispering an artifact's name
tells what is still missing. Artifact names are the item name of the first
version ("Alvarus's Leggings", `ArtifactMgr.GetArtifactName`); the message
columns of our rows hold the placeholder `1001` and are shown only if a pack
ever puts readable text there (`ArtifactMgr.ReadableText`). This covers WP3.4
for the scholars; the per-artifact live dialogue (DOLSharp's 133 `SayTo` lines)
is not ported.

### What changed in the core

| Where | Change |
|---|---|
| `ArtifactMgr.HasFinishedArtifactQuest` | also true for a finished `ArtifactQuest` with the artifact ID (the single place `GrantArtifactCredit` and the scholars ask) |
| `ArtifactMgr.GetPageNumbers`, `HasBook` | accept generic (Object_Type 0, our scroll templates) and magical (41, DOL) backpack items; artifacts without BookID never match |
| `ArtifactMgr.CreatePages`, `CombineScrolls`, `FindPageTemplate` | build pairs and books from the generic `artifact_scroll` template if the save has one, else as a unique copy of a real page template (never of a pair: the template of a non-unique scroll, the template before `#` in a combined item's ID, or the template named like Scroll1-3), so an abandoned version choice can always rebuild the book (no new template ships) |
| `ArtifactMgr.TryCombineScrolls` | the backpack drop: removes both scrolls, puts the combined item in the target slot, tells the player; anything else falls through to the normal move |
| `ArtifactMgr.GetAllVersions`, `GetArtifactName`, `HasScrollSet`, `ReadableText` | new lookups |

**Not ported:** DOLSharp's `ArtifactTurnInQuest` (an Artifact Scholar swaps the
version of an artifact the player carries, losing its experience). It relied on
DOL's `dmg;wep;stat` Version keys; ours are `0`..`15`. Add it later on top of
`GetChoices` if the owner wants it.

## Encounter credit (WP3.3)

Implemented in source, real-client check pending. Code:
`gameutils/Atlantis/ArtifactEncounterCredit.cs`, table
`CoreDatabase/Tables/DbArtifactEncounterTarget.cs`; data: packs 070 and 071
([TOA_DATA_PACKS.md](TOA_DATA_PACKS.md)). Tests:
`Tests/UnitTests/UT_ArtifactEncounterCredit.cs`, `UT_WorldDataPacks`.

### How a kill earns credit

- **The table.** `ArtifactEncounterTarget` has one row per artifact and creature:
  `ArtifactID`, `MobName` (compared without case), `Region` (0 = any) and
  `CreditMode` (`group` or `battlegroup`) and `Zone` (where the creature
  stands, measured from its spawns; text only). The server creates the table at boot
  from the DataObject; pack 070 fills it. `ArtifactMgr.Init` loads it after the
  artifact catalog and registers a global `Dying` listener. A load error
  leaves an empty map; Aphaestia's tokens still work.
- **The kill.** A mapped creature dies a death that pays experience: not a
  GameBot, not a pet, not recently charmed (the gate of `OnNpcKilled`). The
  name and region pick the rows.
- **Who earns it.** `group`: every player in `DyingEventArgs.PlayerKillers`
  (the attackers, pet owners and their group members within experience range,
  as `GameLiving.ProcessDeath` builds it; the killer alone if the list is
  missing). `battlegroup`: those players plus every member of their
  battlegroups. Each earner must be alive, in the world, in the region and
  within experience range of the body (`WorldMgr.MAX_EXPFORKILL_DISTANCE`).
  A persistent companion's kill counts for its owner, as for experience; a
  companion or world bot never earns credit itself (they are GameNPCs, never
  in the list and never battlegroup keys).
- **The grant.** `ArtifactMgr.GrantArtifactCredit` per earner and artifact:
  nothing for a player who already holds the credit, has awakened the artifact
  (also after the scholar used the credit up) or has no version of it for the
  class and realm. The player reads "With Minkhat defeated in Mesothalassa, you
  have earned the encounter credit for Alvarus's Leggings. Bring its story to a
  scholar to awaken it." A common name takes the article ("With the protector
  of light defeated in Oceanus Boreal"); the zone is the world's zone at the
  body, else the table's. For the three bookless artifacts the line points to
  an Artifact Scholar. There is no level gate at the kill; the scholar asks for 45.
- **The scholar's hint.** Without credit a scholar names the guardians and
  their zone: "Its secret is guarded: defeat Hallea, Kosma or Zoea in
  Mesothalassa, or trade tales of your battles with Aphaestia ...". Guardians
  of several zones read "Landrine in Stygian Delta or Kertom in Arbor Glen".

### How the mapping was made

Sources, in order of preference (the owner's rule: invent as little as possible):

1. **DOLSharp** artifact quests, step 1 ("Defeat Minkhat."): 29 artifacts.
   21 of the 27 named targets stand in the gaheris spawns (some under a longer
   name: "Behrooz the Jinni", "High Priest Akil"; "the protectors" are the
   protectors of light and dark). The six missing ones spawn with pack 071.
   DOLSharp's Belt of Oglidarsh step says "Defeat Snatcher.", a copy of the
   Snatcher quest; it is not used for the belt.
2. **Camelot Herald** patch notes and Q&A in the DAoC knowledge base that name
   an encounter's creature: 12 artifacts (Golden Spear's three bosses,
   Phoebus' Harp's three holders, Kertom, Karise, Itet, Ferenc, ...).
3. **Chosen**: 16 artifacts whose quest is a stub or not a kill (a chest, the
   flask, the gladiator rounds, the cloak). Each gets a named creature of the
   region the Herald's scholar table gives the artifact (Oceanus, Stygia,
   Volcanus or Aerus Haven), not used by another artifact, standing in all
   three realm copies.

**Credit mode** follows the Herald's 1.75 Q&A list of single-group encounters
(20 artifacts, from Eternal Plant to Cloudsong); every other encounter is a
battlegroup encounter, as the Herald says. A Flask gave credit by range; here
it is a battlegroup encounter.

**Live mechanics not reproduced.** Every encounter is a kill. The scripted
parts of live (Aten's chest, the flask past the taurs, Linos' key, the
gladiator rounds, the cloak and statue of Shades of Mist, Sobekhotep's spawn
conditions, Battler levelling on kills) are not built. Credit is the only
thing the kill gives; the creatures keep their own loot (the six new ones have
no loot table). The gaheris encounter mob classes (`GaherisDoppleganger`,
`GaherisSplitter`, `GaherisSeedsman`, `GaherisRewardMob`) are not ported: no
mob row uses them, placing them would be invention, and `GaherisRewardMob`'s
flat realm point payout would clash with our Old Frontiers rules.

### Artifact to creature

Every artifact of our 57 has at least one credit kill; none is without. "(071)"
marks the six creatures spawned by pack 071. The zone column is the `Zone`
value of the rows, with the Herald region of the artifact in brackets.

| ID | Artifact | Creature | Zone | Credit | Source and reason |
|---|---|---|---|---|---|
| 1001 | Alvarus's Leggings | Minkhat | Mesothalassa (Oceanus) | battlegroup | DOLSharp: "Defeat Minkhat." |
| 1002 | Arms of the Winds | Raging Tornado | Oceanus Notos (Oceanus) | battlegroup | DOLSharp: "Defeat Raging Tornado." |
| 1003 | Aten's Shield | Aidoios | Oceanus Hesperos or Oceanus Anatole (Oceanus) | battlegroup | chosen: DOLSharp: "Find the right chest." A named guardian of Oceanus Hesperos instead |
| 1004 | A Flask | Taur Scribe | Typhon's Reach (Volcanus) | battlegroup | chosen: live: carry the flask past the taurs (credit by range); the named taur of Typhon's Reach |
| 1005 | Band of Stars | Chisisi | Stygian Delta (Stygia) | group | DOLSharp: "Defeat Chisisi." |
| 1006 | Battler | Battler (071) | Typhon's Reach (Volcanus) | battlegroup | DOLSharp: "Defeat Battler."; Herald: "the Battler monster"; spawned by pack 071 |
| 1007 | Belt of the Moon | protector of dark | Oceanus Boreal (Oceanus) | group | DOLSharp: "Defeat the protectors."; the dark ones for the moon (Oceanus Boreal) |
| 1008 | Belt of the Sun | protector of light | Oceanus Boreal (Oceanus) | group | DOLSharp: "Defeat the protectors."; the light ones for the sun, by the belt of the sun barrier |
| 1009 | Bracer of Zo'arkat | Krojer | Oceanus Anatole (Oceanus) | battlegroup | chosen: DOLSharp: "Defeat the champion." Herald: a sea encounter tested with two groups; Krojer, master of the champions of Oceanus Anatole |
| 1010 | Braggart's Bow | Karise | Green Glades (Aerus) | battlegroup | Herald: "Karise, the Braggart's Bow mob" |
| 1011 | Bruiser | taur warlord | Typhon's Reach (Volcanus) | battlegroup | DOLSharp: "Kill Taur Warlord." |
| 1012 | Ceremonial Bracers | wavelord (071) | Mesothalassa (Oceanus) | battlegroup | DOLSharp: "Defeat a wavelord."; spawned by pack 071 |
| 1013 | Cloudsong | Eramai | Green Glades (Aerus) | group | DOLSharp: "Defeat Eramai." |
| 1014 | Crocodile Tear Ring | Cynere | Oceanus Boreal (Oceanus) | group | DOLSharp: "Defeat Cynere." |
| 1015 | Crocodile's Tooth Dagger | Itet | Stygian Delta (Stygia) | battlegroup | Herald: "the Crocodile's Tooth Dagger encounter ... did Itet drop" |
| 1016 | Crown of Zahur | Zahur | Oceanus Boreal (Oceanus) | battlegroup | Herald: "Zahur and his minions" (patch notes) |
| 1017 | Cyclops Eye Shield | Kertom | Arbor Glen (Aerus) | battlegroup | Herald: "Cyclops' Eye ... Kertom would not spawn" |
| 1018 | Dream Sphere | Torih | Stygian Delta (Stygia) | battlegroup | DOLSharp: "Defeat the attackers and end Torih's dream." |
| 1019 | Eerie Darkness Stone | Runihura | Oceanus Boreal (Oceanus) | group | chosen: no source names a creature; Runihura of Oceanus Boreal, whose temple holds a djinn stone |
| 1020 | Egg of Youth | Sililia | Oceanus Notos (Oceanus) | battlegroup | DOLSharp: "Defeat Sililia." |
| 1021 | Eirene's Hauberk | Linos (071) | Mesothalassa (Oceanus) | battlegroup | DOLSharp: "Defeat Linos."; Herald: Linos and Khonorn's half of the key; spawned by pack 071 near Khonorn |
| 1022 | Enyalios Boots | Behrooz the Jinni | Arbor Glen (Aerus) | battlegroup | DOLSharp: "Defeat Behrooz." |
| 1023 | Erinys Charm | Samut | Typhon's Reach (Volcanus) | battlegroup | DOLSharp: "Defeat Samut." |
| 1024 | Eternal Plant | Sobekhotep (071) | Oceanus Boreal (Oceanus) | group | DOLSharp: "Defeat Sobekhotep."; Herald: Sobekhotep among swimming sobekites; spawned by pack 071 |
| 1025 | Fool's Bow | Kirkleis | Oceanus Anatole (Oceanus) | group | chosen: no source names a creature; Kirkleis of Oceanus Anatole |
| 1026 | Foppish Sleeves | Gaspara | Green Glades (Aerus) | battlegroup | chosen: live: the Blood Soaked Kibisis and its snake guardians; Gaspara of Green Glades |
| 1028 | Golden Spear | Landrine, Hounthro, Jomnaer | Stygian Delta (Stygia) | battlegroup | Herald: "The Golden Spear story encounter bosses Landrine, Hounthro and Jomnaer" |
| 1029 | Guard of Valor | Danos | Green Glades (Aerus) | battlegroup | DOLSharp: "Defeat Danos." |
| 1030 | Golden Scarab Vest | Tey | Stygian Delta (Stygia) | group | Herald: the maubastet Tey of the encounter (patch notes) |
| 1031 | Harpy Feather Cloak | frustrated harpy | Oceanus Notos (Oceanus) | group | DOLSharp: "Defeat the Harpies."; the one named harpy of Oceanus Notos |
| 1032 | Healer's Embrace | Mesedsubastet | Stygian Delta (Stygia) | group | DOLSharp: "Defeat Mesedsubastet." |
| 1033 | Jacina's Sash | Cyrek | Arbor Glen (Aerus) | battlegroup | DOLSharp: "Defeat Cyrek." |
| 1034 | Kalare's Necklace | Warrior of Stone | Oceanus Notos (Oceanus) | battlegroup | DOLSharp: "Defeat the Warrior of Stone and escape with the necklace." |
| 1035 | Gem of Lost Memories | Night Terror | Oceanus Hesperos (Oceanus) | group | Herald: "How often does the Night Terror mob spawn for The Gem Of Lost Memories" |
| 1036 | Maddening Scalars | Ylyssan | Typhon's Reach (Volcanus) | battlegroup | DOLSharp: "Defeat Ylyssan." |
| 1037 | Malice's Axe | Malamis (071) | Typhon's Reach (Volcanus) | battlegroup | DOLSharp: "Defeat Malamis."; Herald: Malamis credits a group as well as a battlegroup; spawned by pack 071 |
| 1038 | Nailah's Robes | Deshrentan | Stygian Delta (Stygia) | group | chosen: no source names a creature; Deshrentan of Stygian Delta |
| 1039 | Night's Shroud Bracelet | Kythera | Oceanus Anatole (Oceanus) | battlegroup | DOLSharp: "Defeat Kythera." |
| 1040 | Oglidarsh's Belt | tame shark | Mesothalassa, Oceanus Hesperos or Oceanus Boreal (Oceanus) | battlegroup | chosen: DOLSharp names Snatcher (a copy of the Snatcher quest); Herald: the encounter's sharks. The three lone tame sharks |
| 1041 | Orion's Belt | Peridas | Oceanus Hesperos (Oceanus) | group | chosen: Herald pairs it with the Gem of Lost Memories; Peridas of the same Hesperos island |
| 1042 | Phoebus' Harp | Hallea, Kosma, Zoea | Mesothalassa (Oceanus) | battlegroup | Herald: "Kosma, Hallea, and that other one that starts with a Z"; credit for slaying the holder of the harp |
| 1043 | Ring of Fire | Siraadi | Typhon's Reach (Volcanus) | group | chosen: DOLSharp: "Win three consecutive rounds of battle with the gladiators."; Siraadi of Typhon's Reach |
| 1044 | Ring of Unyielding Will | Mordom | Oceanus Notos (Oceanus) | group | DOLSharp: "Defeat Mordom." |
| 1045 | Scepter of the Meritorious | Ferenc | Oceanus Boreal (Oceanus) | battlegroup | Herald: "sceptor of meritorious ... before ferenc decides to join the frey" |
| 1046 | Scorpion's Tail Ring | Terkari | Stygian Delta (Stygia) | group | DOLSharp: "Defeat Terkari." |
| 1047 | Shades of Mist | Vazul | Ashen Isles (Volcanus) | battlegroup | chosen: DOLSharp: "Find the Shades of Mist." Herald: the Long Black Cloak, criosphinxes; Vazul of Ashen Isles |
| 1048 | Mariasha's Sharkskin Gloves | Deep-Chasm Matriarch | Oceanus Hesperos (Oceanus) | group | chosen: Herald: a treasure chest step; the Deep-Chasm Matriarch among the sharks of Oceanus Hesperos |
| 1049 | Shield of Khaos | Chief Creon | Green Glades (Aerus) | battlegroup | DOLSharp: "Defeat Chief Creon." |
| 1050 | Snakecharmer's Weapon | Maedion | Land of Atum (Stygia) | group | Herald: "In the Snake Charmer's Weapon encounter, Maedion will move" |
| 1051 | Snatcher | Snatcher (071) | Ashen Isles (Volcanus) | battlegroup | DOLSharp: "Defeat Snatcher."; spawned by pack 071 |
| 1052 | Staff of the Gods | Naxos Tidal Lord | Mesothalassa (Oceanus) | battlegroup | chosen: DOLSharp: "Obtain the staff ..."; Herald: the jagger sharks of Mesothalassa; the Naxos Tidal Lord there |
| 1053 | Spear of Kings | Seviach | Oceanus Notos (Oceanus) | battlegroup | chosen: Herald: the creature with the spear stands in Oceanus Notos; Seviach |
| 1055 | Tartaros' Gift | High Priest Akil | Typhon's Reach (Volcanus) | battlegroup | DOLSharp: "Defeat Akil." |
| 1056 | Traitor's Dagger | Sadri the Traitor | Oceanus Boreal (Oceanus) | group | chosen: no source names a creature; Sadri the Traitor of Oceanus Boreal |
| 1057 | Traldor's Oracle | Traldor | Oceanus Boreal (Oceanus) | battlegroup | chosen: no source names a creature; Traldor of Oceanus Boreal |
| 1058 | Winged Helm | Hermes | Oceanus Notos (Oceanus) | battlegroup | Herald: "Winged Helm: Reduced the odds of Hermes healing himself" |
| 1059 | Wings Dive | Keres | Arbor Glen (Aerus) | battlegroup | Herald: "Keres now drops the Wings Dive artifact" |

## Not checked

- Encounter credit: no server start and no real client. The kill message, the
  battlegroup reach and the six new spawns (place, depth, difficulty) are
  unverified in game; the chosen creatures of 16 artifacts are a judgement,
  not a source.
- No server start and no real client: messages, the stat window refresh on
  level-up, the delve layout and the mez intercept are unverified in game.
- Artifact pacing on our rates is a calculation, not a measurement.
- The ClassType SQL ran on a snapshot copy only; the data pack applies it.
- Weapon artifacts: bonuses follow the active weapon (`OnItemEquipped` /
  `OnItemUnequipped` pairs); switching weapons during a level-up was read in
  code, not tested.
- Companions wearing an artifact a player gave them: they never gain artifact
  experience (the event is a player event) and see only the slots unlocked by
  the experience the item already has. WP2.4 decides.
Pack 050-artifact-item-classtype ships the ClassType SQL (pack set 2, 0.321.0).
  yet; the data-pack owner adds it.
- Cloudsong fall damage is guarded by a source test only (`CalcFallDamage` needs
  a live client and region).
