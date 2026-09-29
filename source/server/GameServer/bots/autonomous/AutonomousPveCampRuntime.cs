using System;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Live state for PvE camp play of autonomous world-bot groups: the pull
    /// style per party, the camp watch and the enemy hold. Companions and
    /// player-led groups never get an entry, so their add control is unchanged.
    /// </summary>
    public static class AutonomousPveCampRuntime
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousPveCampRuntime));

        /// <summary>A plan older than this is ignored (the party left PvE or broke up).</summary>
        public const long PlanLifetimeMilliseconds = 5 * 60_000;
        private const long RefreshMilliseconds = 5_000;

        private sealed class GroupState
        {
            public readonly object Sync = new();
            public PvePullPlan? Plan;
            public long RefreshedAt;
            public long NextRefresh;
            public AutonomousPveCampWatch Watch;
            public volatile bool EnemyHold;
        }

        private static readonly ConditionalWeakTable<Group, GroupState> States = new();

        public static readonly AutonomousPveRestStats RestStats = new();

        private static bool IsAutonomousPartyBot(GameBot bot) =>
            bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper && !bot.IsPlayerLedGroup &&
            !bot.IsPersistentPlayerCompanion && bot.Group != null;

        /// <summary>
        /// Recomputes the pull style from the party's classes every few
        /// seconds (leader only) and logs it when it changes.
        /// </summary>
        public static PvePullPlan Refresh(GameBot leader, string groupId, long now)
        {
            Group group = leader?.Group;
            if (!IsAutonomousPartyBot(leader))
                return new(PvePullStyle.Single, false, 0, 0, 1);
            GroupState state = States.GetValue(group, _ => new GroupState());
            lock (state.Sync)
            {
                if (state.Plan.HasValue && now < state.NextRefresh)
                    return state.Plan.Value;
                state.NextRefresh = now + RefreshMilliseconds;
                PvePullPlan plan = AutonomousPvePullStyle.For(group.GetMembersInTheGroup().OfType<GameBot>()
                    .Where(member => member.CharacterClass != null)
                    .Select(member => ((eCharacterClass)member.CharacterClass.ID, (int)member.Level)));
                if (state.Plan != plan)
                {
                    Log.Info($"PVE_PULL_STYLE group={groupId} style={plan.StyleLabel} " +
                             $"mezzer={plan.Mezzer.ToString().ToLowerInvariant()} bombers={plan.Bombers} " +
                             $"pets={plan.Pets} max_pull={plan.MaxPull}");
                }
                state.Plan = plan;
                state.RefreshedAt = now;
                return plan;
            }
        }

        private static PvePullPlan? CurrentPlan(GameBot bot)
        {
            if (!IsAutonomousPartyBot(bot) || !States.TryGetValue(bot.Group, out GroupState state))
                return null;
            // An RvR party (or one that changed objective) never uses the PvE plan.
            bool groupPve = AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.GroupPve);
            lock (state.Sync)
            {
                if (!groupPve)
                {
                    state.Plan = null;
                    return null;
                }
                PvePullPlan? plan = state.Plan;
                return plan.HasValue && GameLoop.GameLoopTime - state.RefreshedAt <= PlanLifetimeMilliseconds ? plan : null;
            }
        }

        /// <summary>This bot's autonomous party is a mez group: mezzed adds are left alone.</summary>
        public static bool MezGroupProtects(GameBot bot) => CurrentPlan(bot)?.Style == PvePullStyle.MezGroup;

        /// <summary>A crowd-control class in an autonomous mez group mezzes adds.</summary>
        public static bool HasAutonomousMezDuty(GameBot bot) =>
            bot?.CharacterClass != null && BotPartyRoles.IsCrowdControlClass((eCharacterClass)bot.CharacterClass.ID) &&
            MezGroupProtects(bot);

        public static PvePullPlan? PlanFor(GameBot bot) => CurrentPlan(bot);

        /// <summary>The watch for this party's current camp; a new camp starts a new watch.</summary>
        public static AutonomousPveCampWatch Watch(GameBot leader, string campId, int averageLevel, long now)
        {
            if (!IsAutonomousPartyBot(leader))
                return new AutonomousPveCampWatch(campId, averageLevel, now);
            GroupState state = States.GetValue(leader.Group, _ => new GroupState());
            lock (state.Sync)
            {
                if (state.Watch == null || !string.Equals(state.Watch.CampId, campId, StringComparison.Ordinal))
                {
                    state.Watch = new AutonomousPveCampWatch(campId, averageLevel, now);
                    state.EnemyHold = false;
                }
                return state.Watch;
            }
        }

        public static bool EnemyHold(GameBot bot) =>
            IsAutonomousPartyBot(bot) && States.TryGetValue(bot.Group, out GroupState state) && state.EnemyHold;

        public static void SetEnemyHold(GameBot leader, bool hold)
        {
            if (IsAutonomousPartyBot(leader))
                States.GetValue(leader.Group, _ => new GroupState()).EnemyHold = hold;
        }

        public static void ReportIfDue(long now)
        {
            string line = RestStats.TryReport(now);
            if (line != null)
                Log.Info(line);
        }
    }
}
