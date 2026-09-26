using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DOL.Database;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture, NonParallelizable]
    public class UT_EffectExpirationRefreshLocking
    {
        private sealed class Living : GameNPC
        {
            public override bool IsAlive => true;
        }

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly PropertyInfo Clock = typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime));
        private GameServer _previousServer;
        private long _previousTime;
        private Living _owner;

        [SetUp]
        public void Setup()
        {
            _previousServer = GameServer.Instance;
            _previousTime = GameLoop.GameLoopTime;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            Clock.SetValue(null, 100_000L);

            _owner = (Living)RuntimeHelpers.GetUninitializedObject(typeof(Living));
            _owner.ObjectState = GameObject.eObjectState.Active;
            typeof(GameNPC).GetField("m_brains", PrivateInstance).SetValue(_owner, new ArrayList());
            _owner.effectListComponent = EffectListComponent.Create(_owner);
        }

        [TearDown]
        public void Teardown()
        {
            if (_owner?.effectListComponent != null)
                ServiceObjectStore.Remove(_owner.effectListComponent);

            GameServer.LoadTestDouble(_previousServer);
            Clock.SetValue(null, _previousTime);
        }

        [Test]
        public void SimultaneousExpirationAndRefreshDoNotDeadlockOrRetainExpiredEffect()
        {
            const int spellId = 991321;
            Spell spell = new(new DbSpell
            {
                SpellID = spellId,
                Type = "SpeedEnhancement",
                Target = "Realm",
                Range = 1500,
                Duration = 60,
                Value = 147
            }, 1);
            SpellLine line = new("test", "test", "test", true);
            SpellHandler oldHandler = new(_owner, spell, line);
            SpellHandler refreshHandler = new(_owner, spell, line);
            ECSGameSpellEffect oldEffect = new(new(_owner, 60_000, 1, oldHandler));
            ECSGameSpellEffect refreshedEffect = new(new(_owner, 60_000, 1, refreshHandler));
            oldEffect.FinalizeState(EffectListComponent.AddEffectResult.Added);

            Dictionary<eEffect, List<ECSGameEffect>> effects = (Dictionary<eEffect, List<ECSGameEffect>>)
                typeof(EffectListComponent).GetField("_effects", PrivateInstance).GetValue(_owner.effectListComponent);
            effects[eEffect.MovementSpeedBuff] = new() { oldEffect };

            Lock effectsLock = (Lock)typeof(EffectListComponent).GetField("_effectsLock", PrivateInstance)
                .GetValue(_owner.effectListComponent);
            Thread expirationThread = null;
            Exception expirationFailure = null;

            Task<bool> refreshTask = Task.Run(() =>
            {
                lock (effectsLock)
                {
                    expirationThread = new Thread(() =>
                    {
                        try
                        {
                            oldEffect.End();
                        }
                        catch (Exception exception)
                        {
                            expirationFailure = exception;
                        }
                    }) { IsBackground = true };
                    expirationThread.Start();

                    Assert.That(SpinWait.SpinUntil(() => oldEffect.IsEnding, TimeSpan.FromSeconds(5)), Is.True,
                        "The expiration thread must claim the old effect before the refresh runs");
                    return refreshedEffect.Start();
                }
            });

            Assert.That(refreshTask.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "Refreshing an effect while its expiration waits on the effect list must not deadlock");
            Assert.That(expirationThread.Join(TimeSpan.FromSeconds(5)), Is.True,
                "The old effect expiration must complete after the replacement releases the effect list");
            Assert.That(expirationFailure, Is.Null);
            Assert.That(refreshTask.Result, Is.True);
            Assert.That(oldEffect.IsEnded, Is.True, "The refresh must not resurrect the expired instance");
            Assert.That(refreshedEffect.IsActive, Is.True, "The newly requested effect should remain active");
            Assert.That(_owner.effectListComponent.GetSpellEffects(eEffect.MovementSpeedBuff),
                Is.EqualTo(new[] { refreshedEffect }));
        }
    }
}
