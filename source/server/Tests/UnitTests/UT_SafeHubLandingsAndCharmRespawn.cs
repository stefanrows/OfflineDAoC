using System.Linq;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Bug 65 and Camlann decision 7 option b: generated charm bodies never come
/// back as ordinary respawning mobs, and the teleporter landings outside the
/// Castle Sauvage and Svasud Faste keep radius are safe circles of their own.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_SafeHubLandingsAndCharmRespawn
{
    private EpicTestServerScope _server;

    [SetUp]
    public void SetUp() => _server = new EpicTestServerScope();

    [TearDown]
    public void TearDown() => _server.Dispose();

    // ---- Landing circles -------------------------------------------------

    [TestCase((ushort)1, 583913, 487012, TestName = "Castle Sauvage teleporter landing is safe")]
    [TestCase((ushort)1, 584770, 486230, TestName = "Castle Sauvage east bindstone is safe")]
    [TestCase((ushort)1, 584638, 486320, TestName = "Castle Sauvage west bindstone is safe")]
    [TestCase((ushort)1, 584770 + 750, 486230, TestName = "Castle Sauvage bindstone radius edge is safe")]
    [TestCase((ushort)100, 765694, 673509, TestName = "Svasud Faste teleporter landing is safe")]
    [TestCase((ushort)100, 764082, 672416, TestName = "Svasud Faste outer bindstone is safe")]
    [TestCase((ushort)100, 764082 - 750, 672416, TestName = "Svasud Faste bindstone radius edge is safe")]
    public void LandingAndItsBindstonesAreInsideTheLandingCircle(ushort region, int x, int y) =>
        Assert.That(PvpCombatant.IsSafeHubLanding(region, x, y), Is.True);

    [TestCase((ushort)1, 584340 + 1_600, 486620, TestName = "Castle Sauvage beyond the landing radius is open")]
    [TestCase((ushort)1, 584340, 486620 + 1_600, TestName = "Castle Sauvage north of the landing is open")]
    [TestCase((ushort)100, 764890 - 1_900, 672960, TestName = "Svasud Faste beyond the landing radius is open")]
    [TestCase((ushort)200, 584340, 486620, TestName = "Same numbers in Hibernia are not a landing")]
    [TestCase((ushort)100, 584340, 486620, TestName = "Same numbers in Midgard are not a landing")]
    public void OutsideTheLandingCircleIsNotALanding(ushort region, int x, int y) =>
        Assert.That(PvpCombatant.IsSafeHubLanding(region, x, y), Is.False);

    [Test]
    public void RoadBetweenCastleSauvageLandingAndKeepStaysOpenPvp()
    {
        // The landing sits about 9,000 units north of the keep centre; the
        // road between both circles is ordinary frontier.
        for (int y = 482_000; y >= 481_500; y -= 250)
        {
            Assert.Multiple(() =>
            {
                Assert.That(PvpCombatant.IsSafeBorderHub(1, 584_700, y), Is.False, $"hub at y={y}");
                Assert.That(PvpCombatant.IsSafeHubLanding(1, 584_700, y), Is.False, $"landing at y={y}");
            });
        }
    }

    [Test]
    public void DruimLigenNeedsNoLandingCircle()
    {
        // Its teleporter destination and its only bindstone lie in the hub.
        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.IsSafeBorderHub(200, 334342, 419994), Is.True, "teleporter destination");
            Assert.That(PvpCombatant.IsSafeBorderHub(200, 333220, 420510), Is.True, "bindstone");
            Assert.That(PvpCombatant.SafeHubLandings, Has.None.Matches<PvpCombatant.SafeHubLanding>(l => l.RegionId == 200));
        });
    }

    [Test]
    public void LandingCircleProtectsHumansBothWaysButNotTheRoad()
    {
        var rules = new PvPServerRules();
        var landed = new LandingPlayer { Realm = eRealm.Midgard, CurrentRegionID = 1, X = 583913, Y = 487012 };
        var edge = new LandingPlayer { Realm = eRealm.Albion, CurrentRegionID = 1, X = 584340, Y = 486620 - 1_600 };
        var road = new LandingPlayer { Realm = eRealm.Hibernia, CurrentRegionID = 1, X = 584700, Y = 482000 };

        Assert.Multiple(() =>
        {
            Assert.That(PvpCombatant.IsSafeArea(landed), Is.True);
            Assert.That(PvpCombatant.IsSafeArea(road), Is.False);
            Assert.That(rules.IsAllowedToAttack(edge, landed, true), Is.False, "no attacks into the landing");
            Assert.That(rules.IsAllowedToAttack(landed, edge, true), Is.False, "no attacks out of the landing");
            Assert.That(rules.IsAllowedToAttack(edge, road, true), Is.True, "the road stays open");
        });
    }

    private sealed class LandingPlayer : GamePlayer
    {
        public LandingPlayer() : base(null, null) { }
        public override bool IsAlive => true;
        public override byte Level { get; set; } = 50;
        public override eRealm Realm { get; set; }
        public override GameClient Client { get; } = new BotDummyClient();
        public override bool IsInvulnerableToAttack => false;
        public override ushort CurrentRegionID { get; set; }
        public override bool SafetyFlag { get; set; }
    }

    // ---- Generated charm bodies ------------------------------------------

    [Test]
    public void GeneratedCharmBodyLosesItsTemplateRespawn()
    {
        var body = new GameNPC { RespawnInterval = 80_000 };
        AutonomousPetSupport.MarkSyntheticCharmBody(body);

        Assert.Multiple(() =>
        {
            Assert.That(body.RespawnInterval, Is.LessThanOrEqualTo(0));
            Assert.That(AutonomousPetSupport.IsSyntheticCharm(body), Is.True);
        });

        body.StartRespawn();
        Assert.That(body.IsRespawning, Is.False, "a dead generated pet must not come back as a wild template mob");
    }

    [Test]
    public void GeneratedCharmBodyIsStillRecognisedAfterDeathWipesItsProperties()
    {
        var body = new GameNPC();
        AutonomousPetSupport.MarkSyntheticCharmBody(body);

        // GameNPC death clears TempProperties before the charm effect's
        // deferred stop asks whether this was a generated pet.
        body.TempProperties.RemoveAllProperties();

        Assert.That(AutonomousPetSupport.IsSyntheticCharm(body), Is.True);
    }

    [Test]
    public void CharmStopAfterDeathWipeStillDeletesTheGeneratedBody()
    {
        var caster = new GameNPC { Name = "caster" };
        var body = new GameNPC { Name = "phantom magi" };
        AutonomousPetSupport.MarkSyntheticCharmBody(body);
        // Death: health gone, TempProperties wiped before the deferred stop.
        body.TempProperties.RemoveAllProperties();

        var spell = new Spell(new DbSpell
        {
            Name = "Bound Sorcerer Companion", Type = eSpellType.Charm.ToString(), Target = "Enemy",
            Damage = 100, Value = 50, Duration = ushort.MaxValue,
        }, 7);
        var handler = new CharmSpellHandler(caster, spell, new SpellLine("test", "test", string.Empty, true));
        var effect = new CharmECSGameEffect(new ECSGameEffectInitParams(body, 0, 1, handler));

        effect.OnStopEffect();

        ECSGameTimer cleanup = ServiceObjectStore
            .UpdateAndGetAll<ECSGameTimer>(ServiceObjectType.Timer, out int last)
            .Take(last + 1)
            .SingleOrDefault(timer => timer?.Owner == body);
        Assert.That(cleanup, Is.Not.Null, "the stop schedules the generated-body cleanup");
        cleanup.Tick();

        Assert.That(body.ObjectState, Is.EqualTo(GameObject.eObjectState.Deleted));
    }

    [Test]
    public void OrdinaryMobIsNotAGeneratedCharmBody()
    {
        var mob = new GameNPC { RespawnInterval = 80_000 };
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousPetSupport.IsSyntheticCharm(mob), Is.False);
            Assert.That(AutonomousPetSupport.IsSyntheticCharm(null), Is.False);
            Assert.That(mob.RespawnInterval, Is.EqualTo(80_000));
        });
    }

    [Test]
    public void OrdinaryMobStillRespawns()
    {
        var mob = new GameNPC { RespawnInterval = 80_000 };
        mob.StartRespawn();
        try
        {
            Assert.That(mob.IsRespawning, Is.True, "world spawns keep their normal respawn");
        }
        finally
        {
            mob.Delete();
        }
    }
}
