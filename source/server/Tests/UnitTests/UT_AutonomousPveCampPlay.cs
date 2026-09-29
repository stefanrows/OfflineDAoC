using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_AutonomousPveCampPlay
    {
        [TestCase(eCharacterClass.Necromancer, false, ConColor.ORANGE, ConColor.ORANGE)]
        [TestCase(eCharacterClass.Spiritmaster, false, ConColor.YELLOW, ConColor.YELLOW)]
        [TestCase(eCharacterClass.Wizard, false, ConColor.ORANGE, ConColor.YELLOW)]
        [TestCase(eCharacterClass.Wizard, true, ConColor.ORANGE, ConColor.ORANGE)]
        [TestCase(eCharacterClass.Armsman, false, ConColor.ORANGE, ConColor.YELLOW)]
        [TestCase(eCharacterClass.Paladin, true, ConColor.YELLOW, ConColor.YELLOW)]
        [TestCase(eCharacterClass.Nightshade, false, ConColor.YELLOW, ConColor.BLUE)]
        [TestCase(eCharacterClass.Cleric, false, ConColor.YELLOW, ConColor.BLUE)]
        [TestCase(eCharacterClass.Theurgist, false, ConColor.GREEN, ConColor.GREEN)]
        public void PreferredConFollowsArchetypeWithinCeiling(eCharacterClass characterClass, bool hasRoot,
            ConColor ceiling, ConColor expected) =>
            Assert.That(AutonomousPveArchetype.PreferredCon(characterClass, 25, hasRoot, ceiling), Is.EqualTo(expected));

        [Test]
        public void LowLevelPetCasterStartsAtYellow() =>
            Assert.That(AutonomousPveArchetype.PreferredCon(eCharacterClass.Necromancer, 3, false, ConColor.ORANGE),
                Is.EqualTo(ConColor.YELLOW));

        [TestCase(eCharacterClass.Bonedancer, false, ConColor.ORANGE)]
        [TestCase(eCharacterClass.Enchanter, false, ConColor.ORANGE)]
        [TestCase(eCharacterClass.Runemaster, false, ConColor.YELLOW)]
        [TestCase(eCharacterClass.Runemaster, true, ConColor.ORANGE)]
        [TestCase(eCharacterClass.Warrior, true, ConColor.YELLOW)]
        [TestCase(eCharacterClass.Shadowblade, false, ConColor.YELLOW)]
        public void SoloCeilingIsOrangeOnlyForPetsAndRootCasters(eCharacterClass characterClass, bool hasRoot,
            ConColor expected) =>
            Assert.That(AutonomousPveArchetype.NaturalSoloCeiling(characterClass, hasRoot, 25), Is.EqualTo(expected));

        [TestCase(1)] [TestCase(4)]
        public void SoloCeilingIsYellowBelowLevelFive(int level)
        {
            Assert.That(AutonomousPveArchetype.NaturalSoloCeiling(eCharacterClass.Necromancer, false, level), Is.EqualTo(ConColor.YELLOW));
            Assert.That(AutonomousPveArchetype.NaturalSoloCeiling(eCharacterClass.Wizard, true, level), Is.EqualTo(ConColor.YELLOW));
            Assert.That(AutonomousPveArchetype.NaturalSoloCeiling(eCharacterClass.Necromancer, false, 5), Is.EqualTo(ConColor.ORANGE));
        }

        [Test]
        public void CampWeightFavoursPreferredThenOneEasier()
        {
            Assert.That(AutonomousPveArchetype.ConWeight(ConColor.ORANGE, ConColor.ORANGE), Is.EqualTo(3));
            Assert.That(AutonomousPveArchetype.ConWeight(ConColor.YELLOW, ConColor.ORANGE), Is.EqualTo(2));
            Assert.That(AutonomousPveArchetype.ConWeight(ConColor.GREEN, ConColor.ORANGE), Is.EqualTo(1));
            Assert.That(AutonomousBotDecisionEngine.SoloConWeight(ConColor.BLUE, ConColor.BLUE), Is.EqualTo(3));
            Assert.That(AutonomousBotDecisionEngine.SoloConWeight(ConColor.BLUE, null),
                Is.EqualTo(AutonomousBotDecisionEngine.SoloConWeight(ConColor.BLUE)));
        }

        [Test]
        public void RestThresholdsByArchetypeWithoutJitter()
        {
            const long noJitter = 5; // 5 % 11 - 5 == 0
            Assert.That(AutonomousPveArchetype.Jitter(noJitter), Is.Zero);
            Assert.That(AutonomousPveArchetype.RestThresholds(eCharacterClass.Wizard, noJitter), Is.EqualTo(new PveRestThresholds(60, 75, 0)));
            Assert.That(AutonomousPveArchetype.RestThresholds(eCharacterClass.Healer, noJitter), Is.EqualTo(new PveRestThresholds(60, 75, 0)));
            Assert.That(AutonomousPveArchetype.RestThresholds(eCharacterClass.Necromancer, noJitter), Is.EqualTo(new PveRestThresholds(60, 75, 0)));
            Assert.That(AutonomousPveArchetype.RestThresholds(eCharacterClass.Berserker, noJitter), Is.EqualTo(new PveRestThresholds(80, 0, 50)));
            Assert.That(AutonomousPveArchetype.RestThresholds(eCharacterClass.Paladin, noJitter), Is.EqualTo(new PveRestThresholds(80, 50, 50)));
        }

        [Test]
        public void RestJitterStaysWithinFivePoints()
        {
            int[] jitters = Enumerable.Range(0, 200).Select(seed => AutonomousPveArchetype.Jitter(seed * 7919L)).ToArray();
            Assert.That(jitters.Min(), Is.EqualTo(-5));
            Assert.That(jitters.Max(), Is.EqualTo(5));
            PveRestThresholds low = AutonomousPveArchetype.RestThresholds(eCharacterClass.Wizard, 0);
            Assert.That(low, Is.EqualTo(new PveRestThresholds(55, 70, 0)));
        }

        [Test]
        public void CasterPullsAtSeventyFivePowerNotFull()
        {
            var caster = new PveRestThresholds(60, 75, 0);
            Assert.That(AutonomousPveArchetype.ReadyToPull(caster, 61, 76, 10, true), Is.True);
            Assert.That(AutonomousPveArchetype.ReadyToPull(caster, 100, 74, 100, true), Is.False);
            Assert.That(AutonomousPveArchetype.ReadyToPull(caster, 59, 100, 100, true), Is.False);
            var melee = new PveRestThresholds(80, 0, 50);
            Assert.That(AutonomousPveArchetype.ReadyToPull(melee, 80, 0, 50, false), Is.True);
            Assert.That(AutonomousPveArchetype.ReadyToPull(melee, 79, 0, 100, false), Is.False);
            Assert.That(AutonomousPveArchetype.ReadyToPull(melee, 100, 0, 49, false), Is.False);
        }

        private static PvePullPlan Plan(params (eCharacterClass, int)[] members) =>
            AutonomousPvePullStyle.For(members);

        [Test]
        public void MezzerMakesAMezGroupPullingOneAtATime()
        {
            PvePullPlan plan = Plan((eCharacterClass.Armsman, 30), (eCharacterClass.Cleric, 30),
                (eCharacterClass.Sorcerer, 30), (eCharacterClass.Wizard, 30));
            Assert.That(plan.Style, Is.EqualTo(PvePullStyle.MezGroup));
            Assert.That(plan.StyleLabel, Is.EqualTo("mez_group"));
            Assert.That(plan.Mezzer, Is.True);
            Assert.That(plan.MaxPull, Is.EqualTo(1));
        }

        [Test]
        public void OnlyPetsMakeAMassPullSizedByPetCount()
        {
            PvePullPlan pets = Plan((eCharacterClass.Necromancer, 15), (eCharacterClass.Cabalist, 15),
                (eCharacterClass.Friar, 15));
            Assert.That((pets.Style, pets.StyleLabel, pets.Pets, pets.MaxPull),
                Is.EqualTo((PvePullStyle.MassPull, "mass_pull", 2, 4)));
            Assert.That(AutonomousPvePullStyle.MassPullSize(4), Is.EqualTo(6));
            Assert.That(AutonomousPvePullStyle.MassPullSize(7), Is.EqualTo(6));
        }

        [Test]
        public void BombersAndAreaStunDoNotSizeAPullYet()
        {
            // Autonomous PvE bots do not bomb or area stun; a lone SM is one pet.
            PvePullPlan midgard = Plan((eCharacterClass.Warrior, 30), (eCharacterClass.Healer, 30),
                (eCharacterClass.Spiritmaster, 30), (eCharacterClass.Runemaster, 30));
            Assert.That((midgard.Style, midgard.Bombers, midgard.Pets, midgard.MaxPull),
                Is.EqualTo((PvePullStyle.Single, 2, 1, 1)));
            PvePullPlan wizard = Plan((eCharacterClass.Armsman, 40), (eCharacterClass.Wizard, 40));
            Assert.That(wizard.Style, Is.EqualTo(PvePullStyle.Single));
        }

        [Test]
        public void EverybodyElsePullsSingles()
        {
            Assert.That(Plan((eCharacterClass.Hero, 30), (eCharacterClass.Druid, 30)).Style, Is.EqualTo(PvePullStyle.Single));
            PvePullPlan young = Plan((eCharacterClass.Hero, 12), (eCharacterClass.Eldritch, 12));
            Assert.That((young.StyleLabel, young.Bombers, young.MaxPull), Is.EqualTo(("single", 0, 1)));
        }

        [Test]
        public void RivalThatWasFirstMakesUsLeaveAfterThreeMinutes()
        {
            var watch = new AutonomousPveCampWatch("camp", 20, 0);
            for (long t = 10_000; t < 190_000; t += 10_000)
                Assert.That(watch.ObserveRival(t, true), Is.False, $"t={t}");
            Assert.That(watch.ObserveRival(190_000, true), Is.True);
        }

        [Test]
        public void LaterArrivalYieldsSoTheFirstPartyStays()
        {
            // Nobody fighting in our first 30 s: we were first; a party that
            // turns up later never makes us leave (it leaves instead).
            var first = new AutonomousPveCampWatch("camp", 20, 0);
            Assert.That(first.ObserveRival(10_000, false), Is.False);
            Assert.That(first.ObserveRival(25_000, false), Is.False);
            for (long t = 40_000; t <= 600_000; t += 10_000)
                Assert.That(first.ObserveRival(t, true), Is.False, $"t={t}");
        }

        [Test]
        public void RivalGapResetsTheClock()
        {
            var watch = new AutonomousPveCampWatch("camp", 20, 0);
            watch.ObserveRival(10_000, true);
            watch.ObserveRival(100_000, true);
            Assert.That(watch.ObserveRival(150_000, false), Is.False); // 50 s unseen
            Assert.That(watch.ObserveRival(190_000, true), Is.False);
            Assert.That(watch.ObserveRival(370_000, true), Is.True);
        }

        [Test]
        public void EnemyShiftHoldThenLeaveAfterNinetySecondsPresent()
        {
            var watch = new AutonomousPveCampWatch("camp", 20, 0);
            Assert.That(watch.ObserveEnemy(0, true, true), Is.EqualTo(PveEnemyDecision.None), "never mid-fight");
            Assert.That(watch.ObserveEnemy(10_000, true, false), Is.EqualTo(PveEnemyDecision.Shift));
            Assert.That(watch.EnemyHold, Is.True);
            for (long t = 20_000; t < 100_000; t += 10_000)
                Assert.That(watch.ObserveEnemy(t, true, false), Is.EqualTo(PveEnemyDecision.Hold), $"t={t}");
            Assert.That(watch.ObserveEnemy(100_000, true, false), Is.EqualTo(PveEnemyDecision.Leave));
        }

        [Test]
        public void TimeTheEnemyWasAwayDoesNotCount()
        {
            var watch = new AutonomousPveCampWatch("camp", 20, 0);
            watch.ObserveEnemy(0, true, false);
            watch.ObserveEnemy(10_000, true, false);            // 10 s present
            watch.ObserveEnemy(25_000, false, false);           // away
            Assert.That(watch.ObserveEnemy(35_000, true, false), Is.EqualTo(PveEnemyDecision.Hold));
            // 35 s since last seen: the gap is not added; 80 s more present are needed.
            for (long t = 45_000; t < 115_000; t += 10_000)
                Assert.That(watch.ObserveEnemy(t, true, false), Is.Not.EqualTo(PveEnemyDecision.Leave), $"t={t}");
        }

        [Test]
        public void EnemyGoneTwentySecondsClearsTheHold()
        {
            var watch = new AutonomousPveCampWatch("camp", 20, 0);
            watch.ObserveEnemy(0, true, false);
            Assert.That(watch.ObserveEnemy(10_000, false, false), Is.EqualTo(PveEnemyDecision.Hold));
            Assert.That(watch.ObserveEnemy(20_000, false, false), Is.EqualTo(PveEnemyDecision.Clear));
            Assert.That(watch.EnemyHold, Is.False);
            Assert.That(watch.ObserveEnemy(30_000, false, false), Is.EqualTo(PveEnemyDecision.None));
        }

        [Test]
        public void OutgrownOnlyAfterLevelling()
        {
            // No levels gained since arrival: the camp is kept whatever its con.
            Assert.That(AutonomousPveCampWatch.IsOutgrown(10, 20, 20, 20, 0), Is.False);
            // Grey to the highest member.
            Assert.That(AutonomousPveCampWatch.IsOutgrown(10, 18, 20, 20, 0), Is.True);
            // Still yellow after one level.
            Assert.That(AutonomousPveCampWatch.IsOutgrown(21, 20, 21, 21, 0), Is.False);
            int campLevel = 30;
            int average = Enumerable.Range(30, 20).First(level =>
                ConLevels.GetConColor(ConLevels.GetConLevel(level, campLevel)) == ConColor.GREEN);
            Assert.That(AutonomousPveCampWatch.IsOutgrown(campLevel, 30, average, average, 0), Is.True);
            // A wiped party keeps a green camp.
            Assert.That(AutonomousPveCampWatch.IsOutgrown(campLevel, 30, average, average, 1), Is.False);
        }

        [Test]
        public void ShiftIsThreeToSixHundredUnits()
        {
            int[] distances = Enumerable.Range(0, 500).Select(seed => AutonomousPveCampWatch.ShiftDistance(seed)).ToArray();
            Assert.That(distances.Min(), Is.EqualTo(300));
            Assert.That(distances.Max(), Is.EqualTo(600));
            Assert.That(AutonomousPveCampWatch.ReturnMemoryMilliseconds, Is.EqualTo(15 * 60_000));
        }

        [Test]
        public void RestCounterReportsOncePerTenMinutes()
        {
            var stats = new AutonomousPveRestStats();
            stats.RecordRest(1_000);
            stats.RecordPull(2_000, 80, 90);
            stats.RecordPull(3_000, 70, 70);
            stats.RecordPull(4_000, null, 100);
            Assert.That(stats.TryReport(500_000), Is.Null);
            Assert.That(stats.TryReport(601_000), Is.EqualTo(
                "PVE_REST window_s=600 rests=1 avg_power_pct_at_pull=75 avg_hp_pct_at_pull=86 pulls=3"));
            Assert.That(stats.TryReport(700_000), Is.Null);
        }
    }
}
