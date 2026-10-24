# RvR mode: implementation plan and to-do list

Owner decisions: [RVR_MODE_ANALYSIS.md](RVR_MODE_ANALYSIS.md) sections 5 and
"Further owner decisions". Spec: `docs/superpowers/specs/2026-10-24-rvr-mode-design.md`
(written by the Phase 0 agent; refine this list from it). Night session
2026-10-24/25 authorised for everything on the owner's PC incl. wiping the
current save and creating the RvR world as the default; the Camlann PvP
world becomes a working launcher button with its own fresh save.

Status legend: `[ ]` open · `[~]` running (agent) · `[x]` done (version) ·
`[!]` blocked

## Packages (refine from the spec; one developer + one reviewer per package)

- [~] **R0 Spec.** Agent rvr-spec writes the design; orchestrator refines
  this list from it (2026-10-24 23:55).
- [ ] **R1 World marker and rules core.** Save declares its world
  (`Realm-1` / `Camlann-1`); startup accepts both; friend/enemy per world in
  the one alliance seam (same realm = friend everywhere, guild/battlegroup
  only within a realm, companions = owner's realm); safe areas and hub peace
  per realm; client colour handling and realm byte per world; Tier 3 test
  allows the RvR branch. Both worlds covered by tests.
- [ ] **R2 Travel and population rules.** Porters, capitals, `/tc`, Realm
  Exchange, Atlantis havens, BG porters realm-bound in RvR; `/spawn` own realm
  only; open BG perrealm default in RvR; Gothwaite mode excluded in RvR.
- [ ] **R3 Keeps and relics.** Neutral start (Frontier Wardens), capture by a
  realm sticks; claims by the keep's realm (solo player guild allowed, bot
  guilds of that realm); guards by owner realm; relics on realm pads with a
  realm-wide bonus; Camlann keep campaign code mode-aware.
- [ ] **R4 Bots per realm.** Single-realm bot guilds (~600 bots per realm),
  realm login balance, autonomous RvR layer per realm (muster at the realm's
  border keep, staging, sieges, retreat; raids as battlegroups within a realm,
  as close to upstream 0.37b as the fork allows), grudges/banter per realm.
  Builds on Stefan's movement work (tasks 135-139).
- [ ] **R5 World reset and launcher.** Reset routine/data packs for a fresh
  RvR world (keeps neutral, bot guilds per realm, properties) and for a fresh
  Camlann world; launcher world picker (RvR default, "PvP" button), reset per
  world, import guard; both worlds start and stop from the launcher.
- [ ] **R6 Verification.** Both-worlds regression suite, load test with ~1,800
  bots, server log review, owner's real-client checklist, docs (CAMLANN.md
  contract amended, PLAY.md), TASKS entry closed.

## Log

- 2026-10-24 23:55: plan created from the analysis and the owner's decisions;
  spec agent running; Majestic Will 1.68 started as a side package.
