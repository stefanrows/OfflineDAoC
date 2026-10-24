-- World data pack 026-toa-travel-realms
-- Atlantis travel per realm: 33 Teleport rows of type atlantis (ruins city, four havens, six dungeon entrances in each realm copy), ten haven porters, and the Oceanus channelers that were plain NPCs become porters.
-- Built by tools/toa/build_data_packs.py from tools/toa/sql. Do not hand-edit:
-- the server applies a pack once per save, so a fix ships as a new pack.
-- Applied by the server in one transaction (docs/TOA_DATA_PACKS.md).

-- ======== part: 026-toa-travel-realms.sql ========

-- Trials of Atlantis, WP1.2 travel (docs/TOA_TRAVEL.md, docs/TOA_DATA_PACKS.md). Hand-written, SQLite.
--
-- The porters' Atlantis lines ([Atlantis], [Atlantis Havens], [Atlantis Dungeons], AtlantisTravel.cs)
-- read Teleport rows of Type 'atlantis' in the asking player's own realm. This pack writes them for all
-- three realms and puts a working porter (an AllRealmsTeleporter) into every haven that lacks one.
--
-- Realm copies (zone points of pack 022): Albion 73 / 70 / 78-83 / 88 / 90, Midgard 30 / 71 / 35-40 /
-- 45 / 47, Hibernia 130 / 72 / 135-140 / 145 / 147. The overworld and dungeon copies are geometric
-- clones: identical zone offsets, the same four haven bind points, and 98 % of the 11,183 mobs of 73
-- stand on the same X, Y and Z in 30 and 130 (median height difference 0). A spot where an NPC
-- stands in one copy is floor in the other two.
--
-- The 13 gaheris rows of pack 021 (Type 'gaheris', Realm 0, Albion copy only) are not used: 9 of them
-- are anchored on monster spawns (Land of Atum lands among 94 aggressive mobs, Arbor Glen next to
-- Lord Tholos, level 65). Destinations here are only safe ones:
--
-- * Atlantis: the realm's ruins city (70/71/72: Arbiter, scholars, trainers, vault, healer), at the
--   arrival point of zone point 119, where a player walking in from Oceanus Hesperos lands.
-- * The four havens (towns with a bind point; no aggressive mob within aggro range of the landing):
--   Oceanus (Oceanus Hesperos; the DOL Oceanus landing the [X Oceanus] lines already use), Stygia
--   (Stygian Delta), Volcanus (Oceanus Anatole) and Aerus (Oceanus Notos). The landing is 80 units in
--   front of the haven's porter.
-- * Six dungeon entrances at their zone point arrivals: Cetus' Pit (126), Sobekite Eternal (122),
--   Temple of Twilight (128), Halls of Ma'ati (131), Great Pyramid (134), City of Aerus (140). Deep
--   Volcanus and Celestius hold no mobs and are left out.
--
-- Porters. Present before this pack: the Oceanus channelers in 73 (Raifaol) and 30 (Jarosua, Xatryn),
-- the Stygia porters in 73 and 130 (LiveTeleporter rows of the base world). New: one per ruins city,
-- 120 units ahead of the arrival (the hall floor is flat: every hall NPC stands at the arrival's Z);
-- one per haven of Volcanus and Aerus and Midgard's Stygia, on the spot where an NPC of another copy
-- stands, at least 150 units from every NPC of the target copy, facing the haven's bind point.
-- Idempotent: the rows are deleted by type and package first; the UPDATEs only touch plain GameNPCs.

-- 1. 33 destinations: 11 per realm.
DELETE FROM Teleport WHERE Type = 'atlantis';

INSERT INTO Teleport (Teleport_ID, Type, TeleportID, Realm, RegionID, X, Y, Z, Heading) VALUES
('toa-atlantis-1-atlantis', 'atlantis', 'Atlantis', 1, 70, 578494, 533304, 7295, 643),
('toa-atlantis-1-haven-of-oceanus', 'atlantis', 'Haven of Oceanus', 1, 73, 271184, 539600, 8344, 645),
('toa-atlantis-1-haven-of-stygia', 'atlantis', 'Haven of Stygia', 1, 73, 331010, 451481, 8141, 1030),
('toa-atlantis-1-haven-of-volcanus', 'atlantis', 'Haven of Volcanus', 1, 73, 448043, 551207, 8597, 1532),
('toa-atlantis-1-haven-of-aerus', 'atlantis', 'Haven of Aerus', 1, 73, 384944, 650489, 8132, 3124),
('toa-atlantis-1-cetus-pit', 'atlantis', 'Cetus'' Pit', 1, 78, 31363, 39344, 16086, 2116),
('toa-atlantis-1-sobekite-eternal', 'atlantis', 'Sobekite Eternal', 1, 79, 22666, 45432, 8500, 2114),
('toa-atlantis-1-temple-of-twilight', 'atlantis', 'Temple of Twilight', 1, 80, 29308, 26252, 17300, 400),
('toa-atlantis-1-halls-of-ma-ati', 'atlantis', 'Halls of Ma''ati', 1, 83, 32362, 29972, 15975, 180),
('toa-atlantis-1-great-pyramid', 'atlantis', 'Great Pyramid', 1, 88, 32898, 28195, 16900, 4091),
('toa-atlantis-1-city-of-aerus', 'atlantis', 'City of Aerus', 1, 90, 41979, 38173, 10341, 2257),
('toa-atlantis-2-atlantis', 'atlantis', 'Atlantis', 2, 71, 565601, 570056, 7255, 1716),
('toa-atlantis-2-haven-of-oceanus', 'atlantis', 'Haven of Oceanus', 2, 30, 271184, 539600, 8344, 645),
('toa-atlantis-2-haven-of-stygia', 'atlantis', 'Haven of Stygia', 2, 30, 331010, 451326, 8140, 1028),
('toa-atlantis-2-haven-of-volcanus', 'atlantis', 'Haven of Volcanus', 2, 30, 448043, 551207, 8597, 1532),
('toa-atlantis-2-haven-of-aerus', 'atlantis', 'Haven of Aerus', 2, 30, 385009, 650739, 8133, 3804),
('toa-atlantis-2-cetus-pit', 'atlantis', 'Cetus'' Pit', 2, 35, 31363, 39344, 16086, 2116),
('toa-atlantis-2-sobekite-eternal', 'atlantis', 'Sobekite Eternal', 2, 36, 22666, 45432, 8500, 2114),
('toa-atlantis-2-temple-of-twilight', 'atlantis', 'Temple of Twilight', 2, 37, 29308, 26252, 17300, 400),
('toa-atlantis-2-halls-of-ma-ati', 'atlantis', 'Halls of Ma''ati', 2, 40, 32362, 29972, 15975, 180),
('toa-atlantis-2-great-pyramid', 'atlantis', 'Great Pyramid', 2, 45, 32898, 28195, 16900, 4091),
('toa-atlantis-2-city-of-aerus', 'atlantis', 'City of Aerus', 2, 47, 41979, 38173, 10341, 2257),
('toa-atlantis-3-atlantis', 'atlantis', 'Atlantis', 3, 72, 551807, 577161, 6767, 2061),
('toa-atlantis-3-haven-of-oceanus', 'atlantis', 'Haven of Oceanus', 3, 130, 271184, 539600, 8344, 645),
('toa-atlantis-3-haven-of-stygia', 'atlantis', 'Haven of Stygia', 3, 130, 331523, 451167, 8141, 3072),
('toa-atlantis-3-haven-of-volcanus', 'atlantis', 'Haven of Volcanus', 3, 130, 448564, 551424, 8596, 3083),
('toa-atlantis-3-haven-of-aerus', 'atlantis', 'Haven of Aerus', 3, 130, 384944, 650489, 8132, 3124),
('toa-atlantis-3-cetus-pit', 'atlantis', 'Cetus'' Pit', 3, 135, 31363, 39344, 16086, 2116),
('toa-atlantis-3-sobekite-eternal', 'atlantis', 'Sobekite Eternal', 3, 136, 22666, 45432, 8500, 2114),
('toa-atlantis-3-temple-of-twilight', 'atlantis', 'Temple of Twilight', 3, 137, 29308, 26252, 17300, 400),
('toa-atlantis-3-halls-of-ma-ati', 'atlantis', 'Halls of Ma''ati', 3, 140, 32362, 29972, 15975, 180),
('toa-atlantis-3-great-pyramid', 'atlantis', 'Great Pyramid', 3, 145, 32898, 28195, 16900, 4091),
('toa-atlantis-3-city-of-aerus', 'atlantis', 'City of Aerus', 3, 147, 41979, 38173, 10341, 2257);

-- 2. Ten porters (AlbionTeleporter, MidgardTeleporter, HiberniaTeleporter: the full porter menu).
DELETE FROM Mob WHERE PackageID = 'toa-travel';

INSERT INTO Mob (Mob_ID, ClassType, Name, Guild, X, Y, Z, Heading, Region, Model, Size, Strength, Constitution, Dexterity, Quickness, Intelligence, Piety, Empathy, Charisma, Level, Realm, EquipmentTemplateID, NPCTemplateID, Flags, AggroLevel, AggroRange, MeleeDamageType, RespawnInterval, VisibleWeaponSlots, PackageID) VALUES
('toa-travel-1-atlantis', 'DOL.GS.AlbionTeleporter', 'Channeler Aldith', 'Teleporter', 578394, 533370, 7295, 2691, 70, 760, 52, 350, 50, 100, 50, 30, 30, 30, 30, 50, 1, NULL, -1, 16, 0, 0, 2, 0, 34, 'toa-travel'),
('toa-travel-2-atlantis', 'DOL.GS.MidgardTeleporter', 'Channeler Ragna', 'Teleporter', 565542, 569951, 7255, 3764, 71, 221, 50, 350, 50, 100, 50, 30, 30, 30, 30, 50, 2, '7c29295c-2e25-40c1-9599-355f1c140bbb', -1, 16, 0, 0, 2, 0, 34, 'toa-travel'),
('toa-travel-3-atlantis', 'DOL.GS.HiberniaTeleporter', 'Channeler Muirne', 'Teleporter', 551809, 577041, 6767, 13, 72, 1160, 52, 350, 50, 100, 50, 30, 30, 30, 30, 50, 3, 'ff81420c-53f7-4cd6-9bb4-ddef0f7d0730', -1, 16, 0, 0, 2, 0, 34, 'toa-travel'),
('toa-travel-2-haven-of-stygia', 'DOL.GS.MidgardTeleporter', 'Channeler Bjarni', 'Teleporter', 330930, 451326, 8140, 3076, 30, 221, 50, 350, 50, 100, 50, 30, 30, 30, 30, 50, 2, '7c29295c-2e25-40c1-9599-355f1c140bbb', -1, 16, 0, 0, 2, 0, 34, 'toa-travel'),
('toa-travel-1-haven-of-volcanus', 'DOL.GS.AlbionTeleporter', 'Channeler Osric', 'Teleporter', 447986, 551151, 8597, 3580, 73, 760, 52, 350, 50, 100, 50, 30, 30, 30, 30, 50, 1, NULL, -1, 16, 0, 0, 2, 0, 34, 'toa-travel'),
('toa-travel-2-haven-of-volcanus', 'DOL.GS.MidgardTeleporter', 'Channeler Torvi', 'Teleporter', 447986, 551151, 8597, 3580, 30, 221, 50, 350, 50, 100, 50, 30, 30, 30, 30, 50, 2, '7c29295c-2e25-40c1-9599-355f1c140bbb', -1, 16, 0, 0, 2, 0, 34, 'toa-travel'),
('toa-travel-3-haven-of-volcanus', 'DOL.GS.HiberniaTeleporter', 'Channeler Ailbe', 'Teleporter', 448644, 551425, 8596, 1035, 130, 1160, 52, 350, 50, 100, 50, 30, 30, 30, 30, 50, 3, 'ff81420c-53f7-4cd6-9bb4-ddef0f7d0730', -1, 16, 0, 0, 2, 0, 34, 'toa-travel'),
('toa-travel-1-haven-of-aerus', 'DOL.GS.AlbionTeleporter', 'Channeler Edyth', 'Teleporter', 385024, 650495, 8132, 1076, 73, 760, 52, 350, 50, 100, 50, 30, 30, 30, 30, 50, 1, NULL, -1, 16, 0, 0, 2, 0, 34, 'toa-travel'),
('toa-travel-2-haven-of-aerus', 'DOL.GS.MidgardTeleporter', 'Channeler Hallveig', 'Teleporter', 385044, 650811, 8133, 1756, 30, 221, 50, 350, 50, 100, 50, 30, 30, 30, 30, 50, 2, '7c29295c-2e25-40c1-9599-355f1c140bbb', -1, 16, 0, 0, 2, 0, 34, 'toa-travel'),
('toa-travel-3-haven-of-aerus', 'DOL.GS.HiberniaTeleporter', 'Channeler Siofra', 'Teleporter', 385024, 650495, 8132, 1076, 130, 1160, 52, 350, 50, 100, 50, 30, 30, 30, 30, 50, 3, 'ff81420c-53f7-4cd6-9bb4-ddef0f7d0730', -1, 16, 0, 0, 2, 0, 34, 'toa-travel');

-- 3. The Oceanus channelers that are plain GameNPCs become porters like their neighbours. Hibernia's
--    two load an NPC template with ReplaceMobValues = 1 and Flags 0, which would drop the peace flag;
--    they keep the template's look (Hialysam 302, Lana 1160) without it.
UPDATE Mob
   SET ClassType = 'DOL.GS.HiberniaTeleporter', NPCTemplateID = -1, Model = 302, Flags = (Flags | 16),
       AggroLevel = 0, AggroRange = 0
 WHERE Region = 130 AND Name = 'Channeler Hialysam' AND ClassType = 'DOL.GS.GameNPC';

UPDATE Mob
   SET ClassType = 'DOL.GS.HiberniaTeleporter', NPCTemplateID = -1, Flags = (Flags | 16),
       AggroLevel = 0, AggroRange = 0
 WHERE Region = 130 AND Name = 'Channeler Lana' AND ClassType = 'DOL.GS.GameNPC';

UPDATE Mob
   SET ClassType = 'DOL.GS.AlbionTeleporter', Flags = (Flags | 16), AggroLevel = 0, AggroRange = 0
 WHERE Region = 73 AND Name = 'Channeler Khol' AND ClassType = 'DOL.GS.GameNPC';
