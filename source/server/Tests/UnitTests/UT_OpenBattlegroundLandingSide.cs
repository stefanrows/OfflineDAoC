using System;
using System.Collections.Generic;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Server PvP spreads the open BG units over the three portal keeps (owner report 2026-10-17).</summary>
[TestFixture]
public sealed class UT_OpenBattlegroundLandingSide
{
    [Test]
    public void TheKeepWithTheFewestUnitsWinsWhateverTheUnitsRealm()
    {
        var load = new Dictionary<eRealm, (int Units, int Seats)>
        {
            [eRealm.Albion] = (2, 10), [eRealm.Midgard] = (1, 4), [eRealm.Hibernia] = (2, 6),
        };
        Assert.That(OpenBattlegroundRoster.BalancedSide(eRealm.Albion, load, new Random(1)), Is.EqualTo(eRealm.Midgard));
    }

    [Test]
    public void EqualUnitsGoToTheFewestSeatsThenToTheOwnRealm()
    {
        var seats = new Dictionary<eRealm, (int Units, int Seats)>
        {
            [eRealm.Albion] = (1, 8), [eRealm.Midgard] = (1, 3), [eRealm.Hibernia] = (1, 4),
        };
        Assert.That(OpenBattlegroundRoster.BalancedSide(eRealm.Hibernia, seats, new Random(1)), Is.EqualTo(eRealm.Midgard));
        Assert.That(OpenBattlegroundRoster.BalancedSide(eRealm.Hibernia, null, new Random(1)), Is.EqualTo(eRealm.Hibernia), "empty field");
    }

    [Test]
    public void SuccessiveUnitsOfOneRealmFillAllThreeKeeps()
    {
        var load = new Dictionary<eRealm, (int Units, int Seats)>();
        var rng = new Random(7);
        for (int i = 0; i < 9; i++)
        {
            eRealm side = OpenBattlegroundRoster.BalancedSide(eRealm.Albion, load, rng);
            load.TryGetValue(side, out var value);
            load[side] = (value.Units + 1, value.Seats + 4);
        }
        Assert.That(load.Values, Has.All.Matches<(int Units, int Seats)>(value => value.Units == 3));
    }

    [Test]
    public void OnlyARealModeChangeWipesTheCrew()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundRoster.ModeSwitched(null, OpenBgMode.PerRealm), Is.False, "first tick after a start");
            Assert.That(OpenBattlegroundRoster.ModeSwitched(OpenBgMode.ServerPvp, OpenBgMode.ServerPvp), Is.False);
            Assert.That(OpenBattlegroundRoster.ModeSwitched(OpenBgMode.ServerPvp, OpenBgMode.PerRealm), Is.True);
            Assert.That(OpenBattlegroundRoster.ModeSwitched(OpenBgMode.PerRealm, OpenBgMode.ServerPvp), Is.True);
        });
    }
}
