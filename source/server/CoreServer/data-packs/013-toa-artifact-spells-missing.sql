-- World data pack 013-toa-artifact-spells-missing
-- The four artifact spells that exist nowhere, written from the live delve texts: 13135 Stat Drain (Scepter of the Meritorious), 30115 Boon of Kings (Spear of Kings), 30125 Disoriented (Shield of Khaos), 32223 Vision of Malice (Malice's Axe). Inserted only where the SpellID is missing.
-- Built by tools/toa/build_data_packs.py from tools/toa/sql. Do not hand-edit:
-- the server applies a pack once per save, so a fix ships as a new pack.
-- Applied by the server in one transaction (docs/TOA_DATA_PACKS.md).

-- ======== part: 013-toa-artifact-spells-missing.sql ========

-- Trials of Atlantis, WP3.0b (docs/TOA_ARTIFACT_SPELLS.md, docs/TOA_DATA_PACKS.md).
-- Hand-written, SQLite.
--
-- The four spells our artifact item templates reference that exist nowhere: not in our save,
-- not in gaheris (its 13135 is the Vampiir spell "Wither Defense" in a line we do not ship),
-- not in DOL or OpenDAoC. Built from the live delve texts. Each row is inserted only when its
-- SpellID is missing, so a save that already has one keeps it. Idempotent.

-- 13135: Scepter of the Meritorious (crush and hammer), second proc (ProcSpellID1). Live the
-- Scepter's proc is Stat Drain, -25 strength and constitution moved to the wielder for one
-- minute at 1000 range (Camelot Herald item database, Scepter of the Meritorious). Same as
-- 30114 "Scepter's Curse", the proc of the blunt Scepter.
INSERT INTO "spell" ("Spell_ID","SpellID","ClientEffect","Icon","Name","Description","Target","Range","Power","CastTime","Damage","DamageType","Type","Duration","Frequency","Pulse","PulsePower","Radius","RecastDelay","ResurrectHealth","ResurrectMana","Value","Concentration","LifeDrainReturn","AmnesiaChance","Message1","Message2","Message3","Message4","InstrumentRequirement","SpellGroup","EffectGroup","SubSpellID","MoveCast","Uninterruptible","IsPrimary","IsSecondary","AllowBolt","SharedTimerGroup","PackageID","IsFocus","TooltipId","LastTimeRowUpdated")
SELECT '9273b556-cd92-5d05-ac03-2d4ee4b2af5b', 13135, 4385, 4385, 'Stat Drain', 'Decreases the target''s listed stat(s), and transfers them to the caster.
Str/Con decrease: 25', 'Enemy', 1000, 0, 0, 0, 10, 'StrengthConstitutionDrain', 60, 0, 0, 0, 0, 0, 0, 0, 25, 0, 0, 0, '', '', '', '', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 'Artifacts', 0, 13135, '2000-01-01 00:00:00'
 WHERE NOT EXISTS (SELECT 1 FROM "spell" WHERE "SpellID" = 13135)
ON CONFLICT DO NOTHING;

-- 30115: Spear of Kings (17 templates) and Scepter of the Meritorious (blunt), level 10 use.
-- Live Spear of Kings "Boon of Kings": realm, radius 1500, 5 seconds pulsing every 4.5
-- seconds, armor and health +5% (Camelot Herald item database). The Scepter's own Boon of
-- Kings was self only, +4%, 10 minutes; one row serves both, the Spear's version wins.
-- Value -5 is a percentage (AfHitsBuffSpellHandler, type AFHitsBuff).
INSERT INTO "spell" ("Spell_ID","SpellID","ClientEffect","Icon","Name","Description","Target","Range","Power","CastTime","Damage","DamageType","Type","Duration","Frequency","Pulse","PulsePower","Radius","RecastDelay","ResurrectHealth","ResurrectMana","Value","Concentration","LifeDrainReturn","AmnesiaChance","Message1","Message2","Message3","Message4","InstrumentRequirement","SpellGroup","EffectGroup","SubSpellID","MoveCast","Uninterruptible","IsPrimary","IsSecondary","AllowBolt","SharedTimerGroup","PackageID","IsFocus","TooltipId","LastTimeRowUpdated")
SELECT 'ce73b1e0-dda1-5276-9c7c-3a83f8e47626', 30115, 12035, 12035, 'Boon of Kings', 'Improves the armor and health of realm mates within range of this aura by 5%.', 'Realm', 0, 0, 0, 0, 0, 'AFHitsBuff', 5, 45, 1, 0, 1500, 0, 0, 0, -5, 0, 0, 0, '', '', '', '', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 'Artifacts', 0, 30115, '2000-01-01 00:00:00'
 WHERE NOT EXISTS (SELECT 1 FROM "spell" WHERE "SpellID" = 30115)
ON CONFLICT DO NOTHING;

-- 30125: Shield of Khaos, level 10 use. Live "Disoriented": 1500 range, radius 700, one
-- minute, melee to-hit -10% and more susceptible to spells, body; it also casts an energy
-- damage over time (20 per tick, radius 700), our 30126 (Camelot Herald item database).
-- Type HarpyFeatherCloak is the artifact to-hit debuff; the spell-hit part has no core reader.
INSERT INTO "spell" ("Spell_ID","SpellID","ClientEffect","Icon","Name","Description","Target","Range","Power","CastTime","Damage","DamageType","Type","Duration","Frequency","Pulse","PulsePower","Radius","RecastDelay","ResurrectHealth","ResurrectMana","Value","Concentration","LifeDrainReturn","AmnesiaChance","Message1","Message2","Message3","Message4","InstrumentRequirement","SpellGroup","EffectGroup","SubSpellID","MoveCast","Uninterruptible","IsPrimary","IsSecondary","AllowBolt","SharedTimerGroup","PackageID","IsFocus","TooltipId","LastTimeRowUpdated")
SELECT '7e0832f1-71f8-5d97-9f6d-ca72bed2e4ad', 30125, 5353, 5353, 'Disoriented', 'Target is disoriented, reducing their chance to hit in melee by 10% and making them more susceptible to spells.', 'Enemy', 1500, 0, 0, 0, 10, 'HarpyFeatherCloak', 60, 0, 0, 0, 700, 900, 0, 0, 10, 0, 0, 0, '', '', '', '', 0, 0, 0, 30126, 0, 0, 0, 0, 0, 0, 'Artifacts', 0, 30125, '2000-01-01 00:00:00'
 WHERE NOT EXISTS (SELECT 1 FROM "spell" WHERE "SpellID" = 30125)
ON CONFLICT DO NOTHING;

-- 32223: Malice's Axe (13 templates), level 5 proc; Scepter of the Meritorious (crush and hammer)
-- name it too. Live "Vision of Malice": 350 range, body, all stats of the target reduced.
-- Until patch 1.87 (2007) it was 25% for one minute, later 10% for 30 seconds (Camelot
-- Herald 1.87 notes); the ToA-era 25%/60 s ships here.
INSERT INTO "spell" ("Spell_ID","SpellID","ClientEffect","Icon","Name","Description","Target","Range","Power","CastTime","Damage","DamageType","Type","Duration","Frequency","Pulse","PulsePower","Radius","RecastDelay","ResurrectHealth","ResurrectMana","Value","Concentration","LifeDrainReturn","AmnesiaChance","Message1","Message2","Message3","Message4","InstrumentRequirement","SpellGroup","EffectGroup","SubSpellID","MoveCast","Uninterruptible","IsPrimary","IsSecondary","AllowBolt","SharedTimerGroup","PackageID","IsFocus","TooltipId","LastTimeRowUpdated")
SELECT '7ccd80ae-2d56-50da-a415-0d96db203cbd', 32223, 9103, 9103, 'Vision of Malice', 'Reduces all of the target''s stats by 25% of their current value.', 'Enemy', 350, 0, 0, 0, 10, 'AllStatsPercentDebuff', 60, 0, 0, 0, 0, 0, 0, 0, 25, 0, 0, 0, '', '', '', '', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 'Artifacts', 0, 32223, '2000-01-01 00:00:00'
 WHERE NOT EXISTS (SELECT 1 FROM "spell" WHERE "SpellID" = 32223)
ON CONFLICT DO NOTHING;
