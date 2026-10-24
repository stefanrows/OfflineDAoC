using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Events;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Trials of Atlantis core hooks (TOA_PLAN WP2.0): the global Dying event
    /// names every player who earned the kill, and GamePlayer.GainExperience
    /// raises GainedExperience for Master Level and artifact experience.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_ToaCoreHooks
    {
        public class Packets : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                return method.ReturnType != typeof(void) && method.ReturnType.IsValueType
                    ? Activator.CreateInstance(method.ReturnType)
                    : null;
            }
        }

        private sealed class Killer : GamePlayer
        {
            private Killer() : base(null, null) { }
            public int Kills;
            public override void EnemyKilled(GameLiving enemy) => Kills++;
        }

        private sealed class Earner : GamePlayer
        {
            private Earner() : base(null, null) { }
            public IPacketLib Packets;
            public long Xp;
            public override IPacketLib Out => Packets;
            public override byte Level { get => 1; set { } }
            public override long Experience { get => Xp; set => Xp = value; }
            public override long ExperienceForCurrentLevel => 0;
            public override long ExperienceForNextLevel => long.MaxValue;
            public ICharacterClass Class;
            public override ICharacterClass CharacterClass => Class;
            private ConcurrentDictionary<AbstractQuest, byte> _quests;
            public override ConcurrentDictionary<AbstractQuest, byte> QuestList => _quests ??= new();
        }

        private sealed class Victim : GameLiving
        {
            public override eGameObjectType GameObjectType => eGameObjectType.NPC;
        }

        private static Killer NewKiller() => (Killer)RuntimeHelpers.GetUninitializedObject(typeof(Killer));

        private static Earner NewEarner()
        {
            var earner = (Earner)RuntimeHelpers.GetUninitializedObject(typeof(Earner));
            earner.Packets = DispatchProxy.Create<IPacketLib, Packets>();
            earner.Class = DispatchProxy.Create<ICharacterClass, Packets>();
            return earner;
        }

        [Test]
        public void DyingNotifyCarriesEveryPlayerAttacker()
        {
            var victim = new Victim();
            Killer first = NewKiller();
            Killer second = NewKiller();
            long expire = GameLoop.GameLoopTime + 60000;
            victim.attackComponent.AttackerTracker.AddOrUpdate(first, true, expire);
            victim.attackComponent.AttackerTracker.AddOrUpdate(second, false, expire);

            DyingEventArgs seen = null;
            DOLEventHandler listener = (e, sender, args) =>
            {
                if (sender == victim)
                    seen = args as DyingEventArgs;
            };

            GameEventMgr.AddHandler(GameLivingEvent.Dying, listener);

            try
            {
                victim.ProcessDeath(first);
            }
            finally
            {
                GameEventMgr.RemoveHandler(GameLivingEvent.Dying, listener);
            }

            Assert.That(seen, Is.Not.Null, "the global Dying notify fires");
            Assert.That(seen.Killer, Is.SameAs(first));
            Assert.That(seen.PlayerKillers, Is.EquivalentTo(new GamePlayer[] { first, second }),
                "every player attacker is credited, not only the killing blow");
            Assert.That(first.Kills, Is.EqualTo(1));
            Assert.That(second.Kills, Is.EqualTo(1));
        }

        [Test]
        public void GainExperienceRaisesGainedExperienceWithTheArgumentsPassedIn()
        {
            Earner player = NewEarner();
            var received = new List<GainedExperienceEventArgs>();
            DOLEventHandler listener = (e, sender, args) =>
            {
                if (sender == player && args is GainedExperienceEventArgs xp)
                    received.Add(xp);
            };

            GameEventMgr.AddHandler(player, GamePlayerEvent.GainedExperience, listener);

            try
            {
                var arguments = new GainedExperienceEventArgs(1234, 100, 50, 0, 0, 25, false, false, eXPSource.NPC);
                player.GainExperience(arguments);

                Assert.That(player.Xp, Is.EqualTo(1234 + 100 + 50 + 25));
                Assert.That(received, Has.Count.EqualTo(1));
                Assert.That(received[0], Is.SameAs(arguments));
                Assert.That(received[0].ExpBase, Is.EqualTo(1234));
                Assert.That(received[0].XPSource, Is.EqualTo(eXPSource.NPC));
            }
            finally
            {
                GameEventMgr.RemoveHandler(player, GamePlayerEvent.GainedExperience, listener);
            }
        }

        [Test]
        public void GainExperienceStaysQuietWhenNotifyIsOffOrNothingWasGained()
        {
            Earner player = NewEarner();
            int raised = 0;
            DOLEventHandler listener = (e, sender, args) => raised++;
            GameEventMgr.AddHandler(player, GamePlayerEvent.GainedExperience, listener);

            try
            {
                player.GainExperience(new GainedExperienceEventArgs(500, 0, 0, 0, 0, 0, false, false, eXPSource.NPC), notify: false);
                player.GainExperience(new GainedExperienceEventArgs(0, 0, 0, 0, 0, 0, false, false, eXPSource.NPC));

                Assert.That(player.Xp, Is.EqualTo(500), "notify=false still grants the experience");
                Assert.That(raised, Is.Zero);
            }
            finally
            {
                GameEventMgr.RemoveHandler(player, GamePlayerEvent.GainedExperience, listener);
            }
        }

        [Test]
        public void GainedExperienceIsReachableAsPlayerAndLivingEvent()
        {
            Assert.That(GamePlayerEvent.GainedExperience, Is.SameAs(GameLivingEvent.GainedExperience));
        }
    }
}
