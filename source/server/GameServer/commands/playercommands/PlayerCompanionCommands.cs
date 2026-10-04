using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{

    [CmdAttribute("&companions", ePrivLevel.Player,
        "Open the Companion Manager, or manage your roster, cast, tactics, training, and equipment by command", "/companions [find <name or class> | help [topic] | status [page or name] | list | cast | recruit <class> [build] | recruit authored <name> [build] | invite <name> | bench <name> | guild leave <name> | reset [name] | profile <name> | role <name> tank|healer|buffer|attacker|cc | stance <name> aggressive|defensive|passive | group default | mode <name> manual|automatic | plan <name> | build <name> [build] | train <name> <line> <level> | respec <name> | squad <1-5> add|remove|lead <name> | squad <1-5> disband | squad list]")]
    public sealed class PlayerCompanionCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        internal const string CompanionRespecProperty = "PLAYER_COMPANION_FULL_RESPEC_ID";

        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null)
                return;

            if (args.Length == 1)
            {
                CompanionManager.Open(player);
                return;
            }

            if (args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                ShowRoster(client, player);
                return;
            }

            string action = args[1].ToLowerInvariant();
            switch (action)
            {
                case "help":
                    ShowUsage(client, args.Length > 2 ? args[2] : null);
                    break;
                case "status":
                    ShowStatus(client, player, args);
                    break;
                case "ui":
                    if (args.Length == 4)
                        CompanionManager.HandleClientControl(player, args[2], args[3]);
                    else
                        ShowUsage(client);
                    break;
                case "find":
                case "search":
                    CompanionManager.SetQuery(player, string.Join(' ', args.Skip(2)));
                    break;
                case "cast":
                    ShowCast(client, player, args);
                    break;
                case "recruit":
                    Recruit(client, player, args);
                    break;
                case "profile":
                    ShowProfile(client, player, args);
                    break;
                case "role":
                case "stance":
                    SetTactics(client, player, args, action);
                    break;
                case "group":
                    if (args.Length == 3 && args[2].Equals("default", StringComparison.OrdinalIgnoreCase))
                    {
                        DisplayMessage(client, CompanionGroupOrders.UseSavedStances(player));
                    }
                    else ShowUsage(client);
                    break;
                case "invite":
                    UpdateCompanion(client, player, args, invite: true);
                    break;
                case "bench":
                    UpdateCompanion(client, player, args, invite: false);
                    break;
                case "guild":
                    if (args.Length >= 4 && args[2].Equals("leave", StringComparison.OrdinalIgnoreCase))
                    {
                        PlayerCompanionRoster.TryLeaveGuild(player, string.Join(' ', args.Skip(3)), out string guildMessage);
                        DisplayMessage(client, guildMessage);
                    }
                    else
                        DisplayMessage(client, "Use /companions guild leave <name> to remove your companion from their guild.");
                    break;
                case "squad":
                    SquadCommand(client, player, args);
                    break;
                case "reset":
                    ResetCompanions(client, player, args);
                    break;
                case "mode":
                    SetTrainingMode(client, player, args);
                    break;
                case "plan":
                    ShowPlanAvailability(client, args);
                    break;
                case "build":
                case "builds":
                    SelectBuild(client, player, args);
                    break;
                case "train":
                    TrainCompanion(client, player, args);
                    break;
                case "respec":
                    if (args.Length < 3)
                        DisplayMessage(client, "Use /companions respec <name>. This resets only that companion's specializations and uses your full-skill respec eligibility.");
                    else if (!TryBeginCompanionRespec(client, player, string.Join(' ', args.Skip(2)), out string respecMessage))
                        DisplayMessage(client, respecMessage);
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
                    string training = !string.Equals(record.TrainingMode, "automatic", StringComparison.OrdinalIgnoreCase)
                        ? "manual"
                        : CompanionBuildPlanCatalog.TryGetPlanById((eCharacterClass)record.ClassId, record.TrainingPlanId,
                            out CompanionBuildPlan build)
                            ? $"automatic, {build.Name} build"
                            : $"automatic ({record.TrainingPlanId})";
                    DisplayMessage(client, $"{record.Name}, level {record.Level} {(eCharacterClass)record.ClassId} ({state}; {training} training; {record.UnspentSpecPoints} unspent spec points; {xpProgress})");
                }
            }
        }

        private void ShowCast(GameClient client, GamePlayer player, string[] args)
        {
            if (!PlayerCompanionRoster.TryGetRoster(player, out var roster))
            {
                DisplayMessage(client, "Your companion roster could not be loaded.");
                return;
            }
            eRealm? realm = null;
            int page = 1;
            if (args.Length > 2)
            {
                if (!int.TryParse(args[2], out _) && Enum.TryParse(args[2], true, out eRealm selectedRealm) &&
                    selectedRealm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia)
                    realm = selectedRealm;
                else if (!int.TryParse(args[2], out page))
                {
                    DisplayMessage(client, "Use /companions cast [Albion|Midgard|Hibernia] [page].");
                    return;
                }
            }
            if (args.Length > 3 && !int.TryParse(args[3], out page))
            {
                DisplayMessage(client, "Use /companions cast [realm] [page].");
                return;
            }
            var cast = CompanionCharacterCatalog.All.Where(entry => realm == null || entry.Realm == realm).ToArray();
            int pageCount = Math.Max(1, (cast.Length + 7) / 8);
            page = Math.Clamp(page, 1, pageCount);
            DisplayMessage(client, $"Authored cast, page {page}/{pageCount}. Use /companions cast [realm] [page] to browse.");
            foreach (var entry in cast.Skip((page - 1) * 8).Take(8))
            {
                string state = roster.Any(item => item.AuthoredRecruitKey == entry.Key) ? "recruited" : "available";
                DisplayMessage(client, $"{entry.Name}, {entry.Realm} {entry.Class} ({entry.Personality}; {state}).");
            }
            DisplayMessage(client, "Recruit with /companions recruit authored <name> [build], or open /companions for biographies.");
        }

        private void ShowProfile(GameClient client, GamePlayer player, string[] args)
        {
            if (args.Length < 3 || !PlayerCompanionRoster.TryMatchOwnedCompanionPrefix(player, args, 2, args.Length,
                    out PlayerCompanionRecord record, out _))
            {
                DisplayMessage(client, "Use /companions profile <name> for a companion in your roster.");
                return;
            }
            foreach (string line in CompanionPersonality.Profile(record).Split('\n'))
                DisplayMessage(client, line);
        }

        private void SetTactics(GameClient client, GamePlayer player, string[] args, string kind)
        {
            if (args.Length < 4 || !PlayerCompanionRoster.TryMatchOwnedCompanionPrefix(player, args, 2, args.Length - 1,
                    out PlayerCompanionRecord record, out _))
            {
                DisplayMessage(client, $"Use /companions {kind} <name> <choice> for a companion in your roster.");
                return;
            }
            PlayerCompanionRoster.TrySetTactics(player, record.CompanionId, kind, args[^1], out string message);
            DisplayMessage(client, message);
        }

        private void Recruit(GameClient client, GamePlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                DisplayMessage(client, "Use /companions recruit <class> [build], or /companions recruit <realm> <class> [build].");
                return;
            }

            if (args.Length >= 4 && args[2].Equals("authored", StringComparison.OrdinalIgnoreCase))
            {
                string authoredName = string.Join(' ', args.Skip(3));
                string authoredBuild = null;
                if (CompanionCharacterCatalog.FindByName(authoredName) == null && args.Length >= 5)
                {
                    // A trailing word that is not part of the name names the build.
                    authoredName = string.Join(' ', args.Skip(3).Take(args.Length - 4));
                    authoredBuild = args[^1];
                }
                PlayerCompanionRoster.TryRecruitAuthored(player, authoredName, authoredBuild, out _, out string authoredMessage);
                DisplayMessage(client, authoredMessage);
                return;
            }

            string requestedClass = string.Join(' ', args.Skip(2));
            string build = null;
            if (!TemporaryGroupClassCatalog.TryResolveForCompanion(player.Realm, requestedClass,
                    out eRealm realm, out eCharacterClass characterClass, out bool ambiguous))
            {
                // A trailing word that is not part of the class names the build.
                string classWithoutBuild = string.Join(' ', args.Skip(2).Take(args.Length - 3));
                if (args.Length < 4 || !TemporaryGroupClassCatalog.TryResolveForCompanion(player.Realm, classWithoutBuild,
                        out realm, out characterClass, out _))
                {
                    DisplayMessage(client, ambiguous
                        ? $"'{requestedClass}' is used by more than one realm. Use /companions recruit <realm> <class> [build]."
                        : $"'{requestedClass}' is not a supported Classic + SI class. Type /classes to see the catalog.");
                    return;
                }
                build = args[^1];
            }

            PlayerCompanionRoster.TryRecruit(player, realm, characterClass, build, out _, out string message);
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

        // /companions squad <1-5> add|remove|lead <name> | /companions squad <1-5> disband | /companions squad list
        private void SquadCommand(GameClient client, GamePlayer player, string[] args)
        {
            if (args.Length >= 3 && args[2].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                DisplayMessage(client, PlayerCompanionRoster.FormatSquadList(player));
                return;
            }

            if (args.Length < 4 || !int.TryParse(args[2], out int squadIndex) ||
                !CompanionSquadFormation.IsValidSquadIndex(squadIndex))
            {
                DisplayMessage(client, $"Use /companions squad <1-{CompanionSquadFormation.MaxSquadCount}> add|remove|lead <name>, /companions squad <1-{CompanionSquadFormation.MaxSquadCount}> disband, or /companions squad list.");
                return;
            }

            string squadAction = args[3].ToLowerInvariant();
            switch (squadAction)
            {
                case "disband":
                    PlayerCompanionRoster.TrySquadDisband(player, squadIndex, out string disbandMessage);
                    DisplayMessage(client, disbandMessage);
                    break;
                case "add":
                case "remove":
                case "lead":
                    if (args.Length < 5)
                    {
                        DisplayMessage(client, $"Use /companions squad {squadIndex} {squadAction} <name>.");
                        break;
                    }

                    string companionName = string.Join(' ', args.Skip(4));
                    string squadMessage;
                    switch (squadAction)
                    {
                        case "add":
                            PlayerCompanionRoster.TrySquadAdd(player, squadIndex, companionName, out squadMessage);
                            break;
                        case "remove":
                            PlayerCompanionRoster.TrySquadRemove(player, squadIndex, companionName, out squadMessage);
                            break;
                        default:
                            PlayerCompanionRoster.TrySquadLead(player, squadIndex, companionName, out squadMessage);
                            break;
                    }
                    DisplayMessage(client, squadMessage);
                    break;
                default:
                    DisplayMessage(client, $"Use /companions squad <1-{CompanionSquadFormation.MaxSquadCount}> add|remove|lead <name>, /companions squad <1-{CompanionSquadFormation.MaxSquadCount}> disband, or /companions squad list.");
                    break;
            }
        }

        private void ResetCompanions(GameClient client, GamePlayer player, string[] args)
        {
            if (args.Length > 2)
            {
                PlayerCompanionRoster.TryReset(player, string.Join(' ', args.Skip(2)), out string message);
                DisplayMessage(client, message);
                return;
            }

            PlayerCompanionRoster.TryResetActive(player, out var messages);
            foreach (string message in messages)
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
                PlayerCompanionRoster.TrySetAutomaticTrainingMode(player, name, out string automaticMessage);
                DisplayMessage(client, automaticMessage);
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

            if (!PlayerCompanionRoster.TryMatchOwnedCompanionPrefix(client.Player, args, 2, args.Length,
                    out PlayerCompanionRecord record, out _))
            {
                DisplayMessage(client, "That companion name or ID is not in your roster.");
                return;
            }

            string status = string.Equals(record.TrainingMode, "automatic", StringComparison.OrdinalIgnoreCase)
                ? $"automatic plan {record.TrainingPlanId}: {CompanionBuildPlanCatalog.GetBlocker((eCharacterClass)record.ClassId, record.TrainingPlanId)}"
                : $"manual training; {CompanionBuildPlanCatalog.GetBlocker((eCharacterClass)record.ClassId)}";
            DisplayMessage(client, $"{record.Name}: {status}.");
            ShowBuilds(client, record);
        }

        private void SelectBuild(GameClient client, GamePlayer player, string[] args)
        {
            if (args.Length < 3 || !PlayerCompanionRoster.TryMatchOwnedCompanionPrefix(player, args, 2, args.Length,
                    out PlayerCompanionRecord record, out int nameTokens))
            {
                DisplayMessage(client, "Use /companions build <name> to list builds, or /companions build <name> <build> to switch.");
                return;
            }

            if (2 + nameTokens >= args.Length)
            {
                ShowBuilds(client, record);
                return;
            }

            PlayerCompanionRoster.TrySelectBuild(player, record.CompanionId,
                string.Join(' ', args.Skip(2 + nameTokens)), out string message);
            DisplayMessage(client, message);
        }

        private void ShowBuilds(GameClient client, PlayerCompanionRecord record)
        {
            eCharacterClass characterClass = (eCharacterClass)record.ClassId;
            var plans = CompanionBuildPlanCatalog.GetPlans(characterClass);
            if (plans.Count == 0)
                return;

            bool automatic = string.Equals(record.TrainingMode, "automatic", StringComparison.OrdinalIgnoreCase);
            DisplayMessage(client, $"{characterClass} builds (* = current):");
            foreach (CompanionBuildPlan plan in plans)
            {
                string marker = automatic && string.Equals(record.TrainingPlanId, plan.Id, StringComparison.Ordinal) ? "*" : "-";
                DisplayMessage(client, $"{marker} {plan.Key}: {plan.Name}, {CompanionManager.BuildRoleText(plan)} Level 50: {plan.FormatTargets()}.");
            }
            DisplayMessage(client, $"Switch with /companions build {record.Name} <build>. Switching is free, needs no trainer, resets {record.Name}'s specializations, and retrains the new build to their level.");
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

        /// <summary>Checks eligibility and opens the existing respec confirmation dialog.</summary>
        internal static bool TryBeginCompanionRespec(GameClient client, GamePlayer player, string nameOrId, out string message)
        {
            if (!PlayerCompanionRoster.TryGetActiveCompanion(player, nameOrId, out GameBot companion))
            {
                message = "Invite that companion first, then respecialize them while they are active in your group.";
                return false;
            }

            if (!CanUseCompanionTrainer(client, companion))
            {
                message = "Select a trainer who can train your companion's class.";
                return false;
            }

            if (!companion.GetSpecList().Any(spec => spec.Trainable && spec.Level > 1))
            {
                message = $"{companion.Name} has no trained specialization levels to reset.";
                return false;
            }

            if (!HasCompanionFullRespecEligibility(player))
            {
                message = "You need a full-skill respec available to reset a companion's specializations.";
                return false;
            }

            player.TempProperties.SetProperty(CompanionRespecProperty, companion.PlayerCompanionRecord.CompanionId);
            client.Out.SendCustomDialog(
                $"Reset all specializations for {companion.Name}? This uses your full-skill respec eligibility and keeps their level, XP, inventory, and equipment.",
                new CustomDialogResponse(CompanionRespecDialogResponse));
            message = $"Confirm the respec for {companion.Name} in the dialog.";
            return true;
        }

        internal static void CompanionRespecDialogResponse(GamePlayer player, byte response)
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

        internal static bool CanUseCompanionTrainer(GameClient client, GameBot companion)
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

        internal static void ShowClientGuidance(GameClient client)
        {
            if (client?.IsPlaying != true)
                return;
            client.Out.SendMessage("No Companion Manager window? It needs the Companion Manager client extension. Every roster action also works by command; type /companions help.",
                eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        private void ShowStatus(GameClient client, GamePlayer player, string[] args)
        {
            if (!PlayerCompanionRoster.TryGetRoster(player, out var roster))
            {
                DisplayMessage(client, "Your companion roster could not be loaded. Try again later.");
                return;
            }

            const int pageSize = 4;
            int page = 1;
            bool named = args.Length > 2 && !int.TryParse(args[2], out page);
            List<PlayerCompanionRecord> selected;
            if (named)
            {
                if (!PlayerCompanionRoster.TryMatchOwnedCompanionPrefix(player, args, 2, args.Length,
                        out PlayerCompanionRecord record, out int nameTokens) || nameTokens != args.Length - 2)
                {
                    DisplayMessage(client, "Use /companions status [page or name]. Type /companions list to see names.");
                    return;
                }
                selected = new List<PlayerCompanionRecord> { record };
            }
            else
            {
                if (args.Length > 3)
                {
                    DisplayMessage(client, "Use /companions status [page or name].");
                    return;
                }
                selected = roster.Where(record => record.IsActive)
                    .OrderBy(record => record.SquadIndex).ThenBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            int pages = Math.Max(1, (selected.Count + pageSize - 1) / pageSize);
            page = named ? 1 : Math.Clamp(page, 1, pages);
            var lines = new List<string>
            {
                $"Roster: {roster.Count} saved; {roster.Count(record => record.IsActive)} marked active.",
                CompanionEngagementMode.TryGetGroupOrder(player, out eCompanionEngagementMode order)
                    ? $"Group order: {order.ToString().ToLowerInvariant()} (overrides saved stances)."
                    : "Group order: saved individual stances."
            };
            if (selected.Count == 0)
            {
                lines.Add(roster.Count == 0
                    ? "Recruit with /companions recruit <class>; /classes lists classes."
                    : "No active companions. /companions list shows names; /companions invite <name> brings one along.");
            }
            foreach (PlayerCompanionRecord record in selected.Skip((page - 1) * pageSize).Take(pageSize))
            {
                string assignment = record.SquadIndex > 0
                    ? $"squad {record.SquadIndex}{(record.IsSquadLeader ? " leader" : string.Empty)}"
                    : "own party";
                string saved = string.IsNullOrWhiteSpace(record.EngagementPreference) ? "aggressive" : record.EngagementPreference;
                string readiness;
                string details = $"saved stance {saved}; {record.UnspentSpecPoints} spec points";
                if (PlayerCompanionRoster.TryGetActiveCompanionById(player, record.CompanionId, out GameBot bot))
                {
                    string effective = CompanionEngagementMode.Effective(bot).ToString().ToLowerInvariant();
                    readiness = !bot.IsAlive ? "dead"
                        : bot.CurrentRegionID != player.CurrentRegionID ? "another region"
                        : player.GetDistanceTo(bot) > CompanionEngagementMode.RecallDistance ? "distant"
                        : bot.Group == null || !bot.Group.IsInTheGroup(bot) ? "outside party"
                        : bot.InCombat ? "in combat" : "nearby";
                    int bagCount = bot.Inventory?.AllItems.Count(item => item.SlotPosition >= (int)eInventorySlot.FirstBackpack &&
                        item.SlotPosition <= (int)eInventorySlot.LastBackpack) ?? 0;
                    details = $"stance {effective}{(effective == saved ? string.Empty : $" (saved {saved})")}; " +
                        $"{bot.UnspentSpecPoints} spec points; bag {bagCount}/40";
                }
                else
                    readiness = record.IsActive ? "active elsewhere or unavailable here" : "benched";

                lines.Add($"{record.Name}, level {record.Level}: {assignment}; {readiness}.");
                lines.Add($"  {details}.");
            }
            if (!named && pages > 1)
                lines.Add($"Page {page}/{pages}. Use /companions status <page> or /companions status <name>.");
            lines.Add("Recovery: /companions help recovery. This is a snapshot; rerun to refresh.");
            // Four entries, two lines each, keep the native detail packet bounded even for unusual saved text.
            client.Out.SendCustomTextWindow($"Companion status {page}/{pages}",
                lines.Select(line => line.Length <= 150 ? line : line[..147] + "...").ToArray());
        }

        private void ShowUsage(GameClient client, string topic = null)
        {
            string[] lines = (topic ?? string.Empty).ToLowerInvariant() switch
            {
                "roster" => new[]
                {
                    "/companions list | status [page or name] | profile <name>",
                    "/companions recruit <class> [build] (free, level 1, anywhere). /classes lists names.",
                    "/companions cast [realm] [page] | recruit authored <name> [build]",
                    "/companions invite <name> | bench <name> | guild leave <name>"
                },
                "tactics" => new[]
                {
                    "/companions role <name> tank|healer|buffer|attacker|cc",
                    "/companions stance <name> aggressive|defensive|passive saves an individual preference.",
                    "/aggressive | /defensive | /passive overrides saved stances across your force.",
                    "/companions group default restores saved stances. /companions status shows effective stances.",
                    "/pull orders an attack on your target; passive and the distance leash still apply."
                },
                "training" => new[]
                {
                    "/companions build <name> lists builds; build <name> <build> switches freely and retrains.",
                    "/companions mode <name> manual|automatic | plan <name>",
                    "/companions train <name> <line> <level> spends available specialization points.",
                    "Manual training needs a compatible trainer unless train-anywhere is enabled.",
                    "/companions respec <name> confirms a reset using your full-skill respec eligibility."
                },
                "squads" => new[]
                {
                    "/companions squad list",
                    "/companions squad <1-5> add <name> spawns or moves a companion into a squad.",
                    "/companions squad <1-5> lead <name> selects an existing member as leader.",
                    "/companions squad <1-5> remove <name> benches them; their next invite uses your own party.",
                    "/companions squad <1-5> disband benches that squad. /bg brings your squads along.",
                    "Your own party is full? Bench one companion or add the recruit to a squad."
                },
                "recovery" => new[]
                {
                    "/companions status [page or name] shows dead, distant and unavailable companions.",
                    "/companions reset <name> saves and recreates that companion beside you.",
                    "/companions reset recreates every active roster companion; it can affect your squads.",
                    "If rejoining fails, the saved companion stays in the roster. Read the reported reason.",
                    "An unavailable actor may belong to another logged-in character on your account.",
                    "Reset is an explicit recovery action; status never moves or changes companions."
                },
                _ => new[]
                {
                    "Bare /companions opens the Companion Manager with its client extension.",
                    "/companions find <name or class> searches the manager; /companions list works by chat.",
                    "/companions status [page or name] opens a live readiness snapshot.",
                    "/companions help roster | tactics | training | squads | recovery",
                    "Start: /classes, then /companions recruit <class>, then /companions invite <name>.",
                    "Saved companions keep progress and gear. /spawn helpers are temporary."
                }
            };
            foreach (string line in lines)
                DisplayMessage(client, line);
        }
    }
}