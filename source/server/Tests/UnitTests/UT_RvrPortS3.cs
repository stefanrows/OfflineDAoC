using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// Port session S3 (docs/RVR_PORT_MAP.md "S3 done"): the RvR world (Realm-1) takes upstream 0.38c's periphery
/// values while Camlann keeps the fork's: 8 enemies on a relic pad instead of 16, route threat awareness on,
/// and the upstream raid rally timing for forced raids.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_RvrPortS3
{
    private const long Minute = 60_000;

    [Test]
    public void TheRvrWorldNeedsEightOnARelicPadAndCamlannKeepsItsProperty()
    {
        int saved = Properties.RELIC_PLAYERS_REQUIRED_ON_PAD;
        try
        {
            Properties.RELIC_PLAYERS_REQUIRED_ON_PAD = 16;
            Assert.Multiple(() =>
            {
                Assert.That(RvrRelicRules.RvrPlayersRequiredOnPad, Is.EqualTo(8), "upstream 0.38c default");
                Assert.That(RvrRelicRules.PlayersRequiredOnPad(WorldModelKind.Realm), Is.EqualTo(8));
                Assert.That(RvrRelicRules.PlayersRequiredOnPad(WorldModelKind.Camlann), Is.EqualTo(16));
            });
            using (WorldMode.OverrideForTests(WorldModelKind.Realm))
                Assert.That(RvrRelicRules.PlayersRequiredOnPad(), Is.EqualTo(8));
            using (WorldMode.OverrideForTests(WorldModelKind.Camlann))
                Assert.That(RvrRelicRules.PlayersRequiredOnPad(), Is.EqualTo(16));
        }
        finally { Properties.RELIC_PLAYERS_REQUIRED_ON_PAD = saved; }
    }

    [Test]
    public void RouteThreatAwarenessIsOnInTheRvrWorldAndOffInCamlann()
    {
        bool camlann = AutonomousRouteThreatPolicy.CamlannEnabled, rvr = AutonomousRouteThreatPolicy.RvrEnabled;
        try
        {
            AutonomousRouteThreatPolicy.CamlannEnabled = false;
            AutonomousRouteThreatPolicy.RvrEnabled = true;
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRouteThreatPolicy.IsEnabled(WorldModelKind.Realm), Is.True, "upstream 0.38c default on");
                Assert.That(AutonomousRouteThreatPolicy.IsEnabled(WorldModelKind.Camlann), Is.False, "Camlann waits for its comparison");
            });
            using (WorldMode.OverrideForTests(WorldModelKind.Realm))
                Assert.That(AutonomousRouteThreatPolicy.Enabled, Is.True);
            using (WorldMode.OverrideForTests(WorldModelKind.Camlann))
                Assert.That(AutonomousRouteThreatPolicy.Enabled, Is.False);
            AutonomousRouteThreatPolicy.RvrEnabled = false;
            using (WorldMode.OverrideForTests(WorldModelKind.Realm))
                Assert.That(AutonomousRouteThreatPolicy.Enabled, Is.False, "the RvR key can still switch it off");
        }
        finally
        {
            AutonomousRouteThreatPolicy.CamlannEnabled = camlann;
            AutonomousRouteThreatPolicy.RvrEnabled = rvr;
        }
    }

    [Test]
    public void ForcedRaidsInTheRvrWorldGoInAsSoonAsTwoHundredAreStaged()
    {
        using (WorldMode.OverrideForTests(WorldModelKind.Realm))
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRecruitmentPolicy.Ready(true, 1 * Minute, 200, true), Is.True, "no 45-minute preparation");
                Assert.That(RealmRaidRecruitmentPolicy.Ready(true, 1 * Minute, 199, true), Is.False);
                Assert.That(RealmRaidRecruitmentPolicy.Ready(true, 1 * Minute, 200, false), Is.False, "a dragon must still land");
                Assert.That(RealmRaidRecruitmentPolicy.Ready(false, 75 * Minute, 180, true), Is.True, "late start with 180 after 75 minutes");
                Assert.That(RealmRaidRecruitmentPolicy.Ready(false, 74 * Minute, 180, true), Is.False);
                Assert.That(RealmRaidRecruitmentPolicy.StagingExpired(true, 300 * Minute), Is.False, "a forced raid never expires");
                Assert.That(RealmRaidRecruitmentPolicy.StagingExpired(false, 90 * Minute), Is.True);
            });
        }
        using (WorldMode.OverrideForTests(WorldModelKind.Camlann))
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRecruitmentPolicy.Ready(true, 1 * Minute, 200, true), Is.False, "Camlann keeps the 45-minute preparation");
                Assert.That(RealmRaidRecruitmentPolicy.Ready(true, 45 * Minute, 200, true), Is.True);
                Assert.That(RealmRaidRecruitmentPolicy.Ready(false, 75 * Minute, 180, true), Is.False, "no late start in Camlann");
                Assert.That(RealmRaidRecruitmentPolicy.StagingExpired(true, 90 * Minute), Is.True);
            });
        }
    }

    [Test]
    public void RealmRaidsRunPerRealmAndOnTheirOwnOnlyInTheRvrWorld()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRealmRaid.RaidsByRealm(WorldModelKind.Realm), Is.True, "upstream: each realm raids its own encounters");
            Assert.That(AutonomousRealmRaid.RaidsByRealm(WorldModelKind.Camlann), Is.False, "Camlann keeps mixed-realm PvE expeditions");
            Assert.That(RealmRaidRecruitmentPolicy.AutonomousRaidsRun(WorldModelKind.Realm), Is.True, "upstream: level-50 parties open raids");
            Assert.That(RealmRaidRecruitmentPolicy.AutonomousRaidsRun(WorldModelKind.Camlann), Is.False);
            Assert.That(RealmRaidRecruitmentPolicy.ScheduledRaidsEnabled, Is.False, "the fork's raid calendar stays silent in both worlds");
            Assert.That(AutonomousRealmRaid.MayStartAutomatic("epic-albion", "epic-albion"), Is.False, "never the same automatic event twice in a row");
            Assert.That(AutonomousRealmRaid.MayStartAutomatic("dragon-albion", "epic-albion"), Is.True);
            Assert.That(AutonomousRealmRaid.MayStartAutomatic("dragon-albion", null), Is.True);
            Assert.That(AutonomousRealmRaid.ForcedStartCondition(true), Does.Not.Contain("land"), "dungeons have no landing to wait for");
            Assert.That(AutonomousRealmRaid.ForcedStartCondition(false), Does.Contain("dragon must land"));
        });
        using (WorldMode.OverrideForTests(WorldModelKind.Realm))
            Assert.That(RealmRaidRecruitmentPolicy.AutonomousRaidsEnabled, Is.True);
        using (WorldMode.OverrideForTests(WorldModelKind.Camlann))
            Assert.That(RealmRaidRecruitmentPolicy.AutonomousRaidsEnabled, Is.False);
    }

    [Test]
    public void TheBracketCampaignAndTownTeleportersFollowUpstreamInTheRvrWorld()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBattlegroundParticipation.BracketCampaignRuns(WorldModelKind.Realm), Is.False,
                "upstream sends no bot to a bracket battleground by default (Battlegrounds 0%)");
            Assert.That(AutonomousBattlegroundParticipation.BracketCampaignRuns(WorldModelKind.Camlann), Is.True);
        });
        bool saved = AutonomousTownTeleporters.Enabled;
        try
        {
            AutonomousTownTeleporters.Enabled = true;
            using (WorldMode.OverrideForTests(WorldModelKind.Realm))
                Assert.That(AutonomousTownTeleporters.IsEnabled, Is.True, "upstream default on (bot_use_town_teleporters)");
            using (WorldMode.OverrideForTests(WorldModelKind.Camlann))
                Assert.That(AutonomousTownTeleporters.IsEnabled, Is.False, "Camlann keeps its own porter use");
            AutonomousTownTeleporters.Enabled = false;
            using (WorldMode.OverrideForTests(WorldModelKind.Realm))
                Assert.That(AutonomousTownTeleporters.IsEnabled, Is.EqualTo(System.Environment.GetEnvironmentVariable(AutonomousTownTeleporters.OverrideVariable) == "1"));
        }
        finally { AutonomousTownTeleporters.Enabled = saved; }
    }

    [Test]
    public void TownTeleporterRouteLengthsAndBattlegroundBracketsAreUpstreams()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousTownTeleporters.RouteLength(new(0, 0, 0), [new(300, 400, 0)], new(300, 1400, 0)), Is.EqualTo(1500).Within(0.01));
            Assert.That(BattlegroundBrackets.ForLevel(14), Is.Null);
            Assert.That(BattlegroundBrackets.ForLevel(22)?.RegionId, Is.EqualTo((ushort)252), "Thidranki is the 20-24 bracket");
            Assert.That(BattlegroundBrackets.ForLevel(36), Is.Null);
            Assert.That(BattlegroundBrackets.IsBattlegroundRegion(251), Is.True);
        });
    }
}
