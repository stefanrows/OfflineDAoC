using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.Events;
using DOL.GS;
using DOL.GS.Effects;
using DOL.GS.PacketHandler;
using DOL.GS.PropertyCalc;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Behaviour of the four Master Level hold effects (TOA_PLAN WP2.3): what
    /// they set up, what ends them, and what they clean up. The effects are
    /// driven directly (OnStartEffect, watch samples, OnStopEffect), without the
    /// effect-list service.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_MasterLevelHolds
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<ECSGameSpellEffect> _started = new();

        public class Packets : DispatchProxy
        {
            public readonly List<string> Messages = new();

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == "SendMessage" && args.Length > 0 && args[0] is string text)
                    Messages.Add(text);

                return method.ReturnType != typeof(void) && method.ReturnType.IsValueType
                    ? Activator.CreateInstance(method.ReturnType)
                    : null;
            }
        }

        private sealed class Member : GamePlayer
        {
            private Member() : base(null, null) { }
            public IPacketLib Packets;
            public string TestName;
            public bool Sitting, Moving, Attacking, Stealthed;
            public int StealthSpec;
            public int PosX, PosY;
            public eRealm TestRealm;
            public ushort Region;
            public override IPacketLib Out => Packets;
            public override string Name { get => TestName; set => TestName = value; }
            public override bool IsAlive => true;
            public override eObjectState ObjectState { get => eObjectState.Active; set { } }
            public override bool IsSitting { get => Sitting; set => Sitting = value; }
            public override bool IsMoving => Moving;
            public override bool IsAttacking => Attacking;
            public override bool IsCasting => false;
            public override int X { get => PosX; set => PosX = value; }
            public override int Y { get => PosY; set => PosY = value; }
            public override int Z { get => 0; set { } }
            public override ushort CurrentRegionID { get => Region; set => Region = value; }
            public override Region CurrentRegion { get => null; set { } }
            public override eRealm Realm { get => TestRealm; set => TestRealm = value; }
            public override bool IsStealthed => Stealthed;
            public override void Stealth(bool goStealth) => Stealthed = goStealth;
            public override bool GroundTargetInView => true;
            public override Specialization GetSpecializationByName(string name) =>
                name == Specs.Stealth && StealthSpec > 0
                    ? new Specialization(Specs.Stealth, "Stealth", 0) { Level = StealthSpec }
                    : null;
        }

        /// <summary>Battlewarder effect whose warder is "planted" without a region.</summary>
        private sealed class TestBattlewarderEffect : BattlewarderECSEffect
        {
            public TestBattlewarderEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }
            protected override bool PlantWarder(GameNPC warder) => true;
        }

        [TearDown]
        public void StopEverything()
        {
            foreach (ECSGameSpellEffect effect in _started)
                effect.OnStopEffect();

            _started.Clear();
        }

        private static void Set(Type type, object instance, string field, object value) =>
            type.GetField(field, Hidden)!.SetValue(instance, value);

        private static Member NewMember(string name, int x = 0)
        {
            var member = (Member)RuntimeHelpers.GetUninitializedObject(typeof(Member));
            member.TestName = name;
            member.TestRealm = eRealm.Albion;
            member.Region = 1;
            member.PosX = x;
            member.Packets = DispatchProxy.Create<IPacketLib, Packets>();
            Set(typeof(GameLiving), member, "<BaseBuffBonusCategory>k__BackingField", new PropertyIndexer());
            Set(typeof(GameLiving), member, "<GroundTarget>k__BackingField", new GroundTarget());
            Set(typeof(GameLiving), member, "m_effects", new GameEffectList(member));
            return member;
        }

        private static List<string> MessagesOf(Member member) => ((Packets)(object)member.Packets).Messages;

        private static Spell NewSpell(string type, string target) => new(new DbSpell
        {
            SpellID = 7000 + type.Length,
            Name = type,
            Type = type,
            Target = target,
            Duration = 600,
            Range = 1500,
        }, 1);

        private static readonly SpellLine Line = new("ML_test", "Master Level", "", true);

        private T Begin<T>(T effect) where T : ECSGameSpellEffect
        {
            _started.Add(effect);
            effect.OnStartEffect();
            return effect;
        }

        // ------------------------------------------------------------------ Lookout

        [Test]
        public void LookoutUsesTheSeatedPartnerWhenTheSpellLandsOnTheScout()
        {
            Member scout = NewMember("Scout");
            Member sitter = NewMember("Sitter", x: 100);
            sitter.Sitting = true;
            var handler = new LoockoutSpellHandler(scout, NewSpell("Loockout", "Self"), Line);
            typeof(LoockoutSpellHandler).GetProperty(nameof(LoockoutSpellHandler.Partner))!.SetValue(handler, sitter);

            var effect = Begin(new LoockoutECSEffect(new ECSGameEffectInitParams(scout, 600000, 1, handler)));

            Assert.That(effect.Sitter, Is.SameAs(sitter), "a Self row falls back to the partner chosen at cast start");
            Assert.That(effect.BonusGiven, Is.True);
            Assert.That(scout.BaseBuffBonusCategory[eProperty.Skill_Stealth], Is.EqualTo(LoockoutECSEffect.StealthBonus));
            Assert.That(effect.SitterWatch.Watched, Is.SameAs(sitter));
            Assert.That(effect.ScoutWatch.Watched, Is.SameAs(scout));

            effect.OnStopEffect();
            _started.Clear();
            Assert.That(scout.BaseBuffBonusCategory[eProperty.Skill_Stealth], Is.Zero, "the borrowed stealth is returned");
        }

        [Test]
        public void LookoutEndsWhenThePartnerStandsUp()
        {
            Member scout = NewMember("Scout");
            Member sitter = NewMember("Sitter", x: 100);
            sitter.Sitting = true;
            var handler = new LoockoutSpellHandler(scout, NewSpell("Loockout", "Realm"), Line);
            var effect = Begin(new LoockoutECSEffect(new ECSGameEffectInitParams(sitter, 600000, 1, handler)));

            Assert.That(effect.SitterWatch.Check(), Is.True);
            sitter.Sitting = false;
            Assert.That(effect.SitterWatch.Check(), Is.False);
            Assert.That(effect.EndReason, Is.EqualTo(MovementWatch.Standing));
            Assert.That(MessagesOf(scout), Has.Some.Contains("stands up"));
        }

        [Test]
        public void LookoutEndsWhenTheScoutMoves()
        {
            Member scout = NewMember("Scout");
            Member sitter = NewMember("Sitter", x: 100);
            sitter.Sitting = true;
            var handler = new LoockoutSpellHandler(scout, NewSpell("Loockout", "Realm"), Line);
            var effect = Begin(new LoockoutECSEffect(new ECSGameEffectInitParams(sitter, 600000, 1, handler)));

            scout.Moving = true;
            Assert.That(effect.ScoutWatch.Check(), Is.False);
            Assert.That(effect.EndReason, Is.EqualTo(MovementWatch.Moving));
            Assert.That(MessagesOf(scout), Has.Some.Contains("Your concentration fades"));
        }

        [Test]
        public void LookoutPartnerMustBeASeatedRealmMateBesideTheScout()
        {
            Member scout = NewMember("Scout");
            Member beside = NewMember("Beside", x: LoockoutSpellHandler.PartnerRange - 10);
            beside.Sitting = true;
            Assert.That(LoockoutSpellHandler.PartnerProblem(scout, beside), Is.Null);

            Member far = NewMember("Far", x: LoockoutSpellHandler.PartnerRange + 50);
            far.Sitting = true;
            Assert.That(LoockoutSpellHandler.PartnerProblem(scout, far), Is.Not.Null, "too far away");

            Member enemy = NewMember("Enemy", x: 50);
            enemy.Sitting = true;
            enemy.TestRealm = eRealm.Midgard;
            Assert.That(LoockoutSpellHandler.PartnerProblem(scout, enemy), Is.Not.Null, "other realm");

            Member standing = NewMember("Standing", x: 50);
            Assert.That(LoockoutSpellHandler.PartnerProblem(scout, standing), Is.Not.Null, "not sitting");

            scout.Sitting = true;
            Assert.That(LoockoutSpellHandler.PartnerProblem(scout, scout), Is.Not.Null, "not beside yourself");
        }

        [Test]
        public void LookoutWithoutAValidPartnerGivesNothing()
        {
            Member scout = NewMember("Scout");
            Member far = NewMember("Far", x: 5000);
            far.Sitting = true;
            var handler = new LoockoutSpellHandler(scout, NewSpell("Loockout", "Realm"), Line);
            var effect = Begin(new LoockoutECSEffect(new ECSGameEffectInitParams(far, 600000, 1, handler)));

            Assert.That(effect.BonusGiven, Is.False);
            Assert.That(effect.SitterWatch, Is.Null);
            Assert.That(scout.BaseBuffBonusCategory[eProperty.Skill_Stealth], Is.Zero);
        }

        // ------------------------------------------------------- Blanket of Camouflage

        [Test]
        public void BlanketHoldsAMemberWithoutStealthAndEndsOnAttack()
        {
            Member caster = NewMember("Spymaster");
            Member member = NewMember("Cleric", x: 50);
            var handler = new GroupstealthHandler(caster, NewSpell("BlanketOfCamouflage", "Group"), Line);
            var effect = Begin(new GroupstealthECSEffect(new ECSGameEffectInitParams(member, 600000, 1, handler)));

            Assert.That(member.Stealthed, Is.True);
            Assert.That(effect.Held, Is.True);

            member.Attacking = true;
            Assert.That(effect.Watch.Check(), Is.False);
            Assert.That(effect.EndReason, Is.EqualTo(MovementWatch.Attacking));
            Assert.That(MessagesOf(member), Has.Some.Contains("You are attacking. Your camouflage fades!"));

            effect.OnStopEffect();
            _started.Clear();
            Assert.That(member.Stealthed, Is.False, "a held member is revealed when the blanket ends");
        }

        [Test]
        public void BlanketLeavesStealthersToTheNormalStealthRules()
        {
            Member caster = NewMember("Spymaster");
            Member infiltrator = NewMember("Infiltrator", x: 50);
            infiltrator.StealthSpec = 30;
            var handler = new GroupstealthHandler(caster, NewSpell("BlanketOfCamouflage", "Group"), Line);
            var effect = Begin(new GroupstealthECSEffect(new ECSGameEffectInitParams(infiltrator, 600000, 1, handler)));

            Assert.That(infiltrator.Stealthed, Is.True);
            Assert.That(effect.Held, Is.False);
            Assert.That(effect.Watch, Is.Null, "moving in stealth is the stealther's own business");

            effect.OnStopEffect();
            _started.Clear();
            Assert.That(infiltrator.Stealthed, Is.True, "a stealther keeps their own stealth");
        }

        [Test]
        public void EachBlanketMemberEndsOnlyTheirOwnEffect()
        {
            Member caster = NewMember("Spymaster");
            Member first = NewMember("First", x: 50);
            Member second = NewMember("Second", x: 80);
            var handler = new GroupstealthHandler(caster, NewSpell("BlanketOfCamouflage", "Group"), Line);
            var one = Begin(new GroupstealthECSEffect(new ECSGameEffectInitParams(first, 600000, 1, handler)));
            var two = Begin(new GroupstealthECSEffect(new ECSGameEffectInitParams(second, 600000, 1, handler)));

            first.Moving = true;
            Assert.That(one.Watch.Check(), Is.False);
            Assert.That(two.Watch.Check(), Is.True);
            Assert.That(one.EndReason, Is.EqualTo(MovementWatch.Moving));
            Assert.That(two.EndReason, Is.Null, "one handler, one effect per member: no shared field");
        }

        // ---------------------------------------------------------------- Battlewarder

        private (Member caster, TestBattlewarderEffect effect) PlantedWarder()
        {
            Member caster = NewMember("Convoker");
            caster.GroundTarget.Set(400, 400, 0);
            var handler = new BattlewarderSpellHandler(caster, NewSpell("Battlewarder", "Self"), Line);
            var effect = Begin(new TestBattlewarderEffect(new ECSGameEffectInitParams(caster, 600000, 1, handler)));
            return (caster, effect);
        }

        [Test]
        public void BattlewarderIsPlantedAtTheGroundTargetAndDismissedWhenTheCasterMoves()
        {
            var (caster, effect) = PlantedWarder();
            GameNPC warder = effect.Warder;

            Assert.That(warder, Is.InstanceOf<BattleWarderNpc>());
            Assert.That((warder.RealX, warder.RealY), Is.EqualTo((400, 400)), "spawn point, read by AddToWorld");
            Assert.That(warder.Realm, Is.EqualTo(caster.Realm));

            caster.Moving = true;
            Assert.That(effect.Watch.Check(), Is.False);
            Assert.That(effect.EndReason, Is.EqualTo(MovementWatch.Moving));

            effect.OnStopEffect();
            _started.Clear();
            Assert.That(effect.Warder, Is.Null);
            Assert.That(warder.ObjectState, Is.EqualTo(GameObject.eObjectState.Deleted));
        }

        [Test]
        public void BattlewarderEndsWhenTheWarderFalls()
        {
            var (caster, effect) = PlantedWarder();
            GameNPC warder = effect.Warder;

            GameEventMgr.Notify(GameLivingEvent.Dying, warder, new DyingEventArgs(null));

            Assert.That(effect.EndReason, Is.EqualTo(BattlewarderECSEffect.WarderFallen));
            Assert.That(MessagesOf(caster), Has.Some.Contains("Your Battle Warder has fallen!"));

            effect.OnStopEffect();
            _started.Clear();
            Assert.That(warder.ObjectState, Is.EqualTo(GameObject.eObjectState.Deleted));
        }

        [Test]
        public void AKilledWarderNeverRespawnsAndPaysNothing()
        {
            using var server = new EpicTestServerScope();
            var warder = new BattleWarderNpc { Name = "Battle Warder", Realm = eRealm.Albion, MaxSpeedBase = 0 };
            Member killer = NewMember("Enemy");
            killer.TestRealm = eRealm.Midgard;
            warder.XPGainers[killer] = 500;

            Assert.That(warder.RewardStatus, Is.EqualTo(GameNPC.RewardEligibility.DeniedInvalid));
            Assert.That(warder.CanDropLoot, Is.False);
            Assert.That(warder.RespawnInterval, Is.LessThanOrEqualTo(0));

            GameServer.ServerRules.OnNpcKilled(warder, killer);
            Assert.That(MessagesOf(killer), Has.Some.EqualTo("You gain no experience from this kill!"));

            warder.ProcessDeath(null);
            Assert.That(warder.IsRespawning, Is.False, "no respawn timer: it would return ownerless");
        }
    }
}
