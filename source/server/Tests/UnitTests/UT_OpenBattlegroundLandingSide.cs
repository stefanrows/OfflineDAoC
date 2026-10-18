using System;
using System.Collections.Generic;
using System.Linq;
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

    [Test]
    public void RaidsDifferFromCycleToCycle()
    {
        var rng = new Random(3);
        var shares = Enumerable.Range(0, 300).Select(_ => OpenBattlegroundDirector.DrawRaidShare(rng)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(shares, Has.Some.EqualTo(OpenBattlegroundDirector.ProbeRaidShare), "probes");
            Assert.That(shares, Has.Some.EqualTo(OpenBattlegroundDirector.MusterRaidShare), "big musters");
            Assert.That(OpenBattlegroundDirector.RaidTargetBots(60, 60, OpenBattlegroundDirector.ProbeRaidShare), Is.EqualTo(6));
            Assert.That(OpenBattlegroundDirector.RaidTargetBots(20, 20, OpenBattlegroundDirector.ProbeRaidShare),
                Is.EqualTo(OpenBattlegroundDirector.ProbeMinimumBots), "a probe is one or two groups");
            Assert.That(OpenBattlegroundDirector.RaidTargetBots(60, 20, OpenBattlegroundDirector.MusterRaidShare), Is.EqualTo(20),
                "a muster takes the whole realm at most");
            Assert.That(OpenBattlegroundDirector.RaidTargetBots(54, 54, 1.0 / 3), Is.EqualTo(OpenBattlegroundDirector.RaidTargetBots(54, 54)));
        });
    }

    [Test]
    public void APartOfTheOwnersUnitsAnswersTheRaid()
    {
        int answering = Enumerable.Range(1, 1000).Count(unit => OpenBattlegroundDirector.AnswersRaid(unit, 7, 0.5));
        Assert.Multiple(() =>
        {
            Assert.That(answering, Is.InRange(400, 600));
            Assert.That(OpenBattlegroundDirector.AnswersRaid(5, 7, 0.5), Is.EqualTo(OpenBattlegroundDirector.AnswersRaid(5, 7, 0.5)), "stable");
            Assert.That(Enumerable.Range(1, 100).All(unit => OpenBattlegroundDirector.AnswersRaid(unit, 7, 1.0)), Is.True);
        });
    }
}
