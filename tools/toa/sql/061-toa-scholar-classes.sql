-- 061-toa-scholar-classes: the artifact NPCs work (WP3.1b). Hand-written; every statement is guarded.
--
-- The 26 Scholars (DOL.GS.Scholar), 5 Artifact Scholars (DOL.GS.ArtifactScholar) and the two
-- Aphaestias in 70/71 (DOL.GS.ArtifactCreditMerchant, list artifact_credits) of pack 020 already
-- name their classes; those exist in GameServer now and need no row change.
--
-- * Aphaestia in region 72 (Hibernia) was a GameBountyMerchant without a list (gaheris 35
--   section 2, not shipped): she becomes the credit merchant with the same list as in 70/71.
-- * Artifact.Credit of 22 artifacts did not match their credit token. Tokens match by item ID,
--   Id_nb = lower(Credit) || '_credit' (the server compares without case). Each Credit is set
--   to the stem of the one token whose item name is that artifact's (checked by name on the
--   dev snapshot, e.g. 1002 Arms of the Winds: 'arms_of_the_winds_credit').
-- * Five tokens on the list belong to artifacts this save does not have (Atlantis Tablet,
--   Flamedancer's Boots, Goddess Necklace, Ring of Dances, Stone of Atlantis). They come off
--   the list so nobody pays bounty points for knowledge that leads nowhere; the item
--   templates stay.

UPDATE "Mob" SET "ClassType" = 'DOL.GS.ArtifactCreditMerchant', "ItemsListTemplateID" = 'artifact_credits'
WHERE "Name" = 'Aphaestia' AND "Region" = 72 AND "ClassType" = 'DOL.GS.GameBountyMerchant';

UPDATE "Artifact" SET "Credit" = CASE "ArtifactID"
    WHEN '1002' THEN 'arms_of_the_winds'
    WHEN '1006' THEN 'battler'
    WHEN '1009' THEN 'BraceletofZoarkat'
    WHEN '1011' THEN 'bruiser'
    WHEN '1014' THEN 'croc_ring'
    WHEN '1015' THEN 'crocodiles_tooth'
    WHEN '1017' THEN 'cyclops_eye_shield'
    WHEN '1028' THEN 'golden_spear'
    WHEN '1029' THEN 'guard_of_valor'
    WHEN '1030' THEN 'golden_scarab_vest'
    WHEN '1035' THEN 'gem_of_lost_memories'
    WHEN '1037' THEN 'malice'
    WHEN '1038' THEN 'nailahs_robes'
    WHEN '1040' THEN 'belt_of_oglidarsh'
    WHEN '1045' THEN 'scepter_of_the_meritorious'
    WHEN '1046' THEN 'scorpions_tail'
    WHEN '1047' THEN 'shades_of_mist'
    WHEN '1048' THEN 'mariashas_sharkskin_gloves'
    WHEN '1050' THEN 'snakecharmers_weapon'
    WHEN '1052' THEN 'staff_of_the_god'
    WHEN '1053' THEN 'spear_of_kings'
    WHEN '1056' THEN 'traitors_dagger'
  END
WHERE ("ArtifactID", "Credit") IN (VALUES
    ('1002', 'Fish_Scales'), ('1006', 'Bane_of_Battler'), ('1009', 'Bracer_Of_Zoarkat'), ('1011', 'Tale_of_Bruiser'),
    ('1014', 'Crocodile_Tear_Ring'), ('1015', 'CrocToothDagger'), ('1017', 'Cyclops_Eye'), ('1028', 'GoldenSpear'),
    ('1029', 'GOV'), ('1030', 'GSV'), ('1035', 'Lost_Memories'), ('1037', 'MaliceAxe'), ('1038', 'Nailah_Robe'),
    ('1040', 'Oglidarshs_Belt'), ('1045', 'Scepter'), ('1046', 'Scorpions_Tail_Ring'), ('1047', 'Shade_of_Mist'),
    ('1048', 'Sharkskin_Gloves'), ('1050', 'Snakecharmers'), ('1052', 'SOTG'), ('1053', 'SoK'), ('1056', 'TraitorsDagger'));

DELETE FROM "MerchantItem"
WHERE "ItemListID" = 'artifact_credits'
  AND lower("ItemTemplateID") IN ('atlantis_tablet_credit', 'flamedancers_boots_credit', 'goddess_necklace_credit',
                                  'ring_of_dances_credit', 'stone_of_atlantis_credit');
