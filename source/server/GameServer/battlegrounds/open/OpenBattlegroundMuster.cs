namespace DOL.GS
{
    /// <summary>
    /// Pure rules of the mixed-realm muster: preset units in Server PvP draw members from all realms and the
    /// coordinator's remote meetup brings the members of other regions to the leader's porter.
    /// </summary>
    public static class OpenBattlegroundMuster
    {
        /// <summary>One candidate pool of all realms for a unit: archetype preset units in Server PvP only.</summary>
        public static bool PoolsMerged(OpenBgMode mode, bool presetUnit) => mode == OpenBgMode.ServerPvp && presetUnit;

        /// <summary>
        /// A mate in the leader's region must be within porter reach as before; a mate of another region is accepted only
        /// from a merged pool, where the coordinator's remote meetup brings it to the leader's porter.
        /// </summary>
        public static bool AcceptsMember(bool merged, bool sameRegion, bool withinPorterReach) => sameRegion ? withinPorterReach : merged;

        /// <summary>The leader's region test: another region is accepted only when the coordinator's remote meetup brings the member.</summary>
        public static bool RegionAccepted(bool allowRemoteMembers, ushort memberRegion, ushort leaderRegion) =>
            allowRemoteMembers || memberRegion == leaderRegion;

        /// <summary>A merged unit is registered under its admit's realm and lands at its leader's keep, so its leader is of that realm.</summary>
        public static bool SeedAllowed(bool merged, eRealm seedRealm, eRealm admitRealm) => !merged || seedRealm == admitRealm;
    }
}
