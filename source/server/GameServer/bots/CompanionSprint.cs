using System;

namespace DOL.GS
{
    /// <summary>
    /// Companions on a stick run sprint while their leader sprints, and pay
    /// the player's endurance cost for it (5 per second before an endurance
    /// buff), so a sprint without an endurance regeneration buff empties them
    /// as it empties the player.
    /// </summary>
    public static class CompanionSprint
    {
        public const int EndurancePerSecond = 5;

        public static bool ShouldSprint(bool stickRun, bool leaderSprinting) => stickRun && leaderSprinting;

        /// <summary>A target this far beyond melee range is worth a sprint; closer than that the tank just walks in.</summary>
        public const int ChaseSlack = 100;

        /// <summary>Below this endurance a chasing tank stops sprinting, unless Charge makes the sprint free.</summary>
        public const int ChaseEnduranceFloorPercent = 20;

        /// <summary>A tank that stopped at the floor sprints again only from here, so it does not flap on and off.</summary>
        public const int ChaseEnduranceResumePercent = 35;

        /// <summary>
        /// A tank in assist mode sprints behind the target it was called onto until the target is in melee range.
        /// Charge makes the sprint free, so it ignores the endurance floor.
        /// </summary>
        public static bool ShouldChaseSprint(bool tank, bool assistTrain, bool hostileTarget, double distance, int meleeRange,
            int endurancePercent, bool charging, bool sprinting = false) =>
            tank && assistTrain && hostileTarget && distance > meleeRange + ChaseSlack &&
            (charging || endurancePercent > (sprinting ? ChaseEnduranceFloorPercent : ChaseEnduranceResumePercent));

        /// <summary>Mirrors GamePlayer's sprint endurance tick (without Long Wind; Charge is handled by GameBot).</summary>
        public static int SprintingRegen(int regen, int fatigueConsumption, int endurance, int maxEndurance)
        {
            regen -= EndurancePerSecond;
            if (fatigueConsumption > 1)
                regen = (int)Math.Ceiling(regen * fatigueConsumption * 0.01);
            if (endurance + regen > maxEndurance - EndurancePerSecond)
                regen -= endurance + regen - (maxEndurance - EndurancePerSecond);
            return regen;
        }
    }
}
