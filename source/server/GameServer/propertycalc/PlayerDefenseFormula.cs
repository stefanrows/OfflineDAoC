namespace DOL.GS.PropertyCalc
{
    /// <summary>
    /// Shared player block, parry and evade formulas for players and GameBots.
    /// Ordinary NPCs keep their template defense chances.
    /// </summary>
    public static class PlayerDefenseFormula
    {
        public static bool UsesPlayerDefense(GameObject living) => living is GamePlayer or GameBot;

        public static int Parry(int dexterity, bool hasParrySpec, int modifiedParrySpec) =>
            hasParrySpec ? (dexterity * 2 - 100) / 4 + (modifiedParrySpec - 1) * 5 + 50 : 0;

        public static int Block(int dexterity, int modifiedShieldSpec) =>
            (dexterity * 2 - 100) / 4 + (modifiedShieldSpec - 1) * 5 + 50;

        public static int Evade(int quickness, int dexterity, int evadeAbilityLevel) =>
            evadeAbilityLevel > 0 ? (900 + quickness + dexterity) * evadeAbilityLevel * 5 / 100 : 0;
    }
}
