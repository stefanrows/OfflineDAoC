using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.GS.RealmAbilities;

namespace DOL.GS
{
    /// <summary>
    /// Purchases and uses the small Atlas active set for autonomous world bots.
    /// Cooldowns are stored in the bot's existing SerializedAbilities string.
    /// </summary>
    public static class AutonomousRealmAbilityActives
    {
        public const string FirstAid = "AtlasOF_FirstAid";
        public const string IgnorePain = "AtlasOF_IgnorePain";
        public const string IgnorePainTank = "AtlasOF_IgnorePainTank";
        public const string Purge = "AtlasOF_Purge";
        public const string PurgeReduced = "AtlasOF_PurgeReduced";
        public const string SecondWind = "AtlasOF_SecondWind";
        public const string AugmentedConstitution = "AtlasOF_AugCon";

        private const string CooldownPrefix = "ra-cooldown";
        private const int CriticalHealthPercent = 30;
        private const int CriticalEndurancePercent = 30;
        private const int RecentCombatMilliseconds = 10_000;

        private static readonly HashSet<string> CooldownKeys = new(StringComparer.Ordinal)
        {
            FirstAid, IgnorePain, IgnorePainTank, Purge, PurgeReduced, SecondWind
        };

        private static readonly ConditionalWeakTable<GameBot, State> States = new();

        private sealed class State
        {
            public readonly object Gate = new();
            public readonly Dictionary<string, long> Deadlines = new(StringComparer.Ordinal);
            public readonly List<string> OriginalCooldownTokens = [];
            public readonly List<string> OtherTokens = [];
            public bool AllocationsValid;
            public bool CooldownDataValid = true;
            public long NextCheckTick;
        }

        public static IReadOnlyList<string> PurchaseKeys(IReadOnlyCollection<RealmAbility> classAbilities)
        {
            Dictionary<string, RealmAbility> available = classAbilities
                .GroupBy(ability => ability.KeyName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            List<string> keys = [];

            string purge = SelectVariantKey(classAbilities, Purge, PurgeReduced,
                typeof(AtlasOF_PurgeAbilityReduced));
            string ignorePain = SelectVariantKey(classAbilities, IgnorePain, IgnorePainTank,
                typeof(AtlasOF_IgnorePainTank));

            if (purge != null)
                keys.Add(purge);
            if (available.ContainsKey(FirstAid))
                keys.Add(FirstAid);
            if (ignorePain != null)
                keys.Add(ignorePain);
            if (available.ContainsKey(SecondWind))
                keys.Add(SecondWind);

            if (ignorePain != null && available.ContainsKey(FirstAid))
                keys.Add(FirstAid);
            if (available.ContainsKey(SecondWind) && available.ContainsKey(AugmentedConstitution))
                keys.Add(AugmentedConstitution);
            return keys.Distinct(StringComparer.Ordinal).ToArray();
        }

        private static string SelectVariantKey(IEnumerable<RealmAbility> abilities,
            string standardKey, string variantKey, Type variantType) =>
            abilities.FirstOrDefault(ability => variantType.IsInstanceOfType(ability))?.KeyName ??
            abilities.FirstOrDefault(ability => ability.KeyName == variantKey)?.KeyName ??
            abilities.FirstOrDefault(ability => ability.KeyName == standardKey)?.KeyName;

        public static bool HasValidSavedPrerequisites(IReadOnlyDictionary<string, int> allocations)
        {
            bool hasIgnorePain = allocations.ContainsKey(IgnorePainTank) || allocations.ContainsKey(IgnorePain);
            if (hasIgnorePain &&
                (!allocations.TryGetValue(FirstAid, out int firstAidRank) || firstAidRank < 2))
                return false;

            return !allocations.ContainsKey(SecondWind) ||
                allocations.TryGetValue(AugmentedConstitution, out int constitutionRank) && constitutionRank >= 3;
        }

        public static bool AdvanceInitialPurchases(GameBot bot,
            IReadOnlyDictionary<string, RealmAbility> catalog, IDictionary<string, int> allocations,
            ref int remaining, out bool changed)
        {
            changed = false;
            string purge = SelectVariantKey(catalog.Values, Purge, PurgeReduced,
                typeof(AtlasOF_PurgeAbilityReduced));
            if (purge != null && !TryRaise(bot, purge, 1, catalog, allocations, ref remaining, ref changed))
                return false;

            string ignorePain = SelectVariantKey(catalog.Values, IgnorePain, IgnorePainTank,
                typeof(AtlasOF_IgnorePainTank));
            int firstAidTarget = ignorePain != null ? 2 : 1;
            if (catalog.ContainsKey(FirstAid) &&
                !TryRaise(bot, FirstAid, firstAidTarget, catalog, allocations, ref remaining, ref changed))
                return false;

            if (ignorePain != null)
            {
                if (!catalog.TryGetValue(FirstAid, out RealmAbility firstAid) || firstAid.MaxLevel < 2)
                    ignorePain = null;
                else if (!TryRaise(bot, FirstAid, 2, catalog, allocations, ref remaining, ref changed))
                    return false;
            }
            if (ignorePain != null &&
                !TryRaise(bot, ignorePain, 1, catalog, allocations, ref remaining, ref changed))
                return false;

            if (catalog.ContainsKey(SecondWind))
            {
                if (catalog.TryGetValue(AugmentedConstitution, out RealmAbility augmentedConstitution) &&
                    augmentedConstitution.MaxLevel >= 3)
                {
                    if (!TryRaise(bot, AugmentedConstitution, 3, catalog, allocations, ref remaining, ref changed))
                        return false;
                    if (!TryRaise(bot, SecondWind, 1, catalog, allocations, ref remaining, ref changed))
                        return false;
                }
            }

            return true;
        }

        public static bool AdvanceRemainingFirstAid(GameBot bot,
            IReadOnlyDictionary<string, RealmAbility> catalog, IDictionary<string, int> allocations,
            ref int remaining, out bool changed)
        {
            changed = false;
            if (!catalog.TryGetValue(FirstAid, out RealmAbility firstAid))
                return true;
            return TryRaise(bot, FirstAid, firstAid.MaxLevel, catalog, allocations, ref remaining, ref changed);
        }

        private static bool TryRaise(GameBot bot, string key, int target,
            IReadOnlyDictionary<string, RealmAbility> catalog, IDictionary<string, int> allocations,
            ref int remaining, ref bool changed)
        {
            if (!catalog.TryGetValue(key, out RealmAbility ability))
                return true;

            target = Math.Min(target, ability.MaxLevel);
            int rank = allocations.TryGetValue(key, out int current) ? current : 0;
            while (rank < target)
            {
                int cost = ability.CostForUpgrade(rank);
                if (cost < 0 || cost > remaining)
                    return false;
                ability.Level = ++rank;
                bot.AddAbility(ability, false);
                allocations[key] = rank;
                remaining -= cost;
                changed = true;
            }
            return true;
        }

        public static bool IsEligible(bool autonomousWorldBot, bool temporaryHelper,
            bool playerLedGroup, bool onStableMasterRoute) =>
            autonomousWorldBot && !temporaryHelper && !playerLedGroup && !onStableMasterRoute;

        public static bool ShouldUse(string key, bool alive, bool sitting, bool mezzedOrStunned,
            bool inCombat, bool recentlyInCombat, int healthPercent, int endurancePercent)
        {
            if (!alive || sitting)
                return false;

            return key switch
            {
                Purge or PurgeReduced => (mezzedOrStunned &&
                    (inCombat || recentlyInCombat || healthPercent <= 60)),
                IgnorePain or IgnorePainTank => inCombat && !mezzedOrStunned &&
                    healthPercent <= CriticalHealthPercent,
                SecondWind => inCombat && !mezzedOrStunned &&
                    endurancePercent <= CriticalEndurancePercent,
                FirstAid => !inCombat && !mezzedOrStunned && healthPercent <= CriticalHealthPercent,
                _ => false,
            };
        }

        public static void Restore(GameBot bot, string serialized, bool allocationsValid)
        {
            if (bot == null)
                return;

            State state = States.GetOrCreateValue(bot);
            lock (state.Gate)
            {
                state.Deadlines.Clear();
                state.OriginalCooldownTokens.Clear();
                state.OtherTokens.Clear();
                state.AllocationsValid = allocationsValid;
                state.CooldownDataValid = true;
                state.NextCheckTick = 0;

                HashSet<string> seen = new(StringComparer.Ordinal);
                foreach (string token in (serialized ?? string.Empty)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.StartsWith(CooldownPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        state.OriginalCooldownTokens.Add(token);
                        string[] fields = token.Split('|');
                        if (fields.Length != 3 || fields[0] != CooldownPrefix ||
                            !CooldownKeys.Contains(fields[1]) || !seen.Add(fields[1]) ||
                            !long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) ||
                            ticks <= 0 || ticks > DateTime.MaxValue.Ticks)
                        {
                            state.CooldownDataValid = false;
                            continue;
                        }
                        state.Deadlines.Add(fields[1], ticks);
                    }
                    else if (!token.StartsWith("ra|", StringComparison.Ordinal) &&
                             !token.StartsWith("trained-level|", StringComparison.Ordinal) &&
                             !token.StartsWith("generated-level|", StringComparison.Ordinal))
                    {
                        state.OtherTokens.Add(token);
                    }
                }
            }
        }

        public static string SerializeCooldownTokens(GameBot bot)
        {
            if (bot == null)
                return string.Empty;

            State state = States.GetOrCreateValue(bot);
            lock (state.Gate)
            {
                if (!state.CooldownDataValid)
                    return string.Join(";", state.OriginalCooldownTokens.Concat(state.OtherTokens));

                IEnumerable<string> cooldowns = state.Deadlines.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => CooldownPrefix + "|" + pair.Key + "|" +
                        pair.Value.ToString(CultureInfo.InvariantCulture));
                return string.Join(";", cooldowns.Concat(state.OtherTokens));
            }
        }

        public static bool TryReadCooldownTokens(string serialized, out Dictionary<string, long> deadlines,
            out string[] preservedTokens, out bool valid)
        {
            deadlines = new Dictionary<string, long>(StringComparer.Ordinal);
            List<string> preserved = [];
            valid = true;
            foreach (string token in (serialized ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!token.StartsWith(CooldownPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                preserved.Add(token);
                string[] fields = token.Split('|');
                if (fields.Length != 3 || fields[0] != CooldownPrefix ||
                    !CooldownKeys.Contains(fields[1]) ||
                    !long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) ||
                    ticks <= 0 || ticks > DateTime.MaxValue.Ticks || deadlines.ContainsKey(fields[1]))
                {
                    valid = false;
                    continue;
                }
                deadlines.Add(fields[1], ticks);
            }
            preservedTokens = preserved.ToArray();
            return valid;
        }

        internal static bool TrySetCooldown(GameBot bot, string key, DateTime deadline)
        {
            if (bot == null || !CooldownKeys.Contains(key) || deadline.Kind != DateTimeKind.Utc)
                return false;

            State state = States.GetOrCreateValue(bot);
            lock (state.Gate)
            {
                if (!state.AllocationsValid || !state.CooldownDataValid)
                    return false;
                state.Deadlines[key] = deadline.Ticks;
                return true;
            }
        }

        public static void UseActives(GameBot bot)
        {
            if (bot == null || !IsEligible(bot.IsAutonomousWorldBot, bot.IsTemporaryGroupHelper,
                    bot.IsPlayerLedGroup, bot.IsOnStableMasterRoute))
                return;

            State state = States.GetOrCreateValue(bot);
            long nowTick = GameLoop.GameLoopTime;
            lock (state.Gate)
            {
                if (!state.AllocationsValid || !state.CooldownDataValid || nowTick < state.NextCheckTick)
                    return;
                state.NextCheckTick = nowTick + 1_000;
            }

            bool recentlyInCombat = bot.InCombatInLast(RecentCombatMilliseconds);
            string[] useOrder =
            [
                PurgeReduced, Purge, IgnorePainTank, IgnorePain, SecondWind, FirstAid
            ];
            DateTime gameplayNow = WorldSimulationClock.UtcNow;
            foreach (string key in useOrder)
            {
                if (bot.GetAbility(key) is not TimedRealmAbility ability ||
                    bot.GetSkillDisabledDuration(ability) > 0 ||
                    IsCoolingDown(state, key, gameplayNow) ||
                    !ShouldUse(key, bot.IsAlive, bot.IsSitting, bot.IsMezzed || bot.IsStunned,
                        bot.InCombat, recentlyInCombat, bot.HealthPercent, bot.EndurancePercent))
                    continue;

                int reuseSeconds = ability.GetReUseDelay(ability.Level);
                if (reuseSeconds <= 0)
                    return;
                DateTime deadline;
                try
                {
                    deadline = gameplayNow.AddSeconds(reuseSeconds);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return;
                }

                if (!bot.TryPersistAutonomousRealmAbilityCooldown(key, deadline))
                    return;

                ability.Execute(bot);
                return;
            }
        }

        private static bool IsCoolingDown(State state, string key, DateTime now)
        {
            lock (state.Gate)
                return state.Deadlines.TryGetValue(key, out long ticks) &&
                    new DateTime(ticks, DateTimeKind.Utc) > now;
        }
    }
}
