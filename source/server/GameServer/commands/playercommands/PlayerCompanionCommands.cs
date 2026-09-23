using System;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{

    [CmdAttribute("&companions", ePrivLevel.Player,
        "Manage your persistent companion roster", "/companions [list | recruit <class> | recruit <realm> <class> | invite <name> | bench <name> | mode <name> manual|automatic | plan <name> | train <name> <line> <level> | respec <name>]")]
    public sealed class PlayerCompanionCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        private const string CompanionRespecProperty = "PLAYER_COMPANION_FULL_RESPEC_ID";

        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null)
                return;

            if (args.Length == 1 || args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                ShowRoster(client, player);
                return;
            }

            string action = args[1].ToLowerInvariant();
            switch (action)
            {
                case "recruit":
                    Recruit(client, player, args);
                    break;
                case "invite":
                    UpdateCompanion(client, player, args, invite: true);
                    break;
                case "bench":
                    UpdateCompanion(client, player, args, invite: false);
                    break;
                case "mode":
                    SetTrainingMode(client, player, args);
                    break;
                case "plan":
                    ShowPlanAvailability(client, args);
                    break;
                case "train":
                    TrainCompanion(client, player, args);
                    break;
                case "respec":
                    BeginCompanionRespec(client, player, args);
                    break;
                default:
                    ShowUsage(client);
                    break;
            }
        }

        private void ShowRoster(GameClient client, GamePlayer player)
        {
            if (!PlayerCompanionRoster.TryGetRoster(player, out var records))
            {
                DisplayMessage(client, "Your companion roster could not be loaded. Try again later.");
                return;
            }

            DisplayMessage(client, $"Persistent companions: {records.Count}/{PlayerCompanionRoster.MaximumRosterSize}.");
            if (records.Count == 0)
            {
                DisplayMessage(client, "Your roster is empty. Recruit with /companions recruit <class>.");
                return;
            }

            foreach (var realmGroup in records
                         .OrderBy(record => record.Realm)
                         .ThenBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
                         .GroupBy(record => record.Realm))
            {
                DisplayMessage(client, $"{TemporaryGroupClassCatalog.RealmName((eRealm)realmGroup.Key)}:");
                foreach (PlayerCompanionRecord record in realmGroup)
                {
                    string state = record.IsActive ? "active" : "benched";
                    string xpProgress = record.Level >= 50
                        ? $"XP {record.Experience:N0} (max level)"
                        : $"XP {record.Experience:N0}/{GamePlayer.GetExperienceAmountForLevel(record.Level):N0}";
                    DisplayMessage(client, $"{record.Name}, level {record.Level} {(eCharacterClass)record.ClassId} ({state}; manual training; {record.UnspentSpecPoints} unspent spec points; {xpProgress})");
                }
            }
        }

        private void Recruit(GameClient client, GamePlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                DisplayMessage(client, "Use /companions recruit <class>, or /companions recruit <realm> <class>.");
                return;
            }

            string requestedClass = string.Join(' ', args.Skip(2));
            if (!TemporaryGroupClassCatalog.TryResolveForCompanion(player.Realm, requestedClass,
                    out eRealm realm, out eCharacterClass characterClass, out bool ambiguous))
            {
                DisplayMessage(client, ambiguous
                    ? $"'{requestedClass}' is used by more than one realm. Use /companions recruit <realm> <class>."
                    : $"'{requestedClass}' is not a supported Classic + SI class. Type /classes to see the catalog.");
                return;
            }

            PlayerCompanionRoster.TryRecruit(player, realm, characterClass, out _, out string message);
            DisplayMessage(client, message);
        }

        private void UpdateCompanion(GameClient client, GamePlayer player, string[] args, bool invite)
        {
            if (args.Length < 3)
            {
                DisplayMessage(client, invite
                    ? "Use /companions invite <name>. Type /companions list to see companion names."
                    : "Use /companions bench <name>. Type /companions list to see companion names.");
                return;
            }

            string companionName = string.Join(' ', args.Skip(2));
            string message;
            if (invite)
                PlayerCompanionRoster.TryInvite(player, companionName, out message);
            else
                PlayerCompanionRoster.TryBench(player, companionName, out message);
            DisplayMessage(client, message);
        }

        private void SetTrainingMode(GameClient client, GamePlayer player, string[] args)
        {
            if (args.Length < 4)
            {
                DisplayMessage(client, "Use /companions mode <name> manual|automatic.");
                return;
            }

            string mode = args[^1].ToLowerInvariant();
            string name = string.Join(' ', args.Skip(2).Take(args.Length - 3));
            if (mode is "automatic" or "auto")
            {
                DisplayMessage(client, "Automatic training is unavailable because no companion build plan has passed the required class, point-budget, milestone, and runtime-skill checks. Use manual mode for now.");
                return;
            }

            if (mode != "manual")
            {
                DisplayMessage(client, "Choose manual or automatic training mode.");
                return;
            }

            PlayerCompanionRoster.TrySetManualTrainingMode(player, name, out string message);
            DisplayMessage(client, message);
        }

        private void ShowPlanAvailability(GameClient client, string[] args)
        {
            if (args.Length < 3)
            {
                DisplayMessage(client, "Use /companions plan <name> to check available automatic builds.");
                return;
            }

            DisplayMessage(client, "No automatic companion build plans are validated yet. Per-class research and a disposable runtime database check are still required before plans can be selected.");
        }

        private void TrainCompanion(GameClient client, GamePlayer player, string[] args)
        {
            if (args.Length < 5 || !int.TryParse(args[^1], out int targetLevel) ||
                !PlayerCompanionRoster.TryMatchOwnedCompanionPrefix(player, args, 2, args.Length - 1,
                    out PlayerCompanionRecord record, out int nameTokens))
            {
                DisplayMessage(client, "Use /companions train <name> <specialization line> <level>.");
                return;
            }

            string specializationName = string.Join(' ', args.Skip(2 + nameTokens).Take(args.Length - 3 - nameTokens));
            if (string.IsNullOrWhiteSpace(specializationName) ||
                !PlayerCompanionRoster.TryGetActiveCompanion(player, record.CompanionId, out GameBot companion))
            {
                DisplayMessage(client, "Invite that companion first, then train them while they are active in your group.");
                return;
            }

            if (!CanUseCompanionTrainer(client, companion))
            {
                DisplayMessage(client, "Select a trainer who can train your companion's class.");
                return;
            }

            Specialization specialization = companion.GetSpecList().FirstOrDefault(spec => spec.Trainable &&
                (string.Equals(spec.KeyName, specializationName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(spec.Name, specializationName, StringComparison.OrdinalIgnoreCase)));
            if (specialization == null)
            {
                DisplayMessage(client, $"'{specializationName}' is not a trainable career line for {companion.Name}.");
                return;
            }

            if (!companion.TryTrainCompanionSpecialization(specialization, targetLevel,
                    out int pointsSpent, out string error))
            {
                DisplayMessage(client, error);
                return;
            }

            bool saved = PlayerCompanionRoster.SaveProgress(companion);
            if (!saved)
                PlayerCompanionProgressPersistence.Queue(companion);
            DisplayMessage(client, saved
                ? $"{companion.Name} trained {specialization.Name} to {specialization.Level}; spent {pointsSpent} points, {companion.UnspentSpecPoints} remain."
                : $"{companion.Name} trained {specialization.Name} to {specialization.Level}, but the save failed. Their progress is queued for another save attempt.");
        }

        private void BeginCompanionRespec(GameClient client, GamePlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                DisplayMessage(client, "Use /companions respec <name>. This resets only that companion's specializations and uses your full-skill respec eligibility.");
                return;
            }

            string name = string.Join(' ', args.Skip(2));
            if (!PlayerCompanionRoster.TryGetActiveCompanion(player, name, out GameBot companion))
            {
                DisplayMessage(client, "Invite that companion first, then respecialize them while they are active in your group.");
                return;
            }

            if (!CanUseCompanionTrainer(client, companion))
            {
                DisplayMessage(client, "Select a trainer who can train your companion's class.");
                return;
            }

            if (!companion.GetSpecList().Any(spec => spec.Trainable && spec.Level > 1))
            {
                DisplayMessage(client, $"{companion.Name} has no trained specialization levels to reset.");
                return;
            }

            if (!HasCompanionFullRespecEligibility(player))
            {
                DisplayMessage(client, "You need a full-skill respec available to reset a companion's specializations.");
                return;
            }

            player.TempProperties.SetProperty(CompanionRespecProperty, companion.PlayerCompanionRecord.CompanionId);
            client.Out.SendCustomDialog(
                $"Reset all specializations for {companion.Name}? This uses your full-skill respec eligibility and keeps their level, XP, inventory, and equipment.",
                new CustomDialogResponse(CompanionRespecDialogResponse));
        }

        private static void CompanionRespecDialogResponse(GamePlayer player, byte response)
        {
            string companionId = player?.TempProperties.GetProperty<string>(CompanionRespecProperty);
            player?.TempProperties.RemoveProperty(CompanionRespecProperty);
            if (response != 0x01 || player == null || string.IsNullOrWhiteSpace(companionId))
                return;

            if (!PlayerCompanionRoster.TryGetActiveCompanionById(player, companionId, out GameBot companion))
            {
                player.Out.SendMessage("That companion is no longer active. No respec was used.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (!CanUseCompanionTrainer(player.Client, companion))
            {
                player.Out.SendMessage("Select a trainer who can train your companion's class. No respec was used.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (!HasCompanionFullRespecEligibility(player))
            {
                player.Out.SendMessage("You no longer have full-skill respec eligibility available. No respec was used.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (!companion.ResetCompanionSpecializations())
            {
                player.Out.SendMessage("There are no trained specialization levels to reset.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            bool saved = PlayerCompanionRoster.SaveProgress(companion);
            if (!saved)
                PlayerCompanionProgressPersistence.Queue(companion);
            else if (!ServerProperties.Properties.FREE_RESPEC && RequiresFullRespecToken(player))
                player.RespecAmountAllSkill--;

            if (player.Level == 5)
                player.IsLevelRespecUsed = true;

            player.Out.SendMessage(saved
                    ? $"{companion.Name}'s specializations were reset. They have {companion.UnspentSpecPoints} points available; their level, XP, inventory, and equipment were kept."
                    : $"{companion.Name}'s specializations were reset, but the save failed. Their progress is queued for another save attempt.",
                eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        private static bool HasCompanionFullRespecEligibility(GamePlayer player) =>
            ServerProperties.Properties.FREE_RESPEC || !RequiresFullRespecToken(player) || player.RespecAmountAllSkill > 0;

        private static bool RequiresFullRespecToken(GamePlayer player) =>
            TimeSpan.FromSeconds(player?.PlayedTimeSinceLevel ?? 0).TotalHours > 24;

        private static bool CanUseCompanionTrainer(GameClient client, GameBot companion)
        {
            if (client?.Player == null || companion == null)
                return false;

            if (ServerProperties.Properties.ALLOW_TRAIN_ANYWHERE ||
                (ePrivLevel)client.Account.PrivLevel is not ePrivLevel.Player)
            {
                return true;
            }

            return client.Player.TargetObject is GameTrainer trainer &&
                   (trainer.TrainedClass == eCharacterClass.Unknown ||
                    trainer.TrainedClass == (eCharacterClass)companion.CharacterClass.ID);
        }

        private void ShowUsage(GameClient client)
        {
            DisplayMessage(client, "Commands: /companions list | recruit <class> | recruit <realm> <class> | invite <name> | bench <name> | mode <name> manual|automatic | plan <name> | train <name> <line> <level> | respec <name>.");
            DisplayMessage(client, "Recruitment is free, starts at level 1, and works anywhere. Type /classes for names grouped by realm.");
        }
    }
}