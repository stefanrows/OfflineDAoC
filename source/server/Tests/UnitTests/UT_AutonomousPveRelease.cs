using System;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Bug 144: PvE world bots release at their own bind and may open their own
/// realm's border gates; RvR bots keep the hub release.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_AutonomousPveRelease
{
    // Live region 100 bind rows (Realm 0, owned by Midgard): the Svasud Faste
    // outer and keep stones and three Mularn-area stones in the Vale of Mularn.
    private static readonly DbBindPoint[] Region100Binds =
    {
        new() { X = 764082, Y = 672416, Z = 5852, Realm = 0 },
        new() { X = 765147, Y = 668315, Z = 5736, Realm = 0 },
        new() { X = 803816, Y = 726487, Z = 4776, Realm = 0 },
        new() { X = 804518, Y = 724128, Z = 4680, Realm = 0 },
        new() { X = 804487, Y = 701766, Z = 4956, Realm = 0 },
    };

    // Region 1: Cotswold and the two Castle Sauvage outer stones.
    private static readonly DbBindPoint[] Region1Binds =
    {
        new() { X = 560491, Y = 511708, Z = 2000, Realm = 0 },
        new() { X = 584770, Y = 486230, Z = 2600, Realm = 0 },
        new() { X = 584638, Y = 486320, Z = 2600, Realm = 0 },
    };

    [TearDown]
    public void ClearBinds()
    {
        BotReleaseBindPoints.Replace(1, Array.Empty<DbBindPoint>());
        BotReleaseBindPoints.Replace(100, Array.Empty<DbBindPoint>());
    }

    [Test]
    public void OnlyRvrAndBattlegroundDeathsKeepTheHubRelease()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BotReleaseBindPoints.UsesPveBindRelease(rvrObjective: false, battlegroundRegion: false), Is.True);
            Assert.That(BotReleaseBindPoints.UsesPveBindRelease(rvrObjective: true, battlegroundRegion: false), Is.False,
                "RvR death: Svasud Faste hub release");
            Assert.That(BotReleaseBindPoints.UsesPveBindRelease(rvrObjective: false, battlegroundRegion: true), Is.False,
                "battleground death: campaign landing");
            Assert.That(AutonomousRvrStaging.TryGetBorderKeep(eRealm.Midgard, out var hub) && hub.Name == "Svasud Faste" &&
                hub.RegionId == 100, Is.True);
        });
    }

    [Test]
    public void MidgardPveDeathInTheValeReleasesAtMularnHomeBind()
    {
        BotReleaseBindPoints.Replace(100, Region100Binds);
        // Died near the Svasud-side gate, where the old safe-only rule and the
        // plain nearest stone both pick the Svasud Faste outer stone.
        Point3D release = BotReleaseBindPoints.PveRelease(100, 767000, 677000, eRealm.Midgard, 0,
            homeRegion: 100, homeX: 803816, homeY: 726487, homeZ: 4776, out bool homeBind);
        Assert.Multiple(() =>
        {
            Assert.That(homeBind, Is.True);
            Assert.That((release.X, release.Y), Is.EqualTo((803816, 726487)));
        });
    }

    [Test]
    public void MidgardPveDeathWithoutHomeBindInRegionTakesNearestOwnBindNotTheSanctuary()
    {
        BotReleaseBindPoints.Replace(100, Region100Binds);
        // Home bind in Jordheim (region 101): nearest Midgard stone of the Vale.
        Point3D release = BotReleaseBindPoints.PveRelease(100, 790000, 720000, eRealm.Midgard, 0,
            homeRegion: 101, homeX: 32020, homeY: 28294, homeZ: 8803, out bool homeBind);
        Assert.Multiple(() =>
        {
            Assert.That(homeBind, Is.False);
            Assert.That((release.X, release.Y), Is.EqualTo((804518, 724128)), "Mularn, not Svasud Faste");
        });
    }

    [Test]
    public void AlbionPveDeathNearCotswoldReleasesAtItsHomeBind()
    {
        BotReleaseBindPoints.Replace(1, Region1Binds);
        Point3D release = BotReleaseBindPoints.PveRelease(1, 566000, 503000, eRealm.Albion, 0,
            homeRegion: 1, homeX: 560500, homeY: 511700, homeZ: 2010, out bool homeBind);
        Assert.Multiple(() =>
        {
            Assert.That(homeBind, Is.True);
            Assert.That((release.X, release.Y, release.Z), Is.EqualTo((560500, 511700, 2010)));
        });
    }

    [Test]
    public void RejectedHomeBindFallsBackToNearestOwnBind()
    {
        BotReleaseBindPoints.Replace(1, Region1Binds);
        Point3D release = BotReleaseBindPoints.PveRelease(1, 583000, 488000, eRealm.Albion, 0,
            homeRegion: 1, homeX: 560500, homeY: 511700, homeZ: 2010, out bool homeBind, acceptHome: _ => null);
        Assert.Multiple(() =>
        {
            Assert.That(homeBind, Is.False);
            Assert.That((release.X, release.Y), Is.EqualTo((584638, 486320)));
            Assert.That(BotReleaseBindPoints.PveRelease(1, 566000, 503000, eRealm.Hibernia, 0, 0, 0, 0, 0, out _),
                Is.Null, "never an enemy realm's stone");
        });
    }

    [Test]
    public void OwnRealmPveBotMayOpenItsRealmsBorderGate()
    {
        // Gate 100087402 (Svasud Faste, Vale of Mularn side): door realm 6, zone 100.
        eRealm valeSide = AutonomousWorldBotController.ProtectedRealm(100, 100);
        eRealm upplandSide = AutonomousWorldBotController.ProtectedRealm(100, 111);
        Assert.Multiple(() =>
        {
            Assert.That(valeSide, Is.EqualTo(eRealm.Midgard));
            Assert.That(AutonomousRvrTravel.MayOpenBorderDoor(true, false, eRealm.Midgard, eRealm.Door, valeSide), Is.True,
                "own-realm PvE bot");
            Assert.That(AutonomousRvrTravel.MayOpenBorderDoor(true, false, eRealm.Albion, eRealm.Door, valeSide), Is.False,
                "enemy-realm PvE bot");
            Assert.That(AutonomousRvrTravel.MayOpenBorderDoor(true, false, eRealm.Midgard, eRealm.Door, upplandSide), Is.False,
                "frontier-side gate stays an RvR matter");
            Assert.That(AutonomousRvrTravel.MayOpenBorderDoor(true, true, eRealm.Midgard, eRealm.Door, upplandSide), Is.True,
                "RvR bots keep their old right");
            Assert.That(AutonomousRvrTravel.MayOpenBorderDoor(true, true, eRealm.Hibernia, eRealm.Midgard, valeSide), Is.False,
                "door realm check stays");
            Assert.That(AutonomousRvrTravel.MayOpenBorderDoor(false, false, eRealm.Midgard, eRealm.Door, valeSide), Is.False,
                "not an autonomous world bot");
        });
    }
}
