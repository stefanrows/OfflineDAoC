-- World data pack 050-artifact-item-classtype
-- Artifact item templates load as DOL.GS.InventoryArtifact: the 197 templates named by ArtifactXItem that have no class type.
-- Built by tools/toa/build_data_packs.py from tools/toa/sql. Do not hand-edit:
-- the server applies a pack once per save, so a fix ships as a new pack.
-- Applied by the server in one transaction (docs/TOA_DATA_PACKS.md).

-- ======== part: 050-artifact-item-classtype.sql ========

-- Trials of Atlantis, TOA_PLAN WP3.1 (docs/TOA_ARTIFACTS.md). Hand-written, SQLite.
--
-- Artifact item templates load as DOL.GS.InventoryArtifact. GameInventoryItem.Create
-- builds an inventory item's class from ItemTemplate.ClassType. Without this the
-- 197 artifact templates (ClassType NULL in the shipped world DB) reload as plain
-- items with every bonus active at level 0, and they never gain artifact experience.
--
-- Scope: every template named by an ArtifactXItem row (200 rows; 197 resolve, the
-- three without a template are ignored). Only rows without a class type are
-- touched, so a deliberate custom class is kept.
--
-- Idempotent: a second run changes 0 rows. ItemTemplate is pre-cached at server
-- start, so run this while the server is stopped (the data pack does).
-- Reversible: UPDATE ItemTemplate SET ClassType = NULL
--   WHERE ClassType = 'DOL.GS.InventoryArtifact';
-- (Artifact experience stays in Inventory.Experience either way.)

UPDATE ItemTemplate
SET ClassType = 'DOL.GS.InventoryArtifact'
WHERE Id_nb IN (SELECT ItemID FROM ArtifactXItem)
  AND (ClassType IS NULL OR ClassType = '');
