using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DOL.Database;
using DOL.GS;
using DOL.GS.Commands;
using DOL.GS.ServerRules;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.UnitTests;

/// <summary>
/// RvR package R2 (docs/RVR_TRAVEL_POPULATION.md): travel and population rules in both worlds. Camlann keeps
/// "everything open"; the RvR world binds porters, destinations, brokers, companions, parties, bot travel,
/// release points and the open BG rule to the character's realm.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_RvrTravelPopulation
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

    private sealed class CapturingPorter : DOL.GS.Scripts.LiveTeleporter
    {
        public static eRealm PorterRealm = eRealm.None;
        public DbTeleport Destination { get; private set; }
        public override int X { get => 0; set { } }
        public override int Y { get => 0; set { } }
        public override int Z { get => 0; set { } }
        public override eRealm Realm { get => PorterRealm; set { } }
        public override void SayTo(GamePlayer player, string message, bool announce = true) { }
        protected override void OnTeleportSpell(GamePlayer player, DbTeleport destination) => Destination = destination;
    }

    private sealed class RealmPlayer : GamePlayer
    {
        public static eRealm PlayerRealm = eRealm.Midgard;
        public RealmPlayer() : base(null, null) { }
        public override eRealm Realm { get => PlayerRealm; set { } }
    }

    private GameServer _previousServer;
    private object _previousTeleports;
    private static readonly FieldInfo TeleportField =
        typeof(WorldMgr).GetField("m_teleportLocations", BindingFlags.Static | BindingFlags.NonPublic)!;

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        GameServer.LoadTestDouble((TravelServer)RuntimeHelpers.GetUninitializedObject(typeof(TravelServer)));
        _previousTeleports = TeleportField.GetValue(null);
        CapturingPorter.PorterRealm = eRealm.None;
        RealmPlayer.PlayerRealm = eRealm.Midgard;
    }

    [TearDown]
    public void TearDown()
    {
        TeleportField.SetValue(null, _previousTeleports);
        GameServer.LoadTestDouble(_previousServer);
        GothwaitePvpMode.Reset();
    }

    private static DbTeleport InstallDestination(string name, eRealm realm, int region)
    {
        var destination = new DbTeleport { TeleportID = name, Realm = (int)realm, RegionID = region, X = 100, Y = 100, Z = 100 };
        TeleportField.SetValue(null, new Dictionary<eRealm, Dictionary<string, DbTeleport>>
        {
            [realm] = new() { [":" + name] = destination }
        });
        return destination;
    }

    private static (CapturingPorter Porter, GamePlayer Player) Actors()
    {
        var porter = (CapturingPorter)RuntimeHelpers.GetUninitializedObject(typeof(CapturingPorter));
        return (porter, (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(RealmPlayer)));
    }

    // ---- porters and menus --------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void PorterMenuListsEveryRealmInCamlannAndOnlyTheOwnRealmInRvr(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        string menu = AllRealmsTeleporter.BuildTravelMenu(world, eRealm.Albion, false, true);

        Assert.That(menu, Does.Contain("[Camelot]").And.Contain("[Albion Frontiers]").And.Contain("[Albion Mainland]")
            .And.Contain("[Albion Darkness Falls]").And.Contain("[Albion Oceanus]").And.Contain(AtlantisTravel.MenuLine)
            .And.Contain("[Housing]"));
        bool enemies = BothWorlds.Expect(world, true, false);
        foreach (string enemy in new[] { "[Jordheim]", "[Tir na Nog]", "[Midgard Mainland]", "[Hibernia Dungeons]",
                     "[Midgard Frontiers]", "[Hibernia Oceanus]", "[Midgard Darkness Falls]" })
            Assert.That(menu.Contains(enemy), Is.EqualTo(enemies), enemy);
    }

    [Test]
    public void CamlannMenuIsTheUnchangedRealmOpenMenu()
    {
        using var _ = BothWorlds.Use(WorldModelKind.Camlann);
        foreach (eRealm realm in RealmTravel.PlayableRealms)
            Assert.That(AllRealmsTeleporter.BuildTravelMenu(WorldModelKind.Camlann, realm, true, true),
                Is.EqualTo(AllRealmsTeleporter.BuildTravelMenu(true, true)));
    }

    [TestCase("Jordheim", eRealm.Midgard)]
    [TestCase("cotswold village", eRealm.Albion)]
    [TestCase("Hibernia Oceanus", eRealm.Hibernia)]
    [TestCase("Svasud Faste", eRealm.Midgard)]
    [TestCase("Midgard Darkness Falls", eRealm.Midgard)]
    [TestCase("Albion", eRealm.Albion)]
    public void EveryRealmLineKnowsItsRealm(string line, eRealm realm)
    {
        Assert.That(AllRealmsTeleporter.TryGetDestinationRealm(line, out eRealm actual), Is.True);
        Assert.That(actual, Is.EqualTo(realm));
    }

    [TestCase("Battlegrounds")]
    [TestCase("open BG")]
    [TestCase("Housing")]
    [TestCase("Atlantis")]
    public void SharedLinesBelongToNoRealm(string line) =>
        Assert.That(AllRealmsTeleporter.TryGetDestinationRealm(line, out _), Is.False);

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void PorterRefusesAnEnemyRealmDestinationOnlyInRvr(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        DbTeleport cotswold = InstallDestination("Cotswold Village", eRealm.Albion, 1);
        (CapturingPorter porter, GamePlayer midgardPlayer) = Actors();

        Assert.That(porter.WhisperReceive(midgardPlayer, "Cotswold Village"), Is.True);
        Assert.That(porter.Destination, Is.SameAs(BothWorlds.Expect(world, cotswold, null)));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void PorterServesItsOwnRealmsDestinationsInBothWorlds(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        DbTeleport mularn = InstallDestination("Mularn", eRealm.Midgard, 100);
        CapturingPorter.PorterRealm = eRealm.Midgard;
        (CapturingPorter porter, GamePlayer midgardPlayer) = Actors();

        Assert.That(porter.WhisperReceive(midgardPlayer, "Mularn"), Is.True);
        Assert.That(porter.Destination, Is.SameAs(mularn));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void AnEnemyRealmsPorterServesNobodyOfAnotherRealmInRvr(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        DbTeleport mularn = InstallDestination("Mularn", eRealm.Midgard, 100);
        CapturingPorter.PorterRealm = eRealm.Albion; // a Camelot porter
        (CapturingPorter porter, GamePlayer midgardPlayer) = Actors();

        Assert.That(porter.WhisperReceive(midgardPlayer, "Mularn"), Is.True);
        Assert.That(porter.Destination, Is.SameAs(BothWorlds.Expect(world, mularn, null)));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void ServiceRealmRuleMatchesTheWorld(WorldModelKind world)
    {
        foreach (eRealm own in RealmTravel.PlayableRealms)
        foreach (eRealm service in RealmTravel.PlayableRealms)
            Assert.That(RealmTravel.Allows(world, own, service), Is.EqualTo(world != WorldModelKind.Realm || own == service));
        Assert.That(RealmTravel.Allows(world, eRealm.Albion, eRealm.None), Is.True, "neutral porters and brokers serve all");
    }

    // ---- /tc and Realm Exchange ---------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void TcLeadsToTheCharactersOwnCapitalInBothWorlds(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        Assert.That(TeleportToExchangeCommandHandler.CapitalRegion(eRealm.Albion), Is.EqualTo((ushort)10));
        Assert.That(TeleportToExchangeCommandHandler.CapitalRegion(eRealm.Midgard), Is.EqualTo((ushort)101));
        Assert.That(TeleportToExchangeCommandHandler.CapitalRegion(eRealm.Hibernia), Is.EqualTo((ushort)201));
        Assert.That(TeleportToExchangeCommandHandler.FindBroker(eRealm.Midgard, Array.Empty<GameNPC>()), Is.Null);
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void ExchangeBrokerOfAnEnemyCapitalRefusesInRvr(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        GamePlayer midgardPlayer = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(RealmPlayer));
        Assert.That(RealmTravel.Serves(world, midgardPlayer, eRealm.Albion), Is.EqualTo(world == WorldModelKind.Camlann));
        Assert.That(RealmTravel.Serves(world, midgardPlayer, eRealm.Midgard), Is.True);
    }

    // ---- companions -----------------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void SpawnMenuOffersOnlyTheOwnersRealmInRvr(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        foreach (eRealm owner in RealmTravel.PlayableRealms)
        {
            eRealm[] realms = TemporaryGroupClassCatalog.Offered(world, owner).Select(entry => entry.Realm).Distinct().ToArray();
            Assert.That(realms, Is.EquivalentTo(BothWorlds.Expect(world, RealmTravel.PlayableRealms, new[] { owner })));

            string[] links = Regex.Matches(TemporaryGroupSpawnMenu.BuildMenuText(owner), @"\[([^\]]+)\]")
                .Select(match => match.Groups[1].Value).ToArray();
            string[] expected = TemporaryGroupClassCatalog.Offered(world, owner)
                .Select(entry => $"{TemporaryGroupClassCatalog.RealmName(entry.Realm)}: {entry.CharacterClass}").ToArray();
            Assert.That(links, Is.EquivalentTo(expected));
        }
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void CompanionManagerRecruitTabListsOnlyTheOwnRealmInRvr(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        var entries = (IEnumerable<CompanionManagerEntry>)typeof(CompanionManager)
            .GetMethod("RecruitEntries", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { new List<PlayerCompanionRecord>(), eRealm.Midgard });
        eRealm[] realms = entries.Select(entry => entry.Realm).Distinct().ToArray();
        Assert.That(realms, Is.EquivalentTo(BothWorlds.Expect(world, RealmTravel.PlayableRealms, new[] { eRealm.Midgard })));
    }

    [Test]
    public void CamlannOffersTheFullCatalog()
    {
        using var _ = BothWorlds.Use(WorldModelKind.Camlann);
        Assert.That(TemporaryGroupClassCatalog.Offered(eRealm.Hibernia).ToArray(), Is.EqualTo(TemporaryGroupClassCatalog.All().ToArray()));
    }

    // ---- parties --------------------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void PickupPartiesStaySameRealmInRvr(WorldModelKind world)
    {
        Assert.That(RealmTravel.PartyAccepts(world, eRealm.Albion, new[] { eRealm.Albion, eRealm.Albion }), Is.True);
        Assert.That(RealmTravel.PartyAccepts(world, eRealm.Albion, new[] { eRealm.Albion, eRealm.Midgard }),
            Is.EqualTo(world == WorldModelKind.Camlann));
        Assert.That(RealmTravel.PartyAccepts(world, eRealm.Hibernia, new[] { eRealm.None, eRealm.Hibernia }), Is.True,
            "companions, helpers and staff do not count for the realm rule");
        Assert.That(RealmTravel.PartyAccepts(world, eRealm.Midgard, Array.Empty<eRealm>()), Is.True);
    }

    // ---- bot travel -----------------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void WorldBotsKeepOutOfEnemyHomelandsInRvr(WorldModelKind world)
    {
        bool camlann = world == WorldModelKind.Camlann;
        Assert.That(AutonomousRealmBoundary.Allows(world, eRealm.Midgard, 10, 0), Is.EqualTo(camlann), "Camelot");
        Assert.That(AutonomousRealmBoundary.Allows(world, eRealm.Midgard, 1, 0), Is.EqualTo(camlann), "Albion mainland");
        Assert.That(AutonomousRealmBoundary.Allows(world, eRealm.Albion, 201, 0), Is.EqualTo(camlann), "Tir na Nog");
        Assert.That(AutonomousRealmBoundary.Allows(world, eRealm.Midgard, 101, 0), Is.True, "own capital");
        Assert.That(AutonomousRealmBoundary.Allows(world, eRealm.Midgard, 100, 100), Is.True, "own homeland");
        foreach (ushort frontier in new ushort[] { 11, 12, 14, 15 })
            Assert.That(AutonomousRealmBoundary.Allows(world, eRealm.Midgard, 1, frontier), Is.True, "Albion frontier zones stay open");
        Assert.That(AutonomousRealmBoundary.Allows(world, eRealm.Midgard, 252, 0), Is.False, "battlegrounds only by participation");
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void BattlegroundPlayersLandAtTheirOwnPortalKeepInRvr(WorldModelKind world)
    {
        Assert.That(BattlegroundCampaignPolicy.LeaderLanding(world, true, 2), Is.EqualTo(BothWorlds.Expect<int?>(world, 2, null)));
        Assert.That(BattlegroundCampaignPolicy.LeaderLanding(world, false, 2), Is.Null);
        var landings = new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };
        Assert.That(BattlegroundCampaignPolicy.ChooseLanding(eRealm.Hibernia, BattlegroundCampaignPolicy.LeaderLanding(world, true, 0),
            landings, 0), Is.EqualTo(BothWorlds.Expect(world, 0, 2)));
    }

    // ---- modes ----------------------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void OpenBgPlaysPerRealmInRvrWhateverTheProperty(WorldModelKind world)
    {
        Assert.That(OpenBattlegroundMode.ModeFor(world, "serverpvp"), Is.EqualTo(BothWorlds.Expect(world, OpenBgMode.ServerPvp, OpenBgMode.PerRealm)));
        Assert.That(OpenBattlegroundMode.ModeFor(world, "perrealm"), Is.EqualTo(OpenBgMode.PerRealm));
        Assert.That(OpenBattlegroundMode.PeaceFor(world, "all"), Is.EqualTo(BothWorlds.Expect(world, OpenBgLandingPeace.All, OpenBgLandingPeace.Realm)));
        string line = OpenBattlegroundMode.WorldOverrideLine(world, "serverpvp", "");
        if (world == WorldModelKind.Realm)
            Assert.That(line, Does.Contain("reason=rvr-world"));
        else
            Assert.That(line, Is.Null);
        Assert.That(OpenBattlegroundMode.WorldOverrideLine(world, "perrealm", "realm"), Is.Null, "nothing to ignore, no log line");
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void BgSetupShowsTheEffectiveModeAndWhatTheRvrWorldIgnores(WorldModelKind world)
    {
        bool rvr = world == WorldModelKind.Realm;
        Assert.That(OpenBattlegroundMode.ShownMode(world, "serverpvp"),
            Is.EqualTo(rvr ? "perrealm (ignored: rvr-world; stored serverpvp)" : "serverpvp"));
        Assert.That(OpenBattlegroundMode.ShownMode(world, "perrealm"), Is.EqualTo("perrealm"));
        Assert.That(OpenBattlegroundMode.ShownPeace(world, ""), Is.EqualTo(rvr ? "realm" : "auto"));
        Assert.That(OpenBattlegroundMode.ShownPeace(world, "all"), Is.EqualTo(rvr ? "realm (ignored: rvr-world; stored all)" : "all"));
    }

    [TestCase("odin_necklace", true)]
    [TestCase("battlegrounds_necklace", true)]
    [TestCase("city_necklace", true)]
    [TestCase("df_necklace", true)]
    [TestCase("some_jewel_of_the_mythical_slot", false)]
    [TestCase(null, false)]
    public void FrontierPorterActsOnlyOnItsMedallions(string id, bool porterMedallion) =>
        Assert.That(DOL.GS.Scripts.OFTeleporter.IsPorterMedallion(id), Is.EqualTo(porterMedallion));

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void GothwaiteModeCannotRunInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        Assert.That(GothwaitePvpMode.ActiveIn(world, true), Is.EqualTo(world == WorldModelKind.Camlann));
        Assert.That(GothwaitePvpMode.ActiveIn(world, false), Is.False);

        GothwaitePvpMode.Apply(GothwaitePvpSettings.Defaults with { Enabled = true });
        Assert.That(GothwaitePvpMode.IsActive, Is.EqualTo(world == WorldModelKind.Camlann));
        GothwaitePvpMode.EnforceWorld();
        Assert.That(GothwaitePvpMode.IsActive, Is.EqualTo(world == WorldModelKind.Camlann));
    }

    // ---- release points and hubs -------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void ReleasePointsBelongToTheOwnRealmInRvr(WorldModelKind world)
    {
        bool camlann = world == WorldModelKind.Camlann;
        // Castle Sauvage hub centre (region 1, Albion frontier zone).
        Assert.That(RealmPlaces.IsOwnRealmPoint(world, eRealm.Albion, 1, 11, 585085, 477504, eRealm.None), Is.True);
        Assert.That(RealmPlaces.IsOwnRealmPoint(world, eRealm.Midgard, 1, 11, 585085, 477504, eRealm.None), Is.EqualTo(camlann));
        // Its outer bindstones (hub landing circle).
        Assert.That(RealmPlaces.IsOwnRealmPoint(world, eRealm.Hibernia, 1, 11, 584770, 486230, eRealm.None), Is.EqualTo(camlann));
        // Midgard's own portal keep inside the Albion frontier.
        Assert.That(RealmPlaces.IsOwnRealmPoint(world, eRealm.Midgard, 1, 15, 655200, 293217, eRealm.Midgard), Is.True);
        Assert.That(RealmPlaces.IsOwnRealmPoint(world, eRealm.Albion, 1, 15, 655200, 293217, eRealm.Midgard), Is.EqualTo(camlann));
        // Homelands and capitals.
        Assert.That(RealmPlaces.IsOwnRealmPoint(world, eRealm.Midgard, 1, 0, 561443, 511128, eRealm.None), Is.EqualTo(camlann), "Cotswold");
        Assert.That(RealmPlaces.IsOwnRealmPoint(world, eRealm.Midgard, 101, 0, 30094, 27589, eRealm.None), Is.True, "Jordheim");
        // Open frontier field belongs to nobody.
        Assert.That(RealmPlaces.IsOwnRealmPoint(world, eRealm.Hibernia, 1, 11, 540000, 400000, eRealm.None), Is.True);
    }

    [Test]
    public void HubRealmsComeFromTheBorderKeeps()
    {
        Assert.That(RealmPlaces.HubRealmAt(1, 585085, 477504), Is.EqualTo(eRealm.Albion));
        Assert.That(RealmPlaces.HubRealmAt(100, 766235, 669173), Is.EqualTo(eRealm.Midgard));
        Assert.That(RealmPlaces.HubRealmAt(200, 333229, 419539), Is.EqualTo(eRealm.Hibernia));
        Assert.That(RealmPlaces.HubRealmAt(100, 764890, 672960), Is.EqualTo(eRealm.Midgard), "Svasud outer bindstone");
        Assert.That(RealmPlaces.HubRealmAt(1, 540000, 400000), Is.EqualTo(eRealm.None));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void HubsProtectOnlyTheirOwnRealmInRvr(WorldModelKind world)
    {
        Assert.That(RealmPlaces.HubProtects(world, eRealm.Albion, eRealm.Albion), Is.True);
        Assert.That(RealmPlaces.HubProtects(world, eRealm.Albion, eRealm.Midgard), Is.EqualTo(world == WorldModelKind.Camlann));
        Assert.That(RealmPlaces.HubProtects(world, eRealm.None, eRealm.Midgard), Is.True);
    }
}

/// <summary>
/// Spec 3.2 safe hubs per realm (RvR package R2): in the RvR world a border hub and its landing protect only the
/// hub's realm, and a defender inside its own hub may fight an intruder; capitals still disarm everyone. Camlann
/// keeps every hub safe for everyone.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_RvrSafeHubs
{
    private EpicTestServerScope _server;

    [SetUp]
    public void SetUp() => _server = new EpicTestServerScope();

    [TearDown]
    public void TearDown() => _server.Dispose();

    private sealed class HubPlayer : GamePlayer
    {
        public HubPlayer() : base(null, null) { }

        public override bool IsAlive => true;
        public override byte Level { get; set; } = 50;
        public override eRealm Realm { get; set; }
        public override GameClient Client { get; } = new BotDummyClient();
        public override bool IsInvulnerableToAttack => false;
        public override ushort CurrentRegionID { get; set; } = 1;
        public override bool SafetyFlag { get; set; }
        public override int X { get; set; }
        public override int Y { get; set; }
        public override short Race { get; set; } = (short)eRace.Briton;
        public override eGender Gender { get; set; } = eGender.Male;
        public override string Name { get; set; } = "Tester";
    }

    // Castle Sauvage, Albion's border hub in region 1, and a field well outside it.
    private const int HubX = 585085, HubY = 477504, FieldX = 540000, FieldY = 400000;

    private static HubPlayer At(eRealm realm, int x, int y, ushort region = 1) =>
        new() { Realm = realm, X = x, Y = y, CurrentRegionID = region };

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void OwnRealmPlayerIsSafeInItsOwnHub(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        Assert.That(PvpCombatant.IsSafeArea(At(eRealm.Albion, HubX, HubY)), Is.True);
        Assert.That(PvpCombatant.IsSafeArea(At(eRealm.Albion, 584340, 486620)), Is.True, "outer bindstone landing");
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void EnemyInTheHubIsSafeOnlyInCamlann(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        bool camlann = world == WorldModelKind.Camlann;
        Assert.That(PvpCombatant.IsSafeArea(At(eRealm.Midgard, HubX, HubY)), Is.EqualTo(camlann));
        Assert.That(PvpCombatant.IsSafeArea(At(eRealm.Hibernia, 584340, 486620)), Is.EqualTo(camlann), "outer bindstone landing");
        Assert.That(PvpCombatant.IsSafeArea(At(eRealm.Midgard, FieldX, FieldY)), Is.False, "the open frontier is never safe");
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void DefenderInItsHubMayAttackAnIntruderOnlyInRvr(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        HubPlayer defender = At(eRealm.Albion, HubX, HubY);
        HubPlayer intruder = At(eRealm.Midgard, HubX + 200, HubY);
        bool rvr = world == WorldModelKind.Realm;

        Assert.That(rules.IsAllowedToAttack(defender, intruder, true), Is.EqualTo(rvr), "home realm fights the intruder");
        Assert.That(rules.IsAllowedToAttack(intruder, defender, true), Is.False, "the hub still protects its own realm");
        Assert.That(PvpCombatant.BlocksAttackFromSafeArea(defender, intruder), Is.EqualTo(!rvr));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void DefenderInItsHubCannotSnipeIntoTheFieldInEitherWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        IServerRules rules = BothWorlds.Rules(world);
        HubPlayer defender = At(eRealm.Albion, HubX, HubY);
        HubPlayer outside = At(eRealm.Midgard, HubX + 3_600, HubY); // just beyond the 3,500-unit circle

        Assert.That(PvpCombatant.IsSafeArea(outside), Is.False);
        Assert.That(PvpCombatant.BlocksAttackFromSafeArea(defender, outside), Is.True);
        Assert.That(rules.IsAllowedToAttack(defender, outside, true), Is.False, "no attacking out of cover");
        Assert.That(rules.IsAllowedToAttack(outside, defender, true), Is.False, "and the defender stays protected");
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void IntruderInsideAHubOfAnotherRealmIsNotCoveredByIt(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        HubPlayer intruder = At(eRealm.Midgard, HubX, HubY);
        HubPlayer albionInField = At(eRealm.Albion, FieldX, FieldY);
        // The hub is Albion's: in the RvR world it neither shields nor disarms a Midgard visitor.
        Assert.That(PvpCombatant.HubRealmOf(intruder), Is.EqualTo(eRealm.Albion));
        Assert.That(PvpCombatant.BlocksAttackFromSafeArea(intruder, albionInField), Is.EqualTo(world == WorldModelKind.Camlann));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void CapitalsStillDisarmEveryoneInBothWorlds(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        HubPlayer inCamelot = At(eRealm.Albion, 36240, 29695, 10);
        Assert.That(PvpCombatant.IsSafeArea(inCamelot), Is.True);
        Assert.That(PvpCombatant.BlocksAttackFromSafeArea(inCamelot, At(eRealm.Albion, 36300, 29695, 10)), Is.True);
        Assert.That(PvpCombatant.BlocksAttackFromSafeArea(At(eRealm.Albion, FieldX, FieldY), At(eRealm.Midgard, FieldX + 100, FieldY)), Is.False);
    }
}
