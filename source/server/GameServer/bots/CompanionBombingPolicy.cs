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
        public static GameNPC[] PullTargets(GameBot bot, GameLiving target, Spell spell, GameLiving center)
        {
            if (!CanUseBombs(bot) || !IsBombSpell(bot, spell) || bot.Group == null ||
                target is not GameNPC focus || center == null || focus.CurrentRegion != center.CurrentRegion)
                return [];

            HashSet<GameLiving> focused = CompanionAddControl.FocusTargets(bot);
            focused.Add(focus);
            int radius = System.Math.Clamp(spell.Radius, 1, ushort.MaxValue);
            return focused.OfType<GameNPC>()
                .Where(npc => npc.IsAlive && npc.ObjectState == GameObject.eObjectState.Active &&
                    npc.CurrentRegion == center.CurrentRegion && center.IsWithinRadius(npc, radius) &&
                    !BotPvpCrowdControl.PlayerLike(npc) && !CompanionAddControl.ProtectsMezz(bot, npc) &&
                    GameServer.ServerRules.IsAllowedToAttack(bot, npc, true))
                .Distinct()
                .ToArray();
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
}
