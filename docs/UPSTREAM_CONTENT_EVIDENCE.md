# Upstream Content Evidence: Stages 4 and 7

Date: 2026-10-09. Comparison baseline: upstream commit
`c8b0dca0428666fbe2c6f68d0e4d0363b0d03caa`. This is an evidence record for
the selected equipment-appearance and classic-quest stages in
`UPSTREAM_ADAPTATION_PLAN.md`; it does not mark either stage complete.

## Stage 4: Equipment appearance

### Evidence reviewed

- Upstream `a137337a1ad7103def7e1e0f5d36f05ea22c9e81` adds a 256-ID
  `BotCrossRealmGear` exclusion set. Its generator reads pickable
  `ItemTemplate` rows and the model names in
  `source/tools/observer-model-inspection/objects.csv`.
- The generator calls a model cross-realm when its name contains `Alb`,
  `Mid`, or `Hib` but not the template's realm. It does not inspect the mesh,
  race fit, or the item's equipment slot. Its own source comment says
  cross-realm weapons and shields only look like another realm's equipment.
- The pinned object catalog names model 825 `Norse Cloth Cap` and model 826
  `Celtic Cloth Cap 1`; models 1279 and 1280 are named `Gandalf Hat Hibernia`
  and `Gandalf Hat Midgard`. Those records provide names and NIF indices, not
  a verified player-race fit matrix.
- A read-only query of only the matching `ItemTemplate` rows in the current
  installed catalog found all 256 IDs. This catalog belongs to the mutable
  installation, not a clean pinned release, so its presence check is
  supplementary and is not used as clean-baseline evidence. No character,
  inventory, save, or database hash was read or recorded.
- The 256 entries break down by the local `eInventorySlot` values as follows:

  | Slot | Candidate rows |
  | --- | ---: |
  | Right hand (10) | 16 |
  | Left hand (11) | 129 |
  | Two hand (12) | 36 |
  | Ranged (13) | 5 |
  | Head armor (21) | 48 |
  | Feet armor (23) | 2 |
  | Torso armor (25) | 5 |
  | Leg armor (27) | 2 |
  | Arm armor (28) | 3 |
  | Backpack slot (40) | 10 |

  The 186 hand-slot rows include 118 shield templates (`Object_Type=42`). The
  set therefore contains many items whose model may be visually distinct
  without being a fit failure, plus 10 non-wearable backpack-slot entries.

### Decision and remaining evidence

Realm mismatch alone does not establish a broken appearance. The pinned
classifier would exclude weapons, shields, and miscellaneous entries along
with armor. Even the 60 armor candidates lack evidence here that a particular
mesh fails for a particular race. The existing helmet report (bug 5) remains
unconfirmed: the source catalog's model name and the saved template's realm do
not establish what the supported client rendered or why.

No selection filter, appearance remapping, equipment change, or trade rule was
added. To complete this stage, review the exact local client model assets for
the visible armor candidates, record model-to-slot and model-to-race fit, and
verify the reported cap in the ordinary client. Any resulting rule must be
limited to demonstrated fit failures and future bot selection; it must keep
saved equipment, upgrades, stats, player-to-companion transfers, and valid
cross-realm trades intact. Check that affected classes still have usable gear
before enabling a filter.

## Stage 7: Classic quest pilot

### Pinned source and clean release evidence

- Pinned source baseline: `c8b0dca0428666fbe2c6f68d0e4d0363b0d03caa`.
  Release tag `v0.35b` resolves to `d337d184034f4c84e1e1748fda7f5b2deef135b8`.
  The source delta from the release tag to the pinned baseline is one
  README-only commit; a source/runtime diff is empty. The clean data below is
  identified as the release-tag asset, not as a file at the later source
  commit.
- The release has no separate quest-data or database asset. Its three-part
  runtime archive totals 5,099,005,010 bytes. Selective retrieval used the
  central catalog and full `.003` part only; that part's published SHA-256
  `95f11848419a150d65b4e3b7c853449d4f0b29e9b67a58ec2769365aa7d61bf7` matched.
  ZIP CRC checks passed for the selected clean database, both quest JSON
  members, and `zone181.nav` extracted under `/tmp`; the other archive parts
  were not downloaded.
- Extracted clean DB: `runtime/data/opendaoc.sqlite3.db`, 92,037,120 bytes,
  SHA-256 `0fff1a26913430a9c8f258484a1e2bae69102354c5e287d7d505d200dd3fdfe6`.
  Clean `DataQuest` has 1,540 rows; `classic-quests.json` has 1,302 quest
  entries, and the selected quest is present in all three runtime inputs.
  The release schema for `DataQuest` and `ItemTemplate` matches the current
  static catalog schema column-for-column.
- The upstream `ClassicQuests` hook adds markers, event spawns, race checks and
  monster protection around the ordinary data-quest engine. The selected
  pilot uses only standard data-quest steps; its JSON entry has marker points
  but no race list, custom step, or event spawn. Its `ClassType` is cleared in
  the local migration so the fork does not log a missing `ClassicQuestStep`
  hook. Marker JSON and the Quest Guide integration are intentionally omitted.
- Quest Guide metadata attributes its walkthrough to archived Allakhazam
  material. That guide text is not copied into this fork; the pilot relies on
  the clean `DataQuest` journal fields and an original short summary.

### Selected pilot: Miari's Seed

- Clean `DataQuest` ID 20054 is a standard three-step chain: start with
  Searlait in region 181, interact with Mirari, kill a lesser sylvanshade and
  receive `cq_pouch_of_seeds`, then turn the item in to Mirari. The stored step
  values are `Interact (4) | Kill (0) | CollectFinish (11)`. The fork's
  existing `DataQuest` engine implements those step types; this was source
  inspection, not a runtime test.
- The row is limited to levels 1-5 and Guardian class ID 52. The local
  `eCharacterClass` enum identifies 52 as Guardian, and `DataQuest` qualification
  checks its parsed `AllowedClasses`. There is no race list in the pinned
  `classic-quests.json` entry. Hibernian guide metadata and actor realm values
  are not used to invent a realm or race gate; the pilot preserves source actor
  rows without changing their allegiance.
- The clean row stores `RewardMoney=0|0|0`, `RewardXP=0|0|8`, and no final item
  reward. Those are configured row values, not a promise of literal XP awarded:
  local `ForceGainExperience` may adjust the applied amount around level-stage
  thresholds. No separate archived guide values are imported.
- The sole item row is `cq_pouch_of_seeds` / "Pouch of Seeds": model 488,
  `Item_Type=40`, pickable and droppable, not loot-table eligible, not
  tradable, maximum stack 1, and no realm restriction. It is a quest item, not
  equipment or a new loot drop.
- Read-only source and current static-catalog checks agree on exact required
  actors. Searlait uses template 60165718 / model 355 at
  (424708, 445176, 5952); Mirari uses template 60164082 / model 743 at
  (424908, 446201, 5977); the selected lesser sylvanshade marker is template
  60163235 / model `890;889`, race 2005, at (434611, 435902, 4308). Region,
  spawn level, realm, template class, name, model, and race are all checked by
  the migration preflight. The selected mob marker is one of 21 matching
  lesser sylvanshade spawns in that region. None of those world rows is copied.
- Region 181 has seven zones. The fork's `WorldMgr` scales zone offsets and
  dimensions by 8192, placing Domnann zone 181 at X/Y bounds
  `[393216, 458752)`. All three reviewed actor positions are inside those
  bounds, so the giver, both targets, and the checked map region are in zone
  181 rather than merely somewhere in region 181. The clean v0.35b archive
  contains `runtime/server/navmesh/zone181.nav` (29,575,872 bytes; ZIP CRC32
  `57aec183`); its containing release part's published SHA-256 was verified.
  The current installation also contains its zone-181 runtime mesh. Mesh
  presence does not prove a valid path, floor height, obstruction-free
  interaction point, or ordinary-client reachability. The selected
  static-catalog target is missing only this `DataQuest` row and the item row;
  required actors, templates, spawns, and the relevant schemas are already
  present. The audit read static catalog data only, not characters, inventory,
  saves, or credentials.
- The release `classic-quests.json` entry contains step markers but no
  `Races`, custom step, or event spawn. Markers and Quest Guide integration are
  omitted. The Quest Guide metadata attributes its walkthrough to archived
  Allakhazam material; that walkthrough text is not copied into this fork.

### Additive integration and acceptance gates

The new allowlist contains exactly the `DataQuest` and `ItemTemplate` rows
above. The development utility reads those complete rows from the verified
clean release DB, clears the upstream-only `ClassicQuestStep` hook, and assigns
a fresh `ItemTemplate_ID`; it imports no NPC, spawn, reward-item, progress, or
guide rows. The source database must match its pinned SHA-256. The utility is
dry-run by default and refuses targets outside the OS temporary directory,
the known installation path, hard-link or symlink targets, missing
disposable-copy markers, WAL mode/sidecars, schema mismatches,
actor/template/spawn mismatches, and primary/unique-key conflicts. Explicit
`--apply` first writes a durable prepared manifest with exact planned row
snapshots and a hash-verified named backup, then rechecks under an exclusive
transaction, inserts only missing allowlisted rows, verifies them, and
finalizes the manifest. A recovered commit keeps its backup and ownership but
disables automatic rollback because later unrelated edits cannot be ruled
out. Explicit rollback is limited to an unchanged marked disposable copy; it
rechecks references under the exclusive lock and removes only manifest-owned
rows in place. It retains the higher SQLite `DataQuest` sequence value and
keeps the named backup, so it does not replace the whole database or erase
unrelated rows. Progress/item references, changed rows, changed database
fingerprints, or ambiguous recovery make it refuse.

Under the owner's isolated verification authorization, seven migration tests
passed in 0.214.1 using marked temporary clones of the hash-pinned clean release
database. Checks exercised default dry-run, exact apply, idempotent rerun,
partial preexisting ownership, prepared/committed interruption recovery,
owned-row rollback and unsafe-target/reference refusals. Unrelated synthetic
progress, inventory and currency rows survived; the read-only source fixture
was unchanged. No installed or played database was modified. Syntax parsing,
exact JSON allowlist validation and SOL 6.1 High source/test review passed;
the parent task's server and launcher Release builds also passed.
Before gameplay completion, the owner must check the
ordinary quest offer/journal,
interact-kill-item-turn-in sequence, repeated and failed item steps, configured
reward behavior, restart persistence, bot non-interference, and actual route
and client reachability. Do not add later content batches until the owner
accepts this pilot. Native markers, the Quest Guide button, the war map, and
upstream's wider quest engine remain out of scope.

**Current status:** Stage 4 remains an appearance audit without verified
mesh-to-race or ordinary-client visual evidence. Stage 7 now has a bounded
source-backed pilot and a disposable-copy-verified additive utility, but it is
not installed, gameplay-verified, or owner-accepted. Neither stage is complete.
