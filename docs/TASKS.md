# Tasks and Ideas

Capture rapid-fire tasks, feature requests, and ideas here. Include the desired outcome, scope, and any constraints or acceptance checks that are known; leave unknown details explicit rather than inventing requirements. Recording an idea does not authorize implementing the backlog. Bugs belong in [BUGS.md](BUGS.md).

When a task is done and its required verification is complete, move it out of its current section into **Finished** immediately, mark it **Done**, and record a brief result and completion version (or date for tracking-only work). Do not leave completed items in **Open**. If implementation still awaits installation or real-client verification, keep it under **Implemented in source; installation verification pending**, recording the implementation version and outstanding checks, until those checks are complete. Preserve completed entries as history.

## Open

6. **Check XP and Realm Point rewards for `/companions` group members.** Verify
   whether companions receive the same XP and Realm Points as their player for
   PvP kills, and explain any differences in reward distribution. Observation:
   after two PvP kills, the player was level 17 while companions were level 13;
   everyone started at level 1.

## Implemented in source; installation verification pending

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

2. **Autonomous dungeon activity, Darkness Falls and Camlann PvP behavior.** Implemented in source 0.61.0 after the owner's explicit DF implementation approval. DF now has a bounded entrance-stair repair, 1,417 proved spawn destinations and extra weight within the dungeon share. Hunters can patrol dungeons; ordinary pickup groups can choose connected dungeon camps. PvE aggression bypasses, patrol dwell timing and misleading RvR launcher labels are corrected. Awaiting installation, all-entrance client traversal and live PvE/PvP balance checks in [CAMLANN_PVP_REVIEW.md](CAMLANN_PVP_REVIEW.md).

3. **Population slider and Danger tooltips.** Expanded in source 0.62.0: all six type labels, sliders and percentage inputs explain the behavior, relevant levels and the effect on new versus existing bots; Danger explains its effect on existing Hunters after a restart. Source 0.70.0 further distinguishes saving new-bot weights from applying a live mix to the saved roster. Installation and launcher hover verification pending.

4. **Focus staff selection for casters.** Source 0.65.0 ranks earned and owned staves by the focus levels covering learned spell lines, including all-lines focus, before general item value. Installation and real-client inspection of caster loadouts and power use remain pending.

5. **Realm Points in Active Population.** Source version 0.67.0 adds a sortable Realm Points column using live points for active autonomous bots and saved points otherwise. Companions without a Realm Points record show a dash. Launcher display and live point updates await installation verification.

6. **Active Groups for large populations.** Source version 0.68.0 replaces the per-group card stack with a sortable, realm-filterable table. Search covers group, bot, class, zone, and task details; selecting a row shows member roles, locations, and live timers. Launcher installation and inspection with a large list (including the reported 82-group case) remain pending.

7. **Bard songs useful during combat.** Source version 0.69.0 keeps grouped Bards with an endurance song on their instrument during combat, preserves the endurance pulse, and lets them use the existing group-support actions. Mana and speed songs resume after combat; solo Bard behavior is unchanged. Installation and real-client verification of endurance upkeep alongside healing and control remain pending.

8. **Apply population-type changes to existing bots live.** Source version 0.70.0 adds a launcher action and server request/status flow for the entire non-retired saved autonomous roster, including offline bots. Player-led companions and temporary helpers are excluded. Offline changes run in bounded batches; active changes wait for safe task or group boundaries. The server saves the requested mix and bot types, and reports pending/applied/failed status. Installation and live acceptance remain pending: verify counts converge, active groups stay intact, repeated application is stable, and settings plus character progress survive restart.

## Finished

2. **Done — Thirty-minute observation of installed 0.73.0** (2026-09-26). Captured 61 snapshots at 600-bot peak population: seven PvE assemblies had eight members physically present; nine eight-member PvE rosters began tasks. PvP task starts remained two to four members and no keep ownership changed. Thirteen parties hit the camp-travel deadline and twelve found no usable camp. No observed freeze or empty-party exception recurrence; all samples had zero connected clients. Evidence supports partial PvE recruitment success, not a controlled before/after performance improvement. Raw logs and the detailed report remain outside Git; broader feature acceptance stays pending.

1. **Done — Review current Camlann bot setup and running-session activity** (2026-09-25). Confirmed the PvP ruleset, inspected type/charter meaning, sampled 600 active bots and recorded combat/activity aggregates without changing the running installation. Findings and offline navigation evidence are in [CAMLANN_PVP_REVIEW.md](CAMLANN_PVP_REVIEW.md). Implementation acceptance remains in the pending item above; camp-travel expirations remain in the bug tracker.
