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
}
