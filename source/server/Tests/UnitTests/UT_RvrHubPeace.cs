using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Wave 6: hub-band peace between same-realm autonomous world bots
/// (owner decision 2026-09-29, "you leave the door first, then you hunt").</summary>
[TestFixture, NonParallelizable]
public sealed class UT_RvrHubPeace
{
    // Svasud Faste keep centre (region 100) and three reference points.
    private const int SvasudX = 766_235, SvasudY = 669_173;
    private static readonly (int X, int Y) InBand = (SvasudX, SvasudY - 4_500);   // 1,000 beyond the safe edge
    private static readonly (int X, int Y) Frontier = (SvasudX, SvasudY - 9_000); // open frontier

    private EpicTestServerScope _server;

    [SetUp]
    public void SetUp()
    {
        _server = new EpicTestServerScope();
        AutonomousHubDeparture.DrainPeaceLine();
        AutonomousPvpEngagementTracker.Drain();
    }

    [TearDown]
    public void TearDown() => _server.Dispose();

    private sealed class PeaceBot : GameBot
    {
        public PeaceBot() : base((OfflineWorldBotRecord)null) { }
        public int PosX, PosY;
        public override int X => PosX;
        public override int Y => PosY;
        public override ushort CurrentRegionID { get; set; }
        public override bool IsAlive => true;
        public override byte Level { get; set; } = 50;
        public override eRealm Realm { get; set; }
    }

    private sealed class PeacePlayer : GamePlayer
    {
        public PeacePlayer() : base(null, null) { }
        public override bool IsAlive => true;
        public override byte Level { get; set; } = 50;
        public override eRealm Realm { get; set; }
        public override GameClient Client { get; } = new BotDummyClient();
        public override bool IsInvulnerableToAttack => false;
        public override ushort CurrentRegionID { get; set; }
        public override bool SafetyFlag { get; set; }
    }

    private static PeaceBot Bot(eRealm realm, (int X, int Y) at, bool autonomous = true, ushort region = 100)
    {
        var bot = (PeaceBot)RuntimeHelpers.GetUninitializedObject(typeof(PeaceBot));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(bot, new ArrayList());
        typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(bot, new BotBrain { Body = bot });
        typeof(GameLiving).GetField("<TempProperties>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(bot, new PropertyCollection());
        typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!.SetValue(bot, autonomous);
        bot.Realm = realm;
        bot.Level = 50;
        bot.CurrentRegionID = region;
        bot.PosX = at.X;
        bot.PosY = at.Y;
        return bot;
    }

    // ---- Pure predicates --------------------------------------------------

    [TestCase(true, true, true, true, ExpectedResult = true, TestName = "Same-realm bots both inside the band are at peace")]
    [TestCase(true, true, true, false, ExpectedResult = true, TestName = "The attacker inside the band is enough")]
    [TestCase(true, true, false, true, ExpectedResult = true, TestName = "The target inside the band is enough")]
    [TestCase(true, true, false, false, ExpectedResult = false, TestName = "Both outside the band may fight")]
    [TestCase(true, false, true, true, ExpectedResult = false, TestName = "Other realms inside the band may fight")]
    [TestCase(false, true, true, true, ExpectedResult = false, TestName = "Humans and companions are unaffected")]
    public bool PeaceRule(bool bothWorldBots, bool sameRealm, bool attackerInBand, bool targetInBand) =>
        AutonomousHubDeparture.HubPeaceApplies(bothWorldBots, sameRealm, attackerInBand, targetInBand);

    [Test]
    public void InjectedBandCirclesAreMeasuredFromTheirCentres()
    {
        var band = new[]
        {
            new AutonomousHubDeparture.SafeAnchor(7, new Vector2(0, 0), 6_000, "Keep"),
            new AutonomousHubDeparture.SafeAnchor(7, new Vector2(20_000, 0), 1_000 + AutonomousHubDeparture.DepartureBand, "Keep"),
        };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousHubDeparture.InHubBand(band, 7, 5_999, 0), Is.True, "keep band edge");
            Assert.That(AutonomousHubDeparture.InHubBand(band, 7, 6_001, 0), Is.False, "just beyond the keep band");
            Assert.That(AutonomousHubDeparture.InHubBand(band, 7, 23_400, 0), Is.True, "landing radius + 2,500");
            Assert.That(AutonomousHubDeparture.InHubBand(band, 7, 23_600, 0), Is.False, "beyond the landing band");
            Assert.That(AutonomousHubDeparture.InHubBand(band, 8, 0, 0), Is.False, "another region");
            Assert.That(AutonomousHubDeparture.InHubBand((AutonomousHubDeparture.SafeAnchor[])null, 7, 0, 0), Is.False);
        });
    }

    [Test]
    public void RealHubBandsUseTheKeepAndTheOuterLandings()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousHubDeparture.InHubBand(eRealm.Midgard, 100, InBand.X, InBand.Y), Is.True);
            Assert.That(AutonomousHubDeparture.InHubBand(eRealm.Midgard, 100, Frontier.X, Frontier.Y), Is.False);
            Assert.That(AutonomousHubDeparture.InHubBand(eRealm.Albion, 100, InBand.X, InBand.Y), Is.False,
                "Svasud is not Albion's hub");
            // Castle Sauvage's bindstone landing lies about 9,100 from the keep:
            // its own band is 1,500 + 2,500 around the landing centre.
            Assert.That(AutonomousHubDeparture.InHubBand(eRealm.Albion, 1, 584_340, 486_620 + 3_900), Is.True);
            Assert.That(AutonomousHubDeparture.InHubBand(eRealm.Albion, 1, 584_340, 486_620 + 4_100), Is.False);
            Assert.That(AutonomousHubDeparture.InHubBand(eRealm.Hibernia, 200, 333_229, 419_539 + 5_900), Is.True);
            Assert.That(AutonomousHubDeparture.HubBandOf(eRealm.None), Is.Empty);
        });
    }

    // ---- Live identities --------------------------------------------------

    [Test]
    public void LivePeaceCoversBotsAndTheirPetsButNotHumansOrOtherRealms()
    {
        var inside = Bot(eRealm.Midgard, InBand);
        var outside = Bot(eRealm.Midgard, Frontier);
        var farAway = Bot(eRealm.Midgard, (Frontier.X, Frontier.Y - 1_000));
        var albion = Bot(eRealm.Albion, InBand);
        var companionLike = Bot(eRealm.Midgard, InBand, autonomous: false);
        var human = new PeacePlayer { Realm = eRealm.Midgard, CurrentRegionID = 100 };

        Assert.Multiple(() =>
        {
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(outside, inside), Is.True, "hunter outside, target inside");
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(inside, outside), Is.True, "attacker inside, retaliation too");
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(outside, farAway), Is.False, "both out on the frontier");
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(albion, inside), Is.False, "other realm");
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(inside, albion), Is.False, "defends against another realm");
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(human, inside), Is.False, "a human may attack");
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(inside, human), Is.False, "a bot defends against a human");
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(companionLike, inside), Is.False, "non-autonomous bot");
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(inside, inside), Is.False, "self");
        });
    }

    [Test]
    public void AttackPermissionConsultsThePeaceAndCountsIt()
    {
        var rules = new PvPServerRules();
        var inside = Bot(eRealm.Midgard, InBand);
        var outside = Bot(eRealm.Midgard, Frontier);
        var farAway = Bot(eRealm.Midgard, (Frontier.X, Frontier.Y - 1_000));
        var albion = Bot(eRealm.Albion, InBand);

        Assert.Multiple(() =>
        {
            Assert.That(rules.IsAllowedToAttack(outside, inside, true), Is.False, "same-realm opener into the band");
            Assert.That(rules.IsAllowedToAttack(outside, farAway, true), Is.True, "frontier fight stays open");
            Assert.That(rules.IsAllowedToAttack(albion, inside, true), Is.True, "cross-realm stays open");
            Assert.That(rules.IsAllowedToAttack(inside, albion, true), Is.True, "and the bot defends");
            Assert.That(PvpCombatant.BlocksAutonomousPvp(inside, outside, countStray: true), Is.True,
                "damage-time guard (DoT, AoE splash in flight), counted in TakeDamage");
            Assert.That(PvpCombatant.BlocksAutonomousPvp(inside, outside), Is.True,
                "OnAttackedByEnemy blocks too but does not count the same hit again");
        });

        string line = AutonomousHubDeparture.DrainPeaceLine();
        Assert.That(line, Is.EqualTo("RVR_HUB_PEACE window_s=300 blocked=1 by_hub=Svasud:1,Sauvage:0,Druim:0 stray=1"));
    }

    [Test]
    public void GroupmatesAreRefusedByTheAllianceCheckNotCountedAsPeace()
    {
        var rules = new PvPServerRules();
        var first = Bot(eRealm.Midgard, InBand);
        var second = Bot(eRealm.Midgard, InBand);
        var group = new Group(first);
        var members = (System.Collections.Generic.List<GameLiving>)typeof(Group)
            .GetField("_groupMembers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(group);
        members.AddRange(new GameLiving[] { first, second });
        first.Group = group;
        second.Group = group;

        Assert.That(rules.IsAllowedToAttack(first, second, true), Is.False);
        Assert.That(AutonomousHubDeparture.DrainPeaceLine(), Does.Contain("blocked=0 "));
    }

    [Test]
    public void PlayerLedAndCompanionBotsAreOutsideThePeace()
    {
        var inside = Bot(eRealm.Midgard, InBand);
        var playerLed = Bot(eRealm.Midgard, InBand);
        typeof(GameBot).GetProperty(nameof(GameBot.PlayerGroupLeader))!
            .SetValue(playerLed, new PeacePlayer { Realm = eRealm.Midgard, CurrentRegionID = 100 });
        var companion = Bot(eRealm.Midgard, InBand);
        typeof(GameBot).GetProperty(nameof(GameBot.PlayerCompanionRecord))!
            .SetValue(companion, new PlayerCompanionRecord { CompanionId = string.Empty });

        Assert.Multiple(() =>
        {
            Assert.That(playerLed.IsPlayerLedGroup, Is.True);
            Assert.That(companion.IsPersistentPlayerCompanion, Is.True);
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(playerLed, inside), Is.False, "player-led attacker");
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(inside, playerLed), Is.False, "player-led target");
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(companion, inside), Is.False, "companion attacker");
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(inside, companion), Is.False, "companion target");
        });
    }

    private sealed class PeacePet : GameNPC
    {
        public override bool IsAlive => true;
    }

    private sealed class PetBrain : ControlledMobBrain
    {
        public PetBrain(GameLiving owner) : base(owner) { }
    }

    private static PeacePet PetOf(GameLiving owner)
    {
        var pet = (PeacePet)RuntimeHelpers.GetUninitializedObject(typeof(PeacePet));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(pet, new ArrayList());
        typeof(GameNPC).GetField("m_ownBrain", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(pet, new PetBrain(owner) { Body = pet });
        return pet;
    }

    [Test]
    public void ABotsPetIsBlockedButAHumansPetIsNot()
    {
        var inside = Bot(eRealm.Midgard, InBand);
        var hunter = Bot(eRealm.Midgard, Frontier);
        var human = new PeacePlayer { Realm = eRealm.Midgard, CurrentRegionID = 100 };

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.Resolve(PetOf(hunter)), Is.SameAs(hunter));
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(PetOf(hunter), inside), Is.True, "bot pet as attacker");
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(hunter, PetOf(inside)), Is.True, "bot pet as target");
            Assert.That(AutonomousHubDeparture.HubPeaceApplies(PetOf(human), inside), Is.False, "human pet");
        });
    }

    [Test]
    public void EngageSummaryCountsNewFightsInsideAHubBand()
    {
        var inside = Bot(eRealm.Midgard, InBand);
        var albion = Bot(eRealm.Albion, InBand);
        var hunter = Bot(eRealm.Albion, Frontier);
        var prey = Bot(eRealm.Midgard, Frontier);

        AutonomousPvpEngagementTracker.ObserveAttack(inside, new AttackData { Attacker = albion, Target = inside });
        AutonomousPvpEngagementTracker.ObserveAttack(prey, new AttackData { Attacker = hunter, Target = prey });
        var summary = AutonomousPvpEngagementTracker.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(summary.Fights, Is.EqualTo(2));
            Assert.That(summary.HubBand, Is.EqualTo(1));
        });
    }
}
