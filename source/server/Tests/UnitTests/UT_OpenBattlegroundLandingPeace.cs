using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Same-realm hub peace at the open BG landings (0.246.0): the arrival camps 4,000-5,000 units from each realm's
/// portal keep held 891 of 1,144 same-realm crew deaths in the 0.245.0 run. A circle like the bindstone peace of
/// bug 63, never a realm alliance: the field, bridges and keep stay Camlann free-for-all.
/// </summary>
[TestFixture]
public sealed class UT_OpenBattlegroundLandingPeace
{
    private static readonly (ushort Region, int X, int Y, eRealm Realm)[] Landings =
    {
        (252, 18362, 18257, eRealm.Hibernia), (252, 54053, 24680, eRealm.Midgard), (252, 37301, 52362, eRealm.Albion),
    };

    private static bool Near(int x, int y, eRealm realm, ushort region = 252) =>
        AutonomousHubDeparture.NearOwnLanding(region, x, y, realm, AutonomousHubDeparture.OpenBgLandingPeaceRadius, Landings);

    [Test]
    public void TheCircleCoversTheLandingAndTheArrivalCampOfTheOwnRealmOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Near(54053, 24680, eRealm.Midgard), Is.True, "on the Midgard landing");
            Assert.That(Near(50500, 27000, eRealm.Midgard), Is.True, "the Midgard arrival camp, 4,250 out");
            Assert.That(Near(50500, 27000, eRealm.Albion), Is.False, "an Albion bot at the Midgard camp is fair game");
            Assert.That(Near(50500, 27000, eRealm.Midgard, 238), Is.False, "another region");
            Assert.That(Near(50500, 27000, eRealm.None), Is.False);
            Assert.That(Near(38656, 36096, eRealm.Midgard), Is.False, "the east bridge is open field");
            Assert.That(Near(37301, 47000, eRealm.Albion), Is.True, "the Albion camp, 5,362 out");
            Assert.That(Near(34333, 38275, eRealm.Albion), Is.False, "the keep gate, 14,000 from the Albion landing");
            Assert.That(Near(18362, 18257, eRealm.Hibernia), Is.True);
        });
    }

    [Test]
    public void TheRadiusStaysBetweenTheCampsAndTheBridges()
    {
        // Camps lie 4,000-5,000 from the landings; the nearest bridge (SW, from the Albion landing) 14,000.
        Assert.That(AutonomousHubDeparture.OpenBgLandingPeaceRadius, Is.InRange(5_000, 8_000));
        Assert.That(AutonomousHubDeparture.NearOwnLanding(252, 0, 0, eRealm.Albion, 1_000, null), Is.False, "no landings yet");
    }

    [Test]
    public void WithoutAWorldThereAreNoLandingsAndNoException()
    {
        Assert.That(AutonomousHubDeparture.OpenBgLandings(), Is.Empty);
    }
}
