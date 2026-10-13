using OfflineDaoc.Configuration;

namespace DOL.GS;

/// <summary>
/// The group shape a castle bot draws (docs/GOTHWAITE_PVP_MODE.md 6.3). A solo departs alone at once; the other shapes
/// gather at the castle until they are full or their wait runs out.
/// </summary>
public enum GothwaitePvpShape { Solo, Duo, Small, Eight }

/// <summary>
/// The shape draw and the group sizes. Every value is a pure function of the bot id and its ready cycle, so the same bot
/// draws the same shape again, and over many bots the mix follows the settings shares.
/// </summary>
public static class GothwaitePvpShapes
{
    public const int SmallestSmall = 3;
    public const int LargestSmall = 5;
    public const int EightSize = 8;
    public const int EightMinimum = 5;

    /// <summary>The shape of one ready cycle of one bot, drawn in the shares' proportions (Solo, Duo, Small, Eight).</summary>
    public static GothwaitePvpShape Draw(long botId, int cycle, GothwaitePvpGroupShares shares)
    {
        int total = shares?.Total ?? 0;
        if (total <= 0)
            return GothwaitePvpShape.Solo;
        int bucket = (int)(Hash(botId, cycle, 0) % (ulong)total);
        if (bucket < shares.Solo)
            return GothwaitePvpShape.Solo;
        if (bucket < shares.Solo + shares.Duo)
            return GothwaitePvpShape.Duo;
        return bucket < shares.Solo + shares.Duo + shares.Small ? GothwaitePvpShape.Small : GothwaitePvpShape.Eight;
    }

    /// <summary>Members a group of this shape must have to leave when its wait runs out.</summary>
    public static int MinimumSize(GothwaitePvpShape shape) => shape switch
    {
        GothwaitePvpShape.Solo => 1,
        GothwaitePvpShape.Duo => 2,
        GothwaitePvpShape.Small => SmallestSmall,
        _ => EightMinimum,
    };

    /// <summary>The full size that makes a group leave at once. A small group's size (3 to 5) is drawn from the same bot.</summary>
    public static int TargetSize(GothwaitePvpShape shape, long botId, int cycle) => shape switch
    {
        GothwaitePvpShape.Solo => 1,
        GothwaitePvpShape.Duo => 2,
        GothwaitePvpShape.Small => SmallestSmall + (int)(Hash(botId, cycle, 1) % (ulong)(LargestSmall - SmallestSmall + 1)),
        _ => EightSize,
    };

    /// <summary>The shape's name as written in the log lines.</summary>
    public static string Name(GothwaitePvpShape shape) => shape switch
    {
        GothwaitePvpShape.Solo => "solo",
        GothwaitePvpShape.Duo => "duo",
        GothwaitePvpShape.Small => "small",
        _ => "eight",
    };

    /// <summary>A 64-bit hash of (bot, cycle, salt). Salt 0 draws the shape, salt 1 the small group's size.</summary>
    public static ulong Hash(long botId, int cycle, int salt)
    {
        ulong first = Mix(unchecked((ulong)botId) ^ 0x243F6A8885A308D3UL);
        return Mix(first + unchecked((ulong)(uint)cycle) * 0x9E3779B97F4A7C15UL + unchecked((ulong)(uint)salt) * 0xD1B54A32D192ED03UL);
    }

    // SplitMix64 finaliser.
    private static ulong Mix(ulong value)
    {
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }
}
