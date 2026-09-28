using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.RealmAbilities;

namespace DOL.GS
{
    /// <summary>Class-legal realm ability purchases saved on a persistent companion.</summary>
    public static class CompanionRealmAbilityTraining
    {
        public static int RealmLevel(long realmPoints)
        {
            if (realmPoints <= 0)
                return 0;
            long[] thresholds = GamePlayer.REALMPOINTS_FOR_LEVEL;
            for (int level = thresholds.Length - 1; level > 0; level--)
                if (realmPoints >= thresholds[level])
                    return level;
            return 0;
        }

        public static int PointPool(int level, long realmPoints)
        {
            int realmLevel = RealmLevel(realmPoints);
            return level > 19 ? Math.Max(1, realmLevel) : realmLevel;
        }

        public static IReadOnlyList<RealmAbility> ClassAbilities(int classId) =>
            SkillBase.GetClassRealmAbilities(classId)
                .Where(ability => ability is not TimedRealmAbility && ability.MaxLevel > 0)
                .ToList();

        public static Dictionary<string, int> ReadAllocations(string serialized, IReadOnlyList<RealmAbility> abilities,
            int pointPool)
        {
            var allocations = new Dictionary<string, int>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(serialized))
                return allocations;
            int spent = 0;
            foreach (string entry in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = entry.Split('|');
                if (fields.Length != 2 || !int.TryParse(fields[1], out int rank))
                    continue;
                RealmAbility ability = abilities.FirstOrDefault(candidate => candidate.KeyName == fields[0]);
                if (ability == null || rank < 1 || rank > ability.MaxLevel || allocations.ContainsKey(fields[0]))
                    continue;
                int cost = Enumerable.Range(0, rank).Sum(ability.CostForUpgrade);
                if (cost < 0 || cost > pointPool - spent)
                    continue;
                allocations.Add(fields[0], rank);
                spent += cost;
            }
            return allocations;
        }

        public static int UnspentPoints(int pointPool, IReadOnlyDictionary<string, int> allocations,
            IReadOnlyList<RealmAbility> abilities)
        {
            int spent = abilities.Where(ability => allocations.TryGetValue(ability.KeyName, out _))
                .Sum(ability => Enumerable.Range(0, allocations[ability.KeyName]).Sum(ability.CostForUpgrade));
            return Math.Max(0, pointPool - spent);
        }

        public static string Serialize(IReadOnlyDictionary<string, int> allocations) =>
            string.Join(';', allocations.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}|{pair.Value}"));

        public static void Restore(GameBot companion)
        {
            PlayerCompanionRecord record = companion?.PlayerCompanionRecord;
            if (record == null)
                return;
            IReadOnlyList<RealmAbility> abilities = ClassAbilities(record.ClassId);
            Dictionary<string, int> allocations = ReadAllocations(record.SerializedRealmAbilities, abilities,
                PointPool(companion.Level, companion.CompanionRealmPoints));
            foreach (RealmAbility ability in abilities)
            {
                if (!allocations.TryGetValue(ability.KeyName, out int rank))
                    continue;
                ability.Level = rank;
                companion.AddAbility(ability, false);
            }
        }
    }
}
