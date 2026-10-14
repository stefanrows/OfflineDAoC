using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.GS;

/// <summary>Where a mode bot is in its cycle (docs/GOTHWAITE_PVP_MODE.md 6.1).</summary>
public enum GothwaitePvpPhase { Castle, Departing, Arena }

/// <summary>
/// The Gothwaite PvP mode driver (docs/GOTHWAITE_PVP_MODE.md section 6). A mode bot rests in the castle to the rest level,
/// holds a short buff window, and offers itself to the castle group former. A group walks out to the castle exit, which is
/// only a waypoint: the leader roams on once it is out of the gate. No bot opens a fight inside the no-fight ring (section 6.2).
/// From the gate a group first walks an outbound leg to a patrol point picked at random over the whole island and opens no fight
/// on the way (it still fights back), so the groups spread out instead of fighting the next group out just beyond the ring.
/// In the arena the leader (or a solo) roams the patrol points with a dwell at each, every bot scans for legal openings every
/// 1.5 s and drops a fight that breaks the castle or edge rules, a bot keeps inside its edge, and a bot goes home when it is
/// low, its group is gone, or it stalls. <see cref="Turn"/> returns true on every turn that lets the class routine run
/// (heals, buffs, the native FSM), except for a dead or crowd-controlled bot.
/// </summary>
public static class GothwaitePvpDriver
{
    /// <summary>The castle rest never lasts longer than this (game time), e.g. while regeneration is suppressed.</summary>
    public const int RestCapMilliseconds = 2 * 60_000;
    /// <summary>After the rest the bot stays this long, and until it stops casting, so the class routine can buff.</summary>
    public const int BuffWindowMilliseconds = 8_000;
    /// <summary>A bot outside the castle with no route back for this long is placed on its landing.</summary>
    public const int WalkBackRelocateMilliseconds = 60_000;
    // The chase limit logs one line per target; the set of logged targets is cleared past this size.
    private const int ChaseLogLimit = 16;

    private static readonly Logger Log = LoggerManager.Create(typeof(GothwaitePvpDriver));
    private static readonly ConditionalWeakTable<GameBot, State> States = new();

    private enum CastleStep { Resting, Buffing, Ready }

    /// <summary>Per-bot driver state. It is touched only by the bot's own turn, never by the group former.</summary>
    private sealed class State
    {
        // Castle cycle (stage 2).
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

        // Arena cycle (stage 3).
        public bool InGroup;
        public bool Returning;
        public Random Random;
        public long NextScan;
        public Point3D Goal;
        public GothwaitePvpSpot? Spot;
        public long DwellUntil;
        public int StuckFailures;
        public bool ProgressSet;
        public double ProgressDistance;
        public double ProgressX;
        public double ProgressY;
        public long ProgressAt;
        public bool EdgeLogged;
        // The next patrol pick is the first after the group arrived outside the gate: it ignores heat (section 6.4, live-run fix 2026-10-13).
        public bool FirstPick;
        // On the outbound leg (second live run, 2026-10-13) the bot opens no fight. A leader sets it for itself; a follower copies its
        // leader's every turn. Volatile: GothwaitePvpTargetRules.MayOpen reads it from other bots' turns and the class brain.
        public volatile bool Outbound;
        public long OutboundSince;
        public string OutboundReason;
        // The patrol point a leader or solo has picked and walks to or dwells at, by name; read by other leaders' picks to spread out.
        public volatile string Claim;
        public HashSet<int> ChaseLogged;
        // The scan's acceptance test, built once for this bot (see AcceptFor).
        public Func<GameLiving, bool> Accept;

        // Stage 6 tactics (docs/GOTHWAITE_PVP_MODE.md section 13).
        // The bot runs from a lost fight and fights nothing meanwhile; a follower copies its leader. Volatile: the self-defence
        // rule reads it from other bots' turns and the class brain.
        public volatile bool Retreating;
        public long RetreatUntil;
        public long NextRetreatCheck;
        // The group rests and gathers after a fight: the leader holds its spot, the members sit, and the return rules wait.
        public volatile bool Regrouping;
        public long RegroupSince;
        public bool WasEngaged;
        // Where the current fight began, for the chase rule ("don't chase into the adds").
        public bool FightStartSet;
        public int FightStartX;
        public int FightStartY;
        // The add draw: the leader listens for nearby fights now and then and may walk to one (a goal that is no patrol point).
        public long NextAddCheck;
        public bool AddLeg;
        // A hunter (a stealth class alone or in a stealth doctrine) stays hidden on the hunt; its stealth attempts are rate-limited.
        public bool Hunter;
        public long NextStealthTry;
        public long NextRefusedCount;
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
            state.Random = new Random(unchecked((int)BotIdOf(bot)) ^ Environment.TickCount);
            state.NextScan = now + GothwaitePvpArenaRules.ScanOffsetMilliseconds(BotIdOf(bot));
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

        bool placed = GothwaitePvpGroupFormer.TryGetMembership(bot, out GothwaitePvpGroupFormer.Membership membership);
        if (placed && membership.Departing && state.Step != CastleStep.Ready)
        {
            // Not ready to walk out (it was released while its group was departing): it leaves the group and rests on its own.
            GothwaitePvpGroupFormer.Leave(bot, "not_ready");
            placed = false;
        }
        if (placed)
            state.InGroup = true;
        else if (state.InGroup)
        {
            // The former ended this bot's group while it was not leaving it: a lost leader, or a group that was wiped.
            state.InGroup = false;
            GroupEnded(bot, state, now);
        }

        if (placed && membership.Departing)
            return Depart(brain, bot, state, membership, now);
        if (!InCastle(bot))
            return WalkBack(bot, state, now, FightNow(brain, bot, state, null, now));

        state.WalkBackLogged = false;
        state.Returning = false;
        bool engaged = brain.HasAggro || bot.InCombat;
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

    /// <summary>
    /// The phase of a mode bot for the status command (read-only, lock-free). The driver writes its state on the bot's own turn, so
    /// the answer is a turn stale at most. A bot with no state yet has not had a turn and counts as castle resting, and so does a dead
    /// bot: it releases to the castle.
    /// </summary>
    public static GothwaitePvpBotPhase PhaseOf(GameBot bot)
    {
        if (bot == null || !bot.IsAlive || !States.TryGetValue(bot, out State state))
            return GothwaitePvpBotPhase.CastleResting;
        if (state.Phase == GothwaitePvpPhase.Arena)
            return GothwaitePvpBotPhase.Arena;
        if (state.Phase == GothwaitePvpPhase.Departing)
            return GothwaitePvpBotPhase.Departing;
        return state.Step == CastleStep.Ready ? GothwaitePvpBotPhase.CastleReady : GothwaitePvpBotPhase.CastleResting;
    }

    /// <summary>
    /// True while the bot is on an outbound leg and opens no fight (<see cref="GothwaitePvpTargetRules.MayOpen"/> asks). Read-only and
    /// lock-free, like <see cref="PhaseOf"/>. False for a bot with no driver state.
    /// </summary>
    public static bool IsOutbound(GameBot bot) => bot != null && States.TryGetValue(bot, out State state) && state.Outbound;

    /// <summary>
    /// True while the bot runs from a lost fight (stage 6): it opens nothing and fights nothing back until the run is over.
    /// Read-only and lock-free, like <see cref="IsOutbound"/>. False for a bot with no driver state.
    /// </summary>
    public static bool IsRetreating(GameBot bot) => bot != null && States.TryGetValue(bot, out State state) && state.Retreating;

    /// <summary>True while the bot's group rests and gathers after a fight (stage 6). False for a bot with no driver state.</summary>
    public static bool IsRegrouping(GameBot bot) => bot != null && States.TryGetValue(bot, out State state) && state.Regrouping;

    /// <summary>
    /// A hunter wants to stay stealthed (stage 6): it is outside the castle, departing or in the arena, and not walking home.
    /// <see cref="GothwaitePvpStealth.KeepsStealth"/> asks, for the brain's idle gate. False for a bot with no driver state.
    /// </summary>
    public static bool WantsStealth(GameBot bot) => bot != null && States.TryGetValue(bot, out State state) &&
        state.Phase != GothwaitePvpPhase.Castle && !state.Returning;

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
            // Back in the castle hurt (e.g. after a walk home from a dissolved group): rest again before the next offer. A bot that waits
            // in the pool (a solo in particular) leaves it now, so the departure stagger never releases it hurt.
            GothwaitePvpGroupFormer.Withdraw(bot);
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

    /// <summary>
    /// A departing group (section 6.4): the leader walks to the castle exit and, once there, roams the arena; a follower
    /// follows its leader. Every departing bot fights what the rules allow, drops a fight that breaks them, scans while it
    /// walks, and goes home when it must. The return rules apply only when the bot is not fighting.
    /// </summary>
    private static bool Depart(BotBrain brain, GameBot bot, State state, GothwaitePvpGroupFormer.Membership membership, long now)
    {
        state.Phase = membership.Arrived ? GothwaitePvpPhase.Arena : GothwaitePvpPhase.Departing;
        state.Hunter = GothwaitePvpStealth.Applies(bot);
        if (!membership.IsLeader)
        {
            FollowOutbound(state, membership, now);
            FollowRetreat(bot, state, membership);
        }
        if (state.Retreating && now >= state.RetreatUntil && EndRetreat(bot, state, membership, now))
            return GoHome(brain, bot, state, membership, "retreat", now, logReturn: true);
        bool engaged = FightNow(brain, bot, state, membership, now);
        if (state.Retreating)
        {
            // Running (stage 6): no fight, no scan, no return rule. The walk below is the run: the leader's goal, a follower's slot behind it.
            if (engaged)
                StandDown(brain, bot, state);
            engaged = false;
            Run(bot);
        }
        else if (engaged)
        {
            Fighting(brain, bot, state, membership, now);
        }
        else
        {
            if (state.WasEngaged)
                FightOver(bot, state, membership, now);
            string reason = ReturnReasonOf(bot, membership);
            if (state.Regrouping)
                reason = GothwaitePvpTactics.ReturnWhileRegrouping(reason);
            if (reason != null)
                return GoHome(brain, bot, state, membership, reason, now, logReturn: true);
            if (state.Hunter)
                GothwaitePvpStealth.Maintain(bot, now, ref state.NextStealthTry);
            engaged = Scan(brain, bot, state, membership, now);
        }
        state.WasEngaged = engaged;
        if (!membership.IsLeader)
            return Follow(brain, bot, state, membership, engaged, now);
        if (membership.Arrived)
            return Roam(brain, bot, state, membership, engaged, now);
        return WalkToExit(brain, bot, state, membership, engaged, now);
    }

    // While the bot fights (stage 6): the start of the fight is noted for the chase rule, a regroup is over, and every few seconds a
    // leader weighs the fight (the doctrine's retreat rule) while a solo weighs whether it was added on or is losing.
    private static void Fighting(BotBrain brain, GameBot bot, State state, GothwaitePvpGroupFormer.Membership membership, long now)
    {
        if (!state.FightStartSet)
        {
            state.FightStartSet = true;
            state.FightStartX = bot.X;
            state.FightStartY = bot.Y;
        }
        if (state.Regrouping)
            EndRegroup(bot, state, "fight", now);
        if (!membership.IsLeader || now < state.NextRetreatCheck)
            return;
        state.NextRetreatCheck = now + GothwaitePvpTactics.RetreatCheckMilliseconds;
        string reason;
        if (membership.Shape == GothwaitePvpShape.Solo)
        {
            int attackers = GothwaitePvpCombat.PlayerShapedAttackers(bot, GothwaitePvpTactics.FollowerChaseRange);
            int enemyHealth = bot.TargetObject is GameLiving { IsAlive: true } target ? target.HealthPercent : 100;
            if (!GothwaitePvpTactics.SoloFlees(bot.HealthPercent, enemyHealth, attackers, bot.PersistentRecord?.RiskTolerance ?? 50,
                    state.Random.NextDouble()))
                return;
            reason = GothwaitePvpTactics.SoloFleeReason(attackers);
        }
        else
        {
            GothwaitePvpGroupFormer.Vitals vitals = GothwaitePvpGroupFormer.VitalsOf(bot);
            int enemies = GothwaitePvpCombat.EnemiesNear(bot, GothwaitePvpTactics.RetreatEnemyRange);
            if (!GothwaitePvpTactics.GroupRetreats(membership.Doctrine, vitals.Alive, membership.FormedMembers, vitals.AliveHealers,
                    membership.FormedHealers, enemies, state.Random.NextDouble()))
                return;
            reason = GothwaitePvpTactics.GroupRetreatReason(vitals.Alive, membership.FormedMembers, vitals.AliveHealers,
                membership.FormedHealers, enemies);
        }
        BeginRetreat(brain, bot, state, reason, now);
    }

    // The fight is over (stage 6): a hunting solo breaks away on a short outbound leg; an arrived leader rests and gathers its group.
    private static void FightOver(GameBot bot, State state, GothwaitePvpGroupFormer.Membership membership, long now)
    {
        state.FightStartSet = false;
        if (state.Retreating || !membership.IsLeader || !membership.Arrived)
            return;
        if (state.Hunter && membership.Shape == GothwaitePvpShape.Solo)
        {
            // One kill, then leave: a fresh point, no opening on the way, and the leg times out after the break length.
            state.Goal = null;
            state.Spot = null;
            state.DwellUntil = 0;
            state.FirstPick = true;
            BeginOutbound(state, now - (GothwaitePvpArenaRules.OutboundMaxMilliseconds - GothwaitePvpTactics.BreakMilliseconds), "break");
            return;
        }
        BeginRegroup(bot, state, now);
    }

    // The leader (or solo) breaks off: the fight is dropped, the run goes back toward the castle gate (the safe spot every bot knows),
    // and self-defence is off until the run is over. Followers copy the flag and run behind their leader.
    private static void BeginRetreat(BotBrain brain, GameBot bot, State state, string reason, long now)
    {
        state.Retreating = true;
        state.RetreatUntil = now + GothwaitePvpTactics.RetreatMilliseconds(state.Random);
        state.Regrouping = false;
        state.AddLeg = false;
        state.Spot = null;
        state.Claim = null;
        state.DwellUntil = 0;
        GothwaitePvpStats.CountRetreat();
        Log.Info(GothwaitePvpLines.Retreat(bot.Name, reason, Detail(bot)));
        StandDown(brain, bot, state);
        WakeRest(bot);
        SetGoal(state, ExitPoint());
    }

    // The run is over. A leader (or solo) with enough health takes a new outbound leg to a fresh point; one that is still hurt goes
    // home (true). A follower just stops running and follows its leader again.
    private static bool EndRetreat(GameBot bot, State state, GothwaitePvpGroupFormer.Membership membership, long now)
    {
        state.Retreating = false;
        if (bot.IsSprinting)
            bot.Sprint(false);
        if (!membership.IsLeader)
            return false;
        bool resume = GothwaitePvpTactics.ResumesAfterRetreat(bot.HealthPercent);
        Log.Info(GothwaitePvpLines.RetreatEnd(bot.Name, resume ? "resume" : "home", bot.HealthPercent));
        if (!resume)
            return true;
        state.Goal = null;
        state.Spot = null;
        state.DwellUntil = 0;
        state.FirstPick = true;
        BeginOutbound(state, now, "retreat");
        return false;
    }

    // A follower runs while its leader runs (the group's retreat is the leader's): its run ends with its leader's, never on its own clock.
    private static void FollowRetreat(GameBot bot, State state, GothwaitePvpGroupFormer.Membership membership)
    {
        bool retreating = IsRetreating(membership.Leader);
        if (state.Retreating && !retreating && bot.IsSprinting)
            bot.Sprint(false);
        state.Retreating = retreating;
        state.RetreatUntil = long.MaxValue;
    }

    // Sprint while running, as long as there is endurance for it.
    private static void Run(GameBot bot)
    {
        if (!bot.IsSprinting && bot.EndurancePercent > 15)
            bot.Sprint(true);
    }

    private static void BeginRegroup(GameBot bot, State state, long now)
    {
        state.Regrouping = true;
        state.RegroupSince = now;
        state.DwellUntil = 0;
        GothwaitePvpStats.CountRegroup();
        Log.Info(GothwaitePvpLines.Regroup(bot.Name, "start", Detail(bot)));
    }

    private static void EndRegroup(GameBot bot, State state, string reason, long now)
    {
        state.Regrouping = false;
        WakeRest(bot);
        state.Goal = null;
        Log.Info(GothwaitePvpLines.Regroup(bot.Name, reason, Detail(bot)));
    }

    // The leader holds its spot and sits while the group gathers and recovers; the regroup ends when everyone is near and recovered,
    // or when its time runs out. True while it goes on.
    private static bool HoldRegroup(GameBot bot, State state, GothwaitePvpGroupFormer.Membership membership, long now)
    {
        GothwaitePvpGroupFormer.Vitals vitals = GothwaitePvpGroupFormer.VitalsOf(bot, GothwaitePvpTactics.RegroupRange);
        if (GothwaitePvpTactics.RegroupDone(now, state.RegroupSince, vitals.Alive - 1, vitals.Near, vitals.LowestHealth, vitals.LowestPower,
                vitals.AnyPowerUser))
        {
            EndRegroup(bot, state, now - state.RegroupSince >= GothwaitePvpTactics.RegroupMaxMilliseconds ? "time" : "recovered", now);
            return false;
        }
        Rest(bot);
        return true;
    }

    // Sits to recover in the field (the brain wakes the bot when it is hit); a cast in progress finishes first.
    private static void Rest(GameBot bot)
    {
        if (bot.IsRecoveryResting)
            return;
        GothwaitePvpNavigation.Stand(bot);
        if (!bot.IsCasting && !BotRestRecovery.BlocksRest(bot))
            bot.BeginRecoveryRest();
    }

    private static void WakeRest(GameBot bot)
    {
        if (bot.IsRecoveryResting)
            bot.WakeRecoveryRest();
    }

    // Drops the fight without any new goal: the brain forgets its aggro and target, the attack stops, the FSM idles.
    private static void StandDown(BotBrain brain, GameBot bot, State state)
    {
        brain.ClearAggroList();
        bot.StopAttack();
        bot.TargetObject = null;
        brain.FSM.SetCurrentState(eFSMStateType.IDLE);
        ResetProgress(state);
    }

    /// <summary>
    /// A bot outside the castle walks back to its landing. While it fights, the class routine answers (self-defence) and the
    /// walk waits. With no route for a minute it is placed on the landing.
    /// </summary>
    private static bool WalkBack(GameBot bot, State state, long now, bool engaged)
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
        if (engaged)
        {
            state.WalkBackSince = now;
            return true;
        }
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
        state.Returning = false;
        state.InGroup = false;
        ClearArenaWalk(state);
        GothwaitePvpGroupFormer.TakeEndReason(bot);
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

    // A gathering follower in the castle follows its leader; it attacks the leader's target only when that is legal for it.
    private static void Assist(BotBrain brain, GameBot bot, GameBot leader, State state, long now)
    {
        if (leader == null || !leader.IsAlive || leader.CurrentRegion != bot.CurrentRegion)
        {
            GothwaitePvpNavigation.Stand(bot);
            return;
        }
        if (AssistLeader(brain, bot, leader))
            return;
        Point3D follow = new(leader.X, leader.Y, leader.Z);
        if (bot.GetDistanceTo(follow) > GothwaitePvpNavigation.FollowDistance)
            GothwaitePvpNavigation.GoTo(bot, state.Route, follow, now);
        else
            GothwaitePvpNavigation.Stand(bot);
    }

    // A group that ended while the bot was not leaving it. A bot still in the arena logs why it returns, and walks home.
    private static void GroupEnded(GameBot bot, State state, long now)
    {
        string ended = GothwaitePvpGroupFormer.TakeEndReason(bot);
        if (ended == null || state.Returning || InCastle(bot))
            return;
        Log.Info(GothwaitePvpLines.Return(bot.Name, ended, Detail(bot)));
        BeginWalkHome(state, now);
    }

    /// <summary>
    /// The leader (or a solo) that has not reached the castle exit walks there. The exit is a waypoint, not a post (section 6.4, live-run fix 2026-10-13):
    /// once the leader is within the arrival distance of it, or already outside the no-fight ring on its way, the group has arrived and the
    /// leader roams from this turn. The exit's stall window sends it home at once, since it has no other goal.
    /// </summary>
    private static bool WalkToExit(BotBrain brain, GameBot bot, State state, GothwaitePvpGroupFormer.Membership membership, bool engaged, long now)
    {
        Point3D exit = ExitPoint();
        SetGoal(state, exit);
        if (GothwaitePvpArenaRules.GateWalkDone(GothwaitePvpNavigation.Arrived(bot, exit, state.Route), OutsideGateRing(bot)))
        {
            GothwaitePvpGroupFormer.MarkArrived(bot);
            state.Goal = null;
            state.Spot = null;
            state.StuckFailures = 0;
            state.FirstPick = true;
            BeginOutbound(state, now, "gate");
            ResetProgress(state);
            return Roam(brain, bot, state, membership, engaged, now);
        }
        if (engaged)
        {
            ResetProgress(state);
            return true;
        }
        if (GothwaitePvpNavigation.GoTo(bot, state.Route, exit, now))
            state.RouteFailureLogged = false;
        else if (!state.RouteFailureLogged)
        {
            state.RouteFailureLogged = true;
            Log.Info(GothwaitePvpLines.RouteFailed(bot.Name, GothwaitePvpLines.Spot(exit), state.Route.LastReason));
        }
        if (Stalled(bot, state, exit, now, follower: false))
            return StuckAtGoal(brain, bot, state, membership, now, canRepick: false);
        return true;
    }

    /// <summary>
    /// The leader, or a solo, once it has arrived at the castle exit (section 6.4): it keeps its edge, dwells at each patrol
    /// point and then walks to the next one it picks. A fight holds it in place, and the dwell goes on after the fight. The
    /// edge is the group's: the lowest living member's, so no follower is left outside its own edge.
    /// </summary>
    private static bool Roam(BotBrain brain, GameBot bot, State state, GothwaitePvpGroupFormer.Membership membership, bool engaged, long now)
    {
        state.Phase = GothwaitePvpPhase.Arena;
        if (engaged)
        {
            WakeRest(bot);
            ResetProgress(state);
            return true;
        }
        if (state.Retreating)
        {
            // The run: to the goal set when the retreat began, then stand there until the run is over.
            if (state.Goal != null && !GothwaitePvpNavigation.Arrived(bot, state.Goal, state.Route))
                GothwaitePvpNavigation.GoTo(bot, state.Route, state.Goal, now);
            else
                GothwaitePvpNavigation.Stand(bot);
            ResetProgress(state);
            return true;
        }
        if (state.Regrouping && HoldRegroup(bot, state, membership, now))
            return true;
        if (state.Outbound && GothwaitePvpArenaRules.OutboundOver(false, now, state.OutboundSince))
            EndOutbound(bot, state, "time");
        int edge = GroupEdgeOf(bot);
        if (!state.Outbound && !state.AddLeg && now >= state.NextAddCheck)
        {
            state.NextAddCheck = now + GothwaitePvpTactics.AddCheckMilliseconds;
            if (TryAdd(bot, state, edge, now))
                return true;
        }
        if (OutsideEdge(bot, edge))
        {
            if (!KeepEdge(bot, state, edge, now))
            {
                state.Goal = null;
                return StuckAtGoal(brain, bot, state, membership, now, canRepick: false);
            }
        }
        else
            state.EdgeLogged = false;
        if (state.DwellUntil != 0)
        {
            if (now < state.DwellUntil)
            {
                GothwaitePvpNavigation.Stand(bot);
                return true;
            }
            // The dwell is over. The next patrol point is picked below, never the one the bot has just left.
            state.DwellUntil = 0;
            state.Goal = null;
        }
        if (state.Goal == null && !PickPatrol(bot, state, edge, now))
        {
            state.Goal = null;
            return StuckAtGoal(brain, bot, state, membership, now, canRepick: false);
        }
        Point3D goal = state.Goal;
        if (GothwaitePvpNavigation.Arrived(bot, goal, state.Route))
        {
            if (state.Outbound)
                EndOutbound(bot, state, "arrived");
            state.AddLeg = false;
            // A hunting solo waits long at a point, beside the road; everyone else moves on soon.
            state.DwellUntil = now + (state.Hunter && membership.Shape == GothwaitePvpShape.Solo
                ? GothwaitePvpTactics.AmbushDwellMilliseconds(state.Random)
                : GothwaitePvpArenaRules.DwellMilliseconds(state.Random));
            state.StuckFailures = 0;
            ResetProgress(state);
            GothwaitePvpNavigation.Stand(bot);
            return true;
        }
        WakeRest(bot);
        GothwaitePvpNavigation.GoTo(bot, state.Route, goal, now);
        if (Stalled(bot, state, goal, now, follower: false))
            return StuckAtGoal(brain, bot, state, membership, now, canRepick: true);
        return true;
    }

    // The add draw (stage 6): a leader or solo that is not hunting listens for a recent fight nearby, outside the no-fight ring and
    // inside its edge, and walks to it as often as its aggression says. The fight's spot is the goal; it is no patrol point.
    private static bool TryAdd(GameBot bot, State state, int edge, long now)
    {
        if (state.Hunter)
            return false;
        GothwaitePvpArena arena = GothwaitePvpMode.Arena;
        if (!GothwaitePvpStats.TryRecentFightNear(bot.X, bot.Y, GothwaitePvpTactics.AddRange, GothwaitePvpTactics.AddMaxAgeMilliseconds, now,
                out int fightX, out int fightY))
            return false;
        double castleDistance = arena.CastleDistance(fightX, fightY);
        if (castleDistance < arena.MinimumRoamDistance(GothwaitePvpMode.Settings.GateRing) ||
            castleDistance > GothwaitePvpArenaRules.GoalEdge(edge))
            return false;
        int distance = (int)GothwaitePvpArenaRules.Distance2D(bot.X, bot.Y, fightX, fightY);
        if (distance <= 2 * GothwaitePvpNavigation.ArrivalDistance ||
            !GothwaitePvpTactics.JoinsFight(bot.PersistentRecord?.Aggression ?? 50, state.Random.NextDouble()))
            return false;
        state.Spot = null;
        state.Claim = null;
        state.DwellUntil = 0;
        state.AddLeg = true;
        SetGoal(state, new Point3D(fightX, fightY, bot.Z));
        GothwaitePvpStats.CountAdd();
        Log.Info(GothwaitePvpLines.Add(bot.Name, fightX, fightY, distance));
        WakeRest(bot);
        GothwaitePvpNavigation.GoTo(bot, state.Route, state.Goal, now);
        return true;
    }

    /// <summary>
    /// A follower walks with its leader (section 6.4). It attacks the leader's target when that is legal for it, keeps its own
    /// edge, and otherwise follows the leader while the leader stands inside its goal edge and beyond the follow distance. A
    /// walk that moves the bot 96 units in 45 s is not stalled; one that does not sends the bot home, out of the group.
    /// </summary>
    private static bool Follow(BotBrain brain, GameBot bot, State state, GothwaitePvpGroupFormer.Membership membership, bool engaged, long now)
    {
        state.Phase = membership.Arrived ? GothwaitePvpPhase.Arena : GothwaitePvpPhase.Departing;
        if (engaged)
        {
            WakeRest(bot);
            ResetProgress(state);
            return true;
        }
        int edge = EdgeOf(bot);
        bool outside = OutsideEdge(bot, edge);
        // Outside its own edge a follower walks back first; an assist from there would be dropped by the chase limit at once.
        if (!outside && !state.Retreating && AssistLeader(brain, bot, membership.Leader))
        {
            WakeRest(bot);
            ResetProgress(state);
            return true;
        }
        Point3D goal;
        bool slot = false;
        if (outside)
        {
            if (!KeepEdge(bot, state, edge, now))
            {
                state.Goal = null;
                return StuckAtGoal(brain, bot, state, membership, now, canRepick: false);
            }
            goal = state.Goal;
        }
        else
        {
            state.EdgeLogged = false;
            if (!FollowGoal(bot, membership, edge, state.Spot == null ? state.Goal : null, out goal, out slot))
            {
                HoldWithLeader(bot, membership);
                ResetProgress(state);
                return true;
            }
            state.Spot = null;
        }
        // A follower's stall window is its own movement, so a new follow goal does not reopen it.
        SetGoal(state, goal, resetProgress: false);
        if (GothwaitePvpNavigation.Arrived(bot, goal, state.Route))
        {
            HoldWithLeader(bot, membership);
            ResetProgress(state);
            return true;
        }
        WakeRest(bot);
        if (!GothwaitePvpNavigation.GoTo(bot, state.Route, goal, now) && slot && membership.Leader is { } leader)
        {
            // The slot lies off the mesh (a slope, a wall): fall back to the leader's own spot, as before stage 6.
            goal = new Point3D(leader.X, leader.Y, leader.Z);
            SetGoal(state, goal, resetProgress: false);
            GothwaitePvpNavigation.GoTo(bot, state.Route, goal, now);
        }
        if (Stalled(bot, state, goal, now, follower: true))
            return StuckAtGoal(brain, bot, state, membership, now, canRepick: false);
        return true;
    }

    // A follower that stands with its leader sits when the leader's group regroups (stage 6), else it just stands.
    private static void HoldWithLeader(GameBot bot, GothwaitePvpGroupFormer.Membership membership)
    {
        if (IsRegrouping(membership.Leader))
            Rest(bot);
        else
            GothwaitePvpNavigation.Stand(bot);
    }

    /// <summary>
    /// Scans for a legal opening every <see cref="AutonomousBattlegroundDriver.ScanMilliseconds"/>. A leader or a solo always
    /// scans; a follower scans only while its leader has no legal target. A returning bot never scans. True when it attacked.
    /// The scan clock is read first, so a bot that is not due runs no other test and no FindOpponent.
    /// </summary>
    private static bool Scan(BotBrain brain, GameBot bot, State state, GothwaitePvpGroupFormer.Membership membership, long now)
    {
        if (now < state.NextScan || state.Outbound || state.Retreating || state.Regrouping)
            return false;
        // A follower opens nothing while its leader's group rests after a fight (stage 6); it still fights back.
        if (!membership.IsLeader && IsRegrouping(membership.Leader))
            return false;
        bool leaderHasLegalTarget = !membership.IsLeader && MayAttack(bot, membership.Leader?.TargetObject as GameLiving);
        // Inside the no-fight ring no scan runs at all (section 6.2): a bot there opens nothing.
        if (!GothwaitePvpArenaRules.MayScan(true, state.Returning, OutsideEdge(bot, EdgeOf(bot)), membership.IsLeader, leaderHasLegalTarget,
                GothwaitePvpTargetRules.InGateRing(bot)))
            return false;
        state.NextScan = GothwaitePvpArenaRules.NextScanAfter(now);
        // Stage 6: the opening is weighed by the kill order and the group's fight appetite against the target's party (a hunter by its
        // own rule). The group's strength is its living members near the bot.
        (int others, _) = GothwaitePvpGroupFormer.CountAliveOthers(bot, GothwaitePvpArenaRules.NearMemberRange);
        GameLiving opponent;
        bool refused;
        using (GothwaitePvpLoadMeter.MeasureScan())
        using (BotThinkProfiler.Measure(BotThinkPhase.GothwaitePvpScan))
            opponent = GothwaitePvpCombat.FindOpening(bot, AutonomousBattlegroundDriver.OpponentRange, AcceptFor(bot, state), membership.Doctrine,
                others + 1, bot.Level, state.Hunter, now, out refused);
        if (opponent == null)
        {
            if (refused && now >= state.NextRefusedCount)
            {
                state.NextRefusedCount = now + GothwaitePvpTactics.AppetiteBucketMilliseconds;
                GothwaitePvpStats.CountRefusedOpening();
            }
            return false;
        }
        if (state.Hunter)
        {
            GothwaitePvpStats.CountAmbush();
            (int party, _) = GothwaitePvpCombat.Party(PvpCombatant.Resolve(opponent) ?? opponent);
            Log.Info(GothwaitePvpLines.Ambush(bot.Name, opponent.Name, party, opponent.HealthPercent));
        }
        WakeRest(bot);
        brain.Attack(opponent);
        return true;
    }

    // The scan's acceptance test is built once per bot. Only CreateAccept captures the bot, so a scan allocates nothing here.
    private static Func<GameLiving, bool> AcceptFor(GameBot bot, State state) => state.Accept ??= CreateAccept(bot);

    private static Func<GameLiving, bool> CreateAccept(GameBot bot) => target => MayAttack(bot, target);

    /// <summary>
    /// The return rules of section 6.1, for a bot that is not fighting: low health, low power (a caster), a lost leader, or a
    /// wiped group. Null when the bot stays. The group facts are read only for a group member.
    /// </summary>
    private static string ReturnReasonOf(GameBot bot, GothwaitePvpGroupFormer.Membership membership)
    {
        bool solo = membership.Shape == GothwaitePvpShape.Solo;
        bool leaderLost = false;
        bool wiped = false;
        if (!solo)
        {
            (int others, int near) = GothwaitePvpGroupFormer.CountAliveOthers(bot, GothwaitePvpArenaRules.NearMemberRange);
            bool leaderPresent = membership.Leader is { IsAlive: true } leader && InArena(leader);
            leaderLost = !membership.IsLeader && GothwaitePvpArenaRules.FollowerLost(leaderPresent, near);
            wiped = GothwaitePvpArenaRules.Wiped(solo, others);
        }
        return GothwaitePvpArenaRules.ReturnReason(bot.HealthPercent, bot.ManaPercent, bot.MaxMana > 0, leaderLost, wiped);
    }

    /// <summary>
    /// The bot leaves its group and walks back to the castle. A leader hands its group on or ends it (the former decides). The
    /// return line is written first when <paramref name="logReturn"/> is set; a stuck walk has its own line instead.
    /// </summary>
    private static bool GoHome(BotBrain brain, GameBot bot, State state, GothwaitePvpGroupFormer.Membership membership, string reason,
        long now, bool logReturn)
    {
        if (logReturn)
            Log.Info(GothwaitePvpLines.Return(bot.Name, reason, Detail(bot)));
        if (membership.IsLeader)
            GothwaitePvpGroupFormer.Retire(bot, reason);
        else
            GothwaitePvpGroupFormer.Leave(bot, reason);
        BeginWalkHome(state, now);
        return WalkBack(bot, state, now, engaged: false);
    }

    // The bot is on its way back to the castle: it rests and buffs there, and regroups once it is ready.
    private static void BeginWalkHome(State state, long now)
    {
        state.Returning = true;
        state.InGroup = false;
        state.Step = CastleStep.Resting;
        state.Resting = false;
        state.WalkBackLogged = true;
        state.WalkBackSince = now;
        state.RouteFailureLogged = false;
        ClearArenaWalk(state);
    }

    private static void ClearArenaWalk(State state)
    {
        state.Goal = null;
        state.Spot = null;
        state.DwellUntil = 0;
        state.StuckFailures = 0;
        state.EdgeLogged = false;
        state.FirstPick = false;
        state.Outbound = false;
        state.Claim = null;
        state.Retreating = false;
        state.Regrouping = false;
        state.WasEngaged = false;
        state.FightStartSet = false;
        state.AddLeg = false;
        ResetProgress(state);
    }

    /// <summary>
    /// A stalled walk (section 6.4 stuck rule). A patrol goal is refused for five minutes and another is picked, for the first
    /// two stalls in a row; the third stall, or a walk with no patrol goal, sends the bot home. Logged once per event.
    /// </summary>
    private static bool StuckAtGoal(BotBrain brain, GameBot bot, State state, GothwaitePvpGroupFormer.Membership membership, long now, bool canRepick)
    {
        state.StuckFailures++;
        string action = GothwaitePvpArenaRules.StuckAction(state.StuckFailures, canRepick && (state.Spot != null || state.AddLeg));
        Log.Info(GothwaitePvpLines.Stuck(bot.Name, GothwaitePvpLines.Spot(state.Goal), action));
        if (action == GothwaitePvpArenaRules.ActionRepick)
        {
            GothwaitePvpNavigation.MarkUnreachableFor(state.Route, state.Goal, now, GothwaitePvpArenaRules.UnreachableMilliseconds);
            state.Goal = null;
            state.AddLeg = false;
            ResetProgress(state);
            return true;
        }
        return GoHome(brain, bot, state, membership, "stuck", now, logReturn: false);
    }

    // The chase limit (section 6.4). A player the bot fights who stands in the castle circle, or beyond the bot's edge, or the bot
    // itself beyond its edge plus the slack, drops the fight at once. Mobs are the class routine's self-defence and are not in this
    // rule. Returns true when a fight was dropped.
    private static bool ChaseLimit(BotBrain brain, GameBot bot, State state, GothwaitePvpGroupFormer.Membership? membership, long now)
    {
        GothwaitePvpArena arena = GothwaitePvpMode.Arena;
        int edge = EdgeOf(bot);
        double botDistance = bot.CurrentRegionID == arena.RegionId ? arena.CastleDistance(bot.X, bot.Y) : double.PositiveInfinity;
        bool botInRing = GothwaitePvpTargetRules.InGateRing(bot);
        foreach (GameLiving target in FightTargets(bot))
        {
            bool inArena = target.CurrentRegionID == arena.RegionId;
            string reason = GothwaitePvpArenaRules.ChaseLimitReason(
                inArena && arena.InCastle(target.CurrentRegionID, target.X, target.Y),
                inArena ? arena.CastleDistance(target.X, target.Y) : double.PositiveInfinity,
                botDistance,
                edge);
            // The no-fight ring (section 6.2): a fight opened outside it does not go on inside it. An attacker is fought back (self-defence).
            bool targetInRing = GothwaitePvpTargetRules.InGateRing(target);
            if (reason == null && (targetInRing || botInRing))
                reason = GothwaitePvpArenaRules.GateRingReason(targetInRing, botInRing, GothwaitePvpSelfDefence.AttackedUs(bot, target));
            // Stage 6 chase discipline: a follower does not chase beyond its healers, a leader not far from where the fight began,
            // unless the target is about to die. A target that is still hitting the group is not a chase.
            if (reason == null && membership is { } group && !GothwaitePvpSelfDefence.AttackedUs(bot, target))
            {
                if (!group.IsLeader && group.Leader is { IsAlive: true } leader && leader.CurrentRegionID == target.CurrentRegionID &&
                    GothwaitePvpTactics.Overextended(leader.GetDistanceTo(target), target.HealthPercent))
                    reason = GothwaitePvpArenaRules.ChaseOverextended;
                else if (group.IsLeader && state.FightStartSet &&
                    GothwaitePvpTactics.ChasedTooFar(GothwaitePvpArenaRules.Distance2D(bot.X, bot.Y, state.FightStartX, state.FightStartY),
                        target.HealthPercent))
                    reason = GothwaitePvpArenaRules.ChaseFar;
            }
            if (reason == null)
                continue;
            LogChaseOnce(bot, state, target, reason);
            DropFight(brain, bot, state, membership, now, reason);
            return true;
        }
        return false;
    }

    // True while the bot fights. A fight that breaks the castle or edge rules is dropped first, so the rest of the turn sees a
    // bot that is free to walk away.
    private static bool FightNow(BotBrain brain, GameBot bot, State state, GothwaitePvpGroupFormer.Membership? membership, long now)
    {
        if (!Fighting(brain, bot))
            return false;
        if (!ChaseLimit(brain, bot, state, membership, now))
            return true;
        return Fighting(brain, bot);
    }

    // The brain has aggro, or the bot attacks a living target within its combat time.
    private static bool Fighting(BotBrain brain, GameBot bot) =>
        brain.HasAggro || bot.InCombat && bot.TargetObject is GameLiving { IsAlive: true };

    // The player-shaped living targets of a fight. The current target is the fight. Until the bot has one (the FSM picks it a turn
    // after an attack), the players and companions that are attacking it stand in, so a hitter outside the rules is dropped first.
    private static List<GameLiving> FightTargets(GameBot bot)
    {
        var targets = new List<GameLiving>(4);
        if (bot.TargetObject is GameLiving { IsAlive: true } current)
        {
            if (current != bot && PvpCombatant.IsPlayerShaped(current))
                targets.Add(current);
            return targets;
        }
        if (bot.attackComponent?.AttackerTracker is { } tracker)
            foreach (GameLiving attacker in tracker.Attackers)
                if (attacker is { IsAlive: true } && attacker != bot && PvpCombatant.IsPlayerShaped(attacker))
                    targets.Add(attacker);
        return targets;
    }

    // Drops the fight: the brain forgets its aggro and target, the attack stops, and the FSM stands down to idle. The RvR and
    // stuck recovery do the same; BotBrain.Disengage is not used because it sends the bot to follow. A walking leader or solo
    // heads for the nearest patrol point inside its edge after an edge drop. After a drop at the castle or the no-fight ring it starts
    // a new outbound leg to a freshly picked point (picked by Roam on this turn), so it walks away from the gate instead of back to
    // the patrol point next to it; one already outbound keeps its goal.
    private static void DropFight(BotBrain brain, GameBot bot, State state, GothwaitePvpGroupFormer.Membership? membership, long now,
        string reason)
    {
        brain.ClearAggroList();
        bot.StopAttack();
        bot.TargetObject = null;
        brain.FSM.SetCurrentState(eFSMStateType.IDLE);
        ResetProgress(state);
        if (membership is not { IsLeader: true, Arrived: true })
            return;
        state.DwellUntil = 0;
        if (GothwaitePvpArenaRules.DropSendsOutbound(reason))
        {
            if (state.Outbound)
                return;
            state.Goal = null;
            state.FirstPick = true;
            BeginOutbound(state, now, reason);
            return;
        }
        if (WalkBackSpot(bot, state, GroupEdgeOf(bot), now) is { } spot)
            SetSpot(state, spot);
    }

    // The outbound leg starts: the bot opens no fight until it reaches the point it picks next (or the time cap runs out).
    private static void BeginOutbound(State state, long now, string reason)
    {
        state.Outbound = true;
        state.OutboundSince = now;
        state.OutboundReason = reason;
    }

    private static void EndOutbound(GameBot bot, State state, string reason)
    {
        state.Outbound = false;
        Log.Info(GothwaitePvpLines.OutboundEnd(bot.Name, reason, (int)GothwaitePvpMode.Arena.CastleDistance(bot.X, bot.Y)));
    }

    // A follower is outbound while its arrived leader is: the group's openings are the leader's.
    private static void FollowOutbound(State state, GothwaitePvpGroupFormer.Membership membership, long now)
    {
        bool outbound = membership.Arrived && IsOutbound(membership.Leader);
        if (outbound && !state.Outbound)
            state.OutboundSince = now;
        state.Outbound = outbound;
    }

    private static void LogChaseOnce(GameBot bot, State state, GameLiving target, string reason)
    {
        state.ChaseLogged ??= new HashSet<int>();
        if (state.ChaseLogged.Count >= ChaseLogLimit)
            state.ChaseLogged.Clear();
        if (state.ChaseLogged.Add(target.ObjectID))
            Log.Info(GothwaitePvpLines.Chase(bot.Name, target.Name, reason));
    }

    // A leader or solo picks its next patrol point: weighted by heat (capped) and a little by distance, never the point it has just
    // left, never a point refused for five minutes, and never outside its goal edge. The first pick of an outbound leg ignores heat
    // and distance, so the groups spread (section 6.4, live-run fix 2026-10-13). Every pick also divides by 1 + the other groups
    // already at or heading to the point (second live run). False when none is left.
    private static bool PickPatrol(GameBot bot, State state, int edge, long now)
    {
        GothwaitePvpArena arena = GothwaitePvpMode.Arena;
        bool firstPick = state.FirstPick;
        Dictionary<string, int> claims = ClaimCounts(state);
        GothwaitePvpSpot? chosen = GothwaitePvpArenaRules.ChoosePatrolPoint(arena, GothwaitePvpArenaRules.GoalEdge(edge), PatrolPoints(bot),
            state.Spot,
            point => !GothwaitePvpNavigation.IsUnreachable(state.Route, PointOf(point), now),
            point => GothwaitePvpArenaRules.RoamWeight(firstPick, firstPick ? 0 : GothwaitePvpStats.Heat(point, now),
                GothwaitePvpArenaRules.Distance2D(bot.X, bot.Y, point.X, point.Y), claims.GetValueOrDefault(point.Name)),
            state.Random);
        if (chosen is not { } spot)
            return false;
        state.FirstPick = false;
        SetSpot(state, spot);
        state.Claim = spot.Name;
        if (state.Outbound)
            Log.Info(GothwaitePvpLines.Outbound(bot.Name, spot.Name, (int)arena.CastleDistance(spot.X, spot.Y),
                firstPick ? state.OutboundReason : "repick"));
        return true;
    }

    // The patrol points other leaders and solos have claimed, counted by name. A released or returning bot has cleared its claim.
    // The table is enumerated lock-free (safe alongside other bots' turns); a pick happens once per patrol leg, so the cost is small.
    private static Dictionary<string, int> ClaimCounts(State self)
    {
        var claims = new Dictionary<string, int>();
        foreach (KeyValuePair<GameBot, State> entry in States)
        {
            if (entry.Value == self || entry.Value.Claim is not { } name ||
                entry.Key is not { IsAlive: true, ObjectState: GameObject.eObjectState.Active })
                continue;
            claims[name] = claims.GetValueOrDefault(name) + 1;
        }
        return claims;
    }

    // Outside its edge, a bot walks back to the nearest usable patrol point inside its goal edge. Logged once per excursion.
    private static bool KeepEdge(GameBot bot, State state, int edge, long now)
    {
        if (!state.EdgeLogged)
        {
            state.EdgeLogged = true;
            Log.Info(GothwaitePvpLines.Edge(bot.Name, (int)GothwaitePvpMode.Arena.CastleDistance(bot.X, bot.Y), edge));
        }
        if (WalkBackSpot(bot, state, edge, now) is not { } spot)
            return false;
        SetSpot(state, spot);
        return true;
    }

    private static GothwaitePvpSpot? WalkBackSpot(GameBot bot, State state, int edge, long now) =>
        GothwaitePvpArenaRules.WalkBackPoint(GothwaitePvpMode.Arena, edge, PatrolPoints(bot), bot.X, bot.Y,
            point => !GothwaitePvpNavigation.IsUnreachable(state.Route, PointOf(point), now));

    // The point a follower walks to: its travel slot behind its leader (stage 6: column or loose by the group's doctrine; a clump
    // doctrine keeps the leader's own spot), when the leader stands inside the follower's goal edge and the follower is beyond the
    // follow distance of it. The previous follow goal is kept while the slot stays within the arrival distance of it, so a moving
    // leader does not make the route be proved again every turn. False to stand where it is. <paramref name="slot"/> says the goal
    // is a formation slot (the walk falls back to the leader's spot when the slot is off the mesh).
    private static bool FollowGoal(GameBot bot, GothwaitePvpGroupFormer.Membership membership, int edge, Point3D previous, out Point3D goal,
        out bool slot)
    {
        goal = null;
        slot = false;
        GameBot leader = membership.Leader;
        if (leader is not { IsAlive: true } || leader.CurrentRegionID != bot.CurrentRegionID)
            return false;
        GothwaitePvpArena arena = GothwaitePvpMode.Arena;
        if (leader.CurrentRegionID != arena.RegionId ||
            GothwaitePvpArenaRules.OutsideEdge(arena.CastleDistance(leader.X, leader.Y), GothwaitePvpArenaRules.GoalEdge(edge)))
            return false;
        Point3D at = FormationSpot(bot, leader, membership, out slot);
        int hold = slot ? GothwaitePvpNavigation.FollowDistance / 2 : GothwaitePvpNavigation.FollowDistance;
        if (bot.GetDistanceTo(at) <= hold)
            return false;
        bool keep = previous != null && GothwaitePvpArenaRules.Distance2D(previous.X, previous.Y, at.X, at.Y) <= AutonomousBattlegroundDriver.ArrivalDistance;
        goal = keep ? previous : at;
        return true;
    }

    // The follower's travel slot: by the group's travel shape and the follower's place in the marching order (guards and melee first,
    // healers in the middle, casters and archers behind), relative to the leader's facing. The leader's spot for a clump doctrine.
    private static Point3D FormationSpot(GameBot bot, GameBot leader, GothwaitePvpGroupFormer.Membership membership, out bool slot)
    {
        slot = false;
        RvrTravelShape shape = membership.Doctrine?.Travel ?? RvrTravelShape.Clump;
        if (shape == RvrTravelShape.Clump)
            return new Point3D(leader.X, leader.Y, leader.Z);
        (int index, int count) = GothwaitePvpGroupFormer.MarchSlot(bot);
        if (index < 0)
            return new Point3D(leader.X, leader.Y, leader.Z);
        System.Numerics.Vector3 point = GothwaitePvpTactics.FormationPoint(shape, index, count,
            new System.Numerics.Vector3(leader.X, leader.Y, leader.Z), GothwaitePvpTactics.Forward(leader.Heading), BotIdOf(bot));
        slot = true;
        return new Point3D((int)point.X, (int)point.Y, leader.Z);
    }

    // A follower attacks its leader's target when the follower may fight it, visible and in range: the leader's fight is the group's.
    // "May fight" is a legal opening or self-defence (the target attacked the group), so an outbound group, which opens nothing, still
    // fights back together.
    private static bool AssistLeader(BotBrain brain, GameBot bot, GameBot leader)
    {
        if (leader is not { IsAlive: true } || leader.TargetObject is not GameLiving target)
            return false;
        if (!GothwaitePvpSelfDefence.MayFight(bot, target) || !BotSiegeRuntime.Visible(bot, target) ||
            !bot.IsWithinRadius(target, AutonomousBattlegroundDriver.OpponentRange))
            return false;
        brain.Attack(target);
        return true;
    }

    private static void SetSpot(State state, GothwaitePvpSpot spot)
    {
        state.Spot = spot;
        state.DwellUntil = 0;
        SetGoal(state, PointOf(spot));
    }

    // A new goal forgets the last route proof, and (unless the caller says otherwise) opens a fresh progress window. The same goal
    // is walked on as it was.
    private static void SetGoal(State state, Point3D goal, bool resetProgress = true)
    {
        if (state.Goal != null && state.Goal.X == goal.X && state.Goal.Y == goal.Y && state.Goal.Z == goal.Z)
            return;
        state.Goal = goal;
        state.Route.RouteGoal = null;
        state.Route.RouteOk = false;
        state.Route.RouteFinal = false;
        if (resetProgress)
            ResetProgress(state);
    }

    // True when the walk has run its progress window without progress: a leader must come 96 units closer to its goal, a follower
    // must move 96 units, within 45 s of game time. The window opens at the first call after a goal is set or a fight ends.
    private static bool Stalled(GameBot bot, State state, Point3D goal, long now, bool follower)
    {
        double distance = GothwaitePvpArenaRules.Distance2D(bot.X, bot.Y, goal.X, goal.Y);
        if (!state.ProgressSet)
        {
            StartWindow(bot, state, distance, now);
            return false;
        }
        bool progressed = follower
            ? GothwaitePvpArenaRules.MovedEnough(GothwaitePvpArenaRules.Distance2D(bot.X, bot.Y, state.ProgressX, state.ProgressY))
            : GothwaitePvpArenaRules.MadeProgress(distance, state.ProgressDistance);
        if (progressed)
        {
            StartWindow(bot, state, distance, now);
            return false;
        }
        return GothwaitePvpArenaRules.ProgressWindowElapsed(now, state.ProgressAt);
    }

    private static void StartWindow(GameBot bot, State state, double distance, long now)
    {
        state.ProgressSet = true;
        state.ProgressDistance = distance;
        state.ProgressX = bot.X;
        state.ProgressY = bot.Y;
        state.ProgressAt = now;
    }

    private static void ResetProgress(State state) => state.ProgressSet = false;

    private static bool MayAttack(GameBot bot, GameLiving target) => GothwaitePvpTargetRules.MayOpen(bot, target);

    private static Point3D ExitPoint()
    {
        GothwaitePvpSpot exit = GothwaitePvpMode.Arena.CastleExit;
        return new Point3D(exit.X, exit.Y, exit.Z);
    }

    private static Point3D PointOf(GothwaitePvpSpot spot) => new(spot.X, spot.Y, spot.Z);

    // The cache returns the same points as GothwaitePvpArena.PatrolPointsFor, without a new list on every pick and edge check.
    private static IReadOnlyList<GothwaitePvpSpot> PatrolPoints(GameBot bot) =>
        GothwaitePvpPatrolCache.PointsFor(GothwaitePvpMode.Arena, GothwaitePvpMode.Settings, bot.Level);

    private static int EdgeOf(GameBot bot) => GothwaitePvpArena.EdgeFor(bot.Level, GothwaitePvpMode.Settings);

    // A leader keeps its group inside the edge of its lowest living member (a solo, its own edge).
    private static int GroupEdgeOf(GameBot leader) =>
        GothwaitePvpArena.EdgeFor(GothwaitePvpGroupFormer.LowestAliveLevel(leader), GothwaitePvpMode.Settings);

    private static bool OutsideEdge(GameBot bot, int edge) =>
        bot.CurrentRegionID != GothwaitePvpMode.Arena.RegionId ||
        GothwaitePvpArenaRules.OutsideEdge(GothwaitePvpMode.Arena.CastleDistance(bot.X, bot.Y), edge);

    // In the arena's region and outside the castle circle.
    private static bool InArena(GameBot bot) =>
        bot.CurrentRegionID == GothwaitePvpMode.Arena.RegionId && !InCastle(bot);

    // In the arena's region and outside the no-fight ring: the walk out of the castle has passed the gate.
    private static bool OutsideGateRing(GameBot bot) =>
        bot.CurrentRegionID == GothwaitePvpMode.Arena.RegionId && !GothwaitePvpTargetRules.InGateRing(bot);

    private static bool InCastle(GameBot bot) => GothwaitePvpMode.Arena.InCastle(bot.CurrentRegionID, bot.X, bot.Y);

    private static int DeathsOf(GameBot bot) => bot.PersistentRecord?.DeathCount ?? 0;

    private static long BotIdOf(GameBot bot) => bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;

    private static string Detail(GameBot bot) => $"health={bot.HealthPercent} power={bot.ManaPercent}";

    private static string Lower(bool value) => value ? "true" : "false";
}
