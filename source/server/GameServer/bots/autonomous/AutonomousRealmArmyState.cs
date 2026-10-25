using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// The attack plan of each realm in the RvR world (package R4b), the world counterpart of the open BG plans
/// (owner request 2026-10-18): single (one siege at a time), double (two at once) or opportunist (waits for another
/// realm's keep fight and joins it as the third realm). A realm keeps its plan for about an hour of game time, as a
/// realm's mood for an evening, then draws the next one with the open BG odds (40 / 30 / 30). Only the RvR world asks.
/// </summary>
public static class AutonomousRealmArmyPlans
{
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousRealmArmyPlans));
    private static readonly object Sync = new();
    private static readonly Dictionary<eRealm, (OpenBgAttackPlan Plan, long Until)> Plans = new();

    /// <summary>The plan source; tests replace it.</summary>
    public static Func<Random> RandomSource = () => System.Random.Shared;

    /// <summary>The realm's current plan, drawing a new one when the last has run out.</summary>
    public static OpenBgAttackPlan PlanFor(eRealm realm, long nowTick)
    {
        if (!RealmTravel.IsPlayable(realm)) return OpenBgAttackPlan.Single;
        lock (Sync)
        {
            if (Plans.TryGetValue(realm, out var current) && current.Until > nowTick) return current.Plan;
            Random rng = RandomSource();
            OpenBgAttackPlan plan = OpenBattlegroundDirector.DrawPlan(rng);
            long until = nowTick + AutonomousRealmArmyPolicy.PlanMilliseconds(AutonomousRealmArmyProperties.RVR_ARMY_PLAN_MINUTES, rng.NextDouble());
            Plans[realm] = (plan, until);
            if (Log.IsInfoEnabled)
                Log.Info($"RVR_REALM_PLAN realm={GlobalConstants.RealmToName(realm)} plan={plan} " +
                    $"previous={(current.Until > 0 ? current.Plan.ToString() : "none")} minutes={(until - nowTick) / 60_000}");
            return plan;
        }
    }

    /// <summary>Test hook: fix a realm's plan until the given tick.</summary>
    public static void SetForTests(eRealm realm, OpenBgAttackPlan plan, long untilTick)
    {
        lock (Sync) Plans[realm] = (plan, untilTick);
    }

    public static void Clear()
    {
        lock (Sync) Plans.Clear();
    }
}

/// <summary>
/// Invaders per realm in the RvR world: living enemy players and world bots standing in a realm's home frontier
/// region. Refreshed at most every thirty seconds by whichever bot asks first, so the live reserve follows the
/// situation without a census per decision.
/// </summary>
public static class AutonomousRealmArmyCensus
{
    public const long RefreshMilliseconds = 30_000;
    private static long _nextRefresh;
    private static int _refreshing;
    private static int[] _invaders = new int[4];

    /// <summary>Enemies seen in <paramref name="realm"/>'s home frontier at the last census.</summary>
    public static int Invaders(eRealm realm, long nowTick)
    {
        if (!RealmTravel.IsPlayable(realm)) return 0;
        if (nowTick >= Interlocked.Read(ref _nextRefresh) && Interlocked.Exchange(ref _refreshing, 1) == 0)
        {
            try
            {
                Interlocked.Exchange(ref _nextRefresh, nowTick + RefreshMilliseconds);
                Volatile.Write(ref _invaders, Count());
            }
            catch (Exception)
            {
                // A census is advisory; a failure keeps the previous counts.
            }
            finally { Volatile.Write(ref _refreshing, 0); }
        }
        return Volatile.Read(ref _invaders)[(int)realm];
    }

    /// <summary>The last census without refreshing it (event-layer decisions; the controller refreshes).</summary>
    public static int Current(eRealm realm) => RealmTravel.IsPlayable(realm) ? Volatile.Read(ref _invaders)[(int)realm] : 0;

    /// <summary>One actor as the census sees it.</summary>
    public readonly record struct Actor(eRealm Realm, ushort Region, bool Alive);

    /// <summary>
    /// Pure gathering: every world bot, every human player and every active companion of a human (companions fight
    /// as their owner's realm, so the caller passes their alliance realm).
    /// </summary>
    public static IEnumerable<(eRealm Realm, ushort Region, bool Alive)> Actors(IEnumerable<Actor> worldBots,
        IEnumerable<(Actor Human, IEnumerable<Actor> Companions)> humans)
    {
        foreach (Actor bot in worldBots ?? [])
            yield return (bot.Realm, bot.Region, bot.Alive);
        foreach (var (human, companions) in humans ?? [])
        {
            yield return (human.Realm, human.Region, human.Alive);
            foreach (Actor companion in companions ?? [])
                yield return (companion.Realm, companion.Region, companion.Alive);
        }
    }

    /// <summary>Pure count: one per living actor of another realm standing in a realm's home frontier region.</summary>
    public static int[] Count(IEnumerable<(eRealm Realm, ushort Region, bool Alive)> actors)
    {
        int[] counts = new int[4];
        foreach (var actor in actors ?? [])
        {
            if (!actor.Alive || !RealmTravel.IsPlayable(actor.Realm)) continue;
            foreach (eRealm home in RealmTravel.PlayableRealms)
                if (home != actor.Realm && actor.Region == AutonomousRealmArmyPolicy.HomeFrontierRegion(home))
                    counts[(int)home]++;
        }
        return counts;
    }

    private static int[] Count()
    {
        Actor[] bots = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && bot.ObjectState == GameObject.eObjectState.Active)
            .Select(bot => new Actor(bot.Realm, bot.CurrentRegionID, bot.IsAlive)).ToArray();
        // The playing clients (GameLoop.GetListForTick is an empty scratch list), with each human's active
        // companions; companions count as their owner's realm.
        var humans = (ClientService.Instance?.GetPlayers() ?? new List<GamePlayer>())
            .Where(player => player?.ObjectState == GameObject.eObjectState.Active)
            .Select(player => (new Actor(PvpCombatant.AllianceRealm(player), player.CurrentRegionID, player.IsAlive),
                (IEnumerable<Actor>)PlayerCompanionRoster.GetActiveCompanions(player)
                    .Where(companion => companion?.ObjectState == GameObject.eObjectState.Active)
                    .Select(companion => new Actor(PvpCombatant.AllianceRealm(companion), companion.CurrentRegionID, companion.IsAlive))
                    .ToArray()))
            .ToArray();
        return Count(Actors(bots, humans));
    }

    public static void SetForTests(eRealm realm, int invaders, long untilTick)
    {
        int[] counts = (int[])Volatile.Read(ref _invaders).Clone();
        counts[(int)realm] = invaders;
        Volatile.Write(ref _invaders, counts);
        Interlocked.Exchange(ref _nextRefresh, untilTick);
    }

    public static void Clear()
    {
        Volatile.Write(ref _invaders, new int[4]);
        Interlocked.Exchange(ref _nextRefresh, 0);
    }
}

/// <summary>
/// Revenge trips in the RvR world (open point of R4a): a grudge is realm-wide, so without a cap every warband of a
/// realm would ride after the same killer. A warband takes a lease on a grudge target for ten minutes (renewed
/// while it keeps hunting it); at most <see cref="AutonomousRealmArmyProperties.RVR_GRUDGE_HUNTERS_PER_TARGET"/>
/// warbands of a realm hold a lease on one target. Every RvR destination pick is counted per realm, and every ten
/// game minutes one <c>RVR_GRUDGE_SHARE</c> line per realm shows how many picks were revenge trips and how many a
/// full target turned away.
/// </summary>
public static class AutonomousRealmGrudgeHunts
{
    public const long LeaseMilliseconds = 10 * 60_000L;
    public const long ReportMilliseconds = 10 * 60_000L;
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousRealmGrudgeHunts));
    private static readonly object Sync = new();
    private static readonly Dictionary<(eRealm Realm, string Target), Dictionary<string, long>> Leases = new();
    private static readonly long[,] Counts = new long[4, 3];
    private static long _nextReport;

    public readonly record struct Share(long Picks, long Grudges, long Capped)
    {
        public double GrudgeShare => Picks == 0 ? 0 : (double)Grudges / Picks;
    }

    private static int HuntersLocked(eRealm realm, string target, string force, long nowTick, out bool alreadyHunting)
    {
        alreadyHunting = false;
        if (!Leases.TryGetValue((realm, target), out var forces)) return 0;
        foreach (string stale in forces.Where(pair => pair.Value <= nowTick).Select(pair => pair.Key).ToArray())
            forces.Remove(stale);
        alreadyHunting = force != null && forces.ContainsKey(force);
        return forces.Count;
    }

    /// <summary>Whether this warband may (still) hunt the target.</summary>
    public static bool HasRoom(eRealm realm, string target, string force, long nowTick, int cap)
    {
        if (string.IsNullOrEmpty(target)) return false;
        lock (Sync)
            return AutonomousRealmArmyPolicy.HasGrudgeRoom(HuntersLocked(realm, target, force, nowTick, out bool hunting), hunting, cap);
    }

    /// <summary>Takes or renews the warband's lease on the target; false (and counted as capped) when it is full.</summary>
    public static bool TryClaim(eRealm realm, string target, string force, long nowTick, int cap)
    {
        if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(force) || !RealmTravel.IsPlayable(realm)) return false;
        lock (Sync)
        {
            int hunters = HuntersLocked(realm, target, force, nowTick, out bool hunting);
            if (!AutonomousRealmArmyPolicy.HasGrudgeRoom(hunters, hunting, cap))
            {
                Counts[(int)realm, 2]++;
                return false;
            }
            // A warband hunts one grudge at a time.
            foreach (var entry in Leases.Where(pair => pair.Key.Realm == realm && pair.Key.Target != target))
                entry.Value.Remove(force);
            if (!Leases.TryGetValue((realm, target), out var forces)) Leases[(realm, target)] = forces = new(StringComparer.Ordinal);
            forces[force] = nowTick + LeaseMilliseconds;
            return true;
        }
    }

    /// <summary>A pick that skipped at least one grudge target because its realm's hunters were full.</summary>
    public static void NoteCapped(eRealm realm)
    {
        if (!RealmTravel.IsPlayable(realm)) return;
        lock (Sync) Counts[(int)realm, 2]++;
    }

    /// <summary>Counts one RvR destination pick of a realm; a revenge trip when <paramref name="grudge"/>.</summary>
    public static void NotePick(eRealm realm, bool grudge, long nowTick)
    {
        if (!RealmTravel.IsPlayable(realm)) return;
        string report = null;
        lock (Sync)
        {
            Counts[(int)realm, 0]++;
            if (grudge) Counts[(int)realm, 1]++;
            if (nowTick >= _nextReport)
            {
                bool first = _nextReport == 0;
                _nextReport = nowTick + ReportMilliseconds;
                if (!first) report = string.Join(" | ", RealmTravel.PlayableRealms.Select(each =>
                {
                    Share share = SnapshotLocked(each);
                    return $"realm={GlobalConstants.RealmToName(each)} picks={share.Picks} grudge={share.Grudges} " +
                        $"capped={share.Capped} share={share.GrudgeShare.ToString("0.000", CultureInfo.InvariantCulture)}";
                }));
            }
        }
        if (report != null && Log.IsInfoEnabled)
            Log.Info($"RVR_GRUDGE_SHARE cap={AutonomousRealmArmyProperties.RVR_GRUDGE_HUNTERS_PER_TARGET} {report}");
    }

    /// <summary>Running totals since start.</summary>
    public static Share Snapshot(eRealm realm)
    {
        lock (Sync) return SnapshotLocked(realm);
    }

    private static Share SnapshotLocked(eRealm realm) => RealmTravel.IsPlayable(realm)
        ? new(Counts[(int)realm, 0], Counts[(int)realm, 1], Counts[(int)realm, 2])
        : default;

    public static void Clear()
    {
        lock (Sync)
        {
            Leases.Clear();
            Array.Clear(Counts);
            _nextReport = 0;
        }
    }
}
