using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS.Commands
{
    public static class TemporaryGroupClassCatalog
    {

        private static readonly eRealm[] PlayableRealms =
        [
            eRealm.Albion,
            eRealm.Midgard,
            eRealm.Hibernia,
        ];

        public static IEnumerable<(eCharacterClass CharacterClass, string Role)> ForRealm(eRealm realm) =>
            AutonomousBotIdentityGenerator.GetEraClasses(realm)
                .Select(characterClass => (characterClass, BotPartyRoles.Label(characterClass)));

        public static IEnumerable<(eRealm Realm, eCharacterClass CharacterClass, string Role)> All() =>
            PlayableRealms.SelectMany(realm => ForRealm(realm)
                .Select(entry => (realm, entry.CharacterClass, entry.Role)));

        public static string RealmName(eRealm realm)
        {
            return realm switch
            {
                eRealm.Albion => "Albion",
                eRealm.Midgard => "Midgard",
                eRealm.Hibernia => "Hibernia",
                _ => realm.ToString(),
            };
        }

        public static bool TryResolveForCompanion(
            eRealm preferredRealm,
            string input,
            out eRealm realm,
            out eCharacterClass characterClass,
            out bool ambiguous)
        {
            string wanted = Normalize(input);
            (eRealm Realm, eCharacterClass CharacterClass, string Role)[] entries = All().ToArray();

            var explicitMatches = entries.Where(entry =>
                Normalize(RealmName(entry.Realm) + entry.CharacterClass) == wanted ||
                Normalize(entry.Realm.ToString() + entry.CharacterClass) == wanted).ToArray();
            if (explicitMatches.Length == 1)
            {
                realm = explicitMatches[0].Realm;
                characterClass = explicitMatches[0].CharacterClass;
                ambiguous = false;
                return true;
            }

            var classMatches = entries.Where(entry =>
                Normalize(entry.CharacterClass.ToString()) == wanted).ToArray();
            var preferredMatches = classMatches.Where(entry => entry.Realm == preferredRealm).ToArray();
            if (preferredMatches.Length == 1)
            {
                realm = preferredMatches[0].Realm;
                characterClass = preferredMatches[0].CharacterClass;
                ambiguous = false;
                return true;
            }

            if (classMatches.Length == 1)
            {
                realm = classMatches[0].Realm;
                characterClass = classMatches[0].CharacterClass;
                ambiguous = false;
                return true;
            }

            ambiguous = classMatches.Length > 1 || explicitMatches.Length > 1;
            realm = eRealm.None;
            characterClass = default;
            return false;
        }

        public static bool TryResolve(eRealm realm, string input, out eCharacterClass characterClass)
        {
            if (TryResolve(realm, input, out _, out characterClass))
                return true;

            characterClass = default;
            return false;
        }

        public static bool TryResolve(
            eRealm defaultRealm,
            string input,
            out eRealm realm,
            out eCharacterClass characterClass)
        {
            string wanted = Normalize(input);
            foreach ((eRealm candidateRealm, eCharacterClass candidate, _) in All())
            {
                bool explicitRealm = Normalize(RealmName(candidateRealm) + candidate) == wanted ||
                                     Normalize(candidateRealm.ToString() + candidate) == wanted;
                bool defaultRealmClass = candidateRealm == defaultRealm && Normalize(candidate.ToString()) == wanted;
                if (!explicitRealm && !defaultRealmClass)
                    continue;

                realm = candidateRealm;
                characterClass = candidate;
                return true;
            }

            realm = eRealm.None;
            characterClass = default;
            return false;
        }

        private static string Normalize(string value)
        {
            return new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }
    }

    /// <summary>
    /// The 1.65 client only turns bracketed popup text into clickable choices
    /// when an NPC owns the conversation.  This invisible, short-lived NPC is
    /// created solely for the requesting player and remains available so the
    /// player can add several companions without retyping the command.
    /// </summary>
    public sealed class TemporaryGroupSpawnMenu : GameNPC
    {
        private const string ActiveMenuProperty = "OfflineDAoC.ActiveSpawnClassMenu";
        private const int MenuLifetimeMilliseconds = 600_000;
        private const ushort InvisibleModel = 150;

        private readonly GamePlayer _owner;
        private readonly GameObject _previousTarget;
        private ECSGameTimer _expirationTimer;
        private bool _closed;

        private TemporaryGroupSpawnMenu(GamePlayer owner)
        {
            _owner = owner;
            _previousTarget = owner.TargetObject;
            Name = "Companion Class Menu";
            Realm = owner.Realm;
            Level = 1;
            Model = InvisibleModel;
            Size = 1;
            Flags = eFlags.PEACE | eFlags.DONTSHOWNAME;
            X = owner.X;
            Y = owner.Y;
            Z = owner.Z;
            Heading = owner.Heading;
            CurrentRegionID = owner.CurrentRegionID;
        }

        public static string BuildMenuText(eRealm realm)
        {
            string links = string.Join('\n', TemporaryGroupClassCatalog.All()
                .GroupBy(entry => entry.Realm)
                .SelectMany(group => new[] { $"{TemporaryGroupClassCatalog.RealmName(group.Key)}:" }
                    .Concat(group.Select(entry => $"[{TemporaryGroupClassCatalog.RealmName(entry.Realm)}: {entry.CharacterClass}]")
                        .Chunk(4).Select(chunk => string.Join("   ", chunk)))));
            return $"Choose a companion from any realm to add to your group:\n\n{links}\n\nClick one class name.";
        }

        internal static bool Open(GamePlayer player)
        {
            if (player?.CurrentRegion == null)
                return false;

            player.TempProperties.GetProperty<TemporaryGroupSpawnMenu>(ActiveMenuProperty)?.Close(true);

            var menu = new TemporaryGroupSpawnMenu(player);
            if (!menu.AddToWorld())
                return false;

            player.TempProperties.SetProperty(ActiveMenuProperty, menu);
            player.TargetObject = menu;
            player.Out.SendChangeTarget(menu);
            menu._expirationTimer = new ECSGameTimer(menu, _ =>
            {
                menu.Close(true);
                return 0;
            }, MenuLifetimeMilliseconds);
            player.Out.SendMessage(BuildMenuText(player.Realm), eChatType.CT_Say, eChatLoc.CL_PopupWindow);
            return true;
        }

        public override bool WhisperReceive(GameLiving source, string text)
        {
            // This private menu is only usable by its owner. Do not invoke the
            // normal NPC whisper throttle: each link is an explicit UI click,
            // and the player may legitimately add several classes in a row.
            if (!ReferenceEquals(source, _owner) || string.IsNullOrWhiteSpace(text))
                return false;

            if (!TemporaryGroupClassCatalog.TryResolve(_owner.Realm, text, out eRealm realm, out eCharacterClass characterClass))
                return false;

            SpawnTemporaryGroupBotCommandHandler.Spawn(_owner.Client, realm, characterClass);

            // A class can be clicked repeatedly and other classes can be added
            // without typing /spawn again. Reassert the invisible conversation
            // target and reopen the same popup because this client closes an
            // NPC speech choice after the link is clicked.
            if (!_closed && _owner.ObjectState is eObjectState.Active &&
                _owner.CurrentRegion == CurrentRegion)
            {
                _owner.TargetObject = this;
                _owner.Out.SendChangeTarget(this);
                _owner.Out.SendMessage(BuildMenuText(_owner.Realm), eChatType.CT_Say, eChatLoc.CL_PopupWindow);
            }
            return true;
        }

        private void Close(bool restoreTarget)
        {
            if (_closed)
                return;

            _closed = true;
            _expirationTimer?.Stop();
            _expirationTimer = null;

            if (ReferenceEquals(_owner?.TempProperties.GetProperty<TemporaryGroupSpawnMenu>(ActiveMenuProperty), this))
                _owner.TempProperties.RemoveProperty(ActiveMenuProperty);

            if (restoreTarget && _owner?.ObjectState is eObjectState.Active && ReferenceEquals(_owner.TargetObject, this))
            {
                GameObject target = _previousTarget?.ObjectState is eObjectState.Active &&
                                    _previousTarget.CurrentRegion == _owner.CurrentRegion
                    ? _previousTarget
                    : null;
                _owner.TargetObject = target;
                _owner.Out.SendChangeTarget(target);
            }

            Delete();
        }
    }

    [CmdAttribute("&classes", ePrivLevel.Player, "Lists Classic + SI class names grouped by realm", "/classes")]
    public sealed class ClassesCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            DisplayMessage(client, "Classic + SI classes by realm:");
            foreach (var realmGroup in TemporaryGroupClassCatalog.All().GroupBy(entry => entry.Realm))
            {
                DisplayMessage(client, $"{TemporaryGroupClassCatalog.RealmName(realmGroup.Key)}:");
                foreach (string line in realmGroup
                             .Select(entry => entry.CharacterClass.ToString())
                             .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                             .Chunk(4)
                             .Select(chunk => string.Join(", ", chunk)))
                    DisplayMessage(client, line);
            }
            DisplayMessage(client, "Use /companions recruit <class> to add a persistent recruit from any realm.");
            DisplayMessage(client, "For temporary helpers, use /spawn <class> for your realm or /spawn <realm> <class>.");
        }
    }

    [CmdAttribute("&spawn", ePrivLevel.Player, "Opens the companion class menu or creates a temporary helper", "/spawn [class name]")]
    public sealed class SpawnTemporaryGroupBotCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client.Player;
            if (args.Length < 2)
            {
                if (player.Group != null && player.Group.MemberCount >= player.Group.MaximumMemberCount)
                {
                    DisplayMessage(client, "Your group is full.");
                    return;
                }

                if (!TemporaryGroupSpawnMenu.Open(player))
                    DisplayMessage(client, "The companion class menu could not open here. Use /spawn <class name> instead.");
                return;
            }

            string requestedClass = string.Join(' ', args.Skip(1));
            if (!TemporaryGroupClassCatalog.TryResolve(player.Realm, requestedClass, out eRealm realm, out eCharacterClass characterClass))
            {
                DisplayMessage(client, $"'{requestedClass}' is not a Classic + SI class from any realm. Type /classes.");
                return;
            }

            Spawn(client, realm, characterClass);
        }

        internal static void Spawn(GameClient client, eCharacterClass characterClass)
        {
            if (client?.Player != null)
                Spawn(client, client.Player.Realm, characterClass);
        }

        internal static void Spawn(GameClient client, eRealm realm, eCharacterClass characterClass)
        {
            GamePlayer player = client?.Player;
            if (player == null || !TemporaryGroupClassCatalog.ForRealm(realm)
                    .Any(entry => entry.CharacterClass == characterClass))
                return;

            if (player.Group != null && player.Group.MemberCount >= player.Group.MaximumMemberCount)
            {
                client.Out.SendMessage("Your group is full.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            eGender gender = Random.Shared.Next(2) == 0 ? eGender.Male : eGender.Female;
            AutonomousBotIdentityGenerator.Identity identity =
                AutonomousBotIdentityGenerator.GenerateForClass(realm, gender, characterClass);
            byte gearLevel = player.Level == 50 ? (byte)50 :
                (byte)Random.Shared.Next(Math.Max(1, player.Level - 10), player.Level + 1);
            GameBot helper;

            try
            {
                helper = new GameBot(
                    player,
                    (byte)characterClass,
                    identity.Name,
                    (byte)identity.Race,
                    (byte)identity.Gender,
                    temporaryGroupHelper: true,
                    equipmentLevel: gearLevel);
            }
            catch (Exception exception)
            {
                client.Out.SendMessage($"Could not create the {characterClass} helper: {exception.Message}",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            double angle = Random.Shared.NextDouble() * Math.PI * 2;
            int distance = Random.Shared.Next(110, 221);
            Vector3 current = new(player.X, player.Y, player.Z);
            Vector3 desired = new(
                player.X + (float)(Math.Cos(angle) * distance),
                player.Y + (float)(Math.Sin(angle) * distance),
                player.Z);
            Vector3 spawn = PathfindingProvider.Instance.GetMoveAlongSurface(
                player.CurrentZone,
                current,
                desired,
                PathfindingProvider.Instance.DefaultFilters) ?? current;
            helper.X = (int)Math.Round(spawn.X);
            helper.Y = (int)Math.Round(spawn.Y);
            helper.Z = (int)Math.Round(spawn.Z);
            helper.Heading = player.Heading;
            helper.CurrentRegionID = player.CurrentRegionID;

            if (!helper.AddToWorld())
            {
                helper.Delete();
                client.Out.SendMessage("The temporary helper could not enter this region.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            if (player.Group == null)
            {
                var group = new Group(player);
                GroupMgr.AddGroup(group);
                group.AddMember(player);
            }

            if (!player.Group.AddMember(helper))
            {
                helper.Delete();
                client.Out.SendMessage("The temporary helper could not join your group.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }

            helper.EnterPlayerLedGroup(player);
            helper.Follow(player, BotManager.FOLLOW_DISTANCE, BotManager.MAX_FOLLOW_DISTANCE);
            if (helper.Brain is BotBrain brain)
                brain.FSM.SetCurrentState(eFSMStateType.FOLLOW);

            client.Out.SendMessage(
                $"{helper.Name}, level {helper.Level} {characterClass}, joined fully equipped: armor/accessories level {Math.Max(1, helper.Level - 10)}–{helper.Level}, weapons/offhand level {Math.Max(1, helper.Level - 2)}–{helper.Level}. " +
                "This helper earns normal XP while active, takes no loot, is never saved, and vanishes when removed from the group.",
                eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }

    [CmdAttribute("&pull", ePrivLevel.Player, "Orders party bots and their pets to engage your target", "/pull")]
    public sealed class PullGroupCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            string message = Order(client.Player, "type /pull");
            if (message != null)
                DisplayMessage(client, message);
        }

        /// <summary>Orders the pull on the player's target; null when the attack rules already told the player why not.</summary>
        public static string Order(GamePlayer player, string retry)
        {
            if (player.TargetObject is not GameLiving target || !target.IsAlive)
                return $"Select a living enemy first, then {retry}.";
            if (!GameServer.ServerRules.IsAllowedToAttack(player, target, false))
                return null;

            return PlayerLedPullCoordinator.Begin(player, target);
        }
    }
}
