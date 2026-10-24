using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.Tests.UnitTests;

[TestFixture]
public class UT_CamlannWorldModel
{
    [Test]
    public void PvPWithTheExpectedMarkerIsSupported()
    {
        var marker = new DbOfflineLocalOption { Key = CamlannWorldModel.MarkerKey, Value = CamlannWorldModel.MarkerValue };

        Assert.That(CamlannWorldModel.IsSupported(EGameServerType.GST_PvP, marker), Is.True);
    }

    [TestCase(EGameServerType.GST_Normal)]
    [TestCase(EGameServerType.GST_PvE)]
    [TestCase(EGameServerType.GST_Test)]
    public void NonPvPServerTypesAreNeverSupported(EGameServerType serverType)
    {
        var marker = new DbOfflineLocalOption { Key = CamlannWorldModel.MarkerKey, Value = CamlannWorldModel.MarkerValue };

        Assert.That(CamlannWorldModel.IsSupported(serverType, marker), Is.False);
    }

    [Test]
    public void MissingOrDifferentMarkerIsNeverSupported()
    {
        Assert.That(CamlannWorldModel.IsSupported(EGameServerType.GST_PvP, null), Is.False);
        Assert.That(CamlannWorldModel.IsSupported(EGameServerType.GST_PvP,
            new DbOfflineLocalOption { Key = CamlannWorldModel.MarkerKey, Value = "Normal" }), Is.False);
    }

    // Two worlds since the RvR mode (R1a): Camlann-1 and Realm-1, both on PvP.

    [Test]
    public void BothMarkersParseToTheirWorldOnPvP()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CamlannWorldModel.Parse(EGameServerType.GST_PvP, Marker("Camlann-1")), Is.EqualTo(WorldModelKind.Camlann));
            Assert.That(CamlannWorldModel.Parse(EGameServerType.GST_PvP, Marker("Realm-1")), Is.EqualTo(WorldModelKind.Realm));
            Assert.That(CamlannWorldModel.IsSupported(EGameServerType.GST_PvP, Marker(CamlannWorldModel.RealmMarkerValue)), Is.True);
            Assert.That(CamlannWorldModel.CamlannMarkerValue, Is.EqualTo(CamlannWorldModel.MarkerValue));
        });
    }

    [TestCase(EGameServerType.GST_Normal)]
    [TestCase(EGameServerType.GST_PvE)]
    [TestCase(EGameServerType.GST_Test)]
    public void RealmMarkerIsRefusedOffPvP(EGameServerType serverType)
    {
        Assert.That(CamlannWorldModel.Parse(serverType, Marker(CamlannWorldModel.RealmMarkerValue)), Is.EqualTo(WorldModelKind.Unsupported));
        Assert.That(CamlannWorldModel.IsSupported(serverType, Marker(CamlannWorldModel.RealmMarkerValue)), Is.False);
    }

    [Test]
    public void SaveWithoutMarkerIsRefused()
    {
        Assert.That(CamlannWorldModel.Parse(EGameServerType.GST_PvP, null), Is.EqualTo(WorldModelKind.Unsupported));
        Assert.That(CamlannWorldModel.Parse(EGameServerType.GST_PvP, Marker(null)), Is.EqualTo(WorldModelKind.Unsupported));
        Assert.That(CamlannWorldModel.Parse(EGameServerType.GST_PvP, Marker("")), Is.EqualTo(WorldModelKind.Unsupported));
    }

    [TestCase("realm-1")]
    [TestCase("REALM-1")]
    [TestCase(" Realm-1")]
    [TestCase("Realm-1 ")]
    [TestCase("Realm-2")]
    [TestCase("Realm")]
    [TestCase("camlann-1")]
    [TestCase("Normal")]
    public void UnknownOrMisspelledMarkersAreRefused(string value)
    {
        Assert.That(CamlannWorldModel.ParseMarker(value), Is.EqualTo(WorldModelKind.Unsupported));
        Assert.That(CamlannWorldModel.IsSupported(EGameServerType.GST_PvP, Marker(value)), Is.False);
    }

    [TestCase(WorldModelKind.Camlann, "Camlann-1")]
    [TestCase(WorldModelKind.Realm, "Realm-1")]
    [TestCase(WorldModelKind.Unsupported, null)]
    public void MarkerValueRoundTrips(WorldModelKind world, string marker)
    {
        Assert.That(CamlannWorldModel.MarkerValueFor(world), Is.EqualTo(marker));
        if (marker != null)
            Assert.That(CamlannWorldModel.ParseMarker(marker), Is.EqualTo(world));
    }

    private static DbOfflineLocalOption Marker(string value) =>
        new() { Key = CamlannWorldModel.MarkerKey, Value = value };
}
