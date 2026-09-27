using System.Collections.Generic;
using System.Linq;
using System.Numerics;

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

        /// <summary>
        /// A PBAoE hits hardest at its caster's feet and fades to nothing at the
        /// edge, so a bomber runs into the middle of the pile. The pile is the
        /// focus mob plus every pull target around it, which in PvE is the
        /// knot of mobs beating on the tank.
        /// </summary>
        public static bool TryGetPileCentre(GameBot bot, GameLiving target, Spell spell, out Vector3 centre)
        {
            centre = default;
            GameLiving[] pile = PullTargets(bot, target, spell, target);
            if (pile.Length < MinimumTargets(bot))
                return false;
            centre = Centroid(pile.Append(target).Distinct().Select(member => new Vector3(member.X, member.Y, member.Z)));
            return true;
        }

        public static Vector3 Centroid(IEnumerable<Vector3> points)
        {
            Vector3 sum = Vector3.Zero;
            int count = 0;
            foreach (Vector3 point in points)
            {
                sum += point;
                count++;
            }
            return count == 0 ? Vector3.Zero : sum / count;
        }

        /// <summary>A group Healer who could open with an area stun right now.</summary>
        public static bool HasReadyAreaStun(GameBot healer) =>
            healer?.CharacterClass?.ID == (int)eCharacterClass.Healer && !healer.IsIncapacitated &&
            (healer.Spells ?? []).Any(spell => spell?.SpellType == eSpellType.Stun && spell.Radius > 0 &&
                spell.Level <= healer.Level && healer.Mana >= healer.PowerCost(spell) &&
                healer.GetSkillDisabledDuration(spell) <= 0);

        /// <summary>Close enough to the centre that the whole pile takes near-full damage.</summary>
        public static int CentreTolerance(Spell spell) => System.Math.Clamp((spell?.Radius ?? 0) / 6, 30, 60);

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

    /// <summary>
    /// One bomber's run into the pile. It counts as in position within the
    /// tolerance (with some slack once there, so a drifting pile does not
    /// start a new run), or after a short give-up time so a blocked path or a
    /// kiting pile still gets bombed from where the bomber stands.
    /// </summary>
    internal sealed class CompanionBombCentreApproach
    {
        public const int GiveUpMilliseconds = 3000;
        // A pile that drifts after the first bomb gets only a short catch-up run.
        public const int DriftGiveUpMilliseconds = 1500;
        private const int StaleRunMilliseconds = 10_000;
        private object _target;
        private long _startedTick;
        private bool _inPosition;
        private bool _drift;
        private Vector3 _lastDestination;
        private bool _hasDestination;

        public bool Arrived(object target, float distance, int tolerance, long now)
        {
            if (!ReferenceEquals(_target, target))
            {
                // The next mob of the same pile is not a new run: a bomber
                // already standing in the knot keeps bombing.
                _target = target;
                _hasDestination = false;
                // A run in progress keeps its clock, so flipping targets
                // cannot hold the bomber at the edge forever.
                if (!_inPosition && now - _startedTick > StaleRunMilliseconds)
                {
                    _startedTick = now;
                    _drift = false;
                }
            }

            int allowed = _inPosition ? tolerance * 3 : tolerance;
            int giveUp = _drift ? DriftGiveUpMilliseconds : GiveUpMilliseconds;
            if (distance <= allowed)
                _inPosition = true;
            else if (_inPosition)
            {
                // The pile moved away after arrival: a new, short run.
                _inPosition = false;
                _drift = true;
                _startedTick = now;
            }
            else if (now - _startedTick >= giveUp)
                _inPosition = true;
            return _inPosition;
        }

        public bool IsInPositionFor(object target) => _inPosition && ReferenceEquals(_target, target);

        public bool ShouldRepath(Vector3 destination, bool moving)
        {
            if (moving && _hasDestination && Vector3.Distance(_lastDestination, destination) < 40)
                return false;
            _lastDestination = destination;
            _hasDestination = true;
            return true;
        }
    }

    /// <summary>
    /// The tank-aggro grace period, once per fight: the bomber lets the tank
    /// grab the pull before the first bomb, but switching to the next mob of
    /// the same fight does not start another wait. Reset when combat ends.
    /// </summary>
    internal sealed class CompanionBombTankWait
    {
        private object _target;
        private long _startedTick;
        private bool _expired;

        public bool ShouldWait(object target, long now)
        {
            if (target == null)
                return false;

            if (_target == null)
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

        /// <summary>The fight is under way (tank has aggro, bombs already fell): no more waiting.</summary>
        public void Finish(object target)
        {
            _target ??= target;
            _expired = true;
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

        /// <summary>A groupmate bombed recently: the fight is on, nobody waits any more.</summary>
        public bool ChainOpen(long now)
        {
            lock (_lock)
                return now - _lastReleaseTick <= ChainWindowMilliseconds;
        }

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
