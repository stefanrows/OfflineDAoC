namespace DOL.GS
{
    /// <summary>
    /// Keep ownership text shared by /ck and /battleground list. Legacy battlegrounds keep their
    /// original /ck line; campaign battlegrounds also mark a defeated, unclaimed lord.
    /// </summary>
    public static class BattlegroundKeepOwnership
    {
        public const string LordDefeatedUnclaimed = "lord defeated, unclaimed";

        /// <summary>RvR world (R3a): the saved LordDefeated flag means a realm captured the keep and its lord
        /// stands again; the keep waits for a claim by a guild of that realm.</summary>
        public const string CapturedAwaitingClaim = "captured, awaiting claim";

        public static string AwaitingClaimText(WorldModelKind world) =>
            world == WorldModelKind.Realm ? CapturedAwaitingClaim : LordDefeatedUnclaimed;

        public static string Owner(string guildName, bool lordDefeated) =>
            Owner(WorldMode.Current, guildName, lordDefeated);

        public static string Owner(WorldModelKind world, string guildName, bool lordDefeated) =>
            guildName ?? (lordDefeated ? AwaitingClaimText(world) : "unclaimed");

        public static string Describe(string keepName, eRealm realm, string guildName, bool lordDefeated, bool campaign) =>
            Describe(WorldMode.Current, keepName, realm, guildName, lordDefeated, campaign);

        public static string Describe(WorldModelKind world, string keepName, eRealm realm, string guildName, bool lordDefeated, bool campaign)
        {
            string text = keepName + ": " + GlobalConstants.RealmToName(realm);
            if (guildName != null)
                text += " (" + guildName + ")";
            else if (campaign && lordDefeated)
                text += " - " + AwaitingClaimText(world);
            return text;
        }

        public static string CentralLine(string keepName, string guildName, bool lordDefeated) =>
            "Owner of " + keepName + ": " + Owner(guildName, lordDefeated) + ".";
    }
}
