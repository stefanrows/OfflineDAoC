-- World data pack 024-toa-arbiter-npcs
-- The Master Level NPCs of gaheris 35: the three Arbiters become DOL.GS.MasterLevelArbiter, Demyphon in region 72 a Master Level credit merchant.
-- Built by tools/toa/build_data_packs.py from tools/toa/sql. Do not hand-edit:
-- the server applies a pack once per save, so a fix ships as a new pack.
-- Applied by the server in one transaction (docs/TOA_DATA_PACKS.md).

-- ======== part: 024-toa-arbiter-npcs.sql ========

-- Trials of Atlantis, TOA_PLAN pack set 2 (docs/TOA_DATA_PACKS.md). Hand-written, SQLite.
--
-- The subset of gaheris sql/35-atlantis-npcs.sql that makes the Master Level path work:
-- the Arbiter enrols, Demyphon sells the Master Level credits. Both rows come from pack
-- 020 (gaheris file 13). Sections 2 and 3 of file 35 (Aphaestia as
-- ArtifactCreditMerchant, Zosyne and Mnosus as scroll merchants) do not ship here: the
-- credit merchant class does not exist yet (WP3.1b), and pack 023 already wires the
-- scroll binders.
--
-- 1. The three Arbiters (regions 70, 71, 72) are DOL.GS.Arbiter in file 13, which only
--    speaks lore. DOL.GS.MasterLevelArbiter (gameobjects/Atlantis/MasterLevelArbiter.cs)
--    enrols the speaker and the group members nearby. gaheris writes its own class name
--    DOL.GS.Scripts.GaherisArbiter; our class of that name is an alias, ours is used here.
UPDATE Mob
   SET ClassType = 'DOL.GS.MasterLevelArbiter'
 WHERE Name = 'Arbiter'
   AND ClassType = 'DOL.GS.Arbiter'
   AND Region IN (70, 71, 72);

-- 2. Demyphon in region 72 is a bare GameAtlanteanGlassMerchant without a list in file 13.
--    His rows in 70 and 71 already carry DOL.GS.Scripts.MasterLevelsMerchant and the list
--    'Master Level Credits' (11 rows, in the world database). Same as gaheris 35 section 4.
UPDATE Mob
   SET ClassType = 'DOL.GS.Scripts.MasterLevelsMerchant',
       ItemsListTemplateID = 'Master Level Credits'
 WHERE Name = 'Demyphon'
   AND Region = 72
   AND (ItemsListTemplateID IS NULL OR ItemsListTemplateID = '');
