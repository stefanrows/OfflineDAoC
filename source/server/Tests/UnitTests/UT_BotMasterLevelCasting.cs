using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PropertyCalc;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bots casting Convoker, Stormlord and Spymaster abilities (TOA_PLAN WP2.6): the human pacing
    /// (reaction delay, one decision at a time, pause after a press, cooldowns), where ground
    /// targets go, when an ability is skipped as pointless, and the placed fonts, traps and storms
    /// that now take a bot as their owner and live on an ECS effect.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_BotMasterLevelCasting
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly SpellLine Line = new("ML_cast_test", "Master Level", "", true);

        private sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public int PosX, PosY;
            public bool Moving;
            public override string Name { get => "Convoker"; set { } }
            public override bool IsAlive => true;
            public override bool IsMoving => Moving;
            public override bool IsAttacking => false;
            public override bool IsCasting => false;
            public override int X => PosX;
            public override int Y => PosY;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override eRealm Realm { get => eRealm.Albion; set { } }
        }

        /// <summary>A caster whose cooldown and power the test sets.</summary>
        private sealed class Caster : GameNPC
        {
            public int Disabled;
            public int TestMana;
            public override int GetSkillDisabledDuration(Skill skill) => Disabled;
            public override int Mana { get => TestMana; set => TestMana = value; }
        }

        private sealed class Target : GamePlayer
        {
            private Target() : base(null, null) { }
            public eRealm TestRealm = eRealm.Midgard;
            public override bool IsAlive => true;
            public override bool InCombat => false;
            public override eRealm Realm { get => TestRealm; set => TestRealm = value; }
        }

        private static Target NewTarget(eRealm realm)
        {
            var target = (Target)RuntimeHelpers.GetUninitializedObject(typeof(Target));
            target.TestRealm = realm;
            return target;
        }

        /// <summary>
        /// A font whose world lookup and sub-spell are recorded: which livings a pulse reaches and
        /// with which sub-handler. Hostility is the realm, as in RvR.
        /// </summary>
        private interface IRecordingFont
        {
            List<(ISpellHandler handler, GameLiving living)> Applied { get; }
        }

        private sealed class HealFont : FOHSpellHandler, IRecordingFont
        {
            private readonly List<GameLiving> _around;
            public List<(ISpellHandler handler, GameLiving living)> Applied { get; } = new();

            public HealFont(GameLiving caster, Spell spell, List<GameLiving> around) : base(caster, spell, Line)
            {
                _around = around;
                heal ??= NewPulseHandler();
            }

            protected override IEnumerable<GameLiving> Around(GameObject center, ushort radius, bool withNpcs) => _around;
            protected override bool IsEnemy(GameLiving living) => living.Realm != Caster.Realm;
            protected override ISpellHandler NewPulseHandler() => new SpellHandler(Caster, s, Line);
            protected override void Apply(ISpellHandler handler, GameLiving living) => Applied.Add((handler, living));
        }

        private sealed class DamageFont : FODSpellHandler, IRecordingFont
        {
            private readonly List<GameLiving> _around;
            public List<(ISpellHandler handler, GameLiving living)> Applied { get; } = new();

            public DamageFont(GameLiving caster, Spell spell, List<GameLiving> around) : base(caster, spell, Line)
            {
                _around = around;
                heal ??= NewPulseHandler();
            }

            protected override IEnumerable<GameLiving> Around(GameObject center, ushort radius, bool withNpcs) => _around;
            protected override bool IsEnemy(GameLiving living) => living.Realm != Caster.Realm;
            protected override ISpellHandler NewPulseHandler() => new SpellHandler(Caster, s, Line);
            protected override void Apply(ISpellHandler handler, GameLiving living) => Applied.Add((handler, living));
        }

        /// <summary>
        /// A bot as the Master Level step sees it: the brain's six questions, answered by the test,
        /// and a record of every think on which a key was pressed.
        /// </summary>
        private sealed class FakeActor : IBotMasterLevelActor
        {
            public List<Spell> Spells = new();
            public bool Busy;
            public bool Wanted = true;
            public long CooldownUntil;
            public long Now;
            public int Judged;
            public readonly List<long> Presses = new();
            public readonly List<string> Pressed = new();
            public IReadOnlyList<Spell> MasterLevelSpells => Spells;
            public bool IsBusyForMasterLevel => Busy;
            public bool IsMasterLevelReady(Spell spell) => Now >= CooldownUntil;

            public bool WantsMasterLevel(BotMasterLevelUse use, Spell spell, bool combat, out Point3D spot)
            {
                Judged++;
                spot = null;
                return Wanted;
            }

            public bool AimMasterLevel(Spell spell, Point3D spot) => true;

            public bool CastMasterLevel(BotMasterLevelUse use, Spell spell)
            {
                Presses.Add(Now);
                Pressed.Add(spell.SpellType.ToString());
                CooldownUntil = Now + spell.RecastDelay; // the handler's DisableSkill
                return true;
            }
        }

        /// <summary>A placement handler that counts what the effect asks of it.</summary>
        private sealed class CountingPlacement : SpellHandler, IMasterLevelPlacement
        {
            public CountingPlacement(GameLiving caster, Spell spell) : base(caster, spell, Line) { }
            public GameNPC Placed;
            public int Pulses, Removals;
            public GameNPC PlacedObject => Placed;
            public void PulsePlaced(MasterLevelPlacedECSEffect effect) => Pulses++;
            public void RemovePlaced() => Removals++;
        }

        private sealed class TestBattlewarderEffect : BattlewarderECSEffect
        {
            public TestBattlewarderEffect(in ECSGameEffectInitParams initParams) : base(initParams) { }
            protected override bool PlantWarder(GameNPC warder) => true;
        }

        private static void Set(Type type, object instance, string field, object value) =>
            type.GetField(field, Hidden)!.SetValue(instance, value);

        private static Bot NewBot(int x = 0, int y = 0)
        {
            var bot = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
            bot.PosX = x;
            bot.PosY = y;
            Set(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
            Set(typeof(GameLiving), bot, "<GroundTarget>k__BackingField", new GroundTarget());
            Set(typeof(GameNPC), bot, "m_brains", new ArrayList());
            bot.GroundTargetInView = true;
            bot.ObjectState = GameObject.eObjectState.Active;
            return bot;
        }

        private static Spell NewSpell(string type, string target = "Self", int castTime = 0, int range = 0, int recast = 300,
            int duration = 600, int frequency = 30, int value = 15) => new(new DbSpell
        {
            SpellID = 7300 + type.Length,
            Name = type,
            Type = type,
            Target = target,
            CastTime = castTime,
            Range = range,
            RecastDelay = recast,
            Duration = duration,
            Frequency = frequency,
            Value = value,
            ResurrectHealth = 3,
        }, 1);

        private static Point3D P(int x, int y) => new(x, y, 0);

        // ------------------------------------------------------------------ pacing

        [Test]
        public void TheReactionDelayIsOneToThreeSeconds()
        {
            var random = new Random(7);
            int[] delays = Enumerable.Range(0, 2000).Select(_ => BotMasterLevelCasting.ReactionDelay(random)).ToArray();

            Assert.That(delays, Has.All.InRange(1_000, 3_000));
            Assert.That(delays.Min(), Is.LessThan(1_200), "spread over the whole range, not one fixed delay");
            Assert.That(delays.Max(), Is.GreaterThan(2_800));
        }

        [Test]
        public void ADecisionIsPressedOnlyAfterTheReactionTime()
        {
            var plan = new BotMasterLevelPlan();
            Spell storm = NewSpell("EnergyTempest", castTime: 20);

            Assert.That(plan.CanDecide(10_000), Is.True);
            plan.Decide(storm, BotMasterLevelUse.OffensiveStorm, 10_000, new Random(1));

            Assert.That(plan.DueTick, Is.InRange(11_000, 13_000));
            Assert.That(plan.IsDue(10_000), Is.False, "nothing in the same think: no instant press");
            Assert.That(plan.IsDue(plan.DueTick - 1), Is.False);
            Assert.That(plan.IsDue(plan.DueTick), Is.True);
            Assert.That(plan.CanDecide(plan.DueTick), Is.False, "one decision at a time");
        }

        [Test]
        public void APressIsFollowedByAPauseBeforeTheNextDecision()
        {
            var plan = new BotMasterLevelPlan();
            Spell storm = NewSpell("EnergyTempest", castTime: 20);
            plan.Decide(storm, BotMasterLevelUse.OffensiveStorm, 0, new Random(2));
            long pressed = plan.DueTick;
            plan.Pressed(pressed, new Random(3));

            Assert.That(plan.IsPending, Is.False);
            Assert.That(plan.CanDecide(pressed + storm.CastTime + BotMasterLevelCasting.GAP_MIN_MS - 1), Is.False,
                "no chain-casting: the cast time and a pause of at least 8 seconds");
            Assert.That(plan.CanDecide(pressed + storm.CastTime + BotMasterLevelCasting.GAP_MAX_MS), Is.True);
        }

        [Test]
        public void AMomentMissedWhileBusyIsLetGo()
        {
            var plan = new BotMasterLevelPlan();
            plan.Decide(NewSpell("Battlewarder", castTime: 1), BotMasterLevelUse.Battlewarder, 0, new Random(4));

            Assert.That(plan.IsStale(plan.DueTick + BotMasterLevelCasting.PRESS_WINDOW_MS), Is.False);
            Assert.That(plan.IsStale(plan.DueTick + BotMasterLevelCasting.PRESS_WINDOW_MS + 1), Is.True);

            plan.Drop(50_000);
            Assert.That(plan.IsPending, Is.False);
            Assert.That(plan.CanDecide(50_000 + BotMasterLevelCasting.DROPPED_GAP_MS - 1), Is.False);
            Assert.That(plan.CanDecide(50_000 + BotMasterLevelCasting.DROPPED_GAP_MS), Is.True);
        }

        [Test]
        public void CooldownAndPowerGateEveryPress()
        {
            var caster = new Caster { TestMana = 100 };
            Spell trap = NewSpell("PowerTrap");

            Assert.That(BotMasterLevelCasting.IsReady(caster, trap, 30), Is.True);

            caster.Disabled = 120_000;
            Assert.That(BotMasterLevelCasting.IsReady(caster, trap, 30), Is.False, "on cooldown");

            caster.Disabled = 0;
            caster.TestMana = 10;
            Assert.That(BotMasterLevelCasting.IsReady(caster, trap, 30), Is.False, "not enough power");
        }

        [Test]
        public void TheConvokerStormlordAndSpymasterAbilitiesHaveAUseAndHumanChances()
        {
            var uses = new Dictionary<string, BotMasterLevelUse>
            {
                ["Battlewarder"] = BotMasterLevelUse.Battlewarder,
                ["SummonTitan"] = BotMasterLevelUse.SummonTitan,
                ["PowerTrap"] = BotMasterLevelUse.HoldTrap,
                ["DissonanceTrap"] = BotMasterLevelUse.HoldTrap,
                ["TangleSnare"] = BotMasterLevelUse.HoldTrap,
                ["PoisonSpike"] = BotMasterLevelUse.HoldTrap,
                ["SiegeWrecker"] = BotMasterLevelUse.SiegeTrap,
                ["DazzlingArray"] = BotMasterLevelUse.DefensiveStorm,
                ["VacuumVortex"] = BotMasterLevelUse.SteerStorm,
                ["EnervatingGas"] = BotMasterLevelUse.OffensiveStorm,
                ["InebriatingFumes"] = BotMasterLevelUse.OffensiveStorm,
                ["MentalSiphon"] = BotMasterLevelUse.OffensiveStorm,
                ["ChokingVapors"] = BotMasterLevelUse.OffensiveStorm,
                ["SenseDullingCloud"] = BotMasterLevelUse.OffensiveStorm,
                ["EnergyTempest"] = BotMasterLevelUse.OffensiveStorm,
            };

            foreach ((string type, BotMasterLevelUse use) in uses)
            {
                Assert.That(BotMasterLevels.UseOf(NewSpell(type)), Is.EqualTo(use), type);
                Assert.That(BotMasterLevels.ReactionChance(use), Is.InRange(20, 60), type + ": noticed often, never every think");
            }

            foreach (string held in new[] { "PrescienceNode", "SpeedWrapWard", "BrittleGuard", "SummonMastery", "SummonWood",
                         "SummonWarcrystal", "FocusingWinds", "TargetModifier", "Decoy", "Loockout", "EssenceFlare", "BlanketOfCamouflage" })
                Assert.That(BotMasterLevels.UseOf(NewSpell(held)), Is.EqualTo(BotMasterLevelUse.None), held);

            Assert.That(BotMasterLevels.WorksIn(BotMasterLevelUse.HoldTrap, combat: true), Is.False, "traps are laid before a fight");
            Assert.That(BotMasterLevels.WorksIn(BotMasterLevelUse.HoldTrap, combat: false), Is.True);
            Assert.That(BotMasterLevels.WorksIn(BotMasterLevelUse.Battlewarder, combat: false), Is.False, "the warder answers a melee");
            Assert.That(BotMasterLevels.WorksIn(BotMasterLevelUse.FightBuff, combat: false), Is.False);
            Assert.That(BotMasterLevels.WorksIn(BotMasterLevelUse.OffensiveStorm, combat: false), Is.True);
            Assert.That(BotMasterLevels.WorksIn(BotMasterLevelUse.OffensiveStorm, combat: true), Is.True);
        }

        // ------------------------------------------------------------------ placement

        [Test]
        public void TheBattleWarderStandsAtTheDefendedMatesFeetTowardTheAttacker()
        {
            Point3D spot = BotMasterLevelCasting.WarderSpot(P(1_000, 1_000), P(1_200, 1_000));
            Assert.That((spot.X, spot.Y), Is.EqualTo((1_060, 1_000)));

            Point3D close = BotMasterLevelCasting.WarderSpot(P(0, 0), P(0, 30));
            Assert.That((close.X, close.Y), Is.EqualTo((0, 30)), "never past the attacker");
        }

        [Test]
        public void TheVortexPointSitsBehindTheStormSoThePushCarriesItIntoTheEnemies()
        {
            Point3D storm = P(1_000, 0);
            Point3D cluster = P(1_600, 0);
            Point3D vortex = BotMasterLevelCasting.SteerSpot(storm, cluster);

            Assert.That((vortex.X, vortex.Y), Is.EqualTo((850, 0)), "150 behind, on the far side from the enemies");

            Point3D pushed = VacuumVortexSpellHandler.PushedTo(storm, vortex, 500, 0);
            Assert.That((pushed.X, pushed.Y), Is.EqualTo((1_500, 0)), "pushed directly away from the point: into the cluster");
            Assert.That(BotMasterLevelCasting.Within(pushed, cluster, BotMasterLevelCasting.STORM_RADIUS), Is.True);
        }

        [Test]
        public void TheDensestEnemyGroupIsTheTarget()
        {
            var enemies = new List<Point3D> { P(0, 0), P(100, 0), P(0, 100), P(2_000, 0), P(2_050, 0) };
            BotMasterLevelCasting.Cluster? cluster = BotMasterLevelCasting.BestCluster(enemies, 350, 2);

            Assert.That(cluster, Is.Not.Null);
            Assert.That(cluster.Value.Count, Is.EqualTo(3));
            Assert.That((cluster.Value.Centre.X, cluster.Value.Centre.Y), Is.EqualTo((33, 33)));

            Assert.That(BotMasterLevelCasting.BestCluster(new List<Point3D> { P(0, 0), P(1_000, 0) }, 350, 2), Is.Null,
                "two enemies far apart are no cluster");
            Assert.That(BotMasterLevelCasting.BestCluster(new List<Point3D>(), 350, 2), Is.Null);
        }

        [Test]
        public void TheAimIsUsuallyExactAndSometimesALittleOff()
        {
            var random = new Random(11);
            Point3D spot = P(5_000, 5_000);
            int off = 0;

            for (int i = 0; i < 4_000; i++)
            {
                Point3D aimed = BotMasterLevelCasting.Aim(spot, random);
                double distance = Math.Sqrt(BotMasterLevelCasting.DistanceSquared(aimed, spot));

                if (distance < 1)
                    continue;

                off++;
                Assert.That(distance, Is.InRange(BotMasterLevelCasting.MISTAKE_MIN - 1, BotMasterLevelCasting.MISTAKE_MAX + 1));
            }

            Assert.That(off / 4_000.0, Is.InRange(0.15, 0.25), "about one press in five lands a little off");
        }

        // ------------------------------------------------------------------ pointless presses

        [Test]
        public void AStormBeforeContactNeedsAHeldSpotEnemiesInSightAndNoneClose()
        {
            Assert.That(BotMasterLevelCasting.WantsStormBeforeContact(true, 3, 1_500, false, 2), Is.True);
            Assert.That(BotMasterLevelCasting.WantsStormBeforeContact(false, 3, 1_500, false, 2), Is.False, "the bot is walking");
            Assert.That(BotMasterLevelCasting.WantsStormBeforeContact(true, 3, 500, false, 2), Is.False, "a 20 second cast would be interrupted");
            Assert.That(BotMasterLevelCasting.WantsStormBeforeContact(true, 1, 1_500, false, 2), Is.False, "one enemy is no group");
            Assert.That(BotMasterLevelCasting.WantsStormBeforeContact(true, 0, int.MaxValue, false, 1), Is.False, "nobody coming");
            Assert.That(BotMasterLevelCasting.WantsStormBeforeContact(true, 3, 1_500, true, 2), Is.False, "the same storm is already there");
        }

        [Test]
        public void AStormInAFightNeedsEnemiesAtTheBotsFeetAndNobodyHittingIt()
        {
            var atFeet = new BotMasterLevelCasting.Cluster(P(100, 0), 3);
            var far = new BotMasterLevelCasting.Cluster(P(900, 0), 3);
            var single = new BotMasterLevelCasting.Cluster(P(0, 0), 1);

            Assert.That(BotMasterLevelCasting.WantsStormInFight(atFeet, P(0, 0), false, false), Is.True);
            Assert.That(BotMasterLevelCasting.WantsStormInFight(far, P(0, 0), false, false), Is.False, "the storm appears at the bot's feet");
            Assert.That(BotMasterLevelCasting.WantsStormInFight(atFeet, P(0, 0), true, false), Is.False, "the bot is being hit");
            Assert.That(BotMasterLevelCasting.WantsStormInFight(single, P(0, 0), false, false), Is.False);
            Assert.That(BotMasterLevelCasting.WantsStormInFight(null, P(0, 0), false, false), Is.False, "no enemies");
            Assert.That(BotMasterLevelCasting.WantsDefensiveStormInFight(2, false, false), Is.True);
            Assert.That(BotMasterLevelCasting.WantsDefensiveStormInFight(1, false, false), Is.False);
            Assert.That(BotMasterLevelCasting.WantsDefensiveStormInFight(3, false, true), Is.False, "one Dazzling Array per spot");
        }

        [Test]
        public void AStormIsSteeredOnlyTowardANearClusterItDoesNotCoverYet()
        {
            Point3D storm = P(0, 0);

            Assert.That(BotMasterLevelCasting.WorthSteering(storm, new BotMasterLevelCasting.Cluster(P(600, 0), 2)), Is.True);
            Assert.That(BotMasterLevelCasting.WorthSteering(storm, new BotMasterLevelCasting.Cluster(P(200, 0), 4)), Is.False, "already inside");
            Assert.That(BotMasterLevelCasting.WorthSteering(storm, new BotMasterLevelCasting.Cluster(P(2_000, 0), 4)), Is.False, "too far");
            Assert.That(BotMasterLevelCasting.WorthSteering(storm, null), Is.False);
        }

        [Test]
        public void ATrapIsLaidOnlyOnAHeldRvrSpotWithoutOneAlready()
        {
            Assert.That(BotMasterLevelCasting.WantsTrap(true, true, false), Is.True);
            Assert.That(BotMasterLevelCasting.WantsTrap(false, true, false), Is.False);
            Assert.That(BotMasterLevelCasting.WantsTrap(true, false, false), Is.False, "no traps in PvE");
            Assert.That(BotMasterLevelCasting.WantsTrap(true, true, true), Is.False, "one trap of its own there is enough");
        }

        // ------------------------------------------------------------------ the step loop

        /// <summary>Runs the step every 500 ms for <paramref name="seconds"/>; returns how many thinks pressed.</summary>
        private static int Run(BotMasterLevelStep step, FakeActor actor, int seconds, bool combat = true, long from = 0)
        {
            int pressingThinks = 0;

            for (long now = from; now < from + seconds * 1_000L; now += 500)
            {
                actor.Now = now;
                int before = actor.Presses.Count;
                bool pressed = step.Think(actor, combat, now);

                Assert.That(actor.Presses.Count - before, Is.LessThanOrEqualTo(1), "at most one ability per think");
                Assert.That(pressed, Is.EqualTo(actor.Presses.Count > before));

                if (pressed)
                    pressingThinks++;
            }

            return pressingThinks;
        }

        [Test]
        public void TheStepPressesOncePerThinkAfterTheReactionTimeAndPausesBetweenPresses()
        {
            var actor = new FakeActor { Spells = { NewSpell("EnergyTempest", recast: 0), NewSpell("VacuumVortex", recast: 0), NewSpell("DazzlingArray", recast: 0) } };
            var step = new BotMasterLevelStep(new Random(5), notices: _ => true);

            Run(step, actor, 120);

            Assert.That(actor.Presses, Is.Not.Empty);
            Assert.That(actor.Presses[0], Is.GreaterThanOrEqualTo(BotMasterLevelCasting.REACTION_MIN_MS), "never on the think that noticed it");

            for (int i = 1; i < actor.Presses.Count; i++)
                Assert.That(actor.Presses[i] - actor.Presses[i - 1], Is.GreaterThanOrEqualTo(BotMasterLevelCasting.GAP_MIN_MS + BotMasterLevelCasting.REACTION_MIN_MS),
                    "no chain-casting: pause, then a fresh reaction time");

            Assert.That(actor.Presses.Count, Is.LessThanOrEqualTo(120_000 / (BotMasterLevelCasting.GAP_MIN_MS + BotMasterLevelCasting.REACTION_MIN_MS) + 1));
        }

        [Test]
        public void TheStepNeverPressesWhileTheBotCastsOrTheAbilityIsOnCooldown()
        {
            var actor = new FakeActor { Spells = { NewSpell("PowerTrap", recast: 300) }, Busy = true };
            var step = new BotMasterLevelStep(new Random(6), notices: _ => true);

            Assert.That(Run(step, actor, 30, combat: false), Is.Zero, "casting or a cast queued: the key is not pressed");
            Assert.That(actor.Judged, Is.Zero, "and nothing is even judged");

            actor.Busy = false;
            Run(step, actor, 600, combat: false, from: 30_000);

            Assert.That(actor.Presses, Has.Count.EqualTo(2), "a 5-minute recast allows two presses in ten minutes");
            Assert.That(actor.Presses[1] - actor.Presses[0], Is.GreaterThanOrEqualTo(300_000));
        }

        [Test]
        public void APointlessAbilityIsJudgedNowAndThenNotEveryThink()
        {
            var actor = new FakeActor { Spells = { NewSpell("EnergyTempest") }, Wanted = false };
            var step = new BotMasterLevelStep(new Random(8), notices: _ => true);

            Assert.That(Run(step, actor, 60), Is.Zero, "pointless: never pressed");
            Assert.That(actor.Judged, Is.LessThanOrEqualTo(60_000 / BotMasterLevelCasting.NOT_NOW_MIN_MS + 1),
                "after a no it rests 4-8 seconds; 120 thinks, at most 16 judgements");
            Assert.That(actor.Judged, Is.GreaterThanOrEqualTo(60_000 / (BotMasterLevelCasting.NOT_NOW_MAX_MS + 500)));
        }

        [Test]
        public void ATrapIsNeverJudgedInAFightAndAWarderNeverOutOfOne()
        {
            var actor = new FakeActor { Spells = { NewSpell("PowerTrap"), NewSpell("Battlewarder", range: 2_300) } };
            var step = new BotMasterLevelStep(new Random(9), notices: _ => true);

            Run(step, actor, 10, combat: true);
            Assert.That(actor.Pressed, Is.EqualTo(new[] { "Battlewarder" }), "in a fight only the warder, once (5-minute recast)");

            var idle = new FakeActor { Spells = { NewSpell("Battlewarder", range: 2_300) } };
            Assert.That(Run(new BotMasterLevelStep(new Random(10), notices: _ => true), idle, 30, combat: false), Is.Zero);
            Assert.That(idle.Judged, Is.Zero);
        }

        // ------------------------------------------------------------------ fonts

        [Test]
        public void AHealingFontReachesEveryAllyInRangeOnOnePulse()
        {
            Bot bot = NewBot();
            Target first = NewTarget(eRealm.Albion), second = NewTarget(eRealm.Albion), enemy = NewTarget(eRealm.Midgard);
            var font = new HealFont(bot, NewSpell("FOH"), new List<GameLiving> { first, enemy, second });

            font.PulsePlaced(null);

            Assert.That(font.Applied.Select(a => a.living), Is.EqualTo(new GameLiving[] { first, second }), "both allies, not the enemy");
            Assert.That(font.Applied[0].handler, Is.Not.SameAs(font.Applied[1].handler),
                "one sub-handler each: a shared one keeps its first target (SpellHandler.StartSpell)");
        }

        [Test]
        public void AHostileFontReachesTheEnemyAndSkipsTheAlly()
        {
            Bot bot = NewBot();
            Target ally = NewTarget(eRealm.Albion), enemy = NewTarget(eRealm.Hibernia);
            var dissonating = new DamageFont(bot, NewSpell("FOD"), new List<GameLiving> { ally, enemy });

            dissonating.PulsePlaced(null);

            Assert.That(dissonating.Applied.Select(a => a.living), Is.EqualTo(new GameLiving[] { enemy }));
        }


        [Test]
        public void FontsTrapsAndStormsTakeABotAsTheirOwner()
        {
            Bot bot = NewBot(400, 300);

            var tempest = new EnergyTempestSpellHandler(bot, NewSpell("EnergyTempest"), Line);
            var dazzling = new DazzlingArraySpellHandler(bot, NewSpell("DazzlingArray"), Line);
            var trap = new PowerTrapSpellHandler(bot, NewSpell("PowerTrap"), Line);
            var snare = new TangleSnareSpellHandler(bot, NewSpell("TangleSnare"), Line);
            var font = new PrescienceNodeSpellHandler(bot, NewSpell("PrescienceNode"), Line);

            Assert.That(((GameStorm)tempest.PlacedObject).Owner, Is.SameAs(bot), "GameStorm took a GamePlayer");
            Assert.That(((GameStorm)dazzling.PlacedObject).Owner, Is.SameAs(bot));
            Assert.That(((GameMine)trap.PlacedObject).Owner, Is.SameAs(bot));
            Assert.That(((GameMine)snare.PlacedObject).Owner, Is.SameAs(bot));
            Assert.That(((GameFont)font.PlacedObject).Owner, Is.SameAs(bot), "GameFont took a GamePlayer");
            Assert.That((tempest.PlacedObject.RealX, tempest.PlacedObject.RealY), Is.EqualTo((400, 300)), "at the caster's feet, as for players (spawn point, read by AddToWorld)");
            Assert.That(dazzling.Friendly, Is.True);
            Assert.That(tempest.Friendly, Is.False);
        }

        [Test]
        public void APlacedObjectPulsesWhileItStandsAndGoesWhenItsEffectEnds()
        {
            Bot bot = NewBot();
            var storm = new GameStorm { Name = "Energy Tempest" };
            storm.ObjectState = GameObject.eObjectState.Active;
            var handler = new CountingPlacement(bot, NewSpell("EnergyTempest")) { Placed = storm };
            var effect = new MasterLevelPlacedECSEffect(new ECSGameEffectInitParams(storm, 600_000, 1, handler));

            Assert.That(effect.PulseFreq, Is.EqualTo(3_000), "pulses at the spell's frequency");
            effect.OnEffectPulse();
            Assert.That(handler.Pulses, Is.EqualTo(1));

            storm.ObjectState = GameObject.eObjectState.Deleted;
            effect.OnEffectPulse();
            Assert.That(handler.Pulses, Is.EqualTo(1), "a storm that is gone pulses no more");

            effect.OnStopEffect();
            Assert.That(handler.Removals, Is.EqualTo(1), "the end of the effect removes the object");
        }

        [Test]
        public void TheRealHandlersRemoveTheirObjectAtTheEnd()
        {
            Bot bot = NewBot();
            var tempest = new EnergyTempestSpellHandler(bot, NewSpell("EnergyTempest"), Line);
            var trap = new DissonanceTrapSpellHandler(bot, NewSpell("DissonanceTrap"), Line);

            tempest.RemovePlaced();
            trap.RemovePlaced();

            Assert.That(tempest.PlacedObject.ObjectState, Is.EqualTo(GameObject.eObjectState.Deleted));
            Assert.That(trap.PlacedObject.ObjectState, Is.EqualTo(GameObject.eObjectState.Deleted));
        }

        [Test]
        public void AStormDebuffIsAppliedWhileInsideAndRemovedAfter()
        {
            var target = (Target)RuntimeHelpers.GetUninitializedObject(typeof(Target));
            Set(typeof(GameLiving), target, "<DebuffCategory>k__BackingField", new PropertyIndexer());
            Bot bot = NewBot();
            var debuff = new StormDexQuickDebuff(bot, NewSpell("StormDexQuickDebuff", "Enemy", value: 15), Line);
            var effect = new MasterLevelStormStatECSEffect(new ECSGameEffectInitParams(target, 3_000, 1, debuff));

            effect.OnStartEffect();
            Assert.That(target.DebuffCategory[eProperty.Dexterity], Is.EqualTo(-15));
            Assert.That(target.DebuffCategory[eProperty.Quickness], Is.EqualTo(-15));

            effect.OnStopEffect();
            Assert.That(target.DebuffCategory[eProperty.Dexterity], Is.Zero, "the legacy path never took it back");
            Assert.That(target.DebuffCategory[eProperty.Quickness], Is.Zero);
        }

        [Test]
        public void TheCrystalTitanGrowsThenFightsAndLeavesWhenItsTimeIsUp()
        {
            var titan = new GameNPC { Size = 10 };
            titan.Flags |= GameNPC.eFlags.PEACE;
            titan.ObjectState = GameObject.eObjectState.Active;
            Bot bot = NewBot();
            var handler = new Convoker10SpellHandler(bot, NewSpell("SummonTitan", duration: 300), Line);
            ECSGameEffectInitParams init = CrystalTitanECSEffect.ParamsFor(titan, handler, 1);
            var effect = new CrystalTitanECSEffect(init);

            Assert.That(init.Duration, Is.EqualTo(300_000), "the spell's 300 seconds, in milliseconds");
            Assert.That(effect.ExpireTick - effect.StartTick, Is.EqualTo(300_000), "the effect service ends it after 5 minutes");

            var sizes = new List<int>();
            while (effect.GrowStep())
            {
                sizes.Add(titan.Size);
                Assert.That(titan.Flags.HasFlag(GameNPC.eFlags.PEACE), Is.True, "peaceful while it grows");
            }

            Assert.That(sizes, Is.EqualTo(new[] { 20, 30, 40, 50 }));
            Assert.That(titan.Size, Is.EqualTo(CrystalTitanECSEffect.FullSize));
            Assert.That(titan.Flags.HasFlag(GameNPC.eFlags.PEACE), Is.False, "fights once grown");
            Assert.That(effect.GrowStep(), Is.False, "fully grown: the grow timer stops");

            effect.OnStopEffect();
            Assert.That(titan.ObjectState, Is.EqualTo(GameObject.eObjectState.Deleted), "gone when the effect ends");
        }

        [Test]
        public void ABotPlantsTheBattleWarderAtItsGroundTargetWithinRange()
        {
            Bot bot = NewBot();
            Spell spell = NewSpell("Battlewarder", castTime: 1, range: 2_300);

            bot.SetGroundTarget(2_500, 0, 0);
            Assert.That(BattlewarderSpellHandler.InReach(bot, spell), Is.False, "2,500 is beyond 2,300");

            bot.SetGroundTarget(600, 200, 0);
            Assert.That(BattlewarderSpellHandler.InReach(bot, spell), Is.True);

            var handler = new BattlewarderSpellHandler(bot, spell, Line);
            var effect = new TestBattlewarderEffect(new ECSGameEffectInitParams(bot, 600_000, 1, handler));
            effect.OnStartEffect();

            try
            {
                Assert.That(effect.Warder, Is.InstanceOf<BattleWarderNpc>(), "a bot caster plants a warder too");
                Assert.That((effect.Warder.RealX, effect.Warder.RealY), Is.EqualTo((600, 200)));

                bot.Moving = true;
                Assert.That(effect.Watch.Check(), Is.False, "it holds only while the caster stands");
            }
            finally
            {
                effect.OnStopEffect();
            }
        }
    }
}
