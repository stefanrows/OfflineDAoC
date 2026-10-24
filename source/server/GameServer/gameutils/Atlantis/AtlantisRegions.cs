using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// The regions of the Trials of Atlantis world and the realm each copy belongs to.
    /// The three overworld copies (73 Albion, 30 Midgard, 130 Hibernia) and the dungeon copies are geometric
    /// clones: same zone offsets, and 98 % of the mobs of 73 stand on the same X, Y and Z in 30 and 130
    /// (dev snapshot after the data packs, docs/TOA_TRAVEL.md). The realm of each copy follows the zone points
    /// of pack 022: Albion 73/70/88/90, Midgard 30/71/45/47, Hibernia 130/72/145/147.
    /// </summary>
    public static class AtlantisRegions
    {
        /// <summary>True for every Atlantis region: overworld copies, the three ruins cities and the ToA dungeons.</summary>
        public static bool Contains(int regionId) => regionId switch
        {
            30 or 73 or 130 => true,                 // Atlantis (Oceanus, Stygia, Volcanus, Aerus zones)
            >= 70 and <= 72 => true,                 // Ruins of Atlantis, Ruinrar Atlantis, Scrios de Atlantis
            >= 35 and <= 40 => true,                 // Midgard: Cetus' Pit, Sobekite Eternal, Temple of Twilight, Halls of Ma'ati
            >= 78 and <= 83 => true,                 // Albion: the same dungeons
            >= 135 and <= 140 => true,               // Hibernia: the same dungeons
            >= 45 and <= 47 => true,                 // Midgard: Great Pyramid, Deep Volcanus, City of Aerus
            >= 88 and <= 90 => true,                 // Albion: the same
            >= 145 and <= 147 => true,               // Hibernia: the same
            91 => true,                              // Celestius (one region for all realms)
            _ => false,
        };

        /// <summary>The ruins city a realm's Atlantis travel starts from (region 70, 71 or 72), 0 for no realm.</summary>
        public static ushort HubOf(eRealm realm) => realm switch
        {
            eRealm.Albion => 70,
            eRealm.Midgard => 71,
            eRealm.Hibernia => 72,
            _ => 0,
        };

        /// <summary>The realm's copy of the Atlantis overworld (region 73, 30 or 130), 0 for no realm.</summary>
        public static ushort OverworldOf(eRealm realm) => realm switch
        {
            eRealm.Albion => 73,
            eRealm.Midgard => 30,
            eRealm.Hibernia => 130,
            _ => 0,
        };

        /// <summary>
        /// Random loot (ROG) from a kill in this region is a Trials of Atlantis item: server property
        /// <c>toa_loot_in_atlantis</c> on and the region inside Atlantis. Outside Atlantis nothing changes.
        /// </summary>
        public static bool UsesToaLoot(int regionId) => Properties.TOA_LOOT_IN_ATLANTIS && Contains(regionId);
    }
}
