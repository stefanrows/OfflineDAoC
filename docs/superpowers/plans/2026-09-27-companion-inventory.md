# Companion Inventory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The owner sees and changes a roster companion's worn gear from the bag window (drag) and the Gear tab (click), and a manual choice gives way only to a clearly better earned item.

**Architecture:** A per-slot mark (`None`/`Manual`/`Locked`) replaces the boolean slot lock inside the existing `SerializedEquipmentState` string. The auto-upgrade path raises its minimum for `Manual` slots. The house-vault bag view maps worn slots to positions 1-19 and the backpack to 21-60; moves are classified by a pure function and executed through `PersistentCompanionGear`, which the Gear tab also uses. Item info reuses the native delve text via an extracted builder.

**Tech Stack:** C# / .NET 10, OpenDAoC server (`source/server`), NUnit.

**Spec:** `docs/superpowers/specs/2026-09-27-companion-inventory-design.md`

## Global Constraints

- Scope: persistent roster companions only (`GameBot.IsPersistentPlayerCompanion`); temporary helpers and autonomous bots unchanged.
- No schema change; state stays in `PlayerCompanionRecord.SerializedEquipmentState` (`s:<slot>` values `L` and new `M`).
- Existing `L` values stay hard locks. Unknown non-`M` values count as hard locks.
- Manual-choice margin: improvement must exceed `max(8, ceil(5% of the removed items' EquipmentValue))`.
- Only items flagged `E` are auto-equipped (unchanged).
- Access rules unchanged: `PlayerCompanionRoster.CanManageInventory` (grouped, same region, 400 units).
- Preserve each file's line endings: CRLF in `PlayerCompanionRoster.cs`, `PersistentCompanionInventoryView.cs`, `DetailDisplayHandler.cs`, and `GameBot.cs`' surrounding region (check with `sed -n 'X,Yp' | cat -A`); LF in `AutonomousBotEconomy.cs`, `PersistentCompanionGear.cs`, `CompanionManager.cs`, `UT_*.cs` listed below. Do not convert whole files.
- Manager window limit: at most 6 action buttons (`CompanionManagerProtocol.Actions`).
- Version: one MINOR bump for the whole feature (0.91.0 if main is still 0.90.0), done in Task 7 only.
- Test command (from `source/server`): `~/.dotnet/dotnet test Tests/Tests.csproj -c Release --filter "FullyQualifiedName~<Fixture>"`. Pre-existing order-dependent failures exist in other fixtures; run the named fixtures only.

## Review Focus

1. Owner drags an item the companion cannot use onto a worn position → nothing leaves the owner's bag; a clear reason is told (Task 4 test `GiveAndEquipRejectsUnusableBeforeTransfer` via the pure pre-check).
2. Owner drags a worn starter item to their own bag → it stays with the companion in its backpack, with a message; never lost (Task 5 routing test + Task 4 code path).
3. A legacy save with `s:<slot>=L` → still a hard lock; auto-upgrade never touches it (Task 1 test `LegacyLockStaysHard`).
4. Two-hander auto-upgrade displaces a manual shield + weapon → margin applies to the combined removed value (Task 2 test `MarginUsesAllRemovedItems`).
5. Companion backpack full when the owner equips from their bag → the displaced worn item needs a free slot; refusal must happen before transfer, or the transfer is rolled back (Task 4 code; checked in review).

---

### Task 1: Slot marks

**Files:**
- Modify: `source/server/GameServer/bots/PlayerCompanionRoster.cs:53-74` (CRLF)
- Modify: `source/server/GameServer/bots/PlayerCompanionRoster.cs:1760-1774` (starter refresh)
- Modify: `source/server/GameServer/bots/GameBot.cs:4976` (manual equip writes Manual)
- Test: `source/server/Tests/UnitTests/UT_PlayerCompanionGearRewards.cs` (LF)

**Interfaces:**
- Produces: `public enum eCompanionSlotMark { None, Manual, Locked }` (namespace `DOL.GS`, in `PlayerCompanionRoster.cs` above the class);
  `PlayerCompanionRoster.GetEquipmentSlotMark(PlayerCompanionRecord, eInventorySlot) : eCompanionSlotMark`;
  `PlayerCompanionRoster.SetEquipmentSlotMark(PlayerCompanionRecord, eInventorySlot, eCompanionSlotMark) : bool`;
  `IsEquipmentSlotLocked` now means `Locked` only; `SetEquipmentSlotLocked(record, slot, true/false)` writes `Locked`/`None`.

- [ ] **Step 1: Write the failing tests** (append to `UT_PlayerCompanionGearRewards`)

```csharp
    [Test]
    public void ManualMarkIsNotAHardLock()
    {
        var record = new PlayerCompanionRecord { CompanionId = "mark" };
        PlayerCompanionRoster.SetEquipmentSlotMark(record, eInventorySlot.HeadArmor, eCompanionSlotMark.Manual);

        Assert.That(PlayerCompanionRoster.GetEquipmentSlotMark(record, eInventorySlot.HeadArmor), Is.EqualTo(eCompanionSlotMark.Manual));
        Assert.That(PlayerCompanionRoster.IsEquipmentSlotLocked(record, eInventorySlot.HeadArmor), Is.False);

        PlayerCompanionRoster.SetEquipmentSlotMark(record, eInventorySlot.HeadArmor, eCompanionSlotMark.None);
        Assert.That(PlayerCompanionRoster.GetEquipmentSlotMark(record, eInventorySlot.HeadArmor), Is.EqualTo(eCompanionSlotMark.None));
    }

    [Test]
    public void LegacyLockStaysHard()
    {
        var record = new PlayerCompanionRecord { CompanionId = "legacy" };
        PlayerCompanionRoster.SetEquipmentSlotLocked(record, eInventorySlot.Cloak, true);

        Assert.That(PlayerCompanionRoster.GetEquipmentSlotMark(record, eInventorySlot.Cloak), Is.EqualTo(eCompanionSlotMark.Locked));
        Assert.That(PlayerCompanionRoster.IsEquipmentSlotLocked(record, eInventorySlot.Cloak), Is.True);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `~/.dotnet/dotnet test Tests/Tests.csproj -c Release --filter "FullyQualifiedName~UT_PlayerCompanionGearRewards"`
Expected: build error, `eCompanionSlotMark` / `GetEquipmentSlotMark` not defined.

- [ ] **Step 3: Implement** (replace `IsEquipmentSlotLocked`/`SetEquipmentSlotLocked`, CRLF)

```csharp
        public static eCompanionSlotMark GetEquipmentSlotMark(PlayerCompanionRecord record, eInventorySlot slot)
        {
            if (record == null)
                return eCompanionSlotMark.None;
            lock (record)
            {
                if (!ParseEquipmentState(record.SerializedEquipmentState).TryGetValue("s:" + (int)slot, out string value))
                    return eCompanionSlotMark.None;
                // Unknown values from other versions stay protective.
                return value == "M" ? eCompanionSlotMark.Manual : eCompanionSlotMark.Locked;
            }
        }

        public static bool SetEquipmentSlotMark(PlayerCompanionRecord record, eInventorySlot slot, eCompanionSlotMark mark)
        {
            if (record == null)
                return false;
            lock (record)
            {
                Dictionary<string, string> state = ParseEquipmentState(record.SerializedEquipmentState);
                string key = "s:" + (int)slot;
                if (mark == eCompanionSlotMark.None)
                    state.Remove(key);
                else
                    state[key] = mark == eCompanionSlotMark.Manual ? "M" : "L";
                WriteEquipmentState(record, state);
                return true;
            }
        }

        /// <summary>True for a hard lock only; a manual choice can still be upgraded with a margin.</summary>
        public static bool IsEquipmentSlotLocked(PlayerCompanionRecord record, eInventorySlot slot) =>
            GetEquipmentSlotMark(record, slot) == eCompanionSlotMark.Locked;

        public static bool SetEquipmentSlotLocked(PlayerCompanionRecord record, eInventorySlot slot, bool locked) =>
            SetEquipmentSlotMark(record, slot, locked ? eCompanionSlotMark.Locked : eCompanionSlotMark.None);
```

Enum, above `public static class PlayerCompanionRoster`:

```csharp
    /// <summary>Owner mark on a companion's worn slot.</summary>
    public enum eCompanionSlotMark
    {
        None,
        /// <summary>Owner equipped it; a clearly better earned item may replace it.</summary>
        Manual,
        /// <summary>Owner locked it; never replaced automatically.</summary>
        Locked,
    }
```

Starter refresh (`UpgradeStarterEquipment`, three places): replace `!IsEquipmentSlotLocked(record, X)` with `GetEquipmentSlotMark(record, X) == eCompanionSlotMark.None`.

Manual equip (`GameBot.cs:4976`, inside `TryManuallyEquipPersistentCompanionItem`):

```csharp
                if (!PlayerCompanionRoster.IsEquipmentSlotLocked(PlayerCompanionRecord, target))
                    PlayerCompanionRoster.SetEquipmentSlotMark(PlayerCompanionRecord, target, eCompanionSlotMark.Manual);
```

Legacy menu `PersistentCompanionMenu.cs:359, 437`: use `GetEquipmentSlotMark(...) != eCompanionSlotMark.None` so its "locked" label keeps covering both.

- [ ] **Step 4: Run tests** — same command. Expected: PASS (including the existing `ItemProvenanceAndSlotLocksRoundTripPerCompanion`).

- [ ] **Step 5: Commit**

```bash
git add source/server/GameServer/bots/PlayerCompanionRoster.cs source/server/GameServer/bots/GameBot.cs source/server/GameServer/commands/playercommands/PersistentCompanionMenu.cs source/server/Tests/UnitTests/UT_PlayerCompanionGearRewards.cs
git commit -m "feat(companions): manual slot mark alongside hard lock"
```

---

### Task 2: Margin for manual choices, pair preference, swap notice

**Files:**
- Modify: `source/server/GameServer/bots/autonomous/AutonomousBotEconomy.cs:206-265, 424-460` (LF)
- Modify: `source/server/GameServer/bots/GameBot.cs:4779-4840` (`TryEquipPersistentCompanionUpgrade`)
- Test: `source/server/Tests/UnitTests/UT_PlayerCompanionGearRewards.cs`

**Interfaces:**
- Consumes: Task 1 `eCompanionSlotMark`, `GetEquipmentSlotMark`, `SetEquipmentSlotMark`.
- Produces: `AutonomousBotEconomy.ManualChoiceMinimum(int minimumImprovement, int removedValue) : int`;
  `ChooseCompanionPairSlot(first, firstLevel, firstLocked, second, secondLevel, secondLocked, incomingLevel, ignoreLocks, tieBreak, bool firstManual = false, bool secondManual = false)`.

- [ ] **Step 1: Write the failing tests**

```csharp
    [TestCase(0, 100, 8)]     // floor of 8
    [TestCase(0, 800, 40)]    // 5% of 800
    [TestCase(0, 801, 41)]    // rounds up
    [TestCase(50, 800, 50)]   // a larger caller minimum wins
    public void ManualChoiceNeedsAClearMargin(int callerMinimum, int removedValue, int expected)
    {
        Assert.That(AutonomousBotEconomy.ManualChoiceMinimum(callerMinimum, removedValue), Is.EqualTo(expected));
    }

    [Test]
    public void MarginUsesAllRemovedItems()
    {
        // A two-hander removing a manual weapon (400) and shield (400) needs > 40, not > 20.
        Assert.That(AutonomousBotEconomy.ManualChoiceMinimum(0, 400 + 400), Is.EqualTo(40));
    }

    [Test]
    public void PairPrefersUnmarkedSlotOverManualChoice()
    {
        Assert.That(AutonomousBotEconomy.ChooseCompanionPairSlot(
            eInventorySlot.LeftRing, 10, false, eInventorySlot.RightRing, 30, false,
            40, false, 0, firstManual: true, secondManual: false), Is.EqualTo(eInventorySlot.RightRing),
            "The owner's ring stays while the other side can take the upgrade.");
        Assert.That(AutonomousBotEconomy.ChooseCompanionPairSlot(
            eInventorySlot.LeftRing, 10, false, eInventorySlot.RightRing, 30, true,
            40, false, 0, firstManual: true, secondManual: false), Is.EqualTo(eInventorySlot.LeftRing),
            "With the other side hard-locked, the manual side is the only candidate.");
    }
```

- [ ] **Step 2: Run to verify failure** — `--filter "FullyQualifiedName~UT_PlayerCompanionGearRewards"`; expected build error.

- [ ] **Step 3: Implement**

In `AutonomousBotEconomy`:

```csharp
    /// <summary>
    /// Minimum improvement to replace an owner's manual choice: at least 8 and
    /// 5% of everything the move removes, so near-equal loot never undoes it.
    /// </summary>
    public static int ManualChoiceMinimum(int minimumImprovement, int removedValue) =>
        Math.Max(Math.Max(minimumImprovement, MinimumEquipmentUpgrade), (int)Math.Ceiling(removedValue * 0.05));
```

In `TryGetEquipmentUpgrade`, after `displaced` is computed and before `return IsEquipmentUpgrade(...)`:

```csharp
            if (!ignoreCompanionSlotLocks && bot.IsPersistentPlayerCompanion &&
                (PlayerCompanionRoster.GetEquipmentSlotMark(bot.PlayerCompanionRecord, equipSlot) == eCompanionSlotMark.Manual ||
                 displaced.Any(worn => PlayerCompanionRoster.GetEquipmentSlotMark(bot.PlayerCompanionRecord,
                     (eInventorySlot)worn.SlotPosition) == eCompanionSlotMark.Manual)))
                minimumImprovement = ManualChoiceMinimum(minimumImprovement,
                    EquipmentValue(equipped) + displaced.Sum(EquipmentValue));
```

Also in `TryGetEquipmentUpgrade`, a displaced hard-locked weapon must block: add before the margin block

```csharp
            if (!ignoreCompanionSlotLocks && bot.IsPersistentPlayerCompanion &&
                displaced.Any(worn => PlayerCompanionRoster.IsEquipmentSlotLocked(bot.PlayerCompanionRecord,
                    (eInventorySlot)worn.SlotPosition)))
                return false;
```

`PreferredCompanionPairSlot`: pass `firstManual`/`secondManual` from `GetEquipmentSlotMark(...) == Manual`. `ChooseCompanionPairSlot`: after building `choices` (hard locks excluded), add

```csharp
        if (!ignoreLocks)
        {
            var unmarked = choices.Where(choice => !(choice.Slot == first ? firstManual : secondManual)).ToList();
            if (unmarked.Count > 0)
                choices = unmarked;
        }
```

In `GameBot.TryEquipPersistentCompanionUpgrade`: inside the mutation lambda, before the moves, record
`var replacedManual = new List<DbInventoryItem>();` — for `target` and each displaced slot whose mark is `Manual`, add the worn item and call `SetEquipmentSlotMark(PlayerCompanionRecord, slot, eCompanionSlotMark.None)` (inside the lambda so a failed mutation restores the state string). After a successful equip:

```csharp
            if (replacedManual.Count > 0 && Owner is GamePlayer owner)
                owner.Out.SendMessage($"{Name} swapped your {string.Join(" and ", replacedManual.Select(old => old.Name))} " +
                    $"for the better {item.Name}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
```

Declare `replacedManual` outside the lambda and clear it at the start of the lambda (the lambda may run once; clearing keeps it correct).

- [ ] **Step 4: Run tests** — expected PASS, including the existing pair tests (defaults keep old behavior).

- [ ] **Step 5: Commit** — `git commit -m "feat(companions): clearly better loot may replace a manual choice"`

---

### Task 3: Native item info from the Gear tab

**Files:**
- Modify: `source/server/GameServer/packets/Client/168/DetailDisplayHandler.cs:114-229` (CRLF, tabs)
- Modify: `source/server/GameServer/commands/playercommands/PersistentCompanionGear.cs` (LF)

**Interfaces:**
- Produces: `DetailDisplayHandler.WriteInventoryItemInfo(GameClient client, DbInventoryItem invItem, List<string> objectInfo) : string` (returns the caption);
  `PersistentCompanionGear.ShowItemInfo(GamePlayer player, DbInventoryItem item) : void`.

- [ ] **Step 1: Extract** the body of `case 1/10` from `caption = invItem.Name;` to the end of the case (before its final `break;`) into

```csharp
		/// <summary>The classic item delve text, shared by inventory clicks and the Companion Manager.</summary>
		public static string WriteInventoryItemInfo(GameClient client, DbInventoryItem invItem, List<string> objectInfo)
		{
			// moved body; the early `break;` after DelveItem becomes `return invItem.Name;`
			return invItem.Name;
		}
```

and call it in the case: `caption = WriteInventoryItemInfo(client, invItem, objectInfo);`. Move the code verbatim; keep tabs and CRLF.

- [ ] **Step 2: Add the helper** to `PersistentCompanionGear`:

```csharp
        /// <summary>Opens the same item info window a player gets by delving their own item.</summary>
        public static void ShowItemInfo(GamePlayer player, DbInventoryItem item)
        {
            if (player?.Client == null || item == null)
                return;
            var info = new List<string>();
            string caption = DOL.GS.PacketHandler.Client.v168.DetailDisplayHandler.WriteInventoryItemInfo(player.Client, item, info);
            if (info.Count > 0)
                player.Out.SendCustomTextWindow(caption, info);
        }
```

(Confirm the handler's namespace with `grep -n "^namespace" DetailDisplayHandler.cs` and adjust the qualifier.)

- [ ] **Step 3: Build** — `~/.dotnet/dotnet build source/server/CoreServer/CoreServer.csproj -c Release`. Expected: success. Behavior of normal delves is unchanged (pure move); real-client check in Task 7.

- [ ] **Step 4: Commit** — `git commit -m "refactor(delve): reusable item info builder for companion gear"`

---

### Task 4: Gear operations across both inventories

**Files:**
- Modify: `source/server/GameServer/commands/playercommands/PersistentCompanionGear.cs`
- Test: `source/server/Tests/UnitTests/UT_CompanionManager.cs` (LF)

**Interfaces:**
- Consumes: Task 1 marks; existing `TryTransferItem`, `TryEquip`, `TryUnequip`, `CanReturnItemToOwner`, `GameBot.GetManualEquipmentSlot`.
- Produces:
  `PersistentCompanionGear.OwnerItemsFitting(GamePlayer owner, GameBot companion, eInventorySlot slot) : IReadOnlyList<DbInventoryItem>`;
  `PersistentCompanionGear.TryGiveAndEquip(GamePlayer owner, string companionId, string ownerItemId, eInventorySlot slot, out string message) : bool`;
  `PersistentCompanionGear.TryUnequipToOwner(GamePlayer owner, string companionId, eInventorySlot slot, string expectedItemId, out string message) : bool`;
  `PersistentCompanionGear.CanGiveForSlot(eInventorySlot resolved, eInventorySlot slot, bool transferable, out string blocker) : bool` (pure pre-check).

- [ ] **Step 1: Write the failing test** (pure pre-check)

```csharp
    [Test]
    public void GiveAndEquipRejectsUnusableBeforeTransfer()
    {
        Assert.That(PersistentCompanionGear.CanGiveForSlot(eInventorySlot.Invalid, eInventorySlot.HeadArmor, true, out string unusable), Is.False);
        Assert.That(unusable, Does.Contain("cannot use"));
        Assert.That(PersistentCompanionGear.CanGiveForSlot(eInventorySlot.TorsoArmor, eInventorySlot.HeadArmor, true, out string wrongSlot), Is.False);
        Assert.That(wrongSlot, Does.Contain("does not fit"));
        Assert.That(PersistentCompanionGear.CanGiveForSlot(eInventorySlot.HeadArmor, eInventorySlot.HeadArmor, false, out _), Is.False);
        Assert.That(PersistentCompanionGear.CanGiveForSlot(eInventorySlot.LeftRing, eInventorySlot.RightRing, true, out _), Is.True);
    }
```

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~UT_CompanionManager"`; expected build error.

- [ ] **Step 3: Implement**

```csharp
        public static bool CanGiveForSlot(eInventorySlot resolved, eInventorySlot slot, bool transferable, out string blocker)
        {
            blocker = resolved == eInventorySlot.Invalid ? "the companion cannot use that item"
                : !FitsSlot(resolved, slot) ? $"it does not fit the {SlotName(slot)} slot"
                : !transferable ? "that item cannot be traded"
                : string.Empty;
            return blocker.Length == 0;
        }

        /// <summary>The owner's backpack items the companion could wear in <paramref name="slot"/>, best first.</summary>
        public static IReadOnlyList<DbInventoryItem> OwnerItemsFitting(GamePlayer owner, GameBot companion, eInventorySlot slot) =>
            owner?.Inventory == null || companion == null
                ? Array.Empty<DbInventoryItem>()
                : owner.Inventory.AllItems.Where(IsBackpack)
                    .Where(item => PlayerCompanionRoster.CanTransferItem(item, out _) &&
                                   FitsSlot(companion.GetManualEquipmentSlot(item), slot))
                    .OrderByDescending(AutonomousBotEconomy.EquipmentValue)
                    .ThenBy(item => item.SlotPosition)
                    .ToArray();

        /// <summary>Owner item → companion, then equipped in <paramref name="slot"/>; rolled back if the equip fails.</summary>
        public static bool TryGiveAndEquip(GamePlayer owner, string companionId, string ownerItemId, eInventorySlot slot,
            out string message)
        {
            DbInventoryItem item = owner?.Inventory?.AllItems.FirstOrDefault(entry => entry.ObjectId == ownerItemId);
            if (item == null || !IsBackpack(item) ||
                !PlayerCompanionRoster.TryGetActiveCompanionById(owner, companionId, out GameBot companion))
            {
                message = "Choose an item from your backpack while the companion is near you.";
                return false;
            }
            if (!CanGiveForSlot(companion.GetManualEquipmentSlot(item), slot,
                    PlayerCompanionRoster.CanTransferItem(item, out _), out string blocker))
            {
                message = $"{item.Name}: {blocker}.";
                return false;
            }
            if (companion.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack) ==
                eInventorySlot.Invalid && companion.Inventory.GetItem(slot) != null)
            {
                message = $"{companion.Name}'s backpack is full; make room for the item it takes off.";
                return false;
            }
            if (!PlayerCompanionRoster.TryTransferItem(owner, companionId, ownerItemId, toCompanion: true, out message))
                return false;
            if (TryEquip(owner, companionId, ownerItemId, slot, out message))
            {
                RefreshOwnerBackpack(owner);
                return true;
            }
            string reason = message;
            PlayerCompanionRoster.TryTransferItem(owner, companionId, ownerItemId, toCompanion: false, out _);
            RefreshOwnerBackpack(owner);
            message = $"{item.Name} was not equipped and came back to you: {reason}";
            return false;
        }

        /// <summary>Worn item → owner when the owner may take it; otherwise it stays in the companion's backpack.</summary>
        public static bool TryUnequipToOwner(GamePlayer owner, string companionId, eInventorySlot slot, string expectedItemId,
            out string message)
        {
            if (!TryUnequip(owner, companionId, slot, expectedItemId, out message))
                return false;
            if (!PlayerCompanionRoster.TryGetActiveCompanionById(owner, companionId, out GameBot companion) ||
                companion.Inventory.AllItems.FirstOrDefault(entry => entry.ObjectId == expectedItemId) is not DbInventoryItem item ||
                !PlayerCompanionRoster.CanReturnItemToOwner(item, companion.PlayerCompanionRecord, out string blocker))
            {
                message += " It stays in the companion's backpack because it cannot be handed over.";
                return true;
            }
            if (!TryReturnToOwner(owner, companionId, expectedItemId, out string returned))
                message += $" {returned}";
            else
                message = $"{item.Name} is unequipped and back in your backpack.";
            return true;
        }

        private static void RefreshOwnerBackpack(GamePlayer owner) =>
            owner.Out.SendInventorySlotsUpdate(Enumerable.Range((int)eInventorySlot.FirstBackpack, 40)
                .Select(slot => (eInventorySlot)slot).ToArray());
```

Also change `TryEquip`'s success text to: `$"{item.Name} is equipped in the {SlotName(slot)} slot. It stays until you change it or {companion.Name} finds something clearly better."` and use `RefreshOwnerBackpack` in `TryReturnToOwner`. Confirm `PlayerCompanionRoster.CanTransferItem` is public (make it public if internal).

- [ ] **Step 4: Run tests** — expected PASS.

- [ ] **Step 5: Commit** — `git commit -m "feat(companions): give-and-equip and unequip-to-owner gear operations"`

---

### Task 5: Bag window shows worn slots

**Files:**
- Modify: `source/server/GameServer/commands/playercommands/PersistentCompanionInventoryView.cs` (CRLF, whole class)
- Test: `source/server/Tests/UnitTests/UT_CompanionManager.cs`

**Interfaces:**
- Consumes: Task 4 operations; `PersistentCompanionGear.SheetSlots`.
- Produces: `PersistentCompanionInventoryView.WornPosition(eInventorySlot worn) : int` (1-based, 0 if none), `BackpackFirstPosition = 21`, `ClassifyMove(Area from, Area to) : MoveKind`, `enum Area { OwnerBackpack, Worn, CompanionBackpack, Other }`, `enum MoveKind { GiveAndEquip, UnequipToOwner, Equip, Unequip, WithinCompanion, GiveToCompanion, ReturnToOwner, Refused }` (all `internal`; tests use `InternalsVisibleTo` if present, else make them `public`).

- [ ] **Step 1: Write the failing tests**

```csharp
    [Test]
    public void InventoryWindowPutsWornSlotsFirstThenBackpack()
    {
        Assert.That(PersistentCompanionInventoryView.WornPosition(eInventorySlot.HeadArmor), Is.EqualTo(1));
        Assert.That(PersistentCompanionInventoryView.WornPosition(eInventorySlot.Mythical), Is.EqualTo(19));
        Assert.That(PersistentCompanionInventoryView.WornPosition(eInventorySlot.RightRing),
            Is.EqualTo(PersistentCompanionInventoryView.WornPosition(eInventorySlot.LeftRing) + 1));
        Assert.That(PersistentCompanionInventoryView.BackpackFirstPosition, Is.EqualTo(21));
        Assert.That(PersistentCompanionInventoryView.BackpackFirstPosition + 39, Is.LessThanOrEqualTo(100));
    }

    [TestCase(PersistentCompanionInventoryView.Area.OwnerBackpack, PersistentCompanionInventoryView.Area.Worn, PersistentCompanionInventoryView.MoveKind.GiveAndEquip)]
    [TestCase(PersistentCompanionInventoryView.Area.Worn, PersistentCompanionInventoryView.Area.OwnerBackpack, PersistentCompanionInventoryView.MoveKind.UnequipToOwner)]
    [TestCase(PersistentCompanionInventoryView.Area.CompanionBackpack, PersistentCompanionInventoryView.Area.Worn, PersistentCompanionInventoryView.MoveKind.Equip)]
    [TestCase(PersistentCompanionInventoryView.Area.Worn, PersistentCompanionInventoryView.Area.CompanionBackpack, PersistentCompanionInventoryView.MoveKind.Unequip)]
    [TestCase(PersistentCompanionInventoryView.Area.Worn, PersistentCompanionInventoryView.Area.Worn, PersistentCompanionInventoryView.MoveKind.Refused)]
    [TestCase(PersistentCompanionInventoryView.Area.CompanionBackpack, PersistentCompanionInventoryView.Area.CompanionBackpack, PersistentCompanionInventoryView.MoveKind.WithinCompanion)]
    [TestCase(PersistentCompanionInventoryView.Area.OwnerBackpack, PersistentCompanionInventoryView.Area.CompanionBackpack, PersistentCompanionInventoryView.MoveKind.GiveToCompanion)]
    [TestCase(PersistentCompanionInventoryView.Area.CompanionBackpack, PersistentCompanionInventoryView.Area.OwnerBackpack, PersistentCompanionInventoryView.MoveKind.ReturnToOwner)]
    [TestCase(PersistentCompanionInventoryView.Area.Other, PersistentCompanionInventoryView.Area.Worn, PersistentCompanionInventoryView.MoveKind.Refused)]
    public void InventoryWindowRoutesEachDrag(PersistentCompanionInventoryView.Area from,
        PersistentCompanionInventoryView.Area to, PersistentCompanionInventoryView.MoveKind expected)
    {
        Assert.That(PersistentCompanionInventoryView.ClassifyMove(from, to), Is.EqualTo(expected));
    }
```

(If the view stays `internal`, make it and the enums `public` — the fixture lives in another assembly; the previous fix for this repo made members public.)

- [ ] **Step 2: Run** — expected build error.

- [ ] **Step 3: Implement** (CRLF). Key pieces:

```csharp
        internal const int WornCount = 19;
        public const int BackpackFirstPosition = 21;
        private const int BackpackSize = 40;

        public enum Area { OwnerBackpack, Worn, CompanionBackpack, Other }
        public enum MoveKind { GiveAndEquip, UnequipToOwner, Equip, Unequip, WithinCompanion, GiveToCompanion, ReturnToOwner, Refused }

        public static int WornPosition(eInventorySlot worn) => Array.IndexOf(PersistentCompanionGear.SheetSlots, worn) + 1;

        public static MoveKind ClassifyMove(Area from, Area to) => (from, to) switch
        {
            (Area.OwnerBackpack, Area.Worn) => MoveKind.GiveAndEquip,
            (Area.Worn, Area.OwnerBackpack) => MoveKind.UnequipToOwner,
            (Area.CompanionBackpack, Area.Worn) => MoveKind.Equip,
            (Area.Worn, Area.CompanionBackpack) => MoveKind.Unequip,
            (Area.CompanionBackpack, Area.CompanionBackpack) => MoveKind.WithinCompanion,
            (Area.OwnerBackpack, Area.CompanionBackpack) => MoveKind.GiveToCompanion,
            (Area.CompanionBackpack, Area.OwnerBackpack) => MoveKind.ReturnToOwner,
            _ => MoveKind.Refused,
        };
```

- `VaultSize => 100`; `LastClientSlot` unchanged formula.
- `GetClientInventory`: worn items (all `SheetSlots` with an item, **unfiltered**) at `HousingInventory_First + WornPosition - 1`; takeable backpack items (existing filter) at `HousingInventory_First + BackpackFirstPosition - 1 + (slot - FirstBackpack)`. `GetDbItems` returns both sets.
- `AreaOf(eInventorySlot clientSlot)`: owner backpack (`IsBackpack`) or `GeneralHousing` target → `OwnerBackpack`/`CompanionBackpack` as today; vault positions 1-19 → `Worn`; 21-60 → `CompanionBackpack`; else `Other`. `WornSlotAt(clientSlot)` and `BackpackSlotAt(clientSlot)` map back.
- `MoveItem`: keep the access check and whole-item check; resolve `item` from the owner backpack or `GetClientInventory`; then `switch (ClassifyMove(...))`:
  - `GiveAndEquip` → `PersistentCompanionGear.TryGiveAndEquip(player, _companionId, item.ObjectId, WornSlotAt(toSlot), out message)`
  - `UnequipToOwner` → `TryUnequipToOwner(player, _companionId, WornSlotAt(fromSlot), item.ObjectId, out message)`
  - `Equip` → `TryEquip(player, _companionId, item.ObjectId, WornSlotAt(toSlot), out message)`
  - `Unequip` → `TryUnequip(player, _companionId, WornSlotAt(fromSlot), item.ObjectId, out message)` (target slot ignored; first free backpack slot)
  - `WithinCompanion`, `GiveToCompanion`, `ReturnToOwner` → existing code blocks, with the backpack mapping through `BackpackSlotAt`
  - `Refused` → `"Drag between your bag, the worn slots (1-19) and the companion's backpack (21-60)."`
- Keep `Refresh()` and the owner-backpack slot update after every successful move.
- Update the class summary: "worn slots at positions 1-19, backpack at 21-60".

- [ ] **Step 4: Run tests** — expected PASS.

- [ ] **Step 5: Commit** — `git commit -m "feat(companions): inventory window with worn slots and drag-to-equip"`

---

### Task 6: Gear tab: legend, owner items, [Equip], [Info], markers

**Files:**
- Modify: `source/server/GameServer/commands/playercommands/CompanionManager.cs:489-497, 640-790, 1026-1036` (LF)
- Modify: `source/server/Tests/UnitTests/UT_CompanionManager.cs:207, 230` (label text only if the test asserts the real label; it builds its own view, so likely unchanged)

**Interfaces:**
- Consumes: Tasks 1, 3, 4, 5.

- [ ] **Step 1: Labels and legend**
  - `[Open bag]` → `[Open inventory]` (both places); `OpenBag` sets `session.DetailTab = CompanionManagerDetailTab.Gear` and message: `$"{companion.Name}'s inventory is open: worn slots 1-19, backpack 21-60. Drag from your bag onto a worn slot to equip."`
  - Worn-slot line: `$"  {WornPosition(slot),2} {SlotLabel(slot)}: {worn?.Name ?? "empty"}{mark}{hint}{marker}"`, where `mark` is `" *"` for `Manual` and `" (locked)"` for `Locked` (via `GetEquipmentSlotMark`). Header line: `"Worn gear (* = your choice; click a slot to see what fits):"`.
  - Backpack line: `"Bag {n}"` becomes `"Bag {n} (window {n + 20})"` only if it fits `MaximumTextLength` (115); otherwise keep `Bag {n}`.

- [ ] **Step 2: Slot detail with owner items**
  - In `BuildGearSlot`: `IReadOnlyList<DbInventoryItem> ownerFits = PersistentCompanionGear.OwnerItemsFitting(player, companion, slot);` list them after the companion's fits under `"From your backpack:"`, suffix `" (yours)"`, key `"own:" + itemId`, click `SelectItem(session, itemId, keepSlot: true)`.
  - `candidate` = selected item if it is in `fits` or `ownerFits`; `bool fromOwner = ownerFits.Contains(candidate)`.
  - Resolve `selected` in `BuildGear` from the companion's inventory **or** the owner's backpack (so owner selections survive the refresh).
  - `[Equip]` (renamed): `fromOwner ? TryGiveAndEquip(player, id, candidateId, slot, out message) : TryEquip(player, id, candidateId, slot, out message)`.
  - Empty-fit text: `"Nothing in either backpack fits this slot."`

- [ ] **Step 3: [Info]**
  - In the slot view add `new Choice("[Info]", infoItem != null, "info:" + infoItem?.ObjectId, () => { PersistentCompanionGear.ShowItemInfo(player, infoItem); })` where `infoItem = candidate ?? worn`. Order: `[Open inventory]`, `[Equip]`, `[Info]`, `[Unequip]`, `[Lock slot]`, `[Keep]` = 6 (the limit).
  - In the selected-backpack-item view (not slot view) add `[Info]` after `[Equip]` (5 buttons).
  - The `Choice.Run` delegate type: match the existing `Choice` constructor (check whether `Run` is `Action`; if it expects a refresh, call `Report(player, session, string.Empty)` after `ShowItemInfo`).
  - Rename the remaining `"[Equip + lock]"` strings and the `"Fits from the bag (select one, then [Equip + lock]):"` hint to `[Equip]`.

- [ ] **Step 4: Build and run** `UT_CompanionManager` and `UT_PlayerCompanionGearRewards` — expected PASS.

- [ ] **Step 5: Commit** — `git commit -m "feat(companions): Gear tab equips from your bag, shows item info and slot legend"`

---

### Task 7: Docs, version, spec note

**Files:**
- Modify: `CHANGELOG.md` (LF top), `source/tools/OfflineDaoc.Launcher/MainForm.cs`, `source/tools/OfflineDaoc.Launcher.Tests/LauncherPresentationTests.cs`, `ALL SERVER COMMANDS.txt` (latin-1; header + the `/companions` Gear sentence at line 254), `docs/TASKS.md`, `docs/COMPANION_MANAGER_INTEGRATION.md` (Gear row + new "Inventory window (0.91.0)" row).

- [ ] **Step 1:** Fetch `origin/main`; if it moved past 0.90.0, rebase and pick the next MINOR.
- [ ] **Step 2:** CHANGELOG `## [0.91.0] - <date>`: Added (inventory window worn slots + drag-to-equip from your bag; Gear tab owner items and [Info]); Changed ([Equip + lock] → [Equip] with manual choice replaced only by clearly better earned loot; [Open bag] → [Open inventory]; backpack moves to window positions 21-60); Fixed: None; Removed: None.
- [ ] **Step 3:** Pins: `DisplayVersion`, launcher test, command header. TASKS: new pending item 23 "Companion inventory window" with real-client checks: vault grid columns / pair adjacency, drag from own bag onto worn slot, [Info] window, auto-swap chat line.
- [ ] **Step 4:** Build both (server + launcher, commands in `docs/DEVELOPMENT.md`); run the three fixtures `UT_PlayerCompanionGearRewards|UT_CompanionManager|UT_CompanionBagVisibility`.
- [ ] **Step 5:** Commit `docs: companion inventory 0.91.0`; shipping follows the owner's instruction (ship now / push only).
