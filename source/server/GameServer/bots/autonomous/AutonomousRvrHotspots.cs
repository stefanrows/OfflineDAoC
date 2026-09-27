using System;

namespace DOL.GS;

/// <summary>
/// The Old Frontier zones players gathered in. Emain Macha, with the Albion
/// and Midgard milegates facing each other across one field, was where every
/// realm's groups met; Hadrian's Wall and Odin's Gate were the other border
/// fights. Roaming groups weight these zones so they cross realm borders.
/// </summary>
public static class AutonomousRvrHotspots
{
    public const double EmainMacha = 3.0;
    public const double BorderZone = 1.6;

    public static double Weight(string zoneName) => zoneName?.Trim() switch
    {
        { } name when name.Equals("Emain Macha", StringComparison.OrdinalIgnoreCase) => EmainMacha,
        { } name when name.Equals("Hadrian's Wall", StringComparison.OrdinalIgnoreCase) ||
                      name.Equals("Odin's Gate", StringComparison.OrdinalIgnoreCase) => BorderZone,
        _ => 1.0,
    };
}
