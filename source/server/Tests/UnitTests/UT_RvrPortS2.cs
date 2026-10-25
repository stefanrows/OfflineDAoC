using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DOL.Database;
using DOL.GS.Keeps;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Port session S2 (docs/RVR_PORT_MAP.md): the RvR world runs the upstream march and siege floor (shadowofze/OfflineDAoC
/// 0.38c): one siege-job pool per realm with upstream's slots and engines, the heavy ram crewed by the bots at it and
/// without the fork's test multiplier, the upstream keep levels after a capture and a claim, no RvR task below level 20,
/// the livelier-RvR waves off outside the battlegrounds, solo siege recruits merged into parties through the facade,
/// and every friend-or-foe compare of the ported code routed through the realm rule. Camlann keeps the fork's rules.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_RvrPortS2
{
    [SetUp] public void SetUp() => RvrEventTestState.Clear();
    [TearDown] public void TearDown() => RvrEventTestState.Clear();

    [TestCase(WorldModelKind.Camlann, 3, ExpectedResult = 0)]
    [TestCase(WorldModelKind.Camlann, 7, ExpectedResult = 0)]
    [TestCase(WorldModelKind.Camlann, 8, ExpectedResult = 2)]
    [TestCase(WorldModelKind.Camlann, 24, ExpectedResult = 4)]
    [TestCase(WorldModelKind.Camlann, 64, ExpectedResult = 6)]
    [TestCase(WorldModelKind.Realm, 3, ExpectedResult = 0)]
    [TestCase(WorldModelKind.Realm, 4, ExpectedResult = 1, TestName = "RvR: a small force still brings one ram")]
    [TestCase(WorldModelKind.Realm, 8, ExpectedResult = 3, TestName = "RvR: a warband brings both rams and the trebuchet")]
    [TestCase(WorldModelKind.Realm, 23, ExpectedResult = 3)]
    [TestCase(WorldModelKind.Realm, 24, ExpectedResult = 5)]
    [TestCase(WorldModelKind.Realm, 64, ExpectedResult = 6)]
    public int SiegeJobsFollowTheWorldsRule(WorldModelKind world, int present) => AutonomousSiegeJobs.Slots(world, present);

    [Test]
    public void TheDefaultSlotRuleFollowsTheLatchedWorld()
    {
        using (WorldMode.OverrideForTests(WorldModelKind.Realm))
            Assert.That(AutonomousSiegeJobs.Slots(8), Is.EqualTo(3));
        using (WorldMode.OverrideForTests(WorldModelKind.Camlann))
            Assert.That(AutonomousSiegeJobs.Slots(8), Is.EqualTo(2));
    }

    [Test]
    public void TheRvrWorldBringsTwoRamsATrebuchetAndACatapultAndDefendersNeverATrebuchet()
    {
        BotSiegeKind[] attack = Enumerable.Range(0, 6).Select(i => AutonomousSiegeJobs.SlotKind(WorldModelKind.Realm, i, true)).ToArray();
        BotSiegeKind[] defend = Enumerable.Range(0, 4).Select(i => AutonomousSiegeJobs.SlotKind(WorldModelKind.Realm, i, false)).ToArray();
        BotSiegeKind[] camlann = Enumerable.Range(0, 6).Select(i => AutonomousSiegeJobs.SlotKind(WorldModelKind.Camlann, i, true)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(attack, Is.EqualTo(new[] { BotSiegeKind.Ram, BotSiegeKind.Ram, BotSiegeKind.Trebuchet,
                BotSiegeKind.Catapult, BotSiegeKind.Ballista, BotSiegeKind.Ballista }));
            Assert.That(defend, Does.Not.Contain(BotSiegeKind.Trebuchet));
            Assert.That(defend, Is.EqualTo(new[] { BotSiegeKind.Ballista, BotSiegeKind.Ballista, BotSiegeKind.Catapult, BotSiegeKind.Catapult }));
            Assert.That(camlann, Is.EqualTo(new[] { BotSiegeKind.Ram, BotSiegeKind.Ram, BotSiegeKind.Catapult,
                BotSiegeKind.Trebuchet, BotSiegeKind.Ballista, BotSiegeKind.Ballista }), "Camlann keeps the fork's slots");
        });
    }

    [Test]
    public void ACarriedKitTakesItsSlotFirst()
    {
        Assert.That(AutonomousSiegeJobs.SlotOrder(WorldModelKind.Realm, 3, true, BotSiegeKind.Trebuchet), Is.EqualTo(new[] { 2, 0, 1 }));
        Assert.That(AutonomousSiegeJobs.SlotOrder(WorldModelKind.Camlann, 4, true, null), Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    [TestCase(WorldModelKind.Realm, 500, 475, true, TestName = "RvR: the heavy ram fires within 475")]
    [TestCase(WorldModelKind.Realm, 500, 476, false, TestName = "RvR: the heavy ram never beyond 475")]
    [TestCase(WorldModelKind.Camlann, 400, 375, true, TestName = "Camlann: the light ram fires within 375")]
    [TestCase(WorldModelKind.Camlann, 400, 401, false, TestName = "Camlann: the light ram never beyond 375")]
    public void BotCrewsUseTheWorldsRam(WorldModelKind world, int reach, int distance, bool valid)
    {
        var ram = (GameSiegeRam)AutonomousWorldBotController.CreateSiegeWeapon(world, BotSiegeKind.Ram);
        Assert.Multiple(() =>
        {
            Assert.That(ram.attackComponent.AttackRange, Is.EqualTo(reach));
            Assert.That(AutonomousWorldBotController.InSiegeRange(ram, new Target { X = distance }), Is.EqualTo(valid));
        });
    }

    [Test]
    public void TheRvrWorldsRamHitsWithTheOriginalDamage()
    {
        var ram = new GameSiegeRam { Level = 3 };
        int baseDamage = ram.BaseDamage;
        using (WorldMode.OverrideForTests(WorldModelKind.Realm))
            Assert.That(ram.CalcDamageToTarget(null), Is.EqualTo(baseDamage), "no crew, no test multiplier");
        using (WorldMode.OverrideForTests(WorldModelKind.Camlann))
            Assert.That(ram.CalcDamageToTarget(null), Is.EqualTo(baseDamage * 10), "Camlann keeps the fork's test multiplier");
    }

    [TestCase(WorldModelKind.Camlann, false, ExpectedResult = true)]
    [TestCase(WorldModelKind.Camlann, true, ExpectedResult = true)]
    [TestCase(WorldModelKind.Realm, false, ExpectedResult = false, TestName = "RvR: the waves are off for world bots")]
    [TestCase(WorldModelKind.Realm, true, ExpectedResult = true, TestName = "RvR: battleground crews keep the fork's waves")]
    public bool TheLivelierRvrWavesBelongToCamlann(WorldModelKind world, bool battlegroundParticipant) =>
        AutonomousRvrWaves.ActiveFor(world, battlegroundParticipant);

    [Test]
    public void KeepLevelsAfterACaptureAndAClaimAreUpstreamsInTheRvrWorld()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PvpKeepCampaign.CaptureLevel(WorldModelKind.Realm), Is.EqualTo(4), "upstream starting_keep_level");
            Assert.That(PvpKeepCampaign.ClaimLevel(WorldModelKind.Realm, 1), Is.EqualTo(5), "upstream starting_keep_claim_level");
            Assert.That(PvpKeepCampaign.CaptureLevel(WorldModelKind.Camlann), Is.EqualTo(PvpKeepCampaign.WardenKeepLevel));
            Assert.That(PvpKeepCampaign.ClaimLevel(WorldModelKind.Camlann, 1), Is.EqualTo(1), "Camlann reads the property");
            Assert.That(PvpKeepCampaign.ClaimLevel(WorldModelKind.Camlann, 3), Is.EqualTo(3));
        });
    }

    [TestCase(WorldModelKind.Realm, 19, ExpectedResult = false, TestName = "RvR: no RvR task below level 20 (upstream goals)")]
    [TestCase(WorldModelKind.Realm, 20, ExpectedResult = true)]
    [TestCase(WorldModelKind.Camlann, 10, ExpectedResult = true, TestName = "Camlann: hunters from 10 as before")]
    public bool RvrTasksStartAtTheWorldsLevel(WorldModelKind world, int level) => AutonomousActivityScheduler.MayTakeRvr(world, level);

    [Test]
    public void ALowLevelHunterTakesNoRvrTaskInTheRvrWorld()
    {
        DateTime now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var record = new OfflineWorldBotRecord { PlayerType = "Hunter", Level = 15,
            Aggression = 50, RiskTolerance = 50, Sociability = 50, Patience = 50 };
        using (WorldMode.OverrideForTests(WorldModelKind.Camlann))
            Assert.That(AutonomousActivityScheduler.Choose(record, now, 0), Is.EqualTo(eAutonomousObjectiveKind.RvR));
        using (WorldMode.OverrideForTests(WorldModelKind.Realm))
        {
            Assert.That(AutonomousActivityScheduler.Choose(record, now, 0), Is.Not.EqualTo(eAutonomousObjectiveKind.RvR));
            record.Level = 20;
            Assert.That(AutonomousActivityScheduler.Choose(record, now, 0), Is.EqualTo(eAutonomousObjectiveKind.RvR));
        }
    }

    [Test]
    public void SoloSiegeRecruitsReachTheCoordinatorOnlyInTheRvrWorld()
    {
        long now = GameLoop.GameLoopTime;
        string id = Guid.NewGuid().ToString();
        var target = new AutonomousRvrEventLayer.LiveObjective(id, "Party rally", AutonomousRvrEventLayer.Intent.AssaultKeep,
            eRealm.Hibernia, 200, 300000, 400000, 100, false, 0, 0, 10, 2);
        RvrEventTestState.ClearRealm();
        using (WorldMode.OverrideForTests(WorldModelKind.Realm))
        {
            Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(new AutonomousRvrEventLayer.Force(id + ":warband", eRealm.Albion, 8, 50, 2),
                [target], now, 0)?.TargetId, Is.EqualTo(id));
            foreach (long member in new long[] { 601, 602 })
                AutonomousRvrEventLayer.ChooseOrJoin(new AutonomousRvrEventLayer.Force($"rvr-{member}", eRealm.Albion, 1, 50, 0,
                    MemberIds: [member]), [target], now, 0);
            Assert.That(AutonomousRvrEventLayer.SoloSiegeRosters(now).Any(r => r.EventId == id), Is.True);
            Assert.That(AutonomousRvrEventLayer.IsEventForce(id + ":warband", now), Is.True, "the muster counts as the event");
        }
        using (WorldMode.OverrideForTests(WorldModelKind.Camlann))
        {
            Assert.That(AutonomousRvrEventLayer.SoloSiegeRosters(now), Is.Empty, "Camlann has no solo rosters");
            Assert.That(AutonomousRvrEventLayer.IsEventForce(id + ":warband", now), Is.False);
            Assert.That(AutonomousRvrEventLayer.MergeSoloForces(id, eRealm.Albion, [601, 602], "party"), Is.False);
        }
    }

    [Test]
    public void AnEmptyHeavyRamHasNoCrewAndTheBaseReload()
    {
        var ram = new GameSiegeRam { Level = 3 };
        int delay = (int)typeof(GameSiegeRam).GetProperty("GetReloadDelay", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ram);
        Assert.That(ram.Crew, Is.Zero, "no operator, no riders");
        Assert.That(delay, Is.EqualTo(10000 + 4 * 2000), "an empty ram reloads at the base delay");
    }

    // Tier 3 audit extension (map section 4b): the code copied from upstream decides friend or foe through
    // PvpCombatant.AreAllied / AllianceRealm or the keep rule, never by comparing two actors' raw realms. Allowed: the
    // keep-to-keep compare of the march detour (the defenders' rally keep) and the roster realm of a siege party.
    private static readonly string[] PortedFiles =
    {
        "AutonomousWorldBotController.RealmRally.cs", "AutonomousWorldBotController.RealmSiege.cs",
        "AutonomousWorldBotController.RealmRvr.cs", "AutonomousWorldBotController.RealmFrontierThreat.cs",
        "AutonomousWorldBotController.RealmMuster.cs", "AutonomousSiegeResurrection.cs", "AutonomousArmyDetour.cs",
        "AutonomousBotGroupCoordinator.SiegeParties.cs",
    };

    private static readonly (string Left, string Right)[] AllowedRawCompares = { ("keep", "target"), ("bot", "roster") };

    [Test]
    public void PortedCodeDecidesFriendOrFoeByTheRealmRule()
    {
        var raw = new Regex(@"\b(\w+)\.Realm\s*(==|!=)\s*(\w+)\.Realm\b");
        foreach (string file in PortedFiles)
        {
            string text = File.ReadAllText(FindRepoFile(Path.Combine("source", "server", "GameServer", "bots", "autonomous", file)));
            foreach (Match match in raw.Matches(text))
            {
                var pair = (match.Groups[1].Value, match.Groups[3].Value);
                Assert.That(AllowedRawCompares, Does.Contain(pair), $"{file}: raw realm compare '{match.Value}'");
            }
            Assert.That(text, Does.Not.Contain("AutonomousKeepApproachNavigation.ForRealm("), $"{file}: keep doors by the keep rule (ForBot)");
        }
    }

    [TestCase(WorldModelKind.Camlann, ExpectedResult = true, TestName = "Camlann: a same-realm stranger is an enemy of the siege code")]
    [TestCase(WorldModelKind.Realm, ExpectedResult = false, TestName = "RvR: a same-realm stranger is an ally of the siege code")]
    public bool TheSiegeCodesSeamTakesTheWorldsAnswer(WorldModelKind world)
    {
        using var _ = WorldMode.OverrideForTests(world);
        GameBot first = StrangerBot(eRealm.Midgard), second = StrangerBot(eRealm.Midgard);
        // The ported siege and raise code asks exactly this seam (AreAllied) for its friend or foe.
        return !DOL.GS.ServerRules.PvpCombatant.AreAllied(first, second);
    }

    private sealed class Target : GameNPC
    {
        public override int MaxHealth => 1000;
        public override int X { get; set; }
        public override int Y { get; set; }
        public override int Z { get; set; }
    }

    private sealed class StrangerTestBot : GameBot
    {
        public StrangerTestBot() : base((OfflineWorldBotRecord)null) { }
        public override bool IsAlive => true;
        public override eRealm Realm { get; set; }
        public override ushort CurrentRegionID { get; set; } = 1;
    }

    private static GameBot StrangerBot(eRealm realm)
    {
        var bot = (StrangerTestBot)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(StrangerTestBot));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(bot, new System.Collections.ArrayList());
        bot.Realm = realm;
        return bot;
    }

    private static string FindRepoFile(string relativePath)
    {
        for (DirectoryInfo directory = new(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }
        Assert.Fail($"Repository file not found from the test directory: {relativePath}");
        return null;
    }
}
