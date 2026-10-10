using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Bug 75: keep assaults stalled during travel and siege placement. A siege
/// column now marches once its leader, ram carriers and a quorum are together;
/// a dead or cut-off RvR group leader hands the group lead over; a guild army
/// that has gathered five minutes settles for a keep-level troop floor.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_SiegeColumnQuorum
{
    // ---------------------------------------------------------------- fix 1

    [Test]
    public void SixOfEightTogetherWithTheLeaderAndCarriersMarch()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrSpeed.SiegeQuorumMarch(true, 6, 8, 6, true), Is.True);
            Assert.That(AutonomousRvrSpeed.SiegeQuorumMarch(true, 6, 8, 5, true), Is.False, "five of eight hold");
            Assert.That(AutonomousRvrSpeed.SiegeQuorumMarch(true, 6, 8, 7, false), Is.False,
                "a ram carrier in another region holds the column");
        });
    }

    [Test]
    public void AGroupNoLargerThanTheQuorumStillNeedsEveryone()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrSpeed.SiegeQuorumMarch(true, 6, 4, 3, true), Is.False);
            Assert.That(AutonomousRvrSpeed.SiegeQuorumMarch(true, 6, 4, 4, true), Is.True);
            Assert.That(AutonomousRvrSpeed.SiegeQuorumMarch(true, 1, 8, 1, true), Is.False, "quorum clamps to at least two");
            Assert.That(AutonomousRvrSpeed.SiegeQuorumMarch(true, 20, 8, 8, true), Is.True, "quorum clamps to at most eight");
            Assert.That(AutonomousRvrSpeed.SiegeQuorumMarch(true, 2, 8, 4, true), Is.False, "never below a strict majority");
            Assert.That(AutonomousRvrSpeed.SiegeQuorumMarch(true, 2, 8, 5, true), Is.True, "a strict majority marches");
        });
    }

    [Test]
    public void WithTheQuorumOffOneCrossRegionStragglerHoldsThenFailsAtTwoMinutes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrSpeed.SiegeQuorumMarch(false, 6, 8, 7, true), Is.False);
            Assert.That(AutonomousRvrSpeed.SiegeCohesion(false, float.PositiveInfinity, 0, 0),
                Is.EqualTo(AutonomousRvrSpeed.SiegeCohesionDecision.Hold));
            Assert.That(AutonomousRvrSpeed.SiegeCohesion(true, float.PositiveInfinity, 119_999, 119_999),
                Is.EqualTo(AutonomousRvrSpeed.SiegeCohesionDecision.Hold));
            Assert.That(AutonomousRvrSpeed.SiegeCohesion(true, float.PositiveInfinity, 120_000, 120_000),
                Is.EqualTo(AutonomousRvrSpeed.SiegeCohesionDecision.Fail));
        });
    }

    [TestCase(nameof(AutonomousSiegeProperties.SIEGE_COLUMN_QUORUM_MARCH), "siege_column_quorum_march", true)]
    [TestCase(nameof(AutonomousSiegeProperties.SIEGE_COLUMN_QUORUM), "siege_column_quorum", 6)]
    [TestCase(nameof(AutonomousSiegeProperties.SIEGE_ARMY_KEEP_LEVEL_QUORUM), "siege_army_keep_level_quorum", true)]
    public void SiegePropertiesDefaultOn(string field, string key, object expected)
    {
        FieldInfo info = typeof(AutonomousSiegeProperties).GetField(field, BindingFlags.Public | BindingFlags.Static);
        var attribute = info.GetCustomAttribute<ServerPropertyAttribute>();
        Assert.Multiple(() =>
        {
            Assert.That(attribute.Category, Is.EqualTo("pvp"));
            Assert.That(attribute.Key, Is.EqualTo(key));
            Assert.That(attribute.DefaultValue, Is.EqualTo(expected));
            Assert.That(info.GetValue(null), Is.EqualTo(expected), "the field starts at the same default");
        });
    }

    // ---------------------------------------------------------------- fix 2

    [Test]
    public void ADeadReleasedOrRemoteGroupLeaderHandsTheRvrLeadOver()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGroupCoordinator.RvrLeaderHandoverReason(true, false, false, false, false), Is.EqualTo("dead"));
            Assert.That(AutonomousBotGroupCoordinator.RvrLeaderHandoverReason(true, false, true, true, true), Is.EqualTo("released"));
            Assert.That(AutonomousBotGroupCoordinator.RvrLeaderHandoverReason(true, false, true, false, false), Is.EqualTo("region"));
            Assert.That(AutonomousBotGroupCoordinator.RvrLeaderHandoverReason(true, false, true, false, true), Is.Null,
                "a living leader beside the column keeps the lead");
            Assert.That(AutonomousBotGroupCoordinator.RvrLeaderHandoverReason(false, false, false, false, false), Is.Null,
                "PvE sessions keep today's rule");
        });
    }

    [Test]
    public void AReturningOldLeaderDoesNotGetTheLeadBack()
    {
        // After the handover the coordinator's leader is the group leader; the
        // old leader, released and walking back or alive again elsewhere, is a member.
        Assert.That(AutonomousBotGroupCoordinator.RvrLeaderHandoverReason(true, true, true, true, false), Is.Null);
        Assert.That(AutonomousBotGroupCoordinator.RvrLeaderHandoverReason(true, true, true, false, true), Is.Null);
    }

    [Test]
    public void ALeaderCutOffFromTheMajorityOfItsColumnIsFound()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGroupCoordinator.SplitLeaderMajorityRegion(163, false, [51, 51, 51, 51, 51, 51, 163]),
                Is.EqualTo((ushort?)51));
            Assert.That(AutonomousBotGroupCoordinator.SplitLeaderMajorityRegion(163, true, [51, 51, 51, 51, 51, 51, 163]),
                Is.Null, "a leader riding a stable-master route is travelling, not lost");
            Assert.That(AutonomousBotGroupCoordinator.SplitLeaderMajorityRegion(163, false, [51, 51, 163, 163, 1]),
                Is.Null, "no strict majority");
            Assert.That(AutonomousBotGroupCoordinator.SplitLeaderMajorityRegion(163, false, [163, 163, 163, 51]),
                Is.Null, "the leader stands with the majority");
            Assert.That(AutonomousBotGroupCoordinator.SplitLeaderMajorityRegion(163, false, [51]),
                Is.Null, "one of two is no majority");
            Assert.That(AutonomousBotGroupCoordinator.SplitLeaderMajorityRegion(163, false, []), Is.Null);
        });
    }

    [Test]
    public void TheGuildArmyAcceptsTheHandedOverLeader()
    {
        using var server = new EpicTestServerScope();
        var old = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        var promoted = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
        old.Name = "Fergeoran";
        promoted.Name = "Fergaegus";
        var group = new QuietGroup(old);
        var members = (List<GameLiving>)typeof(Group)
            .GetField("_groupMembers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(group);
        members.AddRange([old, promoted]);
        old.Group = promoted.Group = group;
        old.GroupIndex = 0;
        promoted.GroupIndex = 1;

        Assert.That(AutonomousRvrEventLayer.LeadsItsParty(promoted), Is.False, "before: the dead leader's party was never counted");
        Assert.That(group.MakeLeader(promoted), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrEventLayer.LeadsItsParty(promoted), Is.True);
            Assert.That(AutonomousRvrEventLayer.LeadsItsParty(old), Is.False);
            Assert.That(AutonomousRvrEventLayer.LeadsItsParty(null), Is.False);
        });
    }

    private sealed class QuietGroup(GameLiving leader) : Group(leader)
    {
        public override void SendMessageToGroupMembers(string msg, eChatType type, eChatLoc loc) { }
    }

    // ---------------------------------------------------------------- fix 3

    [TestCase(0, 6)]
    [TestCase(1, 6)]
    [TestCase(3, 6)]
    [TestCase(4, 12)]
    [TestCase(5, 12)]
    [TestCase(6, 12)]
    [TestCase(7, 16)]
    [TestCase(10, 16)]
    public void TheRelaxedTroopFloorScalesWithKeepLevel(int keepLevel, int expected) =>
        Assert.That(AutonomousGuildAssault.RelaxedRequiredAttackers(keepLevel, 0), Is.EqualTo(expected));

    [Test]
    public void SightedDefendersAreNeverRelaxedAndRelaxingNeverAsksForMore()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousGuildAssault.RelaxedRequiredAttackers(1, 10), Is.EqualTo(15));
            Assert.That(AutonomousGuildAssault.RelaxedRequired(11, 1, 0), Is.EqualTo(6));
            Assert.That(AutonomousGuildAssault.RelaxedRequired(12, 10, 0), Is.EqualTo(12), "never above the full requirement");
        });
    }

    [Test]
    public void TheUnrelaxedFormulaIsUnchanged()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousGuildAssault.RequiredAttackers(0, 28, 0), Is.EqualTo(8));
            Assert.That(AutonomousGuildAssault.RequiredAttackers(0, 28, 2), Is.EqualTo(11));
            Assert.That(AutonomousGuildAssault.RequiredAttackers(4, 30, 2, 2), Is.EqualTo(18));
            Assert.That(AutonomousGuildAssault.Ready(1, 8, 1, 1, 8, true, AutonomousGuildAssault.GatherMilliseconds), Is.True);
            Assert.That(AutonomousGuildAssault.Ready(1, 8, 1, 1, 8, true, AutonomousGuildAssault.GatherMilliseconds, 2), Is.False);
        });
    }

    [Test]
    public void OneSevenManPartyTakesOnALevelOneKeepOnlyAfterTheRelaxDelay()
    {
        const long fourMinutes = 4 * 60_000, fiveMinutes = AutonomousGuildAssault.RelaxAfterMilliseconds;
        int full = AutonomousGuildAssault.RequiredAttackers(0, 28, 2);
        bool relaxedEarly = AutonomousGuildAssault.Relaxes(true, fourMinutes);
        bool relaxedLate = AutonomousGuildAssault.Relaxes(true, fiveMinutes);
        int late = relaxedLate ? AutonomousGuildAssault.RelaxedRequired(full, 1, 0) : full;
        Assert.Multiple(() =>
        {
            Assert.That(relaxedEarly, Is.False);
            Assert.That(relaxedLate, Is.True);
            Assert.That(AutonomousGuildAssault.Relaxes(false, 10 * 60_000), Is.False, "property off: never relaxed");
            Assert.That(AutonomousGuildAssault.Ready(1, 7, 1, 1, full, true, fourMinutes, 1, relaxedEarly, 1), Is.False);
            Assert.That(AutonomousGuildAssault.Ready(1, 7, 1, 1, late, true, fiveMinutes, 1, relaxedLate, 1), Is.True);
            Assert.That(AutonomousGuildAssault.Ready(1, 7, 1, 1, late, true, fiveMinutes, 3, true, 1), Is.True,
                "a relaxed army no longer waits for its planned third party at a level 1 keep");
            Assert.That(AutonomousGuildAssault.Ready(1, 7, 1, 1, late, true, fiveMinutes, 3, false, 1), Is.False);
            Assert.That(AutonomousGuildAssault.Ready(1, 7, 1, 0, late, true, fiveMinutes, 1, true, 1), Is.False,
                "closed doors still need an equipped ram operator");
            Assert.That(AutonomousGuildAssault.Ready(1, 7, 0, 1, late, true, fiveMinutes, 1, true, 1), Is.False,
                "and a healer");
        });
    }
}
