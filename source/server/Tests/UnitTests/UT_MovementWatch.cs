using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// MovementWatch samples the state the dead Moving / AttackFinished /
    /// CastStarting events used to announce, so the Master Level holds
    /// (Lookout, Blanket of Camouflage, Battlewarder, Focusing Winds) end.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_MovementWatch
    {
        private sealed class Holder : GameNPC
        {
            public bool Alive = true, Moving, Attacking, Casting, Sitting;
            public int PosX, PosY, PosZ;
            public ushort Region = 1;
            public override bool IsAlive => Alive;
            public override bool IsMoving => Moving;
            public override bool IsAttacking => Attacking;
            public override bool IsCasting => Casting;
            public override bool IsSitting { get => Sitting; set => Sitting = value; }
            public override int X { get => PosX; set => PosX = value; }
            public override int Y { get => PosY; set => PosY = value; }
            public override int Z { get => PosZ; set => PosZ = value; }
            public override ushort CurrentRegionID { get => Region; set => Region = value; }
            public override eObjectState ObjectState { get => eObjectState.Active; set { } }
        }

        private static (MovementWatch watch, List<string> reasons) Watch(Holder who,
            bool onMove = true, bool onAttack = false, bool onCast = false, ISpellHandler ownCast = null)
        {
            var reasons = new List<string>();
            var watch = new MovementWatch(who, reasons.Add, null, onMove, onAttack, onCast, ownCast);
            watch.Arm();
            return (watch, reasons);
        }

        [Test]
        public void StandingStillKeepsTheHold()
        {
            var who = new Holder { PosX = 1000, PosY = 2000, PosZ = 300 };
            var (watch, reasons) = Watch(who, onAttack: true, onCast: true);

            Assert.That(watch.Check(), Is.True);
            who.PosX += MovementWatch.StepDistance; // drift within StepDistance is a wobble
            Assert.That(watch.Check(), Is.True);
            Assert.That(reasons, Is.Empty);
            Assert.That(watch.IsRunning, Is.True);
        }

        [Test]
        public void MovingBreaksOnceAndStops()
        {
            var who = new Holder();
            var (watch, reasons) = Watch(who);

            who.Moving = true;
            Assert.That(watch.Check(), Is.False);
            Assert.That(watch.Check(), Is.False, "a stopped watch does not report again");
            Assert.That(reasons, Is.EqualTo(new[] { MovementWatch.Moving }));
            Assert.That(watch.IsRunning, Is.False);
        }

        [Test]
        public void DriftFromTheArmedPointCountsAsMoving()
        {
            var who = new Holder { PosX = 1000 };
            var (watch, reasons) = Watch(who);

            // Two small steps, each missed by IsMoving: the drift adds up.
            who.PosX += MovementWatch.StepDistance / 2 + 1;
            Assert.That(watch.Check(), Is.True);
            who.PosX += MovementWatch.StepDistance / 2 + 1;
            Assert.That(watch.Check(), Is.False);
            Assert.That(reasons, Is.EqualTo(new[] { MovementWatch.Moving }));
        }

        [Test]
        public void ARegionChangeCountsAsMoving()
        {
            var who = new Holder();
            var (watch, reasons) = Watch(who);

            who.Region = 2;
            Assert.That(watch.Check(), Is.False);
            Assert.That(reasons, Is.EqualTo(new[] { MovementWatch.Moving }));
        }

        [Test]
        public void AttackingBreaksOnlyWhenWatched()
        {
            var moveOnly = new Holder();
            var (lookout, lookoutReasons) = Watch(moveOnly);
            moveOnly.Attacking = true;
            moveOnly.LastAttackTickPvE = 5000;
            Assert.That(lookout.Check(), Is.True, "Lookout and Focusing Winds end on moving only");
            Assert.That(lookoutReasons, Is.Empty);

            var swinging = new Holder();
            var (warder, warderReasons) = Watch(swinging, onAttack: true);
            swinging.LastAttackTickPvP = 5000;
            Assert.That(warder.Check(), Is.False);
            Assert.That(warderReasons, Is.EqualTo(new[] { MovementWatch.Attacking }));

            var autoAttack = new Holder();
            var (blanket, blanketReasons) = Watch(autoAttack, onAttack: true);
            autoAttack.Attacking = true;
            Assert.That(blanket.Check(), Is.False);
            Assert.That(blanketReasons, Is.EqualTo(new[] { MovementWatch.Attacking }));
        }

        [Test]
        public void AnAttackBeforeTheWatchStartedDoesNotCount()
        {
            var who = new Holder { LastAttackTickPvE = 4000 };
            var (watch, reasons) = Watch(who, onAttack: true);

            Assert.That(watch.Check(), Is.True);
            Assert.That(reasons, Is.Empty);
        }

        [Test]
        public void CastingBreaksExceptForTheSpellThatRaisedTheEffect()
        {
            var who = new Holder();
            var own = (SpellHandler)RuntimeHelpers.GetUninitializedObject(typeof(SpellHandler));
            typeof(CastingComponent)
                .GetField("<SpellHandler>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(who.castingComponent, own);
            who.Casting = true;

            var (watch, reasons) = Watch(who, onCast: true, ownCast: own);
            Assert.That(watch.Check(), Is.True, "the hold's own cast is still finishing");

            var (other, otherReasons) = Watch(who, onCast: true);
            Assert.That(other.Check(), Is.False);
            Assert.That(otherReasons, Is.EqualTo(new[] { MovementWatch.Casting }));
            Assert.That(reasons, Is.Empty);
        }

        [Test]
        public void StandingUpBreaksOnlyWhenWatched()
        {
            var sitter = new Holder { Sitting = true };
            var reasons = new List<string>();
            var watch = new MovementWatch(sitter, reasons.Add, onStand: true);
            watch.Arm();
            var (plain, plainReasons) = Watch(sitter);

            Assert.That(watch.Check(), Is.True);
            sitter.Sitting = false;
            Assert.That(plain.Check(), Is.True, "standing up is not moving");
            Assert.That(watch.Check(), Is.False);
            Assert.That(reasons, Is.EqualTo(new[] { MovementWatch.Standing }));
            Assert.That(plainReasons, Is.Empty);
        }

        [Test]
        public void DeathReportsGone()
        {
            var who = new Holder();
            var (watch, reasons) = Watch(who);

            who.Alive = false;
            Assert.That(watch.Check(), Is.False);
            Assert.That(reasons, Is.EqualTo(new[] { MovementWatch.Gone }));
        }

        [Test]
        public void AnEffectEndedElsewhereStopsTheWatchQuietly()
        {
            var who = new Holder();
            bool wanted = true;
            var reasons = new List<string>();
            var watch = new MovementWatch(who, reasons.Add, () => wanted);
            watch.Arm();

            wanted = false;
            who.Moving = true;
            Assert.That(watch.Check(), Is.False);
            Assert.That(reasons, Is.Empty);
            Assert.That(watch.IsRunning, Is.False);
        }

        [TestCase(typeof(LoockoutSpellHandler))]
        [TestCase(typeof(GroupstealthHandler))]
        [TestCase(typeof(BattlewarderSpellHandler))]
        [TestCase(typeof(FocusingWindsSpellHandler))]
        public void HoldHandlersBuildTheirOwnEcsEffect(System.Type handler)
        {
            // The legacy OnEffectStart(GameSpellEffect) of these handlers was never
            // reached; each must build the ECS effect that carries its watch.
            MethodInfo create = handler.GetMethod(nameof(SpellHandler.CreateECSEffect));
            Assert.That(create, Is.Not.Null);
            Assert.That(create!.DeclaringType, Is.EqualTo(handler));
        }

        [Test]
        public void BlanketOfCamouflageIsABenefitNotAnAttackOnTheGroup()
        {
            var handler = (GroupstealthHandler)RuntimeHelpers.GetUninitializedObject(typeof(GroupstealthHandler));
            Assert.That(handler.HasPositiveEffect, Is.True);
        }

        [Test]
        public void StopBeforeASampleReportsNothing()
        {
            var who = new Holder();
            var (watch, reasons) = Watch(who);
            watch.Stop();

            who.Moving = true;
            Assert.That(watch.Check(), Is.False);
            Assert.That(reasons, Is.Empty);
        }
    }
}
