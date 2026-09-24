using System;

namespace OfflineDaoc.Configuration;

/// <summary>Creation-time population shape. Never recalculates a saved character's progress.</summary>
public static class AutonomousPopulationShape
{
    private static readonly int[] BandEndPercent = [15, 30, 50, 75, 100];
    private static readonly (int Minimum, int Maximum)[] Bands =
        [(1, 9), (10, 19), (20, 34), (35, 49), (50, 50)];

    // The server's GamePlayer.XPForLevel table, indexed by current level - 1.
    private static readonly long[] StartingExperience =
    [
        0, 50, 250, 850, 2300, 6350, 15950, 37950, 88950, 203950,
        459950, 839950, 1399950, 2199950, 3399950, 5199950, 7899950,
        11799950, 17499950, 25899950, 38199950, 54699950, 76999950,
        106999950, 146999950, 199999950, 269999950, 359999950,
        479999950, 639999950, 849999950, 1119999950, 1469999950,
        1929999950, 2529999950, 3319999950, 4299999950, 5499999950,
        6899999950, 8599999950, 12899999950, 20699999950,
        29999999950, 40799999950, 53999999950, 69599999950,
        88499999950, 110999999950, 137999999950, 169999999950,
    ];

    public static int[] NewLevels(PopulationWorldShape shape, int count, Random? random = null)
    {
        if (count < 1 || count > 100) throw new ArgumentOutOfRangeException(nameof(count));
        var levels = new int[count];
        if (shape == PopulationWorldShape.FreshLaunch) { Array.Fill(levels, 1); return levels; }
        if (shape != PopulationWorldShape.Established) throw new ArgumentOutOfRangeException(nameof(shape));
        random ??= Random.Shared;
        for (int index = 0; index < count; index++)
        {
            int percentile = (int)((index + random.NextDouble()) * 100 / count);
            int band = Array.FindIndex(BandEndPercent, end => percentile < end);
            (int minimum, int maximum) = Bands[band];
            levels[index] = random.Next(minimum, maximum + 1);
        }
        for (int index = levels.Length - 1; index > 0; index--)
        {
            int other = random.Next(index + 1);
            (levels[index], levels[other]) = (levels[other], levels[index]);
        }
        return levels;
    }

    public static long ExperienceForLevel(int level) =>
        level is >= 1 and <= 50 ? StartingExperience[level - 1] :
        throw new ArgumentOutOfRangeException(nameof(level));

    public static long RealmPointsForNewLevelFifty(Random? random = null)
    {
        long[] values = [1375, 7125, 25375, 71750];
        return values[(random ?? Random.Shared).Next(values.Length)];
    }
}
