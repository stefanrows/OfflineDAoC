using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture, NonParallelizable]
    public class UT_CompanionProtectionRange
    {
        private CompanionPolicyTestServerScope _serverScope;

        private sealed class PositionedBot : GameBot
        {
            private PositionedBot() : base((OfflineWorldBotRecord)null) { }

            public int PositionX;
            public override int X => PositionX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
        }

        private sealed class PositionedActor : GameNPC
        {
            private PositionedActor() { }

            public int PositionX;
            public override int X => PositionX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
        }

        [TestCase(eEffect.Guard, 257)]
        [TestCase(eEffect.Protect, 1001)]
        public void ProviderIsNotAssignedToAnOutOfRangeTarget(eEffect effectType, int targetX)
        {
            PositionedBot provider = NewUninitialized<PositionedBot>();
            PositionedActor target = NewUninitialized<PositionedActor>();
            target.PositionX = targetX;

            Dictionary<GameBot, GameLiving> plan = Plan([provider], [target], effectType);

            Assert.That(plan, Is.Empty);
        }

        [TestCase(eEffect.Guard, 257)]
        [TestCase(eEffect.Protect, 1001)]
        public void OutOfRangeProviderDoesNotConsumeCoverageAvailableFromANearbyProvider(eEffect effectType, int targetX)
        {
            PositionedBot distantProvider = NewUninitialized<PositionedBot>();
            PositionedBot nearbyProvider = NewUninitialized<PositionedBot>();
            PositionedActor target = NewUninitialized<PositionedActor>();
            nearbyProvider.PositionX = targetX;
            target.PositionX = targetX;

            Dictionary<GameBot, GameLiving> plan = Plan([distantProvider, nearbyProvider], [target], effectType);

            Assert.That(plan, Has.Count.EqualTo(1));
            Assert.That(plan.TryGetValue(nearbyProvider, out GameLiving assignedTarget), Is.True);
            Assert.That(assignedTarget, Is.SameAs(target));
            Assert.That(plan.ContainsKey(distantProvider), Is.False);
        }

        [TestCase(eEffect.Guard, 255)]
        [TestCase(eEffect.Protect, 999)]
        public void ProviderCoversTargetWithinNativeEffectRange(eEffect effectType, int targetX)
        {
            PositionedBot provider = NewUninitialized<PositionedBot>();
            PositionedActor target = NewUninitialized<PositionedActor>();
            target.PositionX = targetX;

            Dictionary<GameBot, GameLiving> plan = Plan([provider], [target], effectType);

            Assert.That(plan.TryGetValue(provider, out GameLiving assignedTarget), Is.True);
            Assert.That(assignedTarget, Is.SameAs(target));
        }

        [TestCase(eEffect.Guard, 257, false)]
        [TestCase(eEffect.Guard, 255, true)]
        [TestCase(eEffect.Protect, 1001, false)]
        [TestCase(eEffect.Protect, 999, true)]
        public void ExternalEffectOnlyReservesTargetWhileItsSourceIsInRange(eEffect effectType, int sourceX, bool expected)
        {
            PositionedActor source = NewUninitialized<PositionedActor>();
            PositionedActor target = NewUninitialized<PositionedActor>();
            source.PositionX = sourceX;

            Assert.That(IsEffectiveExternalSource(source, target, new HashSet<GameBot>(), effectType), Is.EqualTo(expected));
        }

        [TestCase(eEffect.Guard, 1000)]
        [TestCase(eEffect.Protect, 2000)]
        public void ReachableProvidersRemainDistributedAcrossMembers(eEffect effectType, int separation)
        {
            PositionedBot firstProvider = NewUninitialized<PositionedBot>();
            PositionedBot secondProvider = NewUninitialized<PositionedBot>();
            PositionedActor firstTarget = NewUninitialized<PositionedActor>();
            PositionedActor secondTarget = NewUninitialized<PositionedActor>();
            secondProvider.PositionX = separation;
            secondTarget.PositionX = separation;

            Dictionary<GameBot, GameLiving> plan = Plan(
                [firstProvider, secondProvider],
                [firstTarget, secondTarget],
                effectType);

            Assert.That(plan.Count, Is.EqualTo(2));
            Assert.That(plan[firstProvider], Is.SameAs(firstTarget));
            Assert.That(plan[secondProvider], Is.SameAs(secondTarget));
        }

        [TestCase(eEffect.Guard)]
        [TestCase(eEffect.Protect)]
        public void ManagedProviderEffectDoesNotReserveTargetAsExternalCoverage(eEffect effectType)
        {
            PositionedBot provider = NewUninitialized<PositionedBot>();
            PositionedActor target = NewUninitialized<PositionedActor>();

            Assert.That(IsEffectiveExternalSource(provider, target, new HashSet<GameBot> { provider }, effectType), Is.False);
        }

        private static Dictionary<GameBot, GameLiving> Plan(
            IReadOnlyList<GameBot> providers,
            IReadOnlyList<GameLiving> targets,
            eEffect effectType)
        {
            MethodInfo method = PlannerType.GetMethod("PlanCoverage", BindingFlags.Static | BindingFlags.NonPublic);
            return (Dictionary<GameBot, GameLiving>)method.Invoke(null,
            [
                providers,
                targets,
                new HashSet<GameLiving>(),
                new HashSet<GameLiving>(),
                effectType,
            ]);
        }

        private static bool IsEffectiveExternalSource(
            GameLiving source,
            GameLiving target,
            HashSet<GameBot> managedProviders,
            eEffect effectType)
        {
            MethodInfo method = PlannerType.GetMethod("IsEffectiveExternalSource", BindingFlags.Static | BindingFlags.NonPublic);
            return (bool)method.Invoke(null, [source, target, managedProviders, effectType]);
        }

        private static System.Type PlannerType => typeof(GameBot).Assembly.GetType("DOL.AI.Brain.CompanionProtection", true);

        [SetUp]
        public void SetUp() => _serverScope = new CompanionPolicyTestServerScope();

        [TearDown]
        public void TearDown() => _serverScope.Dispose();

        private static T NewUninitialized<T>() where T : class =>
            (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }
}
