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
    /// <summary>Whether another member of the force is already alive on the
    /// far side of this passage; stragglers then follow at once (bug 74).</summary>
    public static bool ForceAlreadyAcross(System.Collections.Generic.IEnumerable<GameBot> party, GameBot self, ushort destinationRegion) =>
        party?.Any(member => member != null && member != self && member.IsAlive && member.CurrentRegionID == destinationRegion) == true;

    /// <summary>
    /// A warband crosses with its leader (bug 76). Members that went ahead
    /// found the leader missing and ported back, then followed its passage
    /// again: 1,500 straggler departures per warband and day. Without a
    /// living leader (solo force) any member may go.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> LeaderHoldLogged = new(StringComparer.Ordinal);

    /// <summary>At most once per force and five minutes: why the leader is not boarding.</summary>
    private static void LogLeaderHold(Request request, GameBot leader, OFTeleporter porter, int waiting)
    {
        long now = GameLoop.GameLoopTime;
        if (LeaderHoldLogged.TryGetValue(request.ForceId, out long last) && now - last < 300_000) return;
        LeaderHoldLogged[request.ForceId] = now;
        var leaderRequest = leader.TempProperties.GetProperty<Request>(RequestKey);
        var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousFrontierTransport));
        if (log.IsInfoEnabled)
            log.Info($"RVR_FRONTIER_LEADER_HOLD force={request.ForceId} leader=\"{leader.Name}\" waiting={waiting} destination=\"{request.Passage.Location?.Name}\" " +
                $"leader_region={leader.CurrentRegionID} leader_porter_dist={(leader.CurrentRegion == porter.CurrentRegion ? (int)leader.GetDistanceTo(porter) : -1)} " +
                $"leader_request=\"{leaderRequest?.Passage?.Location?.Name}\" leader_ready={(Ready(leader, porter, leaderRequest) ? "true" : "false")} " +
                $"leader_combat={(leader.InCombat ? "true" : "false")} leader_ticket={(leaderRequest != null && Ticket(leader, leaderRequest.Passage) != null ? "true" : "false")}");
    }

    public static bool MayCrossWithoutLeader(bool hasLeader, bool leaderAcross, bool leaderBoarding,
        bool leaderOnThisSide = true) =>
        !hasLeader || !leaderOnThisSide || leaderAcross || leaderBoarding;

    /// <summary>A party of more than one realm (logged as <c>mixed</c>).</summary>
    public static bool IsMixedRealm(System.Collections.Generic.IEnumerable<eRealm> realms) =>
        realms?.Distinct().Skip(1).Any() == true;

    public static OFTeleporter NearestPorter(GameBot bot) => Porters.Keys
        .Where(p => p.ObjectState == GameObject.eObjectState.Active && p.CurrentRegion == bot.CurrentRegion)
        .OrderBy(bot.GetDistanceTo).FirstOrDefault();

    /// <summary>
    /// A group uses one actual porter network. Members standing at different
    /// portal keeps in the same frontier must walk to the leader's porter,
    /// rather than wait forever for that leader at their nearest one.
    /// Members rejoining from another frontier use the available native network
    /// there and finish its real home hop before joining the group.
    /// </summary>
    public static string TransportForceId(GameBot bot) =>
        (bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}") +
        (AutonomousWorldBotController.SiegeSupplyRegion(bot) != 0 ? $":supply-{bot.DatabaseID}" : "");

    public static OFTeleporter WarbandPorter(GameBot bot)
    {
        if (AutonomousWorldBotController.SiegeSupplyRegion(bot) != 0)
            return Porters.Keys.Where(porter => porter.ObjectState == GameObject.eObjectState.Active &&
                    porter.CurrentRegion == bot.CurrentRegion)
                .OrderBy(porter => porter.Realm == bot.Realm ? 0 : 1).ThenBy(bot.GetDistanceTo).FirstOrDefault();
        if (bot?.Group?.LivingLeader is GameBot { IsAlive: true } leader &&
            bot.IsAutonomousWorldBot && !bot.IsPlayerLedGroup && !bot.IsTemporaryGroupHelper &&
            AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR))
        {
            Request leaderRequest = leader.TempProperties.GetProperty<Request>(RequestKey);
            if (AutonomousWorldBotController.SiegeSupplyRegion(leader) == 0 &&
                leaderRequest?.Porter is { ObjectState: GameObject.eObjectState.Active } requested &&
                requested.CurrentRegion == bot.CurrentRegion && leader.CurrentRegion == bot.CurrentRegion)
                return requested;
            OFTeleporter shared = Porters.Keys.Where(porter =>
                    porter.ObjectState == GameObject.eObjectState.Active && porter.CurrentRegion == bot.CurrentRegion &&
                    porter.Realm == leader.Realm)
                .OrderBy(bot.GetDistanceTo).FirstOrDefault();
            if (shared != null) return shared;
        }
        return NearestPorter(bot);
    }

    /// <summary>
    /// The living leader's passage is authoritative on this side of the trip,
    /// even while its final destination is in a third region. Keep home hops
    /// intact so followers buy the same ticket and do not repeatedly replan.
    /// </summary>
    public static Request SharedRequest(GameBot bot, OFTeleporter porter, ushort targetRegion) =>
        AutonomousWorldBotController.SiegeSupplyRegion(bot) == 0 &&
        bot?.Group?.LivingLeader is GameBot { IsAlive: true } leader && leader != bot &&
        AutonomousWorldBotController.SiegeSupplyRegion(leader) == 0 &&
        leader.CurrentRegion == bot.CurrentRegion &&
        leader.TempProperties.GetProperty<Request>(RequestKey) is Request request &&
        request.Porter == porter && (request.TargetRegion == targetRegion || request.Passage.Region == targetRegion)
            ? request : null;

    /// <summary>The warband that ports together. RvR groups, including those
    /// answering a committed siege, board as one party (1.65: a group waited a
    /// minute at the porter for its stragglers, then ported together).</summary>
    public static GameBot[] BoardingParty(GameBot bot, Passage passage) =>
        AutonomousWorldBotController.SiegeSupplyRegion(bot) != 0 || passage.Medallion == "home_necklace" &&
        !AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR)
            ? [bot] // Initial PvE meetups can have members returning from different frontiers.
            : bot.Group?.GetMembersInTheGroup().OfType<GameBot>()
                .Where(member => AutonomousWorldBotController.SiegeSupplyRegion(member) == 0).ToArray() ?? [bot];

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
        long? latestReadyRelease, bool partyGathered, long? lastForceDeparture, bool defenderPriority, long nowTick,
        bool forceAlreadyAcross = false)
    {
        if (!outbound)
            return RegroupDecision.Board;
        if (latestReadyRelease is long latest && nowTick - latest < ReleaseHoldMilliseconds)
            return RegroupDecision.ReleaseHold;
        // A straggler whose force is already across follows at once (bug 74).
        if (!warband || forceAlreadyAcross)
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
    public sealed record Request(OFTeleporter Porter, Passage Passage, string ForceId, ushort TargetRegion = 0);

    private static AutonomousRvrEventLayer.Plan ActiveSiegePlan(GameBot bot)
    {
        if (bot?.IsAutonomousWorldBot != true || bot.IsPlayerLedGroup || bot.IsTemporaryGroupHelper ||
            !AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR)) return null;
        string force = bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}";
        return AutonomousGuildKeepDefense.PlanFor(bot) ??
            AutonomousRvrEventLayer.KeepPlan(force, bot.Realm, GameLoop.GameLoopTime);
    }

    public static bool PassageMatchesSiege(GameBot bot, Passage passage, ushort targetRegion = 0)
    {
        ushort supplyRegion = AutonomousWorldBotController.SiegeSupplyRegion(bot);
        if (supplyRegion != 0)
            return passage != null && targetRegion == supplyRegion &&
                (passage.Region == supplyRegion || passage.Medallion == "home_necklace" && bot.CurrentRegionID != passage.Region);
        var plan=ActiveSiegePlan(bot);
        // The home hop of a two-hop passage toward the siege is allowed, and so is a
        // passage to the region of the leader the warband is mustering on (bug 75).
        return plan==null || plan.RegionId==passage?.Region ||
            passage!=null && bot.Group?.LivingLeader is GameBot lead && lead!=bot && lead.CurrentRegionID==passage.Region ||
            passage?.Medallion == "home_necklace" && bot.CurrentRegionID != passage.Region &&
            (targetRegion == plan.RegionId || bot.Group?.LivingLeader is GameBot leader &&
                targetRegion == leader.CurrentRegionID) && bot.CurrentRegionID != targetRegion;
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

    /// <summary>Medallions whose landing follows the porter's realm network.</summary>
    public static bool IsFrontierMedallion(string medallion) => medallion is "hadrian_necklace" or "odin_necklace" or
        "emain_necklace" or "home_necklace" or "snowdonia_necklace" or "vindsaul_necklace" or "druimcain_necklace";

    /// <summary>
    /// Where a human's frontier medallion lands at a porter of
    /// <paramref name="porterRealm"/> (bug 74, owner 2026-09-30: every realm may
    /// use every frontier porter and lands at that porter's landing). A
    /// medallion for the porter's own frontier leads to its home portal keep;
    /// an inner-keep medallion (Snowdonia, Vindsaul, Druim Cain) works only at
    /// a porter of that realm.
    /// </summary>
    public static GameLocation PorterLanding(eRealm porterRealm, string medallion)
    {
        ushort home = HomeRegion(porterRealm);
        return medallion switch
        {
            "hadrian_necklace" => Destination(porterRealm, 1)?.Location,
            "odin_necklace" => Destination(porterRealm, 100)?.Location,
            "emain_necklace" => Destination(porterRealm, 200)?.Location,
            "home_necklace" when home != 0 => Destination(porterRealm, home)?.Location,
            "snowdonia_necklace" when porterRealm == eRealm.Albion => new GameLocation("Snowdonia Alb", 1, 527608, 358918, 3083),
            "vindsaul_necklace" when porterRealm == eRealm.Midgard => new GameLocation("Vindsaul Faste Mid", 100, 704916, 738544, 5704),
            "druimcain_necklace" when porterRealm == eRealm.Hibernia => new GameLocation("Druim Cain Hib", 200, 421788, 486493, 1824),
            _ => null,
        };
    }

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
        (CanBoardForObjective(bot.Realm, PassageRealm(bot), AutonomousObjectiveAssignments.KindFor(bot), request?.Passage) ||
            AutonomousObjectiveAssignments.KindFor(bot) == eAutonomousObjectiveKind.RvR &&
            CanBoardForObjective(porter.Realm, eAutonomousObjectiveKind.RvR, request?.Passage)) &&
        (!AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR) ||
            SameNativeLanding(porter, request?.Passage)) &&
        PassageMatchesSiege(bot, request?.Passage, request?.TargetRegion ?? 0) &&
        request?.Porter == porter && request.ForceId == TransportForceId(bot) &&
        Ticket(bot,request.Passage) != null;

    private static bool SameNativeLanding(OFTeleporter porter, Passage passage)
    {
        GameLocation native = passage == null ? null : PorterLanding(porter.Realm, passage.Medallion);
        return native != null && passage.Location != null && native.RegionID == passage.Region &&
            native.X == passage.Location.X && native.Y == passage.Location.Y && native.Z == passage.Location.Z;
    }

    public static bool CanBoardForObjective(eRealm realm, eAutonomousObjectiveKind objective, Passage passage)
    {
        if (passage == null) return false;
        Passage canonical = Destination(realm, passage.Region);
        if (canonical == null || canonical.Medallion != passage.Medallion) return false;
        return objective == eAutonomousObjectiveKind.RvR || passage.Medallion == "home_necklace";
    }

    // ---- One passage for a whole warband (bug 75) ---------------------------
    // Camlann crews mix birth realms, and Destination(realm, region) differs per
    // realm (Odin Alb, Odin Hib, Home Mid), so one force used to split over
    // several porters, one to three members per departure. A warband on RvR
    // work now takes the passage of its leader's realm: one medallion, one
    // landing, boarding and arriving together (the guilds, not the birth
    // realms, are the sides on Camlann).

    /// <summary>The realm whose passage a bot uses: its leader's within a warband, else its own.</summary>
    public static eRealm ForcePassageRealm(eRealm own, eRealm? leaderRealm, bool warbandOnRvr) =>
        warbandOnRvr && leaderRealm is { } realm && realm != eRealm.None ? realm : own;

    public static eRealm PassageRealm(GameBot bot)
    {
        if (bot == null) return eRealm.None;
        bool warband = bot.IsAutonomousWorldBot && !bot.IsPlayerLedGroup && !bot.IsTemporaryGroupHelper &&
            bot.Group != null && AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR);
        eRealm? leader = warband && bot.Group.LivingLeader is GameBot { IsAutonomousWorldBot: true } lead ? lead.Realm : null;
        return ForcePassageRealm(bot.Realm, leader, warband);
    }

    /// <summary>A member may also use the passage of its force's realm for an RvR objective.</summary>
    public static bool CanBoardForObjective(eRealm own, eRealm forceRealm, eAutonomousObjectiveKind objective, Passage passage) =>
        CanBoardForObjective(own, objective, passage) ||
        objective == eAutonomousObjectiveKind.RvR && forceRealm != own && CanBoardForObjective(forceRealm, objective, passage);

    /// <summary>Same medallion and landing: the members of one departure share a passage.</summary>
    public static bool SamePassage(Passage first, Passage second) =>
        first != null && second != null && first.Region == second.Region && first.Medallion == second.Medallion &&
        first.Location?.Name == second.Location?.Name;

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
                SamePassage(memberRequest.Passage, request.Passage) && Ready(member, porter, memberRequest);
            // Part of the force is already across: stragglers follow at once
            // instead of mustering, regrouping or waiting out the force cap (bug 74).
            GameBot leader = party.Length > 1 && bot.Group?.LivingLeader is GameBot { IsAlive: true } lead && party.Contains(lead) ? lead : null;
            // Only the cohort leaving the leader's side must wait for it.
            // A member returning by a home hop from a third frontier cannot
            // possibly board with a leader who is already in another region.
            bool leaderOnThisSide = leader != null && leader.CurrentRegion == porter.CurrentRegion;
            bool forceAcross = leader != null
                ? leader.CurrentRegionID == request.Passage.Region
                : ForceAlreadyAcross(party, null, request.Passage.Region);
            var group = SelectBoarders(party,
                member => member.CurrentRegionID != request.Passage.Region && MemberReady(member),
                member => !forceAcross && IsIncoming(member.IsAlive, member.CurrentRegion == porter.CurrentRegion,
                    member.CurrentRegionID == request.Passage.Region,
                    member.CurrentRegion == porter.CurrentRegion ? member.GetDistanceTo(porter) : double.PositiveInfinity),
                request.ForceId, request.Passage.Region, GameLoop.GameLoopTime, out var ready, out int incoming, endMuster: false);
            if (group.Length > 0 && !MayCrossWithoutLeader(leader != null, forceAcross, group.Contains(leader), leaderOnThisSide))
            {
                LogLeaderHold(request, leader, porter, group.Length);
                group = [];
            }
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
                Gathered(party, porter), lastDeparture, HasDefenderPriority(bot, request.Passage), nowTick, forceAcross);
            if (regroup != RegroupDecision.Board)
            {
                // Rez, rebuff and regroup at the hub; later casts re-check it.
                foreach (var waiting in group) batch.Handled.Add(waiting);
                continue;
            }
            // Only a real departure ends the muster; a regroup hold keeps its clock.
            EndMuster(request.ForceId, request.Passage.Region);
            // The leader lands first. A bounded transfer slice may stop after
            // one passenger; followers then see the leader across and continue,
            // instead of turning back toward a leader still on the old side.
            group = group.OrderByDescending(member => member == leader).ToArray();
            int departed=0;
            bool leaderTransferFailed = false;
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
                // Conditions can change between choosing the cohort and its
                // transfer slice. Keep actual combat, ticket and route guards
                // at the final transfer boundary, especially for the leader.
                var currentRequest = member.TempProperties.GetProperty<Request>(RequestKey);
                if (!SamePassage(currentRequest?.Passage, request.Passage) || !Ready(member, porter, currentRequest))
                {
                    if (member == leader) break;
                    continue;
                }
                processed++;
                var ticket = Ticket(member,request.Passage);
                member.StopMovingOnPath(); member.StopMoving();
                if (!member.MoveTo(request.Passage.Location))
                {
                    if (member == leader)
                    {
                        leaderTransferFailed = true;
                        break; // never split the column behind a failed leader transfer
                    }
                    continue;
                }
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
            // A straggler's crossing is not a new force departure: the cap
            // keeps counting from the force's own departure.
            if (departed > 0 && request.Passage.Medallion != "home_necklace" && !forceAcross)
                RecordDeparture(request.ForceId, nowTick);
            // since_release_s: seconds since the latest release among those
            // leaving (-1 none); force_gap_s: since this force's previous
            // departure (-1 first); both measure the regroup rule live.
            if (leaderTransferFailed && log.IsWarnEnabled)
                log.Warn($"RVR_FRONTIER_LEADER_TRANSFER_FAILED force={request.ForceId} leader=\"{leader?.Name}\" porter=\"{porter.Name}\" destination=\"{request.Passage.Location.Name}\"; cohort retained for retry");
            if(departed>0 && log.IsInfoEnabled) log.Info($"RVR_FRONTIER_DEPARTURE force={request.ForceId} count={departed} porter=\"{porter.Name}\" destination=\"{request.Passage.Location.Name}\" region={request.Passage.Region} party={party.Length} left_behind={incoming} since_release_s={(releases.Length > 0 ? (nowTick - releases.Max().Value) / 1000 : -1)} force_gap_s={(lastDeparture is long gap ? (nowTick - gap) / 1000 : -1)} porter_realm={porter.Realm} mixed={(IsMixedRealm(party.Select(member => member.Realm)) ? "true" : "false")} straggler={(forceAcross ? "true" : "false")}");
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
