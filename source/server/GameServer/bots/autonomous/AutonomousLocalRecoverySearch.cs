using System;
using System.Diagnostics;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// The collision-safe side-step search of a stalled autonomous route, split
/// into bounded slices. It evaluates exactly the candidates of the former
/// one-turn search, in the same order and with the same score, so the chosen
/// side step is identical. Only the cost moves: a stalled bot stands still for
/// a few AI turns instead of stalling the whole NPC service for one turn.
/// Live profile 2026-09-28: 21 corridor checks against an unreachable goal took
/// 100-300 ms per brain turn and made up about 30 % of all bot think samples.
/// </summary>
public sealed class AutonomousLocalRecoverySearch
{
    public enum Outcome { Pending, Found, NotFound }

    public const int SliceMilliseconds = 8;
    public const float OriginToleranceUnits = 32;
    // Same tolerance as the controller's "destination changed" test.
    public const float DestinationToleranceUnits = 96;

    private static readonly float[] Radii = [220f, 340f, 460f];
    private static readonly int[][] AngleOrders =
    [
        [90, -90, 135, -135, 180, 45, -45],
        [-90, 90, -135, 135, 180, -45, 45],
        [135, -135, 90, -90, 180, 45, -45],
    ];

    public static int CandidateCount => Radii.Length * AngleOrders[0].Length;

    private readonly Vector3 _current;
    private readonly Vector3 _destination;
    private readonly Vector2 _forward;
    private readonly int[] _angles;
    private readonly Func<Vector3?> _corner;
    private readonly Func<Vector3, Vector3?> _moveAlongGround;
    private readonly Func<Vector3, Vector3, bool> _lineOfSight;
    private readonly Func<Vector3, bool> _onwardCorridor;
    private readonly Func<long> _clockMilliseconds;
    private bool _cornerChecked;
    private int _next;
    private float _bestScore = float.MinValue;
    private Vector3 _best;

    /// <param name="corner">Recovery along the existing corridor; checked first.</param>
    /// <param name="moveAlongGround">Walkable surface point toward a raw side step.</param>
    /// <param name="lineOfSight">Navmesh line of sight from the origin to a surface point.</param>
    /// <param name="onwardCorridor">Complete corridor from a surface point to the onward goal
    /// (return true when the onward goal lies in another zone).</param>
    public AutonomousLocalRecoverySearch(Vector3 current, Vector3 destination, int attempt,
        Func<Vector3?> corner, Func<Vector3, Vector3?> moveAlongGround,
        Func<Vector3, Vector3, bool> lineOfSight, Func<Vector3, bool> onwardCorridor,
        Func<long> clockMilliseconds = null)
    {
        _current = current;
        _destination = destination;
        Vector2 forward = new(destination.X - current.X, destination.Y - current.Y);
        _forward = forward.LengthSquared() < 1f ? new(0, 1) : Vector2.Normalize(forward);
        _angles = AngleOrders[Math.Clamp(attempt - 1, 0, AngleOrders.Length - 1)];
        _corner = corner;
        _moveAlongGround = moveAlongGround;
        _lineOfSight = lineOfSight;
        _onwardCorridor = onwardCorridor;
        _clockMilliseconds = clockMilliseconds ?? (() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);
    }

    public Vector3 Origin => _current;
    public Vector3 Destination => _destination;
    public int EvaluatedCandidates => _next;
    public int Slices { get; private set; }

    /// <summary>True while the stalled bot is still where the search began and
    /// the route still targets the same destination.</summary>
    public bool IsFor(Vector3 current, Vector3 destination) =>
        Vector3.DistanceSquared(current, _current) <= OriginToleranceUnits * OriginToleranceUnits &&
        Vector3.DistanceSquared(destination, _destination) <= DestinationToleranceUnits * DestinationToleranceUnits;

    /// <summary>Runs until the slice budget is spent. At least one step always
    /// runs, so every call makes progress and the search ends after at most
    /// <see cref="CandidateCount"/> + 1 calls.</summary>
    public Outcome Continue(out Vector3 recovery)
    {
        recovery = default;
        Slices++;
        long started = _clockMilliseconds();
        bool worked = false;
        if (!_cornerChecked)
        {
            _cornerChecked = true;
            worked = true;
            Vector3? corner = _corner?.Invoke();
            if (corner.HasValue)
            {
                recovery = corner.Value;
                return Outcome.Found;
            }
        }

        while (_next < CandidateCount)
        {
            if (worked && _clockMilliseconds() - started >= SliceMilliseconds)
                return Outcome.Pending;
            int index = _next++;
            worked = true;
            Evaluate(Radii[index / _angles.Length], _angles[index % _angles.Length]);
        }

        if (_bestScore > float.MinValue)
        {
            recovery = _best;
            return Outcome.Found;
        }
        return Outcome.NotFound;
    }

    private void Evaluate(float radius, int degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        Vector2 direction = new(
            _forward.X * MathF.Cos(radians) - _forward.Y * MathF.Sin(radians),
            _forward.X * MathF.Sin(radians) + _forward.Y * MathF.Cos(radians));
        Vector3 raw = new(_current.X + direction.X * radius, _current.Y + direction.Y * radius, _current.Z);
        Vector3? surface = _moveAlongGround(raw);
        if (!surface.HasValue || Vector3.DistanceSquared(_current, surface.Value) < 80 * 80 ||
            !_lineOfSight(_current, surface.Value))
            return;
        if (!_onwardCorridor(surface.Value))
            return;

        Vector2 achieved = new(surface.Value.X - _current.X, surface.Value.Y - _current.Y);
        float lateral = MathF.Abs(_forward.X * achieved.Y - _forward.Y * achieved.X);
        float clearance = achieved.Length();
        float score = lateral * 2f + clearance;
        if (score <= _bestScore)
            return;
        _bestScore = score;
        _best = surface.Value;
    }
}
