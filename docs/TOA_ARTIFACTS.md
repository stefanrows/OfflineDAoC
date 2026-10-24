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
| Grant credit after an encounter (WP3.3) | `ArtifactMgr.GrantArtifactCredit(player, artifactID)` | refuses an unknown artifact, a class/realm without a version, an existing credit, or a finished DOL-style quest in `QuestID`. Encounter mobs call it for each player in `DyingEventArgs.PlayerKillers` |
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

1. **Encounter credit.** Until the encounters of WP3.3 grant it, the credit is
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

## Not checked

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
