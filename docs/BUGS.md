# Known Bugs

Track confirmed, unresolved bugs here. Include the affected version, steps to reproduce, expected and actual behavior, impact, and any workaround. Fixes are handled in a separate task unless noted below. When a bug is fixed and its required verification is complete, move it out of its current section into **Finished** immediately, recording a brief resolution and version. Do not leave completed items in **Open**. If a source fix still awaits installation or real-client verification, keep it under **Fixed in source; installation verification pending** until that check is complete.

## Open

None.

## Fixed in source; installation verification pending

1. **Refresh buffs after upgrades.** Companion buff maintenance compares active buff strength and allows a stronger rank to replace a weaker effect. Source fix: 0.49.0; installation and gameplay verification pending.
2. **Prioritize specialization buffs while covering base buffs.** Player-led companions prefer specialization-line buffs. A base buff is skipped only while another live group member has an equal or stronger compatible buff active on the same target; a human player's known spell alone does not count. Source fixes: 0.49.0 and 0.52.0; installation and gameplay verification pending.
3. **Automatically use Guard and Protect intelligently.** Companion protection assignments distribute Guard and Protect across uncovered group members, prioritize healers and bomb casters, and respect the native ranges (256 and 1,000 units). Existing effects reserve coverage only while their source remains in range. Source fixes: 0.49.0 and 0.52.0; installation and gameplay verification pending.
4. **Update and choose summoned pets.** Idle player-led companions upgrade to stronger learned summons, and Enchanters prefer Underhill Ally when available. Repeating the same summon is allowed only after the owner levels enough to improve that pet. Source fixes: 0.49.0, 0.51.0, and 0.52.0; installation and gameplay verification pending.
5. **Use bomb spells and coordinate bomb groups.** Eligible player-led casters prioritize PBAoE spells on sufficiently large focused pulls. The Companion Manager saves an Auto/Bomb/Off preference per companion, and bombing waits up to 2.5 seconds for tank aggro, restarting that wait for each newly focused target. Source fixes: 0.49.0 and 0.52.0; installation and gameplay verification pending.
6. **Make mobs form groups and award group bonuses.** Mob BAF now resolves companion pullers and controlled pets to their player-led group, counts companion members for add selection, and preserves the existing add-based experience bonus. Source fix: 0.49.0; installation and gameplay verification pending.
7. **Explain the Server population controls.** Preset, type-mix, danger, and world-shape controls now have plain-language tooltips. Source fix: 0.49.0; launcher installation and hover verification pending.

8. **Dungeon mobs were missing and populations were thin.** A read-only audit of all 29 supported dungeon zones found 2,310 levelled neutral mob records archived by the Classic 1.65 population profile and absent from the current world database. The new Setup migration restores the exact archived rows for all 15 Classic realm dungeons and four supported Old Frontiers dungeons. Shrouded Isles and Darkness Falls rows were already restored. The [2002 map compilation](https://www.scribd.com/document/144573276/DAOC-Map-Compilation-Book) and [Prima atlas](https://www.scribd.com/document/131856275/Dark-Age-of-Camelot-the-Atlas-Prima) document period dungeon layouts and rosters; the archived world records provide the numeric spawn baseline. This source fix is version 0.48.0 and still needs deployment and real-client verification.

## Finished

None.
