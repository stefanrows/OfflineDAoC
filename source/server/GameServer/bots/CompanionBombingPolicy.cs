using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>Saved PBAoE preference and safe pull checks for player-led companion groups.</summary>
    public static class CompanionBombingPolicy
    {
        public const string Auto = "auto";
        public const string Bomb = "bomb";
        public const string Off = "off";
        public const int TankAggroWaitMilliseconds = 2500;

        public static bool SupportsClass(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Spiritmaster or eCharacterClass.Wizard or eCharacterClass.Enchanter or eCharacterClass.Eldritch;

        public static bool SupportsClass(GameBot bot) => bot?.CharacterClass != null &&
            SupportsClass((eCharacterClass)bot.CharacterClass.ID);

        public static bool IsPlayerLedCompanion(GameBot bot) =>
            bot?.IsPlayerLedGroup == true && !bot.IsAutonomousWorldBot &&
            (bot.IsPersistentPlayerCompanion || bot.IsTemporaryGroupHelper);

        public static string Choice(PlayerCompanionRecord record) => record?.BombUsePreference?.Trim().ToLowerInvariant() switch
        {
            Bomb => Bomb,
            Off => Off,
            _ => Auto,
        };

        public static string NextChoice(string current) => current switch
        {
            Auto => Bomb,
            Bomb => Off,
            _ => Auto,
        };

        public static string ChoiceLabel(string choice) => choice switch
        {
            Bomb => "Bomb (2+ targets)",
            Off => "Off",
            _ => "Auto (3+ targets)",
        };

        public static bool IsBombSpell(GameBot bot, Spell spell) =>
            IsPlayerLedCompanion(bot) && SupportsClass(bot) && spell is { IsHarmful: true, IsPBAoE: true } &&
            spell.SpellType != eSpellType.TurretPBAoE && BotCasterPriority.IsDamage(spell);

        public static bool CanUseBombs(GameBot bot) => IsPlayerLedCompanion(bot) && SupportsClass(bot) &&
            Choice(bot.PlayerCompanionRecord) != Off;

        public static int MinimumTargets(GameBot bot) => Choice(bot?.PlayerCompanionRecord) == Bomb ? 2 : 3;

        /// <summary>
        /// The pull consists only of live, attackable NPCs that group members
        /// have selected or are fighting. It never treats nearby idle spawns as
        /// a reason to move a caster into range or trigger a bomb.
        /// </summary>
        public static GameLiving[] PullTargets(GameBot bot, GameLiving target, Spell spell, GameLiving center)
        {
            if (!CanUseBombs(bot) || !IsBombSpell(bot, spell) || bot.Group == null ||
                target == null || center == null || target.CurrentRegion != center.CurrentRegion)
                return [];

            int radius = System.Math.Clamp(spell.Radius, 1, ushort.MaxValue);
            if (BotPvpCrowdControl.PlayerLike(target))
            {
                // A Bomb preference permits a committed PvP clump. Never pull
                // idle players into combat just because they are standing near it.
                if (Choice(bot.PlayerCompanionRecord) != Bomb ||
                    !BotPvpCrowdControl.IsInFightWith(bot, target, null))
                    return [];
                GameLiving[] nearby = center.GetNPCsInRadius((ushort)radius).Cast<GameLiving>()
                    .Concat(center.GetPlayersInRadius((ushort)radius))
                    .Where(enemy => enemy.IsAlive && enemy.ObjectState == GameObject.eObjectState.Active &&
                        BotPvpCrowdControl.PlayerLike(enemy) &&
                        GameServer.ServerRules.IsAllowedToAttack(bot, enemy, true))
                    .Distinct().ToArray();
                if (nearby.Any(enemy => enemy.IsMezzed || CompanionAddControl.ProtectsMezz(bot, enemy) ||
                    !BotPvpCrowdControl.IsInFightWith(bot, enemy, null)))
                    return [];
                return nearby;
            }

            if (target is not GameNPC focus) return [];
            HashSet<GameLiving> focused = CompanionAddControl.FocusTargets(bot);
            focused.Add(focus);
            // Adds on healers or casters are part of the pull even when nobody
            // has them targeted; idle spawns in the radius still do not count.
            focused.UnionWith(CompanionAddControl.EngagedWithGroup(bot.Group,
                center.GetNPCsInRadius((ushort)radius).Cast<GameNPC>()));
            return focused.OfType<GameNPC>()
                .Where(npc => npc.IsAlive && npc.ObjectState == GameObject.eObjectState.Active &&
                    npc.CurrentRegion == center.CurrentRegion && center.IsWithinRadius(npc, radius) &&
                    !BotPvpCrowdControl.PlayerLike(npc) && !CompanionAddControl.ProtectsMezz(bot, npc) &&
                    GameServer.ServerRules.IsAllowedToAttack(bot, npc, true))
                .Distinct()
                .Cast<GameLiving>().ToArray();
        }

        public static bool HasSufficientPull(GameBot bot, GameLiving target, Spell spell, GameLiving center) =>
            PullTargets(bot, target, spell, center).Length >= MinimumTargets(bot);

        public static bool HasTank(GameBot bot) => bot?.Group?.GetMembersInTheGroup().Any(IsTank) == true;

        public static bool TankHasAggro(GameBot bot, GameLiving target, Spell spell)
        {
            if (bot?.Group == null || !HasTank(bot))
                return false;

            HashSet<GameLiving> tanks = bot.Group.GetMembersInTheGroup().Where(IsTank).ToHashSet();
            return PullTargets(bot, target, spell, target).Any(npc => npc.TargetObject is GameLiving currentTarget &&
                tanks.Contains(currentTarget));
        }

        private static bool IsTank(GameLiving member)
        {
            if (member is GameBot bot)
                return BotPartyRoles.IsTank(bot);
            return member is GamePlayer player && player.CharacterClass != null &&
                BotPartyRoles.For((eCharacterClass)player.CharacterClass.ID) == BotPartyRole.Tank;
        }
    }

    /// <summary>Tracks the tank-aggro grace period for one focused pull target.</summary>
    internal sealed class CompanionBombTankWait
    {
        private object _target;
        private long _startedTick;
        private bool _expired;

        public bool ShouldWait(object target, long now)
        {
            if (target == null)
            {
                Reset();
                return false;
            }

            if (!ReferenceEquals(_target, target))
            {
                _target = target;
                _startedTick = now;
                _expired = false;
            }

            if (_expired)
                return false;

            if (now - _startedTick >= CompanionBombingPolicy.TankAggroWaitMilliseconds)
            {
                _expired = true;
                return false;
            }

            return true;
        }

        public void Reset()
        {
            _target = null;
            _startedTick = 0;
            _expired = false;
        }
    }

    /// <summary>
    /// Loose volley timing for one group's companion bombers: the first bomber in
    /// position waits briefly so the others can land their bombs together, then
    /// the group chains freely until the fight pauses. Casts are never cut short.
    /// </summary>
    internal sealed class CompanionBombVolley
    {
        public const int MaxHoldMilliseconds = 1200;
        public const int ChainWindowMilliseconds = 6000;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Group, CompanionBombVolley> Groups = new();

        private readonly HashSet<object> _arrived = new();
        private readonly object _lock = new();
        private long _openedTick;
        private long _lastReleaseTick = long.MinValue / 2;

        public static CompanionBombVolley For(Group group) => Groups.GetValue(group, _ => new CompanionBombVolley());

        /// <summary>True while <paramref name="bomber"/> should hold its first bomb for the rest of the volley.</summary>
        public bool ShouldHold(object bomber, IReadOnlyCollection<object> bombers, long now)
        {
            lock (_lock)
            {
                if (now - _lastReleaseTick <= ChainWindowMilliseconds)
                {
                    _lastReleaseTick = now;
                    return false;
                }

                if (_arrived.Count > 0 && now - _openedTick > ChainWindowMilliseconds)
                    _arrived.Clear();
                if (_arrived.Count == 0)
                    _openedTick = now;
                _arrived.Add(bomber);

                if (bombers.All(_arrived.Contains) || now - _openedTick >= MaxHoldMilliseconds)
                {
                    _arrived.Clear();
                    _lastReleaseTick = now;
                    return false;
                }
                return true;
            }
        }
    }
}
