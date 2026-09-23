using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.Styles;

namespace DOL.GS
{
    /// <summary>
    /// Project-recommended automatic builds. Endgame targets are sourced from
    /// the companion research record; every intermediate level is derived by
    /// the documented, no-autotrain progression algorithm below.
    /// </summary>
    public static class CompanionBuildPlanCatalog
    {
        private static readonly IReadOnlyDictionary<eCharacterClass, CompanionBuildPlan> Plans =
            new Dictionary<eCharacterClass, CompanionBuildPlan>
            {
                [eCharacterClass.Armsman] = Create(eCharacterClass.Armsman, 20, "tank and group guard",
                    ("Polearm", 50), ("Shields", 42), ("Slash", 39), ("Parry", 5)),
                [eCharacterClass.Cabalist] = Create(eCharacterClass.Cabalist, 10, "pet caster and farmer",
                    ("Body Magic", 34), ("Spirit Magic", 33), ("Matter Magic", 25)),
                [eCharacterClass.Cleric] = Create(eCharacterClass.Cleric, 10, "healer and buffer",
                    ("Enhancement", 42), ("Rejuvenation", 33)),
                [eCharacterClass.Friar] = Create(eCharacterClass.Friar, 15, "melee support and healer",
                    ("Enhancement", 45), ("Staff", 29), ("Rejuvenation", 34), ("Parry", 17)),
                [eCharacterClass.Infiltrator] = Create(eCharacterClass.Infiltrator, 25, "assassin",
                    ("Thrust", 50), ("Critical Strike", 44), ("Stealth", 36), ("Envenom", 36), ("Dual Wield", 14)),
                [eCharacterClass.Mercenary] = Create(eCharacterClass.Mercenary, 20, "dual-wield melee damage",
                    ("Dual Wield", 50), ("Shields", 42), ("Slash", 36), ("Parry", 16)),
                [eCharacterClass.Minstrel] = Create(eCharacterClass.Minstrel, 15, "songs, charm, and group utility",
                    ("Instruments", 50), ("Thrust", 43)),
                [eCharacterClass.Paladin] = Create(eCharacterClass.Paladin, 20, "defensive tank and group support",
                    ("Chants", 46), ("Thrust", 44), ("Shields", 42)),
                [eCharacterClass.Reaver] = Create(eCharacterClass.Reaver, 20, "shield control and melee pressure",
                    ("Flexible", 50), ("Shields", 42), ("Soulrending", 39), ("Parry", 5)),
                [eCharacterClass.Scout] = Create(eCharacterClass.Scout, 20, "ranged and melee hybrid",
                    ("Longbows", 50), ("Shields", 42), ("Stealth", 35), ("Slash", 18)),
                [eCharacterClass.Sorcerer] = Create(eCharacterClass.Sorcerer, 10, "crowd control and ranged damage",
                    ("Body Magic", 39), ("Mind Magic", 37)),
                [eCharacterClass.Theurgist] = Create(eCharacterClass.Theurgist, 10, "pet interrupts and caster support",
                    ("Earth Magic", 40), ("Wind Magic", 36)),
                [eCharacterClass.Berserker] = Create(eCharacterClass.Berserker, 20, "dual-wield melee damage",
                    ("Sword", 50), ("Left Axe", 50), ("Parry", 28)),
                [eCharacterClass.Bonedancer] = Create(eCharacterClass.Bonedancer, 10, "pet caster with sustain",
                    ("Suppression", 47), ("Darkness", 26)),
                [eCharacterClass.Healer] = Create(eCharacterClass.Healer, 10, "main healer and crowd control",
                    ("Pacification", 38), ("Mending", 33), ("Augmentation", 19)),
                [eCharacterClass.Hunter] = Create(eCharacterClass.Hunter, 20, "pet and ranged/melee hybrid",
                    ("Beastcraft", 50), ("Spear", 44), ("Stealth", 36), ("Composite Bow", 9)),
                [eCharacterClass.Runemaster] = Create(eCharacterClass.Runemaster, 10, "ranged damage and spell utility",
                    ("Darkness", 47), ("Suppression", 26)),
                [eCharacterClass.Savage] = Create(eCharacterClass.Savage, 15, "melee damage with self-buffs",
                    ("Savagery", 49), ("Hand to Hand", 44)),
                [eCharacterClass.Shadowblade] = Create(eCharacterClass.Shadowblade, 22, "assassin",
                    ("Left Axe", 50), ("Sword", 35), ("Stealth", 36), ("Envenom", 36), ("Critical Strike", 5)),
                [eCharacterClass.Shaman] = Create(eCharacterClass.Shaman, 10, "buffer, healer, and ranged damage-over-time",
                    ("Augmentation", 46), ("Subterranean", 27), ("Mending", 8)),
                [eCharacterClass.Skald] = Create(eCharacterClass.Skald, 15, "group speed and melee support",
                    ("Battlesongs", 46), ("Hammer", 44), ("Parry", 17)),
                [eCharacterClass.Spiritmaster] = Create(eCharacterClass.Spiritmaster, 10, "pet caster and group damage",
                    ("Darkness", 47), ("Suppression", 26)),
                [eCharacterClass.Thane] = Create(eCharacterClass.Thane, 20, "hybrid tank and support damage",
                    ("Stormcalling", 46), ("Shields", 42), ("Hammer", 39), ("Parry", 20)),
                [eCharacterClass.Bard] = Create(eCharacterClass.Bard, 15, "group support and healer",
                    ("Nurture", 43), ("Music", 37), ("Regrowth", 33)),
                [eCharacterClass.Champion] = Create(eCharacterClass.Champion, 20, "hybrid tank, debuffs, and interrupts",
                    ("Valor", 50), ("Shields", 42), ("Large Weapons", 39)),
                [eCharacterClass.Druid] = Create(eCharacterClass.Druid, 10, "healer and buffer",
                    ("Nurture", 42), ("Regrowth", 33), ("Nature", 7)),
                [eCharacterClass.Eldritch] = Create(eCharacterClass.Eldritch, 10, "group caster damage and utility",
                    ("Mana", 50), ("Light", 20)),
                [eCharacterClass.Enchanter] = Create(eCharacterClass.Enchanter, 10, "pet support and caster damage",
                    ("Mana", 49), ("Light", 22)),
                [eCharacterClass.Mentalist] = Create(eCharacterClass.Mentalist, 10, "healer support and ranged damage",
                    ("Light", 46), ("Mentalism", 28), ("Mana", 4)),
                [eCharacterClass.Nightshade] = Create(eCharacterClass.Nightshade, 22, "assassin",
                    ("Critical Strike", 44), ("Piercing", 36), ("Stealth", 35), ("Envenom", 35), ("Celtic Dual", 25)),
                [eCharacterClass.Ranger] = Create(eCharacterClass.Ranger, 20, "ranged/melee hybrid with self-buffs",
                    ("Celtic Dual", 42), ("Pathfinding", 40), ("Piercing", 35), ("Stealth", 33), ("Recurve Bow", 11)),
                [eCharacterClass.Valewalker] = Create(eCharacterClass.Valewalker, 15, "melee and spell hybrid",
                    ("Scythe", 50), ("Arboreal Path", 38), ("Parry", 20)),
                [eCharacterClass.Warden] = Create(eCharacterClass.Warden, 15, "defensive melee and group support",
                    ("Nurture", 49), ("Regrowth", 33), ("Blades", 25), ("Parry", 14)),
            };

        private static readonly IReadOnlyDictionary<eCharacterClass, string> Blockers =
            new Dictionary<eCharacterClass, string>
            {
                [eCharacterClass.Animist] = "research has no numeric endgame allocation to seed a level schedule",
                [eCharacterClass.Wizard] = "research has no numeric endgame allocation to seed a level schedule",
                [eCharacterClass.Blademaster] = "the available dated forum template still needs its group role and ranked-skill tiers checked",
                [eCharacterClass.Hero] = "the available dated forum template still needs its group role and ranked-skill tiers checked",
                [eCharacterClass.Warrior] = "the available dated forum template still needs its group role and ranked-skill tiers checked",
                [eCharacterClass.Necromancer] = "the numeric farm template depends on Death Servant, which the existing companion combat profile does not support; no substitute profile was validated",
            };

        private static CompanionBuildPlan Create(eCharacterClass characterClass, int expectedMultiplier,
            string role, params (string Specialization, int Level)[] targets) =>
            new($"general-pve-v1-{characterClass.ToString().ToLowerInvariant()}", role,
                expectedMultiplier, targets.Select(target => new CompanionBuildRank(target.Specialization, target.Level)).ToArray());

        public static bool TryGetEnabledPlan(eCharacterClass characterClass, out string planId)
        {
            bool found = Plans.TryGetValue(characterClass, out CompanionBuildPlan plan);
            planId = found ? plan.Id : string.Empty;
            return found;
        }

        public static bool TryGetPlan(eCharacterClass characterClass, out CompanionBuildPlan plan) =>
            Plans.TryGetValue(characterClass, out plan);

        public static IReadOnlyCollection<CompanionBuildPlan> GetEnabledPlans() => Plans.Values.ToArray();

        public static bool TryValidateRuntimePlan(eCharacterClass characterClass, CompanionBuildPlan plan,
            out string blocker)
        {
            blocker = string.Empty;
            if (plan == null || !Plans.TryGetValue(characterClass, out CompanionBuildPlan enabled) ||
                !string.Equals(plan.Id, enabled.Id, StringComparison.Ordinal))
            {
                blocker = "the selected plan is not the enabled class plan";
                return false;
            }

            Dictionary<string, Specialization> career = SkillBase.GetSpecializationCareer((int)characterClass)
                .Keys.Where(spec => spec.Trainable)
                .ToDictionary(spec => spec.KeyName, StringComparer.OrdinalIgnoreCase);
            foreach (CompanionBuildRank target in plan.TargetAllocations)
            {
                if (!career.TryGetValue(target.Specialization, out Specialization specialization))
                {
                    blocker = $"runtime class data has no trainable {target.Specialization} specialization";
                    return false;
                }

                int classId = (int)characterClass;
                int abilityRank = SkillBase.GetSpecAbilityList(target.Specialization, classId)
                    .Where(ability => ability.SpecLevelRequirement <= target.Level)
                    .Select(ability => (int)ability.SpecLevelRequirement).DefaultIfEmpty(0).Max();
                List<Style> styles = SkillBase.GetStyleList(target.Specialization, classId);
                if (styles.Count == 0)
                    styles = SkillBase.GetStyleList(target.Specialization, 0);
                int styleRank = styles.Where(style => style.SpecLevelRequirement <= target.Level)
                    .Select(style => style.SpecLevelRequirement).DefaultIfEmpty(0).Max();
                int spellRank = SkillBase.GetSpecsSpellLines(target.Specialization)
                    .Where(entry => entry.Item2 == 0 || entry.Item2 == classId)
                    .SelectMany(entry => SkillBase.GetSpellList(entry.Item1.KeyName))
                    .Where(spell => spell.Level <= target.Level)
                    .Select(spell => spell.Level).DefaultIfEmpty(0).Max();
                int rankedData = Math.Max(abilityRank, Math.Max(styleRank, spellRank));

                if (rankedData == 0 && !IsCareerOnlyRankLine(characterClass, target.Specialization))
                {
                    blocker = $"runtime ability, spell, and style tables have no ranked entries for {target.Specialization}";
                    return false;
                }
            }

            return true;
        }

        private static bool IsCareerOnlyRankLine(eCharacterClass characterClass, string specialization) =>
            specialization is "Parry" or "Stealth" ||
            characterClass == eCharacterClass.Scout && specialization == "Longbows" ||
            characterClass == eCharacterClass.Hunter && specialization == "Composite Bow" ||
            characterClass == eCharacterClass.Ranger && specialization == "Recurve Bow";

        public static string GetBlocker(eCharacterClass characterClass)
        {
            if (Plans.TryGetValue(characterClass, out CompanionBuildPlan plan))
            {
                ICharacterClass runtimeClass = ScriptMgr.FindCharacterClass((int)characterClass);
                if (runtimeClass == null || runtimeClass.SpecPointsMultiplier != plan.ExpectedSpecPointsMultiplier)
                    return $"runtime class point multiplier does not match the validated {plan.ExpectedSpecPointsMultiplier}-point plan";
                if (!TryValidateRuntimePlan(characterClass, plan, out string runtimeBlocker))
                    return runtimeBlocker;
                return $"validated project recommendation {plan.Id}; role: {plan.Role}; level-50 targets: {plan.FormatTargets()}; per-level schedule derived without autotrain or automatic respec";
            }
            return Blockers.TryGetValue(characterClass, out string blocker)
                ? blocker
                : "this class has no validated automatic plan";
        }

        public static string GetBlocker(eCharacterClass characterClass, string savedPlanId)
        {
            if (!Plans.TryGetValue(characterClass, out CompanionBuildPlan plan))
                return GetBlocker(characterClass);
            if (!string.Equals(savedPlanId, plan.Id, StringComparison.Ordinal))
                return $"saved plan ID '{savedPlanId}' is missing or changed; allocations are preserved and earned points remain manual until a compatible plan is explicitly selected";
            return GetBlocker(characterClass);
        }
    }

    public sealed class CompanionBuildPlan
    {
        public string Id { get; }
        public string Role { get; }
        public int ExpectedSpecPointsMultiplier { get; }
        public IReadOnlyList<CompanionBuildRank> TargetAllocations { get; }

        internal CompanionBuildPlan(string id, string role, int expectedSpecPointsMultiplier,
            IReadOnlyList<CompanionBuildRank> targetAllocations)
        {
            Id = id;
            Role = role;
            ExpectedSpecPointsMultiplier = expectedSpecPointsMultiplier;
            TargetAllocations = targetAllocations;
        }

        /// <summary>
        /// Creates the full recommended allocation from level 1 through the
        /// requested level. Each earned point trains the eligible next rank
        /// with the least proportional progress toward its level-50 target.
        /// Candidate order breaks ties, and a rank is never bought early.
        /// </summary>
        public IReadOnlyDictionary<string, int> GetTargetsAtLevel(int targetLevel, int specPointsMultiplier)
        {
            int throughLevel = Math.Clamp(targetLevel, 1, 50);
            var levels = TargetAllocations.ToDictionary(rank => rank.Specialization, _ => 1,
                StringComparer.OrdinalIgnoreCase);
            int available = -1;

            for (int level = 1; level <= throughLevel; level++)
            {
                available += AwardPointsAtLevel(level, specPointsMultiplier);
                while (true)
                {
                    int selectedIndex = -1;
                    double leastProgress = double.MaxValue;
                    int selectedCost = 0;
                    for (int index = 0; index < TargetAllocations.Count; index++)
                    {
                        CompanionBuildRank target = TargetAllocations[index];
                        int current = levels[target.Specialization];
                        int levelTarget = Math.Min(target.Level, Math.Max(1, target.Level * level / 50));
                        int nextCost = current + 1;
                        if (current >= levelTarget || available < nextCost)
                            continue;

                        double progress = (double)(current - 1) / Math.Max(1, target.Level - 1);
                        if (progress < leastProgress)
                        {
                            selectedIndex = index;
                            selectedCost = nextCost;
                            leastProgress = progress;
                        }
                    }

                    if (selectedIndex < 0)
                        break;

                    string key = TargetAllocations[selectedIndex].Specialization;
                    levels[key]++;
                    available -= selectedCost;
                }
            }

            return levels;
        }

        public int GetRequiredPointsAtLevel(int targetLevel, int specPointsMultiplier)
        {
            IReadOnlyDictionary<string, int> targets = GetTargetsAtLevel(targetLevel, specPointsMultiplier);
            return TargetAllocations.Sum(rank => CostToReach(1, targets[rank.Specialization]));
        }

        public string FormatTargets() => string.Join(", ", TargetAllocations.Select(rank => $"{rank.Specialization} {rank.Level}"));

        public static int CostToReach(int currentLevel, int targetLevel)
        {
            int current = Math.Max(1, currentLevel);
            int target = Math.Max(current, targetLevel);
            return target * (target + 1) / 2 - current * (current + 1) / 2;
        }

        public static int AwardPointsAtLevel(int level, int specPointsMultiplier)
        {
            if (level <= 0)
                return 0;
            if (level <= 5)
                return level;

            int points = specPointsMultiplier * level / 10;
            if (level > 40)
                points += specPointsMultiplier * (level - 1) / 20;
            return points;
        }
    }

    public sealed record CompanionBuildRank(string Specialization, int Level);
}
