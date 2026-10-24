-- World data pack 012-toa-artifact-spells
-- Artifact spells from gaheris 90, filtered: the 68 spell IDs our artifact items reference and lack, plus the 5 spells they chain to. 5 referenced IDs exist nowhere.
-- Built by tools/toa/build_data_packs.py from tools/toa/sql. Do not hand-edit:
-- the server applies a pack once per save, so a fix ships as a new pack.
-- Applied by the server in one transaction (docs/TOA_DATA_PACKS.md).

-- ======== part: 90-spells.sql ========

-- Generated from gaheris sql/90-spells.sql by tools/toa/convert_mysql_to_sqlite.py
-- (converter v1, uuid mode literal, rules round all). Do not hand-edit; rerun the converter.
-- Source sha256: fb23628ac5729934275ac41cb70bc8469336c954697ddf04d0b94501f12ca075
-- Dialect: SQLite 3.35+ (UPSERT without a conflict target). Run inside one
-- transaction; MySQL session statements below are comments only.
-- Statements: 2; inserts: 1 (1886 VALUES rows);
-- upserts: 0 DO UPDATE, 0 DO NOTHING; UUID literals: 1886.

-- The spells those lines are made of.
--
-- Part 2 of the expansion-class sweep. SpellID is kept exactly as the
-- reference has it -- linexspell, stylexspell and the ability handlers all
-- reach for spells by that number, so renumbering here would break every one
-- of them. Only Spell_ID, the row's own text key, is reissued.
--
-- The DELETE below was added on 5 September 2026, after installing this onto a
-- database built from the current OpenDAoC seed rather than onto one that had
-- grown up alongside these migrations. Two faults showed at once:
--
--   Upstream now ships a spell at SpellID 15001, "Mind Agony", which this file
--   also uses -- for Accursed Scourge. A plain INSERT collided, and because
--   install.sh runs under `set -e` the whole install stopped there. Migrations
--   91 and 92 and everything after them never ran.
--
--   And a plain INSERT was never re-runnable in the first place. Every one of
--   these 1886 rows would collide on a second pass, so "safe to re-run" was
--   not true of this file.
--
-- Deleting by SpellID first fixes both: the rows this file owns are replaced
-- rather than duplicated, and ours wins the one clash with upstream. That is
-- the right way round here -- nothing in either database casts Mind Agony,
-- while Accursed Scourge is referenced by a spell line.

-- Filtered by tools/toa/build_data_packs.py: 73 of 1886 rows (ARTIFACT_SPELL_IDS).
-- The rest of the sweep is not artifact data and does not ship. None of these
-- SpellIDs exist in the save before this pack, so the DELETE removes nothing of ours.

DELETE FROM "spell" WHERE "SpellID" IN (25618,25619,29996,30100,30101,30102,30103,30109,30110,30111,30112,30113,30114,30116,30117,30120,30121,30122,30123,30124,30131,30132,30134,30135,30136,30137,30138,30139,30142,30143,30144,30145,30147,30148,30149,30151,30152,30154,30160,30161,30162,30165,30166,30170,30171,30172,30173,30174,30175,30177,30178,30184,30186,30187,30193,30195,30196,30198,30199,30200,30201,30202,30203,30204,30205,30206,32153,32175,32230,32232,40008,61371,61372);

INSERT INTO "spell" ("Spell_ID","SpellID","ClientEffect","Icon","Name","Description","Target","Range","Power","CastTime","Damage","DamageType","Type","Duration","Frequency","Pulse","PulsePower","Radius","RecastDelay","ResurrectHealth","ResurrectMana","Value","Concentration","LifeDrainReturn","AmnesiaChance","Message1","Message2","Message3","Message4","InstrumentRequirement","SpellGroup","EffectGroup","SubSpellID","MoveCast","Uninterruptible","IsPrimary","IsSecondary","AllowBolt","SharedTimerGroup","PackageID","IsFocus","TooltipId","LastTimeRowUpdated") VALUES
('8b7c1b84-0c78-5bed-b7ba-9d622e2ee87a',32230,902,902,'Dexterity and Quickness Debuff','Decreases Dex/Qui for a character.','Enemy',300,0,0,0,10,'DexterityQuicknessDebuff',60,0,0,0,0,0,0,0,56,0,0,0,'','','','',0,0,13835,0,0,0,0,0,0,0,'',0,587,'2000-01-01 00:00:00'),
('e548536f-cc5c-5999-af8e-e25f5464bb6f',29996,8082,8107,'Speed of the Depths','Target can breathe under water.','Self',0,0,0,0,0,'WaterBreathing',1200,0,0,0,0,900,0,0,200,0,0,0,'','','','',0,0,1060,0,0,0,0,0,0,0,'Artifacts',0,2951,'2000-01-01 00:00:00'),
('a792ddaf-951e-57e5-9359-7f55080c0e53',30123,9515,9515,'Golden Flames','Casts an ablative shield and a damage shield on the player.','Self',0,0,0,5,10,'DamageShield',600,0,0,0,0,900,0,0,0,0,0,0,'','','','',0,5,5,0,0,0,0,0,0,0,'Artifacts',0,2953,'2000-01-01 00:00:00'),
('8e39af53-e736-5e29-83b7-e0735c97e628',30109,7287,7287,'Skill of the Gladiators','Target''s chance to strike successfully with melee attacks increased
','Self',0,0,0,0,0,'ToHitBuff',60,0,0,0,0,0,0,0,10,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2957,'2000-01-01 00:00:00'),
('ae4cc8a8-92fb-583f-ac0f-fcfb1a78f7bc',30110,9132,9132,'Dark Sundering','Reduces the target ''s absorption (ABS) factor, resulting in worse protection against melee attacks.','Enemy',0,0,0,0,11,'ArmorAbsorptionDebuff',60,0,0,0,350,0,0,0,19,0,0,0,'','','','',0,0,1013,30198,0,0,0,0,0,0,'Artifacts',0,2958,'2000-01-01 00:00:00'),
('462ad812-70fa-5568-adf5-a7f0a3c06481',30198,11228,11228,'ArmorAbsorbtionBuff','Adds to the target''s absorption (ABS) factor, resulting in better protection against melee attacks.','Self',0,0,0,0,0,'ArmorAbsorptionBuff',60,0,0,0,0,0,0,0,19,0,0,0,'','','','',0,0,0,0,1,1,0,0,0,0,'Artifacts',0,2959,'2000-01-01 00:00:00'),
('dc718fff-dbb9-55fe-a522-d63caa7f2c2c',30122,4227,4227,'Direct Damage','Damage the target','Enemy',2100,0,0,100,13,'DirectDamage',0,0,0,0,0,0,0,0,0,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2960,'2000-01-01 00:00:00'),
('71793fb4-e16f-58da-b3db-d4d1d0a20c7b',30201,983,983,'Mesmerize Feedback','Bladeturn for mesmerize effects. Small chance that spell will instead be turned upon the caster.','Self',0,0,0,0,0,'CeremonialBracerMezz',600,0,0,0,0,900,0,0,0,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2961,'2000-01-01 00:00:00'),
('145c28ee-08c6-5cd5-ae61-595e2b27ac28',30202,984,984,'Stun Feedback','Bladeturn for stuns. Small chance that the spell will instead be turned upon the caster.','Self',0,0,0,0,0,'CeremonialBracerStun',600,0,0,0,0,900,0,0,0,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2962,'2000-01-01 00:00:00'),
('33a4b09f-9fa8-5cd0-b50f-c3e2bcea6e2e',30154,1705,1705,'Conversion','5% of all damage dealt heals your power and endurance instead','Self',0,0,0,0,0,'CrocodileTearsProc',600,10,0,0,0,600,0,0,5,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2965,'2000-01-01 00:00:00'),
('0f608658-8baf-58c3-b0c2-b022a21d00e2',30203,3578,3578,'Crocodile Tears Aura','All pulsing effect will now affect realm','Self',0,0,0,0,0,'TargetModifier',65535,0,0,0,0,0,0,0,3,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2966,'2000-01-01 00:00:00'),
('615767ea-894c-5fa4-85c2-ebcd59369476',30195,3590,3590,'Shape Change (Black Lion - Group)','Transform into a panther','Group',1500,0,0,0,0,'DreamGroupMorph',600,0,0,0,2000,900,0,0,30196,0,104,0,'','','','',0,0,0,30196,0,0,0,0,0,0,'Artifacts',0,2971,'2000-01-01 00:00:00'),
('d544feca-e991-5348-b87a-412b8fbb6121',30196,576,576,'Dream Sphere Aura','Target has a chance to be poisoned while hitting you','Self',0,0,0,0,0,'OffensiveProc',600,50,0,0,0,900,0,0,25618,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2972,'2000-01-01 00:00:00'),
('75a935a2-756e-5b11-ab23-8c7b0cdfb7fe',25618,5556,5556,'Accuracy bonus','10% Effectiveness bonus granted to the group for landing all spells, melee and ranged attacks.','Self',2000,0,0,0,0,'EffectivenessBuff',600,0,0,0,0,0,0,0,10,0,0,0,'Your effectiveness has increased.','{0} effectiveness has increased.','Your effectiveness is back to normal.','{0} effectiveness is back to normal.',0,0,0,25619,0,0,0,0,0,0,'Artifacts',0,5687,'2000-01-01 00:00:00'),
('c251eeac-cac2-5977-9a63-ea3786b5902a',25619,11216,11216,'Dmg-Over-Time','Inflicts damage to the target repeatedly over a given time period.
','Enemy',350,0,0,25,10,'DamageOverTime',15,25,0,0,0,0,0,0,0,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,5688,'2000-01-01 00:00:00'),
('94d97d36-8d74-5814-92be-504376655144',30193,805,805,'Arrogance','Target initially gains an immunity to melee damage and a bonus to every stat.
After the effect wears off, the target suffers an identical penalty to every stat and a weakness to melee damage. ','Self',0,0,2,0,0,'Arrogance',15,0,0,0,0,900,0,0,20,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2973,'2000-01-01 00:00:00'),
('d169f2d4-73a7-5064-8fde-9e41ee984fe4',30139,5134,5134,'Speed','The movement speed of the target is increased.','Self',0,0,0,0,0,'SpeedEnhancement',600,0,0,0,0,900,0,0,145,0,0,0,'','','','',0,0,10,0,0,0,0,0,0,0,'Artifacts',0,2975,'2000-01-01 00:00:00'),
('fa4f413b-b30d-5426-ba8d-57dc59a0c982',30121,11216,11216,'Dmg-Over-Time','Inflicts damage to the target repeatedly over a given time period.
','Enemy',1500,0,0,20,10,'DamageOverTime',16,20,0,0,0,0,0,0,0,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2981,'2000-01-01 00:00:00'),
('e6677823-1fb9-535b-b0dd-c20f579137c3',30147,10638,10638,'Chink in the Armor','Short term weapon resistance debuff.','Enemy',350,0,0,0,10,'CrushSlashThrustDebuff',30,0,0,0,0,900,0,0,10,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2982,'2000-01-01 00:00:00'),
('51663b94-c9ed-5270-86b7-edee201c431e',30148,9103,9103,'Stupefy','Casts a debuff on opponent that will reduce all stats.','Enemy',350,0,0,0,10,'AllStatsDebuff',60,0,0,0,0,900,0,0,12,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2983,'2000-01-01 00:00:00'),
('91977a1b-1f19-5732-a3e0-1828d77986ba',30100,9132,9132,'AF Debuff','Lowers the target''s Armor Factor (AF), resulting in worse protection against some forms of attack.','Enemy',350,0,2,0,10,'ArmorFactorDebuff',60,0,0,0,0,0,0,0,50,0,0,0,'','','','',0,0,1014,0,0,0,0,0,0,0,'Artifacts',0,2989,'2000-01-01 00:00:00'),
('a34349a4-4bbb-533d-b28b-75e0597784fe',30101,10118,10118,'Damage over time','Inflict damage over time to target','Enemy',350,0,0,35,10,'DamageOverTime',15,25,0,0,0,0,0,0,0,0,0,0,'You are being crushed by an invisible force!','{0} is being crushed by an invisible force!','The destructive energy around you fades.','The destructive energy around {0} fades.',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2990,'2000-01-01 00:00:00'),
('ad9d3778-bfa6-5a40-a995-7a786bd585fb',30177,5353,5353,'Harpy''s Wail','Give a chance to disarm enemy while attacked.','Enemy',0,0,0,0,0,'FumbleChanceDebuff',60,0,0,0,350,900,0,0,10,0,0,0,'','','','',0,0,1045,0,0,0,0,0,0,0,'Artifacts',0,2992,'2000-01-01 00:00:00'),
('e9747155-4efa-5571-b252-02c25335027a',30178,5353,5353,'Disoriented','Target is disoriented. They have less chance of landing melee attacks, and spells have a greater chance of affecting them.
','Enemy',0,0,0,0,0,'ToHitDebuff',60,0,0,0,350,900,0,0,10,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2993,'2000-01-01 00:00:00'),
('8f78f856-50d7-5715-b2f7-e4f9f48086a0',30172,8065,8065,'Replenish power','Replenishes the target.','Group',1500,0,0,0,0,'PowerHeal',0,0,0,0,1500,900,0,0,-25,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2996,'2000-01-01 00:00:00'),
('955166e8-b251-5028-ad13-bb06cdd7e7ec',30173,3042,3042,'Group Purification','Heals the target.','Group',1500,0,0,0,0,'Heal',0,0,0,0,1500,900,0,0,-25,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2997,'2000-01-01 00:00:00'),
('7a8b35ed-ffd5-5210-9279-fcb8c963255d',30184,976,976,'Kalare Regeneration','Regenerate Health Mana Endurance.','Self',0,0,0,0,0,'PowerHealthEnduranceRegenBuff',30,50,0,0,0,0,0,0,-5,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2998,'2000-01-01 00:00:00'),
('d78ee83f-8ba9-5907-a0f1-e0034ab83ccf',30124,11216,11216,'Dmg-Over-Time','Inflicts damage to the target repeatedly over a given time period.
','Enemy',350,0,0,25,10,'DamageOverTime',15,50,0,0,0,0,0,0,0,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,2999,'2000-01-01 00:00:00'),
('7c43303e-c657-5ba3-bd02-935978796793',30103,9103,9103,'Arrogance (Invocation)','Target initially gains an immunity to melee damage and a bonus to every stat. After the effect wears off, the target suffers an identical penalty to every stat and a weakness to melee damage.
','Self',0,0,0,0,0,'Arrogance',20,0,0,0,0,600,0,0,50,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3002,'2000-01-01 00:00:00'),
('a5a252d1-29f7-5474-8efb-2920fda6fe69',30144,11216,11216,'Rancid Bite','Inflicts damage to the target repeatedly over a given period','Enemy',350,0,0,25,10,'DamageOverTime',15,30,0,0,0,0,0,0,0,0,0,0,'','','','',0,0,0,30145,0,0,0,0,0,0,'Artifacts',0,3004,'2000-01-01 00:00:00'),
('72594705-ca07-5356-8eea-089ee89c8b68',30145,3422,3422,'Disease','Inflicts disease on the target.','Enemy',1500,0,0,0,10,'Disease',30,0,0,0,0,0,0,0,0,0,0,0,'','','','',0,0,1042,0,0,0,0,0,0,0,'Artifacts',0,3005,'2000-01-01 00:00:00'),
('100172e2-6113-5efd-b40c-d108f389f219',30134,11216,11216,'Arcane Bonds','Inflicts damage to the target repeatedly over a given period','Enemy',350,0,0,40,10,'DamageOverTime',22,25,0,0,0,0,0,0,0,0,0,0,'','','','',0,0,0,30135,0,0,0,0,0,0,'Artifacts',0,3007,'2000-01-01 00:00:00'),
('adccfe11-4214-5389-88a8-c08265bf3e20',30135,5202,5202,'Snare','Target moves slower for the spell''s duration.','Enemy',350,0,0,0,10,'UnbreakableSpeedDecrease',30,0,0,0,0,0,0,0,35,0,0,0,'','','','',0,0,1054,0,0,0,0,0,0,0,'Artifacts',0,3008,'2000-01-01 00:00:00'),
('c635330c-b68b-5c79-9bfa-7d2359fa98a1',30174,10067,10067,'Shared Might','Group ablative.','Group',1500,0,0,0,0,'AblativeArmor',300,0,0,0,1500,900,0,0,20,0,0,0,'','','','',0,0,1001,0,0,0,0,0,0,0,'Artifacts',0,3009,'2000-01-01 00:00:00'),
('7756b475-a0ec-5bf2-b381-cf04162800e0',30175,1586,1586,'Might of Oglidarsh','A PBAOE strength tap.','Enemy',400,0,0,0,10,'StrengthConstitutionDrain',60,0,0,0,350,900,0,0,30,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3010,'2000-01-01 00:00:00'),
('53481488-5561-5b2d-a307-0ba0cdcfc184',30166,5353,5353,'Dazzler','Target suffers from a magic bedazzlement that may cause him/her to fumble melee attacks, miscast magic spells or both.
','Enemy',350,0,0,0,10,'Bedazzlement',30,0,0,0,0,900,0,0,10,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,5865,'2000-01-01 00:00:00'),
('ba276c31-bda5-5e0a-a4f5-00d0c1186aab',30131,11216,11216,'Dmg-Over-Time','Inflicts damage to the target repeatedly over a given time period.','Enemy',350,0,0,20,10,'DamageOverTime',60,60,0,0,0,0,0,0,0,0,0,0,'','','','',0,0,0,49821,0,0,0,0,0,0,'Artifacts',0,3013,'2000-01-01 00:00:00'),
('a291840a-bb8f-5d8b-9780-457dbcd3c608',30132,3422,3422,'Unending Vengeance','This spell will turn part of the damage back on the opponent.','Self',0,0,0,-40,10,'DamageShield',30,0,0,0,0,900,0,0,0,0,0,0,'','','','',0,5,5,0,0,0,0,0,0,0,'Artifacts',0,3014,'2000-01-01 00:00:00'),
('21e5f642-03c7-511b-81dc-6fcd2e5100d4',30136,11466,11466,'Curse of the Scarab','Target has been shapechanged.','Enemy',350,0,0,0,0,'ScarabProc',30,0,0,0,0,0,0,0,0,0,1200,0,'','','','',0,0,0,30138,0,0,0,0,0,0,'Artifacts',0,3015,'2000-01-01 00:00:00'),
('1f4d3788-9b6d-5591-9fde-9960266f16d8',30138,2732,2732,'NearSight','Decreases the target''s range of vision.','Enemy',350,0,0,0,10,'Nearsight',30,0,0,0,0,0,0,0,50,0,0,0,'','','','',0,0,1047,30135,0,0,0,0,0,0,'Artifacts',0,3016,'2000-01-01 00:00:00'),
('f724792d-f5c4-51df-a2cb-2b56be988fa6',30137,1549,1549,'Scarab Armor','Makes the player more resistant to weapons.','Self',0,0,0,0,0,'CrushSlashThrustBuff',65335,0,0,0,0,1,0,0,3,0,0,0,'Your melee resistance has increased.','{0} melee resistance has increased.','Your melee resistance is back to normal.','{0} melee resistance is back to normal.',0,0,1009,0,1,1,0,0,0,0,'Artifacts',0,3017,'2000-01-01 00:00:00'),
('f0b81666-e58a-53c5-a4b9-a7e23dd9b2a8',30114,4385,4385,'Scepter''s Curse','Decreases the target''s listed stat(s), and transfers them to the caster.
Str/Con decrease: 25
','Enemy',1000,0,0,0,10,'StrengthConstitutionDrain',60,0,0,0,0,0,0,0,25,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3018,'2000-01-01 00:00:00'),
('fdce0037-6862-5b37-b0bb-6cfc91ebcd5d',30111,574,574,'Silver Aura','Creates a field that damages anyone who attacks the target in melee.','Self',0,0,0,6,10,'DamageShield',30,0,0,0,0,600,0,0,0,0,0,0,'','','','',0,5,5,30112,0,0,0,0,0,0,'Artifacts',0,3023,'2000-01-01 00:00:00'),
('9873dd19-ffa2-550d-9bf0-9bb65577f663',30112,11228,11228,'Snake charmer aura','Adds to the target''s absorption (ABS) factor, resulting in better protection against melee attacks.','Self',0,0,0,0,0,'ArmorAbsorptionBuff',30,0,0,0,0,600,0,0,25,0,0,0,'','','','',0,0,0,0,1,1,0,0,0,0,'Artifacts',0,3024,'2000-01-01 00:00:00'),
('0e2b6ea4-8614-5e06-a111-edb2a4f809af',30113,9611,9611,'Silver Fire','Drain life, power and endurance from target','Enemy',350,0,0,100,10,'SnakeCharmer',0,0,0,0,0,0,0,0,0,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3025,'2000-01-01 00:00:00'),
('53a2d075-c9d5-5d62-b28a-9066ac4d4d94',30204,3590,3590,'Stealth Lore','Characters with stealth and stealth lore ability become harder to see, and characters without stealth gain a small chance to see a stealthy opponent when nearby.','Self',0,0,0,0,0,'VampiirStealthDetection',60,0,0,0,0,900,0,0,162,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3026,'2000-01-01 00:00:00'),
('c4e22567-2955-5a66-afc8-d66e7a81904e',30205,11228,11228,'Great Shade Form.','Gives the player an ablative proc.','Self',0,0,0,100,0,'TraitorsDaggerProc',600,0,0,0,0,900,0,0,200,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,5967,'2000-01-01 00:00:00'),
('7193fef5-4d66-5904-9951-9c1b38b0720b',30116,13158,13158,'Stealth Lore','Target has a greater chance of uncovering stealthed opponents. If the target has the Stealth skill, their ability to use stealth is greater as well.','Self',0,0,0,0,0,'VampiirStealthDetection',60,0,0,0,0,900,0,0,162,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3028,'2000-01-01 00:00:00'),
('98bcce21-d93c-5a03-bed6-ea43a5cf00c0',30117,4091,4091,'Tempest','Target is snared, and their combat accuracy is impaired. Unlike other snares, this snare cannot be broken by being struck in battle.
Movement penalty: 25%
','Enemy',1500,0,0,0,10,'UnbreakableSpeedDecrease',30,0,0,0,700,900,0,0,25,0,0,0,'','','','',0,0,1054,0,0,0,0,0,0,0,'Artifacts',0,3029,'2000-01-01 00:00:00'),
('b217e34a-94f4-5027-b895-7d6e7fd0428d',30170,11466,11466,'Baleful Shape Change','Target has been shapechanged.','Enemy',1500,0,0,0,0,'StarsProc2',60,0,0,0,0,900,0,0,24,0,112,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3031,'2000-01-01 00:00:00'),
('4cb9cdfd-9ae2-5b7b-b1c5-c03fa2df8cec',30171,13125,13125,'Light Barrier','Reduces all melee damage against the character for the duration. This spell improves slash, crush and thrust resistances.','Self',0,0,0,0,0,'CrushSlashThrustBuff',30,0,0,0,0,900,0,0,10,0,0,0,'','','','',0,0,1009,0,0,0,0,0,0,0,'Artifacts',0,3034,'2000-01-01 00:00:00'),
('7a5c9437-ef63-5ac8-a92e-e1640702e7bc',30206,661,661,'Summon elemental','Summons an elemental spirit to attack the target.','Enemy',1000,0,0,-75,0,'TraitorsDaggerSummon',30,0,0,0,0,0,0,0,70,0,601,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3039,'2000-01-01 00:00:00'),
('6c2bdce2-754f-50a1-94e6-54fa5ba071e6',30120,4057,4057,'Arch Magery','Grants target additional spell damage and ability to penetrate target''s resistances.
','Group',2000,0,0,0,0,'Traldor',300,0,0,0,2000,1800,0,0,5,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3040,'2000-01-01 00:00:00'),
('b0468c28-45af-5a7d-ba66-68dea60d63c1',30160,4413,4413,'heal over time','Causes the target to regain health during the spell duration.','Self',0,0,0,0,0,'HealOverTime',30,50,0,0,0,0,0,0,30,0,0,0,'You feel calm and healthy.','{0} seems calm and healthy.','Your meditative state fades.','{0}''s meditative state fades.',0,0,13881,0,0,0,0,0,0,0,'Artifacts',0,3043,'2000-01-01 00:00:00'),
('f4a91a08-26e1-5a9b-9bff-718b82116e89',30161,11216,11216,'Dmg-Over-Time','Inflicts damage to the target repeatedly over a given period.','Enemy',1500,0,0,40,15,'DamageOverTime',15,25,0,0,0,0,0,0,0,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3044,'2000-01-01 00:00:00'),
('cddbaf0c-8334-5b59-a2d6-778aafb99520',30162,13640,13640,'Ring of Unyielding will aura','Inflict damage to the target repeatedly over a given time period.
Also casts: heal hits over time value','Self',0,0,0,30161,0,'UnyeldingProc',600,20,0,0,0,900,0,0,30160,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3045,'2000-01-01 00:00:00'),
('2baef880-d3fe-5a15-ac00-a08f4af11f7c',30199,91,91,'Dmg-Over-Time','Inflicts damage to the target repeatedly over a given period','Enemy',0,0,0,35,10,'DamageOverTime',15,25,0,0,700,900,0,0,0,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3047,'2000-01-01 00:00:00'),
('965f81bf-656d-5c63-a6fa-b43316f48166',30200,5353,5353,'Wind disorientation','Inflicts damage to the target repeatedly over a given time period. Also disorients the target, making it more difficult for them to strike at melee targets.','Enemy',0,0,0,0,0,'ToHitDebuff',30,0,0,0,700,900,0,0,5,0,0,0,'','','','',0,0,0,30199,0,0,0,0,0,0,'Artifacts',0,3048,'2000-01-01 00:00:00'),
('ec9f3c2e-df93-5513-8a5a-76d49b85acac',30149,2430,2430,'Speed of the Messenger','Increases the player''s movement rate.','Self',0,0,0,0,0,'SpeedEnhancement',600,0,0,0,0,0,0,0,156,0,0,0,'','','','',0,0,10,0,0,0,0,0,0,0,'Artifacts',0,3049,'2000-01-01 00:00:00'),
('9e9aee61-ebb6-53bd-9d73-da85b147e720',30142,9411,9411,'Aura of Life','Causes the target to regain health during the spell.','Group',500,0,0,0,0,'HealOverTime',20,50,0,0,500,0,0,0,30,0,0,0,'You feel calm and healthy.','{0} seems calm and healthy.','Your meditative state fades.','{0}''s meditative state fades.',0,0,13881,0,0,0,0,0,0,0,'Artifacts',0,3051,'2000-01-01 00:00:00'),
('0b53e1cb-6b7b-5d25-91d5-05f71689b31d',30143,0,581,'Weight of a Feather','Target takes less damage when falling.','Self',0,0,0,0,0,'CloudsongFall',300,0,0,0,0,900,0,0,25,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3052,'2000-01-01 00:00:00'),
('b15537e3-9c93-5225-877c-8bf845fa52e1',30186,3121,3121,'Raise Dead','Brings target back to life, and restores a portion of their health and power, and eliminates the experience penalty and con loss they would have suffered were they to have /released.
','Corpse',0,0,0,0,0,'Resurrect',0,0,0,0,1000,900,10,10,0,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3053,'2000-01-01 00:00:00'),
('8c419583-031a-546a-87d4-15db46274615',30187,7011,7011,'Recovery','Target recovers from the effects of resurrection.','Self',0,0,0,0,0,'YouthIllness',0,0,0,0,0,900,0,0,0,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3054,'2000-01-01 00:00:00'),
('94da73f2-87bd-5c62-b77f-39e567c317d9',30151,14190,14190,'Arcane Leadership','Target has improved spell range and ability to penetrate spell resists while within range of this aura. 
','Realm',0,0,0,0,0,'ZahurAura',5,50,1,0,1500,0,0,0,5,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3056,'2000-01-01 00:00:00'),
('4ad4c565-0c6c-56ec-b62d-ba602ac22054',30152,3295,3295,'Efficient Endurance','A portion of all heals cast on the target restore endurance instead.
','Self',0,0,0,0,0,'ZahurEnduProc',600,0,0,0,0,900,0,0,10,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3057,'2000-01-01 00:00:00'),
('b2d42e6f-a704-51de-abd1-8ebfcfe82a5e',30102,30102,451,'Minion''s of Zo''arkat','Summons an elemental spirit to attack the target.','Enemy',1500,0,0,40,0,'ZoSummon',30,0,0,0,0,900,0,0,40,0,602,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3059,'2000-01-01 00:00:00'),
('bb204631-16aa-5b31-a271-a0aaa1fb89ea',30165,5353,5353,'Bedazzlement','Target suffers from a magic bedazzlement that may cause him/her to fumble melee attacks, miscast magic spells or both.
','Enemy',0,0,0,0,10,'ZoAura',30,0,0,0,700,900,0,0,25,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'Artifacts',0,3060,'2000-01-01 00:00:00'),
('08922726-068a-5eeb-ba43-94fbb965d5aa',32175,8065,8065,'replenish power','Replenishes the target''s power pool.','self',0,0,0,0,0,'PowerHeal',0,0,0,0,0,720,0,0,-50,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'ChampWeapSpell',0,3799,'2000-01-01 00:00:00'),
('23c0bcb8-0311-5e45-8ee0-182f0c715653',32153,1198,1198,'ProcAblativeArmor50','Adds a shield to recipient which will temporarily absorb some of the damage type specified.','Self',0,0,0,50,0,'AblativeArmor',600,0,0,0,0,0,0,0,200,0,0,0,'','','','',0,0,1001,0,0,0,0,0,0,0,'',0,5174,'2000-01-01 00:00:00'),
('9fe29c92-cf6f-587e-b5d9-80e6aac3eb24',32232,976,976,'OmniTap','Regenerate Health Mana Endurance.','Enemy',1350,0,0,100,10,'OmniLifedrain',0,0,0,0,0,600,40,60,100,0,100,0,'','','','',0,0,0,0,0,0,0,0,0,0,'',0,5194,'2000-01-01 00:00:00'),
('96c75dbe-fec8-537b-8244-665cd8d09d64',61372,1372,1372,'Cure Disease','Cures Disease.','Self',1500,0,0,0,0,'CureDisease',0,0,0,0,0,60,0,0,0,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'',0,5306,'2000-01-01 00:00:00'),
('18f2c28c-6401-5d35-8153-9649c49effc8',61371,1371,1371,'Cure Poison','Cures Poison.','Self',1500,0,0,0,0,'CurePoison',0,0,0,0,0,60,0,0,0,0,0,0,'','','','',0,0,0,0,0,0,0,0,0,0,'',0,5307,'2000-01-01 00:00:00'),
('5a45c032-69af-5b20-a500-288198738901',40008,12035,12035,'Shield buff 75','Adds to the recipient''s Armor Factor (AF), resulting in better protection against some forms of attack. It acts in addition to any armor the target is wearing.','Self',0,0,0,0,0,'ArmorFactorBuff',600,0,0,0,0,0,0,0,75,0,0,0,'A crystal shield covers you.','A power shield covers {0}''s skin.','Your crystal shield fades.','',0,0,0,0,1,1,0,0,0,0,'',0,5372,'2000-01-01 00:00:00');
