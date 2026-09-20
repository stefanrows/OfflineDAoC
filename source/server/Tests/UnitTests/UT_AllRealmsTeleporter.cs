using DOL.GS;
using NUnit.Framework;

namespace DOL.GS.Tests;

[TestFixture]
public sealed class UT_AllRealmsTeleporter
{
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
