using System;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// What a companion's kill reward is, the way 1.65 loot felt: mostly armor
    /// pieces and jewelry, now and then a weapon, and only weapons the class
    /// actually swings (no pile of staves on a warrior).
    /// </summary>
    public static class CompanionLootMix
    {
        public enum Kind { Armor, Jewelry, Weapon }

        public const double ArmorShare = 0.45;
        public const double JewelryShare = 0.35;
        public const int RerollAttempts = 12;

        public static Kind Roll(double roll) =>
            roll < ArmorShare ? Kind.Armor :
            roll < ArmorShare + JewelryShare ? Kind.Jewelry :
            Kind.Weapon;

        public static Kind KindOf(eObjectType type) =>
            type == eObjectType.Magical ? Kind.Jewelry :
            type is >= eObjectType._FirstArmor and <= eObjectType._LastArmor ? Kind.Armor :
            Kind.Weapon;

        public static GeneratedUniqueItem Generate(GameBot companion, byte level, bool frontierKill)
        {
            eRealm realm = companion.Realm;
            eCharacterClass characterClass = (eCharacterClass)companion.CharacterClass.ID;
            int MinUtility() => frontierKill ? level - Util.Random(-5, 10) : level - Util.Random(15, 20);

            Kind wanted = Roll(Util.RandomDouble());
            if (wanted == Kind.Jewelry)
                return new GeneratedUniqueItem(realm, characterClass, level, eObjectType.Magical, MinUtility());

            GeneratedUniqueItem last = null;
            for (int attempt = 0; attempt < RerollAttempts; attempt++)
            {
                last = new GeneratedUniqueItem(realm, characterClass, level, MinUtility());
                if (KindOf((eObjectType)last.Object_Type) == wanted && (wanted != Kind.Weapon || CanUse(companion, last)))
                    return last;
            }
            return last;
        }

        /// <summary>The companion could wear or swing this at all (not whether it is an upgrade).</summary>
        public static bool CanUse(GameBot companion, DbItemTemplate item)
        {
            if (companion == null || item == null)
                return false;
            eObjectType type = (eObjectType)item.Object_Type;
            if (companion.CharacterClass?.IsFocusCaster == true && type == eObjectType.Staff)
                return true;
            if (BotWeaponStats.IsMeleeWeapon(type) && !BotWeaponStats.IsConfiguredMeleeWeapon(companion, item))
                return false;
            return GameServer.ServerRules.CheckAbilityToUseItem(companion, item);
        }

        /// <summary>
        /// Worth for keeping, comparable across armor, jewelry and weapons:
        /// level, quality and bonuses, not raw DPS/AF (that ranked every
        /// weapon above every ring and sold the jewelry first).
        /// </summary>
        public static int KeepValue(DbInventoryItem item)
        {
            if (item == null)
                return 0;
            int utility = Math.Abs(item.Bonus) + Math.Abs(item.ExtraBonus) + Math.Abs(item.Bonus1) + Math.Abs(item.Bonus2) +
                          Math.Abs(item.Bonus3) + Math.Abs(item.Bonus4) + Math.Abs(item.Bonus5) + Math.Abs(item.Bonus6) +
                          Math.Abs(item.Bonus7) + Math.Abs(item.Bonus8) + Math.Abs(item.Bonus9) + Math.Abs(item.Bonus10);
            return Math.Max(1, item.Level) * 10 + Math.Max(1, item.Quality) + utility * 3;
        }
    }
}
