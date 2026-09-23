# Persistent companion roadmap

Status: Stage 1 **Complete**; Stage 2 **Complete (implementation and runtime
acceptance)**; Stage 3 **Core implementation complete; validation pending**;
Stages 4–6 **Not started**.
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

**Status:** Core XP and manual-training implementation complete; automatic-plan
research and real-client acceptance pending.

**Progress:** The owner accepted the policies in the [Stage 3 decision
brief](COMPANION_STAGE3_DECISIONS.md). Persistent companions now earn eligible
active-party NPC XP, save progress without rewriting inventory rows, support
manual career training, and can use companion-only full respecialization. The
implementation adds no roster-table column. No automatic build plan has passed
career, point-budget, milestone, and disposable-database checks, so automatic
training remains unavailable. The isolated server build passes; real-client
gameplay acceptance has not been performed.

**Prerequisites:** Stage 2 complete; the owner accepted the Stage 3 XP,
training, respecialization, and realm-point policies. Stage 1 research and
runtime-database checks are required before enabling any automatic build plan.

**Deliverables:** Active-party XP, saved unspent points, validated automatic
builds when available, manual spending, and visible progression. Validate
allocations against actual class rules and refresh combat skills after training.
Preserve player reward shares unless explicitly changed.

**Acceptance:** Benched companions earn no XP. Active companions receive the
agreed NPC reward, controlled-pet damage stays attributed to the companion, and
invalid specs and point overspending are rejected. Manual spending and XP
survive restart. Validate player and autonomous-bot rewards independently.
Runtime acceptance has not been performed in this implementation step.

### Stage 4 - Equipment

**Status:** Not started.

**Prerequisites:** Stages 2-3 complete; drop rules, transfers, capacity/overflow,
starting gear, and manual override behavior decided.

**Deliverables:** Personal gear rolls, persistent inventory, class/build-aware
automatic upgrades, manual equipping, and recoverable replaced items. Define how
manual selections are protected and player-supplied gear is returned.

**Acceptance:** Existing player loot remains intact. Items cannot duplicate or
vanish through equip/replacement, benching, restart, failed transfers, or full
inventories. Illegal equipment is rejected. Recruitment cannot bypass the agreed
starter-item and transfer rules. Check autonomous equipment and normal player
inventory behavior separately.

### Stage 5 - Character and control

**Status:** Not started.

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

**Status:** Not started.

**Prerequisites:** Stages 1-5 complete; death/recovery and raid behavior decided;
all open decisions resolved for enabled features.

**Deliverables:** Cross-system regression coverage, disposable-save compatibility
checks, player instructions, and a real-client checklist. Document rollout,
backup/restore, and any migration before separately authorized deployment.
Do not implement unrelated Camlann tiers.

**Offline acceptance:** Check cross-realm parties, group removal, logout/restart,
death/recovery, travel, raids, inventory ownership, save/load failures, and
independence from autonomous bots. Use disposable existing-save fixtures and
record commands and results.

**Real-client acceptance:** In a separately authorized session, recruit a level-1
companion, level together, inspect training, obtain/equip an upgrade, manually
replace it, bench/reinvite, and restart. Verify a second same-class recruit,
catch-up, support participation, cross-realm travel, death/recovery, and raids.
Record observations separately; the broader Stage 6 gate remains pending until
the listed cross-system scenarios are performed.

## Decisions and stage gates

These are unresolved, not implicit defaults. Resolve and record each before
implementing its dependent stage. If an earlier stage needs a later decision,
resolve it then rather than hiding it in implementation.

| Decision | Required before | Status |
| --- | --- | --- |
| Recruitment location, cost, initial level, and roster limits | Stage 2 | Resolved: anywhere, free, level 1, 78 stored per character |
| Ownership per character/account; uniqueness of authored recruits | Stage 2 | Resolved: per character; each authored individual once per owner |
| Meaning of `/spawn`, old saved-bot compatibility, temporary testing helpers | Stage 2 | Resolved: `/spawn` stays temporary; no legacy conversion or GM test-helper command |
| Companion XP eligibility, participation, catch-up target and pace, support/pet credit | Stage 3 | Resolved; implementation awaits real-client acceptance |
| Automatic/manual modes, plan eligibility, transitions, and respecialization | Stage 3 | Manual and respec rules resolved; automatic plans remain gated on class and runtime-database validation |
| Companion realm-point eligibility and progression | Stage 3 | Resolved: companions do not earn realm points |
| Drop frequency/eligibility, starting gear, and recruit farming | Stage 4 | Open; the existing generated level-1 starter kit is retained for Stage 2 |
| Item transfers, capacity/overflow, manual overrides, and item recovery | Stage 4 | Open |
| Exact tactical controls, personality defaults, and dialogue presentation | Stage 5 | Open |
| Death/recovery and raid rules | Stage 6, or earlier if changed by a prior stage | Open |

Personal-drop transfer restrictions and a dedicated testing-helper command
have not been approved as fixed rules.

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
