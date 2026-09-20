using System;
using System.Reflection;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public sealed class UT_CamlannTier3HostilityAudit
{
    [Test]
    public void KnownCombatPoliciesRequireExplicitEnemyCombatantSignal()
    {
        AssertEnemyCombatantParameter(typeof(AutonomousRvrTargetPolicy), nameof(AutonomousRvrTargetPolicy.IsEligible));
        AssertEnemyCombatantParameter(typeof(AutonomousDungeonPolicy), nameof(AutonomousDungeonPolicy.CanEngageLocalOpponent));
        AssertEnemyCombatantParameter(typeof(AutonomousDarknessFallsPolicy), nameof(AutonomousDarknessFallsPolicy.CanEngageLocalOpponent));

        Assert.That(AutonomousRvrTargetPolicy.IsEligible(false, true, true, true, false, true), Is.False);
        Assert.That(AutonomousRvrTargetPolicy.IsEligible(true, true, true, true, false, true), Is.True);

        Assert.That(AutonomousDungeonPolicy.CanEngageLocalOpponent(false, 246, 246, true, true), Is.False);
        Assert.That(AutonomousDungeonPolicy.CanEngageLocalOpponent(true, 246, 246, true, true), Is.True);

        Assert.That(AutonomousDarknessFallsPolicy.CanEngageLocalOpponent(false, 249, 249, true, true), Is.False);
        Assert.That(AutonomousDarknessFallsPolicy.CanEngageLocalOpponent(true, 249, 249, true, true), Is.True);
    }

    private static void AssertEnemyCombatantParameter(Type policyType, string methodName)
    {
        MethodInfo method = policyType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null, $"{policyType.Name}.{methodName} must remain a public policy seam.");
        Assert.That(method.GetParameters()[0].ParameterType, Is.EqualTo(typeof(bool)),
            $"{policyType.Name}.{methodName} must receive an explicit enemy-combatant result.");
    }
}
