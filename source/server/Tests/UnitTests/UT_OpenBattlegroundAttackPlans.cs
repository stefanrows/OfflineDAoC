using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Per realm the attacks vary: realms take turns, two realms raid at once, or a third joins the keep fight (owner request 2026-10-18).</summary>
[TestFixture]
public sealed class UT_OpenBattlegroundAttackPlans
{
    private static readonly OpenBgRaidCandidate[] Inside =
    {
        new(1, 8, true, "A", eRealm.Albion, false),
        new(2, 8, true, "H", eRealm.Hibernia, false),
        new(3, 4, false, "H2", eRealm.Hibernia, false),
        new(4, 8, true, "M", eRealm.Midgard, true),
        new(5, 8, true, "M2", eRealm.Midgard, true),
    };

    [Test]
    public void TheRealmThatRaidedLongestAgoGoesNext()
    {
        var last = new Dictionary<eRealm, long> { [eRealm.Hibernia] = 6, [eRealm.Albion] = 5 };
        Assert.Multiple(() =>
        {
            Assert.That(OpenBattlegroundDirector.PickRaidRealm(Inside, last, eRealm.None, new Random(1)), Is.EqualTo(eRealm.Albion));
            // Hibernia has more bots inside, but a realm that never raided goes first.
            Assert.That(OpenBattlegroundDirector.PickRaidRealm(Inside, new Dictionary<eRealm, long> { [eRealm.Albion] = 6 }, eRealm.None, new Random(1)),
                Is.EqualTo(eRealm.Hibernia));
            Assert.That(OpenBattlegroundDirector.PickRaidRealm(Inside, last, eRealm.Albion, new Random(1)), Is.EqualTo(eRealm.Hibernia));
            // The keep owner (Midgard, keep friends) never raids its own keep.
            Assert.That(OpenBattlegroundDirector.PickRaidRealm(Inside, last, eRealm.Hibernia, new Random(1)), Is.EqualTo(eRealm.Albion));
            Assert.That(OpenBattlegroundDirector.PickRaidRealm(Inside.Where(unit => unit.Realm == eRealm.Midgard).ToArray(), last, eRealm.None, new Random(1)),
                Is.EqualTo(eRealm.None));
        });
    }

    [Test]
    public void ADoubleRaidSendsTheSecondRealmFromThePush()
    {
        OpenBgRaid second = OpenBattlegroundDirector.SecondRaidForTick(new Random(1), OpenBgPhase.Push, OpenBgAttackPlan.Double, null, eRealm.Albion,
            Inside, new Dictionary<string, int>(), 1.0 / 3, out string reason);
        Assert.Multiple(() =>
        {
            Assert.That(second.Units, Is.EquivalentTo(new[] { 1 }), "Albion only");
            Assert.That(second.LeadRealm, Is.EqualTo(eRealm.Albion));
            Assert.That(reason, Is.EqualTo("join"));
        });
    }

    [Test]
    public void AnOpportunistWaitsForTheKeepFight()
    {
        OpenBgRaid push = OpenBattlegroundDirector.SecondRaidForTick(new Random(1), OpenBgPhase.Push, OpenBgAttackPlan.Opportunist, null, eRealm.Hibernia,
            Inside, new Dictionary<string, int>(), 0.1, out string pushReason);
        OpenBgRaid fight = OpenBattlegroundDirector.SecondRaidForTick(new Random(1), OpenBgPhase.KeepFight, OpenBgAttackPlan.Opportunist, push, eRealm.Hibernia,
            Inside, new Dictionary<string, int>(), 0.1, out string fightReason);
        OpenBgRaid quiet = OpenBattlegroundDirector.SecondRaidForTick(new Random(1), OpenBgPhase.Quiet, OpenBgAttackPlan.Opportunist, fight, eRealm.Hibernia,
            Inside, new Dictionary<string, int>(), 0.1, out string quietReason);
        Assert.Multiple(() =>
        {
            Assert.That(push.IsEmpty, Is.True);
            Assert.That(pushReason, Is.Null);
            Assert.That(fight.IsEmpty, Is.False);
            Assert.That(fight.Units.All(unit => unit is 2 or 3), Is.True, "Hibernia only");
            Assert.That(fightReason, Is.EqualTo("join"));
            Assert.That(quiet.IsEmpty, Is.True);
            Assert.That(quietReason, Is.EqualTo("quiet"));
        });
    }

    [Test]
    public void ASinglePlanHasNoSecondRaidAndASecondRaidThatTookTheKeepEnds()
    {
        OpenBgRaid single = OpenBattlegroundDirector.SecondRaidForTick(new Random(1), OpenBgPhase.Push, OpenBgAttackPlan.Single, null, eRealm.Albion,
            Inside, new Dictionary<string, int>(), 1.0 / 3, out _);
        var held = new OpenBgRaid(new[] { 4 }, 4, 8, "M", eRealm.Midgard);
        OpenBgRaid after = OpenBattlegroundDirector.SecondRaidForTick(new Random(1), OpenBgPhase.KeepFight, OpenBgAttackPlan.Double, held, eRealm.Midgard,
            Inside, new Dictionary<string, int>(), 1.0 / 3, out string reason);
        Assert.Multiple(() =>
        {
            Assert.That(single.IsEmpty, Is.True);
            Assert.That(after.IsEmpty, Is.True);
            Assert.That(reason, Is.EqualTo("keep_changed"));
        });
    }

    [Test]
    public void EveryPlanComesUpAndTheCycleCarriesOne()
    {
        var rng = new Random(7);
        var plans = Enumerable.Range(0, 300).Select(_ => OpenBattlegroundDirector.DrawPlan(rng)).ToArray();
        OpenBgCycle cycle = OpenBattlegroundDirector.NewCycle(new Random(3), 15, 45);
        Assert.Multiple(() =>
        {
            foreach (OpenBgAttackPlan plan in Enum.GetValues<OpenBgAttackPlan>())
                Assert.That(plans.Count(drawn => drawn == plan), Is.GreaterThan(50), plan.ToString());
            Assert.That(cycle.SecondRaid.IsEmpty, Is.True);
            Assert.That(OpenBattlegroundDirector.SecondRaidJoins(OpenBgAttackPlan.Double, OpenBgPhase.KeepFight), Is.False);
            Assert.That(OpenBattlegroundDirector.SecondRaidJoins(OpenBgAttackPlan.Opportunist, OpenBgPhase.KeepFight), Is.True);
        });
    }
}
