using System;
using System.Reflection;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture, NonParallelizable]
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

    /// <summary>
    /// RvR mode spec section 9.3: each seam fed with the world's answer for a
    /// same-realm stranger. Camlann calls the stranger an enemy, the RvR world
    /// an ally; the realm rule lives in the alliance seam, never in a bot filter.
    /// </summary>
    [TestCaseSource(typeof(BothWorlds), nameof(BothWorlds.All))]
    public void SeamsTakeTheWorldsAnswerForASameRealmStranger(WorldModelKind world)
    {
        using var _ = BothWorlds.Use(world);
        GameBot first = StrangerBot(eRealm.Midgard);
        GameBot second = StrangerBot(eRealm.Midgard);
        bool enemy = !DOL.GS.ServerRules.PvpCombatant.AreAllied(first, second);

        Assert.Multiple(() =>
        {
            Assert.That(enemy, Is.EqualTo(BothWorlds.Expect(world, true, false)));
            Assert.That(AutonomousRvrTargetPolicy.IsEligible(enemy, true, true, true, false, true), Is.EqualTo(enemy));
            Assert.That(AutonomousDungeonPolicy.CanEngageLocalOpponent(enemy, 246, 246, true, true), Is.EqualTo(enemy));
            Assert.That(AutonomousDarknessFallsPolicy.CanEngageLocalOpponent(enemy, 249, 249, true, true), Is.EqualTo(enemy));
        });
    }

    private sealed class StrangerTestBot : GameBot
    {
        public StrangerTestBot() : base((OfflineWorldBotRecord)null) { }

        public override bool IsAlive => true;
        public override eRealm Realm { get; set; }
        public override ushort CurrentRegionID { get; set; } = 1;
    }

    private static GameBot StrangerBot(eRealm realm)
    {
        var bot = (StrangerTestBot)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(StrangerTestBot));
        typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(bot, new System.Collections.ArrayList());
        bot.Realm = realm;
        return bot;
    }

    private static void AssertEnemyCombatantParameter(Type policyType, string methodName)
    {
        MethodInfo method = policyType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null, $"{policyType.Name}.{methodName} must remain a public policy seam.");
        Assert.That(method.GetParameters()[0].ParameterType, Is.EqualTo(typeof(bool)),
            $"{policyType.Name}.{methodName} must receive an explicit enemy-combatant result.");
    }
}
