using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.GS.Tests;

// WP1.2/1.3 (docs/TOA_TRAVEL.md): every porter offers [Atlantis], [Atlantis Havens] and [Atlantis Dungeons];
// the destination is always the asking player's own realm copy; random loot from kills inside Atlantis is a
// Trials of Atlantis item, outside it is unchanged.
[TestFixture]
public sealed class UT_AtlantisTravel
{
    private interface IPortCapture
    {
        DbTeleport Destination { get; }
        List<string> Said { get; }
    }

    private sealed class AlbionPorter : AlbionTeleporter, IPortCapture
    {
        public DbTeleport Destination { get; private set; }
        public List<string> Said { get; } = new();
        public override int X { get => 0; set { } }
        public override int Y { get => 0; set { } }
        public override int Z { get => 0; set { } }
        public override void SayTo(GamePlayer player, string message, bool announce = true) => Said.Add(message);
        protected override void OnTeleportSpell(GamePlayer player, DbTeleport destination) => Destination = destination;
    }

    private sealed class LivePorter : DOL.GS.Scripts.LiveTeleporter, IPortCapture
    {
        public DbTeleport Destination { get; private set; }
        public List<string> Said { get; } = new();
        public override int X { get => 0; set { } }
        public override int Y { get => 0; set { } }
        public override int Z { get => 0; set { } }
        public override void SayTo(GamePlayer player, string message, bool announce = true) => Said.Add(message);
        protected override void OnTeleportSpell(GamePlayer player, DbTeleport destination) => Destination = destination;
    }

    private sealed class RealmPlayer : GamePlayer
    {
        public RealmPlayer() : base(null, null) { }
        public eRealm TestRealm;
        public override eRealm Realm { get => TestRealm; set { } }
    }

    private static readonly FieldInfo TeleportLocations =
        typeof(WorldMgr).GetField("m_teleportLocations", BindingFlags.Static | BindingFlags.NonPublic)!;

    private object _previousTeleports;
    private int _previousPlvl;
    private bool _previousToaLoot;
    private int _previousToaChance;

    [SetUp]
    public void SetUp()
    {
        _previousTeleports = TeleportLocations.GetValue(null);
        _previousPlvl = Properties.ATLANTIS_TELEPORT_PLVL;
        _previousToaLoot = Properties.TOA_LOOT_IN_ATLANTIS;
        _previousToaChance = Properties.ROG_TOA_ITEM_CHANCE;
        Properties.ATLANTIS_TELEPORT_PLVL = 1;
        TeleportLocations.SetValue(null, AtlantisRows());
    }

    [TearDown]
    public void TearDown()
    {
        TeleportLocations.SetValue(null, _previousTeleports);
        Properties.ATLANTIS_TELEPORT_PLVL = _previousPlvl;
        Properties.TOA_LOOT_IN_ATLANTIS = _previousToaLoot;
        Properties.ROG_TOA_ITEM_CHANCE = _previousToaChance;
    }

    // One fake row per realm and destination, in that realm's copy, like pack 026 writes them.
    private static Dictionary<eRealm, Dictionary<string, DbTeleport>> AtlantisRows()
    {
        var rows = new Dictionary<eRealm, Dictionary<string, DbTeleport>>();
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            rows[realm] = new Dictionary<string, DbTeleport>();
            foreach (string destination in AtlantisTravel.Destinations)
                rows[realm][AtlantisTravel.TeleportType + ":" + destination] = new DbTeleport
                {
                    Type = AtlantisTravel.TeleportType, TeleportID = destination, Realm = (int)realm,
                    RegionID = ExpectedRegion(realm, destination), X = 1000, Y = 2000, Z = 3000,
                };
        }

        return rows;
    }

    /// <summary>The region of a destination in a realm's copy (zone points of pack 022).</summary>
    public static int ExpectedRegion(eRealm realm, string destination)
    {
        int index = realm switch { eRealm.Albion => 0, eRealm.Midgard => 1, _ => 2 };
        return destination switch
        {
            AtlantisTravel.Hub => new[] { 70, 71, 72 }[index],
            "Cetus' Pit" => new[] { 78, 35, 135 }[index],
            "Sobekite Eternal" => new[] { 79, 36, 136 }[index],
            "Temple of Twilight" => new[] { 80, 37, 137 }[index],
            "Halls of Ma'ati" => new[] { 83, 40, 140 }[index],
            "Great Pyramid" => new[] { 88, 45, 145 }[index],
            "City of Aerus" => new[] { 90, 47, 147 }[index],
            _ => new[] { 73, 30, 130 }[index], // the four havens are in the overworld copy
        };
    }

    private static T Porter<T>() where T : GameNPC, IPortCapture
    {
        T porter = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        typeof(T).GetProperty(nameof(IPortCapture.Said))!.GetBackingField().SetValue(porter, new List<string>());
        return porter;
    }

    private static RealmPlayer Player(eRealm realm)
    {
        var player = (RealmPlayer)RuntimeHelpers.GetUninitializedObject(typeof(RealmPlayer));
        player.TestRealm = realm;
        return player;
    }

    [Test]
    public void MenuShowsTheAtlantisLineOnlyWhenTheAccountMayEnter()
    {
        string open = AllRealmsTeleporter.BuildTravelMenu(false, true);
        Assert.That(open, Does.Contain("[Atlantis] [Atlantis Havens] [Atlantis Dungeons]"));
        Assert.That(open.IndexOf("[Atlantis]", StringComparison.Ordinal),
            Is.GreaterThan(open.IndexOf("[Hibernia Oceanus]", StringComparison.Ordinal)), "after the Oceanus line");
        Assert.That(AllRealmsTeleporter.BuildTravelMenu(false, false), Does.Not.Contain("[Atlantis]"));
        Assert.That(AllRealmsTeleporter.BuildTravelMenu(true), Does.Not.Contain("[Atlantis]"), "the old overload is unchanged");

        Properties.ATLANTIS_TELEPORT_PLVL = 1;
        Assert.That(AtlantisTravel.CanTravel(Player(eRealm.Albion)), Is.True, "an account-less player counts as privilege 1");
        Properties.ATLANTIS_TELEPORT_PLVL = 4;
        Assert.That(AtlantisTravel.CanTravel(Player(eRealm.Albion)), Is.False);
        Assert.That(AtlantisTravel.CanTravel(3), Is.False);
    }

    [TestCase(eRealm.Albion, 70)]
    [TestCase(eRealm.Midgard, 71)]
    [TestCase(eRealm.Hibernia, 72)]
    public void AtlantisLinePortsToTheRuinsOfThePlayersRealm(eRealm realm, int hub)
    {
        // An Albion porter and an Albion LiveTeleporter: the porter's realm does not matter, the player's does.
        foreach (IPortCapture porter in new IPortCapture[] { Porter<AlbionPorter>(), Porter<LivePorter>() })
        {
            Assert.That(((GameNPC)porter).WhisperReceive(Player(realm), "Atlantis"), Is.True);
            Assert.That(porter.Destination, Is.Not.Null);
            Assert.That(porter.Destination.RegionID, Is.EqualTo(hub));
            Assert.That(porter.Destination.Realm, Is.EqualTo((int)realm));
            Assert.That(AtlantisRegions.HubOf(realm), Is.EqualTo(hub));
        }
    }

    [TestCase(eRealm.Albion)]
    [TestCase(eRealm.Midgard)]
    [TestCase(eRealm.Hibernia)]
    public void EveryHavenAndDungeonResolvesInTheOwnCopy(eRealm realm)
    {
        foreach (string destination in AtlantisTravel.Havens.Concat(AtlantisTravel.Dungeons))
        {
            AlbionPorter porter = Porter<AlbionPorter>();
            Assert.That(porter.WhisperReceive(Player(realm), destination.ToLowerInvariant()), Is.True, destination);
            Assert.That(porter.Destination?.RegionID, Is.EqualTo(ExpectedRegion(realm, destination)), destination);
            Assert.That(AtlantisRegions.Contains(porter.Destination.RegionID), Is.True, destination);
        }

        Assert.That(AtlantisRegions.OverworldOf(realm), Is.EqualTo(ExpectedRegion(realm, "Haven of Oceanus")));
    }

    [Test]
    public void SubMenusListTheirDestinations()
    {
        AlbionPorter porter = Porter<AlbionPorter>();
        Assert.That(porter.WhisperReceive(Player(eRealm.Midgard), "atlantis havens"), Is.True);
        Assert.That(porter.WhisperReceive(Player(eRealm.Midgard), "Atlantis Dungeons"), Is.True);
        Assert.That(porter.Destination, Is.Null, "a menu line does not port");
        Assert.That(porter.Said, Is.EqualTo(new[] { AtlantisTravel.HavensMenu, AtlantisTravel.DungeonsMenu }));
        foreach (string haven in AtlantisTravel.Havens)
            Assert.That(AtlantisTravel.HavensMenu, Does.Contain("[" + haven + "]"));
        foreach (string dungeon in AtlantisTravel.Dungeons)
            Assert.That(AtlantisTravel.DungeonsMenu, Does.Contain("[" + dungeon + "]"));
        Assert.That(AtlantisTravel.Destinations, Has.Count.EqualTo(11));
    }

    [Test]
    public void ClosedAtlantisRefusesAndAMissingRowDoesNotPort()
    {
        Properties.ATLANTIS_TELEPORT_PLVL = 4;
        AlbionPorter porter = Porter<AlbionPorter>();
        Assert.That(porter.WhisperReceive(Player(eRealm.Albion), "Atlantis"), Is.True);
        Assert.That(porter.WhisperReceive(Player(eRealm.Albion), "Atlantis Havens"), Is.True);
        Assert.That(porter.Destination, Is.Null);
        Assert.That(porter.Said, Is.EqualTo(new[] { AtlantisTravel.NotAuthorized, AtlantisTravel.NotAuthorized }));

        Properties.ATLANTIS_TELEPORT_PLVL = 1;
        TeleportLocations.SetValue(null, new Dictionary<eRealm, Dictionary<string, DbTeleport>>());
        porter = Porter<AlbionPorter>();
        Assert.That(porter.WhisperReceive(Player(eRealm.Hibernia), "Haven of Stygia"), Is.True);
        Assert.That(porter.Destination, Is.Null, "no pack 026 row, no port");
    }

    [TestCase(73, true), TestCase(30, true), TestCase(130, true)]
    [TestCase(70, true), TestCase(71, true), TestCase(72, true)]
    [TestCase(79, true), TestCase(40, true), TestCase(137, true)]
    [TestCase(88, true), TestCase(46, true), TestCase(147, true), TestCase(91, true)]
    [TestCase(1, false), TestCase(100, false), TestCase(200, false), TestCase(51, false)]
    [TestCase(249, false), TestCase(10, false), TestCase(252, false), TestCase(93, false)]
    public void ToaLootIsTheRuleInsideAtlantisOnly(int region, bool inside)
    {
        Properties.TOA_LOOT_IN_ATLANTIS = true;
        Assert.That(AtlantisRegions.Contains(region), Is.EqualTo(inside));
        Assert.That(AtlantisRegions.UsesToaLoot(region), Is.EqualTo(inside));
        Properties.TOA_LOOT_IN_ATLANTIS = false;
        Assert.That(AtlantisRegions.UsesToaLoot(region), Is.False, "the switch turns the rule off everywhere");
    }

    private static readonly eProperty[] CapBonuses =
    {
        eProperty.PowerPool, eProperty.PowerPoolCapBonus, eProperty.StrCapBonus, eProperty.DexCapBonus,
        eProperty.ConCapBonus, eProperty.QuiCapBonus, eProperty.MaxHealthCapBonus, eProperty.AcuCapBonus,
    };

    private static IEnumerable<eProperty> Bonuses(DbItemTemplate item) => new[]
    {
        item.Bonus1Type, item.Bonus2Type, item.Bonus3Type, item.Bonus4Type, item.Bonus5Type, item.Bonus6Type,
        item.Bonus7Type, item.Bonus8Type, item.Bonus9Type, item.Bonus10Type, item.ExtraBonusType,
    }.Where(type => type != 0).Select(type => (eProperty)type);

    [Test]
    public void AtlantisLootRollsToaCapBonusesAndOtherLootNever()
    {
        // Set the ROG properties before the first touch of GeneratedUniqueItem: its static initialisers copy
        // several of them once per process (UT_AutonomousLootFlow does the same).
        var rogDefaults = new Dictionary<FieldInfo, object>();
        foreach (FieldInfo field in typeof(Properties).GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(field => field.Name.StartsWith("ROG_")))
        {
            rogDefaults[field] = field.GetValue(null);
            field.SetValue(null, field.GetCustomAttribute<ServerPropertyAttribute>()!.DefaultValue);
        }

        var prefixes = (IDictionary)typeof(GeneratedUniqueItem).GetField("hPropertyToMagicPrefix",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        if (prefixes.Count == 0)
            GeneratedUniqueItem.InitializeHashtables();

        try
        {
            Assert.That(Properties.ROG_TOA_ITEM_CHANCE, Is.EqualTo(25), "code default");
            int toaItemsWithCap = 0;
            for (int i = 0; i < 300; i++)
            {
                eRealm realm = (eRealm)(1 + i % 3);
                eCharacterClass characterClass = realm switch
                {
                    eRealm.Albion => eCharacterClass.Armsman, eRealm.Midgard => eCharacterClass.Healer, _ => eCharacterClass.Eldritch,
                };
                GeneratedUniqueItem outside = AtlasROGManager.GenerateMonsterLootROG(realm, characterClass, 51, false, false);
                Assert.That(Bonuses(outside).Intersect(CapBonuses), Is.Empty, "toa false never rolls a cap bonus");
                GeneratedUniqueItem inside = AtlasROGManager.GenerateMonsterLootROG(realm, characterClass, 51, false, true);
                if (Bonuses(inside).Intersect(CapBonuses).Any())
                    toaItemsWithCap++;
            }

            Assert.That(toaItemsWithCap, Is.InRange(60, 290), "about two in three level-51 ToA items carry a cap bonus at 25 %");

            // 100 % per bonus: every bonus slot asks for a cap bonus; the free-slot guard keeps the generator from
            // picking from an empty list.
            Properties.ROG_TOA_ITEM_CHANCE = 100;
            for (int i = 0; i < 200; i++)
                Assert.DoesNotThrow(() => GeneratedUniqueItem.Create(true, eRealm.Hibernia, eCharacterClass.Hero, 51, 35));
        }
        finally
        {
            foreach (var entry in rogDefaults)
                entry.Key.SetValue(null, entry.Value);
        }
    }
}

internal static class AutoPropertyBackingField
{
    public static FieldInfo GetBackingField(this PropertyInfo property) =>
        property.DeclaringType!.GetField($"<{property.Name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
}
