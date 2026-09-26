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

    private static readonly System.Type VolleyType = typeof(GameLiving).Assembly.GetType(
        "DOL.GS.CompanionBombVolley", true)!;

    [Test]
    public void FirstBomberWaitsBrieflyForTheOthersThenTheGroupChainsFreely()
    {
        object a = new(), b = new(), c = new();
        object[] bombers = [a, b, c];
        object volley = System.Activator.CreateInstance(VolleyType, nonPublic: true)!;
        MethodInfo hold = VolleyType.GetMethod("ShouldHold")!;
        bool Hold(object bomber, long now) => (bool)hold.Invoke(volley, [bomber, bombers, now])!;

        Assert.That(Hold(a, 1_000), Is.True, "The first bomber in position waits for the others.");
        Assert.That(Hold(b, 1_400), Is.True);
        Assert.That(Hold(c, 1_600), Is.False, "The last arrival opens the volley.");
        Assert.That(Hold(a, 1_650), Is.False, "Everyone already waiting fires with it.");
        Assert.That(Hold(b, 1_700), Is.False);
        Assert.That(Hold(a, 4_500), Is.False, "Follow-up bombs chain without re-syncing.");
        Assert.That(Hold(b, 10_000), Is.False, "Each bomb keeps the chain window open.");

        Assert.That(Hold(a, 30_000), Is.True, "A new pull after a pause syncs again.");
        Assert.That(Hold(a, 31_199), Is.True);
        Assert.That(Hold(a, 31_200), Is.False, "Missing bombers delay the volley by at most 1.2 s.");
    }

    [Test]
    public void AStaleWaitDoesNotReleaseTheNextPullEarly()
    {
        object a = new(), b = new();
        object[] bombers = [a, b];
        object volley = System.Activator.CreateInstance(VolleyType, nonPublic: true)!;
        MethodInfo hold = VolleyType.GetMethod("ShouldHold")!;
        bool Hold(object bomber, long now) => (bool)hold.Invoke(volley, [bomber, bombers, now])!;

        Assert.That(Hold(a, 1_000), Is.True);
        // The pack died before b arrived; nobody bombed.
        Assert.That(Hold(a, 60_000), Is.True, "An abandoned wait restarts instead of firing at once.");
        Assert.That(Hold(b, 60_300), Is.False);
    }

    private static bool ShouldWait(object instance, System.Reflection.MethodInfo method, object target, long now) =>
        (bool)method.Invoke(instance, [target, now])!;
}
