# Upstream adaptation plan for Camlann PvP

Date: 2026-10-09. Planning task 89 is complete. The owner authorized work
through this roadmap under task 90 on 2026-10-09, with Luna MAX implementation
agents and SOL 6.1 High final review. This authorizes source implementation,
audits and build checks. On 2026-10-09 the owner additionally authorized focused
regression tests and quest migration dry-run/apply/rerun/rollback checks in
isolated development copies, including test-server initialization if needed.
The owner subsequently authorized shipping 0.214.1 on 2026-10-09: build,
push to the fork, stop this installation's verified components and deploy
server/launcher outputs. This does not authorize restarting the installed
server/client, applying the quest pilot to its save or enabling travel threats.

Adapt selected improvements from `shadowofze/OfflineDAoC` while preserving
our Camlann world, existing progress, and companion systems. Start with the
isolated server and navigation fixes, then combat consistency, then larger
gameplay and content changes. Other forks are outside this plan.

## Comparison baseline

- Fork: gameplay version 0.209.0, commit
  `e00d3bca` (2026-10-04). The planning-only version is 0.209.1.
- Upstream: commit `c8b0dca0428666fbe2c6f68d0e4d0363b0d03caa`
  (2026-10-09), covering the 0.35 and 0.35b release work.
- [Upstream changelog at the reviewed revision](https://github.com/shadowofze/OfflineDAoC/blob/c8b0dca0428666fbe2c6f68d0e4d0363b0d03caa/CHANGELOG.md).

Use these revisions to identify changes. Before implementing each stage,
compare its current fork code again: fixes may already have landed independently.
New upstream commits need a separate delta review before joining the scope.
Port focused changes and dependencies into our implementation; do not merge
upstream main or replace whole bot, rules, launcher, or persistence files.

## Camlann rules that every stage must preserve

[CAMLANN.md](CAMLANN.md) remains authoritative, including its later owner
decisions and documented exceptions. In particular:

- Realm describes race, class and equipment identity; hostility follows our
  group, guild and battlegroup rules, safe areas, immunity and existing exceptions.
  Check same-realm enemies and cross-realm allies separately.
- Keeps and relics follow guild ownership. Guild claims remain unlimited;
  automatic relic raids remain off. Battlegrounds remain closed.
- Preserve open cross-realm travel, safe capitals and portal hubs, dangerous
  levelling zones, and the current rules for player-shaped kill rewards.
- Distinguish humans, persistent companions, temporary helpers, autonomous
  world bots, controlled pets, and ordinary monsters before changing behavior.
- Preserve earned progress, inventories, coins, upgrades, Realm Exchange,
  companion orders, pet-pull behavior, and existing save serialization.
- Gameplay deadlines use our simulated clock; logging and operational deadlines
  use real time. Imported timers must work at 1x and accelerated world speed.
- Keep the current navigation meshes and client patch hash guards. A source
  fix does not authorize a mesh replacement, native patch or runtime upgrade.

## Stages and dependencies

| Stage | Deliverable | Dependency | Relative scope |
| --- | --- | --- | --- |
| 1 | Closed console input stops consuming a CPU core | None | Small |
| 2 | Robust route points and corner sight checks | None | Small to medium |
| 3 | Player-style bot block, parry and evade | None | Medium, combat balance |
| 4 | Verified equipment appearance compatibility | Audit against our client | Medium |
| 5 | Active realm abilities for autonomous bots | Stage 3 reviewed; reconcile task 88 | Medium to large |
| 6 | Threat-aware outdoor PvE travel | Stage 2; performance baseline | Large |
| 7 | A classic quest pilot, then bounded content batches | Compatible clean upstream data | Large |

The owner selected all seven stages on 2026-10-09. Preserve each stage's
evidence and compatibility gates: authorization does not establish model fit,
clean quest data or live performance. Finish each stage as its own change set.
A completed source change can remain pending real-client verification without
blocking unrelated source work. Stage 7 content batches still follow pilot
acceptance, as described below.

## Implementation progress

| Stage | Source status | Remaining acceptance |
| --- | --- | --- |
| 1 | Implemented, 0.209.2; build and SOL source review passed | Installation, EOF CPU measurement and shutdown |
| 2 | Implemented, 0.210.0; flags/endpoint retained; surface, reverse-ray and door checks; isolated route-point tests passed in 0.214.1 | Existing-mesh corner, wall, door and route checks |
| 3 | Implemented, 0.211.0; shared player formulas and full defense path; isolated defense-resolution tests passed in 0.214.1 | Installation and combat observations |
| 4 | Initial catalog audit recorded in [content evidence](UPSTREAM_CONTENT_EVIDENCE.md); no filter enabled | Demonstrated affected models/races before selection changes; bug 5 client check |
| 5 | Implemented, 0.212.0; legal actives, charged prerequisites, saved cooldowns; isolated policy/token tests passed in 0.214.1 | Owner purchase/effect/database reload/world-speed observations |
| 6 | Implemented, 0.213.0; default off, bounded leader/puller decisions and retained two-leg detours; isolated policy tests passed in 0.214.1 | Controlled owner routing/performance comparison |
| 7 | Source pilot prepared, 0.214.0; clean release, two-row disposable migration and journal guide; isolated migration checks passed in 0.214.1 | Owner quest/client acceptance before batches |

Final source review was performed by SOL 6.1 High on 2026-10-09 after Luna MAX
implementation. No remaining actionable source defects were found after
correcting retained travel holds, recovery ordering, detour rejoin execution,
durable migration manifests and owned-row rollback. Review does not close the
appearance evidence gate or establish runtime acceptance.

Release builds of the server entry project and Windows launcher passed with
zero errors (warnings remain). The migration passed syntax parsing and its JSON
resource passed an exact two-row allowlist check. In 0.214.1, seven focused
migration tests passed against uniquely marked temporary clones of the pinned
clean release database: dry-run, apply, rerun, partial ownership, interruption
recovery, rollback and refusal guards. The source fixture was unchanged; no
installed or played database was modified. During isolated verification no
deployment or installed server/client start was performed.

The combined focused server run passed 40/40 cases: 25 defense-resolution,
10 active-ability policy/prerequisite/token, three route-threat policy and two
route-point cases. These call production methods using isolated test doubles;
they do not exercise native meshes, live combat or measured routing cost.
Cooldown serialization round-trip is not a database save/reload test. The
Windows launcher version/timer-constant test passed 1/1. SOL 6.1 High found no
actionable defects in the added test sources and verification wording. The
appearance evidence gate and owner gameplay acceptance still prevent full
roadmap completion.

Upstream's unconditional corner retry was narrowed to a
near-origin hit with a checked short surface move, reverse segment proof and
door exclusion.

## Stage 1 Console input handling

Source: [upstream commit c1c465c3](https://github.com/shadowofze/OfflineDAoC/commit/c1c465c33dd2962d3fe8f0a09b0a04b3d75aed67).
Track the existing source defect as [bug 115](BUGS.md).

Adapt the end-of-input backoff in `CoreServer/Actions/ConsoleStart.cs`.
Preserve normal console commands, shutdown, and the server's ability to keep
running independently of its launcher. Do not change game-loop timing.

Acceptance: inspect every console-loop exit and retry path. In an authorized
runtime check, close redirected input while the server stays running and confirm
the console thread no longer spins; check normal exit separately. A source
inspection alone does not establish measured CPU improvement.

## Stage 2 Navigation corner fixes

Source: [upstream LocalPathfindingMgr](https://github.com/shadowofze/OfflineDAoC/blob/c8b0dca0428666fbe2c6f68d0e4d0363b0d03caa/source/server/GameServer/world/Pathfinding/LocalPathfindingMgr.cs).

Port duplicate route-point handling first, preserving combined door flags and
the final destination. Evaluate the small forward retry for failed corner
line-of-sight checks separately: it must not allow sight or movement through
thin walls, closed doors, or disconnected surfaces. Preserve our Darkness Falls
preparation, geometry invalidation, native interfaces and profiling.

Acceptance: inspect empty, single-point, repeated-point, partial-path and
door-crossing cases. Check a demonstrated corner stall, a normal route, a blocked
door and a wall on the existing meshes during authorized runtime verification.
Reject or narrow the sight retry if it bypasses a real obstacle. Do not adopt
upstream's rebuilt outdoor meshes as part of this stage.

## Stage 3 Bot defensive calculations

Source: [upstream commit bf9bf38b](https://github.com/shadowofze/OfflineDAoC/commit/bf9bf38b52caf8537e2be293807d1419316ac227).

Adapt the complete defense path: property calculations, class/spec eligibility,
equipment requirements, facing rules, shield block rounds, buffs and PvP caps.
Review `GameLiving`, `AttackComponent`, the three defense calculators, and bot
equipment/spec access together. Changing only the calculators is insufficient.

Apply player-style defenses to appropriate companions and autonomous bots;
ordinary monsters and pets retain their own rules. This is a balance change,
even if it repairs an inconsistency. Check for existing bot bonuses that would
otherwise be counted twice and preserve the existing human formulas.

Acceptance: compare a shield tank, two-handed fighter, parry specialist and
evading class with equivalent player stats. Check shields absent/broken,
two-handed setups, frontal/rear attacks and PvE versus PvP caps. Verify the same
results for legal same-realm enemies and cross-realm enemies, with allies still
protected. Record live combat observations separately from formula review.

## Stage 4 Equipment appearance audit and adaptation

Source: [upstream commit a137337a](https://github.com/shadowofze/OfflineDAoC/commit/a137337a1ad7103def7e1e0f5d36f05ea22c9e81).
Related issue: [bug 5](BUGS.md), the glitched companion helmet.

First compare upstream's excluded template IDs and model evidence with our
actual client assets and item definitions. An item's realm label alone does not
prove its model is incompatible, particularly in a cross-realm world. Identify
affected races and equipment slots and produce a reviewed list of demonstrated
appearance problems before changing selection.

Then adapt future bot equipment selection or a verified appearance mapping
without deleting owned items, downgrading upgrades, changing stats or banning
valid cross-realm transfers. Existing saved equipment wins unless a separate
repair is requested. If filtering would leave a class without usable gear,
resolve that case before enabling the filter.

Acceptance: check representative races and armor types, the reported helmet,
new rewards, saved gear restoration, and player-to-companion transfers. Bug 5
stays open until its particular appearance failure is verified in the client;
an upstream list is not proof of its cause.

## Stage 5 Autonomous active realm abilities

Source: [upstream AutonomousBotRealmAbilities](https://github.com/shadowofze/OfflineDAoC/blob/c8b0dca0428666fbe2c6f68d0e4d0363b0d03caa/source/server/GameServer/bots/autonomous/AutonomousBotRealmAbilities.cs).
Local foundation: [AUTONOMOUS_RA_BUILDS.md](AUTONOMOUS_RA_BUILDS.md), task 88.

Extend our 39 class-specific paths with a reviewed initial set: Purge, Ignore
Pain, Second Wind and First Aid where class-legal. Borrow upstream's activation
ideas while keeping our saved ranks, earned point accounting and training flow.
Do not import its four-archetype replacement or its serialized-ability format.

Before coding, document each class's purchase priority, activation conditions,
legal cost/prerequisites and cooldown behavior. Existing passive purchases
remain owned; spend only unspent or newly earned points. No automatic respec,
free abilities, companion-training change or player-training change is included.
Review handler assumptions about `GamePlayer` and how cooldowns survive bot
unload/reload so relogging cannot create free repeated uses.

Acceptance: verify affordability, legal ranks, no duplicate purchases, effect
execution, crowd-control removal, healing/endurance conditions, and cooldowns
through reload and world-speed changes. Observe autonomous PvP behavior before
calling the new class builds balanced. If a safe saved-state extension is
required, design its compatibility and rollback before implementation.

## Stage 6 Outdoor travel threat awareness

Source: [upstream route-threat controller](https://github.com/shadowofze/OfflineDAoC/blob/c8b0dca0428666fbe2c6f68d0e4d0363b0d03caa/source/server/GameServer/bots/autonomous/AutonomousWorldBotController.RouteThreats.cs).
Related local work: tasks 47, 48 and 74; current camp and frontier danger policies.

Integrate an outdoor PvE travel check that pulls manageable blockers, detours
around dangerous packs and gives up after bounded repeated failures. Retain our
existing frontier avoidance and dungeon corridor handling. Let the group leader
coordinate decisions rather than giving every follower its own expensive scan.

Use our target eligibility and alliance rules; hostile players are not ordinary
route monsters. Preserve quest-NPC protection, safe areas, companion orders and
pet-pull behavior. Bound scan cadence, candidate counts, retries and detours,
use gameplay time, and provide a way to disable the new behavior independently.

Acceptance: compare the same population/settings/routes before and after at 1x
and a requested accelerated speed. Record camp arrivals, travel deaths, repeated
pulls, route failures, tick cost and achieved speed. The owner chooses the live
observation window and acceptable performance tradeoff from those measurements;
upstream's reported gains are not targets proven for our fork. Do not turn this
stage into implementing the remainder of tasks 47, 48 or 74.

## Stage 7 Classic quests and world content

Source: [upstream ClassicQuests](https://github.com/shadowofze/OfflineDAoC/blob/c8b0dca0428666fbe2c6f68d0e4d0363b0d03caa/source/server/GameServer/quests/QuestsMgr/ClassicQuests.cs)
and [QuestGuide](https://github.com/shadowofze/OfflineDAoC/blob/c8b0dca0428666fbe2c6f68d0e4d0363b0d03caa/source/server/GameServer/quests/QuestsMgr/QuestGuide.cs).

1. Inventory the pinned release's clean quest/NPC/item rows, runtime
   `classic-quests.json`, guide data and source dependencies. Record required
   assets, source attribution, missing rewards and client requirements. Runtime
   data is not supplied by copying the new C# classes alone.
2. Select one small classic quest chain with complete dialogue and rewards and
   reachable locations on our current meshes. Review class/race/realm eligibility
   individually: cross-realm travel does not automatically make every class quest
   available to everyone. Check summoned NPC allegiance and quest protection in
   Camlann, including bots leaving essential quest actors available.
3. Design a repeatable, narrowly scoped migration with collision checks and a
   backup/rollback path. Apply it only to a disposable copy during development.
   Preserve existing quest progress, inventory, currency, guilds, keeps, relics,
   economy and world settings. Never replace our played database with upstream's.
4. Provide ordinary journal/command guidance where our existing client supports
   it. Defer red markers, the native Quest Guide button and the classic war map
   to a separate verified-client patch project. A realm war map would also need
   guild ownership presentation rather than three-realm ownership assumptions.
5. After the pilot is accepted, propose small content batches by quest chain or
   area. Restored monster populations are separate reviewed additions: check
   duplicates, level ranges, camp discovery, reachability and existing loot.

Acceptance: complete the pilot through its conversation, kill/item, reward and
restart steps; check duplicate grants and a failed/retried step. Verify migration
reruns and rollback on a disposable save, unrelated progress preservation, bot
interference and interactions with hostile players. Do not advertise all 448
upstream quests as supported after a single pilot.

## Deferred upstream work

Keep realm armies, realm-owned siege/retaliation rules, relic-raid activation,
battleground goals, realm-only teleports and enemy-realm name masking outside
these stages. Any later proposal needs a specific guild-based Camlann design.
Do not adopt the Sluaghbinder expansion, a newer playable package, wholesale
navigation rebuilds, or replacement launcher/importer workflows incidentally.

Pet travel/recovery, additional spell-selection fixes and allocation reductions
remain candidates for a later focused audit. Preserve our specialized pet-pull,
charm, healing and persistent-companion behavior before considering them. Our
off-map summon fallback is already present, and upstream credits several of our
movement and class fixes; skip duplicate ports.

## Completion and verification

For each authorized implementation stage, record the chosen upstream revision,
dependencies, local adaptations, scoped diff review and remaining checks. Add a
task entry when that stage is selected; use bug 115 for the console defect and
bug 5 for the existing helmet report rather than duplicating those issues.

Follow the root workflow: run automated tests only when the owner explicitly
requests them. The acceptance cases above describe what must be established,
not permission to run suites, start a server or play the client. Add focused
regression coverage where justified by the selected behavior and report whether
it was actually executed. Static review is separate from build success, and
both are separate from runtime and real-client acceptance.

Give each finished change set its required version/changelog update and keep
the launcher, presentation expectation and command-reference version aligned.
Do not preassign future version numbers; use the highest applicable level under
AGENTS.md. Keep the upstream v0.3 download instructions unchanged. Work that is
implemented but awaits installation/gameplay checks belongs in the pending
tracker section, not Finished.

Ordinary implementation does not ship. A later shipping request uses the existing
fork-only build, merge, push and stopped-install deployment workflow. Preserve
its backups and save protections, report deployment separately, and leave the
game stopped. No additional blanket approval step is introduced by this plan.
