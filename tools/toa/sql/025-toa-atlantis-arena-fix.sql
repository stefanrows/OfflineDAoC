-- Trials of Atlantis, TOA_PLAN pack set 2 (docs/TOA_DATA_PACKS.md). Hand-written, SQLite.
--
-- Removes the three Atlas "fight club" Mordreds (DOL.GS.LordOfBattle,
-- scripts/customnpc/LordOfBattle.cs) from the Albion Atlantis copy. The NPC pulls every
-- player within 7,000 units back to itself. These rows stood in zones that were
-- unreachable before the Atlantis packs; now they sit in the middle of the hunting
-- grounds (decision 2026-10-24, TOA_PLAN):
--   region 73, Arbor Glen      (430310, 694470), 11 gaheris mobs inside the radius
--   region 73, Typhon's Reach  (522262, 557174), 81 gaheris mobs inside the radius
--   region 80, Temple of Twilight (61182, 18688), 14 temple mobs inside the radius
-- The Mordreds in regions 334 and 339 stay. No other table references these rows.
--
-- Deleted by Mob_ID and ClassType, so nothing else can match. Idempotent.
DELETE FROM Mob
 WHERE ClassType = 'DOL.GS.LordOfBattle'
   AND Mob_ID IN ('9fe77411-16e8-483a-8378-4ff2b9269b7c',
                  'b7b1994d-cf79-4dcf-b2bd-947e0c3a0036',
                  '824cd725-fe7e-4b64-b88a-e14c934a9b06');
