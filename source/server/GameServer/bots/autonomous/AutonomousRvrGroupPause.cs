using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DOL.GS;

/// <summary>One group member as the rest rule sees it.</summary>
public readonly record struct RvrPauseMember(bool Alive, int Health, int Power, bool HasPower, bool Healer,
    bool CanResurrect);

/// <summary>
/// Rest after a fight (C3, principle P5). A 1.65 group did not roll on at a
/// third of its health: it sat down, rezzed and rebuffed for a minute or two
/// ("just 2 minutes to rebuff and reg"), longer after deaths, and it moved
/// off the road first because "sitting down to regenerate power is an
/// invitation to be killed". Stealth groups hide again instead. Autonomous
/// RvR groups only; the GroupPve rest path and companions are untouched.
/// </summary>
public static class AutonomousRvrGroupPause
{
    /// <summary>The leader must be out of combat this long before the group sits.</summary>
    public const int QuietMilliseconds = 8_000;
    public const int LowPercent = 70;
    public const int RecoveredPercent = 90;
    public const int BaseSeconds = 90;
    public const int DeathBonusSeconds = 60;
    public const int HealerLowPowerBonusSeconds = 60;
    public const int HealerLowPowerPercent = 50;
    public const int MinimumSeconds = 60;
    public const int HardCapSeconds = 300;
    /// <summary>A rest this close to the last fight spot moves off first.</summary>
    public const float FightSpotRadius = 1_500;
    public const float OffRoadMinimum = 600;
    public const float OffRoadMaximum = 1_200;
    /// <summary>A pause starting this soon after a retreat is logged as after_retreat.</summary>
    public const int AfterRetreatWindowMilliseconds = 90_000;
    /// <summary>Members farther than this from the leader neither count nor hold.</summary>
    public const float MemberRange = 3_000;

    // ------------------------------------------------------------ pure rules

    public static bool NeedsRest(RvrPauseMember member) =>
        !member.Alive || member.Health < LowPercent || member.HasPower && member.Power < LowPercent;

    public static int MembersLow(IReadOnlyList<RvrPauseMember> members) => members.Count(NeedsRest);

    /// <summary>
    /// Whether a group sits down now: a doctrine applies and is not a stealth
    /// doctrine, the leader has been quiet 8 s, there was a fight since the
    /// last pause, and any member is dead or below 70 % health or power.
    /// </summary>
    public static bool ShouldStart(RvrDoctrineKind? kind, long leaderQuietMilliseconds, bool foughtSinceLastPause,
        IReadOnlyList<RvrPauseMember> members) =>
        kind.HasValue && !AutonomousRvrRoutePolicy.IsStealthDoctrine(kind.Value) &&
        leaderQuietMilliseconds >= QuietMilliseconds && foughtSinceLastPause &&
        members != null && members.Any(NeedsRest);

    /// <summary>Stealth doctrines skip the rest and hide again after the same trigger.</summary>
    public static bool ShouldRestealth(RvrDoctrineKind? kind, long leaderQuietMilliseconds, bool foughtSinceLastPause) =>
        kind.HasValue && AutonomousRvrRoutePolicy.IsStealthDoctrine(kind.Value) &&
        leaderQuietMilliseconds >= QuietMilliseconds && foughtSinceLastPause;

    /// <summary>
    /// The longest the group sits: 90 s, +60 s when a member is dead (needs a
    /// rez), +60 s when a healer is below 50 % power, scaled by the leader's
    /// Patience (0-100 gives x0.7 to x1.3) and by <paramref name="jitterRoll"/>
    /// in [0,1) (x0.85 to x1.15, 0.5 = none), so one leader does not always sit
    /// the same seconds; kept within 60-300 s.
    /// </summary>
    public static int CapSeconds(IReadOnlyList<RvrPauseMember> members, int patience, double jitterRoll = 0.5)
    {
        int seconds = BaseSeconds;
        if (members.Any(member => !member.Alive))
            seconds += DeathBonusSeconds;
        if (members.Any(member => member.Alive && member.Healer && member.HasPower && member.Power < HealerLowPowerPercent))
            seconds += HealerLowPowerBonusSeconds;
        double scale = (0.7 + Math.Clamp(patience, 0, 100) / 100d * 0.6) *
            (0.85 + Math.Clamp(jitterRoll, 0, 1) * 0.3);
        return Math.Clamp((int)Math.Round(seconds * scale), MinimumSeconds, HardCapSeconds);
    }

    /// <summary>Every living member at 90 %+ health and power, and nobody
    /// left dead while a living member could still rez.</summary>
    public static bool IsRecovered(IReadOnlyList<RvrPauseMember> members) =>
        members.Where(member => member.Alive).All(member =>
            member.Health >= RecoveredPercent && (!member.HasPower || member.Power >= RecoveredPercent)) &&
        !(members.Any(member => !member.Alive) && members.Any(member => member.Alive && member.CanResurrect));

    /// <summary>attacked, recovered, cap, or null while the pause goes on.</summary>
    public static string EndReason(bool attacked, bool recovered, long elapsedMilliseconds, int capSeconds) =>
        attacked ? "attacked" : recovered ? "recovered" : elapsedMilliseconds >= capSeconds * 1000L ? "cap" : null;

    public static bool ShouldMoveOffRoad(Vector3 here, Vector3? lastFight, bool onRoadLeg) =>
        onRoadLeg || lastFight.HasValue &&
        Vector2.Distance(new(here.X, here.Y), new(lastFight.Value.X, lastFight.Value.Y)) <= FightSpotRadius;

    /// <summary>
    /// Two rest spots 600-1,200 units to the side of the leg: first the side
    /// away from the last fight (random when the fight lies on the line),
    /// then the other side.
    /// </summary>
    public static Vector3[] OffRoadCandidates(Vector3 here, Vector2 legDirection, Vector3? lastFight,
        double distanceRoll, double sideRoll)
    {
        if (legDirection.LengthSquared() < 0.0001f)
            legDirection = new Vector2(0, 1);
        legDirection = Vector2.Normalize(legDirection);
        Vector2 perpendicular = new(-legDirection.Y, legDirection.X);
        float side = sideRoll < 0.5 ? -1 : 1;
        if (lastFight.HasValue)
        {
            float away = AutonomousRvrRoutePolicy.CoverSide(here, legDirection, lastFight.Value);
            if (away != 0) side = away;
        }
        float distance = OffRoadMinimum + (float)Math.Clamp(distanceRoll, 0, 1) * (OffRoadMaximum - OffRoadMinimum);
        Vector2 first = new Vector2(here.X, here.Y) + perpendicular * side * distance;
        Vector2 second = new Vector2(here.X, here.Y) - perpendicular * side * distance;
        return [new(first.X, first.Y, here.Z), new(second.X, second.Y, here.Z)];
    }

    /// <summary>A finished retreat the controller has not handled yet.</summary>
    public static bool RetreatFinished(long now, long retreatUntil, int serial, int seenSerial) =>
        serial > seenSerial && now >= retreatUntil;

    /// <summary>
    /// The serial a bot counts as handled when it first sees a group (new,
    /// re-formed or joined): a retreat that already ended is not replayed, a
    /// retreat still running is replanned when it ends.
    /// </summary>
    public static int SeenSerialAfterGroupChange(long now, long retreatUntil, int serial) =>
        now >= retreatUntil ? serial : Math.Max(0, serial - 1);

    // ---------------------------------------------------------- live state

    private sealed class State
    {
        public bool Active;
        public long StartTick;
        public int CapSeconds;
        public Vector3 RestPoint;
        public ushort RestRegion;
        public long LastEndTick;
        public long LastRetreatEndTick;
        public Vector3? LastFight;
        public ushort LastFightRegion;
    }

    private static readonly ConditionalWeakTable<Group, State> Groups = new();
    private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrGroupPause));

    /// <summary>The leader is fighting here (called from the retreat check).</summary>
    public static void NoteFight(GameBot leader)
    {
        if (leader?.Group == null)
            return;
        State state = Groups.GetOrCreateValue(leader.Group);
        lock (state)
        {
            state.LastFight = new(leader.X, leader.Y, leader.Z);
            state.LastFightRegion = leader.CurrentRegionID;
        }
    }

    public static void NoteRetreatEnded(GameBot bot, long now)
    {
        if (bot?.Group == null)
            return;
        State state = Groups.GetOrCreateValue(bot.Group);
        lock (state)
            state.LastRetreatEndTick = now;
    }

    /// <summary>Whether this bot's group is sitting out a rest, and where.</summary>
    public static bool IsPausing(GameBot bot, out Vector3 restPoint)
    {
        restPoint = default;
        if (bot?.Group == null || !Groups.TryGetValue(bot.Group, out State state))
            return false;
        lock (state)
        {
            // A leader who stopped evaluating (objective changed) cannot pin
            // the group: the pause lapses a few seconds after its cap.
            if (!state.Active || bot.CurrentRegionID != state.RestRegion ||
                GameLoop.GameLoopTime > state.StartTick + state.CapSeconds * 1000L + 5_000)
                return false;
            restPoint = state.RestPoint;
            return true;
        }
    }

    /// <summary>A member as the rule sees it.</summary>
    public static RvrPauseMember View(GameLiving member) => new(member.IsAlive, member.HealthPercent,
        member.ManaPercent, member.MaxMana > 0,
        member is GameBot bot && AutonomousRvrDoctrine.TraitsOf((eCharacterClass)(bot.CharacterClass?.ID ?? 0))
            .HasFlag(RvrClassTraits.Healer),
        member is GameBot rezzer && rezzer.ResurrectionSpell != null);

    /// <summary>
    /// The leader's per-tick decision: start, keep or end the group's rest.
    /// <paramref name="findRestSpot"/> returns a validated off-road spot or
    /// null (then the group rests where it stands).
    /// </summary>
    public static void EvaluateLeader(GameBot leader, bool onRoadLeg, bool relicInGroup,
        Func<Vector3?, Vector3?> findRestSpot)
    {
        if (leader?.Group == null || leader.Group.LivingLeader != leader || !AutonomousRvrDoctrineRuntime.Applies(leader))
            return;
        State state = Groups.GetOrCreateValue(leader.Group);
        long now = GameLoop.GameLoopTime;
        GameLiving[] near = leader.Group.GetMembersInTheGroup()
            .Where(member => member.CurrentRegionID == leader.CurrentRegionID &&
                             member.GetDistanceTo(leader) <= MemberRange)
            .ToArray();
        RvrPauseMember[] members = near.Select(View).ToArray();
        long groupLastCombat = near.Select(member => Math.Max(member.LastCombatTickPvE, member.LastCombatTickPvP))
            .DefaultIfEmpty(0).Max();
        long leaderLastCombat = Math.Max(leader.LastCombatTickPvE, leader.LastCombatTickPvP);
        long leaderQuiet = leaderLastCombat <= 0 ? long.MaxValue : now - leaderLastCombat;
        string group = leader.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{leader.DatabaseID}";

        lock (state)
        {
            if (state.Active)
            {
                bool attacked = near.Any(member => member.IsAlive && member.InCombatInLast(2_000));
                long elapsed = now - state.StartTick;
                string reason = relicInGroup ? "relic" : EndReason(attacked, IsRecovered(members), elapsed, state.CapSeconds);
                if (reason == null)
                    return;
                state.Active = false;
                state.LastEndTick = now;
                if (Log.IsInfoEnabled)
                    Log.Info($"RVR_GROUP_PAUSE_END group=\"{group}\" seconds={elapsed / 1000} reason={reason}");
                return;
            }

            if (relicInGroup)
                return;
            bool fought = groupLastCombat > state.LastEndTick && groupLastCombat > 0;
            bool quietGroup = near.All(member => !member.IsAlive || !member.InCombatInLast(QuietMilliseconds));
            RvrDoctrineKind? kind = AutonomousRvrDoctrineRuntime.For(leader)?.Kind;
            if (quietGroup && ShouldRestealth(kind, leaderQuiet, fought))
            {
                state.LastEndTick = now;
                foreach (GameBot member in near.OfType<GameBot>().Where(member => member.IsAlive && !member.IsStealthed &&
                             !member.InCombat && (member.GetSpecializationByName(Specs.Stealth)?.Level ?? 0) > 0))
                    member.Stealth(true);
                return;
            }
            if (!quietGroup || !ShouldStart(kind, leaderQuiet, fought, members))
                return;

            Vector3 here = new(leader.X, leader.Y, leader.Z);
            Vector3? fight = state.LastFightRegion == leader.CurrentRegionID ? state.LastFight : null;
            Vector3? spot = ShouldMoveOffRoad(here, fight, onRoadLeg) ? findRestSpot?.Invoke(fight) : null;
            int patience = leader.PersistentRecord?.Patience ?? 50;
            state.Active = true;
            state.StartTick = now;
            state.CapSeconds = CapSeconds(members, patience, Random.Shared.NextDouble());
            state.RestPoint = spot ?? here;
            state.RestRegion = leader.CurrentRegionID;
            bool afterRetreat = state.LastRetreatEndTick > 0 && now - state.LastRetreatEndTick <= AfterRetreatWindowMilliseconds;
            if (Log.IsInfoEnabled)
                Log.Info($"RVR_GROUP_PAUSE group=\"{group}\" doctrine={kind} reason={(afterRetreat ? "after_retreat" : "rest")} " +
                    $"seconds={state.CapSeconds} members_low={MembersLow(members)} " +
                    $"moved_off_road={(spot.HasValue ? "true" : "false")}");
        }
    }
}
