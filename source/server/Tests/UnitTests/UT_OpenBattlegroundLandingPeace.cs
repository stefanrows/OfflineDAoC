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
        AutonomousHubDeparture.OverrideOpenBgLandings(null);
        Assert.That(AutonomousHubDeparture.OpenBgLandings(), Is.Empty);
    }

    [Test]
    public void TheLandingIsASafeAnchorWithABandAndADepartureClock()
    {
        AutonomousHubDeparture.OverrideOpenBgLandings(Landings);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousHubDeparture.TryGetSafeAnchor(eRealm.Midgard, 252, 50500, 27000, out var anchor), Is.True,
                    "the Midgard camp lies inside the Midgard landing circle");
                Assert.That(anchor.Radius, Is.EqualTo(AutonomousHubDeparture.OpenBgLandingPeaceRadius));
                Assert.That(anchor.HubName, Does.Contain("Midgard"));
                Assert.That(AutonomousHubDeparture.TryGetSafeAnchor(eRealm.Albion, 252, 50500, 27000, out _), Is.False,
                    "an Albion bot at the Midgard camp has no anchor there");
                Assert.That(AutonomousHubDeparture.TryGetSafeAnchor(eRealm.Midgard, 252, 54053 + 6_400, 24680, out _), Is.False,
                    "6,400 out is past the circle: the clock, not the anchor, keeps the peace there");
                Assert.That(AutonomousHubDeparture.InHubBand(eRealm.Midgard, 252, 54053 + 7_900, 24680), Is.True,
                    "the band reaches circle + 2,500");
                Assert.That(AutonomousHubDeparture.InHubBand(eRealm.Midgard, 252, 54053 + 8_100, 24680), Is.False);
                Assert.That(AutonomousHubDeparture.InHubBand(eRealm.Hibernia, 252, 54053 + 1_000, 24680), Is.False, "other realm");
                Assert.That(AutonomousHubDeparture.IsOpenBgLandingRegion(252), Is.True);
                Assert.That(AutonomousHubDeparture.IsOpenBgLandingRegion(238), Is.False);
            });
        }
        finally
        {
            AutonomousHubDeparture.OverrideOpenBgLandings(null);
        }
    }

    [Test]
    public void AllPeaceCoversEveryLandingForEveryRealmAndRealmPeaceOnlyTheOwn()
    {
        Assert.Multiple(() =>
        {
            // An Albion bot standing at the Midgard camp.
            Assert.That(AutonomousHubDeparture.OpenBgPeaceInCircle(OpenBgLandingPeace.All, 252, 50500, 27000, eRealm.Albion,
                Landings), Is.True, "all: any landing protects any realm");
            Assert.That(AutonomousHubDeparture.OpenBgPeaceInCircle(OpenBgLandingPeace.Realm, 252, 50500, 27000, eRealm.Albion,
                Landings), Is.False, "realm: only the own landing");
            Assert.That(AutonomousHubDeparture.OpenBgPeaceInCircle(OpenBgLandingPeace.Realm, 252, 54053, 24680, eRealm.Midgard,
                Landings), Is.True);
            Assert.That(AutonomousHubDeparture.OpenBgPeaceInCircle(OpenBgLandingPeace.All, 252, 34333, 38275, eRealm.Albion,
                Landings), Is.False, "the keep gate is open field in both");
            Assert.That(AutonomousHubDeparture.OpenBgPeaceInCircle(OpenBgLandingPeace.All, 238, 50500, 27000, eRealm.Albion,
                Landings), Is.False, "another region");
            Assert.That(AutonomousHubDeparture.OpenBgPeaceInCircle(OpenBgLandingPeace.All, 252, 50500, 27000, eRealm.None,
                Landings), Is.False, "no realm, no peace");
        });
    }

    [Test]
    public void OpenBgPeaceModeNeverChangesRegionsOutsideTheOpenBg()
    {
        Assert.That(AutonomousHubDeparture.OpenBgPeaceInCircle(OpenBgLandingPeace.All, 1, 37301, 52362, eRealm.Albion, Landings), Is.False,
            "the same coordinates in region 1 are not a landing");
    }
}
