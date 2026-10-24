using System;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture, NonParallelizable]
public sealed class UT_WorldMode
{
    private EpicTestServerScope _server;

    [SetUp]
    public void SetUp() => _server = new EpicTestServerScope();

    [TearDown]
    public void TearDown() => _server.Dispose();

    private sealed class TestPlayer : GamePlayer
    {
        public TestPlayer() : base(null, null) { }

        public override eRealm Realm { get; set; }
        public override GameClient Client { get; } = new BotDummyClient();
    }

    [Test]
    public void ProcessesThatNeverReadAMarkerStayCamlann()
    {
        // No test or tool start sets the world; Camlann behaviour is the default.
        Assert.That(WorldMode.Current, Is.EqualTo(WorldModelKind.Camlann));
        Assert.That(WorldMode.IsCamlann, Is.True);
        Assert.That(WorldMode.IsRvr, Is.False);
    }

    [Test]
    public void TestOverrideSetsAndRestoresTheWorld()
    {
        WorldModelKind before = WorldMode.Current;
        using (WorldMode.OverrideForTests(WorldModelKind.Realm))
        {
            Assert.That(WorldMode.IsRvr, Is.True);
            Assert.That(WorldMode.IsCamlann, Is.False);
        }

        Assert.That(WorldMode.Current, Is.EqualTo(before));
    }

    [Test]
    public void StartupFixesTheWorldForTheProcess()
    {
        using var _ = WorldMode.OverrideForTests(WorldModelKind.Camlann);

        WorldMode.Initialize(WorldModelKind.Realm);
        Assert.That(WorldMode.IsRvr, Is.True);

        Assert.DoesNotThrow(() => WorldMode.Initialize(WorldModelKind.Realm));
        Assert.Throws<InvalidOperationException>(() => WorldMode.Initialize(WorldModelKind.Camlann));
        Assert.That(WorldMode.IsRvr, Is.True);
    }

    [Test]
    public void UnsupportedWorldNeverStarts()
    {
        using var _ = WorldMode.OverrideForTests(WorldModelKind.Camlann);

        Assert.Throws<ArgumentOutOfRangeException>(() => WorldMode.Initialize(WorldModelKind.Unsupported));
        Assert.That(WorldMode.IsCamlann, Is.True);
    }

    [TestCase(WorldModelKind.Camlann, EGameServerType.GST_PvP, true)]
    [TestCase(WorldModelKind.Realm, EGameServerType.GST_PvP, false)]
    [TestCase(WorldModelKind.Camlann, EGameServerType.GST_Normal, false)]
    [TestCase(WorldModelKind.Realm, EGameServerType.GST_Normal, false)]
    public void DummyGuildTrickRunsOnlyInTheCamlannPvPWorld(WorldModelKind world, EGameServerType serverType, bool expected)
    {
        Assert.That(WorldMode.UsesGuildIdMask(world, serverType), Is.EqualTo(expected));

        using var _ = WorldMode.OverrideForTests(world);
        Assert.That(WorldMode.UsesGuildIdMask(serverType), Is.EqualTo(expected));
    }

    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void CrossRealmPairsAreOnlyRefusedInTheRvrWorld(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        var albion = Player(eRealm.Albion);
        var albion2 = Player(eRealm.Albion);
        var midgard = Player(eRealm.Midgard);

        Assert.Multiple(() =>
        {
            Assert.That(WorldMode.AllowsRealmPair(albion, albion2), Is.True);
            Assert.That(WorldMode.AllowsRealmPair(albion, midgard), Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(WorldMode.AllowsRealmPair(albion, null), Is.False);
        });
    }

    [Test]
    public void StaffAccountsMayPairAcrossRealmsInTheRvrWorld()
    {
        var gm = Player(eRealm.Albion);
        gm.Client.Account.PrivLevel = (uint)ePrivLevel.GM;
        var midgard = Player(eRealm.Midgard);

        Assert.That(WorldMode.AllowsRealmPair(WorldModelKind.Realm, gm, midgard), Is.True);
        Assert.That(WorldMode.AllowsRealmPair(WorldModelKind.Realm, midgard, gm), Is.True);
    }

    private static TestPlayer Player(eRealm realm)
    {
        var player = new TestPlayer();
        player.Realm = realm;
        return player;
    }
}
