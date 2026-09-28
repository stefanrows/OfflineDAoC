using System;

namespace DOL.GS
{
    /// <summary>Pure layout rules for companion squads (task 42/43): how many an owner
    /// may have, and how far behind/beside the owner each squad's leader marches.</summary>
    public static class CompanionSquadFormation
    {
        /// <summary>Highest squad index an owner may use; squad 0 is the owner's own group.</summary>
        public const int MaxSquadCount = 5;

        public static bool IsValidSquadIndex(int squadIndex) => squadIndex is >= 1 and <= MaxSquadCount;

        /// <summary>
        /// The squad leader's follow-distance band from the owner: a shallow fan of
        /// standoff distances, each squad a little farther out than the last, all inside
        /// the requested 200-400 unit range. Squad members then follow their own squad
        /// leader at the ordinary companion follow distance.
        /// </summary>
        public static (int MinDistance, int MaxDistance) LeaderFollowBand(int squadIndex)
        {
            if (!IsValidSquadIndex(squadIndex))
                return (BotManager.FOLLOW_DISTANCE, BotManager.MAX_FOLLOW_DISTANCE);

            int min = 200 + (squadIndex - 1) * 40;
            return (min, min + 40);
        }
    }
}
