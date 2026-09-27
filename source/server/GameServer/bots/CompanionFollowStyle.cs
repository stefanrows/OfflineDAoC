namespace DOL.GS
{
    /// <summary>
    /// How a companion follows its player, the way a groupmate would: it does
    /// not jump after the first step, it sticks in a line behind the leader on
    /// a long speed run, and otherwise keeps its own spot in a loose spread.
    /// </summary>
    public readonly record struct CompanionFollowStyle(int Kind)
    {
        public static readonly CompanionFollowStyle Hold = new(0);
        public static readonly CompanionFollowStyle Spread = new(1);
        public static readonly CompanionFollowStyle Stick = new(2);

        /// <summary>Speed above normal running (a speed song or buff).</summary>
        public const short SpeedRunThreshold = 250;
        public const long StickAfterMilliseconds = 3_000;
        public const long ReactAfterMilliseconds = 1_500;
        public const int ReactSlack = 220;

        public static CompanionFollowStyle Choose(bool leaderMoving, long leaderMovingFor, short leaderSpeed,
            double distanceToLeader, int slotDistance, bool companionMoving)
        {
            if (leaderMoving && leaderSpeed >= SpeedRunThreshold && leaderMovingFor >= StickAfterMilliseconds)
                return Stick;
            if (leaderMoving && !companionMoving && leaderMovingFor < ReactAfterMilliseconds &&
                distanceToLeader < slotDistance + ReactSlack)
                return Hold;
            return Spread;
        }

        /// <summary>Trail distance for the n-th companion in a stick line.</summary>
        public static int StickDistance(int place) => 70 + place * 45;
    }
}
