using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DOL.GS
{
    public static class BotLifetimeBuild
    {
        private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
        public static string Encode(BotSpec plan) => JsonSerializer.Serialize(plan, Options);

        /// <summary>
        /// Makes a persistent companion's combat profile follow the selected
        /// specialization plan. The plan's learned lines are authoritative;
        /// randomized constructor defaults must not keep choosing another
        /// weapon or pet-caster school after a build switch or reload.
        /// </summary>
        public static bool ConfigureForCompanionPlan(BotSpec destination, CompanionBuildPlan plan)
        {
            if (destination == null || plan == null || plan.TargetAllocations == null ||
                plan.TargetAllocations.Count == 0 || plan.TargetAllocations.Any(rank =>
                    string.IsNullOrWhiteSpace(rank.Specialization) || rank.Level is < 1 or > 50))
                return false;

            eCharacterClass characterClass = plan.CharacterClass;
            List<BotSpecLine> specLines = plan.TargetAllocations
                .Select(rank => new BotSpecLine(rank.Specialization, (uint)rank.Level, 1.0f))
                .ToList();
            if (specLines.Count == 0)
                return false;

            destination.SpecLines = specLines;

            eSpecType schoolProfile = ResolveSchoolProfile(characterClass, plan);
            if (schoolProfile != eSpecType.None && characterClass != eCharacterClass.Warden)
            {
                destination.SpecType = schoolProfile;
                bool oneHandSupportWeapon = characterClass is eCharacterClass.Healer or eCharacterClass.Shaman;
                destination.WeaponOneType = oneHandSupportWeapon ? eObjectType.Hammer : eObjectType.Staff;
                destination.WeaponTwoType = destination.WeaponOneType;
                destination.DamageType = oneHandSupportWeapon ? eWeaponDamageType.Crush : 0;
                destination.Is2H = !oneHandSupportWeapon;
                return true;
            }

            bool instrumentUser = characterClass is eCharacterClass.Bard or eCharacterClass.Minstrel;
            bool leftAxe = (characterClass is eCharacterClass.Berserker or eCharacterClass.Shadowblade) &&
                Rank(plan, Specs.Left_Axe) > 1;
            bool dualWield = Rank(plan, Specs.Dual_Wield) > 1 || Rank(plan, Specs.Celtic_Dual) > 1 ||
                Rank(plan, Specs.HandToHand) > 1;

            eObjectType primary = BestWeaponType(plan, TwoHand: false);
            eObjectType twoHanded = BestWeaponType(plan, TwoHand: true);

            if (primary == 0 && twoHanded != 0)
            {
                // Champions can train Large Weapons and Shields without
                // buying a Blades/Blunt/Piercing line. They still have a
                // class-legal one-handed weapon for the shield guard set;
                // leave the trained Large Weapons item in the two-hand slot.
                primary = characterClass == eCharacterClass.Champion && Rank(plan, Specs.Shields) > 1
                    ? eObjectType.Blades
                    : twoHanded;
            }
            if (primary != 0)
                destination.WeaponOneType = primary;

            if (characterClass == eCharacterClass.Hunter)
            {
                // The Hunter equipment path creates its spear in the two-hand
                // slot when SpecType is None. The generic TwoHanded profile
                // also attempts a one-hand copy of WeaponOneType.
                destination.WeaponTwoType = twoHanded != 0 ? twoHanded : primary;
                destination.SpecType = eSpecType.None;
                destination.Is2H = true;
                destination.DamageType = DamageTypeFor(destination.WeaponTwoType, plan);
                return true;
            }

            if (leftAxe)
            {
                destination.SpecType = eSpecType.LeftAxe;
                destination.WeaponTwoType = eObjectType.Axe;
                destination.Is2H = false;
            }
            else if (instrumentUser)
            {
                destination.SpecType = eSpecType.Instrument;
                destination.WeaponTwoType = 0;
                destination.Is2H = false;
            }
            else if (dualWield)
            {
                destination.SpecType = Rank(plan, Specs.Shields) > 1
                    ? eSpecType.DualWieldAndShield : eSpecType.DualWield;
                destination.WeaponTwoType = primary;
                destination.Is2H = false;
            }
            else if (twoHanded != 0)
            {
                destination.WeaponTwoType = twoHanded;
                destination.SpecType = primary != twoHanded
                    ? eSpecType.TwoHandHybrid : eSpecType.TwoHanded;
                destination.Is2H = true;
            }
            else if (RequiresClassTwoHander(characterClass, plan))
            {
                destination.WeaponTwoType = primary;
                destination.SpecType = eSpecType.Mid;
                destination.Is2H = true;
            }
            else if (HasTwoHandLoadout(characterClass))
            {
                destination.WeaponTwoType = primary;
                destination.SpecType = eSpecType.Mid;
                destination.Is2H = false;
            }
            else
            {
                destination.WeaponTwoType = 0;
                destination.SpecType = Rank(plan, Specs.Shields) > 1
                    ? eSpecType.OneHandAndShield : eSpecType.OneHanded;
                destination.Is2H = false;
            }

            if (characterClass == eCharacterClass.Warden)
                destination.SpecType = schoolProfile;

            if (primary != 0)
                destination.DamageType = DamageTypeFor(primary, plan);
            return true;
        }

        private static eSpecType ResolveSchoolProfile(eCharacterClass characterClass, CompanionBuildPlan plan)
        {
            string school = characterClass switch
            {
                eCharacterClass.Cabalist => HighestRanked(plan, "Body Magic", "Matter Magic", "Spirit Magic"),
                eCharacterClass.Cleric => HighestRanked(plan, "Rejuvenation", "Enhancement", "Smite"),
                eCharacterClass.Friar => HighestRanked(plan, Specs.Staff, "Rejuvenation", "Enhancement"),
                eCharacterClass.Sorcerer => HighestRanked(plan, "Body Magic", "Matter Magic", "Mind Magic"),
                eCharacterClass.Theurgist => HighestRanked(plan, "Wind Magic", "Earth Magic", "Cold Magic"),
                eCharacterClass.Wizard => HighestRanked(plan, "Fire Magic", "Cold Magic", "Earth Magic"),
                eCharacterClass.Necromancer => HighestRanked(plan, "Deathsight", "Painworking"),
                eCharacterClass.Bonedancer => HighestRanked(plan, "Darkness", "Suppression", "Bone Army"),
                eCharacterClass.Healer => HighestRanked(plan, "Pacification", "Mending", "Augmentation"),
                eCharacterClass.Runemaster => HighestRanked(plan, "Darkness", "Suppression", "Runecarving"),
                eCharacterClass.Shaman => HighestRanked(plan, "Mending", "Augmentation", "Subterranean"),
                eCharacterClass.Spiritmaster => HighestRanked(plan, "Darkness", "Suppression", "Summoning"),
                eCharacterClass.Druid => HighestRanked(plan, "Regrowth", "Nurture", "Nature"),
                eCharacterClass.Eldritch => HighestRanked(plan, "Light", "Mana", "Void"),
                eCharacterClass.Enchanter => HighestRanked(plan, "Light", "Mana", "Enchantments"),
                eCharacterClass.Mentalist => HighestRanked(plan, "Light", "Mana", "Mentalism"),
                eCharacterClass.Animist => HighestRanked(plan, "Arboreal Path", "Creeping Path", "Verdant Path"),
                eCharacterClass.Warden => Rank(plan, Specs.Blades) > 1
                    ? Specs.Blades : HighestRanked(plan, "Nurture", "Regrowth"),
                _ => string.Empty,
            };

            return (characterClass, school) switch
            {
                (eCharacterClass.Cabalist, "Body Magic") => eSpecType.BodyCab,
                (eCharacterClass.Cabalist, "Matter Magic") => eSpecType.MatterCab,
                (eCharacterClass.Cabalist, "Spirit Magic") => eSpecType.SpiritCab,
                (eCharacterClass.Cleric, "Rejuvenation") => eSpecType.RejuvCleric,
                (eCharacterClass.Cleric, "Enhancement") => eSpecType.EnhanceCleric,
                (eCharacterClass.Cleric, "Smite") => eSpecType.SmiteCleric,
                (eCharacterClass.Friar, Specs.Staff) => eSpecType.StaffFriar,
                (eCharacterClass.Friar, "Rejuvenation") => eSpecType.RejuvFriar,
                (eCharacterClass.Friar, "Enhancement") => eSpecType.EnhanceFriar,
                (eCharacterClass.Sorcerer, "Matter Magic") => eSpecType.MatterSorc,
                (eCharacterClass.Sorcerer, "Mind Magic") => eSpecType.MindSorc,
                (eCharacterClass.Sorcerer, "Body Magic") => eSpecType.BodySorc,
                (eCharacterClass.Theurgist, "Earth Magic") => eSpecType.EarthTheur,
                (eCharacterClass.Theurgist, "Cold Magic") => eSpecType.IceTheur,
                (eCharacterClass.Theurgist, "Wind Magic") => eSpecType.AirTheur,
                (eCharacterClass.Wizard, "Earth Magic") => eSpecType.EarthWiz,
                (eCharacterClass.Wizard, "Cold Magic") => eSpecType.IceWiz,
                (eCharacterClass.Wizard, "Fire Magic") => eSpecType.FireWiz,
                (eCharacterClass.Necromancer, "Painworking") => eSpecType.PainworkingNecro,
                (eCharacterClass.Necromancer, "Deathsight") => eSpecType.DeathsightNecro,
                (eCharacterClass.Bonedancer, "Darkness") => eSpecType.DarkBone,
                (eCharacterClass.Bonedancer, "Bone Army") => eSpecType.ArmyBone,
                (eCharacterClass.Bonedancer, "Suppression") => eSpecType.SuppBone,
                (eCharacterClass.Healer, "Mending") => eSpecType.MendHealer,
                (eCharacterClass.Healer, "Augmentation") => eSpecType.AugHealer,
                (eCharacterClass.Healer, "Pacification") => eSpecType.PacHealer,
                (eCharacterClass.Runemaster, "Darkness") => eSpecType.DarkRune,
                (eCharacterClass.Runemaster, "Suppression") => eSpecType.SuppRune,
                (eCharacterClass.Runemaster, "Runecarving") => eSpecType.RuneRune,
                (eCharacterClass.Shaman, "Mending") => eSpecType.MendShaman,
                (eCharacterClass.Shaman, "Augmentation") => eSpecType.AugShaman,
                (eCharacterClass.Shaman, "Subterranean") => eSpecType.SubtShaman,
                (eCharacterClass.Spiritmaster, "Darkness") => eSpecType.DarkSpirit,
                (eCharacterClass.Spiritmaster, "Suppression") => eSpecType.SuppSpirit,
                (eCharacterClass.Spiritmaster, "Summoning") => eSpecType.SummSpirit,
                (eCharacterClass.Druid, "Regrowth") => eSpecType.RegrowthDruid,
                (eCharacterClass.Druid, "Nurture") => eSpecType.NurtureDruid,
                (eCharacterClass.Druid, "Nature") => eSpecType.NatureDruid,
                (eCharacterClass.Eldritch, "Light") => eSpecType.LightEld,
                (eCharacterClass.Eldritch, "Mana") => eSpecType.ManaEld,
                (eCharacterClass.Eldritch, "Void") => eSpecType.VoidEld,
                (eCharacterClass.Enchanter, "Light") => eSpecType.LightEnchanter,
                (eCharacterClass.Enchanter, "Mana") => eSpecType.ManaEnchanter,
                (eCharacterClass.Enchanter, "Enchantments") => eSpecType.EnchantmentEnchanter,
                (eCharacterClass.Mentalist, "Light") => eSpecType.LightMenta,
                (eCharacterClass.Mentalist, "Mana") => eSpecType.ManaMenta,
                (eCharacterClass.Mentalist, "Mentalism") => eSpecType.MentaMenta,
                (eCharacterClass.Animist, "Arboreal Path") => eSpecType.ArborealAnimist,
                (eCharacterClass.Animist, "Creeping Path") => eSpecType.CreepingAnimist,
                (eCharacterClass.Animist, "Verdant Path") => eSpecType.VerdantAnimist,
                (eCharacterClass.Warden, "Nurture") => eSpecType.NurtureWarden,
                (eCharacterClass.Warden, "Regrowth") => eSpecType.RegrowthWarden,
                (eCharacterClass.Warden, Specs.Blades) => eSpecType.BattleWarden,
                _ => eSpecType.None,
            };
        }

        private static string HighestRanked(CompanionBuildPlan plan, params string[] specializations) =>
            plan.TargetAllocations
                .Select((rank, index) => (Rank: rank, Index: index))
                .Where(entry => specializations.Contains(entry.Rank.Specialization, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.Rank.Level)
                .ThenBy(entry => Array.FindIndex(specializations,
                    specialization => string.Equals(specialization, entry.Rank.Specialization, StringComparison.OrdinalIgnoreCase)))
                .ThenBy(entry => entry.Index)
                .Select(entry => entry.Rank.Specialization)
                .FirstOrDefault() ?? string.Empty;

        private static int Rank(CompanionBuildPlan plan, string specialization) =>
            plan.TargetAllocations.FirstOrDefault(rank =>
                string.Equals(rank.Specialization, specialization, StringComparison.OrdinalIgnoreCase))?.Level ?? 0;

        private static eObjectType BestWeaponType(CompanionBuildPlan plan, bool TwoHand)
        {
            foreach (CompanionBuildRank rank in plan.TargetAllocations.OrderByDescending(rank => rank.Level))
            {
                eObjectType? type = WeaponTypeFor(rank.Specialization, TwoHand);
                if (type.HasValue)
                    return type.Value;
            }
            return 0;
        }

        private static eObjectType? WeaponTypeFor(string specialization, bool TwoHand) => specialization switch
        {
            Specs.Slash when !TwoHand => eObjectType.SlashingWeapon,
            Specs.Crush when !TwoHand => eObjectType.CrushingWeapon,
            Specs.Thrust when !TwoHand => eObjectType.ThrustWeapon,
            Specs.Sword when !TwoHand => eObjectType.Sword,
            Specs.Axe when !TwoHand => eObjectType.Axe,
            Specs.Hammer when !TwoHand => eObjectType.Hammer,
            Specs.Blades when !TwoHand => eObjectType.Blades,
            Specs.Blunt when !TwoHand => eObjectType.Blunt,
            Specs.Piercing when !TwoHand => eObjectType.Piercing,
            Specs.Flexible when !TwoHand => eObjectType.Flexible,
            Specs.HandToHand when !TwoHand => eObjectType.HandToHand,
            Specs.Two_Handed when TwoHand => eObjectType.TwoHandedWeapon,
            Specs.Polearms when TwoHand => eObjectType.PolearmWeapon,
            Specs.Large_Weapons when TwoHand => eObjectType.LargeWeapons,
            Specs.Celtic_Spear when TwoHand => eObjectType.CelticSpear,
            Specs.Scythe when TwoHand => eObjectType.Scythe,
            Specs.Spear when TwoHand => eObjectType.Spear,
            Specs.Staff when TwoHand => eObjectType.Staff,
            _ => null,
        };

        private static bool RequiresClassTwoHander(eCharacterClass characterClass, CompanionBuildPlan plan) =>
            characterClass == eCharacterClass.Hunter ||
            characterClass == eCharacterClass.Savage && plan.Key == "hammer" ||
            characterClass == eCharacterClass.Shadowblade && plan.Key == "critblade" ||
            characterClass == eCharacterClass.Thane && plan.Key == "twohanded" ||
            characterClass == eCharacterClass.Friar || characterClass == eCharacterClass.Valewalker;

        private static bool HasTwoHandLoadout(eCharacterClass characterClass) =>
            characterClass is eCharacterClass.Healer or eCharacterClass.Shaman or eCharacterClass.Thane or
                eCharacterClass.Skald or eCharacterClass.Savage;

        private static eWeaponDamageType DamageTypeFor(eObjectType type, CompanionBuildPlan plan) => type switch
        {
            eObjectType.CrushingWeapon or eObjectType.Hammer or eObjectType.Blunt => eWeaponDamageType.Crush,
            eObjectType.ThrustWeapon or eObjectType.Piercing => eWeaponDamageType.Thrust,
            eObjectType.SlashingWeapon or eObjectType.Sword or eObjectType.Axe or eObjectType.Blades or
                eObjectType.LargeWeapons or eObjectType.Scythe => eWeaponDamageType.Slash,
            eObjectType.CelticSpear or eObjectType.Spear => eWeaponDamageType.Thrust,
            eObjectType.PolearmWeapon => HighestRanked(plan, Specs.Slash, Specs.Crush, Specs.Thrust) switch
            {
                Specs.Crush => eWeaponDamageType.Crush,
                Specs.Thrust => eWeaponDamageType.Thrust,
                _ => eWeaponDamageType.Slash,
            },
            _ => 0,
        };

        public static bool Restore(BotSpec destination, string saved)
        {
            if (string.IsNullOrWhiteSpace(saved)) return false;
            BotSpec plan;
            try { plan = JsonSerializer.Deserialize<BotSpec>(saved, Options); }
            catch (JsonException) { return false; }
            if (plan?.SpecLines == null || plan.SpecLines.Count == 0 || plan.SpecLines.Count > 20 ||
                !Enum.IsDefined(plan.SpecType) || !Enum.IsDefined(plan.WeaponOneType) ||
                !Enum.IsDefined(plan.WeaponTwoType) || plan.SpecLines.Any(line => string.IsNullOrWhiteSpace(line.Spec) ||
                    line.SpecCap > 50 || !float.IsFinite(line.levelRatio) || line.levelRatio < 0)) return false;
            destination.WeaponOneType = plan.WeaponOneType;
            destination.WeaponTwoType = plan.WeaponTwoType;
            destination.DamageType = plan.DamageType;
            destination.SpecType = plan.SpecType;
            destination.Is2H = plan.Is2H;
            destination.SpecLines = plan.SpecLines;
            return true;
        }

        /// <summary>
        /// Restores an explicitly saved lifetime build. Older records without
        /// a valid build plan inherit the weapon line their saved spec levels
        /// actually trained.
        /// </summary>
        public static bool RestoreOrAlignWithInvestedWeapons(BotSpec destination, string saved,
            Func<string, int> trained)
        {
            if (Restore(destination, saved))
                return true;

            AlignWithInvestedWeapons(destination, trained);
            return false;
        }

        public static void AlignWithInvestedWeapons(BotSpec plan, Func<string, int> trained)
        {
            eObjectType[] primary = { eObjectType.Sword, eObjectType.Axe, eObjectType.Hammer,
                eObjectType.SlashingWeapon, eObjectType.ThrustWeapon, eObjectType.CrushingWeapon,
                eObjectType.Blades, eObjectType.Blunt, eObjectType.Piercing, eObjectType.Spear,
                eObjectType.HandToHand, eObjectType.Flexible, eObjectType.Scythe, eObjectType.Staff };
            eObjectType[] secondary = { eObjectType.TwoHandedWeapon, eObjectType.PolearmWeapon,
                eObjectType.CelticSpear, eObjectType.LargeWeapons };
            plan.WeaponOneType = Align(plan, plan.WeaponOneType, primary, trained);
            if (secondary.Contains(plan.WeaponTwoType))
                plan.WeaponTwoType = Align(plan, plan.WeaponTwoType, secondary, trained);
        }

        private static eObjectType Align(BotSpec plan, eObjectType original, IEnumerable<eObjectType> choices, Func<string, int> trained)
        {
            if (original == 0) return original;
            eObjectType selected = original;
            int best = Math.Max(1, trained(SkillBase.ObjectTypeToSpec(original)));
            foreach (eObjectType type in choices)
            {
                int points = trained(SkillBase.ObjectTypeToSpec(type));
                if (points > best) { selected = type; best = points; }
            }
            if (selected == original) return original;
            string oldLine = SkillBase.ObjectTypeToSpec(original), newLine = SkillBase.ObjectTypeToSpec(selected);
            for (int i = 0; i < plan.SpecLines.Count; i++)
            {
                BotSpecLine line = plan.SpecLines[i];
                if (line.Spec == oldLine) plan.SpecLines[i] = new BotSpecLine(newLine, line.SpecCap, line.levelRatio);
            }
            return selected;
        }
    }
}
