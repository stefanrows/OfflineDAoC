using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>How a 2003 soloer of this class played PvE (docs/PVE_BOT_PLAY.md).</summary>
    public enum PveArchetype { PetCaster, Caster, Healer, Melee, Hybrid, Stealther }

    /// <summary>Rest targets before the next solo pull, in percent.</summary>
    public readonly record struct PveRestThresholds(int Health, int Power, int Endurance);

    /// <summary>
    /// Solo con and rest habits by archetype. Pure functions; the controller
    /// supplies class, level and whether a root or snare is known.
    /// </summary>
    public static class AutonomousPveArchetype
    {
        public const int RestJitterPercent = 5;

        public static bool IsPetClass(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Necromancer or eCharacterClass.Bonedancer or eCharacterClass.Theurgist or
            eCharacterClass.Animist or eCharacterClass.Enchanter or eCharacterClass.Cabalist or
            eCharacterClass.Spiritmaster;

        public static PveArchetype Of(eCharacterClass characterClass) => characterClass switch
        {
            _ when IsPetClass(characterClass) => PveArchetype.PetCaster,
            eCharacterClass.Wizard or eCharacterClass.Sorcerer or eCharacterClass.Runemaster or
            eCharacterClass.Eldritch or eCharacterClass.Mentalist or eCharacterClass.Heretic or
            eCharacterClass.Warlock or eCharacterClass.Bainshee or eCharacterClass.Druid or
            eCharacterClass.Shaman => PveArchetype.Caster,
            eCharacterClass.Cleric or eCharacterClass.Healer => PveArchetype.Healer,
            eCharacterClass.Infiltrator or eCharacterClass.Nightshade or eCharacterClass.Shadowblade or
            eCharacterClass.Scout or eCharacterClass.Ranger or eCharacterClass.Hunter => PveArchetype.Stealther,
            eCharacterClass.Paladin or eCharacterClass.Reaver or eCharacterClass.Friar or
            eCharacterClass.Minstrel or eCharacterClass.Thane or eCharacterClass.Skald or
            eCharacterClass.Valkyrie or eCharacterClass.Warden or eCharacterClass.Bard or
            eCharacterClass.Champion or eCharacterClass.Valewalker or eCharacterClass.Vampiir => PveArchetype.Hybrid,
            _ => PveArchetype.Melee,
        };

        /// <summary>
        /// The solo con ceiling before death steps. Pet casters, and casters
        /// that can root or snare, solo orange from level 5 ("Necro can solo
        /// oranges readily", D3561); everyone else keeps the yellow ceiling.
        /// </summary>
        public static ConColor NaturalSoloCeiling(eCharacterClass characterClass, bool hasRoot, int level) =>
            level < 5 ? ConColor.YELLOW : Of(characterClass) switch
            {
                PveArchetype.PetCaster => ConColor.ORANGE,
                PveArchetype.Caster when hasRoot => ConColor.ORANGE,
                _ => ConColor.YELLOW,
            };

        /// <summary>
        /// The con a soloer of this archetype looks for, never above the
        /// current ceiling: pets orange, casters yellow (orange with root or
        /// snare), melee and hybrids yellow (blue next), stealthers and
        /// healers blue. Below level five everyone takes yellow at most.
        /// </summary>
        public static ConColor PreferredCon(eCharacterClass characterClass, int level, bool hasRoot, ConColor ceiling)
        {
            ConColor preferred = Of(characterClass) switch
            {
                PveArchetype.PetCaster => ConColor.ORANGE,
                PveArchetype.Caster => hasRoot ? ConColor.ORANGE : ConColor.YELLOW,
                PveArchetype.Stealther or PveArchetype.Healer => ConColor.BLUE,
                _ => ConColor.YELLOW,
            };
            if (level < 5 && preferred > ConColor.YELLOW)
                preferred = ConColor.YELLOW;
            ConColor cap = ceiling < ConColor.GREEN ? ConColor.GREEN : ceiling;
            return preferred > cap ? cap : preferred;
        }

        /// <summary>Camp weight: the preferred con 3, one step easier 2, else 1.</summary>
        public static int ConWeight(ConColor campCon, ConColor preferred) =>
            campCon == preferred ? 3 : (int)campCon == (int)preferred - 1 ? 2 : 1;

        /// <summary>
        /// Robed casters rest after every pull to about 75 % power (D16161);
        /// melee to 80 % health and half endurance; hybrids and stealthers
        /// also to half power. Each bot is up to five points off either way.
        /// </summary>
        public static PveRestThresholds RestThresholds(eCharacterClass characterClass, long jitterSeed)
        {
            int jitter = Jitter(jitterSeed);
            PveRestThresholds baseline = Of(characterClass) switch
            {
                PveArchetype.PetCaster or PveArchetype.Caster or PveArchetype.Healer => new(60, 75, 0),
                PveArchetype.Melee => new(80, 0, 50),
                _ => new(80, 50, 50),
            };
            return new(Apply(baseline.Health, jitter), Apply(baseline.Power, jitter), Apply(baseline.Endurance, jitter));
        }

        public static int Jitter(long seed) =>
            (int)(Math.Abs(seed % (2 * RestJitterPercent + 1)) - RestJitterPercent);

        private static int Apply(int value, int jitter) => value <= 0 ? 0 : Math.Clamp(value + jitter, 1, 100);

        public static bool ReadyToPull(PveRestThresholds thresholds, int health, int power, int endurance, bool usesPower) =>
            health >= thresholds.Health && (!usesPower || power >= thresholds.Power) && endurance >= thresholds.Endurance;
    }

    public enum PvePullStyle { Single, MezGroup, MassPull }

    public readonly record struct PvePullPlan(PvePullStyle Style, bool Mezzer, int Bombers, int Pets, int MaxPull)
    {
        public string StyleLabel => Style switch
        {
            PvePullStyle.MezGroup => "mez_group",
            PvePullStyle.MassPull => "mass_pull",
            _ => "single",
        };
    }

    /// <summary>
    /// Pull size follows the group's control capacity (P13). A single-target
    /// mezzer makes a mez group: one mob at a time, adds mezzed and left
    /// alone. Two or more pet classes make a mass-pull group of
    /// min(6, 2 + pets). Everyone else pulls one at a time. Bombers are
    /// counted for the log only: autonomous PvE bots do not bomb or area
    /// stun yet, so that control must not size a pull.
    /// </summary>
    public static class AutonomousPvePullStyle
    {
        public const int MassPullCap = 6;
        public const int MassPullClusterRadius = 600;
        public const int BomberMinimumLevel = 20;

        /// <summary>Classes whose group job is single-target mez (Sorcerer, Mentalist, Bard).</summary>
        public static bool IsPureMezzer(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Sorcerer or eCharacterClass.Mentalist or eCharacterClass.Bard;

        /// <summary>PBAoE damage or an area stun (the Healer).</summary>
        public static bool IsBomber(eCharacterClass characterClass, int level) => level >= BomberMinimumLevel &&
            characterClass is eCharacterClass.Wizard or eCharacterClass.Eldritch or eCharacterClass.Enchanter or
                eCharacterClass.Spiritmaster or eCharacterClass.Healer;

        public static int MassPullSize(int pets) => Math.Min(MassPullCap, 2 + Math.Max(0, pets));

        public static PvePullPlan For(IEnumerable<(eCharacterClass Class, int Level)> members)
        {
            var list = members?.ToArray() ?? [];
            bool mezzer = list.Any(member => IsPureMezzer(member.Class));
            int bombers = list.Count(member => IsBomber(member.Class, member.Level));
            int pets = list.Count(member => AutonomousPveArchetype.IsPetClass(member.Class));
            if (mezzer)
                return new(PvePullStyle.MezGroup, true, bombers, pets, 1);
            if (pets >= 2)
                return new(PvePullStyle.MassPull, false, bombers, pets, MassPullSize(pets));
            return new(PvePullStyle.Single, false, bombers, pets, 1);
        }
    }

    public enum PveEnemyDecision { None, Shift, Hold, Clear, Leave }

    /// <summary>
    /// When a group or soloer gives up a camp (P12). Time is injected; one
    /// instance per camp stay. Not thread-safe; the camp's leader owns it.
    /// </summary>
    public sealed class AutonomousPveCampWatch
    {
        public const long ReturnMemoryMilliseconds = 15 * 60_000;
        public const int RivalRadius = 1500;
        public const int RivalMinimumGroupSize = 3;
        public const long RivalWindowMilliseconds = 3 * 60_000;
        /// <summary>A rival unseen this long no longer counts as still fighting.</summary>
        public const long RivalGapMilliseconds = 45_000;
        /// <summary>A rival counts only if it was already fighting within this long of our arrival.</summary>
        public const long RivalFirstMilliseconds = 30_000;
        public const int EnemyRadius = 2000;
        public const long EnemyLeaveMilliseconds = 90_000;
        public const long EnemyClearMilliseconds = 20_000;
        public const int MinimumShift = 300;
        public const int MaximumShift = 600;

        private long? _rivalSince;
        private long _rivalLastSeen;
        private bool _rivalWasFirst;
        private bool _enemyHold;
        private long _enemyLastSeen;
        private long _enemyPresentFor;

        public string CampId { get; }
        public int ArrivalAverageLevel { get; }
        public long StartedAt { get; }
        public bool EnemyHold => _enemyHold;

        public AutonomousPveCampWatch(string campId, int arrivalAverageLevel, long startedAt)
        {
            CampId = campId ?? string.Empty;
            ArrivalAverageLevel = arrivalAverageLevel;
            StartedAt = startedAt;
        }

        /// <summary>
        /// True once a rival group that was at the spawn first has kept
        /// fighting there for three minutes. A party that arrives after us is
        /// ignored: the later arrival yields, the first one stays.
        /// </summary>
        public bool ObserveRival(long now, bool rivalFighting)
        {
            if (rivalFighting && !_rivalWasFirst && now - StartedAt <= RivalFirstMilliseconds)
                _rivalWasFirst = true;
            if (!_rivalWasFirst)
                return false;
            if (!rivalFighting)
            {
                if (_rivalSince.HasValue && now - _rivalLastSeen > RivalGapMilliseconds)
                    _rivalSince = null;
                return false;
            }
            _rivalSince ??= now;
            _rivalLastSeen = now;
            return now - _rivalSince.Value >= RivalWindowMilliseconds;
        }

        /// <summary>
        /// Threatening enemy players near the group: move once as a body to
        /// a nearby spot, hold while they stay, resume 20 s after they left,
        /// and leave the camp once they have been present for 90 s (time
        /// they were away does not count). Never starts while fighting.
        /// </summary>
        public PveEnemyDecision ObserveEnemy(long now, bool enemyPresent, bool groupFighting)
        {
            if (enemyPresent)
            {
                if (!_enemyHold)
                {
                    if (groupFighting)
                        return PveEnemyDecision.None;
                    _enemyHold = true;
                    _enemyPresentFor = 0;
                    _enemyLastSeen = now;
                    return PveEnemyDecision.Shift;
                }
                if (now - _enemyLastSeen <= EnemyClearMilliseconds)
                    _enemyPresentFor += Math.Max(0, now - _enemyLastSeen);
                _enemyLastSeen = now;
                return _enemyPresentFor >= EnemyLeaveMilliseconds ? PveEnemyDecision.Leave : PveEnemyDecision.Hold;
            }
            if (!_enemyHold)
                return PveEnemyDecision.None;
            if (now - _enemyLastSeen >= EnemyClearMilliseconds)
            {
                _enemyHold = false;
                _enemyPresentFor = 0;
                return PveEnemyDecision.Clear;
            }
            return PveEnemyDecision.Hold;
        }

        /// <summary>
        /// The group has levelled out of the camp's band since it arrived:
        /// the camp is grey to its highest member, or (without a wipe
        /// penalty) green or lower to its average level.
        /// </summary>
        public static bool IsOutgrown(int campLevel, int arrivalAverageLevel, int averageLevel,
            int highestLevel, int wipePenalty)
        {
            if (campLevel <= 0 || averageLevel <= arrivalAverageLevel)
                return false;
            if (ConLevels.GetConColor(ConLevels.GetConLevel(highestLevel, campLevel)) <= ConColor.GREY)
                return true;
            return wipePenalty <= 0 &&
                   ConLevels.GetConColor(ConLevels.GetConLevel(averageLevel, campLevel)) <= ConColor.GREEN;
        }

        public static int ShiftDistance(long seed) =>
            MinimumShift + (int)(Math.Abs(seed) % (MaximumShift - MinimumShift + 1));
    }

    /// <summary>
    /// Ten-minute counter of solo rests and the resources a soloer had at the
    /// pull. One line per window, never per bot. Bug 72: the plain pull
    /// averages mix pulls that followed a rest with pulls that never needed
    /// one (the bot was already above its class threshold), so the line also
    /// reports the rested pulls separately, the resources at the moment the
    /// rest ended, and how long the bot took from waking to pulling.
    /// </summary>
    public sealed class AutonomousPveRestStats
    {
        public const long WindowMilliseconds = 600_000;

        /// <summary>A pull counts as "after a rest" when it follows the wake within this time.</summary>
        public const long RestedPullWindowMilliseconds = 60_000;

        private sealed class Bucket
        {
            public int Count;
            public int PowerCount;
            public long Power;
            public long Health;

            public void Add(int? powerPercent, int healthPercent)
            {
                Count++;
                if (powerPercent.HasValue)
                {
                    PowerCount++;
                    Power += Math.Clamp(powerPercent.Value, 0, 100);
                }
                Health += Math.Clamp(healthPercent, 0, 100);
            }

            /// <summary>Power is averaged over power users only.</summary>
            public string AveragePower(string empty) => PowerCount == 0 ? empty : (Power / PowerCount).ToString();

            public string AverageHealth(string empty) => Count == 0 ? empty : (Health / Count).ToString();
        }

        private readonly object _sync = new();
        private long _windowStart = -1;
        private int _rests;
        private int _routePulls;
        private long _restToPullMilliseconds;
        private Bucket _all = new();
        private Bucket _wake = new();
        private Bucket _rested = new();
        private Bucket _unrested = new();

        public void RecordRest(long now)
        {
            lock (_sync)
            {
                Start(now);
                _rests++;
            }
        }

        /// <summary>
        /// A resting soloer reached its class thresholds and got up. Power is
        /// averaged over power users only; pass null for pure melee.
        /// </summary>
        public void RecordWake(long now, int? powerPercent, int healthPercent)
        {
            lock (_sync)
            {
                Start(now);
                _wake.Add(powerPercent, healthPercent);
            }
        }

        /// <summary>
        /// Power is averaged over power users only; pass null for pure melee.
        /// <paramref name="restedForMilliseconds"/> is the time since the bot
        /// woke from a rest when this pull followed one within
        /// <see cref="RestedPullWindowMilliseconds"/>, otherwise null.
        /// <paramref name="routeThreat"/> marks a pull on the way (never at a
        /// camp, so never preceded by a camp rest).
        /// </summary>
        public void RecordPull(long now, int? powerPercent, int healthPercent,
            long? restedForMilliseconds = null, bool routeThreat = false)
        {
            lock (_sync)
            {
                Start(now);
                _all.Add(powerPercent, healthPercent);
                if (routeThreat)
                    _routePulls++;
                if (restedForMilliseconds.HasValue)
                {
                    _rested.Add(powerPercent, healthPercent);
                    _restToPullMilliseconds += Math.Max(0, restedForMilliseconds.Value);
                }
                else
                {
                    _unrested.Add(powerPercent, healthPercent);
                }
            }
        }

        /// <summary>Returns the report and starts a new window once ten minutes passed.</summary>
        public string TryReport(long now)
        {
            lock (_sync)
            {
                if (_windowStart < 0 || now - _windowStart < WindowMilliseconds)
                    return null;
                string restToPull = _rested.Count == 0 ? "-" : (_restToPullMilliseconds / _rested.Count / 1000).ToString();
                string line = $"PVE_REST window_s={WindowMilliseconds / 1000} rests={_rests} " +
                              $"avg_power_pct_at_pull={_all.AveragePower("0")} " +
                              $"avg_hp_pct_at_pull={_all.AverageHealth("0")} pulls={_all.Count} " +
                              $"rested_pulls={_rested.Count} rest_wakes={_wake.Count} " +
                              $"rest_power_at_wake={_wake.AveragePower("-")} rest_health_at_wake={_wake.AverageHealth("-")} " +
                              $"rested_power_at_pull={_rested.AveragePower("-")} rested_hp_at_pull={_rested.AverageHealth("-")} " +
                              $"rest_to_pull_s={restToPull} " +
                              $"unrested_power_at_pull={_unrested.AveragePower("-")} unrested_hp_at_pull={_unrested.AverageHealth("-")} " +
                              $"route_pulls={_routePulls}";
                _windowStart = now;
                _rests = 0;
                _routePulls = 0;
                _restToPullMilliseconds = 0;
                _all = new();
                _wake = new();
                _rested = new();
                _unrested = new();
                return line;
            }
        }

        private void Start(long now)
        {
            if (_windowStart < 0)
                _windowStart = now;
        }
    }
}
