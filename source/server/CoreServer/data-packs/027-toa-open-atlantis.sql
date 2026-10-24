-- World data pack 027-toa-open-atlantis
-- Opens Atlantis to players (atlantis_teleport_plvl 4 or 2 -> 1) and sets rog_toa_item_chance 0 -> 25, the ToA bonus chance of random loot inside Atlantis.
-- Built by tools/toa/build_data_packs.py from tools/toa/sql. Do not hand-edit:
-- the server applies a pack once per save, so a fix ships as a new pack.
-- Applied by the server in one transaction (docs/TOA_DATA_PACKS.md).

-- ======== part: 027-toa-open-atlantis.sql ========

-- Trials of Atlantis, WP1.2 and WP1.3 (docs/TOA_TRAVEL.md). Hand-written, SQLite.
--
-- Opens Atlantis to players and gives random loot inside Atlantis its ToA bonuses on saves that the
-- setup tool seeded with the 1.65 values. A value in ServerProperty overrides the code default, and
-- the server reads properties before the packs run; the data-pack runner reloads the properties when
-- a pack changes ServerProperty rows, so these values hold from the first boot with this pack.
-- Only the seeded values change: an owner's own choice (atlantis_teleport_plvl 1, or a
-- rog_toa_item_chance other than 0) is kept. Idempotent: a second run changes nothing.

-- atlantis_teleport_plvl: the setup tool wrote 4 ("No player account can enter Atlantis"), the code
-- default was 2 (GM). 1 lets every player account use the porters' Atlantis lines.
UPDATE ServerProperty
   SET Value = '1', DefaultValue = '1',
       Description = 'Minimum account privilege level for the porters'' Atlantis lines (1 = every player, 4 = nobody).'
 WHERE "Key" = 'atlantis_teleport_plvl' AND CAST(Value AS INTEGER) > 1;

-- rog_toa_item_chance: the setup tool wrote 0. Only Trials of Atlantis items roll it, and random loot
-- is a ToA item only for kills inside Atlantis (toa_loot_in_atlantis), so 25 changes nothing outside.
UPDATE ServerProperty
   SET Value = '25', DefaultValue = '25',
       Description = 'Chance in % that a bonus of a Trials of Atlantis random item (loot from kills inside Atlantis) is a ToA cap bonus'
 WHERE "Key" = 'rog_toa_item_chance' AND Value = '0';
