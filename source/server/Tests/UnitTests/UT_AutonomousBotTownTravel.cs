using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
[NonParallelizable]
public sealed class UT_AutonomousBotTownTravel
{
    private const ushort DestinationRegion = 1;
    private const ushort SourceRegion = 65001;

    private sealed class TravelServer : GameServer
    {
        protected override IServerRules ServerRulesImpl => new PvPServerRules();
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

    private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, EmptyReadDatabase>();

    public class EmptyReadDatabase : DispatchProxy
    {
        protected override object Invoke(MethodInfo method, object[] args)
        {
            Type result = method.ReturnType;
            if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
            return null;
        }
    }

    private sealed class TestRegion : Region
    {
        public ushort RegionID;
        public bool Disabled;

        private TestRegion() : base(null) { }

        public override ushort ID => RegionID;
        public override bool IsDisabled => Disabled;

        public static TestRegion Create(ushort regionID, bool disabled, params GameObject[] objects)
        {
            TestRegion region = (TestRegion)RuntimeHelpers.GetUninitializedObject(typeof(TestRegion));
            region.RegionID = regionID;
            region.Disabled = disabled;
            typeof(Region).GetField("m_zones", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(region, new List<Zone> { new(region, 2, "Town", 0, 0, 1000000, 1000000, 2, false, 0, false, 0, 0, 0, 0, 0) });
            SetObjects(region, objects);
            return region;
        }

        public static void SetObjects(TestRegion region, params GameObject[] objects) =>
            typeof(Region).GetField("m_objects", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(region, objects);
    }

    private sealed class TestBot : GameBot
    {
        private TestBot() : base(null, 0) { }

        public ushort RegionID;
        public int PosX;
        public int PosY;
        public int PosZ;
        public bool Alive = true;
        public bool Combat;
        public bool Attacking;
        public bool MoveSucceeds = true;
        public int MoveCount;
        public ushort MovedRegion;
        public int MovedX;
        public int MovedY;
        public int MovedZ;
        public ushort MovedHeading;

        public override ushort CurrentRegionID { get => RegionID; set => RegionID = value; }
        public override GameObject.eObjectState ObjectState { get; set; } = GameObject.eObjectState.Active;
        public override int X { get => PosX; set => PosX = value; }
        public override int Y { get => PosY; set => PosY = value; }
        public override int Z { get => PosZ; set => PosZ = value; }
        public override short MaxSpeed => 200;
        public override bool IsAlive => Alive;
        public override bool InCombat => Combat;
        public override bool IsAttacking => Attacking;

        public override bool MoveTo(ushort regionID, int x, int y, int z, ushort heading)
        {
            MoveCount++;
            MovedRegion = regionID;
            MovedX = x;
            MovedY = y;
            MovedZ = z;
            MovedHeading = heading;
            if (MoveSucceeds)
                RegionID = regionID;
            return MoveSucceeds;
        }
    }

    private sealed class TestTeleporter : AllRealmsTeleporter
    {
        public ushort RegionID;
        public int PosX;
        public int PosY;
        public int PosZ;

        public override ushort CurrentRegionID { get => RegionID; set => RegionID = value; }
        public override GameObject.eObjectState ObjectState { get; set; } = GameObject.eObjectState.Active;
        public override int X { get => PosX; set => PosX = value; }
        public override int Y { get => PosY; set => PosY = value; }
        public override int Z { get => PosZ; set => PosZ = value; }
    }

    private sealed class TestBotBrain : BotBrain
    {
        public bool Aggro;
        public override bool HasAggro => Aggro;
    }

    private sealed class Mesh : PathfindingMgrBase
    {
        public Func<Vector3, Vector3, bool> Connected = (_, _) => true;
        public override bool IsAvailable => true;
        public override bool HasNavmesh(Zone zone) => true;
        public override Vector3? GetClosestPoint(Zone zone, Vector3 position, float x, float y, float z, EDtPolyFlags[] filters) => position;
        public override PathfindingResult GetPathStraight(Zone zone, Vector3 from, Vector3 to,
            EDtPolyFlags[] filters, Span<WrappedPathfindingNode> nodes)
        {
            bool connected = Connected(from, to);
            nodes[0] = new(connected ? to : from, EDtPolyFlags.Walk);
            return new(connected ? PathfindingStatus.PathFound : PathfindingStatus.PartialPathFound, 1);
        }
    }

    private IPathfindingMgr _previousNav;
    private Mesh _nav;

    private GameServer _previousServer;
    private Dictionary<eRealm, Dictionary<string, DbTeleport>> _previousTeleports;
    private ConcurrentDictionary<ushort, Region> _regions;
    private readonly Dictionary<ushort, Region> _previousRegions = new();
    private TestRegion _source;
    private TestBot _bot;
    private TestTeleporter _teleporter;
    private TestBotBrain _brain;

    [SetUp]
    public void SetUp()
    {
        _previousNav = PathfindingProvider.Instance;
        _nav = new Mesh();
        PathfindingProvider.SetPathfindingMgr(_nav);
        _previousRegions.Clear();
        _previousServer = GameServer.Instance;
        GameServer.LoadTestDouble((TravelServer)RuntimeHelpers.GetUninitializedObject(typeof(TravelServer)));

        FieldInfo teleportField = typeof(WorldMgr).GetField("m_teleportLocations", BindingFlags.Static | BindingFlags.NonPublic)!;
        _previousTeleports = (Dictionary<eRealm, Dictionary<string, DbTeleport>>)teleportField.GetValue(null);
        teleportField.SetValue(null, new Dictionary<eRealm, Dictionary<string, DbTeleport>>());

        _regions = (ConcurrentDictionary<ushort, Region>)typeof(WorldMgr)
            .GetField("m_regions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        RegisterRegion(TestRegion.Create(DestinationRegion, false));
        _source = TestRegion.Create(SourceRegion, false);
        RegisterRegion(_source);

        _bot = (TestBot)RuntimeHelpers.GetUninitializedObject(typeof(TestBot));
        _bot.RegionID = SourceRegion;
        _bot.Alive = true;
        _bot.MoveSucceeds = true;
        _bot.ObjectState = GameObject.eObjectState.Active;
        typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!
            .GetSetMethod(true)!.Invoke(_bot, new object[] { true });
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_bot, new ArrayList());
        _brain = (TestBotBrain)RuntimeHelpers.GetUninitializedObject(typeof(TestBotBrain));
        typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_bot, _brain);

        _teleporter = (TestTeleporter)RuntimeHelpers.GetUninitializedObject(typeof(TestTeleporter));
        _teleporter.RegionID = SourceRegion;
        _teleporter.ObjectState = GameObject.eObjectState.Active;
        TestRegion.SetObjects(_source, _bot, _teleporter);
    }

    [TearDown]
    public void TearDown()
    {
        PathfindingProvider.SetPathfindingMgr(_previousNav);
        foreach (ushort regionID in _previousRegions.Keys)
            RestoreRegion(regionID);

        typeof(WorldMgr).GetField("m_teleportLocations", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, _previousTeleports);
        GameServer.LoadTestDouble(_previousServer);
    }

    [Test]
    public void DestinationResolutionUsesFallbackWhenInstalledTownRouteIsAbsent()
    {
        Assert.That(AutonomousBotTownTravel.TryResolveTownDestination(DestinationRegion, out DbTeleport destination), Is.True);
        Assert.That(destination.TeleportID, Is.EqualTo("Cotswold Village"));
        Assert.That(destination.RegionID, Is.EqualTo(DestinationRegion));
    }

    [Test]
    public void DestinationResolutionUsesInstalledTownRouteBeforeFallback()
    {
        DbTeleport installed = TownRoute(DestinationRegion, 321000, 432000, 1200, 1234);
        installed.TeleportID = "Mularn";
        installed.Realm = (int)eRealm.Midgard;
        ((Dictionary<eRealm, Dictionary<string, DbTeleport>>)typeof(WorldMgr)
            .GetField("m_teleportLocations", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))
            [eRealm.Midgard] = new Dictionary<string, DbTeleport> { [":Mularn"] = installed };

        Assert.That(AutonomousBotTownTravel.TryResolveTownDestination(DestinationRegion, out DbTeleport destination), Is.True);
        Assert.That(destination, Is.SameAs(installed));
        Assert.That(destination.X, Is.EqualTo(321000));
    }

    [Test]
    public void MissingOrDisabledDestinationRegionHasNoTownRoute()
    {
        Assert.That(AutonomousBotTownTravel.TryResolveTownDestination(65002, out _), Is.False);
        RegisterRegion(TestRegion.Create(DestinationRegion, true));
        Assert.That(AutonomousBotTownTravel.TryResolveTownDestination(DestinationRegion, out _), Is.False);
    }

    [Test]
    public void MalformedInstalledTownRouteIsRejected()
    {
        const ushort unsupportedRegion = 65002;
        RegisterRegion(TestRegion.Create(unsupportedRegion, false));
        DbTeleport malformed = TownRoute(unsupportedRegion, 0, 0, 0, 0);
        ((Dictionary<eRealm, Dictionary<string, DbTeleport>>)typeof(WorldMgr)
            .GetField("m_teleportLocations", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))
            [eRealm.Albion] = new Dictionary<string, DbTeleport> { [":Cotswold Village"] = malformed };

        Assert.That(AutonomousBotTownTravel.TryResolveTownDestination(unsupportedRegion, out _), Is.False);
    }

    [Test]
    public void RouteRequiresAnActivePorterInTheBotCurrentRegion()
    {
        TestRegion.SetObjects(_source, _bot);
        Assert.That(AutonomousBotTownTravel.TryGetTownRoute(_bot, DestinationRegion, out _, out _), Is.False);

        TestRegion.SetObjects(_source, _bot, _teleporter);
        Assert.That(AutonomousBotTownTravel.TryGetTownRoute(_bot, DestinationRegion, out AllRealmsTeleporter porter, out DbTeleport destination), Is.True);
        Assert.That(porter, Is.SameAs(_teleporter));
        Assert.That(destination.RegionID, Is.EqualTo(DestinationRegion));

        _teleporter.ObjectState = GameObject.eObjectState.Inactive;
        Assert.That(AutonomousBotTownTravel.TryGetTownRoute(_bot, DestinationRegion, out _, out _), Is.False);
    }

    [Test]
    public void InteractionDistanceUsesActualNpcProximity()
    {
        _bot.PosX = 0;
        _teleporter.PosX = WorldMgr.INTERACT_DISTANCE;
        Assert.That(AutonomousBotTownTravel.IsAtInteractionDistance(_bot, _teleporter), Is.True);

        _teleporter.PosX = WorldMgr.INTERACT_DISTANCE + 1;
        Assert.That(AutonomousBotTownTravel.IsAtInteractionDistance(_bot, _teleporter), Is.False);
    }

    [Test]
    public void TeleportRequiresSafeAutonomousBotAndTransfersToValidatedDestination()
    {
        _bot.Combat = true;
        Assert.That(AutonomousBotTownTravel.TryTeleportToTown(_bot, _teleporter, DestinationRegion), Is.False);
        _bot.Combat = false;

        _bot.Attacking = true;
        Assert.That(AutonomousBotTownTravel.TryTeleportToTown(_bot, _teleporter, DestinationRegion), Is.False);
        _bot.Attacking = false;

        _brain.Aggro = true;
        Assert.That(AutonomousBotTownTravel.TryTeleportToTown(_bot, _teleporter, DestinationRegion), Is.False);
        _brain.Aggro = false;

        _bot.Alive = false;
        Assert.That(AutonomousBotTownTravel.TryTeleportToTown(_bot, _teleporter, DestinationRegion), Is.False);
        _bot.Alive = true;

        _teleporter.PosX = WorldMgr.INTERACT_DISTANCE + 1;
        Assert.That(AutonomousBotTownTravel.TryTeleportToTown(_bot, _teleporter, DestinationRegion), Is.False);
        Assert.That(_bot.MoveCount, Is.Zero);

        _teleporter.PosX = _bot.PosX;
        Assert.That(AutonomousBotTownTravel.TryTeleportToTown(_bot, _teleporter, DestinationRegion), Is.True);
        Assert.That(_bot.MoveCount, Is.EqualTo(1));
        Assert.That(_bot.MovedRegion, Is.EqualTo(DestinationRegion));
        Assert.That(_bot.MovedX, Is.EqualTo(560467));
        Assert.That(_bot.MovedY, Is.EqualTo(511652));
        Assert.That(_bot.MovedZ, Is.EqualTo(2344));
        Assert.That(_bot.MovedHeading, Is.EqualTo(3398));
    }

    [Test]
    public void SuccessfulTownArrivalMustStillReachFormationSlotBeforeAttendance()
    {
        Assert.That(AutonomousBotTownTravel.TryTeleportToTown(_bot, _teleporter, DestinationRegion), Is.True);
        _bot.PosX = _bot.MovedX;
        _bot.PosY = _bot.MovedY;
        _bot.PosZ = _bot.MovedZ;
        Vector3 slot = new(_bot.X + 300, _bot.Y, _bot.Z);
        var attendance = new AutonomousRendezvousAttendance();
        attendance.Rebase(new long[] { 1 }, 0, new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc),
            AutonomousBotGroupCoordinator.RemoteMeetupTimeoutMilliseconds);

        bool atTown = AutonomousRendezvousAttendance.IsAtSlot(new(_bot.X, _bot.Y, _bot.Z), slot, tight: false);
        Assert.That(attendance.Observe(1, 0, atTown), Is.False);
        Assert.That(attendance.HasArrived(1), Is.False);

        _bot.PosX = (int)slot.X;
        _bot.PosY = (int)slot.Y;
        _bot.PosZ = (int)slot.Z;
        Assert.That(attendance.Observe(1, 1,
            AutonomousRendezvousAttendance.IsAtSlot(new(_bot.X, _bot.Y, _bot.Z), slot, tight: false)), Is.False);
        Assert.That(attendance.HasArrived(1), Is.True);
        Assert.That(attendance.DeadlineUtc(1), Is.Null);
    }

    [Test]
    public void RendezvousChoosesNearestReachableTownAcrossInstalledAndFallbackRoutes()
    {
        DbTeleport far = TownRoute(DestinationRegion, 1000, 1000, 0, 0);
        ((Dictionary<eRealm, Dictionary<string, DbTeleport>>)typeof(WorldMgr)
            .GetField("m_teleportLocations", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null))
            [eRealm.Albion] = new Dictionary<string, DbTeleport> { [":Cotswold Village"] = far };
        // Public route resolver must select the fallback at this known town
        // rather than the far-away installed route for a different menu ID.
        Assert.That(AutonomousBotTownTravel.TryResolveTownDestination(DestinationRegion,
            new(408907, 652791, 4944), out DbTeleport selected), Is.True);
        Assert.That(selected.TeleportID, Is.EqualTo("Cornwall Station"));
    }

    [Test]
    public void RemoteInvitationSkipsDisconnectedNearestPorter()
    {
        _teleporter.PosX = 2000;
        var reachable = (TestTeleporter)RuntimeHelpers.GetUninitializedObject(typeof(TestTeleporter));
        reachable.RegionID = SourceRegion;
        reachable.PosX = 5000;
        reachable.ObjectState = GameObject.eObjectState.Active;
        TestRegion.SetObjects(_source, _bot, _teleporter, reachable);
        _nav.Connected = (_, to) => to.X < 1000 || to.X > 3000;
        Vector3 rendezvous = new(560467, 511652, 2344);
        Assert.That(AutonomousBotTownTravel.TryGetTownRoute(_bot, DestinationRegion, rendezvous,
            out AllRealmsTeleporter selected, out _, out double minutes), Is.True);
        Assert.That(selected, Is.SameAs(reachable));
        Assert.That(minutes, Is.GreaterThan(1));
    }

    [Test]
    public void RemoteInvitationRejectsDisconnectedDestinationAndMissingNavigation()
    {
        Vector3 rendezvous = new(560467, 512652, 2344);
        _nav.Connected = (_, _) => false;
        Assert.That(AutonomousBotTownTravel.TryGetTownRoute(_bot, DestinationRegion, rendezvous,
            out _, out _, out _), Is.False);
        PathfindingProvider.SetPathfindingMgr(null);
        Assert.That(AutonomousBotTownTravel.TryGetTownRoute(_bot, DestinationRegion, rendezvous,
            out _, out _, out _), Is.False);
    }

    [Test]
    public void TownReachabilityChecksLaterSeamsAndFinalCorridor()
    {
        _source.Zones.Clear();
        for (int i = 0; i < 3; i++)
            _source.Zones.Add(new Zone(_source, (ushort)(10 + i), "Route", i * 10000, 0,
                10000, 10000, 2, false, 0, false, 0, 0, 0, 0, 0));
        Vector3 start = new(1000, 1000, 0);
        Vector3 end = new(29000, 1000, 0);
        Assert.That(AutonomousBotTownTravel.CanReachTownPoint(_source, _source.Zones[0], start, end), Is.True);

        // The first seam works, but the middle zone has disconnected halves.
        _nav.Connected = (from, to) => !(from.X >= 10000 && from.X < 15000 && to.X > 15000);
        Assert.That(AutonomousBotTownTravel.CanReachTownPoint(_source, _source.Zones[0], start, end), Is.False);

        // The later seam works, but arrivals cannot reach the final town.
        _nav.Connected = (from, to) => !(from.X >= 20000 && to.X >= 28000);
        Assert.That(AutonomousBotTownTravel.CanReachTownPoint(_source, _source.Zones[0], start, end), Is.False);
    }

    private void RegisterRegion(TestRegion region)
    {
        if (!_previousRegions.ContainsKey(region.ID))
            _previousRegions[region.ID] = _regions.TryGetValue(region.ID, out Region previous) ? previous : null;
        _regions[region.ID] = region;
    }

    private void RestoreRegion(ushort regionID)
    {
        if (_previousRegions.TryGetValue(regionID, out Region previous) && previous != null)
            _regions[regionID] = previous;
        else
            _regions.TryRemove(regionID, out _);
    }

    private static DbTeleport TownRoute(ushort regionID, int x, int y, int z, int heading) => new()
    {
        Type = string.Empty,
        TeleportID = "Cotswold Village",
        Realm = (int)eRealm.Albion,
        RegionID = regionID,
        X = x,
        Y = y,
        Z = z,
        Heading = heading,
    };
}
