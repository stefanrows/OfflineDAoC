using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using DOL.GS.Tests;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// RvR package R4b (docs/RVR_ARMIES.md): the autonomous event layer fights per realm in the RvR world. Armies
/// are realm armies (never a member of another realm), a realm never besieges its own keeps, neutral and enemy
/// keeps are weighted, sieges are slotted per realm by its attack plan, the whole realm defends, the third realm
/// joins as its plan says, the army musters at the realm's portal outpost or border keep, the reserve hunts
/// invaders at home and revenge trips are capped. Every event-layer rule is checked in both worlds; Camlann
/// answers as before.
/// Port session S1 (docs/RVR_PORT_MAP.md): the RvR world now runs the upstream layer (RealmRvrEventLayer) and these
/// R4b rules are gated off there. The fixture calls the fork layer (CamlannRvrEventLayer) directly, so the kept R4b
/// code stays covered under both world settings.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_RvrRealmArmies
{
    private const long Forever = long.MaxValue / 4;

    [SetUp]
    public void Reset()
    {
        RvrEventTestState.Clear();
        foreach (eRealm realm in RealmTravel.PlayableRealms)
            AutonomousRealmArmyPlans.SetForTests(realm, OpenBgAttackPlan.Single, Forever);
    }

    [TearDown]
    public void Clear() => RvrEventTestState.Clear();

    private static AutonomousRvrEventLayer.Force Warband(string id, eRealm realm, string guild, int members = 8,
        bool singleGuild = true, bool reserve = false) =>
        new(id, realm, members, 50, 2, RoamingReserve: reserve, GuildName: guild, SingleGuild: singleGuild);

    private static AutonomousRvrEventLayer.LiveObjective Keep(string id, eRealm owner, ushort region = 100,
        string guild = null) =>
        new(id, id, AutonomousRvrEventLayer.Intent.AssaultKeep, owner, region, 100_000, 100_000, 0, false, 0, 0, 2, 2,
            OwningGuild: guild);

    private static AutonomousRvrEventLayer.BattleNotice Battle(string id) =>
        CamlannRvrEventLayer.Snapshot().Single(notice => notice.TargetId == id);

    // --- Army formation ----------------------------------------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void TheArmyKeyIsTheGuildInCamlannAndTheRealmInTheRvrWorld(WorldModelKind world)
    {
        Assert.That(AutonomousRealmArmyPolicy.ArmyKey(world, "guild-1", eRealm.Albion),
            Is.EqualTo(BothWorlds.Expect(world, "guild-1", "realm:1")));
        Assert.That(AutonomousRealmArmyPolicy.ArmyKey(world, null, eRealm.Midgard),
            Is.EqualTo(BothWorlds.Expect<string>(world, null, "realm:2")), "a guildless bot marches in its realm's army");
        Assert.That(AutonomousRealmArmyPolicy.ArmyKey(world, "guild-1", eRealm.None),
            Is.EqualTo(BothWorlds.Expect<string>(world, "guild-1", null)));
    }

    [Test]
    public void ARealmArmyNeverTakesAMemberOfAnotherRealm()
    {
        var guild = new object();
        foreach (eRealm army in RealmTravel.PlayableRealms)
        foreach (eRealm member in RealmTravel.PlayableRealms)
        {
            Assert.That(AutonomousRealmArmyPolicy.SameArmy<object>(WorldModelKind.Realm, null, army, guild, member), Is.EqualTo(army == member),
                $"{member} bot in the {army} army");
            Assert.That(AutonomousRealmArmyPolicy.SameArmy<object>(WorldModelKind.Realm, null, army, null, member), Is.EqualTo(army == member),
                "guildless");
        }
        Assert.That(AutonomousRealmArmyPolicy.SameArmy<object>(WorldModelKind.Realm, null, eRealm.None, guild, eRealm.None), Is.False);
        // Camlann: the guild decides, the realm does not.
        Assert.That(AutonomousRealmArmyPolicy.SameArmy(WorldModelKind.Camlann, guild, eRealm.Albion, guild, eRealm.Hibernia), Is.True);
        Assert.That(AutonomousRealmArmyPolicy.SameArmy(WorldModelKind.Camlann, guild, eRealm.Albion, new object(), eRealm.Albion), Is.False);
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void WarbandsOfAnyGuildOfTheRealmFormOneArmyAndOtherRealmsNeverJoinIt(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        var keep = Keep("keep-n", eRealm.None);

        var opened = CamlannRvrEventLayer.ChooseOrJoin(Warband("alb-1", eRealm.Albion, "Lions of Albion"), [keep], 1, 0);
        var mate = CamlannRvrEventLayer.ChooseOrJoin(Warband("alb-2", eRealm.Albion, "Ye Olde Alehouse"), [keep], 2, 0);
        var mid = CamlannRvrEventLayer.ChooseOrJoin(Warband("mid-1", eRealm.Midgard, "Lions of Albion"), [keep], 3, 0);

        Assert.Multiple(() =>
        {
            Assert.That(opened?.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.AssaultKeep));
            Assert.That(mate?.TargetId, Is.EqualTo("keep-n"));
            // Camlann: the Midgard guildmate joins its guild's army, the other Albion guild contests on its own.
            // RvR: the other Albion guild is part of the realm army, the Midgard warband never is.
            Assert.That(Battle("keep-n").Attackers, Is.EqualTo(16));
            Assert.That(Battle("keep-n").ThirdRealm, Is.EqualTo(BothWorlds.Expect(world, 8, 0)));
            Assert.That(CamlannRvrEventLayer.KeepPlan("mid-1", eRealm.Midgard, 3)?.Intent,
                world == WorldModelKind.Realm ? Is.Null : Is.EqualTo(AutonomousRvrEventLayer.Intent.AssaultKeep));
            Assert.That(mid?.IsSharedEvent == true, Is.EqualTo(world == WorldModelKind.Camlann));
        });
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void AMixedGuildWarbandOfOneRealmOpensASiegeOnlyInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        var mixed = Warband("mixed", eRealm.Hibernia, "Clan Cuchulain", singleGuild: false);

        Assert.That(CamlannRvrEventLayer.IsWholeGuildWarband(world, mixed), Is.EqualTo(world == WorldModelKind.Realm));
        var plan = CamlannRvrEventLayer.ChooseOrJoin(mixed, [Keep("keep-n", eRealm.None, 1)], 1, 0);
        Assert.That(plan?.Intent == AutonomousRvrEventLayer.Intent.AssaultKeep, Is.EqualTo(world == WorldModelKind.Realm));
    }

    // --- Target choice -------------------------------------------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void AnArmyNeverBesiegesAKeepOfItsOwnRealmEvenWhenAnotherGuildClaimedIt(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        var claimed = Keep("keep-own", eRealm.Albion, 1, guild: "Wardens of Benowyc");

        var plan = CamlannRvrEventLayer.ChooseOrJoin(Warband("alb", eRealm.Albion, "Lions of Albion"), [claimed], 1, 0);

        Assert.That(plan?.Intent == AutonomousRvrEventLayer.Intent.AssaultKeep, Is.EqualTo(world == WorldModelKind.Camlann),
            "Camlann: another guild's keep is fair game; RvR: a keep of the own realm never is");
    }

    [Test]
    public void KeepWeightsFollowTheTunablesAndTheHomeFrontier()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRealmArmyPolicy.KeepWeight(eRealm.Albion, eRealm.Albion, 1, 1, 1.5, 2), Is.Zero, "own keep");
            Assert.That(AutonomousRealmArmyPolicy.KeepWeight(eRealm.Albion, eRealm.None, 100, 1, 1.5, 2), Is.EqualTo(1));
            Assert.That(AutonomousRealmArmyPolicy.KeepWeight(eRealm.Albion, eRealm.Midgard, 100, 1, 1.5, 2), Is.EqualTo(1.5));
            Assert.That(AutonomousRealmArmyPolicy.KeepWeight(eRealm.Albion, eRealm.None, 1, 1, 1.5, 2), Is.EqualTo(2), "neutral at home");
            Assert.That(AutonomousRealmArmyPolicy.KeepWeight(eRealm.Albion, eRealm.Hibernia, 1, 1, 1.5, 2), Is.EqualTo(3),
                "an enemy keep in the own frontier is the first retake");
            Assert.That(AutonomousRealmArmyPolicy.KeepWeight(eRealm.None, eRealm.Midgard, 100, 1, 1.5, 2), Is.Zero);
            Assert.That(AutonomousRealmArmyProperties.RVR_ARMY_NEUTRAL_KEEP_WEIGHT, Is.EqualTo(1.0));
            Assert.That(AutonomousRealmArmyProperties.RVR_ARMY_ENEMY_KEEP_WEIGHT, Is.EqualTo(1.5));
            Assert.That(AutonomousRealmArmyProperties.RVR_ARMY_HOME_FRONTIER_WEIGHT, Is.EqualTo(2.0));
        });
    }

    [Test]
    public void TheWeightedDrawPicksNeutralAndEnemyKeepsInProportionAndNeverTheOwnKeep()
    {
        var own = Keep("own", eRealm.Albion, 100);
        var neutral = Keep("neutral", eRealm.None, 100);
        var enemy = Keep("enemy", eRealm.Midgard, 100);
        var counts = new System.Collections.Generic.Dictionary<string, int>();
        const int draws = 10_000;
        for (int i = 0; i < draws; i++)
        {
            var pick = AutonomousRealmArmyPolicy.ChooseWeighted(new[] { own, neutral, enemy },
                keep => AutonomousRealmArmyPolicy.KeepWeight(eRealm.Albion, keep.OwningRealm, keep.RegionId, 1, 1.5, 2),
                _ => false, (i + 0.5) / draws);
            counts[pick.Id] = counts.GetValueOrDefault(pick.Id) + 1;
        }

        Assert.That(counts.ContainsKey("own"), Is.False);
        Assert.That(counts["neutral"] / (double)draws, Is.EqualTo(0.4).Within(0.01));
        Assert.That(counts["enemy"] / (double)draws, Is.EqualTo(0.6).Within(0.01));
    }

    [Test]
    public void TheWeightedDrawPrefersKeepsNotChosenLately()
    {
        var a = Keep("a", eRealm.None);
        var b = Keep("b", eRealm.Midgard);
        for (int i = 0; i < 100; i++)
            Assert.That(AutonomousRealmArmyPolicy.ChooseWeighted(new[] { a, b },
                _ => 1, keep => keep.Id == "b", i / 100.0).Id, Is.EqualTo("a"));
        Assert.That(AutonomousRealmArmyPolicy.ChooseWeighted(new[] { a }, _ => 1, _ => true, 0.5).Id, Is.EqualTo("a"),
            "all chosen lately: the draw starts over");
        Assert.That(AutonomousRealmArmyPolicy.ChooseWeighted(new[] { a }, _ => 0, null, 0.5), Is.Null);
    }

    [Test]
    public void TheRealmNeverOpensASiegeOnItsOwnKeepThroughTheEventLayer()
    {
        using var _ = BothWorlds.Use(WorldModelKind.Realm);
        var keeps = new[] { Keep("own", eRealm.Albion, 1), Keep("neutral", eRealm.None, 100), Keep("enemy", eRealm.Midgard, 100) };
        for (int i = 0; i < 40; i++)
        {
            RvrEventTestState.Clear();
            var plan = CamlannRvrEventLayer.ChooseOrJoin(Warband($"alb-{i}", eRealm.Albion, "Lions of Albion"), keeps, 1, 0);
            Assert.That(plan?.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.AssaultKeep));
            Assert.That(plan.TargetId, Is.Not.EqualTo("own"));
        }
    }

    [Test]
    public void ARvrRelicSiegeTakesTheUpstreamCapOfTwoHundredForty()
    {
        var relic = new AutonomousRvrEventLayer.LiveObjective("relic", "Castle Excalibur", AutonomousRvrEventLayer.Intent.AssaultRelicKeep,
            eRealm.Albion, 1, 100_000, 100_000, 0, true, 0, 0, 2, 2);
        foreach (WorldModelKind world in BothWorlds.All)
        {
            using var _ = BothWorlds.Use(world);
            RvrEventTestState.Clear();
            var plan = CamlannRvrEventLayer.ChooseOrJoin(Warband("mid", eRealm.Midgard, "Valhalla Bound"), [relic], 1, 0);
            Assert.That(plan?.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.AssaultRelicKeep), world.ToString());
            Assert.That(Battle("relic").CapPerRealm, Is.EqualTo(BothWorlds.Expect(world, 192, 240)));
        }
    }

    // --- Siege slots and attack plans ---------------------------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void TheSiegeSideIsTheRealmInTheRvrWorld(WorldModelKind world)
    {
        Assert.That(CamlannRvrEventLayer.SiegeSideKey(world, "Lions of Albion", eRealm.Albion),
            Is.EqualTo(BothWorlds.Expect(world, "guild:Lions of Albion", "realm:" + eRealm.Albion)));
        Assert.That(CamlannRvrEventLayer.SiegeSideKey(world, null, eRealm.Albion), Is.EqualTo("realm:" + eRealm.Albion));
    }

    [Test]
    public void PlansSetHowManySiegesARealmRuns()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRealmArmyPolicy.MaximumOwnSieges(OpenBgAttackPlan.Single), Is.EqualTo(1));
            Assert.That(AutonomousRealmArmyPolicy.MaximumOwnSieges(OpenBgAttackPlan.Double), Is.EqualTo(2));
            Assert.That(AutonomousRealmArmyPolicy.MaximumOwnSieges(OpenBgAttackPlan.Opportunist), Is.EqualTo(1));
            Assert.That(AutonomousRealmArmyPolicy.MayOpenOwnSiege(OpenBgAttackPlan.Single, 0, 3), Is.True);
            Assert.That(AutonomousRealmArmyPolicy.MayOpenOwnSiege(OpenBgAttackPlan.Single, 1, 0), Is.False);
            Assert.That(AutonomousRealmArmyPolicy.MayOpenOwnSiege(OpenBgAttackPlan.Double, 1, 0), Is.True);
            Assert.That(AutonomousRealmArmyPolicy.MayOpenOwnSiege(OpenBgAttackPlan.Opportunist, 0, 0), Is.True);
            Assert.That(AutonomousRealmArmyPolicy.MayOpenOwnSiege(OpenBgAttackPlan.Opportunist, 0, 1), Is.False,
                "an opportunist waits for the other realm's fight");
        });
    }

    [TestCase(OpenBgAttackPlan.Single, false)]
    [TestCase(OpenBgAttackPlan.Double, true)]
    public void ASecondGuildOfTheRealmOpensASecondSiegeOnlyUnderADoublePlan(OpenBgAttackPlan plan, bool secondOpens)
    {
        using var _ = BothWorlds.Use(WorldModelKind.Realm);
        AutonomousRealmArmyPlans.SetForTests(eRealm.Albion, plan, Forever);
        CamlannRvrEventLayer.ChooseOrJoin(Warband("alb-1", eRealm.Albion, "Lions of Albion"), [Keep("keep-1", eRealm.None)], 1, 0);

        var second = CamlannRvrEventLayer.ChooseOrJoin(Warband("alb-2", eRealm.Albion, "Ye Olde Alehouse"),
            [Keep("keep-2", eRealm.Midgard)], 2, 0);

        Assert.That(second?.Intent == AutonomousRvrEventLayer.Intent.AssaultKeep && second.TargetId == "keep-2", Is.EqualTo(secondOpens));
    }

    [Test]
    public void CamlannStillSlotsSiegesPerGuild()
    {
        using var _ = BothWorlds.Use(WorldModelKind.Camlann);
        CamlannRvrEventLayer.ChooseOrJoin(Warband("alb-1", eRealm.Albion, "Raiders"), [Keep("keep-1", eRealm.None)], 1, 0);
        var other = CamlannRvrEventLayer.ChooseOrJoin(Warband("alb-2", eRealm.Albion, "Others"), [Keep("keep-2", eRealm.Midgard)], 2, 0);
        Assert.That(other?.TargetId, Is.EqualTo("keep-2"), "another guild has its own two siege slots");
    }

    [Test]
    public void AnOpportunistRealmDoesNotOpenWhileAnotherRealmBesieges()
    {
        using var _ = BothWorlds.Use(WorldModelKind.Realm);
        AutonomousRealmArmyPlans.SetForTests(eRealm.Hibernia, OpenBgAttackPlan.Opportunist, Forever);
        CamlannRvrEventLayer.ChooseOrJoin(Warband("mid", eRealm.Midgard, "Valhalla Bound"), [Keep("keep-1", eRealm.None)], 1, 0);

        var hib = CamlannRvrEventLayer.ChooseOrJoin(Warband("hib", eRealm.Hibernia, "Clan Cuchulain"),
            [Keep("keep-2", eRealm.Albion, 1)], 2, 0);

        Assert.That(hib?.Intent, Is.Not.EqualTo(AutonomousRvrEventLayer.Intent.AssaultKeep));
    }

    [Test]
    public void PlansAreDrawnPerRealmWithTheOpenBgOddsAndKeptUntilTheyRunOut()
    {
        AutonomousRealmArmyPlans.Clear();
        var previous = AutonomousRealmArmyPlans.RandomSource;
        try
        {
            var rng = new Random(7);
            AutonomousRealmArmyPlans.RandomSource = () => rng;
            OpenBgAttackPlan first = AutonomousRealmArmyPlans.PlanFor(eRealm.Albion, 0);
            for (long tick = 0; tick < 29 * 60_000L; tick += 60_000)
                Assert.That(AutonomousRealmArmyPlans.PlanFor(eRealm.Albion, tick), Is.EqualTo(first), "at least half an hour");
            var seen = new System.Collections.Generic.HashSet<OpenBgAttackPlan>();
            for (long hour = 1; hour < 60; hour++)
                seen.Add(AutonomousRealmArmyPlans.PlanFor(eRealm.Midgard, hour * 2 * 60 * 60_000L));
            Assert.That(seen, Is.EquivalentTo(Enum.GetValues<OpenBgAttackPlan>()));
        }
        finally { AutonomousRealmArmyPlans.RandomSource = previous; }
    }

    // --- Defence and third realm ---------------------------------------------------------------------------

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void TheWholeRealmDefendsItsKeepWhicheverGuildClaimedIt(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        var keep = Keep("keep-mid", eRealm.Midgard, 100, guild: "Valhalla Bound");
        CamlannRvrEventLayer.ChooseOrJoin(Warband("alb", eRealm.Albion, "Lions of Albion"), [keep], 1, 0);

        // A Midgard warband never sees its own realm's keep as an objective; the siege event finds it anyway.
        var defender = CamlannRvrEventLayer.ChooseOrJoin(Warband("mid", eRealm.Midgard, "the dwarf bar"), [], 2, 0);

        Assert.That(defender?.Intent == AutonomousRvrEventLayer.Intent.DefendEvent && defender.TargetId == "keep-mid",
            Is.EqualTo(world == WorldModelKind.Realm), "Camlann: only the owning guild defends, through its recall");
    }

    [Test]
    public void TheThirdRealmContestsOnlyOnceTheKeepFightIsOnAndAsItsPlanSays()
    {
        using var _ = BothWorlds.Use(WorldModelKind.Realm);
        var keep = Keep("keep-mid", eRealm.Midgard, 100);
        CamlannRvrEventLayer.ChooseOrJoin(Warband("alb", eRealm.Albion, "Lions of Albion"), [keep], 1, 0);

        var early = CamlannRvrEventLayer.ChooseOrJoin(Warband("hib-8", eRealm.Hibernia, "Clan Cuchulain"), [keep], 2, 0);
        Assert.That(early?.TargetId == "keep-mid" && early.IsSharedEvent, Is.False, "no intervention before the fight");

        CamlannRvrEventLayer.ReportBattleActivity("keep-mid", 3, "alb");
        var small = CamlannRvrEventLayer.ChooseOrJoin(Warband("hib-4", eRealm.Hibernia, "Clan Cuchulain", members: 4), [keep], 4, 0);
        var whole = CamlannRvrEventLayer.ChooseOrJoin(Warband("hib-8b", eRealm.Hibernia, "Dun Bolg Defenders"), [keep], 5, 0);
        Assert.That(small?.IsSharedEvent == true && small.TargetId == "keep-mid", Is.False, "a single plan sends whole warbands only");
        Assert.That(whole?.TargetId, Is.EqualTo("keep-mid"));
        Assert.That(Battle("keep-mid").ThirdRealm, Is.EqualTo(8));

        AutonomousRealmArmyPlans.SetForTests(eRealm.Hibernia, OpenBgAttackPlan.Opportunist, Forever);
        var opportunist = CamlannRvrEventLayer.ChooseOrJoin(Warband("hib-4b", eRealm.Hibernia, "Lurikeen Social Club", members: 4),
            [keep], 6, 0.99);
        Assert.That(opportunist?.TargetId, Is.EqualTo("keep-mid"), "an opportunist joins a running keep fight without a roll");
    }

    [Test]
    public void TheThirdRealmComesWithHalfTheCapAndContestRulesFollowThePlan()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRealmArmyPolicy.ThirdRealmCapacity(128), Is.EqualTo(64));
            Assert.That(AutonomousRealmArmyPolicy.ThirdRealmCapacity(240), Is.EqualTo(120));
            Assert.That(AutonomousRealmArmyPolicy.MayContestAsThirdRealm(OpenBgAttackPlan.Single, 8, false), Is.False);
            Assert.That(AutonomousRealmArmyPolicy.MayContestAsThirdRealm(OpenBgAttackPlan.Single, 8, true), Is.True);
            Assert.That(AutonomousRealmArmyPolicy.MayContestAsThirdRealm(OpenBgAttackPlan.Double, 7, true), Is.False);
            Assert.That(AutonomousRealmArmyPolicy.MayContestAsThirdRealm(OpenBgAttackPlan.Opportunist, 2, true), Is.True);
            Assert.That(AutonomousRealmArmyPolicy.MayContestAsThirdRealm(OpenBgAttackPlan.Opportunist, 1, true), Is.False);
        });
    }

    [Test]
    public void ANeutralKeepSiegeLetsBothOtherRealmsContest()
    {
        using var _ = BothWorlds.Use(WorldModelKind.Realm);
        var keep = Keep("keep-n", eRealm.None, 1);
        CamlannRvrEventLayer.ChooseOrJoin(Warband("alb", eRealm.Albion, "Lions of Albion"), [keep], 1, 0);
        CamlannRvrEventLayer.ReportBattleActivity("keep-n", 2, "alb");

        var mid = CamlannRvrEventLayer.ChooseOrJoin(Warband("mid", eRealm.Midgard, "Valhalla Bound"), [keep], 3, 0);
        var hib = CamlannRvrEventLayer.ChooseOrJoin(Warband("hib", eRealm.Hibernia, "Clan Cuchulain"), [keep], 4, 0);

        Assert.That(mid?.TargetId, Is.EqualTo("keep-n"));
        Assert.That(hib?.TargetId, Is.EqualTo("keep-n"));
        Assert.That(Battle("keep-n").Defenders, Is.Zero, "the Wardens hold a neutral keep; no realm defends it");
    }

    // --- Muster --------------------------------------------------------------------------------------------

    [Test]
    public void TheArmyMustersAtTheRealmsPortalOutpostInAnEnemyFrontier()
    {
        Assert.That(AutonomousRealmArmyPolicy.TryGetRealmMuster(eRealm.Albion, 100, new(600_000, 600_000), out var odin), Is.True);
        Assert.That((odin.RegionId, odin.Position.X, odin.Position.Y), Is.EqualTo(((ushort)100, 596364f, 631509f)), "Odin Alb landing");
        Assert.That(AutonomousRealmArmyPolicy.TryGetRealmMuster(eRealm.Midgard, 1, new(600_000, 300_000), out var hadrian), Is.True);
        Assert.That((hadrian.RegionId, hadrian.Position.X), Is.EqualTo(((ushort)1, 655200f)), "Hadrian Mid landing");
        Assert.That(AutonomousRealmArmyPolicy.TryGetRealmMuster(eRealm.Hibernia, 1, new(600_000, 300_000), out var hadrianHib), Is.True);
        Assert.That(hadrianHib.Position.X, Is.EqualTo(605743f), "each invading realm has its own outpost");
        Assert.That(odin.Name, Does.Contain("Albion"));
    }

    [Test]
    public void TheArmyMustersAtTheNearerOwnBorderKeepInItsHomeFrontier()
    {
        Assert.That(AutonomousRealmArmyPolicy.TryGetRealmMuster(eRealm.Albion, 1, new(530_000, 360_000), out var near), Is.True);
        Assert.That(near.Name, Is.EqualTo("Snowdonia Fortress"));
        Assert.That(AutonomousRealmArmyPolicy.TryGetRealmMuster(eRealm.Albion, 1, new(590_000, 470_000), out var sauvage), Is.True);
        Assert.That(sauvage.Name, Is.EqualTo("Castle Sauvage"));
        Assert.That(AutonomousRealmArmyPolicy.TryGetRealmMuster(eRealm.Hibernia, 200, new(420_000, 480_000), out var cain), Is.True);
        Assert.That(cain.Name, Is.EqualTo("Druim Cain"));
        Assert.That(AutonomousRealmArmyPolicy.TryGetRealmMuster(eRealm.Midgard, 100, new(760_000, 660_000), out var svasud), Is.True);
        Assert.That(svasud.Name, Is.EqualTo("Svasud Faste"));
    }

    [Test]
    public void NoMusterOutsideTheFrontiersOrTooCloseToTheKeep()
    {
        Assert.That(AutonomousRealmArmyPolicy.TryGetRealmMuster(eRealm.Albion, 51, new(0, 0), out _), Is.False);
        Assert.That(AutonomousRealmArmyPolicy.TryGetRealmMuster(eRealm.None, 1, new(0, 0), out _), Is.False);
        AutonomousRealmArmyPolicy.TryGetRealmMuster(eRealm.Albion, 100, new(0, 0), out var odin);
        Assert.That(AutonomousRealmArmyPolicy.IsUsableMuster(odin, 100, new(596364 + 3000, 631509)), Is.False, "inside the guards' sight");
        Assert.That(AutonomousRealmArmyPolicy.IsUsableMuster(odin, 100, new(596364 + 40_000, 631509)), Is.True);
        Assert.That(AutonomousRealmArmyPolicy.IsUsableMuster(odin, 1, new(596364 + 40_000, 631509)), Is.False, "another region");
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void TheMarchWindowGrowsWithTheMusterDistanceOnlyInTheRvrWorld(WorldModelKind world)
    {
        long near = AutonomousRealmArmyPolicy.ReleaseHoldMilliseconds(world, 9_000);
        long far = AutonomousRealmArmyPolicy.ReleaseHoldMilliseconds(world, 120_000);
        Assert.That(near, Is.GreaterThanOrEqualTo(5 * 60_000L));
        if (world == WorldModelKind.Camlann)
            Assert.That((near, far), Is.EqualTo((5 * 60_000L, 5 * 60_000L)));
        else
        {
            Assert.That(far, Is.GreaterThan(near), "120,000 units take about ten minutes on foot");
            Assert.That(far, Is.LessThanOrEqualTo(AutonomousRvrSiegeMuster.MaximumMarchAllowanceMilliseconds));
        }
    }

    [Test]
    public void TheMusterCallSoundsLikeARaidLeader()
    {
        string call = CamlannRvrEventLayer.RealmArmyMusterCall("the Albion portal outpost", "Svasud Faste");
        Assert.That(call, Does.Contain("the Albion portal outpost").And.Contain("Svasud Faste"));
        Assert.That(call, Does.Not.Contain("guild").IgnoreCase);
    }

    // --- Reserve and invaders ------------------------------------------------------------------------------

    [Test]
    public void TheLiveReserveGrowsWhileInvadersStandInTheHomeFrontier()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRealmArmyPolicy.ReservePercent(15, 0, 20, 4), Is.EqualTo(15));
            Assert.That(AutonomousRealmArmyPolicy.ReservePercent(15, 4, 20, 4), Is.EqualTo(35));
            Assert.That(AutonomousRealmArmyPolicy.ReservePercent(40, 9, 40, 4), Is.EqualTo(AutonomousRealmArmyPolicy.MaximumReservePercent));
            Assert.That(AutonomousRealmArmyProperties.RVR_ARMY_RESERVE_INVADER_PERCENT, Is.EqualTo(20));
        });
    }

    [Test]
    public void OnePassingEnemyIsNoInvasion()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRealmArmyProperties.RVR_ARMY_INVADER_MIN, Is.EqualTo(4));
            Assert.That(AutonomousRealmArmyPolicy.InvadersAtHome(1, 4), Is.False, "a single passing enemy bot");
            Assert.That(AutonomousRealmArmyPolicy.InvadersAtHome(3, 4), Is.False);
            Assert.That(AutonomousRealmArmyPolicy.InvadersAtHome(4, 4), Is.True);
            Assert.That(AutonomousRealmArmyPolicy.InvadersAtHome(1, 0), Is.True, "the minimum is never below one");
            Assert.That(AutonomousRealmArmyPolicy.InvadersAtHome(0, 0), Is.False);
            Assert.That(AutonomousRealmArmyPolicy.ReservePercent(15, 3, 20, 4), Is.EqualTo(15), "below the minimum: no bonus");
        });
    }

    [Test]
    public void TheCensusCountsEnemiesInEachHomeFrontier()
    {
        int[] counts = AutonomousRealmArmyCensus.Count([
            (eRealm.Midgard, (ushort)1, true), (eRealm.Hibernia, (ushort)1, true), (eRealm.Albion, (ushort)1, true),
            (eRealm.Midgard, (ushort)1, false), (eRealm.Albion, (ushort)200, true), (eRealm.Albion, (ushort)51, true),
            (eRealm.None, (ushort)100, true)]);
        Assert.That(counts[(int)eRealm.Albion], Is.EqualTo(2), "two living invaders; the dead and own bots do not count");
        Assert.That(counts[(int)eRealm.Hibernia], Is.EqualTo(1));
        Assert.That(counts[(int)eRealm.Midgard], Is.Zero, "realm-less actors are no invaders");
    }

    [Test]
    public void TheCensusCountsHumansAndTheirCompanionsAsInvaders()
    {
        var bots = new[] { new AutonomousRealmArmyCensus.Actor(eRealm.Albion, 1, true) };
        var humans = new (AutonomousRealmArmyCensus.Actor, System.Collections.Generic.IEnumerable<AutonomousRealmArmyCensus.Actor>)[]
        {
            (new(eRealm.Hibernia, 1, true), [new(eRealm.Hibernia, 1, true), new(eRealm.Hibernia, 1, true), new(eRealm.Hibernia, 1, false)]),
            (new(eRealm.Albion, 1, true), [new(eRealm.Albion, 1, true)]),
            (new(eRealm.Midgard, 200, true), null),
        };

        int[] counts = AutonomousRealmArmyCensus.Count(AutonomousRealmArmyCensus.Actors(bots, humans));

        Assert.That(counts[(int)eRealm.Albion], Is.EqualTo(3), "a Hibernian player and two living companions in Albion's frontier");
        Assert.That(counts[(int)eRealm.Hibernia], Is.EqualTo(1), "a Midgard player without companions");
        Assert.That(AutonomousRealmArmyPolicy.InvadersAtHome(counts[(int)eRealm.Albion] + 1, 4), Is.True);
    }

    private static readonly AutonomousRvrEventLayer.LiveObjective HomeInvader = new("rvr-enemy-2", "invader",
        AutonomousRvrEventLayer.Intent.HuntEnemy, eRealm.Hibernia, 1, 1, 1, 0, false, 1, 0, 0, 0);
    private static readonly AutonomousRvrEventLayer.LiveObjective AbroadPatrol = new("rvr-camp-abroad", "patrol abroad",
        AutonomousRvrEventLayer.Intent.Roam, eRealm.None, 100, 1, 1, 0, false, 0, 0, 0, 0);

    [Test]
    public void CallersOtherThanTheReserveStillRoamAbroad()
    {
        using var _ = BothWorlds.Use(WorldModelKind.Realm);
        AutonomousRealmArmyCensus.SetForTests(eRealm.Albion, 10, Forever);

        var lowLevel = new AutonomousRvrEventLayer.Force("low", eRealm.Albion, 8, 30, 2, MinimumMemberLevel: 30, GuildName: "Lions of Albion");
        Assert.That(CamlannRvrEventLayer.ChooseOrJoin(lowLevel, [HomeInvader, AbroadPatrol], 1, 0)?.TargetId,
            Is.EqualTo("rvr-camp-abroad"), "a low-level warband is no home reserve");

        CamlannRvrEventLayer.ChooseOrJoin(Warband("open", eRealm.Albion, "Lions of Albion"), [Keep("keep-1", eRealm.None)], 2, 0);
        var capped = CamlannRvrEventLayer.ChooseOrJoin(Warband("capped", eRealm.Albion, "Ye Olde Alehouse"),
            [Keep("keep-2", eRealm.Midgard), HomeInvader, AbroadPatrol], 3, 0);
        Assert.That(capped?.TargetId, Is.EqualTo("rvr-camp-abroad"), "a warband without a free siege slot roams as before");
    }

    [Test]
    public void TheReserveStaysHomeOnlyWhileAnInvasionIsCounted()
    {
        using var _ = BothWorlds.Use(WorldModelKind.Realm);
        var reserve = Warband("res", eRealm.Albion, "Lions of Albion", reserve: true);

        AutonomousRealmArmyCensus.SetForTests(eRealm.Albion, 3, Forever);
        Assert.That(CamlannRvrEventLayer.ChooseOrJoin(reserve, [HomeInvader, AbroadPatrol], 1, 0)?.TargetId,
            Is.EqualTo("rvr-camp-abroad"), "three enemies are passers-by");

        AutonomousRealmArmyCensus.SetForTests(eRealm.Albion, 4, Forever);
        Assert.That(CamlannRvrEventLayer.ChooseOrJoin(reserve, [HomeInvader, AbroadPatrol], 2, 0)?.TargetId,
            Is.EqualTo("rvr-enemy-2"));
    }

    [Test]
    public void TheHomeReserveHuntsInvadersInItsOwnFrontier()
    {
        using var _ = BothWorlds.Use(WorldModelKind.Realm);
        AutonomousRealmArmyCensus.SetForTests(eRealm.Albion, 4, Forever);
        var abroad = new AutonomousRvrEventLayer.LiveObjective("rvr-enemy-1", "far", AutonomousRvrEventLayer.Intent.HuntEnemy,
            eRealm.Hibernia, 200, 1, 1, 0, false, 1, 0, 0, 0);
        var invader = abroad with { Id = "rvr-enemy-2", Name = "invader", RegionId = 1 };
        for (int i = 0; i < 20; i++)
        {
            var plan = CamlannRvrEventLayer.ChooseOrJoin(Warband($"res-{i}", eRealm.Albion, "Lions of Albion", reserve: true),
                [abroad, invader], 1 + i, 0);
            Assert.That(plan?.TargetId, Is.EqualTo("rvr-enemy-2"));
            Assert.That(plan.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.HuntEnemy));
        }
    }

    // --- Grudges -------------------------------------------------------------------------------------------

    [Test]
    public void RevengeTripsAreCappedPerTargetAndRealm()
    {
        const string target = "guild-kos-Character-ked";
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRealmGrudgeHunts.TryClaim(eRealm.Albion, target, "a", 0, 2), Is.True);
            Assert.That(AutonomousRealmGrudgeHunts.TryClaim(eRealm.Albion, target, "b", 1, 2), Is.True);
            Assert.That(AutonomousRealmGrudgeHunts.HasRoom(eRealm.Albion, target, "c", 2, 2), Is.False);
            Assert.That(AutonomousRealmGrudgeHunts.TryClaim(eRealm.Albion, target, "c", 2, 2), Is.False, "a third warband is turned away");
            Assert.That(AutonomousRealmGrudgeHunts.TryClaim(eRealm.Albion, target, "a", 3, 2), Is.True, "a hunter renews its lease");
            Assert.That(AutonomousRealmGrudgeHunts.TryClaim(eRealm.Midgard, target, "m", 3, 2), Is.True, "another realm hunts on its own");
            Assert.That(AutonomousRealmGrudgeHunts.TryClaim(eRealm.Albion, target, "c",
                3 + AutonomousRealmGrudgeHunts.LeaseMilliseconds, 2), Is.True, "leases run out after ten minutes");
            Assert.That(AutonomousRealmGrudgeHunts.TryClaim(eRealm.Albion, target, "x", 4, 0), Is.False, "cap 0: no revenge trips");
            Assert.That(AutonomousRealmGrudgeHunts.Snapshot(eRealm.Albion).Capped, Is.EqualTo(2));
            Assert.That(AutonomousRealmArmyProperties.RVR_GRUDGE_HUNTERS_PER_TARGET, Is.EqualTo(2));
        });
    }

    [Test]
    public void AWarbandTurnedAwayByAFullTargetCountsAsCapped()
    {
        AutonomousRealmGrudgeHunts.NoteCapped(eRealm.Midgard);
        AutonomousRealmGrudgeHunts.NoteCapped(eRealm.None);
        Assert.That(AutonomousRealmGrudgeHunts.Snapshot(eRealm.Midgard).Capped, Is.EqualTo(1));
        Assert.That(AutonomousRealmGrudgeHunts.Snapshot(eRealm.Albion).Capped, Is.Zero);
    }

    [Test]
    public void AWarbandHuntsOneGrudgeAtATime()
    {
        AutonomousRealmGrudgeHunts.TryClaim(eRealm.Albion, "t1", "a", 0, 1);
        AutonomousRealmGrudgeHunts.TryClaim(eRealm.Albion, "t2", "a", 1, 1);
        Assert.That(AutonomousRealmGrudgeHunts.HasRoom(eRealm.Albion, "t1", "b", 2, 1), Is.True, "the first lease was given up");
    }

    [Test]
    public void TheGrudgeShareIsCountedPerRealm()
    {
        for (int i = 0; i < 10; i++)
            AutonomousRealmGrudgeHunts.NotePick(eRealm.Hibernia, i < 3, i);
        AutonomousRealmGrudgeHunts.NotePick(eRealm.Albion, true, 11);
        var share = AutonomousRealmGrudgeHunts.Snapshot(eRealm.Hibernia);
        Assert.That((share.Picks, share.Grudges), Is.EqualTo((10L, 3L)));
        Assert.That(share.GrudgeShare, Is.EqualTo(0.3).Within(1e-9));
        Assert.That(AutonomousRealmGrudgeHunts.Snapshot(eRealm.Albion).Picks, Is.EqualTo(1));
    }
}
