namespace DOL.GS
{
    /// <summary>Muster by the normal RvR way: a joiner is ported to the landing keep of its group leader.</summary>
    public static class OpenBattlegroundMuster
    {
        /// <summary>Out and back in when the leader is in the BG and the member is not at the leader's landing; two bots outside the BG board by the porter as before.</summary>
        public static bool NeedsPortOut(ushort memberRegion, ushort leaderRegion, bool atLeaderLanding) =>
            leaderRegion == OpenBattlegroundMode.OpenBgRegion && !(memberRegion == leaderRegion && atLeaderLanding);

        /// <summary>One candidate pool of all realms for a unit: archetype preset units in Server PvP only.</summary>
        public static bool PoolsMerged(OpenBgMode mode, bool presetUnit) => mode == OpenBgMode.ServerPvp && presetUnit;

        /// <summary>
        /// A mate in the leader's region must be within porter reach as before; a mate of another region is accepted only
        /// from a merged pool, where the coordinator's remote meetup brings it to the leader's porter.
        /// </summary>
        public static bool AcceptsMember(bool merged, bool sameRegion, bool withinPorterReach) => sameRegion ? withinPorterReach : merged;

        public static (int X, int Y)? LandingFor(GameBot leader, System.Collections.Generic.IReadOnlyList<(ushort Region, int X, int Y, eRealm Realm)> landings)
        {
            if (leader == null || landings == null) return null;
            foreach ((ushort region, int x, int y, eRealm realm) in landings)
                if (region == leader.CurrentRegionID && realm == leader.Realm) return (x, y);
            return null;
        }
    }
}
