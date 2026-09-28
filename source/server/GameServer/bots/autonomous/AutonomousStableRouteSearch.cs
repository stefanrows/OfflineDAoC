using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// The first-leg corridor checks of a stable-route plan, split into bounded
/// slices (bug 56 round 2). It checks exactly the indices the one-turn loop
/// checked, in the same order, and reports the same blocked set; only the
/// cost moves across turns. Every slice makes at least one check, so a scan
/// always finishes.
/// </summary>
public sealed class AutonomousFirstLegCorridorScan
{
    public enum Outcome { Pending, Done }

    private readonly int _count;
    private readonly Func<int, bool> _needsCorridor;
    private readonly Func<int, bool> _corridor;
    private readonly Func<long> _clockMilliseconds;
    private readonly long _sliceMilliseconds;
    private int _next;

    /// <param name="count">Number of candidate first legs.</param>
    /// <param name="needsCorridor">True when the candidate's boarding point must
    /// be proven reachable (same zone, not already excluded).</param>
    /// <param name="corridor">The corridor proof; false blocks the candidate as a first leg.</param>
    /// <param name="sliceMilliseconds">Budget per slice; long.MaxValue runs to the end.</param>
    public AutonomousFirstLegCorridorScan(int count, Func<int, bool> needsCorridor, Func<int, bool> corridor,
        long sliceMilliseconds, Func<long> clockMilliseconds = null)
    {
        _count = Math.Max(0, count);
        _needsCorridor = needsCorridor ?? throw new ArgumentNullException(nameof(needsCorridor));
        _corridor = corridor ?? throw new ArgumentNullException(nameof(corridor));
        _sliceMilliseconds = Math.Max(0, sliceMilliseconds);
        _clockMilliseconds = clockMilliseconds ?? (() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);
    }

    public HashSet<int> Blocked { get; } = new();
    public int Checks { get; private set; }
    public int Slices { get; private set; }
    public bool IsDone => _next >= _count;

    public Outcome Continue()
    {
        Slices++;
        long started = _clockMilliseconds();
        int checksThisSlice = 0;
        while (_next < _count)
        {
            int index = _next;
            if (_needsCorridor(index))
            {
                if (checksThisSlice > 0 && _clockMilliseconds() - started >= _sliceMilliseconds)
                    return Outcome.Pending;
                checksThisSlice++;
                Checks++;
                if (!_corridor(index))
                    Blocked.Add(index);
            }
            _next++;
        }
        return Outcome.Done;
    }
}

public static partial class AutonomousStableRoutePlanner
{
    /// <summary>
    /// One stable-route plan that can run across brain turns. It snapshots the
    /// bot's position, money and exclusions when it begins, so the choice is
    /// the one <see cref="FindBest"/> would have made at that moment.
    /// </summary>
    public sealed partial class Search
    {
        public enum Outcome { Pending, Done }

        public const int SliceMilliseconds = 8;

        private readonly List<Candidate> _candidates;
        private readonly LegMetric[] _metrics;
        private readonly HashSet<int> _excluded;
        private readonly AutonomousFirstLegCorridorScan _scan;
        private readonly double _startX, _startY;
        private readonly int _walkSpeed;
        private readonly long _money;

        private Search(List<Candidate> candidates, LegMetric[] metrics, HashSet<int> excluded,
            AutonomousFirstLegCorridorScan scan, double startX, double startY, Vector3 goal, int walkSpeed,
            long money, ushort region)
        {
            _candidates = candidates;
            _metrics = metrics;
            _excluded = excluded;
            _scan = scan;
            _startX = startX;
            _startY = startY;
            Goal = goal;
            _walkSpeed = walkSpeed;
            _money = money;
            Region = region;
        }

        public Vector3 Goal { get; }
        public ushort Region { get; }
        /// <summary>Where the bot stood when the plan began (its start snapshot).</summary>
        public Vector2 Origin => new((float)_startX, (float)_startY);

        public const float MaximumGoalShiftUnits = 500;
        public const float MaximumOriginShiftUnits = 1_000;
        public const long MaximumAgeMilliseconds = 15_000;

        /// <summary>A pending plan still belongs to this bot's trip: same region
        /// and goal, a start snapshot the bot has not walked far away from, and
        /// not older than a few seconds. Otherwise it starts over.</summary>
        public static bool IsStillValid(ushort searchRegion, Vector2 origin, Vector3 searchGoal, long ageMilliseconds,
            ushort region, Vector3 position, Vector3 goal) =>
            searchRegion == region &&
            Vector3.DistanceSquared(searchGoal, goal) <= MaximumGoalShiftUnits * MaximumGoalShiftUnits &&
            Vector2.DistanceSquared(origin, new(position.X, position.Y)) <= MaximumOriginShiftUnits * MaximumOriginShiftUnits &&
            ageMilliseconds >= 0 && ageMilliseconds <= MaximumAgeMilliseconds;
        public int Slices => _scan?.Slices ?? 0;

        /// <summary>At most one slice of corridor checks, then the Dijkstra
        /// choice once every check is done.</summary>
        public Outcome Continue(out Choice choice)
        {
            choice = null;
            if (_scan != null && _scan.Continue() == AutonomousFirstLegCorridorScan.Outcome.Pending)
                return Outcome.Pending;

            HashSet<int> excluded = _excluded;
            if (_scan?.Blocked.Count > 0)
                (excluded ??= []).UnionWith(_scan.Blocked);
            RouteDecision? decision = ChooseFirstLeg(
                _startX, _startY, Goal.X, Goal.Y, _walkSpeed, _money, _metrics, excluded);
            if (!decision.HasValue)
                return Outcome.Done;

            Candidate selected = _candidates[decision.Value.FirstLegIndex];
            choice = new Choice(
                selected.Master,
                selected.Ticket,
                CloneRoute(selected.Route),
                TicketDestination(selected.Ticket),
                selected.RideSeconds,
                decision.Value.EstimatedSeconds,
                decision.Value.DirectWalkSeconds,
                decision.Value.HopCount,
                decision.Value.PlannedPrice,
                selected.BoardingPoint,
                selected.InteractionPoint);
            return Outcome.Done;
        }
    }

    /// <summary>
    /// Starts a stable-route plan (everything <see cref="FindBest"/> did before
    /// its corridor loop). Returns null when there is nothing to plan.
    /// </summary>
    public static Search BeginSearch(GameBot bot, Vector3 goal, IReadOnlySet<GameStableMaster> excludedBoardingMasters,
        bool boundedMeetupApproach, long sliceMilliseconds) =>
        Search.Begin(bot, goal, excludedBoardingMasters, boundedMeetupApproach, sliceMilliseconds);

    public sealed partial class Search
    {
        internal static Search Begin(GameBot bot, Vector3 goal, IReadOnlySet<GameStableMaster> excludedBoardingMasters,
            bool boundedMeetupApproach, long sliceMilliseconds)
        {
            if (bot?.CurrentRegion == null)
                return null;

            List<Candidate> candidates = GetCandidates(bot);
            if (candidates.Count == 0)
                return null;

            LegMetric[] metrics = candidates.Select((candidate, index) => new LegMetric(
                index,
                candidate.BoardingPoint.X,
                candidate.BoardingPoint.Y,
                candidate.End.X,
                candidate.End.Y,
                candidate.RideSeconds,
                Math.Max(0L, (long)candidate.Ticket.Price))).ToArray();

            long money = bot.DatabaseID > 0 ? AutonomousBotEconomy.GetMoney(bot.DatabaseID) : 0;
            // Exclude only boarding here. A later horse can still reach that master
            // without repeating this bot's failed walk from its current location.
            HashSet<int> excluded = excludedBoardingMasters == null ? null : candidates
                .Select((candidate, index) => (candidate, index))
                .Where(entry => excludedBoardingMasters.Contains(entry.candidate.Master))
                .Select(entry => entry.index).ToHashSet();

            // A timed meetup must not spend most of its fifteen-minute attendance
            // window walking away to a distant first horse (the Vuloch failures).
            // Later network legs remain legal; outside assembly behavior is unchanged.
            if (boundedMeetupApproach)
                for (int i = 0; i < candidates.Count; i++)
                    if (!CanApproachStableDuringMeetup(Distance(bot.X, bot.Y,
                            candidates[i].BoardingPoint.X, candidates[i].BoardingPoint.Y), bot.MaxSpeed))
                        (excluded ??= []).Add(i);

            // A stable on another disconnected surface can be closer in straight
            // line than the reachable stable inside a fortress.  Exclude it only as
            // this journey's first boarding leg after proving that both points are
            // in the same zone but have no Detour corridor.  It remains available
            // as a later horse-network destination. A leg already excluded above
            // needs no proof: excluding it again cannot change the choice.
            Zone currentZone = bot.CurrentZone;
            Region region = bot.CurrentRegion;
            IPathfindingMgr nav = PathfindingProvider.Instance;
            AutonomousFirstLegCorridorScan scan = null;
            if (currentZone != null && nav.IsAvailable && nav.HasNavmesh(currentZone) &&
                AutonomousNavigationSurface.TryFloor(nav, currentZone,
                    new(bot.X, bot.Y, bot.Z), out Vector3 currentFloor))
            {
                HashSet<int> alreadyExcluded = excluded;
                scan = new AutonomousFirstLegCorridorScan(candidates.Count,
                    index => alreadyExcluded?.Contains(index) != true &&
                             region.GetZone((int)candidates[index].BoardingPoint.X,
                                 (int)candidates[index].BoardingPoint.Y) == currentZone,
                    index => CanUseAsFirstBoardingLeg(true, AutonomousZoneItinerary.HasCompleteCorridor(
                        nav, currentZone, currentFloor, candidates[index].BoardingPoint)),
                    sliceMilliseconds);
            }

            return new Search(candidates, metrics, excluded, scan, bot.X, bot.Y, goal,
                Math.Max(1, (int)bot.MaxSpeed), money, bot.CurrentRegionID);
        }
    }
}
