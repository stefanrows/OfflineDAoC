using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_OpenBattlegroundMode
{
    [Test]
    public void ModeParsesCaseInsensitivelyAndUnknownMeansServerPvp()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundMode.ParseMode("perrealm"), Is.EqualTo(OpenBgMode.PerRealm));
            Assert.That(OpenBattlegroundMode.ParseMode(" PerRealm "), Is.EqualTo(OpenBgMode.PerRealm));
            Assert.That(OpenBattlegroundMode.ParseMode("serverpvp"), Is.EqualTo(OpenBgMode.ServerPvp));
            Assert.That(OpenBattlegroundMode.ParseMode(""), Is.EqualTo(OpenBgMode.ServerPvp));
            Assert.That(OpenBattlegroundMode.ParseMode(null), Is.EqualTo(OpenBgMode.ServerPvp));
            Assert.That(OpenBattlegroundMode.ParseMode("garbage"), Is.EqualTo(OpenBgMode.ServerPvp));
        });
    }

    [Test]
    public void PeaceDefaultFollowsTheModeAndAnExplicitValueWins()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundMode.ParsePeace("", OpenBgMode.ServerPvp), Is.EqualTo(OpenBgLandingPeace.All));
            Assert.That(OpenBattlegroundMode.ParsePeace("", OpenBgMode.PerRealm), Is.EqualTo(OpenBgLandingPeace.Realm));
            Assert.That(OpenBattlegroundMode.ParsePeace("realm", OpenBgMode.ServerPvp), Is.EqualTo(OpenBgLandingPeace.Realm));
            Assert.That(OpenBattlegroundMode.ParsePeace("all", OpenBgMode.PerRealm), Is.EqualTo(OpenBgLandingPeace.All));
            Assert.That(OpenBattlegroundMode.ParsePeace("nonsense", OpenBgMode.PerRealm), Is.EqualTo(OpenBgLandingPeace.Realm));
        });
    }

    [Test]
    public void RealmsAreFriendsOnlyInPerRealmModeInsideRegion252()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundMode.RealmsAreFriends(OpenBgMode.PerRealm, 252, eRealm.Albion, eRealm.Albion), Is.True);
            Assert.That(OpenBattlegroundMode.RealmsAreFriends(OpenBgMode.PerRealm, 252, eRealm.Albion, eRealm.Midgard), Is.False);
            Assert.That(OpenBattlegroundMode.RealmsAreFriends(OpenBgMode.ServerPvp, 252, eRealm.Albion, eRealm.Albion), Is.False);
            Assert.That(OpenBattlegroundMode.RealmsAreFriends(OpenBgMode.PerRealm, 1, eRealm.Albion, eRealm.Albion), Is.False, "region 1 stays Camlann");
            Assert.That(OpenBattlegroundMode.RealmsAreFriends(OpenBgMode.PerRealm, 238, eRealm.Midgard, eRealm.Midgard), Is.False, "238 stays Camlann");
            Assert.That(OpenBattlegroundMode.RealmsAreFriends(OpenBgMode.PerRealm, 252, eRealm.None, eRealm.None), Is.False, "no realm is never a friend");
        });
    }
}
