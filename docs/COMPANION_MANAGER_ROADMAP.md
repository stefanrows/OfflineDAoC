# Companion Manager roadmap

Status: **M0 remainder implemented offline in 0.33.0; owner real-client check
pending. M1–M3 are not implemented yet.**
Last updated: 2026-09-24.

The `Custom8` Companion Manager (0.32.1) passed the owner's real-client gate
on 2026-09-24: the window opened, clicks and chat-line search worked, and the
companion bag opened beside it. See the
[integration handoff](COMPANION_MANAGER_INTEGRATION.md) for the design and the
[companion roadmap](COMPANION_ROADMAP.md) for the underlying stages. This
document plans the next improvements the owner asked for. Implement one
milestone at a time, after its open decisions are resolved.

## M0 - Close out 0.32

- [x] Owner: check that the raid windows still work and that the manager fits
      at 800×600 (gate step 4). Record the result in
      [COMPANION_STAGE6_ACCEPTANCE.md](COMPANION_STAGE6_ACCEPTANCE.md).
- [x] Owner: repeat the open Stage 2 items (paging, readable choices, usable
      inventory, recruitment wording) in the window, then close them in the
      companion roadmap.

The owner marked both window acceptance checks complete on 2026-09-24. Detailed
observations and the installed build/version were not supplied.

The three remaining M0 items were implemented on 2026-09-24 (0.33.0). They
passed offline checks only; the owner check below closes them.

- [x] Fix raid click-to-target (offline). The raid XML used `RaidMemberNN`
      names, which the client's `OnClickEvent` parser rejects. Both raid
      builders now write `1536`–`1615` (`0x600` + member). `game.dll` needs no
      change: the raid's event handler already selects member *n* for event
      `0x600 + n` by calling the client routine that the server's own
      target packet uses (`0x41AB40`, mode 0), including its stock messages
      for a member who cannot be targeted. The fix ships as its own
      hash-guarded XML patch:
      `build_raid_click_fix_client.py`, `test_raid_click_fix_client.py`, and
      `tools/dev/Install-RaidClickFix.ps1`. See the
      [integration handoff](COMPANION_MANAGER_INTEGRATION.md#raid-click-to-target-fix-0330).
- [x] Companion bag title: checked. The client formats the caption itself
      from the fixed string `House Vault %u`. The server sends only the number
      (`GameVault.Index + 1`), so it cannot change the words. Renaming it needs a
      native caption hook; that is not planned unless the owner asks for it.
- [x] Hide the detail `[Up]`/`[Down]` links when nothing scrolls (offline).
      They are now server-filled labels 130 and 131, shown only in the
      direction that can scroll. This needs the rebuilt 0.33.0 manager
      `game.dll`. A 0.32.1 client ignores the new labels and keeps its
      static links, so the new server works with either client.
- [ ] Owner: install both client patches, then check that clicking a raid
      member targets them in `/raid 40` and `/raid 80`, and that the detail
      links appear only when a companion's Overview or Gear page is long
      enough to scroll.

## M1 - Build selection with automatic training

**Goal:** choose a named build per companion, such as Healer "Mending
(healer)", "Tri-spec", "Augmentation (buffer)", or "Pacification (crowd
control)", or Spiritmaster "Darkness (bomb)", "Suppression", or "Summoning
(pet)". In automatic mode the companion trains
along that build at every level. Manual training and respec remain available.

**Today:** `CompanionBuildPlanCatalog` holds one validated plan per class (33
classes; six are manual-only). Each record's saved `TrainingPlanId` names the
plan it follows, and changed or unknown IDs never silently change allocations.
Build choice therefore needs **no save migration**: the plan ID simply names a
variant.

**Work:**

1. Research: two or three builds per class from era sources, in the same format
   as [COMPANION_BUILD_RESEARCH.md](COMPANION_BUILD_RESEARCH.md). It already
   records branches such as Healer Pacification or Augmentation, and Shaman
   Augmentation or Subterranean (Cave). Each build gets a role, level-50 targets,
   and a per-level schedule, labeled as a project recommendation where no
   historical source exists.
2. Catalog: several stable, versioned plans per class, validated like today
   against the runtime skill fixture (point budgets per level, monotonic ranks,
   no overlevel training). Unvalidated variants stay hidden.
3. Manager: a build list in Training & Tactics with a one-line description and
   its role; the current build is marked. Automatic mode follows the selected
   build; `/companions plan` and a new command fallback show and select builds.
4. Tests: every variant simulated through level 50, plus save/reload and
   switching between builds.

**Owner decisions (2026-09-24):**

1. **Builds offered:** the most popular builds for each class at the 1.65
   patch level (Classic + SI). The existing research covers only one build per
   class, noting a few branches, so step 1 above is new research. It must follow
   the same rules: cite era sources, label project recommendations, and keep a
   class manual-only where evidence is missing.
2. **Switching builds:** automatic. Choosing a new build resets the companion's
   specializations and immediately retrains them along the new build to their
   current level. The window keeps a separate **[Respecialize]** action for
   manual rebuilding.
3. **Build sets the role:** yes. The owner's healer example defines the
   mapping. A build's primary job is shown and applied; a hybrid build can fill
   several roles.

   | Healer build | Role |
   | --- | --- |
   | Mending ("healing spec") | Healer |
   | Tri-spec | Healer, also Buffer and Crowd control |
   | Augmentation | Buffer |
   | Pacification | Crowd control (not a healer) |

   Today's roles are Tank, Healer, Buffer, and Attacker. A **Crowd control**
   role must be added, and its AI behaviour (mezz and root choice, target
   selection, breaking rules) checked against the existing bot crowd-control
   code before builds can map to it.
4. **Build chosen at recruitment:** required. Recruit flow: select a story
   companion or class, choose one of its builds, then recruit. An authored
   companion's written preferred build is preselected. The command fallback
   becomes `/companions recruit <class> [build]`.

**Still open:**

- Whether an automatic build switch uses the owner's full-skill respec
  eligibility (the current Stage 3 rule for `/companions respec`) or is free for
  companions. Recommendation: free, since it cannot change the player's own
  character.
- Whether a build switch still needs a class trainer, as manual training does
  today. Recommendation: no, like automatic level-up training.

## M2 - Behaviour controls in the window

**Goal:** everything that already exists as a command, in the manager.

- Per companion (already in the window): role, stance, training mode, train,
  respec.
- Group orders (today `/aggressive`, `/defensive`, `/passive`, and
  `/companions group default`): a group row in the Roster tab showing the
  active order and each companion's effective stance when an order overrides
  it.
- Other existing controls to surface as window actions after the owner picks
  them: `/pull`, `/grind`, and invite/bench-all.

No native work is expected; this reuses the window's labels and click areas.
Decision: which group controls belong in the window versus staying as commands.

## M3 - Companion equipment in a slot layout

**Goal:** see and change a companion's worn slots in a character-sheet layout
like the player's inventory window (helm, torso, hands, weapons, jewellery),
with drag and drop from the companion bag.

The player's own inventory window reads only the player's equipment. Loading a
companion into it would risk the player's real items, so it is ruled out.
Options, in order of preference:

1. **Slot grid in `Custom8`:** item icons at fixed slot positions with
   drag/drop. This needs two native capabilities that are not yet proven: an
   icon adapter (the stock `IconDef` uses `AdapterName`), and a drop from the
   native bag onto a window slot reaching the server. **Gate:** one isolated
   probe, emulated offline first, then a single owner check, as for 0.32.
2. **Second vault page:** the bag window already has pages. Worn slots could
   appear on page 2 in a fixed order, with dragging onto a slot meaning equip.
   Server-only and low risk, but without slot silhouettes.
3. **Window-only (current):** Gear tab lists and actions. This stays as the
   fallback.

All options keep the current protections: `TryApplyEquipmentMutation`, class
legality, slot locks, keep flags, and the rules that a rejected or full-bag
action leaves items and coins intact.

## Order and gates

M0, then M1, then M2, which carry no native risk and give the most gameplay
value. M3 starts with its probe. Each milestone follows the repository's
versioning, uses offline tests before any owner check, and closes only after
the owner confirms it in the real client.
