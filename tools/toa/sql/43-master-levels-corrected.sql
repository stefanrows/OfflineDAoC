-- Generated from gaheris sql/43-master-levels-corrected.sql by tools/toa/convert_mysql_to_sqlite.py
-- (converter v1, uuid mode literal, rules round all). Do not hand-edit; rerun the converter.
-- Source sha256: e344250f807ff68b31677a2370ee8087ec21b9c180d5b3f1cdf3430a122996e5
-- Dialect: SQLite 3.35+ (UPSERT without a conflict target). Run inside one
-- transaction; MySQL session statements below are comments only.
-- Statements: 12; inserts: 1 (9 VALUES rows);
-- upserts: 0 DO UPDATE, 0 DO NOTHING; UUID literals: 9.
-- Dropped column linexspell.PackageID in 1 statement(s): our LineXSpell has no PackageID column; gaheris uses it only as a row tag

-- ===========================================================================
--  43-master-levels-corrected.sql
--
--  Migration 42 put the right abilities at the wrong ranks.
--
--  It worked from the handler names OpenDAoC implements, which told it what
--  was possible but not where anything belonged, so eight of its nine
--  guesses were misplaced or misnamed. The canonical lists settle it -- the
--  per-path pages at camelot.allakhazam.com/masterlevels, which give all ten
--  ranks for each path -- and this puts every one where it goes.
--
--      Battlemaster  1 Sapping Strike    was Essence Dampen at 1
--                    3 Power Leak        was Mana Drain
--                    5 Essence Flames    was Endurance Drain at 5, Flames at 7
--                    7 Essence Sear      was at 9
--                    8 Bodyguard         correct already
--                    9 Essence Dampen    was at 1
--
--      Spymaster     4 Sabotage          was at 3
--                    7 Lookout           was at 1
--
--      Sojourner     7 Resistance of the Ancients   correct already
--
--  The types were right throughout -- MLEndudrain really is the endurance
--  drain, BodyguardHandler really is Bodyguard -- so nothing here changes what
--  the abilities do, only which rank teaches them and what they are called.
--  Getting that wrong is the quiet kind of wrong: the path works, and every
--  ability arrives one or two ranks from where a player expects it.
--
--  Seven gaps remain and all seven are now identified by name, which is worth
--  more than the guesses were:
--
--      Battlemaster 10   Essence Shatter        removes a random buff
--      Spymaster     1   Pickpocket             passive bonus PvE coin
--      Spymaster     3   Enduring Poison        poison survives a resist
--      Sojourner     1   Unburdened Warrior     passive, +30% encumbrance
--      Sojourner     3   Reveal Crystalseed     reveals enemy runes
--      Perfecter     4   Greatness              +20% concentration pool
--      Warlord       1   Siege Master           -30% siege timers
--
--  None of them has a handler in OpenDAoC, and no amount of database work
--  changes that -- each needs code before a row is worth writing. Perfecter's
--  unclaimed CCResist and PowerOverTime turn out to be the sub-effects of
--  Determination Ward and Dissonating Ward rather than abilities of their own,
--  so they are not the missing rank 4 and were rightly left alone.
--
--  db-public does hold Reveal Crystalseed as spell 7204 with an empty Type
--  column, which is why migration 12 skipped it. It stays skipped: without a
--  handler it would be an ability that costs a rank and does nothing, which is
--  worse than an empty rank.
--
--  Read at boot: needs a restart. Safe to re-run.
-- ===========================================================================

-- [MySQL session statement, not run] SET NAMES utf8mb4;
-- [MySQL session statement, not run] SET SESSION sql_mode='';

-- ---------------------------------------------------------------------------
-- Names, to match the canonical lists
-- ---------------------------------------------------------------------------
UPDATE "spell" SET "Name" = 'Sapping Strike',
       "Description" = 'Reduces the target''s endurance by half.'
 WHERE "SpellID" = 7322;

UPDATE "spell" SET "Name" = 'Power Leak',
       "Description" = 'Draws the power out of the target.'
 WHERE "SpellID" = 7321;

UPDATE "spell" SET "Name" = 'Essence Flames',
       "Description" = 'Drains a third of the target''s power, and burns those around it.'
 WHERE "SpellID" = 7323;

UPDATE "spell" SET "Name" = 'Essence Sear',
       "Description" = 'Chains from Essence Flames, searing the essence of those nearby.'
 WHERE "SpellID" = 7325;

UPDATE "spell" SET "Name" = 'Bodyguard',
       "Description" = 'The one you guard cannot be struck in melee while you stand.'
 WHERE "SpellID" = 7324;

UPDATE "spell" SET "Name" = 'Essence Dampen',
       "Description" = 'Suppresses the strength and constitution granted to those nearby.'
 WHERE "SpellID" = 7320;

UPDATE "spell" SET "Name" = 'Sabotage',
       "Description" = 'Ruins an enemy ward where it stands.'
 WHERE "SpellID" = 7327;

UPDATE "spell" SET "Name" = 'Lookout',
       "Description" = 'Guards an ally and reveals what is hidden near them.'
 WHERE "SpellID" = 7326;

-- ---------------------------------------------------------------------------
-- Ranks, to match the canonical lists
-- ---------------------------------------------------------------------------
DELETE FROM "linexspell"
 WHERE "SpellID" IN (7320, 7321, 7322, 7323, 7324, 7325, 7326, 7327, 7328);

INSERT INTO "linexspell" ("LineXSpell_ID", "LineName", "SpellID", "Level", "LastTimeRowUpdated") VALUES
  ('1a55f4ea-9a29-51f8-8f16-a93a730a5a7c', 'Battlemaster', 7322,  1, '2000-01-01 00:00:00'),
  ('f5bad6b3-d0e6-5972-85b7-db53bbf6698c', 'Battlemaster', 7321,  3, '2000-01-01 00:00:00'),
  ('f875a8c6-94b7-579c-8460-fc818543e60f', 'Battlemaster', 7323,  5, '2000-01-01 00:00:00'),
  ('6eebd2b3-96d2-5ba7-988c-4a7edc4e713d', 'Battlemaster', 7325,  7, '2000-01-01 00:00:00'),
  ('58189906-9948-5439-a554-f82f8b16dcc5', 'Battlemaster', 7324,  8, '2000-01-01 00:00:00'),
  ('0dda95d7-28fc-559e-84e7-a12733a3fef3', 'Battlemaster', 7320,  9, '2000-01-01 00:00:00'),
  ('fb2afe3d-fc8a-5ad1-ad9d-704bff5129d0', 'Spymaster',    7327,  4, '2000-01-01 00:00:00'),
  ('12387080-746e-549a-b8be-2dfc3b960f0c', 'Spymaster',    7326,  7, '2000-01-01 00:00:00'),
  ('49b6b4b2-0d30-54c6-81f9-85b957b713f0', 'Sojourner',    7328,  7, '2000-01-01 00:00:00');
