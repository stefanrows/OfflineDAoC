using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>Bug 131: group cohesion must not throw when the leader has just
/// left its group. The leader's Group is null, and the cohesion wait table
/// rejects a null key.</summary>
[TestFixture]
public sealed class UT_AutonomousBotGroupCohesion
{
    private sealed class Walker : GameBot
    {
        private Walker() : base((OfflineWorldBotRecord)null) { }
        public override eRealm Realm { get; set; }
        public override bool IsAlive => true;
    }

    [Test]
    public void ALeaderThatLeftItsGroupIsCohesiveInsteadOfThrowing()
    {
        var leader = (Walker)RuntimeHelpers.GetUninitializedObject(typeof(Walker));
        Assert.That(leader.Group, Is.Null);
        var directive = new AutonomousBotGroupCoordinator.Directive("group-131", "Assembling", "goal", "status",
            eAutonomousObjectiveKind.RvR, leader, Vector3.Zero, null, 1, 50, 0, 0, true);
        Assert.That(() => AutonomousBotGroupCoordinator.IsCohesive(directive), Throws.Nothing);
        Assert.That(AutonomousBotGroupCoordinator.IsCohesive(directive), Is.True);
    }
}
