# Persistent companion roadmap

Status: Stage 1 **Complete**; Stage 2 **Complete (implementation and runtime
acceptance)**; Stage 3 **Implemented; 33 project-recommended automatic plans
validated against runtime data; six classes remain manual-only; real-client
acceptance pending**; Stage 4 **Implemented; offline checks complete;
real-client acceptance pending**; Stage 5 **Implemented; client and combat-role
acceptance pending**; Stage 6 **Offline checks complete; real-client acceptance pending**.
Last updated: 2026-09-23.

This roadmap records the agreed direction for lasting party members. It does
not describe shipped gameplay or authorize implementation of every stage.
Implement only a separately requested stage, resolving its open decisions first.
Read the repository and server AGENTS.md instructions before implementation.

[Current companion behavior](COMPANION_BOTS.md) remains the existing contract.
The target is Classic + SI, 1.65-era mechanics, as described in the
[Camlann conversion](CAMLANN.md); this roadmap does not expand that era.

## Agreed direction

- Two authored companions per supported class across all three realms, plus
  optional generated recruits with the same progression and player controls.
  Authored companions have curated identities and richer writing.
- Persistent identity, level, XP, training, inventory, and equipment across
  benching and server restarts. Identity is independent of class or display name:
  two Shamans are separate individuals with independent progress.
- Benched companions retain their earned level. Catch-up progression happens
  while adventuring, not through passive reserve-roster leveling.
- Personality supplies dialogue and tactical defaults; explicit player orders,
  build choices, and equipment choices take precedence.
- Automatic build plans plus manual specialization, with visible progression.
- Personal companion gear drops without reducing the player's existing loot;
  automatic upgrades, manual equipment management, and recoverable replaced gear.

Example: a Shaman recruited at level 1 adventures with you to level 6. Benching
and reinviting him restores that same person, build, and equipment. If you have
outleveled him, active adventuring enables catch-up. Level 1 is an example, not a
settled initial-level rule for recruitment into an already higher-level party.

## Current behavior and reuse candidates

`/spawn` creates a randomly named temporary helper and equips it at creation.
Helpers earn session XP and **automatically spend specialization points on
level-up**, refreshing skills and spells. They receive no loot or personal gear
rolls and no realm points. They disappear when removed from their group; their
identity and progression are not saved.

The main entry points are `TemporaryGroupCommands.cs`, `GameBot.cs`, and
`AbstractServerRules.cs` in the server. Existing bot profiles/database support,
autonomous progression, and equipment-upgrade logic are reuse candidates, not
proof that persistent player companions already work. Audit ownership,
serialization, inventory lifetime, and save/load behavior before reuse.
Keep real players, owned companions, and autonomous world bots distinct; benched
companions must not accidentally become roaming autonomous bots.

## Implementation stages

Record progress, remaining decisions, offline checks, and real-client results
for each stage. Use isolated build outputs and disposable save fixtures.
Passing offline checks does not imply real-client gameplay verification.

### Stage 1 - Research and design

**Status:** Complete.

**Progress:** The source audit, 39-class runtime catalog, persistence and reward
boundaries, equipment ownership candidates, and client-menu capabilities are
recorded in [COMPANION_DESIGN.md](COMPANION_DESIGN.md). The
[per-class build research](COMPANION_BUILD_RESEARCH.md) records a sourced
proposal or an explicit plan-blocking research gap for every class. A static
reference pass checked all 39 career line sets and 108 ranked lines across 34
numeric proposals; four recommendations were adjusted for companions' lack of
autotrain points. Animist and Wizard lack numeric templates; three dated forum
variants for Blademaster, Hero, and Warrior still need ranked-skill validation.
Missing level-by-level schedules also block the affected automatic plans rather
than inviting guessed templates. Uthgard sources are identified as emulator
recommendations, not retail-era evidence. The local
runtime database check remains a prerequisite before enabling any automatic plan.
Character-scoped ownership and additive save boundaries were recorded as design
recommendations; Stage 2 recruitment, ownership, and compatibility choices were
confirmed by the owner and are recorded below. Code assessment of the menu is
complete; runtime interactions were later verified as part of Stage 2 acceptance.

**Prerequisites:** Read the current companion contract and relevant server code.

**Deliverables:** Audit persistence, reward attribution, equipment ownership,
class/spec mechanics, and client menu capabilities. Establish the supported
class catalog. Research cited 1.65-era leveling and endgame builds for every
supported class, including roles, point priorities, milestones, and source era.
Check allocations against implemented skills and point budgets. Distinguish
historical evidence, recommendations, and server deviations; do not substitute
modern builds or claim popularity without evidence. Document roster/save
interfaces, compatibility strategy, and client interaction design.

**Acceptance:** Every supported class has a sourced proposal or an explicit
research gap that blocks its automatic plans. Ownership and persistence
boundaries are documented. Menu feasibility is assessed from code; interactions
requiring client experiments remain unverified until separately authorized
checks. No native client patch is assumed. Stage 2 decisions are resolved before
roster implementation.

### Stage 2 - Persistent roster

**Status:** Complete (implementation and runtime acceptance, 2026-09-23).

**Progress:** The owner selected the recommended recruitment and compatibility
rules in the 2026-09-23 [decision brief](COMPANION_STAGE2_DECISIONS.md): free
recruitment anywhere, level 1, 78 stored companions per character, per-character
ownership, unique authored individuals per owner, `/spawn` remains temporary,
and no legacy profile conversion. Added an additive `player_companions` record,
stable companion IDs, generated recruit/list/invite/bench commands, namespaced
inventory persistence, and login restoration. Group removal, owner departure,
and group disband save and bench persistent companions. Only generated recruits
are available; authored individuals remain Stage 5 work. XP/catch-up and training
rules remain Stage 3 work. The owner confirmed all Stage 2 runtime
acceptance checks passed in a separate Windows acceptance installation on
2026-09-23. The invite flow was rechecked after fixing persistence of newly
generated unique starter-gear records. Stage 6 retains the broader integration
scenarios that are outside this Stage 2 gate.

**Prerequisites:** Stage 1 architecture/UI audit complete; recruitment, ownership,
roster limits, `/spawn` compatibility, and temporary-helper policy decided.
A minimal fixture can precede the full Stage 5 cast.

**Deliverables:** Stable companion IDs; recruit/list/invite/bench flows; explicit
selection between existing same-class companions and new recruits; saved identity
and progression; restart-safe ownership and save/load. Define compatibility with
existing saved bots without destructive conversion or assuming temporary helpers
are already persisted characters.

**Acceptance:** Two same-class recruits retain independent state. Benching,
reinviting, logout, and restart restore the selected individual. Repeated invites,
full parties, and failed spawning cannot duplicate companions or lose saved
state. Other players cannot access the roster. Disposable existing saves remain
usable; any incompatibility requires an explicit migration decision.

### Stage 3 - Progression and training

**Status:** Implemented; all 33 enabled plans passed fixture-based runtime
validation and per-level budget checks. Six classes remain manual-only for
documented research or combat-profile blockers. Real-client acceptance pending.

**Progress:** The owner accepted the policies in the [Stage 3 decision
brief](COMPANION_STAGE3_DECISIONS.md). XP, manual career training, persistence,
and companion respecialization are implemented. The class-by-class review in
[COMPANION_BUILD_RESEARCH.md](COMPANION_BUILD_RESEARCH.md) covers all 39
classes. The owner delegated the general-adventuring selections: 33 plans use
explicit level-50 recommendations and deterministic level-by-level schedules,
identified as project recommendations rather than historical source builds.
The sanitized local-acceptance skill fixture validated all 105 plan targets
against class careers; 90 targets have ranked ability, spell, or style data and
15 are career-only lines (Parry, Stealth, or class-specific bow lines). Every
schedule was simulated through level 50 within the runtime point budget. Animist
and Wizard lack numeric targets; Blademaster, Hero, and Warrior still need
ranked-skill or role review; Necromancer's numeric candidate depends on the
unsupported Death Servant companion profile. Those six stay manual-only.

**Prerequisites:** Stage 2 complete; the owner accepted the Stage 3 XP,
training, respecialization, and realm-point policies and delegated the supported
build choices. Runtime skill data was copied into a disposable, sanitized
fixture outside Git; no character saves or personal inventories were copied.

**Deliverables:** Active-party XP, saved unspent points, reviewable automatic
build candidates, manual spending, and visible progression. Validate allocations
against actual class rules and refresh combat skills after training. Preserve
player reward shares unless explicitly changed.

**Offline status:** Active-party XP, catch-up, manual career training, explicit
respec, automatic/manual mode transitions, versioned plan persistence, and
multi-level automatic gains are implemented. The focused suite checks every
enabled plan at every level for budgets, monotonic ranks, and no overlevel
training. Runtime fixture validation covers the real class-career and skill
tables. Unsupported or changed plan IDs do not silently change allocations.
**Real-client acceptance:** XP gain, trainer interaction, automatic mode
switching, leveling, and restart persistence remain owner-run checks.

### Stage 4 - Equipment

**Status:** Implemented; focused offline validation and Release build complete.
Clickable menu usability and gameplay acceptance remain owner-run checks.

**Prerequisites:** Stages 2-3 complete; reward pacing, transfers, capacity and
overflow, starter provenance, and manual slot-lock policy are recorded in the
[Stage 4 decision brief](COMPANION_STAGE4_DECISIONS.md).

**Deliverables:** Independent per-companion PvE drops and eligible PvP drops,
persistent class-legal equipment and inventory, safe owner transfers, strict
automatic upgrades, manual slot locks, recoverable replacements, protected
surplus selling, and a private clickable menu with command fallback.

**Offline status:** Atomic insert/update/delete transactions couple drops,
transfers, surplus sales, and coin credits. Existing starter gear is marked
protected; legacy unknown items stay unsellable and non-transferable. Personal
loot does not alter player loot or autonomous-bot handling. Full-bag reward,
transfer, and equip paths can sell only the lowest-scoring positive-value,
unequipped companion-earned item. The bare command opens a temporary private
menu; explicit roster/training commands remain available.
**Real-client acceptance:** Clickable popup behavior, fast leveling, full-party
PvE/PvP drops, equip and recovery, full-bag transfers, merchant-value proceeds,
and reload persistence remain pending. See the checklist in
[COMPANION_STAGE4_DECISIONS.md](COMPANION_STAGE4_DECISIONS.md).

### Stage 5 - Character and control

**Status:** Implemented; offline Release build and static cast checks complete.
Real-client and combat-role acceptance remain pending. The owner selected the
policy in [COMPANION_STAGE5_DECISIONS.md](COMPANION_STAGE5_DECISIONS.md).
The catalog supplies 78 authored people; generated personalities, individual
roles and stances, direct-order precedence, private menu, and command fallbacks
are implemented. Existing records keep their prior class behavior.

**Prerequisites:** Stages 2-4 complete; tactical controls and dialogue presentation
decided. Identity design and writing may be prepared after Stage 1.

**Deliverables:** Two authored individuals per supported class, with names,
appearances, backgrounds, personality, dialogue, and preferred builds. Generated
recruits use persistent identity/personality templates and the same controls.
Expose configurable combat preferences through validated client interactions.

**Acceptance:** The authored catalog covers every supported class across all
three realms. Same-class recruits remain distinguishable; generated identities
survive reload. Orders override personality defaults. Dialogue does not flood
combat chat. Verify roles actually use trained abilities through offline AI
checks and later real-client scenarios.

### Stage 6 - Integration and verification

**Status:** Offline regression checks complete (2026-09-23); owner-run
real-client acceptance pending.

**Progress:** The owner selected the existing GameBot death-recovery path for
persistent companions. `/raid 40|80` remains limited to the owner's temporary
`/spawn` helpers. The additive roster schema remains compatible; no saved death
state or migration was added. Twelve focused NUnit cases use disposable SQLite
fixtures. See [COMPANION_STAGE6_ACCEPTANCE.md](COMPANION_STAGE6_ACCEPTANCE.md)
for the offline commands, results, and client checklist. The six manual-only
class plans remain unchanged.

**Prerequisites:** Stages 1-5 implementation is complete. Stage 4-5 client
acceptance and Stage 6 owner-run cross-system checks remain outstanding.

**Deliverables:** Cross-system regression coverage, disposable-save
compatibility checks, player instructions, and a real-client checklist.
Record rollout, backup/restore, and any migration before separately authorized
deployment. Do not implement unrelated Camlann tiers.

**Offline acceptance:** Passed focused checks for companion identity and owner
isolation, recruit/invite/bench boundaries, group removal, logout/restart,
training-save rollback, XP and player-reward boundaries, gear ownership,
cross-realm grouping and confirmed travel policy, existing death timers, both
raid capacities, and separation from autonomous bots. The fixture creates and
deletes GUID-named SQLite files under the system temporary directory; it never
opens live save data.

**Offline test results:** The Stage 6 fixture passed all 12 cases. The
combined companion, persistence, travel, recovery, raid, and defensive-pull
selection passed 278/278 tests. The complete Release server suite passed
2,008/2,008 tests with no failures or skips. The full results and commands are
recorded in the Stage 6 acceptance brief.

**Real-client acceptance:** Stage 6 remains pending until the owner records the
checklist in the acceptance brief. Verify leveling and training, gear and
inventory, same-class companions, restart and catch-up, cross-realm travel,
death and recovery, and temporary-only raid behavior. No client startup or
deployment is part of the offline gate.

## Decisions and stage gates

These are unresolved, not implicit defaults. Resolve and record each before
implementing its dependent stage. If an earlier stage needs a later decision,
resolve it then rather than hiding it in implementation.

| Decision | Required before | Status |
| --- | --- | --- |
| Recruitment location, cost, initial level, and roster limits | Stage 2 | Resolved: anywhere, free, level 1, 78 stored per character |
| Ownership per character/account; uniqueness of authored recruits | Stage 2 | Resolved: per character; each authored individual once per owner |
| Meaning of `/spawn`, old saved-bot compatibility, temporary testing helpers | Stage 2 | Resolved: `/spawn` stays temporary; no legacy conversion or GM test-helper command |
| Companion XP eligibility, participation, catch-up target and pace, support/pet credit | Stage 3 | Resolved and implemented; real-client acceptance pending |
| Automatic/manual modes, plan eligibility, transitions, and respecialization | Stage 3 | Resolved and implemented; 33 runtime-validated project plans, six manual-only blockers |
| Companion realm-point eligibility and progression | Stage 3 | Resolved: companions do not earn realm points |
| Drop frequency/eligibility, starting gear, and recruit farming | Stage 4 | Resolved and implemented; independent XP-rate PvE chance, eligible PvP rewards, protected starter kit |
| Item transfers, capacity/overflow, manual overrides, and item recovery | Stage 4 | Resolved and implemented; free restricted transfers, manual slot locks, earned-gear surplus selling |
| Exact tactical controls, personality defaults, and dialogue presentation | Stage 5 | Resolved and implemented; client and combat-role acceptance pending |
| Death/recovery and raid rules | Stage 6, or earlier if changed by a prior stage | Resolved: existing GameBot recovery; raids allow owned temporary helpers only |

Stage 6 offline validation is complete. Stage 4–5 real-client acceptance and
Stage 6 client acceptance remain pending. See the Stage 6 acceptance brief.

## Owner acceptance tasks (2026-09-23)

These are the owner's in-client tasks from the current acceptance pass. A code
change or an offline build does not close a client task; mark a task complete
only after the owner confirms the behavior in the installed build. The owner
reports Step 1 preparation complete, but the installed version and backup
details have not yet been recorded in the acceptance brief.

- [x] **Step 1 — Prepare the test installation and save.** Owner reported this
  complete on 2026-09-23. Record the installed version and backup location
  before final acceptance.
- [ ] **Step 2 — Paging and stale choices.** Opening another roster, recruit,
  cast, or inventory page must show a clear current page. Any older choices
  still visible in the popup must either remain safe and usable or disappear;
  clicking one must never act on a different item or companion.
- [ ] **Step 2 — Readable choices.** Remove the raw hexadecimal action tokens
  seen beside every link in the owner's screenshots. Navigation and recruitment
  labels must be understandable without internal IDs.
- [ ] **Step 2 — Usable inventory.** The owner prefers a native inventory-style
  window if the client supports one. Verify that the companion backpack opens
  in the client's external-inventory window, item delves and safe drag/drop
  transfers work, and the separate equipment, companion backpack, and owner
  backpack menus remain short and navigable. Check equip, lock, unequip, keep,
  and transfer actions before closing this task.
- [ ] **Step 2 — Recruitment wording.** Explain generated and authored
  companions in the UI. Both are persistent; authored companions are named,
  written individuals available once per owner, while generated recruits have
  newly created identities.

**Custom8 gate (2026-09-23):** The normal game rendered Custom8, accepted
server label updates, and returned a button click through target selection.
The dedicated action packet did not reach the server, and the visible search
field would not accept focus or text. The failed probe was removed and the
verified raid-patched client restored. See
[COMPANION_CUSTOM8_PROBE.md](COMPANION_CUSTOM8_PROBE.md).

**Companion Manager (0.32.0, offline):** Clicks now use the client's own
slash-command sender, and search uses the ordinary chat line. The server
manager and a staged, hash-guarded client patch are implemented and passed
offline emulation and focused tests. Bare `/companions` opens the manager and
prints command guidance when no patched client answers. See the
[integration handoff](COMPANION_MANAGER_INTEGRATION.md) for the one combined
real-client check. Step 2 stays open until the owner confirms that check.
On 2026-09-24 the 0.32.0 window opened but no click worked; 0.32.1 fixed the
click wiring, and the owner confirmed that clicking and search work. Next steps
(build selection, behaviour controls, equipment slot layout) are planned in the
[Companion Manager roadmap](COMPANION_MANAGER_ROADMAP.md).

The owner supplied four screenshots for Step 2. They show speech pages
accumulating in one popup, raw choice tokens, and an equipment page filled
with per-slot actions. The menu changes addressing these findings require a
new build and owner retest; no Step 2 item is accepted yet. Later acceptance
steps remain as listed in the Stage 4–6 briefs.

## Maintenance and boundaries

Keep this roadmap separate from shipped-feature documentation. Update the
[current companion contract](COMPANION_BOTS.md) when behavior actually changes.
Each completed implementation task follows repository changelog and three-part
versioning rules; this roadmap reserves no future versions and authorizes no
1.0 release. Preserve saves, player items/currency, realm exchange, and travel
except where changes are explicitly approved.

This roadmap authorizes no gameplay, schema changes, native patches, deployment,
or server/client startup by itself. Stage 1 findings and the research gaps that
block automatic plans are recorded in the companion design and build-research
documents linked above.
