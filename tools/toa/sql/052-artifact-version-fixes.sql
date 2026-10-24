-- 052-artifact-version-fixes: three ArtifactXItem rows that point at the wrong place (WP3.1b).
-- Hand-written. Every statement is guarded, so a second run changes nothing.
--
-- * 1035 Gem of Lost Memories names 'Lost_Memories_Archer'; the template is
--   'Lost_Memories_Archers' (same item name, the archer version). Point the row at it and
--   give the template the artifact class type that pack 050 could not (it was not named).
-- * 'Maddening_Scalars_Studded' sits under 1035 (Gem of Lost Memories) as version 6; it is
--   the studded version of 1036 Maddening Scalars, whose other six versions are 0-5.
-- * 'GOV_Leather' (1029) and 'Enyalios_Boots_Studded' (1022) name no template anywhere;
--   the rows are dropped so the scholars do not log a missing template on every lookup.

UPDATE "ArtifactXItem" SET "ItemID" = 'Lost_Memories_Archers'
WHERE "ArtifactID" = '1035' AND "ItemID" = 'Lost_Memories_Archer'
  AND EXISTS (SELECT 1 FROM "ItemTemplate" t WHERE t."Id_nb" = 'Lost_Memories_Archers')
  AND NOT EXISTS (SELECT 1 FROM "ItemTemplate" t WHERE t."Id_nb" = 'Lost_Memories_Archer');

UPDATE "ItemTemplate" SET "ClassType" = 'DOL.GS.InventoryArtifact'
WHERE "Id_nb" = 'Lost_Memories_Archers' AND ("ClassType" IS NULL OR "ClassType" = '')
  AND EXISTS (SELECT 1 FROM "ArtifactXItem" x WHERE x."ItemID" = 'Lost_Memories_Archers');

UPDATE "ArtifactXItem" SET "ArtifactID" = '1036'
WHERE "ArtifactID" = '1035' AND "ItemID" = 'Maddening_Scalars_Studded';

DELETE FROM "ArtifactXItem"
WHERE "ItemID" IN ('GOV_Leather', 'Enyalios_Boots_Studded')
  AND NOT EXISTS (SELECT 1 FROM "ItemTemplate" t WHERE t."Id_nb" = "ArtifactXItem"."ItemID");
