-- World data pack 041-toa-loot-caps
-- Caps the by-name loot template of The Phoenix at one random item per kill.
-- Built by tools/toa/build_data_packs.py from tools/toa/sql. Do not hand-edit:
-- the server applies a pack once per save, so a fix ships as a new pack.
-- Applied by the server in one transaction (docs/TOA_DATA_PACKS.md).

-- ======== part: 041-toa-loot-caps.sql ========

-- Trials of Atlantis, TOA_PLAN pack set 2 (docs/TOA_DATA_PACKS.md). Hand-written, SQLite.
--
-- Caps the loot of The Phoenix (pack 020, regions 47, 90 and 147, NPC template
-- 60166990) at one random item per kill. Pack 040 gives its by-name loot template
-- 'the phoenix' 23 items at 15 % each and no MobXLootTemplate link
-- (docs/TOA_DUNGEON_IMPORT.md, risk 3).
--
-- LootGeneratorTemplate looks the link up by the lower-case mob name, then reads the
-- template named by MobName. A link has DropCount 1 here. Note: the generator's
-- default LootList already picks one random winner per kill, so this row pins the cap
-- rather than changing today's drop rate; it holds if the default ever changes.
--
-- Inserted only where the pair is missing. Idempotent.
INSERT INTO MobXLootTemplate (MobXLootTemplate_ID, MobName, LootTemplateName, DropCount, LastTimeRowUpdated)
SELECT 'db0db8e0-c46c-5c63-bf25-dcb8ecec19ef', 'the phoenix', 'the phoenix', 1, '2000-01-01 00:00:00'
 WHERE NOT EXISTS (SELECT 1 FROM MobXLootTemplate
                    WHERE lower(MobName) = 'the phoenix' AND lower(LootTemplateName) = 'the phoenix')
ON CONFLICT DO NOTHING;
