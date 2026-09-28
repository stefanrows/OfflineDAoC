using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.Keeps;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Task 48, siege slice 1: world-bot rams and riders, door melee,
/// claimable targets only, sieges close when nobody reaches the keep, and only
/// whole one-guild warbands open a siege.</summary>
[TestFixture, NonParallelizable]
public sealed class UT_SiegeSliceOne
{
    private EpicTestServerScope _server;

    [SetUp]
    public void SetUp()
    {
        _server = new EpicTestServerScope();
        DOL.GS.Tests.RvrEventTestState.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        DOL.GS.Tests.RvrEventTestState.Clear();
        _server.Dispose();
    }

    private static void Sweep(long now) => AutonomousRvrEventLayer.TryConsumeRelease("sweep-only", now, out _);

    private static System.Collections.Generic.Dictionary<string, int> Bucket(string targetId, string name)
    {
        var events = (System.Collections.IDictionary)typeof(AutonomousRvrEventLayer)
            .GetField("Events", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        object active = events[targetId];
        return (System.Collections.Generic.Dictionary<string, int>)active.GetType().GetField(name).GetValue(active);
    }

    private static AutonomousRvrEventLayer.LiveObjective OpenSiege(string id, long start)
    {
        var target = new AutonomousRvrEventLayer.LiveObjective(id, "Slice Faste",
            AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Midgard, 100, 100_000, 100_000, 0, false, 0, 0, 4, 2);
        Assert.That(AutonomousRvrEventLayer.ForceStart(target, eRealm.Hibernia, start, out string reason), Is.True, reason);
        return target;
    }

    // ---- 1: the siege job runs only for a committed assault near the keep ----

    [TestCase(AutonomousRvrEventLayer.Intent.AssaultKeep, true, true, 5_999d, false, false, true, TestName = "Assault within 6,000 runs the job")]
    [TestCase(AutonomousRvrEventLayer.Intent.AssaultKeep, true, true, 6_001d, false, false, false, TestName = "Assault beyond 6,000 leaves the march alone")]
    [TestCase(AutonomousRvrEventLayer.Intent.AssaultKeep, true, false, 100d, false, false, false, TestName = "Other region does not run the job")]
    [TestCase(AutonomousRvrEventLayer.Intent.AssaultKeep, true, false, double.PositiveInfinity, true, false, true, TestName = "A started ram purchase continues")]
    [TestCase(AutonomousRvrEventLayer.Intent.AssaultKeep, true, false, double.PositiveInfinity, false, true, true, TestName = "Buying at the hub before the march")]
    [TestCase(AutonomousRvrEventLayer.Intent.AssaultKeep, false, true, 100d, false, false, false, TestName = "No shared siege event, no job")]
    [TestCase(AutonomousRvrEventLayer.Intent.Roam, true, true, 100d, false, false, false, TestName = "Roaming warband never runs the job")]
    [TestCase(AutonomousRvrEventLayer.Intent.HuntEnemy, true, true, 100d, true, true, false, TestName = "Hunting warband never runs the job")]
    [TestCase(AutonomousRvrEventLayer.Intent.DefendEvent, true, true, 100d, false, false, false, TestName = "Defenders are not ram operators")]
    public void SiegeJobRunsOnlyUnderAssaultIntentWithinRange(AutonomousRvrEventLayer.Intent intent, bool shared,
        bool inRegion, double distance, bool supplyTrip, bool hub, bool expected) =>
        Assert.That(AutonomousSiegeDoctrine.ShouldRunSiegeJob(intent, shared, inRegion, distance, supplyTrip, hub), Is.EqualTo(expected));

    [Test]
    public void SiegeJobHasItsOwnProfilerPhase()
    {
        Assert.That(Enum.GetNames<BotThinkPhase>(), Does.Contain(nameof(BotThinkPhase.SiegeJob)));
        Assert.That(AutonomousSiegeDoctrine.SiegeJobRadius, Is.EqualTo(6000));
    }

    // ---- 2: door melee fallback and the lord ---------------------------------

    [Test]
    public void WithoutGuardsAMeleeClassHitsTheClosedGate() =>
        Assert.That(AutonomousSiegeDoctrine.PickKeepTarget(true, false, 0, 700, true, false),
            Is.EqualTo(AutonomousSiegeDoctrine.KeepTarget.Door));

    [TestCase(true, 0, TestName = "Lord alive, gate standing, no guards: the gate")]
    [TestCase(true, 3, TestName = "Lord alive, gate standing, guards: the guards")]
    [TestCase(false, 0, TestName = "Lord not targetable, gate standing: the gate")]
    public void TheLordIsNeverPickedWhileAGateStands(bool lord, int guards)
    {
        var pick = AutonomousSiegeDoctrine.PickKeepTarget(true, lord, guards, 500, true, false);
        Assert.That(pick, Is.Not.EqualTo(AutonomousSiegeDoctrine.KeepTarget.Lord));
        Assert.That(pick, Is.EqualTo(guards > 0 ? AutonomousSiegeDoctrine.KeepTarget.Guard : AutonomousSiegeDoctrine.KeepTarget.Door));
    }

    [Test]
    public void OnceEveryGateIsDownTheLordComesFirst() =>
        Assert.That(AutonomousSiegeDoctrine.PickKeepTarget(false, true, 4, double.PositiveInfinity, true, false),
            Is.EqualTo(AutonomousSiegeDoctrine.KeepTarget.Lord));

    [TestCase(1_101d, true, false, TestName = "Gate out of reach")]
    [TestCase(300d, false, false, TestName = "Caster or healer does not melee the gate")]
    [TestCase(300d, true, true, TestName = "Ram operator stays on its engine")]
    public void NoGateTargetOutOfReachForCastersOrOperators(double distance, bool melee, bool operating) =>
        Assert.That(AutonomousSiegeDoctrine.PickKeepTarget(true, false, 0, distance, melee, operating),
            Is.EqualTo(AutonomousSiegeDoctrine.KeepTarget.None));

    [Test]
    public void GuardsBeforeTheGateAndNothingWithoutAGate()
    {
        Assert.That(AutonomousSiegeDoctrine.PickKeepTarget(true, false, 2, 100, true, false), Is.EqualTo(AutonomousSiegeDoctrine.KeepTarget.Guard));
        Assert.That(AutonomousSiegeDoctrine.PickKeepTarget(false, false, 0, 100, true, false), Is.EqualTo(AutonomousSiegeDoctrine.KeepTarget.None));
    }

    [TestCase(eCharacterClass.Armsman, true, false)]
    [TestCase(eCharacterClass.Berserker, true, false)]
    [TestCase(eCharacterClass.Nightshade, true, false)]
    [TestCase(eCharacterClass.Paladin, true, false)]
    [TestCase(eCharacterClass.Friar, true, false)]
    [TestCase(eCharacterClass.Valewalker, true, false)]
    [TestCase(eCharacterClass.Cleric, false, false)]
    [TestCase(eCharacterClass.Healer, false, false)]
    [TestCase(eCharacterClass.Druid, false, false)]
    [TestCase(eCharacterClass.Bard, false, false)]
    [TestCase(eCharacterClass.Wizard, false, true)]
    [TestCase(eCharacterClass.Runemaster, false, true)]
    [TestCase(eCharacterClass.Eldritch, false, true)]
    [TestCase(eCharacterClass.Theurgist, false, true)]
    public void MeleeHitsTheGateCastersRideTheRamHealersStayFree(eCharacterClass characterClass, bool melee, bool rides)
    {
        Assert.That(AutonomousSiegeDoctrine.CanMeleeDoor(characterClass), Is.EqualTo(melee));
        Assert.That(AutonomousSiegeDoctrine.RidesRam(characterClass), Is.EqualTo(rides));
    }

    private sealed record Gate(string Name, Vector2 Position);

    [Test]
    public void TheOuterGateComesBeforeTheInnerGate()
    {
        var inner = new Gate("inner", new(100_200, 100_000));
        var outer = new Gate("outer", new(100_900, 100_000));
        Assert.That(AutonomousSiegeDoctrine.Outermost(new[] { inner, outer }, g => g.Position, new(100_000, 100_000)), Is.SameAs(outer));
        Assert.That(AutonomousSiegeDoctrine.Outermost(new[] { inner }, g => g.Position, new(100_000, 100_000)), Is.SameAs(inner),
            "with the outer gate down the inner gate is next");
        Assert.That(AutonomousSiegeDoctrine.Outermost(Array.Empty<Gate>(), g => g.Position, default), Is.Null);
    }

    private sealed class Walker : GameBot
    {
        private Walker() : base((OfflineWorldBotRecord)null) { }
        public override eRealm Realm { get; set; }
        public override bool IsAlive => true;
    }

    private sealed class PvpKeeps : DefaultKeepManager
    {
        protected override bool IsPvpServer => true;
    }

    private sealed class ConfiguredServer : GameServer
    {
        private static readonly IObjectDatabase Empty =
            DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private static readonly GameServerConfiguration Normal = new() { ServerType = EGameServerType.GST_Normal };
        protected override IObjectDatabase DataBaseImpl => Empty;
        public override GameServerConfiguration Configuration => Normal;
    }

    private static Guild MakeGuild(string id, string name)
    {
        GameServer previous = GameServer.Instance;
        GameServer.LoadTestDouble((ConfiguredServer)RuntimeHelpers.GetUninitializedObject(typeof(ConfiguredServer)));
        try { RuntimeHelpers.RunClassConstructor(typeof(Guild).TypeHandle); }
        finally { GameServer.LoadTestDouble(previous); }
        var guild = (Guild)RuntimeHelpers.GetUninitializedObject(typeof(Guild));
        typeof(Guild).GetField("m_DBguild", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(guild, new DbGuild { GuildID = id, GuildName = name });
        return guild;
    }

    private static Walker RvrBot(eRealm realm, Guild guild = null)
    {
        var bot = (Walker)RuntimeHelpers.GetUninitializedObject(typeof(Walker));
        bot.Realm = realm;
        typeof(GameBot).GetField("_dummyClient", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new BotDummyClient());
        typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, true);
        typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot, new OfflineWorldBotRecord { ObjectiveKind = "RvR" });
        bot.Guild = guild;
        return bot;
    }

    private static GameKeepDoor WardenGate(byte keepLevel)
    {
        var keep = new GameKeep
        {
            DBKeep = new DbKeep { Realm = 1, BaseLevel = 50, Level = keepLevel },
            Guild = MakeGuild("wardens", PvpKeepCampaign.GarrisonName)
        };
        var component = (GameKeepComponent)RuntimeHelpers.GetUninitializedObject(typeof(GameKeepComponent));
        component.Keep = keep;
        return new GameKeepDoor { Component = component };
    }

    [Test]
    public void AWardenGateIsHostileToAWorldBotOfAnyGuild()
    {
        var gate = WardenGate(5);
        var keeps = new PvpKeeps();
        Assert.That(keeps.IsEnemy(gate, RvrBot(eRealm.Albion, MakeGuild("guild-a", "North Road"))), Is.True);
        Assert.That(keeps.IsEnemy(gate, RvrBot(eRealm.Midgard)), Is.True, "guildless bots too");
    }

    [TestCase(true, 400, 300, TestName = "Bot melee damages a level-5 gate at 75 percent")]
    [TestCase(false, 400, 0, TestName = "With doors_allowpetattack off, NPC melee does nothing")]
    public void BotMeleeDamagesKeepDoorsUnderTheCurrentRules(bool allowNpcDoorDamage, int dealt, int applied)
    {
        bool previousAllow = Properties.DOORS_ALLOWPETATTACK;
        int previousToughness = Properties.SET_KEEP_DOOR_TOUGHNESS;
        try
        {
            Properties.DOORS_ALLOWPETATTACK = allowNpcDoorDamage;
            Properties.SET_KEEP_DOOR_TOUGHNESS = 100; // live value
            var gate = WardenGate(5);
            var attacker = RvrBot(eRealm.Albion);
            typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(attacker, new System.Collections.ArrayList());
            var hit = new AttackData { Attacker = attacker, Target = gate, Damage = dealt,
                DamageType = eDamageType.Slash, AttackResult = eAttackResult.HitUnstyled };
            gate.ModifyAttack(hit);
            Assert.That(hit.Damage, Is.EqualTo(applied));
            Assert.That(hit.AttackResult, Is.EqualTo(allowNpcDoorDamage ? eAttackResult.HitUnstyled : eAttackResult.NotAllowed_ServerRules));
        }
        finally
        {
            Properties.DOORS_ALLOWPETATTACK = previousAllow;
            Properties.SET_KEEP_DOOR_TOUGHNESS = previousToughness;
        }
    }

    // ---- riders --------------------------------------------------------------

    [TestCase(true, true, true, false, 2, 6, false, true, TestName = "Caster boards its group's ram at a standing gate")]
    [TestCase(true, true, true, false, 6, 6, false, false, TestName = "No free seat")]
    [TestCase(true, true, true, true, 6, 6, false, true, TestName = "A seated rider keeps its seat")]
    [TestCase(true, true, false, true, 3, 6, false, false, TestName = "Gate down: riders get off")]
    [TestCase(true, false, true, false, 0, 6, false, false, TestName = "Only the own warband's ram")]
    [TestCase(true, true, true, true, 3, 6, true, false, TestName = "Attacked in melee: get off and fight")]
    [TestCase(false, true, true, false, 0, 6, false, false, TestName = "Melee classes and healers do not ride")]
    public void RidersBoardOnlyTheirWarbandsWorkingRam(bool riderClass, bool sameGroup, bool onGate, bool riding,
        int riders, int seats, bool attacked, bool expected) =>
        Assert.That(AutonomousSiegeDoctrine.ShouldRide(riderClass, sameGroup, onGate, riding, riders, seats, attacked), Is.EqualTo(expected));

    [Test]
    public void ACasterEightHundredUnitsFromTheRamBoardsWithinAFewThinks()
    {
        // Think every second, walking 190 units per think (normal run speed).
        // Each think without a ram goal lets the keep-approach hold pull the
        // caster back to its approach point, as ExecuteRvr does.
        const double approach = 800, boardRadius = AutonomousSiegeDoctrine.RamBoardRadius;
        double distance = approach;
        long nextSearch = 0;
        bool remembered = false;
        int thinks = 0;
        for (long now = 0; now < 60_000 && distance > boardRadius; now += 1_000)
        {
            thinks++;
            if (AutonomousSiegeDoctrine.ShouldSearchForRam(remembered, now, nextSearch))
            {
                remembered = true; // found the group's ram on the first search
                distance = Math.Max(0, distance - 190);
            }
            else distance = approach; // pulled back to the approach point
        }
        Assert.That(distance, Is.LessThanOrEqualTo(boardRadius), "boarded");
        Assert.That(thinks, Is.LessThanOrEqualTo(3));
    }

    [TestCase(false, 4_999, false, TestName = "No ram chosen: fresh search waits for the throttle")]
    [TestCase(false, 5_000, true, TestName = "No ram chosen: search when the throttle expires")]
    [TestCase(true, 1_000, true, TestName = "Walking to a chosen ram: every think")]
    public void OnlyFreshRamSearchesAreThrottled(bool remembered, long now, bool expected) =>
        Assert.That(AutonomousSiegeDoctrine.ShouldSearchForRam(remembered, now, 5_000), Is.EqualTo(expected));

    [Test]
    public void AWorldBotCannotBoardARamWithoutAnOperatorOfItsGroup()
    {
        var ram = (GameSiegeRam)AutonomousWorldBotController.CreateSiegeWeapon(BotSiegeKind.Ram);
        Assert.That(ram.BoardWorldBot(RvrBot(eRealm.Albion)), Is.False);
        Assert.That(ram.BoardWorldBot(null), Is.False);
        Assert.That(ram.PassengerCount, Is.Zero);
    }

    // ---- 3: relic and battleground keeps are not automatic targets ------------

    [TestCase(false, false, 50, false, true, TestName = "Ordinary level-50 keep is claimable")]
    [TestCase(false, true, 60, false, false, TestName = "Relic keep (Castle Myrddin) is not")]
    [TestCase(false, false, 60, false, false, TestName = "Base level 60 without relic skin is not")]
    [TestCase(false, false, 39, false, false, TestName = "Battleground keep is not")]
    [TestCase(false, false, 39, true, true, TestName = "Battleground keep with allow_bg_claim is")]
    [TestCase(true, false, 50, true, false, TestName = "Portal keep never is")]
    public void OnlyClaimableKeepsAreAutomaticTargets(bool portal, bool relic, int baseLevel, bool allowBg, bool expected) =>
        Assert.That(AutonomousRvrKeepPolicy.IsClaimableKeep(portal, relic, baseLevel, allowBg), Is.EqualTo(expected));

    [Test]
    public void TheLiveKeepRowsDecideClaimability()
    {
        bool previous = Properties.ALLOW_BG_CLAIM;
        try
        {
            Properties.ALLOW_BG_CLAIM = false;
            var myrddin = new GameKeep { DBKeep = new DbKeep { BaseLevel = 60, SkinType = 99, Realm = 1 } };
            var benowyc = new GameKeep { DBKeep = new DbKeep { BaseLevel = 50, SkinType = 0, Realm = 1 } };
            Assert.That(AutonomousRvrKeepPolicy.IsClaimableKeep(myrddin), Is.False);
            Assert.That(AutonomousRvrKeepPolicy.IsClaimableKeep(benowyc), Is.True);
            Assert.That(AutonomousRvrKeepPolicy.IsClaimableKeep((AbstractGameKeep)null), Is.False);
        }
        finally { Properties.ALLOW_BG_CLAIM = previous; }
    }

    [Test]
    public void AnUnclaimableKeepIsNeverOpenedAutomatically()
    {
        var relic = new AutonomousRvrEventLayer.LiveObjective("rvr-keep-58", "Castle Myrddin",
            AutonomousRvrEventLayer.Intent.AssaultRelicKeep, eRealm.None, 1, 100, 100, 0, true, 0, 0, 12, 2, Claimable: false);
        var battleground = new AutonomousRvrEventLayer.LiveObjective("rvr-keep-5", "Dun Orseo",
            AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.None, 165, 100, 100, 0, false, 0, 0, 6, 2, Claimable: false);
        var claimable = new AutonomousRvrEventLayer.LiveObjective("rvr-keep-50", "Caer Benowyc",
            AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.None, 1, 100, 100, 0, false, 0, 0, 6, 2);
        for (int roll = 0; roll < 20; roll++)
        {
            var force = new AutonomousRvrEventLayer.Force("force-" + roll, eRealm.Albion, 8, 50, 2, GuildName: "Raiders");
            var plan = AutonomousRvrEventLayer.ChooseOrJoin(force, [relic, battleground], 1_000 + roll, roll / 20d);
            Assert.That(plan?.IsSharedEvent == true, Is.False, $"roll {roll}");
        }
        var opener = new AutonomousRvrEventLayer.Force("opener", eRealm.Albion, 8, 50, 2, GuildName: "Raiders");
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(opener, [relic, battleground, claimable], 5_000, 0)?.TargetId,
            Is.EqualTo(claimable.Id));
    }

    // ---- 4: idle close and march credit ----------------------------------------

    [Test]
    public void ASiegeNobodyReachesClosesAfterFortyFiveMinutesEvenWhileMembersMarch()
    {
        const long start = 200_000_000;
        var target = OpenSiege("rvr-keep-9101", start);
        Bucket(target.Id, "Attackers").Add("far", 8);
        // Real in-region approach progress every two minutes, but it never gets
        // within 3,000 units of the keep (a march that keeps failing short).
        for (int minute = 1; minute <= 44; minute++)
        {
            AutonomousRvrEventLayer.ReportMarch(target.Id, "far", 1, 100,
                new(100_000, 190_000 - minute * 1_000, 0), false, start + minute * 60_000L);
            Sweep(start + minute * 60_000L);
        }
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.True, "44 minutes, still marching");
        AutonomousRvrEventLayer.ReportMarch(target.Id, "far", 1, 100, new(100_000, 145_000, 0), false, start + 45 * 60_000L);
        Sweep(start + 45 * 60_000L);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.False, "nobody within 3,000 for 45 minutes");
        Assert.That(AutonomousRvrEventLayer.CooldownRemaining(target.Id, start + 45 * 60_000L), Is.GreaterThan(0));
    }

    [Test]
    public void ASiegeWithAForceWithinThreeThousandStaysOpenPastAnHour()
    {
        const long start = 210_000_000;
        var target = OpenSiege("rvr-keep-9102", start);
        Bucket(target.Id, "Attackers").Add("at-walls", 8);
        for (int minute = 1; minute <= 70; minute++)
        {
            long now = start + minute * 60_000L;
            // Standing at the gate approach, 2,500 units out: no approach progress.
            AutonomousRvrEventLayer.ReportMarch(target.Id, "at-walls", 2, 100, new(100_000, 102_500, 0), false, now);
            if (minute % 5 == 0) AutonomousRvrEventLayer.ReportBattleActivity(target.Id, now, "at-walls");
            Sweep(now);
        }
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.True);
    }

    [Test]
    public void DamagingTheKeepCountsAsPresenceAndProgress()
    {
        const long start = 215_000_000;
        var target = OpenSiege("rvr-keep-9103", start);
        Bucket(target.Id, "Attackers").Add("rammers", 8);
        for (int minute = 10; minute <= 70; minute += 10)
        {
            AutonomousRvrEventLayer.ReportBattleActivity(target.Id, start + minute * 60_000L, "rammers");
            Sweep(start + minute * 60_000L);
        }
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.True);
    }

    [TestCase(false, 44, true)]
    [TestCase(false, 45, false)]
    [TestCase(true, 90, true)]
    public void AbsenceRuleSparesPlayerSieges(bool playerLed, int minutes, bool open) =>
        Assert.That(AutonomousRvrEventLayer.IsAbandonedByAttackers(true, false, playerLed, 0, minutes * 60_000L), Is.EqualTo(!open));

    [Test]
    public void OnlyApproachInsideTheKeepRegionEarnsMarchCredit()
    {
        const long start = 220_000_000;
        var target = OpenSiege("rvr-keep-9104", start);
        Bucket(target.Id, "Attackers").Add("carousel", 1);
        // Porter carousel: port between two other regions and walk around at
        // the hub, every minute, never approaching inside region 100.
        for (int minute = 0; minute <= 16; minute++)
        {
            ushort region = (ushort)(minute % 2 == 0 ? 1 : 200);
            AutonomousRvrEventLayer.ReportMarch(target.Id, "carousel", 40, region,
                new(500_000 + minute * 2_000, 400_000, 0), false, start + minute * 60_000L);
        }
        Sweep(start + 15 * 60_000L);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.False,
            "region changes and off-region walking earn nothing; closed 15 minutes after opening");
    }

    [Test]
    public void EnteringTheKeepRegionCountsOncePerMember()
    {
        const long start = 230_000_000;
        var target = OpenSiege("rvr-keep-9105", start);
        Bucket(target.Id, "Attackers").Add("returner", 1);
        // The member starts in the keep's region far out, ports away after a
        // death at minute 1, re-enters at minute 5 (credited once), ports away
        // again and re-enters at minute 12 at the same spot (no credit).
        AutonomousRvrEventLayer.ReportMarch(target.Id, "returner", 50, 100, new(100_000, 150_000, 0), false, start);
        AutonomousRvrEventLayer.ReportMarch(target.Id, "returner", 50, 1, new(500_000, 400_000, 0), false, start + 60_000);
        AutonomousRvrEventLayer.ReportMarch(target.Id, "returner", 50, 100, new(100_000, 150_000, 0), false, start + 5 * 60_000L);
        AutonomousRvrEventLayer.ReportMarch(target.Id, "returner", 50, 1, new(500_000, 400_000, 0), false, start + 8 * 60_000L);
        AutonomousRvrEventLayer.ReportMarch(target.Id, "returner", 50, 100, new(100_000, 150_000, 0), false, start + 12 * 60_000L);
        Sweep(start + 19 * 60_000L);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.True, "credited for the entry at minute 5");
        Sweep(start + 20 * 60_000L);
        Assert.That(AutonomousRvrEventLayer.IsTargetActive(target.Id, start + 1), Is.False, "the second entry at minute 12 earned nothing");
    }

    // ---- 5: join rule ------------------------------------------------------------

    private static AutonomousRvrEventLayer.LiveObjective Keep(string id) => new(id, "Join Faste",
        AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Midgard, 100, 100_000, 100_000, 0, false, 0, 0, 4, 2,
        OwningGuild: "Frontier Wardens");

    [Test]
    public void OnlyAWholeOneGuildWarbandOfEightOpensASiege()
    {
        var keep = Keep("rvr-keep-9110");
        var mixed = new AutonomousRvrEventLayer.Force("mixed", eRealm.Albion, 8, 50, 2, GuildName: "Raiders", SingleGuild: false);
        var seven = new AutonomousRvrEventLayer.Force("seven", eRealm.Albion, 7, 50, 2, GuildName: "Raiders");
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(mixed, [keep], 1, 0)?.IsSharedEvent == true, Is.False, "mixed guilds");
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(seven, [keep], 2, 0)?.IsSharedEvent == true, Is.False, "seven members");
        var whole = new AutonomousRvrEventLayer.Force("whole", eRealm.Albion, 8, 50, 2, GuildName: "Raiders");
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(whole, [keep], 3, 0)?.TargetId, Is.EqualTo(keep.Id));
        Assert.That(AutonomousRvrEventLayer.IsWholeGuildWarband(whole), Is.True);
        Assert.That(AutonomousRvrEventLayer.IsWholeGuildWarband(mixed), Is.False);
    }

    [Test]
    public void SmallerForcesJoinOnlyTheirOwnGuildsSiege()
    {
        var keep = Keep("rvr-keep-9111");
        var opener = new AutonomousRvrEventLayer.Force("opener", eRealm.Albion, 8, 50, 2, GuildName: "Raiders");
        Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(opener, [keep], 1, 0)?.TargetId, Is.EqualTo(keep.Id));

        var guildmates = new AutonomousRvrEventLayer.Force("mates", eRealm.Hibernia, 3, 50, 1, GuildName: "Raiders");
        var mixedMates = new AutonomousRvrEventLayer.Force("mixed-mates", eRealm.Albion, 4, 50, 1, GuildName: "Raiders", SingleGuild: false);
        var strangers = new AutonomousRvrEventLayer.Force("strangers", eRealm.Albion, 4, 50, 1, GuildName: "Others");
        var strangerWarband = new AutonomousRvrEventLayer.Force("stranger-8", eRealm.Midgard, 8, 50, 2, GuildName: "Others");
        Assert.Multiple(() =>
        {
            var mates = AutonomousRvrEventLayer.ChooseOrJoin(guildmates, [keep], 2, 0);
            Assert.That(mates?.TargetId, Is.EqualTo(keep.Id), "three guildmates reinforce");
            Assert.That(mates?.Intent, Is.EqualTo(AutonomousRvrEventLayer.Intent.AssaultKeep));
            Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(mixedMates, [keep], 3, 0)?.TargetId, Is.Not.EqualTo(keep.Id),
                "a mixed pickup group is not the opener's guild");
            Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(strangers, [keep], 4, 0)?.TargetId, Is.Not.EqualTo(keep.Id),
                "four strangers do not contest the siege");
            Assert.That(AutonomousRvrEventLayer.ChooseOrJoin(strangerWarband, [keep], 5, 0)?.TargetId, Is.EqualTo(keep.Id),
                "a whole stranger warband may contest it on its own side");
        });
        Assert.That(Bucket(keep.Id, "Attackers").ContainsKey("mates"), Is.True);
        Assert.That(Bucket(keep.Id, "ThirdRealm").ContainsKey("strangers"), Is.False);
        Assert.That(Bucket(keep.Id, "ThirdRealm").ContainsKey("stranger-8"), Is.True);
    }

    [Test]
    public void SingleGuildMeansEveryMemberInTheLeadersGuild()
    {
        var raiders = new object();
        var others = new object();
        Assert.That(AutonomousSiegeDoctrine.IsSingleGuild(raiders, new[] { raiders, raiders, raiders }), Is.True);
        Assert.That(AutonomousSiegeDoctrine.IsSingleGuild(raiders, new[] { raiders, others }), Is.False);
        Assert.That(AutonomousSiegeDoctrine.IsSingleGuild(raiders, new object[] { raiders, null }), Is.False);
        Assert.That(AutonomousSiegeDoctrine.IsSingleGuild<object>(null, new object[] { null, null }), Is.False, "guildless forces cannot claim");
    }
}
