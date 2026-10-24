-- 070-toa-encounter-targets: which creature's death earns an artifact's encounter credit (WP3.3).
-- Hand-written, SQLite. Read by ArtifactEncounterCredit (docs/TOA_ARTIFACTS.md, "Encounter credit").
--
-- One row per artifact and creature: ArtifactID (Artifact.ArtifactID), MobName (compared without
-- case), Region (0 = any; every name here stands only in the three Atlantis copies 30, 73 and 130),
-- CreditMode 'group' (the players who earned the kill: attackers and their group members in range)
-- or 'battlegroup' (also every member of their battlegroups in range), and Zone, where the creature
-- stands (measured from its spawns in the three copies; text for the scholar's hint and the chat line).
--
-- Sources, per artifact in the comment above its rows:
-- * DOLSharp: step 1 of the artifact's quest in Dawn-of-Light/DOLSharp
--   GameServer/quests/Atlantis/Artifacts ("Defeat Minkhat.").
-- * Herald: Camelot Herald patch notes and Q&A in the DAoC knowledge base naming the creature.
-- * chosen: neither names a creature (a chest, a flask, a stub quest); a named creature of the
--   artifact's region, from the Herald's scholar table (Oceanus, Stygia, Volcanus or Aerus Haven).
-- Credit mode: the Herald's list of single-group encounters (1.75 Q&A); every other encounter is a
-- battlegroup encounter, as the Herald says. A Flask (credit by range live) counts as battlegroup.
--
-- The table is created by the server from DbArtifactEncounterTarget before the packs run.
-- Idempotent: fixed row IDs, INSERT OR IGNORE.

-- 1001 Alvarus's Leggings (Oceanus): DOLSharp. "Defeat Minkhat."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1001-minkhat', '1001', 'Minkhat', 0, 'battlegroup', 'Mesothalassa');

-- 1002 Arms of the Winds (Oceanus): DOLSharp. "Defeat Raging Tornado."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1002-raging-tornado', '1002', 'Raging Tornado', 0, 'battlegroup', 'Oceanus Notos');

-- 1003 Aten's Shield (Oceanus): chosen. DOLSharp: "Find the right chest." A named guardian of Oceanus Hesperos instead
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1003-aidoios', '1003', 'Aidoios', 0, 'battlegroup', 'Oceanus Hesperos or Oceanus Anatole');

-- 1004 A Flask (Volcanus): chosen. live: carry the flask past the taurs (credit by range); the named taur of Typhon's Reach
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1004-taur-scribe', '1004', 'Taur Scribe', 0, 'battlegroup', 'Typhon''s Reach');

-- 1005 Band of Stars (Stygia): DOLSharp. "Defeat Chisisi."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1005-chisisi', '1005', 'Chisisi', 0, 'group', 'Stygian Delta');

-- 1006 Battler (Volcanus): DOLSharp. "Defeat Battler."; Herald: "the Battler monster"; spawned by pack 071
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1006-battler', '1006', 'Battler', 0, 'battlegroup', 'Typhon''s Reach');

-- 1007 Belt of the Moon (Oceanus): DOLSharp. "Defeat the protectors."; the dark ones for the moon (Oceanus Boreal)
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1007-protector-of-dark', '1007', 'protector of dark', 0, 'group', 'Oceanus Boreal');

-- 1008 Belt of the Sun (Oceanus): DOLSharp. "Defeat the protectors."; the light ones for the sun, by the belt of the sun barrier
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1008-protector-of-light', '1008', 'protector of light', 0, 'group', 'Oceanus Boreal');

-- 1009 Bracer of Zo'arkat (Oceanus): chosen. DOLSharp: "Defeat the champion." Herald: a sea encounter tested with two groups; Krojer, master of the champions of Oceanus Anatole
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1009-krojer', '1009', 'Krojer', 0, 'battlegroup', 'Oceanus Anatole');

-- 1010 Braggart's Bow (Aerus): Herald. "Karise, the Braggart's Bow mob"
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1010-karise', '1010', 'Karise', 0, 'battlegroup', 'Green Glades');

-- 1011 Bruiser (Volcanus): DOLSharp. "Kill Taur Warlord."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1011-taur-warlord', '1011', 'taur warlord', 0, 'battlegroup', 'Typhon''s Reach');

-- 1012 Ceremonial Bracers (Oceanus): DOLSharp. "Defeat a wavelord."; spawned by pack 071
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1012-wavelord', '1012', 'wavelord', 0, 'battlegroup', 'Mesothalassa');

-- 1013 Cloudsong (Aerus): DOLSharp. "Defeat Eramai."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1013-eramai', '1013', 'Eramai', 0, 'group', 'Green Glades');

-- 1014 Crocodile Tear Ring (Oceanus): DOLSharp. "Defeat Cynere."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1014-cynere', '1014', 'Cynere', 0, 'group', 'Oceanus Boreal');

-- 1015 Crocodile's Tooth Dagger (Stygia): Herald. "the Crocodile's Tooth Dagger encounter ... did Itet drop"
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1015-itet', '1015', 'Itet', 0, 'battlegroup', 'Stygian Delta');

-- 1016 Crown of Zahur (Oceanus): Herald. "Zahur and his minions" (patch notes)
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1016-zahur', '1016', 'Zahur', 0, 'battlegroup', 'Oceanus Boreal');

-- 1017 Cyclops Eye Shield (Aerus): Herald. "Cyclops' Eye ... Kertom would not spawn"
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1017-kertom', '1017', 'Kertom', 0, 'battlegroup', 'Arbor Glen');

-- 1018 Dream Sphere (Stygia): DOLSharp. "Defeat the attackers and end Torih's dream."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1018-torih', '1018', 'Torih', 0, 'battlegroup', 'Stygian Delta');

-- 1019 Eerie Darkness Stone (Oceanus): chosen. no source names a creature; Runihura of Oceanus Boreal, whose temple holds a djinn stone
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1019-runihura', '1019', 'Runihura', 0, 'group', 'Oceanus Boreal');

-- 1020 Egg of Youth (Oceanus): DOLSharp. "Defeat Sililia."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1020-sililia', '1020', 'Sililia', 0, 'battlegroup', 'Oceanus Notos');

-- 1021 Eirene's Hauberk (Oceanus): DOLSharp. "Defeat Linos."; Herald: Linos and Khonorn's half of the key; spawned by pack 071 near Khonorn
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1021-linos', '1021', 'Linos', 0, 'battlegroup', 'Mesothalassa');

-- 1022 Enyalios Boots (Aerus): DOLSharp. "Defeat Behrooz."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1022-behrooz-the-jinni', '1022', 'Behrooz the Jinni', 0, 'battlegroup', 'Arbor Glen');

-- 1023 Erinys Charm (Volcanus): DOLSharp. "Defeat Samut."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1023-samut', '1023', 'Samut', 0, 'battlegroup', 'Typhon''s Reach');

-- 1024 Eternal Plant (Oceanus): DOLSharp. "Defeat Sobekhotep."; Herald: Sobekhotep among swimming sobekites; spawned by pack 071
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1024-sobekhotep', '1024', 'Sobekhotep', 0, 'group', 'Oceanus Boreal');

-- 1025 Fool's Bow (Oceanus): chosen. no source names a creature; Kirkleis of Oceanus Anatole
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1025-kirkleis', '1025', 'Kirkleis', 0, 'group', 'Oceanus Anatole');

-- 1026 Foppish Sleeves (Aerus): chosen. live: the Blood Soaked Kibisis and its snake guardians; Gaspara of Green Glades
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1026-gaspara', '1026', 'Gaspara', 0, 'battlegroup', 'Green Glades');

-- 1028 Golden Spear (Stygia): Herald. "The Golden Spear story encounter bosses Landrine, Hounthro and Jomnaer"
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1028-landrine', '1028', 'Landrine', 0, 'battlegroup', 'Stygian Delta');
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1028-hounthro', '1028', 'Hounthro', 0, 'battlegroup', 'Stygian Delta');
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1028-jomnaer', '1028', 'Jomnaer', 0, 'battlegroup', 'Stygian Delta');

-- 1029 Guard of Valor (Aerus): DOLSharp. "Defeat Danos."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1029-danos', '1029', 'Danos', 0, 'battlegroup', 'Green Glades');

-- 1030 Golden Scarab Vest (Stygia): Herald. the maubastet Tey of the encounter (patch notes)
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1030-tey', '1030', 'Tey', 0, 'group', 'Stygian Delta');

-- 1031 Harpy Feather Cloak (Oceanus): DOLSharp. "Defeat the Harpies."; the one named harpy of Oceanus Notos
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1031-frustrated-harpy', '1031', 'frustrated harpy', 0, 'group', 'Oceanus Notos');

-- 1032 Healer's Embrace (Stygia): DOLSharp. "Defeat Mesedsubastet."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1032-mesedsubastet', '1032', 'Mesedsubastet', 0, 'group', 'Stygian Delta');

-- 1033 Jacina's Sash (Aerus): DOLSharp. "Defeat Cyrek."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1033-cyrek', '1033', 'Cyrek', 0, 'battlegroup', 'Arbor Glen');

-- 1034 Kalare's Necklace (Oceanus): DOLSharp. "Defeat the Warrior of Stone and escape with the necklace."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1034-warrior-of-stone', '1034', 'Warrior of Stone', 0, 'battlegroup', 'Oceanus Notos');

-- 1035 Gem of Lost Memories (Oceanus): Herald. "How often does the Night Terror mob spawn for The Gem Of Lost Memories"
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1035-night-terror', '1035', 'Night Terror', 0, 'group', 'Oceanus Hesperos');

-- 1036 Maddening Scalars (Volcanus): DOLSharp. "Defeat Ylyssan."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1036-ylyssan', '1036', 'Ylyssan', 0, 'battlegroup', 'Typhon''s Reach');

-- 1037 Malice's Axe (Volcanus): DOLSharp. "Defeat Malamis."; Herald: Malamis credits a group as well as a battlegroup; spawned by pack 071
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1037-malamis', '1037', 'Malamis', 0, 'battlegroup', 'Typhon''s Reach');

-- 1038 Nailah's Robes (Stygia): chosen. no source names a creature; Deshrentan of Stygian Delta
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1038-deshrentan', '1038', 'Deshrentan', 0, 'group', 'Stygian Delta');

-- 1039 Night's Shroud Bracelet (Oceanus): DOLSharp. "Defeat Kythera."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1039-kythera', '1039', 'Kythera', 0, 'battlegroup', 'Oceanus Anatole');

-- 1040 Oglidarsh's Belt (Oceanus): chosen. DOLSharp names Snatcher (a copy of the Snatcher quest); Herald: the encounter's sharks. The three lone tame sharks
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1040-tame-shark', '1040', 'tame shark', 0, 'battlegroup', 'Mesothalassa, Oceanus Hesperos or Oceanus Boreal');

-- 1041 Orion's Belt (Oceanus): chosen. Herald pairs it with the Gem of Lost Memories; Peridas of the same Hesperos island
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1041-peridas', '1041', 'Peridas', 0, 'group', 'Oceanus Hesperos');

-- 1042 Phoebus' Harp (Oceanus): Herald. "Kosma, Hallea, and that other one that starts with a Z"; credit for slaying the holder of the harp
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1042-hallea', '1042', 'Hallea', 0, 'battlegroup', 'Mesothalassa');
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1042-kosma', '1042', 'Kosma', 0, 'battlegroup', 'Mesothalassa');
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1042-zoea', '1042', 'Zoea', 0, 'battlegroup', 'Mesothalassa');

-- 1043 Ring of Fire (Volcanus): chosen. DOLSharp: "Win three consecutive rounds of battle with the gladiators."; Siraadi of Typhon's Reach
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1043-siraadi', '1043', 'Siraadi', 0, 'group', 'Typhon''s Reach');

-- 1044 Ring of Unyielding Will (Oceanus): DOLSharp. "Defeat Mordom."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1044-mordom', '1044', 'Mordom', 0, 'group', 'Oceanus Notos');

-- 1045 Scepter of the Meritorious (Oceanus): Herald. "sceptor of meritorious ... before ferenc decides to join the frey"
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1045-ferenc', '1045', 'Ferenc', 0, 'battlegroup', 'Oceanus Boreal');

-- 1046 Scorpion's Tail Ring (Stygia): DOLSharp. "Defeat Terkari."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1046-terkari', '1046', 'Terkari', 0, 'group', 'Stygian Delta');

-- 1047 Shades of Mist (Volcanus): chosen. DOLSharp: "Find the Shades of Mist." Herald: the Long Black Cloak, criosphinxes; Vazul of Ashen Isles
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1047-vazul', '1047', 'Vazul', 0, 'battlegroup', 'Ashen Isles');

-- 1048 Mariasha's Sharkskin Gloves (Oceanus): chosen. Herald: a treasure chest step; the Deep-Chasm Matriarch among the sharks of Oceanus Hesperos
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1048-deep-chasm-matriarch', '1048', 'Deep-Chasm Matriarch', 0, 'group', 'Oceanus Hesperos');

-- 1049 Shield of Khaos (Aerus): DOLSharp. "Defeat Chief Creon."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1049-chief-creon', '1049', 'Chief Creon', 0, 'battlegroup', 'Green Glades');

-- 1050 Snakecharmer's Weapon (Stygia): Herald. "In the Snake Charmer's Weapon encounter, Maedion will move"
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1050-maedion', '1050', 'Maedion', 0, 'group', 'Land of Atum');

-- 1051 Snatcher (Volcanus): DOLSharp. "Defeat Snatcher."; spawned by pack 071
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1051-snatcher', '1051', 'Snatcher', 0, 'battlegroup', 'Ashen Isles');

-- 1052 Staff of the Gods (Oceanus): chosen. DOLSharp: "Obtain the staff ..."; Herald: the jagger sharks of Mesothalassa; the Naxos Tidal Lord there
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1052-naxos-tidal-lord', '1052', 'Naxos Tidal Lord', 0, 'battlegroup', 'Mesothalassa');

-- 1053 Spear of Kings (Oceanus): chosen. Herald: the creature with the spear stands in Oceanus Notos; Seviach
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1053-seviach', '1053', 'Seviach', 0, 'battlegroup', 'Oceanus Notos');

-- 1055 Tartaros' Gift (Volcanus): DOLSharp. "Defeat Akil."
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1055-high-priest-akil', '1055', 'High Priest Akil', 0, 'battlegroup', 'Typhon''s Reach');

-- 1056 Traitor's Dagger (Oceanus): chosen. no source names a creature; Sadri the Traitor of Oceanus Boreal
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1056-sadri-the-traitor', '1056', 'Sadri the Traitor', 0, 'group', 'Oceanus Boreal');

-- 1057 Traldor's Oracle (Oceanus): chosen. no source names a creature; Traldor of Oceanus Boreal
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1057-traldor', '1057', 'Traldor', 0, 'battlegroup', 'Oceanus Boreal');

-- 1058 Winged Helm (Oceanus): Herald. "Winged Helm: Reduced the odds of Hermes healing himself"
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1058-hermes', '1058', 'Hermes', 0, 'battlegroup', 'Oceanus Notos');

-- 1059 Wings Dive (Aerus): Herald. "Keres now drops the Wings Dive artifact"
INSERT OR IGNORE INTO "ArtifactEncounterTarget" ("ArtifactEncounterTarget_ID", "ArtifactID", "MobName", "Region", "CreditMode", "Zone") VALUES ('toa-enc-1059-keres', '1059', 'Keres', 0, 'battlegroup', 'Arbor Glen');
