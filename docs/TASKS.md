# Tasks and Ideas

Capture rapid-fire tasks, feature requests, and ideas here. Include the desired outcome, scope, and any constraints or acceptance checks that are known; leave unknown details explicit rather than inventing requirements. Recording an idea does not authorize implementing the backlog. Bugs belong in [BUGS.md](BUGS.md).

When a task is done and its required verification is complete, move it out of its current section into **Finished** immediately, mark it **Done**, and record a brief result and completion version (or date for tracking-only work). Do not leave completed items in **Open**. If implementation still awaits installation or real-client verification, keep it under **Implemented in source; installation verification pending**, recording the implementation version and outstanding checks, until those checks are complete. Preserve completed entries as history.

## Open

25. **Companion auto-levelling hits the key spell breakpoints.** Aaron's
    level-45 group showed the proportional schedule (`target * level / 50`)
    lands on dead levels: Skald Battlesongs 40 (speed 5 needs 43), Shaman
    Cave 23 (instant AE disease needs 27), Thane Shields 35 (Slam needs 42).
    Players levelled breakpoint-first. Plans get ordered milestones that are
    trained as soon as the character level allows, and automatic companions
    are realigned for free to the corrected schedule. The level-50 end state
    of every plan stays unchanged.

26. **Stun → bomb and owner assist for player-led companions.** A Healer with
    an area stun ready opens on a clump; companion bombers wait briefly for
    the stun, then run into the centre and bomb. Companions assist the
    owner's target in RvR as well as PvE (persistent roster companions
    currently miss the PvP focus logic because `CompanionPvpEngagement.Leader`
    only accepts temporary helpers). Healers and mezzers are a preferred, not
    a forced, target choice.

27. **RvR group doctrine for autonomous warbands.** Every warband derives a
    doctrine from its real composition (bomb group, assist train, melee train,
    stealth pack, caster duo, pickup group with one healer and slow speed,
    zerg/keep raid party, and more; see `docs/RVR_GROUP_DOCTRINE.md`).
    Imperfect groups are normal and still roam and fight. The doctrine drives
    opener, target habits, formation and retreat appetite.

28. **Human-like RvR combat habits.** Bots understand their role rather than
    act perfectly: soft priority for enemy healers and mezzers, per-bot target
    stickiness with occasional switching or scattering, engaging without a
    ready stun, and retreat as a risk-weighted option (a group that sees a
    chance may stay in). Assist trains follow a caller when the doctrine has
    one.

29. **Varied RvR roaming.** Replace the fixed west-to-east camp loop with
    weighted wandering between frontier hotspots (keeps, border keeps,
    frontier camps) that differs by leader personality and doctrine, with
    lingering, scouting and a default doctrine the group falls back on when
    it disagrees. Travel and fight formations follow the group's roles.

30. **Camlann guild cohesion and encounter memory.** Guildmates in the same
    area help each other; groups remember recent encounters (who beat whom,
    who fled) and let that shape grudges and avoidance. PvP happens at every
    group size. Realm-based leftovers in rally/siege/guard-healing code are
    reviewed for Camlann guild ownership.

7. **Investigate the perceived XP slowdown from level 30 with 10x XP.** Check
   the level XP curve and XP awarded at different levels to determine whether
   the slowdown is expected progression or an unintended drop in rewards, then
   document the intended behavior and fix it if needed. Initial read-only check
   on 2026-09-26: installed save settings show `rates.xp_rate=10`; saved
   characters are levels 19 and 36. The server was not running locally, and the
   save has no XP-award history, so it cannot confirm when the slowdown began.

## Implemented in source; installation verification pending

8. **All-class companion builds and caster area damage.** Source 0.96.0
   provides 118 static career/skill-checked plans for all 39 Classic + SI
   classes, at least three per class, with existing defaults preserved. The
   selected automatic build now drives learned specialization, weapon/style,
   spell, pet, and combat-role paths. A saved per-companion ranged-AoE choice
   (Off, 2+ through 8+, default 3+) counts engaged mobs and hostile guards
   belonging to the same hostile keep; a cast is refused if its area would hit
   an idle bystander, unrelated guard, player, illegal target, or protected
   mezz. Necromancer servant area wrappers use that threshold around the
   servant's payload center. Fresh recruits receive plan-aligned starter gear;
   switching an existing companion only activates compatible gear already
   equipped and preserves owner gear and manual locks. Static and runtime-contract
   test coverage was added. CoreServer and the test assembly compiled in Release
   with zero errors; automated tests were not run. Installation and real-client
   verification remain pending: exercise representative builds and learned
   skills, default and saved selection behavior, existing gear and
   locks, AoE thresholds against mobs and same-keep hostile guards, and all
   safety exclusions. See [the implementation and acceptance plan](COMPANION_BUILD_AOE_PLAN.md).

24. **Companions rebuff far too often.** Reported by Aaron on 0.92.0. Source
    0.93.0: player-led companions keep only long buffs (5 min or longer, or
    concentration) up out of combat and refresh them in their last minute;
    short buffs are not maintained out of combat; speed only while traveling;
    player-led Skalds sing only the speed song while the group travels.
    Real-client check pending: buff frequency and power use after a pull.

23. **Companion inventory like your own.** Source 0.92.0 adds worn slots
    (positions 1-19) and the backpack (21-60) to the companion inventory
    window with drag-to-equip from the owner's backpack, **[Info]** and a
    slimmer slot view in the Gear tab, and manual choices that give way only to
    clearly better earned loot. Real-client checks pending: vault grid
    columns and ring/wrist pairs side by side, dragging from your backpack
    onto a worn slot, the **[Info]** window, and the swap chat line.

22. **Companions rest with a sitting player.** Source 0.90.0 lets roster
    companions start fast recovery for any missing health, power, or endurance
    while their player-leader sits, and finish to full. Before, they rested only
    below 70% health, 45% power, or 35% endurance and otherwise regenerated at
    standing speed. Travel and combat still interrupt the rest. Real-client
    check that casters refill during a sit is pending.

21. **Remove level and ability prerequisites from realm ability training.** Source
    0.94.0 removes character-level gates and ignores ability-specific
    prerequisites in the trainer; a level 42 character can train Mastery of
    Pain without Augmented Dexterity II. Realm Point costs, maximum ranks, and
    class availability remain; RR5 abilities still unlock at Realm Level 40.
    Installation and real-client checks of low-level training and ability effects
    are pending.

20. **Remove the player logout timer.** Source 0.85.0 completes accepted player logout immediately, including during combat and while moving, without changing the dead, mounted, crafting, or instance restrictions. `/stuck` still uses its safe position on successful logout and clears the request if logout is refused. Installation and real-client checks of combat logout, normal logout, and `/stuck` remain pending.

19. **Enable companion area taunt.** Source 0.84.0 lets persistent Armsman, Hero, and Warrior companions cast their learned Taunting Shout when at least two attackable NPCs in the frontal cone are already fighting the group. The AI skips cones containing idle NPCs or protected mezzes and leaves single-target taunts in place. Installation and real-client checks of cone targeting, threat transfer, cooldown, and mezz/idle safety remain pending.

18. **Give group members buff priority over pets.** Source 0.83.0 selects missing buffs for eligible group members before pet buffs across routine upkeep and fallback selection. Realm buffs choose members before attached pets, while pet-only and spare pet coverage remain available once member needs are met. Installation and real-client checks with mixed player/companion/pet groups remain pending.

17. **Prioritize Shaman endurance buffs for the whole group.** Source 0.82.0 selects the highest learned endurance rank before other routine Shaman buffs, covers nearby group members before pets or other allies, and frees concentration from a lower priority buff when needed for a group member. Installation and real-client checks with Kiri and full autonomous groups remain pending, including coverage after travel and combat.

14. **Companion bombers open a pull with one loose volley.** Implemented in source 0.81.0. Requested so a group's bombs land together for maximum damage without constant re-syncing or cut casts. Result: the first companion bomber in position holds its first bomb for at most 1.2 s until the group's other companion bombers within 1,500 units are in position; then all fire and chain freely for 6 s after each bomb, so a new pull syncs again. Holding never starts while a cast is running; human bombers are not waited for. Unit tests cover the hold, release, chain and stale-wait cases; real-client check of volley timing pending.

13. **Join Friend remembers the connection details and, optionally, the password.** Implemented in source 0.77.0. Requested so guests do not retype everything each session. Result: the form pre-fills host address, host account and guest account from `%LOCALAPPDATA%\OfflineDAoC\join-friend.json`; "Remember my password" stores it encrypted with Windows DPAPI for the current Windows user (unreadable for other users or PCs), and unticking it removes the stored password. Details are saved after the client starts. Launcher tests cover the round trip, the absence of plaintext on disk and damaged files. Real-client check of pre-fill and rejoin pending.

12. **Diagnose recruitment eligibility and repair separated-party coordination at the existing population.** Source 0.75.0 adds exclusive PvP rejection categories, late-recruitment failure categories and throttled travel-hold diagnostics; repairs missing-zone town lookup, distant-combat travel holds, final-camp-ahead crossing waits and single-seat backfilling. No population setting was changed. The matched 0.74.0 observation showed 1,974 zero-eligible attempts versus 22 eligible-but-no-route failures, camp-selection endings down from twelve to three, and full eight-member PvE assemblies down from seven to one. These are uncontrolled observations. Exact exclusion causes and the complete explanation for smaller PvE parties need the new logs; changes await installation and real-client verification. Server and Windows launcher Release builds passed with zero errors; whitespace, conflict-marker, line-ending and version-pin checks passed. Automated tests were skipped under the project rules.

11. **Follow up on the 0.73.0 runtime observation.** Source 0.74.0 keeps initial PvP recruitment open for ten simulated minutes, advertises partial guild parties at new task boundaries without reversing coordinator/allocation locks, removes the eight-waiter invitation cutoff, rotates failed late-recruitment route probes, recalculates missing class roles, and rechecks the planned PvE camp before admitting late recruits. Diagnostics now separate candidate/route counts and capture camp-distance/movement state at group endings. Server and Windows launcher Release builds passed with zero errors; version, conflict-marker and line-ending checks passed. Automated tests were skipped under the project rules. Installation and gameplay acceptance remain pending. Pending: compare full PvP arrivals, healer/tank composition, camp-selection failures and travel expirations after deployment; the thirteen observed travel timeouts are not claimed fixed.

10. **Larger autonomous parties and viable guild assaults.** Source 0.73.0 fills spare seats during a bounded initial recruitment window, permits route-validated cross-region guild PvP invitations, favors full organized warbands, selects class support, and coordinates eligible guildmates only at new task boundaries. New keep assaults require eight members, healing and siege supplies. PvE remote recruitment can fill all seven follower seats. Installation and sustained high-population verification remain pending: compare party-size distribution, actual meetup arrivals, class composition, keep attendance and captures, routing cost, and small-population fallback. Existing tasks, solo-oriented types, guild hostility and eight-member capacity remain authoritative.

9. **Frontier PvE incentives and NPC-held keeps/relics.** Source version 0.72.0 adds +50% base monster XP in Old Frontiers/DF, on top of the configured rate; restores a manifest of 4,851 archived frontier spawns; and adds stationary Frontier Wardens garrisons, lord-unlocked claim stewards, 25 base guard RP and 1,500 base capture RP (30-minute reward cooldown per keep). Autonomous crews explicitly claim for their guild using native permissions and group checks. Fresh/reset worlds use NPC ownership; captured territory persists across restarts and existing guild claims are preserved. Server, launcher and Setup build verification are recorded in [FRONTIER_CAMPAIGN.md](FRONTIER_CAMPAIGN.md). Installation, backed-up spawn migration, world density/navigation, human/crew capture, rewards, restart persistence and relic raids await owner verification.

1. **Private Tailscale co-op between two home installations.** The launcher now
   has a client-only Join Friend shortcut with a Tailscale IPv4 login-port check,
   separate host and guest account names, and remote-session diagnostics. Source
   version: 0.60.0. Still needs host-side private network setup and the real
   two-home login, region transition, group, reconnect, save persistence, and
   reversed-host checks in [TAILSCALE_COOP.md](TAILSCALE_COOP.md).

2. **Autonomous dungeon activity, Darkness Falls and Camlann PvP behavior.** Implemented in source 0.61.0 after the owner's explicit DF implementation approval. DF now has a bounded entrance-stair repair, 1,417 proved spawn destinations and extra weight within the dungeon share. Hunters can patrol dungeons; ordinary pickup groups can choose connected dungeon camps. PvE aggression bypasses, patrol dwell timing and misleading RvR launcher labels are corrected. Source 0.79.0 modestly raises DF destination weight from two to three for eligible XP camps and Hunter PvP patrols, without changing the overall dungeon share. Awaiting installation, all-entrance client traversal and live PvE/PvP balance checks in [CAMLANN_PVP_REVIEW.md](CAMLANN_PVP_REVIEW.md).

3. **Population slider and Danger tooltips.** Expanded in source 0.62.0: all six type labels, sliders and percentage inputs explain the behavior, relevant levels and the effect on new versus existing bots; Danger explains its effect on existing Hunters after a restart. Source 0.70.0 further distinguishes saving new-bot weights from applying a live mix to the saved roster. Installation and launcher hover verification pending.

4. **Focus staff selection for casters.** Source 0.65.0 ranks earned and owned staves by the focus levels covering learned spell lines, including all-lines focus, before general item value. Installation and real-client inspection of caster loadouts and power use remain pending.

5. **Realm Points in Active Population.** Source version 0.67.0 adds a sortable Realm Points column using live points for active autonomous bots and saved points otherwise. Companions without a Realm Points record show a dash. Launcher display and live point updates await installation verification.

6. **Active Groups for large populations.** Source version 0.68.0 replaces the per-group card stack with a sortable, realm-filterable table. Search covers group, bot, class, zone, and task details; selecting a row shows member roles, locations, and live timers. Launcher installation and inspection with a large list (including the reported 82-group case) remain pending.

7. **Bard songs useful during combat.** Source version 0.69.0 keeps grouped Bards with an endurance song on their instrument during combat, preserves the endurance pulse, and lets them use the existing group-support actions. Mana and speed songs resume after combat; solo Bard behavior is unchanged. Installation and real-client verification of endurance upkeep alongside healing and control remain pending.

8. **Apply population-type changes to existing bots live.** Source version 0.70.0 adds a launcher action and server request/status flow for the entire non-retired saved autonomous roster, including offline bots. Player-led companions and temporary helpers are excluded. Offline changes run in bounded batches; active changes wait for safe task or group boundaries. The server saves the requested mix and bot types, and reports pending/applied/failed status. Installation and live acceptance remain pending: verify counts converge, active groups stay intact, repeated application is stable, and settings plus character progress survive restart.

## Finished

3. **Done — Check XP and Realm Point rewards for `/companions` group members**
   (0.83.1, 2026-09-26). Source audit confirms persistent companions gain XP
   only from eligible NPC kills and gain no Realm Points. Both human-victim and
   autonomous-bot-victim PvP reward paths exclude persistent companions, even
   when they deal damage. The player receives PvP XP and Realm Points only for
   qualifying player or player-controlled-pet damage; companion damage does not
   become owner kill credit. Thus PvP kills can advance the player while leaving
   companions at their prior level, consistent with the reported level 17 versus
   13 gap after two kills. No pre-kill XP snapshots were available to quantify
   those two awards; real-client reward amounts were not measured. The intended
   reward boundary is documented in [COMPANION_BOTS.md](COMPANION_BOTS.md).

2. **Done — Thirty-minute observation of installed 0.73.0** (2026-09-26). Captured 61 snapshots at 600-bot peak population: seven PvE assemblies had eight members physically present; nine eight-member PvE rosters began tasks. PvP task starts remained two to four members and no keep ownership changed. Thirteen parties hit the camp-travel deadline and twelve found no usable camp. No observed freeze or empty-party exception recurrence; all samples had zero connected clients. Evidence supports partial PvE recruitment success, not a controlled before/after performance improvement. Raw logs and the detailed report remain outside Git; broader feature acceptance stays pending.

1. **Done — Review current Camlann bot setup and running-session activity** (2026-09-25). Confirmed the PvP ruleset, inspected type/charter meaning, sampled 600 active bots and recorded combat/activity aggregates without changing the running installation. Findings and offline navigation evidence are in [CAMLANN_PVP_REVIEW.md](CAMLANN_PVP_REVIEW.md). Implementation acceptance remains in the pending item above; camp-travel expirations remain in the bug tracker.
