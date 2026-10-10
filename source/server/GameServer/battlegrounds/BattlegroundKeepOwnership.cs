namespace DOL.GS
{
    /// <summary>
    /// Keep ownership text shared by /ck and /battleground list. Legacy battlegrounds keep their
    /// original /ck line; campaign battlegrounds also mark a defeated, unclaimed lord.
    /// </summary>
    public static class BattlegroundKeepOwnership
    {
        public const string LordDefeatedUnclaimed = "lord defeated, unclaimed";

        public static string Owner(string guildName, bool lordDefeated) =>
            guildName ?? (lordDefeated ? LordDefeatedUnclaimed : "unclaimed");

        public static string Describe(string keepName, eRealm realm, string guildName, bool lordDefeated, bool campaign)
        {
            string text = keepName + ": " + GlobalConstants.RealmToName(realm);
            if (guildName != null)
                text += " (" + guildName + ")";
            else if (campaign && lordDefeated)
                text += " - " + LordDefeatedUnclaimed;
            return text;
        }

        public static string CentralLine(string keepName, string guildName, bool lordDefeated) =>
            "Owner of " + keepName + ": " + Owner(guildName, lordDefeated) + ".";
    }
}
