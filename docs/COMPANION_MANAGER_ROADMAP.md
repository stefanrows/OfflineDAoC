# Companion Manager roadmap

Status: **M0 remainder implemented offline in 0.33.0; owner real-client check
pending. M1a (build research, catalog, and command selection) implemented
offline in 0.34.0. M1b (build list in the window and recruit flow) implemented
offline in 0.35.0. M1c (Crowd control role and build roles) implemented
offline in 0.36.0. M2 (group controls in the window) implemented offline in
0.37.0. M3 (worn slots on the bag window's second half) implemented offline in
0.38.0.**
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

**Before M1a:** `CompanionBuildPlanCatalog` held one validated plan per class (33
classes; six were manual-only). Each record's saved `TrainingPlanId` names the
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

**Resolved (owner, 2026-09-24):**

5. **Switch cost:** a build switch is free. It does not use the owner's
   full-skill respec eligibility, because it cannot change the player's own
   character. `/companions respec` keeps its Stage 3 rule.
6. **Trainer:** a build switch needs no class trainer, like automatic level-up
   training. Manual training still needs one.
7. **Split:** M1 ships in three parts: M1a (research, catalog, command
   selection), M1b (manager build list and recruit flow), and M1c (Crowd
   control role and build-to-role mapping).

### M1a - Builds and command selection (0.34.0, offline)

- [x] Research: 24 builds added from the 1.65 sources, each labelled sourced,
      adjusted, or project. The Uthgard forum was down (HTTP 500), so forum
      numbers come from search excerpts and should be rechecked. See
      [Build choice (M1a)](COMPANION_BUILD_RESEARCH.md#build-choice-m1a).
- [x] Catalog: 57 builds for 35 classes. The original 33 `general-pve-v1`
      IDs are unchanged and stay each class's default build, so no save
      migration is needed. Wizard and Animist now have builds; Blademaster,
      Hero, Warrior, and Necromancer stay manual-only. Each build has a
      one-word key, a name, and a role description.
- [x] Level-up training follows the companion's saved build, not only the
      class default. `/companions mode <name> automatic` keeps a valid saved
      build.
- [x] Commands: `/companions build <name>` lists the builds and marks the
      current one; `/companions build <name> <build>` switches (free, no
      trainer, resets and retrains to the current level, active or benched);
      `/companions plan <name>` also lists builds;
      `/companions recruit <class> [build]` recruits with a chosen build.
- [x] Manager: the Training & Tactics tab names the current build and the
      build keys, with the command to switch.
- [x] Tests: every build simulated through level 50 (budget, monotonic,
      no overlevel, targets reached); switching at every level resets every
      line and keeps the point total; switching away and back restores the
      same allocation; original IDs stay the defaults; unknown builds leave the
      companion and roster unchanged.
- [ ] Owner: in the real client, switch a companion's build (active and
      benched), check its trained lines and spells, level it once, and recruit
      with `/companions recruit healer pacification`.

### M1b - Build list in the window and recruit flow (0.35.0, offline)

Server only. It reuses the window's detail links and action buttons, so the
installed 0.33.0 (or 0.32.1) manager `game.dll` needs no change.

- [x] Training & Tactics: every build is a detail link, followed by its role
      text; the current build is marked `(current)`. Clicking a build selects
      it (`<`) and shows its level-50 targets. **[Use build]** applies it
      through `PlayerCompanionRoster.TrySelectBuild`, with the M1a rules: free,
      no trainer, reset and retrain to the current level, active or benched.
      It stays disabled until a build other than the current one is selected,
      so one stray click cannot reset a companion.
- [x] Recruit flow: a story companion or class lists its builds with the class
      default preselected (`(default) <`). **[Recruit]** or **[Create]** uses
      the selected build. The authored catalog records no preferred build yet,
      so story companions preselect their class default. Manual-only classes
      show why and recruit in manual training. A selection belongs to the one
      row it was made on; changing rows or tabs never carries it over.
- [x] Commands: `/companions recruit authored <name> [build]`.
- [x] Overview names the build that automatic training follows.
- [x] Test: a manual Healer shows all four builds with **[Use build]**
      disabled; choosing Pacification enables it and sends that build, not the
      default, to the roster; the recruit panel preselects Tri-spec and
      recruits with the chosen build. The test server has no skill tables, so it
      checks the refusal path; a successful switch is covered by the M1a
      roster tests.
- [ ] Owner: in the real client, open Training & Tactics, select a different
      build, and choose **[Use build]** for an active and a benched companion.
      Recruit one story companion and one class with a non-default build and
      check the build on the new roster entry.

### M1c - Crowd control role (0.36.0, offline)

Server only. The installed manager `game.dll` needs no change.

**Owner decisions (2026-09-24):**

- PvE: the companion mezzes adds, and other companions protect the mezzes.
- Hybrid builds keep one saved primary role. A build that lists crowd control
  also controls adds when its primary job allows (Tri-spec = Healer + add
  control).
- Choosing a build always sets its role. The player can change it afterwards.
- No native Crowd control filter button for now; the roster filter keeps its
  four role buttons.

**Check against the existing code:** before this change, crowd control was
PvP only. `TryPvpCrowdControl` returned early unless a player-shaped enemy was
involved. In PvE, a caster could mezz the monster it was attacking, and no
companion skipped mezzed monsters when choosing a target, so every PvE mezz was
broken at once.

- [x] Role: `BotPveGroupRole.CrowdControl` (saved as `crowdcontrol`; commands
      also take `cc` and `crowd control`). It is added after the four existing
      values, so the native filter's control IDs are unchanged. It is class-legal
      for the classes with a plain, non-pulsing mesmerize line in the runtime
      spell table: Healer (Pacification), Sorcerer (Mind), Bard (Music),
      Mentalist (Mentalism), and Spiritmaster (Suppression). Minstrel mezzes
      only with a pulsing song, which the policy does not maintain.
- [x] Mezz, not root: PvE add control uses only mesmerize. A rooted monster
      keeps fighting anyone in melee range and can still cast, so roots stay
      with the existing PvP policy.
- [x] Target choice (`CompanionAddControl`, `BotBrain.PveCrowdControl`): nothing
      happens until the group has a focus target, so a pull is never mezzed. The
      focus is every member's attack or harmful-cast target, each companion's
      ordered pull, their pets' targets, and the owner's target. An add is a
      monster within 1,500 units that is attacking a group member or pet, or is
      on the caster's aggro list. Adds hitting non-tanks come first, then the
      most adds caught, then the nearest. Adds are skipped when they are immune,
      under the NPC diminishing-return timer, below 75% health (the native
      "enraged" resist), or taking damage over time. The shared short-lived
      reservation keeps two companions off the same add. Single-target mezzes
      come first; an area mezz is used only when no focus target is inside its
      radius.
- [x] Breaking rules: companions in a player-led group leave a mezzed monster
      alone while another enemy is left, and fall back to it only when nothing
      else remains. They skip harmful area spells whose radius holds a
      protected mezz. If the owner attacks the mezzed monster, companions may
      attack it too. Real players are never restricted, and autonomous bots
      keep their rules.
- [x] Priorities: a Healer (any role) heals first, then controls. A support
      companion's own mezz cast is no longer cancelled by its hold-back-from-melee
      step. A Sorcerer, Bard, Mentalist, or Spiritmaster in the Crowd control role
      attacks when there is nothing to control.
- [x] Build roles: choosing a build (window, `/companions build`, or recruitment)
      saves its primary role in the same save as the new build, with rollback on
      failure. Owner's Healer mapping: Tri-spec = Healer + add control, Mending =
      Healer, Augmentation = Buffer, Pacification = Crowd control. Project
      mapping for the other builds: Sorcerer Body and Mind and Bard Music =
      Crowd control; Bard Nurture and Shaman Augmentation = Buffer; Friar Group
      support = Healer; Armsman Two-handed = Attacker; all others keep the class
      default. Existing records keep their saved role until a build is chosen.
- [x] Tests: role numbers and aliases, class legality, the Healer mapping,
      every build's role is class-legal, build text names the role, and the `cc`
      command saves only for a class that can fill it. The AI needs live
      monsters and is left to the owner check.
- [ ] Owner: in the real client, group with a Pacification Healer (or a Sorcerer
      on Body and Mind), pull two or three monsters, and check that the extra
      monsters are mezzed, the pulled target is not, other companions leave the
      mezzed ones alone until the first dies, and a Tri-spec Healer still heals
      first. Switch builds and check the role follows.

Known limits: companions' pets are not held back from mezzed monsters, and a
monster that a real player's area spell wakes is simply mezzed again.

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

**Owner decision (2026-09-24):** all four go in the window: the group order
row, pull, invite/bench all, and grind.

### M2 - Group orders row (0.37.0, offline)

Server only. The installed manager `game.dll` needs no change.

- [x] Roster list: a **Group orders** row comes first whenever the roster has
      companions or the player is in a group. Search and filters do not hide
      it, and a companion stays the default selection. Its list text shows the
      order (`order: defensive` or `order: saved stances`) and `grinding`.
- [x] Detail: the four orders as links with the current one marked; each
      grouped companion's effective stance, with `(saved: ...)` when the order
      overrides it (clicking a saved companion opens it); temporary helpers are
      listed as such. Per-companion role, stance, training mode, train, and
      respec were already in the window.
- [x] Actions: **[Pull]** orders the pull on the current target through the
      `/pull` code. **[Invite all]** invites the benched companions shown in the
      list, top to bottom, until the group is full (filters choose who comes).
      **[Bench all]** benches every active companion. **[Grind]**/**[Stop
      grind]** use the `/grind` code; grind still accepts only temporary
      `/spawn` helpers, and the refusal explains that.
- [x] Commands and window share one code path (`CompanionGroupOrders`,
      `PullGroupCommandHandler.Order`); command behaviour is unchanged.
- [x] Test: the group row leads the list without taking the default selection;
      Defensive is applied and shown per companion; pull without a target,
      invite all outside the world, and grind with a saved companion report
      the existing refusals; saved stances clears the order.
- [ ] Owner: in the real client, select Group orders, switch between the four
      orders and watch companions react, pull a target with **[Pull]**, and use
      **[Bench all]** then **[Invite all]** with a realm or search filter.
      Start and stop **[Grind]** with `/spawn` helpers.

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

**Owner decision (2026-09-24):** option 2, the second vault page. Option 1
stays a possible later probe; it is not planned.

### M3 - Worn slots in the bag window (0.38.0, offline)

Server only. The installed manager `game.dll` needs no change.

- [x] Layout: the bag view now fills all 100 house-vault positions. Positions
      1-40 stay the backpack; 41-50 are empty; positions 51-69 are the worn
      slots, two per row in the client's two-column list: helm, chest / arms,
      gloves / legs, boots / cloak, neck / jewel, belt / left, right wrist /
      left, right ring / right hand, left hand / two-handed, ranged / mythical.
      Position 51 starts a new page for page sizes of 10, 25, or 50 slots.
      Quivers are not shown.
- [x] Equip: dropping a companion bag item on a worn position calls
      `PersistentCompanionGear.TryEquip`, the **[Equip + lock]** path. The item
      goes to its own legal slot, whatever position it was dropped on. A ring or
      bracer takes the side it was dropped on. The slot is locked.
- [x] Unequip: dragging a worn item onto an empty companion bag position calls
      `TryUnequip` into that exact bag slot and unlocks the slot.
- [x] Refused: worn to worn, worn to the player's bag, the player's bag to a
      worn position, and the empty positions 41-50 and 70-100 each explain the
      right drag. Nothing moves.
- [x] Protections: every change still goes through `TryApplyEquipmentMutation`
      with class legality, displaced-weapon space, and slot locks; the bag
      still needs an active, nearby companion out of combat.
- [x] Manager: the Gear tab and the **[Open bag]** message name the worn
      positions.
- [x] Test: backpack and worn positions round-trip, they do not overlap or
      leave the vault, and ring and wrist pairs share a row. Equip and unequip
      reuse the existing roster paths; the real drags need the client.
- [ ] Owner: in the real client, open a companion's bag, page to position 51,
      and check that the worn items appear there in the order above. Drop a
      helm and a ring from the companion's bag onto the worn positions, drag a
      worn item back to an empty bag slot, and check that the Gear tab and the
      companion's appearance follow. Note the client's page size.

Known limits: empty worn positions have no slot names or silhouettes, so the
order above (also listed in the Gear tab) is the key. The bag window does not
refresh by itself when the companion equips loot automatically; reopen it or
make any move to refresh.

## Order and gates

M0, then M1, then M2, which carry no native risk and give the most gameplay
value. M3 uses the server-only vault page (option 2); a native slot-grid probe
would come later only if the owner asks for it. Each milestone follows the repository's
versioning, uses offline tests before any owner check, and closes only after
the owner confirms it in the real client.
