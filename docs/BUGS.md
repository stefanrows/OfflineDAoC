# Known Bugs

Track confirmed, unresolved bugs here. Include the affected version, steps to reproduce, expected and actual behavior, impact, and any workaround. Fixes are handled in a separate task unless noted below.

## Open

1. **Refresh buffs after upgrades.** Buff-support companions should recognize when they learn a higher-level version of an existing buff and refresh that buff on affected allies.
2. **Prioritize specialization buffs while covering base buffs.** Classes with only base buffs should cast them; this is believed to work already and should be verified. When a companion has specialization buffs, it should primarily apply those, while covering base buffs only when no other group member can provide them.
3. **Automatically use Guard and Protect intelligently.** Tank and shield companions with these abilities currently do not assign them automatically. Choose protected allies based on group needs, including bomb casters and healers who need protection from aggro. When no healer, caster, or other high-priority ally needs coverage, tanks should still protect or guard another suitable group member, such as each other.
4. **Update and choose summoned pets.** Pet classes should summon a newly available higher-level pet after learning it or leveling up. Enchanters should prefer Underhill Ally when available because it heals, or expose a pet choice for each companion in the Companion Manager.
5. **Use bomb spells and coordinate bomb groups.** Spiritmaster, Ice Wizard, Mana Enchanter, and Eldritch companions specced for bomb currently use ranged nukes instead of their bomb spells. Add a per-companion bomb spec choice in the Companion Manager, and coordinate the group to wait briefly for tanks to establish aggro before bombing a sufficiently large pull.
6. **Make mobs form groups and award group bonuses.** Even with a large player/bot group, mobs currently do not form groups as on a normal server and do not provide the related group bonuses.
7. **Explain the Server population controls.** The screenshot shows no tooltips explaining Preset, the Leveler/Casual/Hybrid/Hunter/Roamer/Keep warrior mix, Danger in leveling zones, and World shape. Add plain-language tooltips so players understand each setting and its effect.

## Fixed in source; installation verification pending

8. **Dungeon mobs were missing and populations were thin.** A read-only audit of all 29 supported dungeon zones found 2,310 levelled neutral mob records archived by the Classic 1.65 population profile and absent from the current world database. The new Setup migration restores the exact archived rows for all 15 Classic realm dungeons and four supported Old Frontiers dungeons. Shrouded Isles and Darkness Falls rows were already restored. The [2002 map compilation](https://www.scribd.com/document/144573276/DAOC-Map-Compilation-Book) and [Prima atlas](https://www.scribd.com/document/131856275/Dark-Age-of-Camelot-the-Atlas-Prima) document period dungeon layouts and rosters; the archived world records provide the numeric spawn baseline. This source fix is version 0.48.0 and still needs deployment and real-client verification.
