using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>One keep component, using the same raw values as the /keep fastcreate layouts.</summary>
    public sealed record ComponentSpec(int Id, int Skin, int X, int Y, int Heading);

    /// <summary>
    /// A battleground keep. Existing rows keep their stored coordinates; new rows use a site
    /// coordinate found by tools/dev/build-battleground-nav.sh --bg-keep-sites.
    /// </summary>
    public sealed record KeepSite(ushort Region, int KeepId, string Name, int X, int Y, int Z, int Heading, string Template, bool ExistingRow);

    /// <summary>A portal keep whose client zone data already has a native frontier keep fixture beside it.</summary>
    public sealed record NativePortalKeep(ushort Region, int KeepId);

    /// <summary>
    /// Server-built battleground keeps. Component tuples are copied verbatim from the
    /// /keep fastcreate bracket layouts in commands/gmcommands/keep.cs (height 0). Offsets are
    /// raw DbKeepComponent values; the keep loader reads them as signed bytes, so 253 is -3.
    /// tools/dev/battleground-keeps.json carries the same data for the navigation builder.
    /// </summary>
    public static class BattlegroundKeepLayouts
    {
        public const string OfflineKeepPrefix = "offline-bg-keep:";
        public const string PendingSuffix = ":pending";
        public const int ComponentSpacing = 148;
        public const int FootprintPadding = 200;
        public const int TowerSkin = 11;

        private static readonly IReadOnlyDictionary<string, ComponentSpec[]> Templates = new Dictionary<string, ComponentSpec[]>(StringComparer.Ordinal)
        {
            ["TBG40_44"] = new[]
            {
                new ComponentSpec(0, 11, 253, 4, 0),
            },
            ["ClaimBG5_9"] = new[]
            {
                new ComponentSpec(0, 5, 5, 249, 0),
                new ComponentSpec(1, 5, 251, 249, 1),
                new ComponentSpec(2, 7, 251, 255, 1),
                new ComponentSpec(3, 9, 250, 252, 1),
                new ComponentSpec(4, 9, 6, 250, 3),
                new ComponentSpec(5, 7, 5, 253, 3),
                new ComponentSpec(6, 9, 6, 0, 3),
                new ComponentSpec(7, 5, 5, 3, 3),
                new ComponentSpec(8, 9, 4, 4, 2),
                new ComponentSpec(9, 1, 252, 249, 0),
                new ComponentSpec(10, 2, 2, 249, 0),
                new ComponentSpec(11, 9, 1, 4, 2),
                new ComponentSpec(12, 9, 254, 4, 2),
                new ComponentSpec(13, 5, 251, 3, 2),
                new ComponentSpec(14, 9, 250, 2, 1),
                new ComponentSpec(15, 19, 255, 249, 0),
            },
            ["CaerClaret"] = new[]
            {
                new ComponentSpec(0, 0, 254, 252, 0),
                new ComponentSpec(1, 4, 4, 249, 3),
                new ComponentSpec(2, 4, 250, 252, 0),
                new ComponentSpec(3, 9, 252, 255, 1),
                new ComponentSpec(4, 9, 252, 2, 1),
                new ComponentSpec(5, 9, 5, 253, 3),
                new ComponentSpec(6, 9, 5, 0, 3),
                new ComponentSpec(7, 4, 253, 6, 1),
                new ComponentSpec(8, 9, 0, 4, 2),
                new ComponentSpec(9, 4, 7, 3, 2),
                new ComponentSpec(10, 9, 3, 4, 2),
            },
            ["CKBG15_19"] = new[]
            {
                new ComponentSpec(0, 4, 247, 250, 0),
                new ComponentSpec(1, 4, 7, 247, 3),
                new ComponentSpec(2, 4, 250, 10, 1),
                new ComponentSpec(3, 4, 10, 7, 2),
                new ComponentSpec(4, 0, 254, 251, 0),
                new ComponentSpec(5, 2, 4, 250, 0),
                new ComponentSpec(6, 1, 251, 250, 0),
                new ComponentSpec(7, 1, 253, 8, 2),
                new ComponentSpec(8, 2, 6, 8, 2),
                new ComponentSpec(9, 9, 0, 9, 2),
                new ComponentSpec(10, 9, 3, 9, 2),
                new ComponentSpec(11, 1, 7, 251, 3),
                new ComponentSpec(12, 1, 250, 6, 1),
                new ComponentSpec(13, 2, 250, 253, 1),
                new ComponentSpec(14, 2, 7, 4, 3),
                new ComponentSpec(15, 0, 4, 6, 1),
                new ComponentSpec(16, 9, 7, 254, 3),
                new ComponentSpec(17, 9, 7, 1, 3),
                new ComponentSpec(18, 9, 250, 3, 1),
                new ComponentSpec(19, 9, 250, 0, 1),
            },
            ["CKBG20_24"] = new[]
            {
                new ComponentSpec(0, 0, 253, 251, 0),
                new ComponentSpec(1, 9, 3, 250, 0),
                new ComponentSpec(2, 9, 250, 250, 0),
                new ComponentSpec(3, 9, 6, 250, 0),
                new ComponentSpec(4, 4, 246, 251, 0),
                new ComponentSpec(5, 4, 9, 248, 3),
                new ComponentSpec(6, 9, 248, 254, 1),
                new ComponentSpec(7, 9, 10, 255, 3),
                new ComponentSpec(8, 9, 10, 2, 3),
                new ComponentSpec(9, 9, 248, 1, 1),
                new ComponentSpec(10, 9, 248, 4, 1),
                new ComponentSpec(11, 9, 10, 252, 3),
                new ComponentSpec(12, 9, 10, 5, 3),
                new ComponentSpec(13, 9, 248, 7, 1),
                new ComponentSpec(14, 4, 249, 11, 1),
                new ComponentSpec(15, 4, 12, 8, 2),
                new ComponentSpec(16, 9, 255, 9, 2),
                new ComponentSpec(17, 9, 5, 9, 2),
                new ComponentSpec(18, 9, 8, 9, 2),
                new ComponentSpec(19, 9, 252, 9, 2),
                new ComponentSpec(20, 9, 2, 9, 2),
                new ComponentSpec(21, 10, 253, 5, 0),
            },
            ["CKBG30_34"] = new[]
            {
                new ComponentSpec(0, 0, 255, 250, 0),
                new ComponentSpec(1, 9, 5, 249, 0),
                new ComponentSpec(2, 9, 252, 249, 0),
                new ComponentSpec(3, 9, 249, 249, 0),
                new ComponentSpec(4, 4, 8, 247, 3),
                new ComponentSpec(5, 4, 245, 250, 0),
                new ComponentSpec(6, 9, 247, 253, 1),
                new ComponentSpec(7, 9, 247, 3, 1),
                new ComponentSpec(8, 9, 247, 0, 1),
                new ComponentSpec(9, 9, 9, 251, 3),
                new ComponentSpec(10, 9, 9, 254, 3),
                new ComponentSpec(11, 1, 8, 1, 3),
                new ComponentSpec(12, 1, 7, 4, 3),
                new ComponentSpec(13, 2, 248, 6, 1),
                new ComponentSpec(14, 3, 249, 7, 2),
                new ComponentSpec(15, 3, 6, 7, 3),
                new ComponentSpec(16, 7, 252, 7, 2),
                new ComponentSpec(17, 7, 5, 7, 2),
                new ComponentSpec(18, 9, 255, 8, 2),
                new ComponentSpec(19, 9, 2, 8, 2),
                new ComponentSpec(20, 10, 250, 4, 0),
            },
            ["CKBG35_39"] = new[]
            {
                new ComponentSpec(0, 0, 254, 249, 0),
                new ComponentSpec(1, 9, 251, 248, 0),
                new ComponentSpec(2, 9, 4, 248, 0),
                new ComponentSpec(3, 4, 7, 246, 3),
                new ComponentSpec(4, 4, 247, 249, 0),
                new ComponentSpec(5, 9, 8, 250, 3),
                new ComponentSpec(6, 9, 249, 252, 1),
                new ComponentSpec(7, 9, 249, 255, 1),
                new ComponentSpec(8, 9, 8, 253, 3),
                new ComponentSpec(9, 9, 8, 3, 3),
                new ComponentSpec(10, 7, 250, 2, 1),
                new ComponentSpec(11, 7, 7, 0, 3),
                new ComponentSpec(12, 9, 249, 5, 1),
                new ComponentSpec(13, 10, 253, 253, 3),
                new ComponentSpec(14, 9, 8, 6, 3),
                new ComponentSpec(15, 9, 249, 8, 1),
                new ComponentSpec(16, 4, 10, 9, 2),
                new ComponentSpec(17, 4, 250, 12, 1),
                new ComponentSpec(18, 9, 253, 10, 2),
                new ComponentSpec(19, 9, 6, 10, 2),
                new ComponentSpec(20, 7, 3, 9, 2),
                new ComponentSpec(21, 7, 0, 9, 2),
            },
            ["CKBG40_44"] = new[]
            {
                new ComponentSpec(0, 0, 4, 247, 0),
                new ComponentSpec(1, 9, 1, 246, 0),
                new ComponentSpec(2, 9, 251, 246, 0),
                new ComponentSpec(3, 9, 248, 246, 0),
                new ComponentSpec(4, 7, 254, 247, 0),
                new ComponentSpec(5, 4, 244, 247, 0),
                new ComponentSpec(6, 5, 10, 247, 0),
                new ComponentSpec(7, 9, 246, 250, 1),
                new ComponentSpec(8, 9, 246, 253, 1),
                new ComponentSpec(9, 9, 11, 248, 3),
                new ComponentSpec(10, 9, 11, 254, 3),
                new ComponentSpec(11, 9, 11, 1, 3),
                new ComponentSpec(12, 7, 247, 0, 1),
                new ComponentSpec(13, 7, 10, 251, 3),
                new ComponentSpec(14, 4, 13, 4, 2),
                new ComponentSpec(15, 6, 9, 7, 1),
                new ComponentSpec(16, 6, 249, 7, 0),
                new ComponentSpec(17, 3, 6, 8, 3),
                new ComponentSpec(18, 3, 252, 8, 2),
                new ComponentSpec(19, 9, 255, 9, 2),
                new ComponentSpec(20, 9, 5, 9, 2),
                new ComponentSpec(21, 7, 2, 8, 2),
                new ComponentSpec(22, 4, 248, 7, 1),
                new ComponentSpec(23, 2, 247, 3, 1),
                new ComponentSpec(24, 10, 254, 252, 3),
            },
            // Portal keep ring (bug 135): a closed eight-component ring, one gate (skin 0) on the south side at
            // grid (-2,-2) and seven walls (skin 9). With the gate shut the ring is closed; with the passage open
            // it reaches outside only through the gate. Measured on the raster footprints (8-unit flood, closed
            // with 16 and 24 units of character clearance): the landing centre is 218 units clear of the ring, and
            // every saved NPC stays at least 232 units from it (Siegemaster Wyllam, keep 388, is nearest at 232;
            // Master Eldritch B, keep 897, is 264; the others are 361 to 791). The gate reaches 842 units from the
            // centre, so the layout is not limited to the earlier 676-unit proxy.
            ["PortalKeep"] = new[]
            {
                new ComponentSpec(0, 0, 254, 254, 0),
                new ComponentSpec(1, 9, 254, 255, 1),
                new ComponentSpec(2, 9, 254, 2, 1),
                new ComponentSpec(3, 9, 253, 3, 0),
                new ComponentSpec(4, 9, 255, 3, 0),
                new ComponentSpec(5, 9, 0, 3, 0),
                new ComponentSpec(6, 9, 3, 1, 1),
                new ComponentSpec(7, 9, 3, 2, 1),
            },
        };

        public static IReadOnlyList<KeepSite> Sites { get; } = Array.AsReadOnly(new KeepSite[]
        {
            new KeepSite(234, 140, "Proving Grounds Tower", 557124, 557931, 7807, 0, "TBG40_44", false),
            new KeepSite(235, 141, "Lion's Den Fort", 553363, 555652, 4085, 0, "ClaimBG5_9", false),
            new KeepSite(236, 142, "Caer Claret", 555039, 556950, 8271, 0, "CaerClaret", false),
            new KeepSite(237, 138, "Dun Killaloe", 557067, 556900, 8768, 0, "CKBG15_19", true),
            new KeepSite(240, 144, "Wilton Keep", 554338, 556723, 7047, 0, "CKBG30_34", false),
            new KeepSite(241, 132, "Molvik Faste", 557677, 551751, 5896, 0, "CKBG35_39", true),
            new KeepSite(242, 134, "Leirvik Castle", 294287, 295659, 10976, 87, "CKBG40_44", true),
            new KeepSite(238, 143, "Thidranki Keep", 561678, 549656, 4071, 0, "CKBG20_24", false),
        });

        /// <summary>Sites the navigation search could not place (no flat clear ground). They have no keep until a site is found.</summary>
        public static IReadOnlyList<KeepSite> BlockedSites { get; } = Array.AsReadOnly(new KeepSite[]
        {
        });

        /// <summary>
        /// Portal keeps (BaseLevel 100) of the campaign regions whose client zone data has no keep fixture beside them.
        /// BattlegroundPortalKeepBuilder adds the PortalKeep ring to a row with no components, before keeps load.
        /// Centre, heading and height must equal the saved row, or the site is skipped (site_mismatch).
        /// </summary>
        public const string PortalTemplate = "PortalKeep";
        public const ushort PortalBaseLevel = 100;

        public static IReadOnlyList<KeepSite> PortalSites { get; } = Array.AsReadOnly(new KeepSite[]
        {
            new KeepSite(234, 381, "Albion Portal Keep", 573189, 549387, 8640, 63, PortalTemplate, true),
            new KeepSite(234, 637, "Midgard Portal Keep", 557159, 574661, 8640, 180, PortalTemplate, true),
            new KeepSite(234, 893, "Hibernia Portal Keep", 541023, 549746, 8640, 295, PortalTemplate, true),
            new KeepSite(235, 382, "Albion Portal Keep", 536467, 536600, 5056, 300, PortalTemplate, true),
            new KeepSite(235, 638, "Midgard Portal Keep", 544467, 575832, 5056, 208, PortalTemplate, true),
            new KeepSite(235, 894, "Hibernia Portal Keep", 579923, 554525, 5056, 65, PortalTemplate, true),
            new KeepSite(236, 383, "Albion Portal Keep", 541434, 577799, 8008, 210, PortalTemplate, true),
            new KeepSite(236, 639, "Midgard Portal Keep", 582626, 553398, 8008, 90, PortalTemplate, true),
            new KeepSite(236, 895, "Hibernia Portal Keep", 537984, 539654, 8008, 315, PortalTemplate, true),
            new KeepSite(237, 201, "Albion Portal Keep", 544726, 582450, 8288, 0, PortalTemplate, true),
            new KeepSite(237, 202, "Midgard Portal keep", 584841, 559325, 8288, 0, PortalTemplate, true),
            new KeepSite(237, 203, "Hibernia Portal Keep", 534997, 537319, 8288, 0, PortalTemplate, true),
            new KeepSite(238, 385, "Albion Portal Keep", 563575, 574212, 5408, 180, PortalTemplate, true),
            new KeepSite(238, 641, "Midgard Portal Keep", 569953, 540622, 5408, 0, PortalTemplate, true),
            new KeepSite(238, 897, "Hibernia Portal Keep", 533843, 534134, 5408, 315, PortalTemplate, true),
            new KeepSite(240, 387, "Albion Portal Keep", 554521, 583899, 6952, 180, PortalTemplate, true),
            new KeepSite(240, 643, "Midgard Portal Keep", 533631, 535174, 6728, 315, PortalTemplate, true),
            new KeepSite(240, 899, "Hibernia Portal Keep", 580637, 538535, 6736, 45, PortalTemplate, true),
            new KeepSite(241, 388, "Albion Portal Keep", 531699, 542006, 5992, 290, PortalTemplate, true),
            new KeepSite(241, 644, "Midgard Portal Keep", 550401, 577364, 5992, 180, PortalTemplate, true),
            new KeepSite(241, 900, "Hibernia Portal Keep", 575915, 543437, 5992, 75, PortalTemplate, true),
            new KeepSite(242, 390, "Albion Portal Keep", 321868, 283864, 10128, 65, PortalTemplate, true),
            new KeepSite(242, 646, "Midgard Portal Keep", 271949, 272798, 10128, 0, PortalTemplate, true),
            new KeepSite(242, 902, "Hibernia Portal Keep", 280252, 319625, 10128, 180, PortalTemplate, true),
        });

        /// <summary>
        /// Portal keeps that stay native: Cathal Valley (165) and Murdaigean (251) have a frontier keep fixture within
        /// about 520 units of each, and Murdaigean's gates are native door rows. The builder logs them and adds nothing.
        /// </summary>
        public static IReadOnlyList<NativePortalKeep> PortalNativeKeeps { get; } = Array.AsReadOnly(new NativePortalKeep[]
        {
            new NativePortalKeep(165, 1), new NativePortalKeep(165, 2), new NativePortalKeep(165, 3),
            new NativePortalKeep(251, 41), new NativePortalKeep(251, 42), new NativePortalKeep(251, 43),
        });

        /// <summary>Component tuples for a template, or an empty list for an unknown name.</summary>
        public static IReadOnlyList<ComponentSpec> Template(string name) =>
            name != null && Templates.TryGetValue(name, out ComponentSpec[] specs) ? specs : Array.Empty<ComponentSpec>();

        public static KeepSite FindSite(int keepId) => Sites.FirstOrDefault(site => site.KeepId == keepId);

        /// <summary>The server reads component offsets as signed bytes.</summary>
        public static int SignedOffset(int raw) => unchecked((sbyte)raw);

        /// <summary>Largest component offset from the keep centre, in units, plus room for guards.</summary>
        public static int SearchRadius(string template)
        {
            int largest = 0;
            foreach (ComponentSpec spec in Template(template))
                largest = Math.Max(largest, Math.Max(Math.Abs(SignedOffset(spec.X)), Math.Abs(SignedOffset(spec.Y))));
            return largest * ComponentSpacing + FootprintPadding;
        }

        /// <summary>A single tower is one tower component and nothing else.</summary>
        public static bool IsTower(IReadOnlyCollection<int> skins) => skins.Count == 1 && skins.First() == TowerSkin;
    }
}
