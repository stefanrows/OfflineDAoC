# Companion Stage 4 equipment decisions and acceptance

Updated: 2026-09-23. The owner authorized implementation after offline Stage 3
validation. Equipment rewards, inventory management, and the private clickable
menu are implemented. Focused offline tests and Release builds pass; real-client
acceptance remains pending.

## Reward rules

- Every eligible active companion rolls independently for PvE equipment. The
  chance is `min(100%, 25% × XP_RATE)`, using the player's XP setting: 25% at
  1×, 100% at 4× and above. Party size does not divide a companion's chance.
- Eligibility follows the encounter's existing participation, owner eligibility,
  range, and non-grey rules. Support companions qualify with an eligible owner.
  The owner reaching an XP cap or level 50 does not itself suppress the gear roll.
- A reward-eligible PvP death awards one item per eligible companion, including
  support companions in an eligible owner's party. Human and autonomous-bot
  victims use existing PvP exclusions and repeat-kill protection. Companion or
  controlled-pet damage resolves through the existing reward actor without
  granting companion PvP XP or realm points.
- Class-legal ROG gear uses the companion's current level. Player loot,
  autonomous-bot gear, and temporary `/spawn` helpers keep their separate paths.

## Equipment and inventory rules

- An upgrade considers usable equipment slots, preferring missing equipment and
  then the greatest item-level deficit; ties are randomized. Locked slots are
  excluded when another usable unlocked slot exists. Automatic replacement
  requires a strictly greater score, with legality and scoring shared from
  autonomous equipment code but without autonomous buying, wallet, or
  progression behavior.
- Equipment checks cover trained weapon specializations, armor proficiency,
  shields, handedness, ranged weapons, instruments, and caster focus. A change
  waits until the companion is safely out of combat, casting, and travel. Appearance
  and derived bonuses refresh after equipment changes.
- Manual equipping locks the affected slot. Two-hand conflicts preserve every
  displaced item and refuse changes that would violate a locked slot. Replaced
  gear stays in the companion's 40-slot backpack.
- The generated level-1 kit is marked starter gear (`S`) and stays with its
  original companion. Owner-supplied items are marked (`P`), recoverable, and
  never sold. Companion-earned items use (`E`); keep flags use (`K`). Legacy
  inventory without ownership provenance remains protected from sale and
  outward transfer. Metadata is additive on the companion record; old records
  default to manual training and unknown-item protection.
- Owner transfers require an active nearby companion, idle inventory state,
  available destination space or an eligible earned item to sell, and a
  persisted ordinary tradable item. Quest, relic, siege, and other restricted
  items are rejected. Starter items cannot leave their original companion.
- Automatic sale occurs only to make backpack space. It chooses the lowest
  equipment-score positive-value unequipped companion-earned item. Equipped,
  starter, player-supplied, kept, and unclassified items are never sold. Zero-
  value items do not clear space. Proceeds use normal merchant appraisal and
  credit the owner without a merchant visit.
- Item movement, provenance, companion state, sale removal, and owner coin
  credit are committed in one database transaction. Failed mutations restore
  the prior in-memory item and coin state. Inventory mutations are synchronized
  with companion saves and are rejected during combat.

## Menu and command interface

Bare `/companions` opens a temporary owner-private NPC popup. It provides
paginated roster, detail, build/training, equipment, companion backpack, and
owner backpack pages. Choices resolve to server-side companion/item IDs, and
older visible choices retain their original action for that menu session;
every action checks current ownership, context, slot, capacity, and combat
state. Menus close on expiry, logout, or region/context loss. Respec uses the
existing confirmation dialog. The `/companions` roster, mode, plan, training,
and respec commands stay
available as explicit fallbacks. No native client patch or simulated second
player-inventory window is used.

The owner's later client screenshots exposed raw tokens, accumulated speech
pages, and an overloaded equipment view. The follow-up menu implementation
uses readable choice labels, requests a fresh NPC conversation per page, and
separates equipment from the two backpacks. The companion backpack also opens
in the client's existing housing-vault-style external inventory window. Dragged
items use the companion's validated atomic transfer path; this window never
replaces the player's own inventory. Its appearance and drag behavior remain
pending real-client retest.

## Offline implementation checks

The focused server suite passed 28 tests across personal gear policy and atomic
database helpers. It checks probability boundaries at 1×, 4×, and 10×,
per-companion provenance and slot-lock isolation, paired-slot selection,
every enabled build schedule at every level, and transaction commit/rollback
for updates, inserts, and deletes. A synthetic legacy companion table upgrades
with saved progression intact and manual/unknown-provenance metadata defaults.
Release builds of the server and launcher passed; the launcher suite passed 114
tests. These checks do not establish client popup behavior or gameplay
acceptance.

## Owner real-client checklist

- [ ] Open `/companions`, navigate pages, close it, and confirm another player
      cannot use the popup or its choices.
- [ ] Level a supported companion quickly; check automatic targets across
      multiple-level gains, switch to manual, train, respec, and verify restart
      persistence. Confirm unsupported classes remain manual-only and the group
      window shows each new level immediately.
- [ ] Teleport within a region and across regions with active companions;
      confirm only your living grouped companions arrive and resume following.
- [ ] Run a full party through PvE at 1× and 4× XP; verify independent rolls,
      support eligibility, and unchanged player loot. Repeat with an owner at XP
      cap and with a level-50 owner.
- [ ] Test eligible human and autonomous-bot PvP victims, repeat-kill exclusions,
      support companions, and controlled-pet credit. Confirm no companion XP or
      realm points are awarded.
- [ ] Equip a legal upgrade; verify slot locks, hand conflicts, displaced-item
      recovery, appearance, and bonuses. Confirm a tied or weaker automatic score
      keeps the current item.
- [ ] Transfer ordinary gear both ways; reject restricted items and starter
      gear. Fill both backpacks and confirm failure or eligible surplus sale,
      accurate owner proceeds, protection of kept/supplied/unknown items, and no
      zero-value sale loop.
- [ ] Bench, relog, and restart; verify companion gear, item ownership, locks,
      keep flags, and coin proceeds persist without duplication or loss.
- [ ] Recheck temporary `/spawn` equipment, autonomous equipment, and normal
      player inventory separately.
