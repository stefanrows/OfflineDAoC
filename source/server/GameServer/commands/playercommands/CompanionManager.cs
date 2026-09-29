using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS.PacketHandler;
using static DOL.GS.CompanionManagerProtocol;

namespace DOL.GS.Commands
{
    /// <summary>
    /// Server side of the native Custom8 Companion Manager. The client sends only a
    /// view token and a control number; every click is resolved against this
    /// session and the current roster or catalog, then applied through the
    /// existing PlayerCompanionRoster and gear methods.
    /// </summary>
    public static class CompanionManager
    {
        public const string StoryCopy = "Story companions (authored): Named characters with their own background and personality. You can recruit each one once.";
        public const string CreateCopy = "Create a companion (generated): Choose a class and a new person is created for your roster.";
        public const string PermanenceCopy = "Both are permanent companions who earn XP and keep their training and gear. /spawn helpers are temporary.";
        private const int GuidanceDelayMilliseconds = 3000;
        private const int DetailWidth = WidthDetail - 6;
        private const string BuildIndent = "    ";
        private const string GroupKey = "group";
        private static readonly ConditionalWeakTable<GamePlayer, CompanionManagerSession> Sessions = new();

        private sealed record Line(string Text, string Key = null, Action Run = null);
        private sealed record Choice(string Text, bool Enabled, string Key, Action Run);

        public static bool TryGetSession(GamePlayer player, out CompanionManagerSession session)
        {
            session = null;
            return player != null && Sessions.TryGetValue(player, out session);
        }

        /// <summary>Opens or re-shows the window with a full label refresh.</summary>
        public static void Open(GamePlayer player, string message = null)
        {
            if (player == null)
                return;
            CompanionManagerSession session = Sessions.GetValue(player, CreateSession);
            session.Region = player.CurrentRegionID;
            session.LastUseUtc = DateTime.UtcNow;
            if (message != null)
                session.Message = message;
            Array.Clear(session.SentLabels);
            Render(player, session, show: true, forceToken: true);
            ScheduleGuidance(player, session);
        }

        public static void SetQuery(GamePlayer player, string text)
        {
            if (player == null)
                return;
            CompanionManagerSession session = Sessions.GetValue(player, CreateSession);
            session.Query = CompanionManagerSession.NormalizeQuery(text);
            session.Roster.Offset = 0;
            session.Recruit.Offset = 0;
            session.Message = session.Query.Length == 0
                ? "Search cleared."
                : $"Showing names and classes matching \"{session.Query}\".";
            Open(player);
        }

        public static void Close(GamePlayer player)
        {
            if (player == null || !Sessions.TryGetValue(player, out _))
                return;
            Sessions.Remove(player);
            Send(player, OpHide);
        }

        public static void HandleClientControl(GamePlayer player, string tokenText, string controlText)
        {
            if (player == null)
                return;
            if (!TryParseClientControl(tokenText, controlText, out ushort token, out int control))
            {
                Tell(player, "That Companion Manager control was not recognized.");
                return;
            }
            if (!Sessions.TryGetValue(player, out CompanionManagerSession session))
            {
                if (control != ControlReady)
                    Open(player, "The Companion Manager was reopened; nothing was changed. Please choose again.");
                return;
            }
            session.ClientConfirmed = true;
            if (control == ControlReady)
                return;

            DateTime now = DateTime.UtcNow;
            bool stale = session.IsStale(token, player.CurrentRegionID, now);
            session.LastUseUtc = now;
            session.Region = player.CurrentRegionID;
            if (stale && CompanionManagerSession.IsIndexedControl(control))
            {
                session.Message = "The window changed before that click, so nothing was done. Please choose again.";
                Render(player, session);
                return;
            }

            session.Message = string.Empty;
            CompanionManagerListState list = session.Current;
            switch (control)
            {
                case ControlTabRoster:
                    session.Tab = CompanionManagerTab.Roster;
                    session.RealmAbilityView = false;
                    session.DeleteConfirmationId = null;
                    session.DetailOffset = 0;
                    break;
                case ControlTabRecruit:
                    session.Tab = CompanionManagerTab.Recruit;
                    session.RealmAbilityView = false;
                    session.DeleteConfirmationId = null;
                    session.DetailOffset = 0;
                    break;
                case ControlTabActive:
                    session.Tab = CompanionManagerTab.Active;
                    session.RealmAbilityView = false;
                    session.DeleteConfirmationId = null;
                    session.DetailOffset = 0;
                    break;
                case ControlDetailOverview or ControlDetailTraining or ControlDetailGear:
                    if (session.ShowsCompanions)
                    {
                        session.DetailTab = control switch
                        {
                            ControlDetailTraining => CompanionManagerDetailTab.Training,
                            ControlDetailGear => CompanionManagerDetailTab.Gear,
                            _ => CompanionManagerDetailTab.Overview,
                        };
                        session.RealmAbilityView = false;
                        session.DeleteConfirmationId = null;
                        session.DetailOffset = 0;
                    }
                    break;
                case >= ControlRealmAll and <= ControlRealmHibernia:
                    list.Realm = control switch
                    {
                        ControlRealmAlbion => eRealm.Albion,
                        ControlRealmMidgard => eRealm.Midgard,
                        ControlRealmHibernia => eRealm.Hibernia,
                        _ => null,
                    };
                    list.Offset = 0;
                    break;
                case >= ControlRoleAny and <= ControlRoleAttacker:
                    list.Role = control == ControlRoleAny ? null : (BotPveGroupRole)(control - ControlRoleTank);
                    list.Offset = 0;
                    break;
                case ControlListUp:
                    list.Offset--;
                    break;
                case ControlListDown:
                    list.Offset++;
                    break;
                case ControlListPageUp:
                    list.Offset -= Rows;
                    break;
                case ControlListPageDown:
                    list.Offset += Rows;
                    break;
                case ControlDetailUp:
                    session.DetailOffset -= DetailLines - 1;
                    break;
                case ControlDetailDown:
                    session.DetailOffset += DetailLines - 1;
                    break;
                case ControlClear:
                    session.Query = string.Empty;
                    session.Roster.Offset = 0;
                    session.Recruit.Offset = 0;
                    session.Message = "Search cleared.";
                    break;
                case ControlRefresh:
                    Array.Clear(session.SentLabels);
                    break;
                case ControlClose:
                    Close(player);
                    return;
                case >= ControlRowBase and < ControlRowBase + Rows:
                    session.TrySelectRow(control - ControlRowBase, out _);
                    break;
                case >= ControlDetailBase and < ControlDetailBase + DetailLines:
                    session.DetailActions[control - ControlDetailBase]?.Invoke();
                    break;
                case >= ControlActionBase and < ControlActionBase + Actions:
                    session.ActionHandlers[control - ControlActionBase]?.Invoke();
                    break;
            }
            if (Sessions.TryGetValue(player, out CompanionManagerSession current) && ReferenceEquals(current, session))
                Render(player, session);
        }

        private static CompanionManagerSession CreateSession(GamePlayer player)
        {
            var session = new CompanionManagerSession
            {
                Message = "Select a companion. [Search] opens the chat line with /companions find.",
            };
            session.Recruit.Realm = player.Realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia
                ? player.Realm : eRealm.Albion;
            return session;
        }

        private static void Render(GamePlayer player, CompanionManagerSession session, bool show = false,
            bool forceToken = false)
        {
            var view = new CompanionManagerView();
            bool loaded = PlayerCompanionRoster.TryGetRoster(player, out List<PlayerCompanionRecord> roster);
            bool rosterTab = session.ShowsCompanions;
            bool activeTab = session.Tab == CompanionManagerTab.Active;
            IReadOnlyList<CompanionManagerEntry> all = !rosterTab ? RecruitEntries(roster)
                : RosterEntries(activeTab ? roster.Where(record => InPlayerGroup(player, record)) : roster);
            IReadOnlyList<CompanionManagerEntry> filtered = session.Filter(all);
            IReadOnlyList<CompanionManagerEntry> companions = filtered;
            // The group row ignores search and filters and always leads the roster list.
            bool groupRow = rosterTab && (roster.Count > 0 || player.Group != null);
            if (groupRow)
                filtered = filtered.Prepend(GroupEntry(player)).ToArray();
            IReadOnlyList<CompanionManagerEntry> visible = session.VisibleRows(filtered);
            CompanionManagerListState list = session.Current;
            if (list.SelectedKey == null || all.All(entry => entry.Key != list.SelectedKey) &&
                !(groupRow && list.SelectedKey == GroupKey))
            {
                list.SelectedKey = companions.FirstOrDefault()?.Key ?? all.FirstOrDefault()?.Key;
                session.DeleteConfirmationId = null;
                session.DetailOffset = 0;
                session.RealmAbilityView = false;
                session.SelectedItemId = null;
                session.SelectedSlot = eInventorySlot.Invalid;
            }
            for (int row = 0; row < visible.Count; row++)
            {
                CompanionManagerEntry entry = visible[row];
                view.Rows[row] = (entry.Key == list.SelectedKey, entry.Realm, entry.Name, entry.Info);
            }

            PlayerCompanionRecord selectedRecord = rosterTab
                ? roster.FirstOrDefault(record => RecordKey(record) == list.SelectedKey)
                : null;
            view.Toggles[ToggleTabRoster] = ($"Roster ({roster.Count}/{PlayerCompanionRoster.MaximumRosterSize})",
                session.Tab == CompanionManagerTab.Roster);
            view.Toggles[ToggleTabRecruit] = ("Recruit", session.Tab == CompanionManagerTab.Recruit);
            bool detailTabs = rosterTab && selectedRecord != null;
            view.Toggles[ToggleDetailOverview] = (detailTabs ? "Overview" : null, session.DetailTab == CompanionManagerDetailTab.Overview);
            view.Toggles[ToggleDetailTraining] = (detailTabs ? "Training & Tactics" : null, session.DetailTab == CompanionManagerDetailTab.Training);
            view.Toggles[ToggleDetailGear] = (detailTabs ? "Gear" : null, session.DetailTab == CompanionManagerDetailTab.Gear);
            view.Toggles[ToggleRealmAll] = ("All realms", list.Realm == null);
            view.Toggles[ToggleRealmAlbion] = ("Albion", list.Realm == eRealm.Albion);
            view.Toggles[ToggleRealmMidgard] = ("Midgard", list.Realm == eRealm.Midgard);
            view.Toggles[ToggleRealmHibernia] = ("Hibernia", list.Realm == eRealm.Hibernia);
            view.Toggles[ToggleRoleAny] = ("Any role", list.Role == null);
            view.Toggles[ToggleRoleTank] = ("Tank", list.Role == BotPveGroupRole.Tank);
            view.Toggles[ToggleRoleHealer] = ("Healer", list.Role == BotPveGroupRole.Healer);
            view.Toggles[ToggleRoleBuffer] = ("Buffer", list.Role == BotPveGroupRole.Buffer);
            view.Toggles[ToggleRoleAttacker] = ("Attacker", list.Role == BotPveGroupRole.Attacker);

            var lines = new List<Line>();
            var choices = new List<Choice>();
            if (!loaded)
            {
                view.Header = "Roster unavailable";
                AddText(lines, "Your companion roster could not be loaded. Try [Refresh], or use /companions list.");
            }
            else if (rosterTab && list.SelectedKey == GroupKey)
                BuildGroupDetail(player, session, roster, companions, view, lines, choices);
            else if (rosterTab)
                BuildRosterDetail(player, session, roster, selectedRecord, view, lines, choices);
            else
                BuildRecruitDetail(player, session, roster, view, lines, choices);

            session.DetailOffset = Math.Clamp(session.DetailOffset, 0, Math.Max(0, lines.Count - DetailLines));
            Array.Clear(session.DetailActions);
            Array.Clear(session.ActionHandlers);
            var keys = new List<string>(session.RowKeys.Select(key => key ?? string.Empty))
            {
                session.Tab.ToString(), session.DetailTab.ToString(), list.SelectedKey ?? string.Empty,
                session.SelectedItemId ?? string.Empty, session.SelectedSlot.ToString(),
            };
            for (int line = 0; line < DetailLines; line++)
            {
                int index = session.DetailOffset + line;
                if (index >= lines.Count)
                {
                    keys.Add(string.Empty);
                    continue;
                }
                view.Details[line] = (lines[index].Text, lines[index].Run != null);
                session.DetailActions[line] = lines[index].Run;
                keys.Add(lines[index].Run == null ? string.Empty : lines[index].Key);
            }
            for (int action = 0; action < Actions; action++)
            {
                if (action >= choices.Count)
                {
                    keys.Add(string.Empty);
                    continue;
                }
                view.ActionLabels[action] = (choices[action].Text, choices[action].Enabled);
                session.ActionHandlers[action] = choices[action].Run;
                keys.Add(choices[action].Key);
            }

            view.DetailIndicator = lines.Count > DetailLines
                ? $"Lines {session.DetailOffset + 1}-{Math.Min(lines.Count, session.DetailOffset + DetailLines)} of {lines.Count}"
                : string.Empty;
            view.DetailCanScrollUp = session.DetailOffset > 0;
            view.DetailCanScrollDown = session.DetailOffset + DetailLines < lines.Count;
            view.ListIndicator = filtered.Count == 0
                ? (activeTab && all.Count == 0 ? "None in group" : rosterTab && all.Count == 0 ? "Roster empty" : "No matches")
                : $"{list.Offset + 1}-{list.Offset + visible.Count} of {filtered.Count}";
            string search = session.Query.Length == 0
                ? "[Search] finds a name or class"
                : $"Search \"{session.Query}\": {filtered.Count} found";
            view.Status = activeTab
                ? $"Active: companions in your group ({all.Count}) | {search}"
                : rosterTab
                ? $"Your companions | {search}"
                : $"Story companions and new companions | {search}";
            if (rosterTab && session.RealmAbilityView && !string.IsNullOrEmpty(session.Message))
                view.Status = session.Message;
            view.Message = session.Message;

            bool changed = session.CommitView(string.Join('|', keys));
            string[] labels = view.ToLabels();
            for (int index = 0; index < labels.Length; index++)
            {
                if (string.Equals(session.SentLabels[index], labels[index], StringComparison.Ordinal))
                    continue;
                Send(player, OpLabel, index, labels[index]);
                session.SentLabels[index] = labels[index];
            }
            if (changed || forceToken)
                Send(player, OpToken, 0, FormatToken(session.Revision));
            if (show)
                Send(player, OpShow);
        }

        private static string RecordKey(PlayerCompanionRecord record) => "c:" + record.CompanionId;

        // "Active" means owned and currently live, not "in the owner's own
        // group": a squad companion (task 42) is just as active.
        private static bool InPlayerGroup(GamePlayer player, PlayerCompanionRecord record) =>
            record.IsActive &&
            PlayerCompanionRoster.TryGetActiveCompanionById(player, record.CompanionId, out _);

        private static string SquadLabel(PlayerCompanionRecord record) =>
            record.SquadIndex <= 0 ? string.Empty
                : record.IsSquadLeader ? $", squad {record.SquadIndex} leader" : $", squad {record.SquadIndex}";

        private static IReadOnlyList<CompanionManagerEntry> RosterEntries(IEnumerable<PlayerCompanionRecord> roster) =>
            roster.Select(record => new CompanionManagerEntry(RecordKey(record), (eRealm)record.Realm,
                (eCharacterClass)record.ClassId, record.Name,
                $"{(record.RecruitType == "authored" || !string.IsNullOrEmpty(record.AuthoredRecruitKey) ? "Story" : "Regular")} L{record.Level} {(eCharacterClass)record.ClassId}, {(record.IsActive ? "active" : "benched")}{SquadLabel(record)}",
                (record.IsActive ? "0:" : "1:") + record.Name)).ToArray();

        private static CompanionManagerEntry GroupEntry(GamePlayer player) =>
            new(GroupKey, player.Realm, eCharacterClass.Unknown, "Group orders",
                "order: " + (CompanionEngagementMode.TryGetGroupOrder(player, out eCompanionEngagementMode order)
                    ? order.ToString().ToLowerInvariant() : "saved stances") +
                (PlayerCompanionGrind.IsActive(player) ? ", grinding" : string.Empty), string.Empty);

        private static IReadOnlyList<CompanionManagerEntry> RecruitEntries(IEnumerable<PlayerCompanionRecord> roster)
        {
            HashSet<string> owned = roster.Select(record => record.AuthoredRecruitKey)
                .Where(key => !string.IsNullOrEmpty(key)).ToHashSet(StringComparer.Ordinal);
            IEnumerable<CompanionManagerEntry> authored = CompanionCharacterCatalog.All.Select(entry =>
                new CompanionManagerEntry("a:" + entry.Key, entry.Realm, entry.Class, entry.Name,
                    $"{entry.Class}, {(owned.Contains(entry.Key) ? "recruited" : "story")}",
                    $"{(int)entry.Realm}:{entry.Class}:0:{entry.Name}"));
            IEnumerable<CompanionManagerEntry> generated = TemporaryGroupClassCatalog.All().Select(entry =>
                new CompanionManagerEntry($"g:{(int)entry.Realm}:{(int)entry.CharacterClass}", entry.Realm,
                    entry.CharacterClass, $"New {entry.CharacterClass}", "create a new person",
                    $"{(int)entry.Realm}:{entry.CharacterClass}:1"));
            return authored.Concat(generated).ToArray();
        }

        private static void BuildRosterDetail(GamePlayer player, CompanionManagerSession session,
            List<PlayerCompanionRecord> roster, PlayerCompanionRecord record, CompanionManagerView view,
            List<Line> lines, List<Choice> choices)
        {
            if (record == null)
            {
                session.RealmAbilityView = false;
                view.Header = roster.Count == 0 ? "Your roster is empty" : "No companion selected";
                AddText(lines, roster.Count == 0
                    ? "Open Recruit to add a story companion or create a new one."
                    : "Select a companion from the list.");
                lines.Add(new Line(string.Empty));
                AddText(lines, StoryCopy);
                AddText(lines, CreateCopy);
                AddText(lines, PermanenceCopy);
                choices.Add(new Choice("[Recruit]", true, "tab:recruit", () => session.Tab = CompanionManagerTab.Recruit));
                return;
            }

            string id = record.CompanionId;
            bool live = PlayerCompanionRoster.TryGetActiveCompanionById(player, id, out GameBot companion);
            PlayerCompanionRecord current = live && companion.PlayerCompanionRecord != null ? companion.PlayerCompanionRecord : record;
            eCharacterClass characterClass = (eCharacterClass)current.ClassId;
            view.Header = current.Name;
            view.HeaderRealm = (eRealm)current.Realm;
            view.Subheader = (current.RecruitType == "authored" || !string.IsNullOrEmpty(current.AuthoredRecruitKey) ? "Story" : "Regular") +
                             $" - Level {current.Level} {characterClass} - {(eRace)current.RaceId} {(eGender)current.GenderId} - " +
                             TemporaryGroupClassCatalog.RealmName((eRealm)current.Realm);
            string role = string.IsNullOrWhiteSpace(current.TacticalRole)
                ? BotPartyRoles.DefaultPreference(characterClass) : current.TacticalRole;
            string roleLabel = BotPartyRoles.GroupRoleLabel(role);
            string stance = string.IsNullOrWhiteSpace(current.EngagementPreference) ? "aggressive" : current.EngagementPreference;
            bool automatic = string.Equals(current.TrainingMode, "automatic", StringComparison.OrdinalIgnoreCase);
            int unspent = live ? companion.UnspentSpecPoints : current.UnspentSpecPoints;
            long realmPoints = live ? companion.CompanionRealmPoints : current.RealmPoints;
            var realmAbilities = CompanionRealmAbilityTraining.ClassAbilities(current.ClassId);
            int realmPointPool = CompanionRealmAbilityTraining.PointPool(live ? companion.Level : current.Level, realmPoints);
            var realmAllocations = CompanionRealmAbilityTraining.ReadAllocations(
                current.SerializedRealmAbilities, realmAbilities, realmPointPool);
            int unspentRealmPoints = CompanionRealmAbilityTraining.UnspentPoints(
                realmPointPool, realmAllocations, realmAbilities);

            if (session.DetailTab == CompanionManagerDetailTab.Overview && session.DeleteConfirmationId == id)
                AddText(lines, $"Permanently delete {current.Name}? Level, training and starter gear will be lost. Earned or unknown items block deletion.");

            switch (session.DetailTab)
            {
                case CompanionManagerDetailTab.Training:
                    if (session.RealmAbilityView)
                    {
                        view.Subheader = $"Realm abilities | RR {CompanionRealmAbilityTraining.RealmLevel(realmPoints)} | {realmPoints:N0} RP | {unspentRealmPoints} points left";
                        lines.Add(new Line("Buy one rank per click. Your points update above."));
                        lines.Add(new Line("[Back to Training & Tactics]", "ra:back", () =>
                        {
                            session.RealmAbilityView = false;
                            session.DetailOffset = 0;
                        }));
                        if (realmAbilities.Count == 0)
                            lines.Add(new Line("No passive realm abilities are available for this class."));
                        foreach (var ability in realmAbilities.OrderBy(ability => ability.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            int rank = realmAllocations.TryGetValue(ability.KeyName, out int purchased) ? purchased : 0;
                            if (rank >= ability.MaxLevel)
                            {
                                lines.Add(new Line($"  {ability.Name}: rank {rank}/{ability.MaxLevel} (maximum)"));
                                continue;
                            }
                            int cost = ability.CostForUpgrade(rank);
                            string abilityKey = ability.KeyName;
                            string label = $"  {ability.Name}: rank {rank} > {rank + 1}/{ability.MaxLevel} ({cost} pts)";
                            lines.Add(cost < 0
                                ? new Line(label + " - unavailable")
                                : cost <= unspentRealmPoints
                                    ? new Line(label + " [Buy]", $"ra:{abilityKey}:{rank + 1}",
                                        () => SpendRealmAbility(player, session, id, abilityKey))
                                    : new Line(label + $" - need {cost - unspentRealmPoints} more"));
                        }
                        choices.Add(new Choice("[Back to training]", true, "ra:back:action", () =>
                        {
                            session.RealmAbilityView = false;
                            session.DetailOffset = 0;
                        }));
                        break;
                    }
                    CompanionBuildPlan currentBuild = automatic &&
                        CompanionBuildPlanCatalog.TryGetPlanById(characterClass, current.TrainingPlanId, out CompanionBuildPlan saved)
                        ? saved : null;
                    AddText(lines, $"Training: {(!automatic ? "manual" : currentBuild != null ? $"automatic, {currentBuild.Name} build" : $"automatic, plan {current.TrainingPlanId}")}; {unspent} unspent points.");
                    lines.Add(new Line($"[Realm abilities] RR {CompanionRealmAbilityTraining.RealmLevel(realmPoints)} | {unspentRealmPoints} points left",
                        "ra:open", () =>
                        {
                            session.RealmAbilityView = true;
                            session.DetailOffset = 0;
                        }));
                    IReadOnlyList<CompanionBuildPlan> builds = CompanionBuildPlanCatalog.GetPlans(characterClass);
                    CompanionBuildPlan chosen = null;
                    if (builds.Count > 0)
                    {
                        string entryKey = RecordKey(current);
                        chosen = ChosenBuild(session, entryKey, characterClass, currentBuild);
                        lines.Add(new Line("Builds (select one, then [Use build]):"));
                        AddBuildList(session, lines, entryKey, builds, currentBuild, "current", chosen);
                        if (chosen != null && chosen != currentBuild)
                        {
                            AddText(lines, $"Selected: {chosen.Name}. Level 50: {chosen.FormatTargets()}. " +
                                           $"[Use build] resets {current.Name}'s specializations and retrains them to level {current.Level}; free, no trainer needed.");
                        }
                    }
                    else
                        AddText(lines, "Manual only: " + CompanionBuildPlanCatalog.GetBlocker(characterClass) + ".");
                    lines.Add(automatic
                        ? new Line("Switch to manual training", "mode:manual", () => SetTraining(player, session, id, false))
                        : new Line("Switch to automatic training", "mode:automatic", () => SetTraining(player, session, id, true)));
                    lines.Add(new Line(string.Empty));
                    lines.Add(new Line($"Role: {roleLabel}. Class-legal roles:"));
                    foreach (BotPveGroupRole option in Enum.GetValues<BotPveGroupRole>().Where(option => BotPartyRoles.CanFill(characterClass, option)))
                    {
                        string value = option.ToString().ToLowerInvariant();
                        lines.Add(new Line($"  {BotPartyRoles.GroupRoleLabel(option)}{(value == role ? " (current)" : string.Empty)}", "role:" + value,
                            () => SetTactics(player, session, id, "role", value)));
                    }
                    lines.Add(new Line($"Stance: {stance}. Group orders can override it:"));
                    foreach (string value in new[] { "aggressive", "defensive", "passive" })
                    {
                        lines.Add(new Line($"  {char.ToUpperInvariant(value[0])}{value[1..]}{(value == stance ? " (current)" : string.Empty)}",
                            "stance:" + value, () => SetTactics(player, session, id, "stance", value)));
                    }
                    if (CompanionBombingPolicy.SupportsClass(characterClass))
                    {
                        string bombChoice = CompanionBombingPolicy.Choice(current);
                        string nextBombChoice = CompanionBombingPolicy.NextChoice(bombChoice);
                        lines.Add(new Line($"Bomb use: {CompanionBombingPolicy.ChoiceLabel(bombChoice)} (click to cycle)",
                            "bomb-use:" + bombChoice,
                            () => SetBombUse(player, session, id, nextBombChoice)));
                    }
                    string rangedAoeChoice = CompanionRangedAoePolicy.Choice(current);
                    string nextRangedAoeChoice = CompanionRangedAoePolicy.NextChoice(rangedAoeChoice);
                    lines.Add(new Line($"Ranged AoE: {CompanionRangedAoePolicy.ChoiceLabel(rangedAoeChoice)} (click to cycle)",
                        "ranged-aoe:" + rangedAoeChoice,
                        () => SetRangedAoe(player, session, id, nextRangedAoeChoice)));
                    lines.Add(new Line(string.Empty));
                    if (live)
                    {
                        lines.Add(new Line("Train one rank (target a trainer for this class):"));
                        foreach (Specialization spec in companion.GetSpecList().Where(spec => spec.Trainable)
                                     .OrderBy(spec => spec.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            int target = Math.Min(companion.Level, spec.Level + 1);
                            string key = spec.KeyName;
                            lines.Add(target > spec.Level
                                ? new Line($"  {spec.Name} {spec.Level} -> {target}", $"train:{key}:{target}",
                                    () => TrainRank(player, session, id, key, target))
                                : new Line($"  {spec.Name} {spec.Level} (maximum for level {companion.Level})"));
                        }
                    }
                    else
                        AddText(lines, "Invite this companion to train or respecialize.");
                    if (builds.Count > 0)
                    {
                        bool switchable = chosen != null && chosen != currentBuild;
                        string planId = chosen?.Id;
                        choices.Add(new Choice("[Use build]", switchable, "usebuild:" + (switchable ? planId : string.Empty),
                            () => UseBuild(player, session, id, switchable ? planId : null)));
                    }
                    choices.Add(new Choice("[Respecialize]", live, "respec", () => Respec(player, session, id)));
                    break;

                case CompanionManagerDetailTab.Gear:
                    BuildGear(player, session, current, live ? companion : null, lines, choices);
                    break;

                default:
                    lines.Add(new Line(live ? "Status: active in your group." : current.IsActive
                        ? "Status: active, not currently in the world."
                        : "Status: benched; keeps level, training, and gear."));
                    lines.Add(new Line(current.Level >= 50
                        ? $"XP: {current.Experience:N0} (maximum level)"
                        : $"XP: {current.Experience:N0} / {GamePlayer.GetExperienceAmountForLevel(current.Level):N0}"));
                    AddText(lines, $"Training: {(!automatic ? "manual" : CompanionBuildPlanCatalog.TryGetPlanById(characterClass,
                        current.TrainingPlanId, out CompanionBuildPlan build) ? $"automatic, {build.Name} build" : "automatic")}; {unspent} unspent points.");
                    lines.Add(new Line($"[Realm abilities] RR {CompanionRealmAbilityTraining.RealmLevel(realmPoints)} | {realmPoints:N0} RP | {unspentRealmPoints} points left",
                        "ra:open:overview", () =>
                        {
                            session.DetailTab = CompanionManagerDetailTab.Training;
                            session.RealmAbilityView = true;
                            session.DetailOffset = 0;
                        }));
                    lines.Add(new Line($"Role: {roleLabel}; stance: {stance}."));
                    lines.Add(new Line(string.Empty));
                    CompanionCharacterCatalog.Character authored = CompanionCharacterCatalog.Find(current.AuthoredRecruitKey);
                    if (authored != null)
                    {
                        AddText(lines, $"Story companion; {authored.Personality}. {authored.Background}");
                        AddText(lines, $"\"{authored.Greeting}\"");
                    }
                    else
                    {
                        string personality = string.IsNullOrWhiteSpace(current.PersonalityKey) ? "steady" : current.PersonalityKey;
                        AddText(lines, $"Regular companion with a {personality} temperament and a newly created identity.");
                        AddText(lines, CompanionPersonality.Dialogue(current, "profile"));
                    }
                    break;
            }

            if (session.DetailTab != CompanionManagerDetailTab.Gear)
            {
                choices.Insert(0, live || current.IsActive
                    ? new Choice("[Bench]", true, "bench", () => Report(player, session,
                        Run(PlayerCompanionRoster.TryBench, player, id)))
                    : new Choice("[Invite]", true, "invite", () => Report(player, session,
                        Run(PlayerCompanionRoster.TryInvite, player, id))));
                choices.Add(new Choice("[Open inventory]", live, "bag", () => OpenBag(player, session, id)));
            }
            if (session.DetailTab == CompanionManagerDetailTab.Overview)
            {
                bool confirming = session.DeleteConfirmationId == id;
                choices.Add(new Choice(confirming ? "[Confirm delete]" : "[Delete]", true,
                    confirming ? "confirm-delete:" + id : "delete:" + id,
                    () => DeleteCompanion(player, session, id)));
                if (confirming)
                    choices.Add(new Choice("[Cancel]", true, "cancel-delete:" + id,
                        () => session.DeleteConfirmationId = null));
            }
        }

        /// <summary>Group orders, pull, invite/bench all, and grind; the same code paths as their chat commands.</summary>
        private static void BuildGroupDetail(GamePlayer player, CompanionManagerSession session,
            List<PlayerCompanionRecord> roster, IReadOnlyList<CompanionManagerEntry> shown, CompanionManagerView view,
            List<Line> lines, List<Choice> choices)
        {
            bool ordered = CompanionEngagementMode.TryGetGroupOrder(player, out eCompanionEngagementMode order);
            view.Header = "Group orders";
            view.HeaderRealm = player.Realm;
            view.Subheader = ordered
                ? $"Order: {order}; it overrides every companion's saved stance"
                : "No group order; each companion uses their saved stance";

            lines.Add(new Line("Group order (select one):"));
            foreach ((string text, string key, Func<GamePlayer, string> apply, bool current) in new (string, string, Func<GamePlayer, string>, bool)[]
                     {
                         ("Aggressive: assist your attacks", "order:aggressive", CompanionGroupOrders.Aggressive,
                             ordered && order == eCompanionEngagementMode.Aggressive),
                         ("Defensive: engage threats near you", "order:defensive", CompanionGroupOrders.Defensive,
                             ordered && order == eCompanionEngagementMode.Defensive),
                         ("Passive: return and hold combat", "order:passive", CompanionGroupOrders.Passive,
                             ordered && order == eCompanionEngagementMode.Passive),
                         ("Saved stances: no group order", "order:default", CompanionGroupOrders.UseSavedStances, !ordered),
                     })
            {
                lines.Add(new Line($"  {text}{(current ? " (current)" : string.Empty)}", key,
                    () => Report(player, session, apply(player))));
            }
            lines.Add(new Line(string.Empty));

            // The owner's own group (covers temporary helpers too) plus any of his
            // squad companions (task 42), who are never in that same Group.
            GameBot[] members = (player.Group?.GetMembersInTheGroup().OfType<GameBot>() ?? Enumerable.Empty<GameBot>())
                .Concat(PlayerCompanionRoster.GetActiveCompanions(player).Where(bot => bot.PlayerCompanionRecord?.SquadIndex > 0))
                .Where(bot => (bot.PlayerGroupLeader ?? bot.Owner) == player &&
                              (bot.IsPersistentPlayerCompanion || bot.IsTemporaryGroupHelper) && !bot.IsAutonomousWorldBot)
                .Distinct()
                .ToArray();
            if (members.Length == 0)
                AddText(lines, "No companions are in your group. [Invite all] invites benched companions shown in the list.");
            else
            {
                lines.Add(new Line("Effective stance in your group:"));
                foreach (GameBot bot in members)
                {
                    string effective = CompanionEngagementMode.Effective(bot).ToString().ToLowerInvariant();
                    PlayerCompanionRecord record = bot.PlayerCompanionRecord;
                    if (record == null)
                    {
                        lines.Add(new Line($"  {bot.Name}: {effective} (temporary helper)"));
                        continue;
                    }
                    string saved = string.IsNullOrWhiteSpace(record.EngagementPreference) ? "aggressive" : record.EngagementPreference;
                    string recordKey = RecordKey(record);
                    lines.Add(new Line($"  {bot.Name}: {effective}{(saved != effective ? $" (saved: {saved})" : string.Empty)}",
                        "open:" + recordKey, () => ShowInRoster(session, recordKey)));
                }
            }
            lines.Add(new Line(string.Empty));

            string[] benched = shown
                .Select(entry => roster.FirstOrDefault(record => RecordKey(record) == entry.Key))
                .Where(record => record != null && !PlayerCompanionRoster.TryGetActiveCompanionById(player, record.CompanionId, out _))
                .Select(record => record.CompanionId).ToArray();
            string[] active = roster
                .Where(record => record.IsActive || PlayerCompanionRoster.TryGetActiveCompanionById(player, record.CompanionId, out _))
                .Select(record => record.CompanionId).ToArray();
            bool grinding = PlayerCompanionGrind.IsActive(player);
            AddText(lines, "[Pull] sends your companions after your current target, like /pull.");
            AddText(lines, $"[Invite all] invites the {benched.Length} benched companions shown in the list, top to bottom, " +
                           "until your group is full; use search and filters to choose them. " +
                           $"[Bench all] benches all {active.Length} active companions.");
            AddText(lines, grinding
                ? "Grind mode is active. [Stop grind] ends it, like /grind stop."
                : "[Grind] starts /grind here. It needs a party of only you and temporary /spawn helpers; saved companions cannot grind.");

            choices.Add(new Choice("[Pull]", true, "pull", () => Report(player, session,
                PullGroupCommandHandler.Order(player, "choose [Pull]") ?? "That target cannot be attacked, so nobody was sent.")));
            choices.Add(new Choice("[Invite all]", benched.Length > 0, "inviteall:" + string.Join(',', benched),
                () => InviteAll(player, session, benched)));
            choices.Add(new Choice("[Bench all]", active.Length > 0, "benchall:" + string.Join(',', active),
                () => BenchAll(player, session, active)));
            choices.Add(grinding
                ? new Choice("[Stop grind]", true, "grind:stop", () => StopGrind(player, session))
                : new Choice("[Grind]", true, "grind:start", () => StartGrind(player, session)));
        }

        private static void InviteAll(GamePlayer player, CompanionManagerSession session, IReadOnlyList<string> ids)
        {
            var invited = new List<string>();
            string problem = null;
            for (int index = 0; index < ids.Count; index++)
            {
                if (player.Group != null && player.Group.MemberCount >= player.Group.MaximumMemberCount)
                {
                    problem = $"Your group is full; {ids.Count - index} stay benched.";
                    break;
                }
                if (PlayerCompanionRoster.TryInvite(player, ids[index], out string message) &&
                    PlayerCompanionRoster.TryGetActiveCompanionById(player, ids[index], out GameBot companion))
                    invited.Add(companion.Name);
                else
                    problem ??= message;
            }
            string result = invited.Count == 0 ? "Nobody was invited." : $"Invited {invited.Count}: {string.Join(", ", invited)}.";
            Report(player, session, problem == null ? result : $"{result} {problem}");
        }

        private static void BenchAll(GamePlayer player, CompanionManagerSession session, IReadOnlyList<string> ids)
        {
            int benched = 0;
            string problem = null;
            foreach (string id in ids)
            {
                if (PlayerCompanionRoster.TryBench(player, id, out string message))
                    benched++;
                else
                    problem ??= message;
            }
            Report(player, session, $"Benched {benched} of {ids.Count} companions.{(problem != null ? " " + problem : string.Empty)}");
        }

        private static void StartGrind(GamePlayer player, CompanionManagerSession session)
        {
            PlayerCompanionGrind.TryStart(player, out string message);
            Report(player, session, message);
        }

        private static void StopGrind(GamePlayer player, CompanionManagerSession session)
        {
            if (!PlayerCompanionGrind.IsActive(player))
            {
                Report(player, session, "Grind mode is not active.");
                return;
            }
            // Stop tells the player in chat itself; only the window line is set here.
            PlayerCompanionGrind.Stop(player, "cancelled by you");
            session.Message = "Grind mode stopped.";
        }

        private static void BuildGear(GamePlayer player, CompanionManagerSession session, PlayerCompanionRecord record,
            GameBot companion, List<Line> lines, List<Choice> choices)
        {
            string id = record.CompanionId;
            if (companion?.Inventory == null)
            {
                AddText(lines, "Benched companions keep their gear. It is shown read-only; invite them to change it.");
                DbInventoryItem[] saved = LoadSavedItems(id);
                foreach (eInventorySlot slot in PersistentCompanionGear.SheetSlots)
                    lines.Add(new Line($"  {SlotLabel(slot)}: {saved.FirstOrDefault(item => item.SlotPosition == (int)slot)?.Name ?? "empty"}"));
                lines.Add(new Line($"Backpack: {saved.Count(PersistentCompanionGear.IsBackpack)}/40 items."));
                choices.Add(new Choice("[Invite]", true, "invite", () => Report(player, session,
                    Run(PlayerCompanionRoster.TryInvite, player, id))));
                return;
            }

            DbInventoryItem selected = session.SelectedItemId == null
                ? null
                : companion.Inventory.AllItems.FirstOrDefault(item => item.ObjectId == session.SelectedItemId);
            if (selected == null)
                session.SelectedItemId = null;
            if (!PersistentCompanionGear.SheetSlots.Contains(session.SelectedSlot))
                session.SelectedSlot = eInventorySlot.Invalid;
            choices.Add(new Choice("[Open inventory]", true, "bag", () => OpenBag(player, session, id)));

            if (session.SelectedSlot != eInventorySlot.Invalid)
                BuildGearSlot(player, session, record, companion, session.SelectedSlot, lines, choices);
            else if (selected != null && PersistentCompanionGear.IsBackpack(selected))
            {
                string itemId = selected.ObjectId;
                bool kept = PlayerCompanionRoster.GetEquipmentItemFlags(record, itemId).Contains('K');
                AddText(lines, $"Selected: {selected.Name} (bag {selected.SlotPosition - (int)eInventorySlot.FirstBackpack + 1}).");
                AddItemDetails(lines, record, selected);
                lines.Add(new Line(string.Empty));
                bool returnable = PlayerCompanionRoster.CanReturnItemToOwner(selected, record, out string blocker);
                choices.Add(new Choice("[Equip]", true, "equip:" + itemId, () => Gear(player, session,
                    (out string message) => PersistentCompanionGear.TryEquip(player, id, itemId, out message))));
                choices.Add(InfoChoice(player, session, selected));
                choices.Add(new Choice(returnable ? "[Return to me]" : "[Return: blocked]", returnable, "return:" + itemId,
                    () => Gear(player, session, returnable
                        ? (out string message) => PersistentCompanionGear.TryReturnToOwner(player, id, itemId, out message)
                        : (out string message) => { message = $"Return blocked: {blocker}."; return false; })));
                choices.Add(new Choice(kept ? "[Allow sale]" : "[Keep]", true, $"keep:{itemId}:{!kept}",
                    () => Gear(player, session, (out string message) =>
                        PersistentCompanionGear.TrySetKeep(player, id, itemId, !kept, out message))));
            }

            DbInventoryItem[] backpack = companion.Inventory.AllItems.Where(PersistentCompanionGear.IsBackpack)
                .OrderBy(item => item.SlotPosition).ToArray();
            var resolved = backpack.ToDictionary(item => item, companion.GetManualEquipmentSlot);
            lines.Add(new Line("Worn gear (* = your choice; number = inventory window position; click a slot):"));
            foreach (eInventorySlot slot in PersistentCompanionGear.SheetSlots)
            {
                DbInventoryItem worn = companion.Inventory.GetItem(slot);
                int wornValue = AutonomousBotEconomy.EquipmentValue(worn);
                DbInventoryItem[] fits = backpack.Where(item => PersistentCompanionGear.FitsSlot(resolved[item], slot)).ToArray();
                string hint = worn == null
                    ? fits.Length > 0 ? $" - {fits.Length} fit" : string.Empty
                    : fits.Any(item => AutonomousBotEconomy.EquipmentValue(item) > wornValue) ? " - upgrade in bag" : string.Empty;
                string locked = worn == null ? string.Empty : MarkLabel(PlayerCompanionRoster.GetEquipmentSlotMark(record, slot));
                string marker = slot == session.SelectedSlot ? " <" : string.Empty;
                eInventorySlot target = slot;
                lines.Add(new Line($"  {PersistentCompanionInventoryView.WornPosition(slot),2} {SlotLabel(slot)}: {worn?.Name ?? "empty"}{locked}{hint}{marker}", "slot:" + slot,
                    () => SelectSlot(session, target)));
            }
            lines.Add(new Line($"Backpack {backpack.Length}/40 (window 21-60; select to return or keep):"));
            foreach (DbInventoryItem item in backpack)
            {
                string marker = item.ObjectId == session.SelectedItemId && session.SelectedSlot == eInventorySlot.Invalid
                    ? " <" : string.Empty;
                string itemId = item.ObjectId;
                lines.Add(new Line($"  Bag {item.SlotPosition - (int)eInventorySlot.FirstBackpack + 1}: {item.Name}{marker}",
                    "item:" + itemId, () => SelectItem(session, itemId, keepSlot: false)));
            }
        }

        /// <summary>
        /// One worn slot: the item and its actions. Equipping happens by dragging in the
        /// inventory window, which shows the icons.
        /// </summary>
        private static void BuildGearSlot(GamePlayer player, CompanionManagerSession session, PlayerCompanionRecord record,
            GameBot companion, eInventorySlot slot, List<Line> lines, List<Choice> choices)
        {
            string id = record.CompanionId;
            DbInventoryItem worn = companion.Inventory.GetItem(slot);

            lines.Add(new Line($"{SlotLabel(slot)} < (click to close)", "slot:" + slot, () => SelectSlot(session, slot)));
            if (worn == null)
                AddText(lines, "Worn: nothing. Drag an item onto position " +
                               $"{PersistentCompanionInventoryView.WornPosition(slot)} in [Open inventory].");
            else
            {
                AddText(lines, $"Worn: {worn.Name}{MarkLabel(PlayerCompanionRoster.GetEquipmentSlotMark(record, slot))}.");
                AddItemDetails(lines, record, worn);
            }
            lines.Add(new Line(string.Empty));

            choices.Add(InfoChoice(player, session, worn));
            if (worn == null)
                return;

            string wornId = worn.ObjectId;
            bool slotLocked = PlayerCompanionRoster.IsEquipmentSlotLocked(record, slot);
            bool kept = PlayerCompanionRoster.GetEquipmentItemFlags(record, wornId).Contains('K');
            choices.Add(new Choice("[Unequip]", true, "unequip:" + wornId, () => Gear(player, session,
                (out string message) => PersistentCompanionGear.TryUnequip(player, id, slot, wornId, out message))));
            choices.Add(new Choice(slotLocked ? "[Unlock slot]" : "[Lock slot]", true, $"lock:{wornId}:{!slotLocked}",
                () => Gear(player, session, (out string message) =>
                    PersistentCompanionGear.TrySetSlotLock(player, id, slot, !slotLocked, wornId, out message))));
            choices.Add(new Choice(kept ? "[Allow sale]" : "[Keep]", true, $"keep:{wornId}:{!kept}",
                () => Gear(player, session, (out string message) =>
                    PersistentCompanionGear.TrySetKeep(player, id, wornId, !kept, out message))));
        }

        private static string MarkLabel(eCompanionSlotMark mark) => mark switch
        {
            eCompanionSlotMark.Manual => " *",
            eCompanionSlotMark.Locked => " (locked)",
            _ => string.Empty,
        };

        /// <summary>[Info] opens the native item info window for the item, if any.</summary>
        private static Choice InfoChoice(GamePlayer player, CompanionManagerSession session, DbInventoryItem item) =>
            new("[Info]", item != null, "info:" + (item?.ObjectId ?? string.Empty), () =>
            {
                if (item == null)
                    return;
                PersistentCompanionGear.ShowItemInfo(player, item);
                session.Message = $"Showing {item.Name}.";
            });

        private static void AddItemDetails(List<Line> lines, PlayerCompanionRecord record, DbInventoryItem item)
        {
            AddText(lines, $"Level {item.Level}, quality {item.Quality}, requires {item.LevelRequirement}; score {AutonomousBotEconomy.EquipmentValue(item)}.");
            AddText(lines, PersistentCompanionGear.DescribeStats(item) + ".");
            AddText(lines, $"Ownership: {PersistentCompanionGear.DescribeFlags(PlayerCompanionRoster.GetEquipmentItemFlags(record, item.ObjectId))}.");
        }

        /// <summary>A slot name with a leading capital, as the Gear tab lists it.</summary>
        public static string SlotLabel(eInventorySlot slot)
        {
            string name = PersistentCompanionGear.SlotName(slot);
            return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
        }

        private static void BuildRecruitDetail(GamePlayer player, CompanionManagerSession session,
            List<PlayerCompanionRecord> roster, CompanionManagerView view, List<Line> lines, List<Choice> choices)
        {
            string key = session.Recruit.SelectedKey;
            bool full = roster.Count >= PlayerCompanionRoster.MaximumRosterSize;
            if (key != null && key.StartsWith("a:", StringComparison.Ordinal) &&
                CompanionCharacterCatalog.Find(key[2..]) is CompanionCharacterCatalog.Character story)
            {
                PlayerCompanionRecord owned = roster.FirstOrDefault(record => record.AuthoredRecruitKey == story.Key);
                view.Header = story.Name;
                view.HeaderRealm = story.Realm;
                view.Subheader = $"{TemporaryGroupClassCatalog.RealmName(story.Realm)} {story.Class} - {story.Race} {story.Gender} - {story.Personality}";
                AddText(lines, StoryCopy);
                lines.Add(new Line(string.Empty));
                AddText(lines, story.Background);
                AddText(lines, $"\"{story.Greeting}\"");
                AddText(lines, $"\"{story.FieldNote}\"");
                AddText(lines, $"Group roles: {BotPartyRoles.Label(story.Class)}.");
                AddText(lines, owned != null
                    ? "Already in your roster."
                    : full ? "Your roster is full." : "Available: free to recruit, starting at level 1.");
                lines.Add(new Line(string.Empty));
                // The authored catalog records no preferred build yet, so the class default is preselected.
                string storyBuild = owned == null ? AddRecruitBuilds(session, lines, key, story.Class) : null;
                AddText(lines, PermanenceCopy);
                if (owned != null)
                {
                    string ownedKey = RecordKey(owned);
                    choices.Add(new Choice("[Open in roster]", true, "open:" + ownedKey, () => ShowInRoster(session, ownedKey)));
                }
                else
                {
                    string storyKey = story.Key;
                    choices.Add(new Choice("[Recruit]", !full, $"recruit:{key}:{storyBuild}",
                        () => RecruitStory(player, session, storyKey, storyBuild)));
                }
                return;
            }

            if (TryParseGeneratedKey(key, out eRealm realm, out eCharacterClass characterClass))
            {
                view.Header = $"New {characterClass}";
                view.HeaderRealm = realm;
                view.Subheader = $"{TemporaryGroupClassCatalog.RealmName(realm)} {characterClass} - group roles: {BotPartyRoles.Label(characterClass)}";
                AddText(lines, CreateCopy);
                lines.Add(new Line(string.Empty));
                string build = AddRecruitBuilds(session, lines, key, characterClass);
                AddText(lines, "Recruiting is free and starts at level 1.");
                AddText(lines, $"Roster: {roster.Count}/{PlayerCompanionRoster.MaximumRosterSize}.");
                lines.Add(new Line(string.Empty));
                AddText(lines, PermanenceCopy);
                choices.Add(new Choice("[Create]", !full, $"create:{key}:{build}",
                    () => RecruitGenerated(player, session, key, build)));
                return;
            }

            view.Header = "Recruit";
            AddText(lines, "No story companion or class matches the current filters.");
            lines.Add(new Line(string.Empty));
            AddText(lines, StoryCopy);
            AddText(lines, CreateCopy);
            AddText(lines, PermanenceCopy);
        }

        /// <summary>
        /// Lists a class's builds for recruitment with the class default preselected.
        /// Returns the chosen plan ID, or null for the class default.
        /// </summary>
        private static string AddRecruitBuilds(CompanionManagerSession session, List<Line> lines, string entryKey,
            eCharacterClass characterClass)
        {
            IReadOnlyList<CompanionBuildPlan> builds = CompanionBuildPlanCatalog.GetPlans(characterClass);
            if (builds.Count == 0)
            {
                AddText(lines, $"Manual training only: {CompanionBuildPlanCatalog.GetBlocker(characterClass)}.");
                lines.Add(new Line(string.Empty));
                return null;
            }
            CompanionBuildPlan chosen = ChosenBuild(session, entryKey, characterClass, builds[0]);
            lines.Add(new Line("Build (select one, then recruit):"));
            AddBuildList(session, lines, entryKey, builds, builds[0], "default", chosen);
            AddText(lines, $"Recruits with the {chosen.Name} build; it trains automatically at every level. " +
                           $"Level 50: {chosen.FormatTargets()}. Switching later is free.");
            lines.Add(new Line(string.Empty));
            return chosen == builds[0] ? null : chosen.Id;
        }

        /// <summary>The build chosen for this entry, or <paramref name="fallback"/> when none is.</summary>
        private static CompanionBuildPlan ChosenBuild(CompanionManagerSession session, string entryKey,
            eCharacterClass characterClass, CompanionBuildPlan fallback) =>
            session.BuildChoice.EntryKey == entryKey &&
            CompanionBuildPlanCatalog.TryGetPlanById(characterClass, session.BuildChoice.PlanId, out CompanionBuildPlan chosen)
                ? chosen
                : fallback;

        private static void AddBuildList(CompanionManagerSession session, List<Line> lines, string entryKey,
            IReadOnlyList<CompanionBuildPlan> builds, CompanionBuildPlan marked, string markedLabel, CompanionBuildPlan chosen)
        {
            foreach (CompanionBuildPlan build in builds)
            {
                string planId = build.Id;
                string state = build == marked ? $" ({markedLabel})" : string.Empty;
                string selected = build == chosen ? " <" : string.Empty;
                lines.Add(new Line($"  {build.Name}{state}{selected}", "build:" + planId,
                    () => session.BuildChoice = (entryKey, planId)));
                foreach (string line in CompanionManagerSession.Wrap(Sanitize(BuildRoleText(build), int.MaxValue),
                             DetailWidth - TextWidth(BuildIndent)))
                    lines.Add(new Line(BuildIndent + line));
            }
        }

        public static string BuildRoleText(CompanionBuildPlan build) =>
            $"{build.Role}. Sets role: {BotPartyRoles.GroupRoleLabel(build.PrimaryRole)}" +
            (build.CrowdControlDuty ? ", also controls adds." : ".");

        private static bool TryParseGeneratedKey(string key, out eRealm realm, out eCharacterClass characterClass)
        {
            realm = eRealm.None;
            characterClass = eCharacterClass.Unknown;
            string[] parts = key?.Split(':') ?? Array.Empty<string>();
            if (parts.Length != 3 || parts[0] != "g" || !int.TryParse(parts[1], out int realmValue) ||
                !int.TryParse(parts[2], out int classValue))
                return false;
            (eRealm Realm, eCharacterClass CharacterClass, string Role) match = TemporaryGroupClassCatalog.All()
                .FirstOrDefault(entry => (int)entry.Realm == realmValue && (int)entry.CharacterClass == classValue);
            if (match.Role == null)
                return false;
            realm = match.Realm;
            characterClass = match.CharacterClass;
            return true;
        }

        private delegate bool RosterOperation(GamePlayer owner, string nameOrId, out string message);

        private delegate bool GearOperation(out string message);

        private static string Run(RosterOperation operation, GamePlayer player, string id)
        {
            operation(player, id, out string message);
            return message;
        }

        private static void DeleteCompanion(GamePlayer player, CompanionManagerSession session, string id)
        {
            if (session.DetailTab != CompanionManagerDetailTab.Overview ||
                session.Current.SelectedKey != "c:" + id)
            {
                session.DeleteConfirmationId = null;
                Report(player, session, "Select the companion on Overview before deleting.");
                return;
            }
            if (session.DeleteConfirmationId != id)
            {
                session.DeleteConfirmationId = id;
                Report(player, session, "Confirm deletion of this companion, or cancel.");
                return;
            }
            session.DeleteConfirmationId = null;
            PlayerCompanionRoster.TryDelete(player, id, out string message);
            Report(player, session, message);
        }

        private static void Gear(GamePlayer player, CompanionManagerSession session, GearOperation operation)
        {
            operation(out string message);
            session.Bag?.Refresh();
            Report(player, session, message);
        }

        private static void SelectItem(CompanionManagerSession session, string itemId, bool keepSlot)
        {
            session.SelectedItemId = session.SelectedItemId == itemId ? null : itemId;
            if (!keepSlot)
                session.SelectedSlot = eInventorySlot.Invalid;
            session.DetailOffset = 0;
        }

        /// <summary>Opens a worn slot in the Gear tab, or closes it when it is already open.</summary>
        private static void SelectSlot(CompanionManagerSession session, eInventorySlot slot)
        {
            session.SelectedSlot = session.SelectedSlot == slot ? eInventorySlot.Invalid : slot;
            session.SelectedItemId = null;
            session.DetailOffset = 0;
        }

        private static void SetTraining(GamePlayer player, CompanionManagerSession session, string id, bool automatic)
        {
            string message;
            if (automatic)
                PlayerCompanionRoster.TrySetAutomaticTrainingMode(player, id, out message);
            else
                PlayerCompanionRoster.TrySetManualTrainingMode(player, id, out message);
            Report(player, session, message);
        }

        private static void SetTactics(GamePlayer player, CompanionManagerSession session, string id, string kind, string value)
        {
            PlayerCompanionRoster.TrySetTactics(player, id, kind, value, out string message);
            Report(player, session, message);
        }

        private static void SetBombUse(GamePlayer player, CompanionManagerSession session, string id, string value)
        {
            PlayerCompanionRoster.TrySetBombUsePreference(player, id, value, out string message);
            Report(player, session, message);
        }

        private static void SetRangedAoe(GamePlayer player, CompanionManagerSession session, string id, string value)
        {
            PlayerCompanionRoster.TrySetRangedAoePreference(player, id, value, out string message);
            Report(player, session, message);
        }

        private static void TrainRank(GamePlayer player, CompanionManagerSession session, string id, string line, int level)
        {
            if (!PlayerCompanionRoster.TryGetActiveCompanionById(player, id, out GameBot companion))
            {
                Report(player, session, "Invite that companion first, then train them while they are active in your group.");
                return;
            }
            if (!PlayerCompanionCommandHandler.CanUseCompanionTrainer(player.Client, companion))
            {
                Report(player, session, $"Target a trainer who can train {companion.Name}'s class, then choose again.");
                return;
            }
            Specialization spec = companion.GetSpecList().FirstOrDefault(item => item.Trainable && item.KeyName == line);
            if (spec == null || spec.Level + 1 != level || level > companion.Level)
            {
                Report(player, session, "That career line or rank changed. Please choose again.");
                return;
            }
            if (!companion.TryTrainCompanionSpecialization(spec, level, out int spent, out string error))
            {
                Report(player, session, error);
                return;
            }
            bool saved = PlayerCompanionRoster.SaveProgress(companion);
            if (!saved)
                PlayerCompanionProgressPersistence.Queue(companion);
            Report(player, session, saved
                ? $"{companion.Name} trained {spec.Name} to {spec.Level}; spent {spent} points, {companion.UnspentSpecPoints} remain."
                : $"{companion.Name} trained {spec.Name} to {spec.Level}, but the save failed. Another save attempt is queued.");
        }

        private static void SpendRealmAbility(GamePlayer player, CompanionManagerSession session, string id, string abilityKey)
        {
            PlayerCompanionRoster.TrySpendRealmAbility(player, id, abilityKey, out string message);
            Report(player, session, message);
        }

        private static void UseBuild(GamePlayer player, CompanionManagerSession session, string id, string planId)
        {
            if (planId == null)
            {
                Report(player, session, "Select a different build first, then choose [Use build].");
                return;
            }
            if (PlayerCompanionRoster.TrySelectBuild(player, id, planId, out string message))
                session.BuildChoice = default;
            Report(player, session, message);
        }

        private static void Respec(GamePlayer player, CompanionManagerSession session, string id)
        {
            PlayerCompanionCommandHandler.TryBeginCompanionRespec(player.Client, player, id, out string message);
            Report(player, session, message);
        }

        private static void OpenBag(GamePlayer player, CompanionManagerSession session, string id)
        {
            if (!PlayerCompanionRoster.TryGetActiveCompanionById(player, id, out GameBot companion))
            {
                Report(player, session, "Invite that companion first; the bag opens only for an active companion.");
                return;
            }
            session.Bag = new PersistentCompanionInventoryView(player, id);
            session.Bag.Open();
            session.DetailTab = CompanionManagerDetailTab.Gear;
            session.Message = $"{companion.Name}'s inventory is open: worn slots 1-19, backpack 21-60. " +
                              "Drag from your bag onto a worn slot to equip.";
        }

        private static void ShowInRoster(CompanionManagerSession session, string recordKey)
        {
            session.Tab = CompanionManagerTab.Roster;
            session.Roster.SelectedKey = recordKey;
            session.DetailTab = CompanionManagerDetailTab.Overview;
            session.DetailOffset = 0;
            session.SelectedItemId = null;
            session.SelectedSlot = eInventorySlot.Invalid;
        }

        private static void RecruitStory(GamePlayer player, CompanionManagerSession session, string storyKey, string planId)
        {
            CompanionCharacterCatalog.Character story = CompanionCharacterCatalog.Find(storyKey);
            if (story == null)
            {
                Report(player, session, "That story companion is no longer in the cast.");
                return;
            }
            PlayerCompanionRoster.TryRecruitAuthored(player, story.Name, planId, out PlayerCompanionRecord record, out string message);
            Report(player, session, message);
            if (record?.IsPersisted == true)
            {
                session.BuildChoice = default;
                ShowInRoster(session, RecordKey(record));
            }
        }

        private static void RecruitGenerated(GamePlayer player, CompanionManagerSession session, string key, string planId)
        {
            if (!TryParseGeneratedKey(key, out eRealm realm, out eCharacterClass characterClass))
            {
                Report(player, session, "That class is no longer available for recruitment.");
                return;
            }
            PlayerCompanionRoster.TryRecruit(player, realm, characterClass, planId, out PlayerCompanionRecord record, out string message);
            Report(player, session, message);
            if (record?.IsPersisted == true)
            {
                session.BuildChoice = default;
                ShowInRoster(session, RecordKey(record));
            }
        }

        private static DbInventoryItem[] LoadSavedItems(string companionId)
        {
            try
            {
                return GameServer.Database.SelectObjects<DbInventoryItem>(DB.Column("OwnerID")
                    .IsEqualTo(PlayerCompanionRoster.InventoryOwnerId(companionId))).ToArray();
            }
            catch (Exception)
            {
                return Array.Empty<DbInventoryItem>();
            }
        }

        private static void AddText(List<Line> lines, string text)
        {
            foreach (string line in CompanionManagerSession.Wrap(Sanitize(text, int.MaxValue), DetailWidth))
                lines.Add(new Line(line));
        }

        private static void Report(GamePlayer player, CompanionManagerSession session, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            session.Message = message;
            Tell(player, message);
        }

        private static void Tell(GamePlayer player, string message) =>
            player.Out?.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);

        private static void ScheduleGuidance(GamePlayer player, CompanionManagerSession session)
        {
            if (session.ClientConfirmed || session.GuidancePending || player.Client?.ClientState != GameClient.eClientState.Playing)
                return;
            session.GuidancePending = true;
            _ = new ECSGameTimer(player, _ =>
            {
                session.GuidancePending = false;
                if (!session.ClientConfirmed && Sessions.TryGetValue(player, out CompanionManagerSession current) &&
                    ReferenceEquals(current, session) && player.Client?.ClientState == GameClient.eClientState.Playing)
                    PlayerCompanionCommandHandler.ShowClientGuidance(player.Client);
                return 0;
            }, GuidanceDelayMilliseconds);
        }

        private static void Send(GamePlayer player, byte operation, int index = 0, string text = "")
        {
            if (player.Client?.ClientState != GameClient.eClientState.Playing)
                return;
            // Fixed 128-byte body; the native parser validates marker, version,
            // operation, label index, and the terminator before use.
            byte[] body = BuildBody(operation, index, text);
            using var packet = PooledObjectFactory.GetForTick<GSTCPPacketOut>().Init((byte)eServerPackets.DebugMode);
            packet.Write(body, 0, body.Length);
            player.Out.SendTCP(packet);
        }
    }
}
