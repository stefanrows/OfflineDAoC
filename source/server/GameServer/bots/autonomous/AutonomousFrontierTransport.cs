using System;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Scripts;

namespace DOL.GS;

/// <summary>Bot passengers on the existing Old Frontiers porter's cast cycle.</summary>
public static class AutonomousFrontierTransport
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<OFTeleporter, byte> Porters = new();
    public static void Register(OFTeleporter porter) => Porters[porter] = 0;
    public static void Unregister(OFTeleporter porter) => Porters.TryRemove(porter, out _);
    public static OFTeleporter NearestPorter(GameBot bot) => Porters.Keys
        .Where(p => p.ObjectState == GameObject.eObjectState.Active && p.CurrentRegion == bot.CurrentRegion)
        .OrderBy(bot.GetDistanceTo).FirstOrDefault();

    /// <summary>The warband that ports together. RvR groups, including those
    /// answering a committed siege, board as one party (1.65: a group waited a
    /// minute at the porter for its stragglers, then ported together).</summary>
    public static GameBot[] BoardingParty(GameBot bot, Passage passage) =>
        passage.Medallion == "home_necklace" &&
        AutonomousObjectiveAssignments.Parse(bot.PersistentRecord?.ObjectiveKind) != eAutonomousObjectiveKind.RvR
            ? [bot] // Initial PvE meetups can have members returning from different frontiers.
            : bot.Group?.GetMembersInTheGroup().OfType<GameBot>().ToArray() ?? [bot];

    /// <summary>How long a warband holds the departure for members on their way.</summary>
    public const int MusterWaitMilliseconds = 60_000;
    /// <summary>Members farther than this (about half a minute's run) from the
    /// porter are not waited for; they follow on a later departure.</summary>
    public const int MusterRadius = 6_000;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> Musters = new(StringComparer.Ordinal);

    public enum BoardingDecision { Wait, Board }

    /// <summary>Board once nobody is still on the way, or once the first ready
    /// member has waited <see cref="MusterWaitMilliseconds"/>.</summary>
    public static BoardingDecision DecideBoarding(int readyCount, int incomingCount, long musterStartedTick, long nowTick) =>
        readyCount <= 0 ? BoardingDecision.Wait :
        incomingCount <= 0 || nowTick - musterStartedTick >= MusterWaitMilliseconds ? BoardingDecision.Board : BoardingDecision.Wait;

    /// <summary>A member worth waiting for: alive, same region, not yet across,
    /// and close enough to arrive within the muster window.</summary>
    public static bool IsIncoming(bool alive, bool sameRegion, bool alreadyAcross, double distanceToPorter) =>
        alive && sameRegion && !alreadyAcross && distanceToPorter <= MusterRadius;

    /// <summary>Tick the force's first ready member started waiting; stale
    /// musters (older than five minutes) restart.</summary>
    public static long MusterStart(string forceId, ushort region, long nowTick)
    {
        string key = $"{forceId}:{region}";
        long started = Musters.AddOrUpdate(key, nowTick, (_, previous) => nowTick - previous > 5 * 60_000L ? nowTick : previous);
        if (Musters.Count > 512)
            foreach (var stale in Musters.Where(pair => nowTick - pair.Value > 5 * 60_000L).ToArray())
                Musters.TryRemove(stale.Key, out _);
        return started;
    }

    public static void EndMuster(string forceId, ushort region) => Musters.TryRemove($"{forceId}:{region}", out _);

    // ---- Regroup after release (docs/BUGS.md 66) ---------------------------
    // Live 0.125.0: 7,558 departures in 11 h, single warbands every 3-4 min,
    // 70 % of their departures one member alone. A 2003 group that lost
    // people in the field released to the border keep, rezzed, rebuffed and
    // went back out together a few minutes later.

    /// <summary>Nobody boards within this time of their own release (60-90 s).</summary>
    public const int ReleaseHoldMilliseconds = 75_000;
    /// <summary>A warband whose member released waits up to this long (from the
    /// first waiting member's release) for everyone alive to gather.</summary>
    public const int RegroupWindowMilliseconds = 180_000;
    /// <summary>"Gathered": alive, in the porter's region and this close to it.</summary>
    public const int RegroupRadius = 1_500;
    /// <summary>At most one outbound departure per warband in this time.</summary>
    public const int ForceDepartureIntervalMilliseconds = 300_000;
    /// <summary>Members of the same departure split over transfer slices, or a
    /// straggler of that muster, may still follow within this time.</summary>
    public const int DepartureContinuationMilliseconds = 20_000;
    public const string ReleaseTickKey = "RvrReleaseTick";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> Departures = new(StringComparer.Ordinal);

    public enum RegroupDecision { Board, ReleaseHold, Regroup, DepartureCap }

    /// <summary>
    /// Whether the members ready at the porter may leave now. Home passages
    /// are never held. Every bot waits <see cref="ReleaseHoldMilliseconds"/>
    /// after its own release. A warband with a released member at the porter
    /// waits until all its members are alive and gathered, or until the first
    /// of them has waited <see cref="RegroupWindowMilliseconds"/>. A warband
    /// leaves at most once per <see cref="ForceDepartureIntervalMilliseconds"/>
    /// (continuations of that departure excepted); a keep-defence call skips
    /// only this cap. Solo bots are held only by their own release.
    /// </summary>
    public static RegroupDecision DecideRegroup(bool outbound, bool warband, long? earliestReadyRelease,
        long? latestReadyRelease, bool partyGathered, long? lastForceDeparture, bool defenderPriority, long nowTick)
    {
        if (!outbound)
            return RegroupDecision.Board;
        if (latestReadyRelease is long latest && nowTick - latest < ReleaseHoldMilliseconds)
            return RegroupDecision.ReleaseHold;
        if (!warband)
            return RegroupDecision.Board;
        if (lastForceDeparture is long current && nowTick - current <= DepartureContinuationMilliseconds)
            return RegroupDecision.Board; // the rest of a departure already under way
        if (earliestReadyRelease is long earliest && nowTick - earliest < RegroupWindowMilliseconds && !partyGathered)
            return RegroupDecision.Regroup;
        if (!defenderPriority && lastForceDeparture is long last &&
            nowTick - last > DepartureContinuationMilliseconds && nowTick - last < ForceDepartureIntervalMilliseconds)
            return RegroupDecision.DepartureCap;
        return RegroupDecision.Board;
    }

    /// <summary>Records a departure; a continuation keeps its original start.</summary>
    public static void RecordDeparture(string forceId, long nowTick)
    {
        if (string.IsNullOrEmpty(forceId)) return;
        Departures.AddOrUpdate(forceId, nowTick,
            (_, previous) => nowTick - previous <= DepartureContinuationMilliseconds ? previous : nowTick);
        if (Departures.Count > 1024)
            foreach (var stale in Departures.Where(pair => nowTick - pair.Value > 2L * ForceDepartureIntervalMilliseconds).ToArray())
                Departures.TryRemove(stale.Key, out _);
    }

    public static long? LastDeparture(string forceId) =>
        !string.IsNullOrEmpty(forceId) && Departures.TryGetValue(forceId, out long tick) ? tick : null;

    public static void ForgetDeparture(string forceId) => Departures.TryRemove(forceId ?? string.Empty, out _);

    /// <summary>The release tick that still matters for boarding, or null.</summary>
    public static long? RecentRelease(long? releaseTick, long nowTick) =>
        releaseTick is long tick && tick > 0 && nowTick - tick < RegroupWindowMilliseconds ? tick : null;

    /// <summary>Stamped when an autonomous RvR bot releases (any cause).</summary>
    public static void NoteRelease(GameBot bot)
    {
        if (bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper &&
            AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR))
            bot.TempProperties.SetProperty(ReleaseTickKey, Math.Max(1, GameLoop.GameLoopTime));
    }

    public static long? RecentRelease(GameBot bot, long nowTick)
    {
        long tick = bot?.TempProperties.GetProperty<long>(ReleaseTickKey) ?? 0;
        return RecentRelease(tick > 0 ? tick : null, nowTick);
    }

    private static bool Gathered(GameBot[] party, OFTeleporter porter) =>
        party.All(member => member.IsAlive && member.CurrentRegion == porter.CurrentRegion &&
            member.IsWithinRadius(porter, RegroupRadius));

    /// <summary>
    /// One porter pass for one warband: the members that board now, or none
    /// while the force still waits (up to <see cref="MusterWaitMilliseconds"/>)
    /// for members on their way. Ends the muster when it boards.
    /// </summary>
    public static T[] SelectBoarders<T>(T[] party, Func<T, bool> ready, Func<T, bool> incoming,
        string forceId, ushort region, long nowTick, out T[] readyMembers, out int incomingCount, bool endMuster = true)
    {
        readyMembers = party.Where(ready).ToArray();
        var boarding = readyMembers;
        incomingCount = party.Count(member => !boarding.Contains(member) && incoming(member));
        long started = incomingCount > 0 ? MusterStart(forceId, region, nowTick) : nowTick;
        if (DecideBoarding(readyMembers.Length, incomingCount, started, nowTick) == BoardingDecision.Wait)
            return [];
        if (endMuster)
            EndMuster(forceId, region);
        return readyMembers;
    }

    private sealed class BoardingBatch
    {
        public bool Active;
        public FrontierBoardingQueue<GameBot> Waiting = new();
        public System.Collections.Generic.HashSet<GameBot> Handled = new();
        public ECSGameTimer Timer;
        public long OpenUntil;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<OFTeleporter, BoardingBatch> Batches = new();
    public const string RequestKey = "RvrFrontierPassage";
    public const int BoardingRadius = 500;
    public const int SlicePassengerLimit = 8;
    public const int SliceMilliseconds = 3;
    public const int SliceIntervalMilliseconds = 50;
    public const int BoardingWindowMilliseconds = 30_000;
    public static bool SliceFull(int processed, double elapsedMilliseconds) =>
        processed >= SlicePassengerLimit || elapsedMilliseconds >= SliceMilliseconds;

    public static Vector3 WaitingOffset(long botId, int attempt = 0)
    {
        // Stable per character, not per warband: no shared endpoint at the NPC.
        ulong seed = unchecked((ulong)botId * 11400714819323198485UL);
        double angle = ((seed >> 16) % 65536) * (Math.PI * 2 / 65536) + attempt * 2.399963229728653;
        float radius = 220 + (float)(seed % 191);
        return new((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius, 0);
    }

    public static bool TryWaitingPoint(GameBot bot, OFTeleporter porter, out Vector3 point)
        => TryWaitingPoint(PathfindingProvider.Instance, porter.CurrentZone, new(porter.X, porter.Y, porter.Z),
            bot.Realm, bot.DatabaseID, out point);

    public static bool TryWaitingPoint(IPathfindingMgr nav, Zone zone, Vector3 center, eRealm realm, long botId, out Vector3 point)
    {
        point = default;
        if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 desired = center + WaitingOffset(botId, attempt);
            Vector3? floor = nav.GetClosestPoint(zone, desired, 32, 32, 96, nav.DefaultFilters);
            if (!floor.HasValue || Vector3.Distance(center, floor.Value) > 450 ||
                Vector2.Distance(new(center.X, center.Y), new(floor.Value.X, floor.Value.Y)) < 200 ||
                !AutonomousRealmBoundary.Allows(realm, zone.ZoneRegion.ID, zone.ID) ||
                !AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, center, floor.Value)) continue;
            point = floor.Value;
            return true;
        }
        return false;
    }
    public sealed record Passage(ushort Region, string Medallion, GameLocation Location);
    public sealed record Request(OFTeleporter Porter, Passage Passage, string ForceId);

    private static AutonomousRvrEventLayer.Plan ActiveSiegePlan(GameBot bot)
    {
        if (bot?.IsAutonomousWorldBot != true || bot.IsPlayerLedGroup || bot.IsTemporaryGroupHelper ||
            !AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR)) return null;
        string force = bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}";
        return AutonomousRvrEventLayer.KeepPlan(force, bot.Realm, GameLoop.GameLoopTime);
    }

    public static bool PassageMatchesSiege(GameBot bot, Passage passage)
    {
        var plan=ActiveSiegePlan(bot);
        // The home hop of a two-hop passage toward the siege is allowed.
        return plan==null || plan.RegionId==passage?.Region ||
            passage?.Medallion=="home_necklace" && passage.Region==HomeRegion(bot.Realm) &&
            bot.CurrentRegionID!=plan.RegionId && bot.CurrentRegionID!=passage.Region;
    }
    public static bool HasCommittedSiegePassage(GameBot bot, Passage passage) =>
        ActiveSiegePlan(bot) is { } plan && plan.RegionId==passage?.Region;
    public static bool HasDefenderPriority(GameBot bot, Passage passage) =>
        ActiveSiegePlan(bot) is { Intent: AutonomousRvrEventLayer.Intent.DefendEvent } plan && plan.RegionId==passage?.Region;

    // These are the existing OFTeleporter destinations, shared with its player
    // callback. The two invading realms arrive at different native portal keeps.
    public static Passage Destination(eRealm realm, ushort region) => (realm, region) switch
    {
        (eRealm.Albion, 100) => new(100,"odin_necklace",new("Odin Alb",100,596364,631509,5971)),
        (eRealm.Albion, 200) => new(200,"emain_necklace",new("Emain Alb",200,475835,343661,4080)),
        (eRealm.Midgard, 1) => new(1,"hadrian_necklace",new("Hadrian Mid",1,655200,293217,4879)),
        (eRealm.Midgard, 200) => new(200,"emain_necklace",new("Emain Mid",200,474107,295199,3871)),
        (eRealm.Hibernia, 1) => new(1,"hadrian_necklace",new("Hadrian Hib",1,605743,293676,4839)),
        (eRealm.Hibernia, 100) => new(100,"odin_necklace",new("Odin Hib",100,596055,581400,6031)),
        (eRealm.Albion, 1) => new(1,"home_necklace",new("Home Alb",1,584285,477200,2600)),
        (eRealm.Midgard, 100) => new(100,"home_necklace",new("Home Mid",100,766811,669605,5736)),
        (eRealm.Hibernia, 200) => new(200,"home_necklace",new("Home Hib",200,334386,420071,5184)),
        _ => null,
    };

    public static ushort HomeRegion(eRealm realm) => realm switch
    {
        eRealm.Albion => 1, eRealm.Midgard => 100, eRealm.Hibernia => 200, _ => 0,
    };

    /// <summary>
    /// The passage to take from a porter toward <paramref name="targetRegion"/>.
    /// Albion and Midgard portal keeps in a foreign frontier sell only the
    /// home medallion; there the force ports home first and onward from its
    /// own hub (two hops), as players did. Null when neither is on sale and
    /// no ticket is already held.
    /// </summary>
    public static Passage ChoosePassage(eRealm realm, ushort currentRegion, ushort targetRegion,
        Func<string, bool> sells, Func<string, bool> holds = null)
    {
        Passage direct = Destination(realm, targetRegion);
        if (direct == null)
            return null;
        if (holds?.Invoke(direct.Medallion) == true || sells(direct.Medallion))
            return direct;
        ushort home = HomeRegion(realm);
        if (home == 0 || currentRegion == home || targetRegion == home)
            return null;
        Passage homeward = Destination(realm, home);
        return homeward != null && (holds?.Invoke(homeward.Medallion) == true || sells(homeward.Medallion)) ? homeward : null;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> PorterStock = new(StringComparer.Ordinal);

    /// <summary>Whether a merchant within 3,000 units of the porter sells the medallion (cached; merchants are static).</summary>
    public static bool PorterSells(OFTeleporter porter, string medallion) =>
        porter != null && PorterStock.GetOrAdd($"{porter.InternalID}:{porter.CurrentRegionID}:{porter.X}:{porter.Y}:{medallion}", _ =>
            porter.GetNPCsInRadius(3000).OfType<GameMerchant>().Any(npc =>
                npc.TradeItems?.GetAllItems().Values.OfType<DbItemTemplate>().Any(item => item.Id_nb == medallion) == true));

    /// <summary>Set when a bot could not use a porter toward another frontier;
    /// its crossing search may then fall back to the dungeon road.</summary>
    public const string PorterUnavailableKey = "RvrPorterUnavailableUntil";

    public static DbInventoryItem Ticket(GameBot bot, Passage passage) => bot.Inventory.AllItems
        .FirstOrDefault(item => item.SlotPosition >= (int)eInventorySlot.FirstBackpack &&
            item.SlotPosition <= (int)eInventorySlot.LastBackpack && item.Count > 0 && item.Id_nb == passage.Medallion);

    public static bool Ready(GameBot bot, OFTeleporter porter, Request request) =>
        bot is { IsAutonomousWorldBot: true, IsAlive: true } &&
        !bot.IsPlayerLedGroup && !bot.IsTemporaryGroupHelper && !bot.IsStunned && !bot.IsMezzed &&
        bot.ObjectState == GameObject.eObjectState.Active && bot.CurrentRegion == porter.CurrentRegion &&
        bot.IsWithinRadius(porter, BoardingRadius) && !bot.InCombat && !bot.IsAttacking &&
        (bot.Brain as BotBrain)?.HasAggro != true && !GameRelic.IsPlayerCarryingRelic(bot) &&
        CanBoardForObjective(bot.Realm, AutonomousObjectiveAssignments.KindFor(bot), request?.Passage) &&
        PassageMatchesSiege(bot,request?.Passage) &&
        request?.Porter == porter && request.ForceId == (bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}") &&
        Ticket(bot,request.Passage) != null;

    public static bool CanBoardForObjective(eRealm realm, eAutonomousObjectiveKind objective, Passage passage)
    {
        if (passage == null) return false;
        Passage canonical = Destination(realm, passage.Region);
        if (canonical == null || canonical.Medallion != passage.Medallion) return false;
        return objective == eAutonomousObjectiveKind.RvR || passage.Medallion == "home_necklace";
    }

    public static void Depart(OFTeleporter porter)
    {
        var batch = Batches.GetOrCreateValue(porter);
        lock (batch)
        {
        batch.OpenUntil = GameLoop.GameLoopTime + BoardingWindowMilliseconds;
        if (batch.Waiting.OrdinaryCount == 0)
            foreach (var passenger in porter.GetNPCsInRadius(BoardingRadius).OfType<GameBot>()) batch.Waiting.Enqueue(passenger);
        if (batch.Active) return;
        // The ceremony authorizes this batch; spread actual transfers over
        // bounded slices rather than remove/add hundreds of actors at once.
        batch.Handled.Clear();
        batch.Active = true;
        batch.Timer = new ECSGameTimer(porter) { Callback = _ => ProcessBatch(porter, batch) };
        batch.Timer.Start(1);
        }
    }

    private static int ProcessBatch(OFTeleporter porter, BoardingBatch batch)
    {
        lock (batch) return ProcessBatchLocked(porter, batch);
    }

    private static int ProcessBatchLocked(OFTeleporter porter, BoardingBatch batch)
    {
        int processed = 0;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
        if (porter.ObjectState != GameObject.eObjectState.Active) { batch.Waiting.Clear(); batch.OpenUntil = 0; }
        else if (batch.Waiting.Count == 0 && GameLoop.GameLoopTime < batch.OpenUntil)
        {
            batch.Handled.Clear();
            foreach (var passenger in porter.GetNPCsInRadius(BoardingRadius).OfType<GameBot>()) batch.Waiting.Enqueue(passenger);
        }
        int examined = 0;
        while (examined < 64 && System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < SliceMilliseconds &&
            batch.Waiting.TryDequeue(out var bot, out bool priority))
        {
            examined++;
            if (batch.Handled.Contains(bot)) continue;
            var request = bot.TempProperties.GetProperty<Request>(RequestKey);
            if (priority && !HasDefenderPriority(bot, request?.Passage)) continue;
            if (!Ready(bot,porter,request)) continue;
            var party = BoardingParty(bot, request.Passage);
            bool MemberReady(GameBot member) => member == bot ||
                member.TempProperties.GetProperty<Request>(RequestKey) is Request memberRequest &&
                memberRequest.Passage.Region == request.Passage.Region && Ready(member, porter, memberRequest);
            var group = SelectBoarders(party,
                member => member.CurrentRegionID != request.Passage.Region && MemberReady(member),
                member => IsIncoming(member.IsAlive, member.CurrentRegion == porter.CurrentRegion,
                    member.CurrentRegionID == request.Passage.Region,
                    member.CurrentRegion == porter.CurrentRegion ? member.GetDistanceTo(porter) : double.PositiveInfinity),
                request.ForceId, request.Passage.Region, GameLoop.GameLoopTime, out var ready, out int incoming, endMuster: false);
            if (group.Length == 0)
            {
                // Hold this warband's departure; later casts re-check it.
                foreach (var waiting in ready) batch.Handled.Add(waiting);
                continue;
            }
            long nowTick = GameLoop.GameLoopTime;
            long?[] releases = group.Select(member => RecentRelease(member, nowTick)).Where(tick => tick.HasValue).ToArray();
            long? lastDeparture = LastDeparture(request.ForceId);
            var regroup = DecideRegroup(request.Passage.Medallion != "home_necklace", party.Length > 1,
                releases.Length > 0 ? releases.Min() : null, releases.Length > 0 ? releases.Max() : null,
                Gathered(party, porter), lastDeparture, HasDefenderPriority(bot, request.Passage), nowTick);
            if (regroup != RegroupDecision.Board)
            {
                // Rez, rebuff and regroup at the hub; later casts re-check it.
                foreach (var waiting in group) batch.Handled.Add(waiting);
                continue;
            }
            // Only a real departure ends the muster; a regroup hold keeps its clock.
            EndMuster(request.ForceId, request.Passage.Region);
            int departed=0;
            for (int index = 0; index < group.Length; index++)
            {
                // A warband is checked together but even its transfers can be
                // split across ticks; one costly transfer cannot trigger seven more.
                if (SliceFull(processed, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds))
                {
                    foreach (var pending in group.Skip(index))
                        if(priority)batch.Waiting.EnqueuePriority(pending);else batch.Waiting.Enqueue(pending);
                    break;
                }
                var member = group[index];
                batch.Handled.Add(member);
                if(member.CurrentRegionID==request.Passage.Region) continue;
                processed++;
                var ticket = Ticket(member,request.Passage);
                member.StopMovingOnPath(); member.StopMoving();
                if (!member.MoveTo(request.Passage.Location)) continue;
                departed++;
                member.Inventory.RemoveItem(ticket);
                member.TempProperties.RemoveProperty(RequestKey);
                member.TempProperties.RemoveProperty(ReleaseTickKey);
                member.ForcePathReplot();
                AutonomousBotEconomy.MarkInventoryChanged(member);
                AutonomousStuckWatchdog.MarkProgress(member,eAutonomousProgressKind.Movement);
                AutonomousBotStatusPersistence.Queue(member,true);
            }
            var log=DOL.Logging.LoggerManager.Create(typeof(AutonomousFrontierTransport));
            if (departed > 0 && request.Passage.Medallion != "home_necklace")
                RecordDeparture(request.ForceId, nowTick);
            // since_release_s: seconds since the latest release among those
            // leaving (-1 none); force_gap_s: since this force's previous
            // departure (-1 first); both measure the regroup rule live.
            if(departed>0 && log.IsInfoEnabled) log.Info($"RVR_FRONTIER_DEPARTURE force={request.ForceId} count={departed} porter=\"{porter.Name}\" destination=\"{request.Passage.Location.Name}\" region={request.Passage.Region} party={party.Length} left_behind={incoming} since_release_s={(releases.Length > 0 ? (nowTick - releases.Max().Value) / 1000 : -1)} force_gap_s={(lastDeparture is long gap ? (nowTick - gap) / 1000 : -1)}");
            if (SliceFull(processed, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds)) break;
        }
        }
        catch (Exception error)
        {
            DOL.Logging.LoggerManager.Create(typeof(AutonomousFrontierTransport)).Error("Frontier boarding slice failed; unprocessed passengers retained", error);
        }
        if (batch.Waiting.Count > 0) return SliceIntervalMilliseconds;
        if (GameLoop.GameLoopTime < batch.OpenUntil) return 2000;
        batch.Handled.Clear();
        lock (batch) { batch.Active = false; batch.Timer = null; }
        return 0;
    }

    public static bool WakeBoardingPorter(GameBot bot, Request request)
    {
        if (request?.Porter == null || !Ready(bot, request.Porter, request)) return false;
        if (HasDefenderPriority(bot, request.Passage))
        {
            var batch = Batches.GetOrCreateValue(request.Porter);
            lock (batch)
            {
                bool wasEmpty=batch.Waiting.Count==0;
                batch.Waiting.EnqueuePriority(bot);
                if (!batch.Active)
                {
                    batch.Handled.Clear();
                    batch.Active = true;
                    batch.Timer = new ECSGameTimer(request.Porter) { Callback = _ => ProcessBatch(request.Porter, batch) };
                    batch.Timer.Start(1);
                }
                else if(wasEmpty) batch.Timer.Start(1); // wake a two-second ordinary boarding scan
            }
            return true;
        }
        // Friendly service NPCs are not eligible for the hostile-mob wake scan.
        // Wake the actual pad only; retain its native brain, spell and schedule.
        request.Porter.OnAutonomousBotNearby();
        return true;
    }
}
