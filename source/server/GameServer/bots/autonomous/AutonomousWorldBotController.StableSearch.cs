using System.Collections.Generic;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS;

/// <summary>
/// Bug 56 round 2: a world bot plans its stable route in bounded slices. The
/// first-leg corridor checks of <see cref="AutonomousStableRoutePlanner.FindBest"/>
/// cost up to about 220 ms in one brain turn (live 2026-09-28). One slice runs
/// per turn; the choice is the one FindBest makes when the search begins. While
/// it is pending the bot keeps doing what it did without a horse this turn and
/// thinks again after 250 ms. Other bot kinds keep the one-turn FindBest.
/// </summary>
public sealed partial class AutonomousWorldBotController
{
    private AutonomousStableRoutePlanner.Search _stableSearch;
    private long _stableSearchStartedTick;

    /// <returns>False while the search is still pending; true with the finished
    /// choice (null when no horse helps).</returns>
    private bool ContinueStableRouteSearch(GameBot bot, Vector3 waypoint,
        IReadOnlySet<GameStableMaster> excludedBoardingMasters, bool boundedMeetupApproach,
        out AutonomousStableRoutePlanner.Choice choice)
    {
        choice = null;
        long now = GameLoop.GameLoopTime;
        if (_stableSearch != null && !AutonomousStableRoutePlanner.Search.IsStillValid(_stableSearch.Region,
                _stableSearch.Origin, _stableSearch.Goal, now - _stableSearchStartedTick,
                bot.CurrentRegionID, new(bot.X, bot.Y, bot.Z), waypoint))
            _stableSearch = null;

        AutonomousStableRoutePlanner.Search.Outcome outcome;
        using (BotThinkProfiler.Measure(BotThinkPhase.StableRouteFindBest))
        {
            if (_stableSearch == null)
            {
                _stableSearch = AutonomousStableRoutePlanner.BeginSearch(bot, waypoint, excludedBoardingMasters,
                    boundedMeetupApproach, AutonomousStableRoutePlanner.Search.SliceMilliseconds);
                _stableSearchStartedTick = now;
                if (_stableSearch == null)
                    return true;
            }
            using (BotThinkProfiler.Measure(BotThinkPhase.StableRouteSearchSlice))
                outcome = _stableSearch.Continue(out choice);
        }

        if (outcome == AutonomousStableRoutePlanner.Search.Outcome.Pending)
        {
            if (bot.Brain is BotBrain brain)
                brain.ThinkInterval = 250;
            return false;
        }
        _stableSearch = null;
        return true;
    }
}
