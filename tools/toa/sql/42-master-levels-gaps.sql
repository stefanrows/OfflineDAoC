-- Generated from gaheris sql/42-master-levels-gaps.sql by tools/toa/convert_mysql_to_sqlite.py
-- (converter v1, uuid mode literal, rules round all). Do not hand-edit; rerun the converter.
-- Source sha256: 7dc6fefbe26a20e6452c155ab868d5601b20e14c1bcf2da67a7b90f6b0bdc35a
-- Dialect: SQLite 3.35+ (UPSERT without a conflict target). Run inside one
-- transaction; MySQL session statements below are comments only.
-- Statements: 29; inserts: 18 (18 VALUES rows);
-- upserts: 9 DO UPDATE, 0 DO NOTHING; UUID literals: 9.
-- Dropped column linexspell.PackageID in 9 statement(s): our LineXSpell has no PackageID column; gaheris uses it only as a row tag

-- ===========================================================================
--  42-master-levels-gaps.sql
--
--  The Master Level abilities migration 12 could not supply.
--
--  That migration took its 64 spells from db-public, the Dawn of Light
--  community database, and db-public is short. Against the 80 a complete set
--  needs -- eight paths, ten ranks each -- we were missing sixteen, and they
--  were not spread evenly: Battlemaster had three of ten, so a player who
--  chose it got an empty path at ranks 1, 3, 5, 7, 8, 9 and 10.
--
--      Battlemaster   missing 1,3,5,7,8,9,10   had 3
--      Spymaster      missing 1,3,4,7          had 6
--      Sojourner      missing 1,3,7            had 7
--      Perfecter      missing 4                had 9
--      Warlord        missing 1                had 9
--
--  Two different faults are behind that.
--
--  Sojourner 7 was in db-public all along -- Resistance of the Ancients, spell
--  7278 -- with an empty Type column, so the generator skipped it. It is
--  EssenceResist: Sojourner.cs has the handler and nothing else in that file
--  claims it. That one is recovered rather than invented.
--
--  The other eight here are written against handlers OpenDAoC already
--  implements and nothing uses. Battlemaster.cs alone carries six the database
--  never names -- BodyguardHandler, EssenceDampenHandler, EssenceFlamesProc,
--  EssenceSearHandler, MLEndudrain, MLManadrain -- which is the code for six
--  ranks sitting finished behind a missing row each. Spymaster.cs has Loockout
--  and Sabotage the same way. The names are the ones those abilities carry in
--  Trials of Atlantis; the numbers are ours, modelled on the ML spells we
--  already hold at comparable ranks, and are meant to be tuned rather than
--  trusted.
--
--  Still missing after this, and left alone rather than invented, because no
--  handler names them and no source has them:
--
--      Battlemaster 10, Spymaster 4 and 7, Perfecter 4, Warlord 1
--
--  Perfecter has CCResist and PowerOverTime unclaimed and Spymaster has
--  PoisonspikeDot, but PoisonspikeDot reads like the damage half of Poison
--  Spike at rank 6 rather than an ability of its own, and guessing which rank
--  the other two belong to would put a player on a path whose abilities are in
--  the wrong order. Better an honest gap.
--
--  Spell ids 7320-7328, checked free against every id in the spell table.
--  Read at boot: needs a restart. Safe to re-run.
-- ===========================================================================

-- [MySQL session statement, not run] SET NAMES utf8mb4;
-- [MySQL session statement, not run] SET SESSION sql_mode='';

INSERT INTO "spell" ("SpellID", "Spell_ID", "Name", "Description", "Type", "Target", "CastTime", "RecastDelay", "Power", "Range", "Radius", "Duration", "Value", "Damage", "Icon", "ClientEffect", "TooltipId", "LastTimeRowUpdated") VALUES
  (7320, 'ML_Battlemaster_1', 'Essence Dampen', 'Dampens the target''s essence, blunting the force of its blows.', 'EssenceDampenHandler', 'Enemy', 0, 60, 0, 350, 0, 30, 20, 0, 7320, 7320, 7320, '2000-01-01 00:00:00')
  ON CONFLICT DO UPDATE SET "Name"=excluded."Name", "Description"=excluded."Description", "Type"=excluded."Type", "Target"=excluded."Target", "CastTime"=excluded."CastTime", "RecastDelay"=excluded."RecastDelay", "Power"=excluded."Power", "Range"=excluded."Range", "Radius"=excluded."Radius", "Duration"=excluded."Duration", "Value"=excluded."Value", "Damage"=excluded."Damage";
INSERT INTO "spell" ("SpellID", "Spell_ID", "Name", "Description", "Type", "Target", "CastTime", "RecastDelay", "Power", "Range", "Radius", "Duration", "Value", "Damage", "Icon", "ClientEffect", "TooltipId", "LastTimeRowUpdated") VALUES
  (7321, 'ML_Battlemaster_3', 'Mana Drain', 'Draws power out of the target and into the caster.', 'MLManadrain', 'Enemy', 0, 60, 0, 350, 0, 0, 0, 100, 7321, 7321, 7321, '2000-01-01 00:00:00')
  ON CONFLICT DO UPDATE SET "Name"=excluded."Name", "Description"=excluded."Description", "Type"=excluded."Type", "Target"=excluded."Target", "CastTime"=excluded."CastTime", "RecastDelay"=excluded."RecastDelay", "Power"=excluded."Power", "Range"=excluded."Range", "Radius"=excluded."Radius", "Duration"=excluded."Duration", "Value"=excluded."Value", "Damage"=excluded."Damage";
INSERT INTO "spell" ("SpellID", "Spell_ID", "Name", "Description", "Type", "Target", "CastTime", "RecastDelay", "Power", "Range", "Radius", "Duration", "Value", "Damage", "Icon", "ClientEffect", "TooltipId", "LastTimeRowUpdated") VALUES
  (7322, 'ML_Battlemaster_5', 'Endurance Drain', 'Draws the endurance out of the target and into the caster.', 'MLEndudrain', 'Enemy', 0, 60, 0, 350, 0, 0, 0, 100, 7322, 7322, 7322, '2000-01-01 00:00:00')
  ON CONFLICT DO UPDATE SET "Name"=excluded."Name", "Description"=excluded."Description", "Type"=excluded."Type", "Target"=excluded."Target", "CastTime"=excluded."CastTime", "RecastDelay"=excluded."RecastDelay", "Power"=excluded."Power", "Range"=excluded."Range", "Radius"=excluded."Radius", "Duration"=excluded."Duration", "Value"=excluded."Value", "Damage"=excluded."Damage";
INSERT INTO "spell" ("SpellID", "Spell_ID", "Name", "Description", "Type", "Target", "CastTime", "RecastDelay", "Power", "Range", "Radius", "Duration", "Value", "Damage", "Icon", "ClientEffect", "TooltipId", "LastTimeRowUpdated") VALUES
  (7323, 'ML_Battlemaster_7', 'Essence Flames', 'Wreathes the user in essence fire, which burns those who strike them.', 'EssenceFlamesProc', 'Self', 0, 300, 0, 350, 0, 30, 20, 0, 7323, 7323, 7323, '2000-01-01 00:00:00')
  ON CONFLICT DO UPDATE SET "Name"=excluded."Name", "Description"=excluded."Description", "Type"=excluded."Type", "Target"=excluded."Target", "CastTime"=excluded."CastTime", "RecastDelay"=excluded."RecastDelay", "Power"=excluded."Power", "Range"=excluded."Range", "Radius"=excluded."Radius", "Duration"=excluded."Duration", "Value"=excluded."Value", "Damage"=excluded."Damage";
INSERT INTO "spell" ("SpellID", "Spell_ID", "Name", "Description", "Type", "Target", "CastTime", "RecastDelay", "Power", "Range", "Radius", "Duration", "Value", "Damage", "Icon", "ClientEffect", "TooltipId", "LastTimeRowUpdated") VALUES
  (7324, 'ML_Battlemaster_8', 'Bodyguard', 'Places the user between an ally and harm, taking the blows meant for them.', 'BodyguardHandler', 'Realm', 0, 60, 0, 1000, 0, 30, 0, 0, 7324, 7324, 7324, '2000-01-01 00:00:00')
  ON CONFLICT DO UPDATE SET "Name"=excluded."Name", "Description"=excluded."Description", "Type"=excluded."Type", "Target"=excluded."Target", "CastTime"=excluded."CastTime", "RecastDelay"=excluded."RecastDelay", "Power"=excluded."Power", "Range"=excluded."Range", "Radius"=excluded."Radius", "Duration"=excluded."Duration", "Value"=excluded."Value", "Damage"=excluded."Damage";
INSERT INTO "spell" ("SpellID", "Spell_ID", "Name", "Description", "Type", "Target", "CastTime", "RecastDelay", "Power", "Range", "Radius", "Duration", "Value", "Damage", "Icon", "ClientEffect", "TooltipId", "LastTimeRowUpdated") VALUES
  (7325, 'ML_Battlemaster_9', 'Essence Sear', 'Sears the essence of the target, weakening what holds it together.', 'EssenceSearHandler', 'Enemy', 0, 60, 0, 350, 0, 30, 20, 0, 7325, 7325, 7325, '2000-01-01 00:00:00')
  ON CONFLICT DO UPDATE SET "Name"=excluded."Name", "Description"=excluded."Description", "Type"=excluded."Type", "Target"=excluded."Target", "CastTime"=excluded."CastTime", "RecastDelay"=excluded."RecastDelay", "Power"=excluded."Power", "Range"=excluded."Range", "Radius"=excluded."Radius", "Duration"=excluded."Duration", "Value"=excluded."Value", "Damage"=excluded."Damage";
INSERT INTO "spell" ("SpellID", "Spell_ID", "Name", "Description", "Type", "Target", "CastTime", "RecastDelay", "Power", "Range", "Radius", "Duration", "Value", "Damage", "Icon", "ClientEffect", "TooltipId", "LastTimeRowUpdated") VALUES
  (7326, 'ML_Spymaster_1', 'Lookout', 'Sharpens the user''s sight, revealing what is hidden nearby.', 'Loockout', 'Self', 0, 60, 0, 0, 0, 60, 0, 0, 7326, 7326, 7326, '2000-01-01 00:00:00')
  ON CONFLICT DO UPDATE SET "Name"=excluded."Name", "Description"=excluded."Description", "Type"=excluded."Type", "Target"=excluded."Target", "CastTime"=excluded."CastTime", "RecastDelay"=excluded."RecastDelay", "Power"=excluded."Power", "Range"=excluded."Range", "Radius"=excluded."Radius", "Duration"=excluded."Duration", "Value"=excluded."Value", "Damage"=excluded."Damage";
INSERT INTO "spell" ("SpellID", "Spell_ID", "Name", "Description", "Type", "Target", "CastTime", "RecastDelay", "Power", "Range", "Radius", "Duration", "Value", "Damage", "Icon", "ClientEffect", "TooltipId", "LastTimeRowUpdated") VALUES
  (7327, 'ML_Spymaster_3', 'Sabotage', 'Ruins an enemy''s siege equipment where it stands.', 'Sabotage', 'Enemy', 0, 300, 0, 1500, 0, 0, 0, 0, 7327, 7327, 7327, '2000-01-01 00:00:00')
  ON CONFLICT DO UPDATE SET "Name"=excluded."Name", "Description"=excluded."Description", "Type"=excluded."Type", "Target"=excluded."Target", "CastTime"=excluded."CastTime", "RecastDelay"=excluded."RecastDelay", "Power"=excluded."Power", "Range"=excluded."Range", "Radius"=excluded."Radius", "Duration"=excluded."Duration", "Value"=excluded."Value", "Damage"=excluded."Damage";
INSERT INTO "spell" ("SpellID", "Spell_ID", "Name", "Description", "Type", "Target", "CastTime", "RecastDelay", "Power", "Range", "Radius", "Duration", "Value", "Damage", "Icon", "ClientEffect", "TooltipId", "LastTimeRowUpdated") VALUES
  (7328, 'ML_Sojourner_7', 'Resistance of the Ancients', 'Wraps the user''s allies in the resilience of Atlantis.', 'EssenceResist', 'Realm', 0, 300, 0, 0, 750, 30, 15, 0, 7328, 7328, 7328, '2000-01-01 00:00:00')
  ON CONFLICT DO UPDATE SET "Name"=excluded."Name", "Description"=excluded."Description", "Type"=excluded."Type", "Target"=excluded."Target", "CastTime"=excluded."CastTime", "RecastDelay"=excluded."RecastDelay", "Power"=excluded."Power", "Range"=excluded."Range", "Radius"=excluded."Radius", "Duration"=excluded."Duration", "Value"=excluded."Value", "Damage"=excluded."Damage";

-- The line entries. Level here is the Master Level, 1 to 10.

DELETE FROM "linexspell" WHERE "LineName" = 'Battlemaster' AND "Level" = 1;
INSERT INTO "linexspell" ("LineXSpell_ID", "LineName", "SpellID", "Level", "LastTimeRowUpdated") VALUES
  ('1e3ca3a6-b813-5ac9-814a-60564772a305', 'Battlemaster', 7320, 1, '2000-01-01 00:00:00');
DELETE FROM "linexspell" WHERE "LineName" = 'Battlemaster' AND "Level" = 3;
INSERT INTO "linexspell" ("LineXSpell_ID", "LineName", "SpellID", "Level", "LastTimeRowUpdated") VALUES
  ('445c8bc7-b8b8-57b5-8563-bb1dbcaba907', 'Battlemaster', 7321, 3, '2000-01-01 00:00:00');
DELETE FROM "linexspell" WHERE "LineName" = 'Battlemaster' AND "Level" = 5;
INSERT INTO "linexspell" ("LineXSpell_ID", "LineName", "SpellID", "Level", "LastTimeRowUpdated") VALUES
  ('0e88fea8-ed82-50ee-9a1f-b137ad193fc0', 'Battlemaster', 7322, 5, '2000-01-01 00:00:00');
DELETE FROM "linexspell" WHERE "LineName" = 'Battlemaster' AND "Level" = 7;
INSERT INTO "linexspell" ("LineXSpell_ID", "LineName", "SpellID", "Level", "LastTimeRowUpdated") VALUES
  ('50ed5246-c3ff-51f2-bba5-d43bb4c16caa', 'Battlemaster', 7323, 7, '2000-01-01 00:00:00');
DELETE FROM "linexspell" WHERE "LineName" = 'Battlemaster' AND "Level" = 8;
INSERT INTO "linexspell" ("LineXSpell_ID", "LineName", "SpellID", "Level", "LastTimeRowUpdated") VALUES
  ('5e5ef589-ed89-5280-8ea8-a26b82826d7f', 'Battlemaster', 7324, 8, '2000-01-01 00:00:00');
DELETE FROM "linexspell" WHERE "LineName" = 'Battlemaster' AND "Level" = 9;
INSERT INTO "linexspell" ("LineXSpell_ID", "LineName", "SpellID", "Level", "LastTimeRowUpdated") VALUES
  ('ab6b458a-8eb5-55cf-9fc9-6cafc2a36e4b', 'Battlemaster', 7325, 9, '2000-01-01 00:00:00');
DELETE FROM "linexspell" WHERE "LineName" = 'Spymaster' AND "Level" = 1;
INSERT INTO "linexspell" ("LineXSpell_ID", "LineName", "SpellID", "Level", "LastTimeRowUpdated") VALUES
  ('b18a623a-0520-51d2-8b67-a04f62dcfcf4', 'Spymaster', 7326, 1, '2000-01-01 00:00:00');
DELETE FROM "linexspell" WHERE "LineName" = 'Spymaster' AND "Level" = 3;
INSERT INTO "linexspell" ("LineXSpell_ID", "LineName", "SpellID", "Level", "LastTimeRowUpdated") VALUES
  ('05cb590a-d698-5416-b592-f4229c1c2fae', 'Spymaster', 7327, 3, '2000-01-01 00:00:00');
DELETE FROM "linexspell" WHERE "LineName" = 'Sojourner' AND "Level" = 7;
INSERT INTO "linexspell" ("LineXSpell_ID", "LineName", "SpellID", "Level", "LastTimeRowUpdated") VALUES
  ('30600e8e-49a1-55b4-90bf-a23e209e8f9c', 'Sojourner', 7328, 7, '2000-01-01 00:00:00');
