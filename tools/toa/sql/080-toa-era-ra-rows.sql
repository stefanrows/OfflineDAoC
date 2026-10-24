-- Trials of Atlantis, WP2.7 (docs/TOA_PLAN.md, docs/TOA_RESEARCH.md section
-- "ToA-era realm abilities (WP2.7, 2026-10-24)"). Hand-written, SQLite.
--
-- Realm patch 1.68 (Herald article 500) opened three more class-access
-- realm abilities. Our active set, ClassXRealmAbility_Atlas (ability keys
-- AtlasOF_*), lacks them. Four rows, no code change: the Ability rows and the
-- handlers exist already (DOL.GS.RealmAbilities.AtlasOF_MasteryOfConcentration,
-- AtlasOF_WildPowerAbility, AtlasOF_MasteryOfMagery) and 17, 13 and 13 other
-- classes already own them.
--
--   Bard   (class 48) gets Mastery of Concentration
--   Cleric (class  6) gets Wild Power
--   Thane  (class 21) gets Mastery of Magery and Wild Power
--
-- Wild Power on a class that is not a list caster adds its percentage to the
-- spell critical chance without the list caster's base chance (1.68), which is
-- what CriticalSpellHitChanceCalculator already does. The handlers keep their
-- prerequisites: Augmented Acuity level 2 for Wild Power and Mastery of Magery,
-- level 3 for Mastery of Concentration.
--
-- Majestic Will (5 levels, cost 1/3/6/10/14, 10 minute timer) is not part of
-- this pack: the owner has not yet chosen between the 1.65 baseline and the
-- 1.68 rework.
--
-- SkillBase.LoadClassRealmAbilities reads the table at every boot, after the
-- data packs, so the rows act at the next start. A character or companion
-- keeps the realm ability points it has; the abilities show up as extra
-- purchases. Inserted only where the (class, ability) pair is missing, with
-- fixed row IDs. Idempotent: a second run changes nothing.
INSERT INTO ClassXRealmAbility_Atlas (CharClass, AbilityKey, LastTimeRowUpdated, ClassXRealmAbility_Atlas_ID)
SELECT 48, 'AtlasOF_MasteryOfConcentration', '2000-01-01 00:00:00', 'Bard1-toa-MasteryOfConcentration'
 WHERE NOT EXISTS (SELECT 1 FROM ClassXRealmAbility_Atlas
                    WHERE CharClass = 48 AND AbilityKey = 'AtlasOF_MasteryOfConcentration')
ON CONFLICT DO NOTHING;

INSERT INTO ClassXRealmAbility_Atlas (CharClass, AbilityKey, LastTimeRowUpdated, ClassXRealmAbility_Atlas_ID)
SELECT 6, 'AtlasOF_WildPower', '2000-01-01 00:00:00', 'Cleric1-toa-WildPower'
 WHERE NOT EXISTS (SELECT 1 FROM ClassXRealmAbility_Atlas
                    WHERE CharClass = 6 AND AbilityKey = 'AtlasOF_WildPower')
ON CONFLICT DO NOTHING;

INSERT INTO ClassXRealmAbility_Atlas (CharClass, AbilityKey, LastTimeRowUpdated, ClassXRealmAbility_Atlas_ID)
SELECT 21, 'AtlasOF_MasteryOfMagery', '2000-01-01 00:00:00', 'Thane1-toa-MasteryOfMagery'
 WHERE NOT EXISTS (SELECT 1 FROM ClassXRealmAbility_Atlas
                    WHERE CharClass = 21 AND AbilityKey = 'AtlasOF_MasteryOfMagery')
ON CONFLICT DO NOTHING;

INSERT INTO ClassXRealmAbility_Atlas (CharClass, AbilityKey, LastTimeRowUpdated, ClassXRealmAbility_Atlas_ID)
SELECT 21, 'AtlasOF_WildPower', '2000-01-01 00:00:00', 'Thane1-toa-WildPower'
 WHERE NOT EXISTS (SELECT 1 FROM ClassXRealmAbility_Atlas
                    WHERE CharClass = 21 AND AbilityKey = 'AtlasOF_WildPower')
ON CONFLICT DO NOTHING;
