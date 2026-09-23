using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    /// <summary>Private, short-lived clickable companion roster and inventory menu.</summary>
    public sealed class PersistentCompanionMenu : GameNPC
    {
        private const string ActiveMenuProperty = "OfflineDAoC.ActivePersistentCompanionMenu";
        private const int LifetimeMilliseconds = 600_000;
        private const int ContextPollMilliseconds = 5_000;
        private const int PageSize = 8;
        private readonly GamePlayer _owner;
        private readonly GameObject _previousTarget;
        private readonly DateTime _expiresUtc = DateTime.UtcNow.AddMilliseconds(LifetimeMilliseconds);
        private readonly Dictionary<string, Action> _choices = new(StringComparer.OrdinalIgnoreCase);
        private ECSGameTimer _expiry;
        private string _generation;
        private int _nextToken;
        private bool _closed;

        private PersistentCompanionMenu(GamePlayer owner)
        {
            _owner = owner;
            _previousTarget = owner.TargetObject;
            Name = "Companion Roster Menu";
            Realm = owner.Realm;
            Level = 1;
            Model = 150;
            Size = 1;
            Flags = eFlags.PEACE | eFlags.DONTSHOWNAME;
            X = owner.X;
            Y = owner.Y;
            Z = owner.Z;
            Heading = owner.Heading;
            CurrentRegionID = owner.CurrentRegionID;
        }

        public static bool Open(GamePlayer owner)
        {
            if (owner?.CurrentRegion == null)
                return false;
            owner.TempProperties.GetProperty<PersistentCompanionMenu>(ActiveMenuProperty)?.Close(true);
            var menu = new PersistentCompanionMenu(owner);
            if (!menu.AddToWorld())
                return false;
            owner.TempProperties.SetProperty(ActiveMenuProperty, menu);
            owner.TargetObject = menu;
            owner.Out.SendChangeTarget(menu);
            menu._expiry = new ECSGameTimer(menu, _ =>
            {
                if (menu._owner?.ObjectState != eObjectState.Active ||
                    menu._owner.CurrentRegion != menu.CurrentRegion || DateTime.UtcNow >= menu._expiresUtc)
                {
                    menu.Close(true);
                    return 0;
                }
                return ContextPollMilliseconds;
            }, ContextPollMilliseconds);
            menu.ShowHome();
            return true;
        }

        public override bool WhisperReceive(GameLiving source, string text)
        {
            string token = text?.Trim().Trim('[', ']', ' ');
            if (token?.LastIndexOf('|') is int separator and >= 0)
                token = token[(separator + 1)..].Trim().Trim('[', ']', ' ');
            if (ReferenceEquals(source, _owner) &&
                (_owner?.ObjectState != eObjectState.Active || _owner.CurrentRegion != CurrentRegion))
            {
                Close(false);
                return false;
            }
            if (!ReferenceEquals(source, _owner) || _closed || string.IsNullOrWhiteSpace(token) ||
                _owner?.ObjectState != eObjectState.Active || _owner.CurrentRegion != CurrentRegion ||
                !token.StartsWith(_generation + ":", StringComparison.Ordinal) ||
                !_choices.TryGetValue(token, out Action action))
                return false;

            _choices.Clear();
            action();
            if (!_closed && _owner?.ObjectState == eObjectState.Active && _owner.CurrentRegion == CurrentRegion)
            {
                _owner.TargetObject = this;
                _owner.Out.SendChangeTarget(this);
            }
            return true;
        }

        private void ShowHome(int page = 0)
        {
            StartPage();
            if (!PlayerCompanionRoster.TryGetRoster(_owner, out List<PlayerCompanionRecord> roster))
            {
                Render("Your roster could not be loaded. Use /companions list or try again later.");
                return;
            }

            int pageCount = Math.Max(1, (int)Math.Ceiling((double)(roster.Count + 1) / PageSize));
            page = Math.Clamp(page, 0, pageCount - 1);
            var lines = new List<string> { $"Companions ({roster.Count}/{PlayerCompanionRoster.MaximumRosterSize}), page {page + 1}/{pageCount}:" };
            var entries = roster.OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
                .Select(record => (Label: $"{record.Name}, level {record.Level} {(eCharacterClass)record.ClassId} ({(record.IsActive ? "active" : "benched")})",
                    Action: (Action)(() => ShowDetails(record.CompanionId))))
                .Append((Label: "Recruit a companion", Action: (Action)(() => ShowRecruit(0))))
                .Skip(page * PageSize).Take(PageSize).ToArray();
            foreach (var entry in entries)
                lines.Add(Link(entry.Label, entry.Action));
            if (page > 0)
                lines.Add(Link("Previous page", () => ShowHome(page - 1)));
            if (page + 1 < pageCount)
                lines.Add(Link("Next page", () => ShowHome(page + 1)));
            lines.Add(Link("Close", () => Close(true)));
            Render(string.Join('\n', lines));
        }

        private void ShowRecruit(int page)
        {
            StartPage();
            var classes = TemporaryGroupClassCatalog.All().OrderBy(entry => entry.Realm)
                .ThenBy(entry => entry.CharacterClass.ToString(), StringComparer.Ordinal).ToArray();
            int pageCount = Math.Max(1, (int)Math.Ceiling((double)classes.Length / PageSize));
            page = Math.Clamp(page, 0, pageCount - 1);
            var lines = new List<string> { $"Choose a class and realm, page {page + 1}/{pageCount}:" };
            foreach (var entry in classes.Skip(page * PageSize).Take(PageSize))
            {
                string label = $"{TemporaryGroupClassCatalog.RealmName(entry.Realm)} {entry.CharacterClass} ({entry.Role})";
                lines.Add(Link(label, () =>
                {
                    if (PlayerCompanionRoster.TryRecruit(_owner, entry.Realm, entry.CharacterClass,
                            out PlayerCompanionRecord record, out string message))
                        _owner.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    else
                        _owner.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    ShowDetails(record?.CompanionId);
                }));
            }
            if (page > 0)
                lines.Add(Link("Previous page", () => ShowRecruit(page - 1)));
            if (page + 1 < pageCount)
                lines.Add(Link("Next page", () => ShowRecruit(page + 1)));
            lines.Add(Link("Browse authored cast", () => ShowAuthored(0)));
            lines.Add(Link("Roster", () => ShowHome()));
            Render(string.Join('\n', lines));
        }

        private void ShowAuthored(int page)
        {
            StartPage();
            if (!PlayerCompanionRoster.TryGetRoster(_owner, out List<PlayerCompanionRecord> roster))
            {
                ShowHome();
                return;
            }
            CompanionCharacterCatalog.Character[] cast = CompanionCharacterCatalog.All.ToArray();
            int pageCount = Math.Max(1, (int)Math.Ceiling((double)cast.Length / PageSize));
            page = Math.Clamp(page, 0, pageCount - 1);
            var lines = new List<string> { $"Authored companions, page {page + 1}/{pageCount}:" };
            foreach (CompanionCharacterCatalog.Character entry in cast.Skip(page * PageSize).Take(PageSize))
            {
                bool owned = roster.Any(record => record.AuthoredRecruitKey == entry.Key);
                lines.Add(Link($"{entry.Name}, {entry.Realm} {entry.Class} ({(owned ? "recruited" : "available")})",
                    () => ShowAuthoredDetail(entry.Key, page)));
            }
            if (page > 0) lines.Add(Link("Previous page", () => ShowAuthored(page - 1)));
            if (page + 1 < pageCount) lines.Add(Link("Next page", () => ShowAuthored(page + 1)));
            lines.Add(Link("Recruit classes", () => ShowRecruit(0)));
            lines.Add(Link("Roster", () => ShowHome()));
            Render(string.Join('\n', lines));
        }

        private void ShowAuthoredDetail(string key, int returnPage)
        {
            StartPage();
            CompanionCharacterCatalog.Character entry = CompanionCharacterCatalog.Find(key);
            if (entry == null || !PlayerCompanionRoster.TryGetRoster(_owner, out List<PlayerCompanionRecord> roster))
            {
                ShowAuthored(returnPage);
                return;
            }
            PlayerCompanionRecord owned = roster.FirstOrDefault(record => record.AuthoredRecruitKey == key);
            var lines = new List<string>
            {
                $"{entry.Name}, {entry.Realm} {entry.Class}; {entry.Race}, {entry.Gender}; {entry.Personality}.",
                entry.Background,
                $"{entry.Name}: \"{entry.Greeting}\"",
                $"{entry.Name}: \"{entry.FieldNote}\"",
                owned != null
                    ? Link("Open roster entry", () => ShowDetails(owned.CompanionId))
                    : Link("Recruit this companion", () =>
                    {
                        PlayerCompanionRoster.TryRecruitAuthored(_owner, entry.Name,
                            out PlayerCompanionRecord recruited, out string message);
                        _owner.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
                        if (recruited != null && recruited.IsPersisted) ShowDetails(recruited.CompanionId);
                        else ShowAuthoredDetail(key, returnPage);
                    }),
                Link("Back to cast", () => ShowAuthored(returnPage)),
            };
            Render(string.Join('\n', lines));
        }

        private void ShowDetails(string companionId)
        {
            StartPage();
            if (string.IsNullOrWhiteSpace(companionId) ||
                !PlayerCompanionRoster.TryGetRoster(_owner, out List<PlayerCompanionRecord> roster))
            {
                ShowHome();
                return;
            }
            PlayerCompanionRecord record = roster.FirstOrDefault(item => item.CompanionId == companionId);
            if (record == null)
            {
                ShowHome();
                return;
            }

            var lines = new List<string>
            {
                $"{record.Name} — level {record.Level} {(eCharacterClass)record.ClassId}",
                $"Training: {record.TrainingMode}; {record.UnspentSpecPoints} unspent points.",
                Link(record.IsActive ? "Bench" : "Invite", () => RunRosterAction(companionId, record.IsActive)),
                Link("Manual training mode", () => SetTrainingMode(companionId, automatic: false)),
                Link("Automatic training plan", () => SetTrainingMode(companionId, automatic: true)),
                Link("Character and tactics", () => ShowCharacter(companionId)),
            };

            if (PlayerCompanionRoster.TryGetActiveCompanionById(_owner, companionId, out GameBot companion))
            {
                lines.Add(Link("Training and build", () => ShowTraining(companionId)));
                lines.Add(Link("Equipment and backpack", () => ShowInventory(companionId, 0)));
                lines.Add(Link("Respecialize", () => BeginRespec(companion)));
            }
            else
                lines.Add("Invite the companion to manage training or equipment.");
            lines.Add(Link("Roster", () => ShowHome()));
            Render(string.Join('\n', lines));
        }

        private void ShowCharacter(string companionId)
        {
            StartPage();
            if (!PlayerCompanionRoster.TryGetRoster(_owner, out List<PlayerCompanionRecord> roster) ||
                roster.FirstOrDefault(record => record.CompanionId == companionId) is not PlayerCompanionRecord record)
            {
                ShowHome();
                return;
            }
            var lines = new List<string> { CompanionPersonality.Profile(record) };
            eCharacterClass characterClass = (eCharacterClass)record.ClassId;
            foreach (BotPveGroupRole role in Enum.GetValues<BotPveGroupRole>())
                if (BotPartyRoles.CanFill(characterClass, role))
                    lines.Add(Link($"Role: {role}", () => SetTactics(companionId, "role", role.ToString())));
            lines.Add(Link("Stance: aggressive", () => SetTactics(companionId, "stance", "aggressive")));
            lines.Add(Link("Stance: defensive", () => SetTactics(companionId, "stance", "defensive")));
            lines.Add(Link("Back", () => ShowDetails(companionId)));
            Render(string.Join('\n', lines));
        }

        private void SetTactics(string companionId, string kind, string value)
        {
            PlayerCompanionRoster.TrySetTactics(_owner, companionId, kind, value, out string message);
            _owner.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
            ShowCharacter(companionId);
        }

        private void ShowTraining(string companionId)
        {
            StartPage();
            if (!PlayerCompanionRoster.TryGetActiveCompanionById(_owner, companionId, out GameBot companion))
            {
                ShowDetails(companionId);
                return;
            }
            var lines = new List<string>
            {
                $"{companion.Name}: {companion.UnspentSpecPoints} unspent points.",
                CompanionBuildPlanCatalog.GetBlocker((eCharacterClass)companion.CharacterClass.ID,
                    companion.PlayerCompanionRecord.TrainingPlanId),
            };
            foreach (Specialization spec in companion.GetSpecList().Where(item => item.Trainable)
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                int target = Math.Min(companion.Level, spec.Level + 1);
                lines.Add(Link($"{spec.Name} {spec.Level} → {target}", () => TrainNextRank(companionId, spec.KeyName, target)));
            }
            lines.Add(Link("Back", () => ShowDetails(companionId)));
            Render(string.Join('\n', lines));
        }

        private void ShowInventory(string companionId, int page)
        {
            StartPage();
            if (!PlayerCompanionRoster.TryGetActiveCompanionById(_owner, companionId, out GameBot companion) ||
                companion.Inventory is not BotInventory inventory)
            {
                ShowDetails(companionId);
                return;
            }

            var equipped = inventory.EquippedItems.OrderBy(item => item.SlotPosition).ToArray();
            var backpack = inventory.AllItems.Where(item => item.SlotPosition is >= (int)eInventorySlot.FirstBackpack and <= (int)eInventorySlot.LastBackpack)
                .OrderBy(item => item.SlotPosition).ToArray();
            var lines = new List<string> { $"{companion.Name}: equipment and backpack ({backpack.Length}/40), page {page + 1}." };
            foreach (DbInventoryItem item in equipped)
            {
                eInventorySlot slot = (eInventorySlot)item.SlotPosition;
                bool locked = PlayerCompanionRoster.IsEquipmentSlotLocked(companion.PlayerCompanionRecord, slot);
                string flags = PlayerCompanionRoster.GetEquipmentItemFlags(companion.PlayerCompanionRecord, item.ObjectId);
                lines.Add(Link($"{slot}: {item.Name} (level {item.Level}; {(locked ? "locked" : "unlocked")}; {DescribeFlags(flags)})",
                    () => ShowItem(companionId, item.ObjectId)));
                lines.Add(Link(locked ? $"Unlock {slot}" : $"Lock {slot}", () => ToggleSlotLock(companionId, slot, !locked)));
                lines.Add(Link($"Unequip {slot}", () => Unequip(companionId, slot)));
            }
            foreach (DbInventoryItem item in backpack.Skip(page * PageSize).Take(PageSize))
            {
                string flags = PlayerCompanionRoster.GetEquipmentItemFlags(companion.PlayerCompanionRecord, item.ObjectId);
                lines.Add(Link($"{item.Name} (level {item.Level}; {DescribeFlags(flags)})", () => ShowItem(companionId, item.ObjectId)));
            }
            int pageCount = Math.Max(1, (int)Math.Ceiling((double)backpack.Length / PageSize));
            if (page > 0)
                lines.Add(Link("Previous page", () => ShowInventory(companionId, page - 1)));
            if (page + 1 < pageCount)
                lines.Add(Link("Next page", () => ShowInventory(companionId, page + 1)));
            lines.Add(Link("Owner backpack", () => ShowOwnerBackpack(companionId, 0)));
            lines.Add(Link("Back", () => ShowDetails(companionId)));
            Render(string.Join('\n', lines));
        }

        private void ShowOwnerBackpack(string companionId, int page)
        {
            StartPage();
            if (!PlayerCompanionRoster.TryGetActiveCompanionById(_owner, companionId, out GameBot companion) ||
                _owner.Inventory == null)
            {
                ShowDetails(companionId);
                return;
            }
            DbInventoryItem[] items = _owner.Inventory.AllItems
                .Where(item => item.SlotPosition is >= (int)eInventorySlot.FirstBackpack and <= (int)eInventorySlot.LastBackpack)
                .OrderBy(item => item.SlotPosition).ToArray();
            int pageCount = Math.Max(1, (int)Math.Ceiling((double)items.Length / PageSize));
            page = Math.Clamp(page, 0, pageCount - 1);
            var lines = new List<string> { $"Owner backpack ({items.Length}/40), page {page + 1}/{pageCount}:" };
            foreach (DbInventoryItem item in items.Skip(page * PageSize).Take(PageSize))
                lines.Add(Link($"Inspect {item.Name} (level {item.Level})",
                    () => ShowOwnerItem(companionId, item.ObjectId)));
            if (page > 0)
                lines.Add(Link("Previous page", () => ShowOwnerBackpack(companionId, page - 1)));
            if (page + 1 < pageCount)
                lines.Add(Link("Next page", () => ShowOwnerBackpack(companionId, page + 1)));
            lines.Add(Link("Companion inventory", () => ShowInventory(companionId, 0)));
            Render(string.Join('\n', lines));
        }

        private void ShowOwnerItem(string companionId, string itemId)
        {
            StartPage();
            if (!PlayerCompanionRoster.TryGetActiveCompanionById(_owner, companionId, out GameBot companion) ||
                _owner.Inventory?.AllItems.FirstOrDefault(item => item.ObjectId == itemId) is not DbInventoryItem item ||
                item.SlotPosition is < (int)eInventorySlot.FirstBackpack or > (int)eInventorySlot.LastBackpack)
            {
                ShowOwnerBackpack(companionId, 0);
                return;
            }

            bool transferable = PlayerCompanionRoster.CanTransferItem(item, out string blocker);
            var lines = new List<string>
            {
                $"{item.Name}; owner backpack; level {item.Level}; quality {item.Quality}; requirement {item.LevelRequirement}.",
                DescribeStats(item),
                transferable ? "Transfer: eligible; becomes player-supplied and is protected from automatic sale." : $"Transfer: blocked; {blocker}.",
            };
            if (transferable)
                lines.Add(Link($"Give to {companion.Name}", () => TransferItem(companionId, item.ObjectId, toCompanion: true)));
            lines.Add(Link("Back", () => ShowOwnerBackpack(companionId, 0)));
            Render(string.Join('\n', lines));
        }

        private void ShowItem(string companionId, string itemId)
        {
            StartPage();
            if (!PlayerCompanionRoster.TryGetActiveCompanionById(_owner, companionId, out GameBot companion) ||
                companion.Inventory?.AllItems.FirstOrDefault(item => item.ObjectId == itemId) is not DbInventoryItem item)
            {
                ShowInventory(companionId, 0);
                return;
            }

            string flags = PlayerCompanionRoster.GetEquipmentItemFlags(companion.PlayerCompanionRecord, item.ObjectId);
            bool inBackpack = item.SlotPosition is >= (int)eInventorySlot.FirstBackpack and <= (int)eInventorySlot.LastBackpack;
            eInventorySlot equippedSlot = (eInventorySlot)item.SlotPosition;
            bool saleEligible = inBackpack && PlayerCompanionGearRewards.CanSellForSpace(companion, item);
            string transferBlocker = inBackpack ? string.Empty : "equipped items cannot be transferred";
            bool returnEligible = inBackpack && PlayerCompanionRoster.CanReturnItemToOwner(item,
                companion.PlayerCompanionRecord, out transferBlocker);
            string saleStatus = saleEligible
                ? "eligible for automatic sale only when space is needed"
                : "not eligible for automatic sale (protected or no positive appraisal value)";
            var lines = new List<string>
            {
                $"{item.Name}; {(inBackpack ? "backpack" : equippedSlot.ToString())}; level {item.Level}; quality {item.Quality}; requirement {item.LevelRequirement}.",
                DescribeStats(item),
                $"Equipment score: {AutonomousBotEconomy.EquipmentValue(item)}. Ownership: {DescribeFlags(flags)}; {saleStatus}.",
                returnEligible ? "Transfer: eligible to return to your backpack." : $"Transfer: protected; {transferBlocker}.",
                inBackpack ? Link("Equip and lock its slot", () => EquipItem(companionId, itemId)) : null,
                Link(flags.Contains('K') ? "Remove keep flag" : "Keep item", () => ToggleKeep(companionId, itemId, !flags.Contains('K'))),
                returnEligible ? Link("Return to owner backpack", () => TransferItem(companionId, itemId, toCompanion: false)) : null,
                Link("Back", () => ShowInventory(companionId, 0)),
            };
            Render(string.Join('\n', lines.Where(line => !string.IsNullOrWhiteSpace(line))));
        }

        private void RunRosterAction(string companionId, bool currentlyActive)
        {
            string message;
            if (currentlyActive)
                PlayerCompanionRoster.TryBench(_owner, companionId, out message);
            else
                PlayerCompanionRoster.TryInvite(_owner, companionId, out message);
            _owner.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
            ShowDetails(companionId);
        }

        private void SetTrainingMode(string companionId, bool automatic)
        {
            string message;
            if (automatic)
                PlayerCompanionRoster.TrySetAutomaticTrainingMode(_owner, companionId, out message);
            else
                PlayerCompanionRoster.TrySetManualTrainingMode(_owner, companionId, out message);
            _owner.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
            ShowDetails(companionId);
        }

        private void TrainNextRank(string companionId, string line, int level)
        {
            if (!PlayerCompanionRoster.TryGetActiveCompanionById(_owner, companionId, out GameBot companion))
            {
                ShowDetails(companionId);
                return;
            }
            if (!CanTrain(companion))
            {
                _owner.Out.SendMessage("Select a trainer who can train your companion's class.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                ShowTraining(companionId);
                return;
            }
            Specialization spec = companion.GetSpecList().FirstOrDefault(item => item.Trainable && item.KeyName == line);
            if (spec == null)
            {
                _owner.Out.SendMessage("That career line is no longer available.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                ShowTraining(companionId);
                return;
            }
            if (!companion.TryTrainCompanionSpecialization(spec, level, out int spent, out string error))
            {
                _owner.Out.SendMessage(error, eChatType.CT_System, eChatLoc.CL_SystemWindow);
                ShowTraining(companionId);
                return;
            }
            bool saved = PlayerCompanionRoster.SaveProgress(companion);
            if (!saved)
                PlayerCompanionProgressPersistence.Queue(companion);
            _owner.Out.SendMessage(saved
                    ? $"{companion.Name} trained {spec.Name} to {spec.Level}; spent {spent} points."
                    : $"Training was applied but could not be saved; a retry was queued.",
                eChatType.CT_System, eChatLoc.CL_SystemWindow);
            ShowTraining(companionId);
        }

        private bool CanTrain(GameBot companion) =>
            ServerProperties.Properties.ALLOW_TRAIN_ANYWHERE ||
            (ePrivLevel)_owner.Client.Account.PrivLevel is not ePrivLevel.Player ||
            (_owner.TargetObject as GameTrainer ?? _previousTarget as GameTrainer) is GameTrainer trainer &&
            (trainer.TrainedClass == eCharacterClass.Unknown || trainer.TrainedClass == (eCharacterClass)companion.CharacterClass.ID);

        private void BeginRespec(GameBot companion)
        {
            if (!CanTrain(companion))
            {
                _owner.Out.SendMessage("Select a trainer who can train your companion's class.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                ShowDetails(companion.PlayerCompanionRecord.CompanionId);
                return;
            }
            _owner.TempProperties.SetProperty(PlayerCompanionCommandHandler.CompanionRespecProperty,
                companion.PlayerCompanionRecord.CompanionId);
            _owner.Client.Out.SendCustomDialog(
                $"Reset all specializations for {companion.Name}? This uses your full-skill respec eligibility and keeps their level, XP, inventory, and equipment.",
                new CustomDialogResponse(PlayerCompanionCommandHandler.CompanionRespecDialogResponse));
        }

        private void EquipItem(string companionId, string itemId)
        {
            if (PlayerCompanionRoster.TryGetActiveCompanionById(_owner, companionId, out GameBot companion) &&
                companion.Inventory?.AllItems.FirstOrDefault(item => item.ObjectId == itemId) is DbInventoryItem item &&
                companion.TryManuallyEquipPersistentCompanionItem(item))
            {
                _owner.Out.SendMessage($"{item.Name} is equipped and its slot is locked against automatic replacement.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
            }
            else
                _owner.Out.SendMessage("That item is not a legal upgrade, its slot is protected, or the companion is busy.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
            ShowInventory(companionId, 0);
        }

        private void Unequip(string companionId, eInventorySlot slot)
        {
            if (!PlayerCompanionRoster.TryGetActiveCompanionById(_owner, companionId, out GameBot companion) ||
                companion.Inventory == null)
            {
                _owner.Out.SendMessage("Unequipping requires a nearby companion while you are out of combat.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                ShowInventory(companionId, 0);
                return;
            }
            DbInventoryItem item = companion.Inventory.GetItem(slot);
            if (item == null || !PlayerCompanionRoster.TryApplyEquipmentMutation(companion, () =>
                {
                    if (!ReferenceEquals(companion.Inventory.GetItem(slot), item))
                        return false;
                    eInventorySlot backpack = companion.Inventory.FindFirstEmptySlot(
                        eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                    if (backpack == eInventorySlot.Invalid ||
                        !companion.Inventory.MoveItem(slot, backpack, Math.Max(1, item.Count)))
                        return false;
                    PlayerCompanionRoster.SetEquipmentSlotLocked(companion.PlayerCompanionRecord, slot, false);
                    return true;
                }, out _, requiredFreeBackpackSlots: 1))
                _owner.Out.SendMessage("The item could not be moved to the companion's backpack.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            else
            {
                companion.RefreshPersistentCompanionEquipment(
                    slot is eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon or eInventorySlot.TwoHandWeapon);
            }
            ShowInventory(companionId, 0);
        }

        private void ToggleSlotLock(string companionId, eInventorySlot slot, bool locked)
        {
            if (PlayerCompanionRoster.TryGetActiveCompanionById(_owner, companionId, out GameBot companion) &&
                PlayerCompanionRoster.TryApplyEquipmentMutation(companion, () =>
                {
                    PlayerCompanionRoster.SetEquipmentSlotLocked(companion.PlayerCompanionRecord, slot, locked);
                    return true;
                }, out _))
            {
            }
            else
                _owner.Out.SendMessage("Slot locks can only be changed near an idle companion.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
            ShowInventory(companionId, 0);
        }

        private void ToggleKeep(string companionId, string itemId, bool keep)
        {
            if (PlayerCompanionRoster.TryGetActiveCompanionById(_owner, companionId, out GameBot companion) &&
                PlayerCompanionRoster.TryApplyEquipmentMutation(companion, () =>
                {
                    if (companion.Inventory?.AllItems.Any(item => item.ObjectId == itemId) != true)
                        return false;
                    string flags = PlayerCompanionRoster.GetEquipmentItemFlags(companion.PlayerCompanionRecord, itemId);
                    flags = keep ? string.Concat(flags, "K") : flags.Replace("K", string.Empty, StringComparison.Ordinal);
                    PlayerCompanionRoster.SetEquipmentItemFlags(companion.PlayerCompanionRecord, itemId, flags);
                    return true;
                }, out _))
            {
            }
            else
                _owner.Out.SendMessage("Keep flags can only be changed for inventory owned by an idle companion nearby.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
            ShowItem(companionId, itemId);
        }

        private void TransferItem(string companionId, string itemId, bool toCompanion)
        {
            bool transferred = PlayerCompanionRoster.TryTransferItem(_owner, companionId, itemId, toCompanion, out string message);
            _owner.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
            ShowInventory(companionId, 0);
        }

        private static string DescribeFlags(string flags)
        {
            if (flags.Contains('S')) return "starter gear; protected";
            if (flags.Contains('P')) return "player-supplied; protected";
            if (flags.Contains('E')) return flags.Contains('K') ? "companion gear; kept" : "companion gear";
            return "legacy ownership unknown; protected";
        }

        private static string DescribeStats(DbInventoryItem item)
        {
            var stats = new List<string>();
            AddStat("Bonus", item.Bonus);
            AddStat((eProperty)item.Bonus1Type, item.Bonus1);
            AddStat((eProperty)item.Bonus2Type, item.Bonus2);
            AddStat((eProperty)item.Bonus3Type, item.Bonus3);
            AddStat((eProperty)item.Bonus4Type, item.Bonus4);
            AddStat((eProperty)item.Bonus5Type, item.Bonus5);
            AddStat((eProperty)item.Bonus6Type, item.Bonus6);
            AddStat((eProperty)item.Bonus7Type, item.Bonus7);
            AddStat((eProperty)item.Bonus8Type, item.Bonus8);
            AddStat((eProperty)item.Bonus9Type, item.Bonus9);
            AddStat((eProperty)item.Bonus10Type, item.Bonus10);
            AddStat((eProperty)item.ExtraBonusType, item.ExtraBonus);
            if (item.DPS_AF > 0)
                stats.Add($"DPS/AF {item.DPS_AF}");
            if (item.SPD_ABS > 0)
                stats.Add($"speed {item.SPD_ABS}");
            return stats.Count == 0 ? "Stats: none" : "Stats: " + string.Join(", ", stats);

            void AddStat(object type, int value)
            {
                if (value != 0 && !string.Equals(type?.ToString(), "Undefined", StringComparison.Ordinal))
                    stats.Add($"{type} {value}");
            }
        }

        private void StartPage()
        {
            _choices.Clear();
            _generation = Guid.NewGuid().ToString("N")[..6];
            _nextToken = 0;
        }

        private string Link(string label, Action action)
        {
            string token = _generation + ":" + (_nextToken++).ToString("x");
            _choices[token] = action;
            // The generation token is part of the clickable response so an old
            // popup click cannot invoke the new page's action.
            return $"[{label} | {token}]";
        }

        private void Render(string text)
        {
            _owner.Out.SendMessage(text, eChatType.CT_Say, eChatLoc.CL_PopupWindow);
        }

        private void Close(bool restoreTarget)
        {
            if (_closed)
                return;
            _closed = true;
            _expiry?.Stop();
            _expiry = null;
            if (ReferenceEquals(_owner?.TempProperties.GetProperty<PersistentCompanionMenu>(ActiveMenuProperty), this))
                _owner.TempProperties.RemoveProperty(ActiveMenuProperty);
            if (restoreTarget && _owner?.ObjectState == eObjectState.Active && ReferenceEquals(_owner.TargetObject, this))
            {
                GameObject target = _previousTarget?.ObjectState == eObjectState.Active && _previousTarget.CurrentRegion == _owner.CurrentRegion
                    ? _previousTarget : null;
                _owner.TargetObject = target;
                _owner.Out.SendChangeTarget(target);
            }
            Delete();
        }
    }
}
