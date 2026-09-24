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

    private static bool ShouldWait(object instance, System.Reflection.MethodInfo method, object target, long now) =>
        (bool)method.Invoke(instance, [target, now])!;
}
