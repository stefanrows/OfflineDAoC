using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_CompanionBombingPolicy
{
    private static readonly System.Type WaitType = typeof(GameLiving).Assembly.GetType(
        "DOL.GS.CompanionBombTankWait", true)!;

    [Test]
    public void TankAggroWaitRestartsForANewFocusedTarget()
    {
        object first = new();
        object second = new();
        object wait = System.Activator.CreateInstance(WaitType, nonPublic: true)!;
        System.Reflection.MethodInfo shouldWait = WaitType.GetMethod("ShouldWait")!;

        Assert.That(ShouldWait(wait, shouldWait, first, 1_000), Is.True);
        Assert.That(ShouldWait(wait, shouldWait, first, 3_499), Is.True);
        Assert.That(ShouldWait(wait, shouldWait, second, 3_499), Is.True,
            "Switching focus starts a fresh grace period for the new pull target.");
        Assert.That(ShouldWait(wait, shouldWait, second, 5_998), Is.True);
        Assert.That(ShouldWait(wait, shouldWait, second, 5_999), Is.False);
        Assert.That(ShouldWait(wait, shouldWait, second, 7_000), Is.False,
            "An expired wait stays expired while the same target remains focused.");
    }

    [Test]
    public void AddsFightingTheGroupCountTowardTheBombButIdleSpawnsDoNot()
    {
        // Healers target party members and damage dealers assist one target,
        // so the per-member focus set rarely reached the three-target bomb.
        using var server = new EpicTestServerScope();
        Member[] members = Enumerable.Range(0, 3).Select(_ => NewMember()).ToArray();
        var group = new Group(members[0]);
        typeof(Group).GetField("_groupMembers", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(group, members.Cast<GameLiving>().ToList());
        foreach (Member member in members) member.Group = group;
        Mob onTank = NewMob(members[0]);
        Mob onHealer = NewMob(members[2]);
        Mob idle = NewMob(null);
        Mob dead = NewMob(members[1]); dead.Alive = false;
        Mob elsewhere = NewMob(NewMember());

        GameNPC[] engaged = CompanionAddControl.EngagedWithGroup(group, [onTank, onHealer, idle, dead, elsewhere]).ToArray();

        Assert.That(engaged, Is.EquivalentTo(new GameNPC[] { onTank, onHealer }));
    }

    private sealed class Member : GameBot
    {
        private Member() : base((OfflineWorldBotRecord)null) { }
        public override bool IsAlive => true;
    }

    private sealed class Mob : GameNPC
    {
        public bool Alive = true;
        public GameObject Victim;
        public override bool IsAlive => Alive;
        public override GameObject TargetObject { get => Victim; set => Victim = value; }
    }

    private static Member NewMember()
    {
        var member = (Member)RuntimeHelpers.GetUninitializedObject(typeof(Member));
        typeof(GameNPC).GetField("m_brains", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(member, new ArrayList());
        return member;
    }

    private static Mob NewMob(GameLiving victim)
    {
        var mob = (Mob)RuntimeHelpers.GetUninitializedObject(typeof(Mob));
        mob.Alive = true;
        mob.Victim = victim;
        mob.ObjectState = GameObject.eObjectState.Active;
        return mob;
    }

    private static bool ShouldWait(object instance, System.Reflection.MethodInfo method, object target, long now) =>
        (bool)method.Invoke(instance, [target, now])!;
}
