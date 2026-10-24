-- Generated from gaheris sql/29-atlantis-zonepoints.sql by tools/toa/convert_mysql_to_sqlite.py
-- (converter v1, uuid mode literal, rules round all). Do not hand-edit; rerun the converter.
-- Source sha256: 1b124d3c8bf7b4b30723efffa8b1b8233661036ceff740337a2e8e9ff600e805
-- Dialect: SQLite 3.35+ (UPSERT without a conflict target). Run inside one
-- transaction; MySQL session statements below are comments only.
-- Statements: 100; inserts: 100 (100 VALUES rows);
-- upserts: 0 DO UPDATE, 100 DO NOTHING; UUID literals: 100.

-- Trials of Atlantis zone points.
--
-- The static portals in Atlantis did nothing when you walked into them. Not
-- broken scripts, no data at all: the zonepoint table held 169 rows and not
-- one of them touched an Atlantis region in either direction.
--
-- A portal is not an object the server owns. The client knows where the
-- portal is and sends its Id on contact; RegionChangeRequestHandler looks
-- that Id up in zonepoint and moves the player to the target it finds. With
-- no row, the lookup misses and the handler returns without a word to a
-- normal player -- which is why every portal in Atlantis looked equally dead.
--
-- These 100 rows come from the same DOL dump the 41,727 Atlantis mobs came
-- from (dol-db/ZonePoint.json, 1737 rows). Every ToA row, in both directions,
-- deduplicated on (Id, Realm). None of them collide with a row we already
-- have, so nothing existing changes.
--
-- Realms are kept as they came: 34 Albion, 31 Midgard, 35 Hibernia. Atlantis
-- exists three times over -- region 30 is Albion's, 73 Midgard's arrival hub
-- as we have it, 130 Hibernia's -- and the Id/Realm pair is what picks which
-- one a given player reaches.

INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('ff6b916e-0c3f-5918-8fcf-b05d5d171234', 116, 1, 0, 0, 0, 0, 73, 274175, 534600, 8727, 180, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('c036bc7a-7684-55a9-817c-c6d28260b962', 117, 1, 0, 0, 0, 0, 73, 274175, 534600, 8727, 180, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('7016be17-b105-5bdd-9266-c70186882305', 118, 1, 70, 578757, 533118, 7214, 73, 274175, 534600, 8727, 180, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('c5f586f1-42fc-5208-bb04-03d85f7520ad', 119, 1, 30, 274179, 534341, 8646, 70, 578494, 533304, 7295, 643, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('402924e8-c405-54df-9de0-6042c1b5d04e', 120, 1, 36, 22460, 45673, 8518, 73, 343922, 472891, 4205, 92, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('a01330d7-ec55-52b9-842d-a5d82df49eb1', 121, 1, 36, 29173, 24137, 16978, 73, 333470, 465791, 8783, 250, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('55ee7cab-539e-578b-bf0b-dc25ca7e72b5', 122, 1, 30, 344249, 472538, 4100, 79, 22666, 45432, 8500, 2114, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('e7a465bb-bc37-5ecd-a2a0-d4d8562f3d9f', 123, 1, 30, 333212, 466125, 8822, 79, 29016, 23961, 16917, 350, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('43e797f1-4c8a-5b30-a2a1-3e494066e1d3', 124, 1, 30, 315382, 422206, 8605, 73, 313072, 435211, 8487, 15, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('b634a9d8-a5ab-5f12-9da7-d854148bd166', 125, 1, 30, 313061, 435140, 8485, 73, 315389, 422131, 8599, 2059, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('d256e253-b2ea-5b35-a4db-2b304eea59fd', 126, 1, 30, 387321, 530080, 4425, 78, 31363, 39344, 16086, 2116, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('0853d3c8-ff23-54f1-8315-920867310c76', 127, 1, 78, 31287, 39417, 15853, 73, 387204, 530163, 4411, 228, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('8c981e76-981c-5768-93a2-5f7e7d78e3ca', 128, 1, 30, 344324, 651174, 5295, 80, 29308, 26252, 17300, 400, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('5affc5f7-0ca0-53e8-929b-e4e150fc0a5e', 129, 1, 37, 29468, 26150, 17312, 73, 344404, 651070, 5319, 2209, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('809a7560-245c-5b39-b36a-4af8bfd8dc8c', 130, 1, 30, 234397, 401998, 9100, 83, 33021, 29980, 15975, 180, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('e7d9ee80-c63e-527e-8597-3469d7d556a5', 131, 1, 30, 234397, 401240, 9100, 83, 32362, 29972, 15975, 180, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('9fd83572-e50b-578e-8831-3baf03800026', 132, 1, 40, 32979, 29932, 15975, 73, 234452, 401954, 9100, 2456, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('1ff17fd4-3d8a-51f3-ab93-0462fff7908e', 133, 1, 40, 32301, 29942, 15975, 73, 234467, 401349, 9100, 90, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('5e5deafb-2f4b-5fe6-b906-d73acb048ded', 134, 1, 30, 256406, 425596, 9344, 88, 32898, 28195, 16900, 4091, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('6be82155-543e-56ab-a265-fb9720ca030e', 135, 1, 45, 32899, 27976, 16900, 73, 256300, 425600, 9344, 1048, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('a84f3283-a2c3-5c5c-b1ab-514ad4f80744', 136, 1, 0, 0, 0, 0, 73, 255788, 425626, 11109, 90, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('3459a9c2-901f-5fa1-9804-e3ec9436fd01', 137, 1, 30, 572803, 554167, 10356, 89, 33099, 42988, 16445, 968, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('fd6bb43d-4a24-5e80-9a23-e0b34f4cc7bb', 138, 1, 46, 33133, 43027, 16445, 73, 572737, 554159, 10332, 1037, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('cb5a1a43-1849-566d-9ed3-363092a1ec5a', 139, 1, 89, 48703, 16893, 15668, 73, 572737, 554159, 10332, 1037, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('388ed5c2-e1a0-5506-944e-3ded61eba380', 140, 1, 30, 464493, 665610, 14888, 90, 41979, 38173, 10341, 2257, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('fa6dc31b-efe2-5c5c-8e89-6d5824d988d4', 141, 1, 90, 41846, 38307, 10341, 73, 464393, 665717, 14889, 170, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('4e3d5301-e509-52dc-a490-5acc8b103dee', 142, 1, 0, 0, 0, 0, 91, 31892, 34784, 15763, 0, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('102d0fd5-f2ff-50e0-b411-64a648d88acd', 143, 1, 0, 0, 0, 0, 90, 51377, 39790, 11231, 290, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('9b554f0a-6492-5b06-b0bf-2996d1e9bc1d', 256, 1, 201, 29362, 33080, 7890, 92, 58461, 28753, 11277, 1028, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('42c1ab1a-56ca-58c9-aeaa-31ea265d2f6b', 258, 1, 0, 0, 0, 0, 93, 32452, 31090, 17036, 1979, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('a1b8ebbf-056d-5dd8-b32d-45a8c91b1979', 260, 1, 93, 23003, 30981, 16934, 228, 34405, 24015, 16832, 22, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('2a9d3fc1-ac6a-5230-8585-dcccb71a2d19', 268, 1, 0, 0, 0, 0, 93, 23013, 30909, 16934, 2128, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('93153ac5-b99a-5309-9d3e-6e58f0e65941', 269, 1, 228, 34387, 23872, 16829, 93, 23013, 30909, 16934, 2128, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('703925a5-6468-5b88-8398-58a7ff36a86f', 445, 1, 92, 49825, 30491, 11741, 446, 35587, 36623, 15822, 764, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('2ffb1187-ae19-5e92-ab6f-49c192bbc9f2', 116, 2, 0, 0, 0, 0, 30, 274175, 534600, 8727, 180, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('9ad30a8a-4c87-5f0e-a6a0-9817b833ee5c', 117, 2, 71, 565776, 570396, 7168, 30, 274175, 534600, 8727, 180, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('16eec345-fd49-5a79-98f6-aa9b6a33514d', 118, 2, 0, 0, 0, 0, 30, 274175, 534600, 8727, 180, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('1e6c3b91-a811-593c-aa87-90f46b441f2b', 119, 2, 73, 274228, 534289, 8637, 71, 565601, 570056, 7255, 1716, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('85b78121-33ce-5d4c-91b2-68e71a52231b', 120, 2, 73, 343922, 472891, 4205, 30, 343922, 472891, 4205, 92, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('c0f735c7-b566-502a-a875-6a2363b7caef', 121, 2, 0, 0, 0, 0, 30, 333470, 465791, 8783, 250, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('cdb8e46f-393d-5256-955f-a6f9235014e2', 122, 2, 73, 344196, 472447, 3969, 36, 22666, 45432, 8500, 2114, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('e33997dd-26c1-5525-9461-653a23dbec5b', 123, 2, 73, 333531, 465814, 8778, 36, 29016, 23961, 16917, 350, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('3c199601-65ca-5b1e-b29d-530ff63a023e', 124, 2, 73, 315377, 422257, 8605, 30, 313072, 435211, 8487, 15, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('6b32803e-67af-592a-9229-dd550b182a27', 125, 2, 73, 313083, 435130, 8490, 30, 315389, 422131, 8599, 2059, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('cbff4e0b-c9b1-5c31-aef6-ef5aae562861', 126, 2, 73, 387414, 530171, 4518, 35, 31363, 39344, 16086, 2116, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('ea5fb212-4165-5046-8761-3f529b0b533d', 127, 2, 78, 31300, 39405, 15809, 30, 387204, 530163, 4411, 228, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('28f5776a-0c2f-5afe-97dc-2fd1d3a69996', 128, 2, 73, 344183, 651066, 5360, 37, 29308, 26252, 17300, 400, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('00acaf17-df6b-5e6e-87c9-e9c2d7ea5460', 129, 2, 0, 0, 0, 0, 30, 344404, 651070, 5319, 2209, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('fc99ae24-43d0-5e07-b59b-294c948bb01d', 131, 2, 73, 234427, 401379, 9100, 40, 32362, 29972, 15975, 180, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('4270a31b-be1e-5a84-a4bb-8807d343326f', 132, 2, 83, 33037, 29939, 15975, 30, 234452, 401954, 9100, 2456, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('3e044cae-33d1-50a9-a26a-0903ed57182b', 133, 2, 0, 0, 0, 0, 30, 234467, 401349, 9100, 90, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('d49e047d-f97b-57d6-a0c7-9994545308c8', 134, 2, 73, 256461, 425601, 9344, 45, 32898, 28195, 16900, 4091, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('2db27b63-63e5-5a28-8ac6-c9a392549718', 135, 2, 88, 32891, 27866, 16900, 30, 256300, 425600, 9344, 1048, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('685c19d0-2127-5a6b-a245-4bb66820b195', 136, 2, 0, 0, 0, 0, 30, 255788, 425626, 11109, 90, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('67e7ce81-59a3-5f31-90be-05eb0aaa5e92', 137, 2, 73, 572779, 554156, 10356, 46, 33099, 42988, 16445, 968, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('a393c0b2-6951-5873-ba30-319ddba38985', 138, 2, 89, 33179, 43030, 16445, 30, 572737, 554159, 10332, 1037, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('bd729e25-0d5d-542a-bf6d-305e3e602537', 139, 2, 0, 0, 0, 0, 30, 572737, 554159, 10332, 1037, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('d52cba78-3780-5dd0-91b4-63df8cd150aa', 140, 2, 73, 464455, 665519, 14888, 47, 41979, 38173, 10341, 2257, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('33221fc2-e790-5a7e-b22f-59dd0d391ee2', 141, 2, 90, 41838, 38297, 10341, 30, 464393, 665717, 14889, 170, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('5b0a5465-bef7-56a7-b1d4-73e52942f2e7', 142, 2, 47, 53102, 38926, 11920, 91, 31892, 34784, 15763, 0, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('2859d8f3-46c7-5e9c-bea0-1dda07de0848', 143, 2, 91, 31906, 34971, 15722, 47, 51377, 39790, 11231, 290, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('a8223692-e5da-5428-9fda-46443b77f077', 256, 2, 0, 0, 0, 0, 92, 58461, 28753, 11277, 1028, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('dd42d9bf-11ac-5952-9428-2956c8aa7773', 258, 2, 0, 0, 0, 0, 93, 32452, 31090, 17036, 1979, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('8da7e526-6012-5e45-82cd-ec6682dd391e', 268, 2, 0, 0, 0, 0, 93, 26344, 27854, 17597, 3072, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('d6b5ca80-de99-523c-9a9e-46c78774322e', 269, 2, 0, 0, 0, 0, 93, 23013, 30909, 16934, 2128, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('0005f052-cd68-5cca-b313-3f2323a0bc07', 116, 3, 72, 551784, 577456, 6686, 130, 274175, 534600, 8727, 180, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('d040fb3d-428a-5b61-8218-cf4285789db3', 117, 3, 0, 0, 0, 0, 130, 274175, 534600, 8727, 180, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('83d1b849-036f-52de-a8ff-c2e105056a02', 118, 3, 0, 0, 0, 0, 130, 274175, 534600, 8727, 180, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('05d565e0-8d10-5686-999c-3a05fbe349b6', 119, 3, 30, 274176, 534359, 8637, 72, 551807, 577161, 6767, 2061, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('89f48fb0-7e5e-52eb-b768-81cb916b8b54', 120, 3, 0, 0, 0, 0, 130, 343922, 472891, 4205, 92, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('e5e26292-0a10-5148-b711-0feb5d3569b5', 121, 3, 0, 0, 0, 0, 130, 333470, 465791, 8783, 250, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('27468849-434b-53b3-9147-39fa3c085d8a', 122, 3, 130, 344246, 472445, 4073, 136, 22666, 45432, 8500, 2114, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('70ff3155-905b-544f-8d43-0fa44053bf11', 124, 3, 130, 315378, 422220, 8606, 130, 313072, 435211, 8487, 15, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('7f67b37c-a1e5-57d2-8e00-588d506cec63', 125, 3, 130, 313073, 435147, 8494, 130, 315389, 422131, 8599, 2059, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('02bf89fe-d669-554d-8752-1496f8ff1f56', 126, 3, 130, 387406, 530204, 4733, 135, 31363, 39344, 16086, 2116, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('7aeb5d01-1237-53fc-8dcd-6ef4c0247172', 127, 3, 135, 31386, 39446, 16085, 130, 387204, 530163, 4411, 228, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('a5597e40-ede2-5f5b-a089-2fcd01b51e91', 128, 3, 130, 344143, 651141, 5386, 137, 29308, 26252, 17300, 400, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('72737262-a465-5f37-8937-2379ed329cce', 129, 3, 0, 0, 0, 0, 130, 344404, 651070, 5319, 2209, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('e82badee-4bd8-5c3b-88c1-a18b82d1a124', 130, 3, 130, 234385, 402008, 9100, 140, 33021, 29980, 15975, 180, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('9ae9026c-545a-5234-9773-1cc68ea4e42a', 131, 3, 130, 234390, 401350, 9100, 140, 32362, 29972, 15975, 180, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('b9cc7b13-c700-5aa2-ab03-928c20ec96c8', 132, 3, 0, 0, 0, 0, 130, 234452, 401954, 9100, 2456, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('453642b0-a3bc-59f4-b57e-3ce65f56935b', 133, 3, 140, 32349, 29920, 15975, 130, 234467, 401349, 9100, 90, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('74e9958e-eeb4-5229-96b2-971eaf732959', 134, 3, 130, 256566, 425615, 9344, 145, 32898, 28195, 16900, 4091, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('73880ac6-f319-57d0-af45-f9b49c6ef1ba', 135, 3, 145, 32907, 27876, 16900, 130, 256300, 425600, 9344, 1048, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('dd6d6fef-e913-5fa7-b9e2-304087ce74ef', 136, 3, 0, 0, 0, 0, 130, 255788, 425626, 11109, 90, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('47e83dea-a1da-5487-91b6-385b430e4bc4', 137, 3, 130, 572873, 554158, 10356, 146, 33099, 42988, 16445, 968, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('4becbbb4-2d2a-5e2a-a93e-d9eb29dc4b0e', 138, 3, 146, 33186, 43001, 16445, 130, 572737, 554159, 10332, 1037, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('a8237089-bd05-528c-a0a5-600b13b3a73b', 139, 3, 146, 48737, 16902, 15688, 130, 572737, 554159, 10332, 1037, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('b7fd47b1-4be1-5dc2-a7de-f329933a1e7d', 140, 3, 130, 464438, 665513, 14889, 147, 41979, 38173, 10341, 2257, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('609a2b92-667b-5d9b-85b7-0f1786afac83', 141, 3, 147, 41816, 38317, 10341, 130, 464393, 665717, 14889, 170, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('8b6f9444-7028-5a56-93c1-e05bdc096ff8', 142, 3, 147, 53012, 38863, 11968, 91, 31892, 34784, 15763, 0, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('feedd6bb-5c4c-5c51-bca2-c0ee70ad24f1', 143, 3, 0, 0, 0, 0, 147, 51377, 39790, 11231, 290, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('673b3511-ce4c-5c7e-aaab-3f1ff81819ee', 256, 3, 201, 29340, 33112, 7880, 92, 58461, 28753, 11277, 1028, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('184ea518-eb3f-551b-bf45-1fafeee41f64', 258, 3, 197, 19325, 24207, 15470, 93, 32452, 31090, 17036, 1979, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('8c9d67ef-80c0-55b3-b36a-e0d0fec70854', 260, 3, 93, 23040, 30982, 16938, 228, 34405, 24015, 16832, 22, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('237d5953-843c-5da7-9aaf-25c52764d8b1', 261, 3, 93, 32437, 31321, 17060, 197, 19506, 24014, 15477, 3163, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('cbaf54a4-16b3-5c17-a64a-ff3835fc3d43', 268, 3, 228, 32808, 23670, 17628, 93, 23013, 30909, 16934, 2128, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('e65c39ca-124d-54fe-b1c9-11225276b181', 269, 3, 228, 34378, 23873, 16833, 93, 23013, 30909, 16934, 2128, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('2cf79ee4-251d-5913-92e0-c93e0ddac100', 445, 3, 92, 49634, 30670, 11820, 446, 35587, 36623, 15822, 764, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
INSERT INTO "zonepoint" (ZonePoint_ID, Id, Realm, SourceRegion, SourceX, SourceY, SourceZ, TargetRegion, TargetX, TargetY, TargetZ, TargetHeading, ClassType, LastTimeRowUpdated) VALUES ('a44ff169-01cc-55f3-96a6-529f641f87f5', 449, 3, 92, 31306, 39910, 15729, 447, 35698, 31254, 15822, 990, '', '2000-01-01 00:00:00')
ON CONFLICT DO NOTHING;
