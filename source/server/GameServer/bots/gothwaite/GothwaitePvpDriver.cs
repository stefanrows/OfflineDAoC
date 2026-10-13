using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.GS;

/// <summary>Where a mode bot is in its cycle (docs/GOTHWAITE_PVP_MODE.md 6.1).</summary>
public enum GothwaitePvpPhase { Castle, Departing, Arena }

/// <summary>
/// The Gothwaite PvP mode driver (docs/GOTHWAITE_PVP_MODE.md section 6). A mode bot rests in the castle to the rest level,
/// holds a short buff window, and then offers itself to the castle group former. A group walks out to the castle exit and
/// holds there (the arena phase; stage 3 adds roaming and fights). <see cref="Turn"/> returns true on every turn that lets
/// the class routine run (heals, buffs, the native FSM), except for a dead or crowd-controlled bot.
/// </summary>
public static class GothwaitePvpDriver
{
    /// <summary>The castle rest never lasts longer than this (game time), e.g. while regeneration is suppressed.</summary>
    public const int RestCapMilliseconds = 2 * 60_000;
    /// <summary>After the rest the bot stays this long, and until it stops casting, so the class routine can buff.</summary>
    public const int BuffWindowMilliseconds = 8_000;
    /// <summary>A bot outside the castle with no route back for this long is placed on its landing.</summary>
    public const int WalkBackRelocateMilliseconds = 60_000;

    private static readonly Logger Log = LoggerManager.Create(typeof(GothwaitePvpDriver));
    private static readonly ConditionalWeakTable<GameBot, State> States = new();

    private enum CastleStep { Resting, Buffing, Ready }

    /// <summary>Per-bot driver state. It is touched only by the bot's own turn, never by the group former.</summary>
    private sealed class State
    {
        public bool Entered;
        public GothwaitePvpPhase Phase = GothwaitePvpPhase.Castle;
        public CastleStep Step = CastleStep.Resting;
        public bool Resting;
        public long RestUntil;
        public long BuffUntil;
        public int ReadyCycle;
        public int DeathCount = -1;
        public bool SawDead;
        public bool WalkBackLogged;
        public long WalkBackSince;
        public bool RouteFailureLogged;
        public readonly GothwaitePvpRoute Route = new();
    }

    /// <summary>
    /// Runs one driver turn. Returns false when the bot takes no action this turn (no bot, dead or incapacitated).
    /// True lets the class routine run this turn.
    /// </summary>
    public static bool Turn(BotBrain brain, GameBot bot)
    {
        if (brain == null || bot == null)
            return false;
        long now = GameLoop.GameLoopTime;

        State state = States.GetValue(bot, _ => new State());
        if (!state.Entered)
        {
            state.Entered = true;
            state.DeathCount = DeathsOf(bot);
            Log.Info($"GOTH_PVP_ENTER bot=\"{bot.Name}\" level={bot.Level} realm={(int)bot.Realm} " +
                $"region={bot.CurrentRegionID} x={bot.X} y={bot.Y}");
        }
        if (!bot.IsAlive)
        {
            // Dead: the release is handled on the first turn alive again (the death counter also catches a death that was missed).
            state.SawDead = true;
            return false;
        }
        if (bot.IsIncapacitated)
            return false;

        int deaths = DeathsOf(bot);
        if (state.SawDead || deaths != state.DeathCount)
        {
            state.SawDead = false;
            state.DeathCount = deaths;
            OnReleased(bot, state);
        }

        bool engaged = brain.HasAggro || bot.InCombat;
        bool placed = GothwaitePvpGroupFormer.TryGetMembership(bot, out GothwaitePvpGroupFormer.Membership membership);
        if (placed && membership.Departing && state.Step != CastleStep.Ready)
        {
            // Not ready to walk out (it was released while its group was departing): it leaves the group and rests on its own.
            GothwaitePvpGroupFormer.Leave(bot, "not_ready");
            placed = false;
        }
        if (placed && membership.Departing)
            return Depart(brain, bot, state, membership, now, engaged);

        if (!InCastle(bot))
            return WalkBack(bot, state, now);
        state.WalkBackLogged = false;
        switch (state.Step)
        {
            case CastleStep.Resting:
                return Rest(bot, state, now, engaged);
            case CastleStep.Buffing:
                return Buff(bot, state, now, engaged);
            default:
                return Ready(brain, bot, state, placed ? membership : null, now, engaged);
        }
    }

    // The castle rest: rest until the rest level, then the buff window. The rest ends as recovered or cap, logged once each.
    private static bool Rest(GameBot bot, State state, long now, bool engaged)
    {
        state.Phase = GothwaitePvpPhase.Castle;
        int restPercent = GothwaitePvpMode.Settings.RestPercent;
        bool usesPower = bot.MaxMana > 0;
        if (!state.Resting)
        {
            if (GothwaitePvpCastleRules.RestDone(bot.HealthPercent, bot.ManaPercent, usesPower, restPercent))
            {
                BeginBuffWindow(state, now);
                return true;
            }
            state.Resting = true;
            state.RestUntil = now + RestCapMilliseconds;
            Log.Info(GothwaitePvpLines.RestStart(bot.Name, bot.HealthPercent, bot.ManaPercent));
        }
        string end = GothwaitePvpCastleRules.RestEndReason(bot.HealthPercent, bot.ManaPercent, usesPower, restPercent, now, state.RestUntil);
        if (end != null)
        {
            state.Resting = false;
            if (bot.IsRecoveryResting)
                bot.WakeRecoveryRest();
            Log.Info(GothwaitePvpLines.RestEnd(bot.Name, end, bot.HealthPercent, bot.ManaPercent));
            BeginBuffWindow(state, now);
            return true;
        }
        if (engaged)
        {
            // Self-defence: the class routine fights, and the rest resumes after the fight until its cap.
            if (bot.IsRecoveryResting)
                bot.WakeRecoveryRest();
            return true;
        }
        bot.WakeRecoveryRestIfCombatBlocked();
        if (bot.IsRecoveryResting)
            return true;
        GothwaitePvpNavigation.Stand(bot);
        // A cast in progress (a heal or a rebuff) finishes first; the rest starts on a later turn.
        if (!bot.IsCasting && !BotRestRecovery.BlocksRest(bot))
            bot.BeginRecoveryRest();
        return true;
    }

    // The buff window: the bot stands in the castle while the class routine casts its buffs (it runs on every true turn).
    private static bool Buff(GameBot bot, State state, long now, bool engaged)
    {
        state.Phase = GothwaitePvpPhase.Castle;
        if (!engaged)
            GothwaitePvpNavigation.Stand(bot);
        if (GothwaitePvpCastleRules.BuffWindowDone(now, state.BuffUntil, bot.IsCasting))
        {
            state.Step = CastleStep.Ready;
            state.ReadyCycle++;
        }
        return true;
    }

    // A ready bot offers itself to the former with the shape it draws for this ready cycle. In a gathering group a follower
    // follows its leader inside the castle, and the leader waits.
    private static bool Ready(BotBrain brain, GameBot bot, State state, GothwaitePvpGroupFormer.Membership? membership, long now, bool engaged)
    {
        state.Phase = GothwaitePvpPhase.Castle;
        if (membership == null && !GothwaitePvpCastleRules.RestDone(bot.HealthPercent, bot.ManaPercent, bot.MaxMana > 0,
                GothwaitePvpMode.Settings.RestPercent))
        {
            // Back in the castle hurt (e.g. after a walk home from a dissolved group): rest again before the next offer.
            state.Step = CastleStep.Resting;
            state.Resting = false;
            return true;
        }
        GothwaitePvpShape shape = GothwaitePvpShapes.Draw(BotIdOf(bot), state.ReadyCycle, GothwaitePvpMode.Settings.GroupShares);
        GothwaitePvpGroupFormer.Offer(bot, shape, state.ReadyCycle, now);
        if (engaged)
            return true;
        if (membership is { IsLeader: false } gathering)
            Assist(brain, bot, gathering.Leader, state, now);
        else
            GothwaitePvpNavigation.Stand(bot);
        return true;
    }

    // A departing or arrived group: the leader walks to the castle exit and holds there; followers follow their leader.
    private static bool Depart(BotBrain brain, GameBot bot, State state, GothwaitePvpGroupFormer.Membership membership, long now, bool engaged)
    {
        state.Phase = membership.Arrived ? GothwaitePvpPhase.Arena : GothwaitePvpPhase.Departing;
        if (!membership.IsLeader)
        {
            if (!engaged)
                Assist(brain, bot, membership.Leader, state, now);
            return true;
        }
        if (membership.Arrived)
        {
            if (!engaged)
                GothwaitePvpNavigation.Stand(bot);
            return true;
        }
        Point3D exit = ExitPoint();
        if (GothwaitePvpNavigation.Arrived(bot, exit, state.Route))
        {
            GothwaitePvpGroupFormer.MarkArrived(bot);
            if (!engaged)
                GothwaitePvpNavigation.Stand(bot);
            return true;
        }
        if (engaged)
            return true;
        if (GothwaitePvpNavigation.GoTo(bot, state.Route, exit, now))
        {
            state.RouteFailureLogged = false;
            return true;
        }
        if (!state.RouteFailureLogged)
        {
            state.RouteFailureLogged = true;
            Log.Info(GothwaitePvpLines.RouteFailed(bot.Name, $"{exit.X},{exit.Y},{exit.Z}", state.Route.LastReason));
        }
        return true;
    }

    // A bot outside the castle walks back to its landing. With no route for a minute it is placed on the landing.
    private static bool WalkBack(GameBot bot, State state, long now)
    {
        state.Phase = GothwaitePvpPhase.Castle;
        GothwaitePvpArena arena = GothwaitePvpMode.Arena;
        GothwaitePvpSpot spot = arena.CastleLanding(BotIdOf(bot));
        Point3D landing = new(spot.X, spot.Y, spot.Z);
        if (!state.WalkBackLogged)
        {
            state.WalkBackLogged = true;
            state.WalkBackSince = now;
            Log.Info(GothwaitePvpLines.Return(bot.Name, "outside_castle", $"x={bot.X} y={bot.Y} z={bot.Z}"));
        }
        // A rest in progress (pushed out during it) would refuse every walk, so the rest ends first.
        if (bot.IsRecoveryResting)
            bot.WakeRecoveryRest();
        if (GothwaitePvpNavigation.GoTo(bot, state.Route, landing, now))
            return true;
        if (now - state.WalkBackSince >= WalkBackRelocateMilliseconds)
        {
            state.WalkBackSince = now;
            if (bot.MoveTo(arena.RegionId, spot.X, spot.Y, spot.Z, bot.Heading))
                Log.Info(GothwaitePvpLines.Return(bot.Name, "relocated", $"route_reason={state.Route.LastReason}"));
        }
        return true;
    }

    /// <summary>
    /// A release (the first alive turn after a death) resets the bot to the castle cycle. It leaves its group unless the
    /// group is still gathering in the castle with its leader there; then it rests and rejoins once it is ready.
    /// </summary>
    private static void OnReleased(GameBot bot, State state)
    {
        state.Phase = GothwaitePvpPhase.Castle;
        state.Step = CastleStep.Resting;
        state.Resting = false;
        state.WalkBackLogged = false;
        state.RouteFailureLogged = false;
        GothwaitePvpGroupFormer.Withdraw(bot);
        string detail = "group=none";
        if (GothwaitePvpGroupFormer.TryGetMembership(bot, out GothwaitePvpGroupFormer.Membership membership))
        {
            bool leaderInCastle = membership.Leader is { IsAlive: true } leader && InCastle(leader);
            bool keep = GothwaitePvpCastleRules.KeepsGroupOnRelease(membership.IsLeader, membership.Departing, leaderInCastle);
            if (!keep)
                GothwaitePvpGroupFormer.Leave(bot, "released");
            detail = $"leader_in_castle={Lower(leaderInCastle)} left_group={Lower(!keep)}";
        }
        Log.Info(GothwaitePvpLines.Return(bot.Name, "released", detail));
    }

    private static void BeginBuffWindow(State state, long now)
    {
        state.Step = CastleStep.Buffing;
        state.BuffUntil = now + BuffWindowMilliseconds;
    }

    // A follower walks to its leader when it is far; otherwise it holds. It attacks the leader's target only when that target is legal.
    private static void Assist(BotBrain brain, GameBot bot, GameBot leader, State state, long now)
    {
        if (leader == null || !leader.IsAlive || leader.CurrentRegion != bot.CurrentRegion)
        {
            GothwaitePvpNavigation.Stand(bot);
            return;
        }
        GameLiving target = leader.TargetObject as GameLiving;
        if (target != null && MayAttack(bot, target) && bot.IsWithinRadius(target, AutonomousBattlegroundDriver.OpponentRange))
        {
            brain.Attack(target);
            return;
        }
        Point3D follow = new(leader.X, leader.Y, leader.Z);
        if (bot.GetDistanceTo(follow) > GothwaitePvpNavigation.FollowDistance)
            GothwaitePvpNavigation.GoTo(bot, state.Route, follow, now);
        else
            GothwaitePvpNavigation.Stand(bot);
    }

    /// <summary>
    /// A legal fight for a mode bot: a player-shaped target (a player, a companion or another autonomous bot) that cons to it
    /// green to red (G4), outside the castle circle, and that the server rules allow. Monsters are never a target here.
    /// </summary>
    private static bool MayAttack(GameBot bot, GameLiving target)
    {
        if (target == bot || !target.IsAlive || PvpCombatant.Resolve(target) == null)
            return false;
        if (!GothwaitePvpCastleRules.ConMayOpen(ConLevels.GetConColor(ConLevels.GetConLevel(bot.Level, target.Level))))
            return false;
        if (GothwaitePvpMode.Arena.InCastle(target.CurrentRegionID, target.X, target.Y))
            return false;
        return BattlegroundCombatTargets.IsLegal(bot, target, bot.CurrentZone) && BotSiegeRuntime.Visible(bot, target);
    }

    private static Point3D ExitPoint()
    {
        GothwaitePvpSpot exit = GothwaitePvpMode.Arena.CastleExit;
        return new Point3D(exit.X, exit.Y, exit.Z);
    }

    private static bool InCastle(GameBot bot) => GothwaitePvpMode.Arena.InCastle(bot.CurrentRegionID, bot.X, bot.Y);

    private static int DeathsOf(GameBot bot) => bot.PersistentRecord?.DeathCount ?? 0;

    private static long BotIdOf(GameBot bot) => bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;

    private static string Lower(bool value) => value ? "true" : "false";
}
