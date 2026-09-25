# Tasks and Ideas

Capture rapid-fire tasks, feature requests, and ideas here. Include the desired outcome, scope, and any constraints or acceptance checks that are known; leave unknown details explicit rather than inventing requirements. Recording an idea does not authorize implementing the backlog. Bugs belong in [BUGS.md](BUGS.md).

When a task is done and its required verification is complete, move it out of its current section into **Finished** immediately, mark it **Done**, and record a brief result and completion version (or date for tracking-only work). Do not leave completed items in **Open**. If implementation still awaits installation or real-client verification, keep it under **Implemented in source; installation verification pending**, recording the implementation version and outstanding checks, until those checks are complete. Preserve completed entries as history.

## Open

2. **Add a Realm Points column to the launcher's Active Population table.** Show each listed bot's current Realm Points alongside its existing population details. Requested from the Active Population screenshot; implementation has not started.
3. **Rework the Active Groups window for large group lists.** The window takes a long time to load when many groups are present, and scrolling through the groups is inconvenient. Improve loading and make large lists easier to navigate. The screenshot shows the current card-based view with 82 groups; the exact cause and preferred navigation design have not been determined.


4. **Apply population-type changes to existing bots live.** Add an explicit
   "Apply mix to existing bots" launcher action so the six type percentages
   can rebalance the current autonomous population without restarting. This is
   one contained task; no separate roadmap. Recorded 2026-09-25; implementation
   has not started.

   Implementation checklist:
   - Send validated settings from the launcher to the running server and show
     whether the rebalance is pending, applied, or failed.
   - Reassign enough existing autonomous bots to approach the requested mix,
     minimizing unnecessary type changes. Define whether the target counts
     cover the active population or the entire saved roster before implementing.
   - Apply behavior changes at safe task boundaries after combat; handle active
     group tasks without abruptly breaking parties or leaving stale assignments.
   - Preserve character identity, levels, equipment, inventories, money, guild
     membership and progress. Exclude player-led companions and temporary helpers.
   - Persist both settings and reassigned types consistently; make repeated
     application of the same mix stable and update launcher help accordingly.

   Acceptance: change the mix while the server runs, observe the type counts
   approach the requested percentages as pending tasks finish, verify safe
   combat/group transitions, and confirm the new mix and character progress
   survive a restart. Saving creation weights and explicitly rebalancing existing
   bots must have clear, distinct effects.

## Implemented in source; installation verification pending

1. **Private Tailscale co-op between two home installations.** The launcher now
   has a client-only Join Friend shortcut with a Tailscale IPv4 login-port check,
   separate host and guest account names, and remote-session diagnostics. Source
   version: 0.60.0. Still needs host-side private network setup and the real
   two-home login, region transition, group, reconnect, save persistence, and
   reversed-host checks in [TAILSCALE_COOP.md](TAILSCALE_COOP.md).

2. **Autonomous dungeon activity, Darkness Falls and Camlann PvP behavior.** Implemented in source 0.61.0 after the owner's explicit DF implementation approval. DF now has a bounded entrance-stair repair, 1,417 proved spawn destinations and extra weight within the dungeon share. Hunters can patrol dungeons; ordinary pickup groups can choose connected dungeon camps. PvE aggression bypasses, patrol dwell timing and misleading RvR launcher labels are corrected. Awaiting installation, all-entrance client traversal and live PvE/PvP balance checks in [CAMLANN_PVP_REVIEW.md](CAMLANN_PVP_REVIEW.md).

3. **Population slider and Danger tooltips.** Expanded in source 0.62.0: all six type labels, sliders and percentage inputs explain the behavior, relevant levels and the effect on new versus existing bots; Danger explains its effect on existing Hunters after a restart. Tests and builds skipped as requested. Installation and launcher hover verification pending.

## Finished

1. **Done — Review current Camlann bot setup and running-session activity** (2026-09-25). Confirmed the PvP ruleset, inspected type/charter meaning, sampled 600 active bots and recorded combat/activity aggregates without changing the running installation. Findings and offline navigation evidence are in [CAMLANN_PVP_REVIEW.md](CAMLANN_PVP_REVIEW.md). Implementation acceptance remains in the pending item above; camp-travel expirations remain in the bug tracker.
