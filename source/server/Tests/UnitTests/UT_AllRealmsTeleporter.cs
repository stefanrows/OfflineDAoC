using System;
using System.Collections;
using System.Collections.Generic;
using DOL.Database.Handlers;
using DOL.GS.ServerRules;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public sealed class UT_AllRealmsTeleporter
{
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
    private GameServer _previousServer;
    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        GameServer.LoadTestDouble((TravelServer)RuntimeHelpers.GetUninitializedObject(typeof(TravelServer)));
    }
    [TearDown]
    public void TearDown() => GameServer.LoadTestDouble(_previousServer);

    private interface IPortCapture { DbTeleport Destination { get; } }
    private sealed class InlandPorter : DOL.GS.Scripts.InlandTeleporter, IPortCapture
    {
        public DbTeleport Destination { get; private set; }
        public override int X { get => 0; set { } }
        public override int Y { get => 0; set { } }
        public override int Z { get => 0; set { } }
        public override void SayTo(GamePlayer player, string message, bool announce = true) { }
        protected override void OnTeleportSpell(GamePlayer player, DbTeleport destination) => Destination = destination;
    }
    private sealed class LivePorter : DOL.GS.Scripts.LiveTeleporter, IPortCapture
    {
        public DbTeleport Destination { get; private set; }
        public override int X { get => 0; set { } }
        public override int Y { get => 0; set { } }
        public override int Z { get => 0; set { } }
        public override void SayTo(GamePlayer player, string message, bool announce = true) { }
        protected override void OnTeleportSpell(GamePlayer player, DbTeleport destination) => Destination = destination;
    }
    private sealed class MidgardPorter : MidgardTeleporter, IPortCapture
    {
        public DbTeleport Destination { get; private set; }
        public override int X { get => 0; set { } }
        public override int Y { get => 0; set { } }
        public override int Z { get => 0; set { } }
        public override void SayTo(GamePlayer player, string message, bool announce = true) { }
        protected override void OnTeleportSpell(GamePlayer player, DbTeleport destination) => Destination = destination;
    }
    private sealed class MidgardPlayer : GamePlayer
    {
        public MidgardPlayer() : base(null, null) { }
        public override eRealm Realm { get => eRealm.Midgard; set { } }
    }

    [TestCase(typeof(InlandPorter), "Cotswold Village", eRealm.Albion)]
    [TestCase(typeof(InlandPorter), "Mag Mell", eRealm.Hibernia)]
    [TestCase(typeof(LivePorter), "Cotswold Village", eRealm.Albion)]
    [TestCase(typeof(LivePorter), "Mag Mell", eRealm.Hibernia)]
    [TestCase(typeof(MidgardPorter), "Cotswold Village", eRealm.Albion)]
    [TestCase(typeof(MidgardPorter), "Mag Mell", eRealm.Hibernia)]
    public void InstalledPortersResolveForeignTownsForMidgardCharacters(Type type, string town, eRealm realm)
    {
        FieldInfo field = typeof(WorldMgr).GetField("m_teleportLocations", BindingFlags.Static | BindingFlags.NonPublic)!;
        object previous = field.GetValue(null);
        DbTeleport destination = new() { TeleportID = town, Realm = (int)realm, RegionID = realm == eRealm.Albion ? 1 : 200, X = 100, Y = 100, Z = 100 };
        try
        {
            field.SetValue(null, new Dictionary<eRealm, Dictionary<string, DbTeleport>>
            {
                [realm] = new() { [":" + town] = destination }
            });
            GameNPC porter = (GameNPC)RuntimeHelpers.GetUninitializedObject(type);
            GamePlayer player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(MidgardPlayer));
            Assert.That(porter.WhisperReceive(player, town), Is.True);
            Assert.That(((IPortCapture)porter).Destination, Is.SameAs(destination));
        }
        finally { field.SetValue(null, previous); }
    }

    [TestCase("Forest Sauvage", eRealm.Albion, 1)]
    [TestCase("Uppland", eRealm.Midgard, 100)]
    [TestCase("Cruachan Gorge", eRealm.Hibernia, 200)]
    public void FrontierTravelReplacesInstalledNewFrontiersRoutes(string destinationName, eRealm realm, int oldFrontiersRegion)
    {
        FieldInfo field = typeof(WorldMgr).GetField("m_teleportLocations", BindingFlags.Static | BindingFlags.NonPublic)!;
        object previous = field.GetValue(null);
        try
        {
            field.SetValue(null, new Dictionary<eRealm, Dictionary<string, DbTeleport>>
            {
                [realm] = new() { [":" + destinationName] = new DbTeleport { TeleportID = destinationName, Realm = (int)realm, RegionID = 163 } }
            });
            LivePorter porter = (LivePorter)RuntimeHelpers.GetUninitializedObject(typeof(LivePorter));
            GamePlayer player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(MidgardPlayer));
            Assert.That(porter.WhisperReceive(player, destinationName), Is.True);
            Assert.That(porter.Destination?.RegionID, Is.EqualTo(oldFrontiersRegion));
        }
        finally { field.SetValue(null, previous); }
    }

    [TestCase("New Frontiers")]
    [TestCase("Agramon")]
    [TestCase("Albion Agramon")]
    public void AgramonCannotBeReachedByTypingAnOldMenuOption(string destinationName)
    {
        LivePorter porter = (LivePorter)RuntimeHelpers.GetUninitializedObject(typeof(LivePorter));
        GamePlayer player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(MidgardPlayer));
        Assert.That(porter.WhisperReceive(player, destinationName), Is.True);
        Assert.That(porter.Destination, Is.Null);
    }

    [TestCase(eRealm.Albion)]
    [TestCase(eRealm.Midgard)]
    [TestCase(eRealm.Hibernia)]
    public void EveryRealmSeesAllRealmTravelMenusWithoutBattlegrounds(eRealm playerRealm)
    {
        string menu = AllRealmsTeleporter.BuildTravelMenu();

        Assert.That(menu, Does.Contain("[Camelot]").And.Contain("[Jordheim]").And.Contain("[Tir na Nog]"));
        Assert.That(menu, Does.Contain("[Albion Mainland]").And.Contain("[Midgard Mainland]").And.Contain("[Hibernia Mainland]"));
        Assert.That(menu, Does.Contain("[Albion Dungeons]").And.Contain("[Midgard Dungeons]").And.Contain("[Hibernia Dungeons]"));
        Assert.That(menu, Does.Not.Contain("Battlegrounds"));
    }
}
