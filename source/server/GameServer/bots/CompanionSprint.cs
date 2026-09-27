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

        /// <summary>Mirrors GamePlayer's sprint endurance tick (without Long Wind or Charge).</summary>
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
