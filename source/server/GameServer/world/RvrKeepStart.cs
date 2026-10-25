#nullable enable
using System.Collections.Generic;

namespace OfflineDaoc.Configuration;

/// <summary>
/// The start of the RvR world's (<c>Realm-1</c>) 21 frontier keeps, as the upstream release world ships them
/// (shadowofze/OfflineDAoC <c>source/tools/release-world-defaults.json</c>, "keep owners and levels as in the
/// original v0.32 world"; upstream 0.36 notes: "Every keep, relic and battleground keep starts with its original
/// owner and level"). Each keep stands for its original realm at this level; portal keeps, relic keeps (level 10)
/// and battleground keeps keep their own start. Shared by the server (one-time move of an older save's Frontier
/// Wardens keeps, <c>PvpKeepCampaign</c>) and the launcher (fresh RvR world, keep reset). Port session S1,
/// docs/RVR_PORT_MAP.md.
/// </summary>
public static class RvrKeepStart
{
    private static readonly Dictionary<int, byte> Levels = new()
    {
        // Albion: Caer Benowyc, Berkstead, Erasleigh, Boldiam, Sursbrooke, Hurbury, Renaris
        [50] = 5, [51] = 5, [52] = 5, [53] = 5, [54] = 5, [55] = 5, [56] = 5,
        // Midgard: Bledmeer, Nottmoor, Hlidskialf, Blendrake, Glenlock, Fensalir, Arvakr Faste
        [75] = 5, [76] = 5, [77] = 5, [78] = 5, [79] = 5, [80] = 5, [81] = 5,
        // Hibernia: Dun Crauchon, Crimthain, Bolg, nGed, Da Behnn, Scathaig, Ailinne
        [100] = 4, [101] = 5, [102] = 5, [103] = 1, [104] = 1, [105] = 5, [106] = 1,
    };

    /// <summary>The 21 frontier keep ids with their upstream start level.</summary>
    public static IReadOnlyDictionary<int, byte> FrontierKeepLevels => Levels;

    /// <summary>The upstream start level of a frontier keep, or null for any other keep.</summary>
    public static byte? StartLevel(int keepId) => Levels.TryGetValue(keepId, out byte level) ? level : null;
}
