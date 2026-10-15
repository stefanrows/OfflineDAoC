using System;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The Gothwaite PvP player tactics of stage 6 (docs/GOTHWAITE_PVP_MODE.md section 13) as pure functions: the main-assist leader
    /// rank, the fight appetite, the retreat and solo-flee decisions, the regroup, the formation slots, the chase discipline, the add
    /// draw, the hunting stealther's opening rule, the new log lines, the tactics counters and the recent-fight lookup. No GameBot and
    /// no database.
    /// </summary>
    [TestFixture]
    public class UT_GothwaitePvpTactics
    {
        [SetUp]
        public void SetUp()
        {
            GothwaitePvpMode.Reset();
            GothwaitePvpStats.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            GothwaitePvpMode.Reset();
            GothwaitePvpStats.Reset();
        }

        private static void TurnOn() => GothwaitePvpMode.Apply(GothwaitePvpSettings.Defaults with { Enabled = true });

        private static RvrClassTraits Traits(eCharacterClass characterClass) => AutonomousRvrDoctrine.TraitsOf(characterClass);

        private static RvrDoctrine Doctrine(RvrDoctrineKind kind, params eCharacterClass[] classes)
        {
            RvrDoctrine doctrine = AutonomousRvrDoctrine.Derive(classes.Select(c => new RvrDoctrineMember(c, 30)).ToArray(),
                AutonomousPlayerType.Casual, RvrLeaderTraits.Neutral);
            Assert.That(doctrine.Kind, Is.EqualTo(kind), "the sample group reads as the expected doctrine");
            return doctrine;
        }

        // ------------------------------------------------------------ main assist

        [Test]
        public void TheLeaderRankPutsATankFirstAndAHealerLast()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpTactics.LeaderRank(Traits(eCharacterClass.Armsman)), Is.EqualTo(GothwaitePvpTactics.RankTank));
                Assert.That(GothwaitePvpTactics.LeaderRank(Traits(eCharacterClass.Warrior)), Is.EqualTo(GothwaitePvpTactics.RankTank));
                Assert.That(GothwaitePvpTactics.LeaderRank(Traits(eCharacterClass.Mercenary)), Is.EqualTo(GothwaitePvpTactics.RankMelee));
                Assert.That(GothwaitePvpTactics.LeaderRank(Traits(eCharacterClass.Skald)), Is.EqualTo(GothwaitePvpTactics.RankMelee));
                Assert.That(GothwaitePvpTactics.LeaderRank(Traits(eCharacterClass.Scout)), Is.EqualTo(GothwaitePvpTactics.RankArcher));
                Assert.That(GothwaitePvpTactics.LeaderRank(Traits(eCharacterClass.Wizard)), Is.EqualTo(GothwaitePvpTactics.RankCaster));
                Assert.That(GothwaitePvpTactics.LeaderRank(Traits(eCharacterClass.Infiltrator)), Is.EqualTo(GothwaitePvpTactics.RankStealther));
                Assert.That(GothwaitePvpTactics.LeaderRank(Traits(eCharacterClass.Cleric)), Is.EqualTo(GothwaitePvpTactics.RankHealer));
                Assert.That(GothwaitePvpTactics.LeaderRank(Traits(eCharacterClass.Friar)), Is.EqualTo(GothwaitePvpTactics.RankHealer),
                    "a Friar heals, so it does not lead");
                Assert.That(GothwaitePvpTactics.LeaderRank(Traits(eCharacterClass.Bard)), Is.EqualTo(GothwaitePvpTactics.RankHealer));
            });
        }

        [Test]
        public void TheLeaderChoiceTakesTheLowestEligibleRankAndKeepsJoinOrderOnTies()
        {
            var candidates = new[]
            {
                new GothwaitePvpLeaderCandidate(1, GothwaitePvpTactics.RankHealer, true),
                new GothwaitePvpLeaderCandidate(2, GothwaitePvpTactics.RankTank, false),
                new GothwaitePvpLeaderCandidate(3, GothwaitePvpTactics.RankMelee, true),
                new GothwaitePvpLeaderCandidate(4, GothwaitePvpTactics.RankMelee, true),
            };

            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpTactics.ChooseLeader(candidates), Is.EqualTo(2), "the first melee; the tank is not eligible");
                Assert.That(GothwaitePvpTactics.ChooseLeader(new[] { candidates[0] }), Is.EqualTo(0), "a healer leads when nobody else can");
                Assert.That(GothwaitePvpTactics.ChooseLeader(new[] { candidates[1] }), Is.EqualTo(-1));
                Assert.That(GothwaitePvpTactics.ChooseLeader(Array.Empty<GothwaitePvpLeaderCandidate>()), Is.EqualTo(-1));
                Assert.That(GothwaitePvpTactics.ChooseLeader(null), Is.EqualTo(-1));
            });
        }

        [Test]
        public void ThePromotionTakesTheBestRankedLivingMemberInTheArena()
        {
            var members = new[]
            {
                new GothwaitePvpPromotionCandidate(1, Alive: true, InArena: true, Rank: GothwaitePvpTactics.RankHealer),
                new GothwaitePvpPromotionCandidate(2, Alive: true, InArena: false, Rank: GothwaitePvpTactics.RankTank),
                new GothwaitePvpPromotionCandidate(3, Alive: false, InArena: true, Rank: GothwaitePvpTactics.RankTank),
                new GothwaitePvpPromotionCandidate(4, Alive: true, InArena: true, Rank: GothwaitePvpTactics.RankMelee),
                new GothwaitePvpPromotionCandidate(5, Alive: true, InArena: true, Rank: GothwaitePvpTactics.RankMelee),
            };

            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpArenaRules.BestAliveInArena(members), Is.EqualTo(3), "the first melee that is alive in the arena");
                Assert.That(GothwaitePvpArenaRules.FirstAliveInArena(members), Is.EqualTo(0), "the stage 3 rule still answers by join order");
                Assert.That(GothwaitePvpArenaRules.BestAliveInArena(new[] { members[1], members[2] }), Is.EqualTo(-1));
                Assert.That(GothwaitePvpArenaRules.BestAliveInArena(null), Is.EqualTo(-1));
            });
        }

        // ------------------------------------------------------------ fight appetite

        [Test]
        public void ASoloTakesASoloAndLeavesADuoAlone()
        {
            const double lone = 0.9; // the lone roamer's appetite at neutral aggression
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpTactics.TakesFight(lone, 1, 20, 1, 20, 0.99), Is.True);
                Assert.That(GothwaitePvpTactics.TakesFight(lone, 1, 20, 2, 20, 0.0), Is.False, "no dare below appetite 0.9");
                Assert.That(GothwaitePvpTactics.TakesFight(lone, 1, 27, 2, 20, 0.5), Is.False, "seven levels up, a careful solo still leaves a duo");
                Assert.That(GothwaitePvpTactics.TakesFight(1.125, 1, 27, 2, 20, 0.5), Is.True, "an aggressive solo seven levels up takes the duo");
                Assert.That(GothwaitePvpTactics.TakesFight(lone, 1, 20, 0, 20, 0.5), Is.True, "nobody there is no refusal");
            });
        }

        [Test]
        public void ASmallManAvoidsAFullGroupAndAnEightTakesOneAndALittleMore()
        {
            const double small = 1.0, eight = 1.15;
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpTactics.TakesFight(small, 5, 30, 5, 30, 0.99), Is.True);
                Assert.That(GothwaitePvpTactics.TakesFight(small, 5, 30, 8, 30, 0.99), Is.False, "a small-man does not take an eight");
                Assert.That(GothwaitePvpTactics.TakesFight(small, 5, 30, 8, 30, 0.01), Is.True, "except on a rare dare (4 %)");
                Assert.That(GothwaitePvpTactics.TakesFight(eight, 8, 30, 9, 30, 0.99), Is.True, "an eight takes nine");
                Assert.That(GothwaitePvpTactics.TakesFight(eight, 8, 30, 14, 30, 0.05), Is.True, "and dares fourteen one time in ten");
                Assert.That(GothwaitePvpTactics.TakesFight(eight, 8, 30, 14, 30, 0.5), Is.False);
                Assert.That(GothwaitePvpTactics.TakesFight(eight, 8, 30, 16, 30, 0.0), Is.False, "never twice its strength");
                Assert.That(GothwaitePvpTactics.TakesFight(0, 3, 30, 3, 30, 0.5), Is.True, "a missing appetite counts as one");
            });
        }

        [Test]
        public void TheStableRollIsInRangeStablePerBucketAndChangesAcrossBuckets()
        {
            double first = GothwaitePvpTactics.StableRoll(11, 22, 3);
            Assert.Multiple(() =>
            {
                Assert.That(first, Is.GreaterThanOrEqualTo(0).And.LessThan(1));
                Assert.That(GothwaitePvpTactics.StableRoll(11, 22, 3), Is.EqualTo(first));
                Assert.That(Enumerable.Range(0, 50).Select(bucket => GothwaitePvpTactics.StableRoll(11, 22, bucket)).Distinct().Count(),
                    Is.GreaterThan(40), "different minutes give different rolls");
                Assert.That(Enumerable.Range(0, 1000).Select(bucket => GothwaitePvpTactics.StableRoll(7, 9, bucket)).Average(),
                    Is.EqualTo(0.5).Within(0.06), "the rolls spread over the unit range");
            });
        }

        // ------------------------------------------------------------ retreat

        [Test]
        public void AGroupRetreatsWhenItsHealerIsDeadAndItIsOutnumberedUnlessItDecidesToStay()
        {
            RvrDoctrine smallMan = Doctrine(RvrDoctrineKind.SmallMan, eCharacterClass.Armsman, eCharacterClass.Cleric, eCharacterClass.Wizard,
                eCharacterClass.Mercenary);
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpTactics.GroupRetreats(smallMan, 3, 4, 0, 1, 6, 0.5), Is.True, "healer dead and outnumbered 2:1");
                Assert.That(GothwaitePvpTactics.GroupRetreats(smallMan, 3, 4, 0, 1, 6, 0.1), Is.False, "the stay roll keeps it in");
                Assert.That(GothwaitePvpTactics.GroupRetreats(smallMan, 4, 4, 1, 1, 4, 0.5), Is.False, "an even fight with the healer up");
                Assert.That(GothwaitePvpTactics.GroupRetreatReason(3, 4, 0, 1, 6), Is.EqualTo("healer_dead"));
                Assert.That(GothwaitePvpTactics.GroupRetreatReason(2, 4, 1, 1, 6), Is.EqualTo("half_down"));
                Assert.That(GothwaitePvpTactics.GroupRetreatReason(4, 4, 1, 1, 6), Is.EqualTo("outnumbered"));
                Assert.That(GothwaitePvpTactics.GroupRetreats(null, 3, 4, 0, 1, 6, 0.5), Is.False, "no doctrine, no retreat");
            });
        }

        [Test]
        public void ASoloFleesWhenAddedOnOrLosingByItsRiskTolerance()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpTactics.SoloFlees(80, 80, 2, 15, 0.5), Is.True, "two attackers, a careful bot runs");
                Assert.That(GothwaitePvpTactics.SoloFlees(80, 80, 2, 85, 0.5), Is.False, "a bold bot stays in at that roll");
                Assert.That(GothwaitePvpTactics.SoloFlees(80, 80, 2, 85, 0.7), Is.True, "but runs one time in three");
                Assert.That(GothwaitePvpTactics.SoloFlees(30, 80, 1, 50, 0.9), Is.True, "losing: low health against a healthy enemy");
                Assert.That(GothwaitePvpTactics.SoloFlees(30, 50, 1, 50, 0.9), Is.False, "the enemy is hurt too: it stays");
                Assert.That(GothwaitePvpTactics.SoloFlees(80, 20, 1, 50, 0.9), Is.False, "winning");
                Assert.That(GothwaitePvpTactics.SoloFleeReason(2), Is.EqualTo("added_on"));
                Assert.That(GothwaitePvpTactics.SoloFleeReason(1), Is.EqualTo("low_health"));
            });
        }

        [Test]
        public void TheRetreatLengthAndTheResumeRuleFollowTheConstants()
        {
            var random = new Random(3);
            Assert.Multiple(() =>
            {
                for (int i = 0; i < 200; i++)
                    Assert.That(GothwaitePvpTactics.RetreatMilliseconds(random),
                        Is.InRange(GothwaitePvpTactics.RetreatMinMilliseconds, GothwaitePvpTactics.RetreatMaxMilliseconds));
                Assert.That(GothwaitePvpTactics.ResumesAfterRetreat(GothwaitePvpTactics.RetreatResumeHealthPercent), Is.True);
                Assert.That(GothwaitePvpTactics.ResumesAfterRetreat(GothwaitePvpTactics.RetreatResumeHealthPercent - 1), Is.False);
            });
        }

        // ------------------------------------------------------------ regroup

        [Test]
        public void TheRegroupEndsWhenEveryoneIsNearAndRecoveredOrWhenItsTimeRunsOut()
        {
            const long since = 100_000;
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpTactics.RegroupDone(since + 1_000, since, 3, 3, 90, 80, true), Is.True, "all near and recovered");
                Assert.That(GothwaitePvpTactics.RegroupDone(since + 1_000, since, 3, 2, 90, 80, true), Is.False, "one member still away");
                Assert.That(GothwaitePvpTactics.RegroupDone(since + 1_000, since, 3, 3, 60, 80, true), Is.False, "someone still hurt");
                Assert.That(GothwaitePvpTactics.RegroupDone(since + 1_000, since, 3, 3, 90, 30, true), Is.False, "a caster still drained");
                Assert.That(GothwaitePvpTactics.RegroupDone(since + 1_000, since, 3, 3, 90, 30, false), Is.True, "nobody uses power");
                Assert.That(GothwaitePvpTactics.RegroupDone(since + GothwaitePvpTactics.RegroupMaxMilliseconds, since, 3, 0, 10, 0, true), Is.True,
                    "time is up");
                Assert.That(GothwaitePvpTactics.RegroupDone(since + 1_000, since, 0, 0, 90, 80, false), Is.True, "a solo with nobody to wait for");
            });
        }

        [Test]
        public void OnlyALostLeaderOrAWipeSendsARegroupingBotHome()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpTactics.ReturnWhileRegrouping(GothwaitePvpArenaRules.ReasonLowHealth), Is.Null);
                Assert.That(GothwaitePvpTactics.ReturnWhileRegrouping(GothwaitePvpArenaRules.ReasonLowPower), Is.Null);
                Assert.That(GothwaitePvpTactics.ReturnWhileRegrouping(GothwaitePvpArenaRules.ReasonLeaderLost), Is.EqualTo(GothwaitePvpArenaRules.ReasonLeaderLost));
                Assert.That(GothwaitePvpTactics.ReturnWhileRegrouping(GothwaitePvpArenaRules.ReasonWiped), Is.EqualTo(GothwaitePvpArenaRules.ReasonWiped));
                Assert.That(GothwaitePvpTactics.ReturnWhileRegrouping(null), Is.Null);
            });
        }

        // ------------------------------------------------------------ formation

        [Test]
        public void TheForwardVectorFollowsTheDaocHeading()
        {
            Vector2 north = GothwaitePvpTactics.Forward(0);
            Vector2 quarter = GothwaitePvpTactics.Forward(1024);
            Vector2 half = GothwaitePvpTactics.Forward(2048);
            Assert.Multiple(() =>
            {
                Assert.That(north.X, Is.EqualTo(0).Within(0.001));
                Assert.That(north.Y, Is.EqualTo(1).Within(0.001));
                Assert.That(quarter.X, Is.EqualTo(-1).Within(0.001));
                Assert.That(quarter.Y, Is.EqualTo(0).Within(0.001));
                Assert.That(half.Y, Is.EqualTo(-1).Within(0.001));
            });
        }

        [Test]
        public void AColumnSlotLiesBehindTheLeaderAndAClumpKeepsTheLeadersSpot()
        {
            var leader = new Vector3(1000, 1000, 50);
            Vector2 forward = GothwaitePvpTactics.Forward(0);
            Vector3 first = GothwaitePvpTactics.FormationPoint(RvrTravelShape.Column, 0, 4, leader, forward, 7);
            Vector3 third = GothwaitePvpTactics.FormationPoint(RvrTravelShape.Column, 2, 4, leader, forward, 7);
            Vector3 clump = GothwaitePvpTactics.FormationPoint(RvrTravelShape.Clump, 2, 4, leader, forward, 7);
            Vector3 loose = GothwaitePvpTactics.FormationPoint(RvrTravelShape.Loose, 1, 3, leader, forward, 7);
            Assert.Multiple(() =>
            {
                Assert.That(first.Y, Is.LessThan(leader.Y), "behind the leader (the leader faces +Y)");
                Assert.That(third.Y, Is.LessThan(first.Y), "the third slot is farther back than the first");
                Assert.That(Vector3.Distance(first, leader), Is.GreaterThan(90).And.LessThan(400));
                Assert.That(clump, Is.EqualTo(leader));
                Assert.That(Vector3.Distance(loose, leader), Is.GreaterThan(200), "a loose fan keeps its distance");
                Assert.That(first.Z, Is.EqualTo(leader.Z));
            });
        }

        [Test]
        public void TheMarchingOrderPutsGuardsFirstHealersInTheMiddleAndCastersBehind()
        {
            Assert.That(GothwaitePvpTactics.MarchRank(Traits(eCharacterClass.Armsman)), Is.LessThan(GothwaitePvpTactics.MarchRank(Traits(eCharacterClass.Cleric))));
            Assert.That(GothwaitePvpTactics.MarchRank(Traits(eCharacterClass.Cleric)), Is.LessThan(GothwaitePvpTactics.MarchRank(Traits(eCharacterClass.Wizard))));
        }

        // ------------------------------------------------------------ chase

        [Test]
        public void TheChaseDisciplineDropsARunnerBeyondTheHealersUnlessItIsAboutToDie()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpTactics.Overextended(GothwaitePvpTactics.FollowerChaseRange + 1, 80), Is.True);
                Assert.That(GothwaitePvpTactics.Overextended(GothwaitePvpTactics.FollowerChaseRange, 80), Is.False);
                Assert.That(GothwaitePvpTactics.Overextended(GothwaitePvpTactics.FollowerChaseRange + 1, GothwaitePvpTactics.FinishHealthPercent - 1),
                    Is.False, "finish it");
                Assert.That(GothwaitePvpTactics.ChasedTooFar(GothwaitePvpTactics.ChaseFromStartRange + 1, 80), Is.True);
                Assert.That(GothwaitePvpTactics.ChasedTooFar(GothwaitePvpTactics.ChaseFromStartRange, 80), Is.False);
                Assert.That(GothwaitePvpTactics.ChasedTooFar(GothwaitePvpTactics.ChaseFromStartRange + 1, 10), Is.False);
            });
        }

        // ------------------------------------------------------------ adds

        [Test]
        public void TheAddDrawFollowsAggression()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpTactics.JoinsFight(15, 0.30), Is.True, "a third of the time at low aggression");
                Assert.That(GothwaitePvpTactics.JoinsFight(15, 0.33), Is.False);
                Assert.That(GothwaitePvpTactics.JoinsFight(85, 0.66), Is.True, "two thirds at high aggression");
                Assert.That(GothwaitePvpTactics.JoinsFight(85, 0.68), Is.False);
                Assert.That(GothwaitePvpTactics.JoinsFight(500, 0.74), Is.True, "clamped at 75 %");
                Assert.That(GothwaitePvpTactics.JoinsFight(500, 0.76), Is.False);
            });
        }

        [Test]
        public void TheRecentFightLookupFindsTheNewestFightInRange()
        {
            TurnOn();
            GothwaitePvpFight Fight(int x, int y) => new(new GothwaitePvpFighter("K", 20, GothwaitePvpKind.ModeBot),
                new GothwaitePvpFighter("V", 20, GothwaitePvpKind.ModeBot), true, x, y, 9000);
            GothwaitePvpStats.Record(Fight(10_000, 10_000), 100_000);
            GothwaitePvpStats.Record(Fight(11_000, 10_000), 120_000);
            GothwaitePvpStats.Record(Fight(50_000, 50_000), 130_000);

            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpStats.TryRecentFightNear(10_500, 10_000, 2_000, 45_000, 140_000, out int x, out int y), Is.True);
                Assert.That((x, y), Is.EqualTo((11_000, 10_000)), "the newest fight in range");
                Assert.That(GothwaitePvpStats.TryRecentFightNear(10_500, 10_000, 2_000, 45_000, 170_000, out _, out _), Is.False, "too old");
                Assert.That(GothwaitePvpStats.TryRecentFightNear(30_000, 30_000, 2_000, 45_000, 140_000, out _, out _), Is.False, "none in range");
                Assert.That(GothwaitePvpStats.TryRecentFightNear(11_000, 10_000, 2_000, 45_000, 140_000, out x, out y, GothwaitePvpTactics.AddOwnRange), Is.True);
                Assert.That((x, y), Is.EqualTo((10_000, 10_000)), "the group's own kill under it is skipped; the next one draws it");
                Assert.That(GothwaitePvpStats.TryRecentFightNear(10_500, 10_000, 2_000, 45_000, 140_000, out _, out _, GothwaitePvpTactics.AddOwnRange), Is.False,
                    "both fights are within the own range");
            });
        }

        // ------------------------------------------------------------ stealthers

        [Test]
        public void AHuntingStealtherOpensOnLoneSoftOrWoundedTargetsOnly()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpTactics.StealtherMayOpen(1, 100, false), Is.True, "a lone walker");
                Assert.That(GothwaitePvpTactics.StealtherMayOpen(2, 100, true), Is.True, "a caster in a duo");
                Assert.That(GothwaitePvpTactics.StealtherMayOpen(2, 100, false), Is.False, "a tank in a duo");
                Assert.That(GothwaitePvpTactics.StealtherMayOpen(3, 100, true), Is.False, "a caster with a group around it");
                Assert.That(GothwaitePvpTactics.StealtherMayOpen(5, GothwaitePvpTactics.StealthWoundedPercent - 1, false), Is.True, "a straggler bleeding");
            });
        }

        [Test]
        public void TheStealthDoctrinesHuntStealthedAndTheHunterClassesExcludeTheMinstrel()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpTactics.HuntsStealthed(RvrDoctrineKind.SoloAssassin), Is.True);
                Assert.That(GothwaitePvpTactics.HuntsStealthed(RvrDoctrineKind.StealthPack), Is.True);
                Assert.That(GothwaitePvpTactics.HuntsStealthed(RvrDoctrineKind.GankSquad), Is.True);
                Assert.That(GothwaitePvpTactics.HuntsStealthed(RvrDoctrineKind.ArcherGroup), Is.True);
                Assert.That(GothwaitePvpTactics.HuntsStealthed(RvrDoctrineKind.SmallMan), Is.False);
                Assert.That(GothwaitePvpTactics.HuntsStealthed(RvrDoctrineKind.AssistTrain), Is.False);
                Assert.That(GothwaitePvpTactics.IsStealther(Traits(eCharacterClass.Shadowblade)), Is.True);
                Assert.That(GothwaitePvpTactics.IsStealther(Traits(eCharacterClass.Hunter)), Is.True);
                Assert.That(GothwaitePvpTactics.IsStealther(Traits(eCharacterClass.Minstrel)), Is.False);
                Assert.That(GothwaitePvpStealth.IsHunterClass(eCharacterClass.Nightshade), Is.True);
                Assert.That(GothwaitePvpStealth.IsHunterClass(eCharacterClass.Ranger), Is.True);
                Assert.That(GothwaitePvpStealth.IsHunterClass(eCharacterClass.Minstrel), Is.False);
                Assert.That(GothwaitePvpStealth.IsHunterClass(eCharacterClass.Armsman), Is.False);
            });
        }

        [Test]
        public void TheAmbushDwellIsLongerThanThePatrolDwell()
        {
            var random = new Random(5);
            for (int i = 0; i < 200; i++)
            {
                int dwell = GothwaitePvpTactics.AmbushDwellMilliseconds(random);
                Assert.That(dwell, Is.InRange(GothwaitePvpTactics.AmbushDwellMinMilliseconds, GothwaitePvpTactics.AmbushDwellMaxMilliseconds));
                Assert.That(dwell, Is.GreaterThan(GothwaitePvpArenaRules.DwellMaxMilliseconds));
            }
        }

        // ------------------------------------------------------------ lines, counters and status

        [Test]
        public void TheStageSixLogLinesHaveTheGreppableFormat()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpLines.Depart("7", "Ana", 4, "SmallMan"), Is.EqualTo("GOTH_PVP_DEPART group=7 leader=\"Ana\" size=4 doctrine=SmallMan"));
                Assert.That(GothwaitePvpLines.Retreat("Bo", "healer_dead", "health=40 power=10"),
                    Is.EqualTo("GOTH_PVP_RETREAT bot=\"Bo\" reason=healer_dead health=40 power=10"));
                Assert.That(GothwaitePvpLines.RetreatEnd("Bo", "resume", 70), Is.EqualTo("GOTH_PVP_RETREAT_END bot=\"Bo\" reason=resume health=70"));
                Assert.That(GothwaitePvpLines.Regroup("Bo", "start", "health=55 power=20"),
                    Is.EqualTo("GOTH_PVP_REGROUP bot=\"Bo\" reason=start health=55 power=20"));
                Assert.That(GothwaitePvpLines.Add("Bo", 1, 2, 3000), Is.EqualTo("GOTH_PVP_ADD bot=\"Bo\" fight=1,2 distance=3000"));
                Assert.That(GothwaitePvpLines.Ambush("Bo", "Cy", 1, 95), Is.EqualTo("GOTH_PVP_AMBUSH bot=\"Bo\" target=\"Cy\" party=1 health=95"));
                Assert.That(GothwaitePvpLines.GroupLeader("7", "Bo", "Ana", "main_assist"),
                    Is.EqualTo("GOTH_PVP_GROUP_LEADER group=7 leader=\"Bo\" previous=\"Ana\" reason=main_assist"));
            });
        }

        [Test]
        public void TheTacticsCountersCountAndReset()
        {
            GothwaitePvpStats.CountMainAssist();
            GothwaitePvpStats.CountRefusedOpening();
            GothwaitePvpStats.CountRefusedOpening();
            GothwaitePvpStats.CountRetreat();
            GothwaitePvpStats.CountRegroup();
            GothwaitePvpStats.CountAdd();
            GothwaitePvpStats.CountAmbush();

            Assert.That(GothwaitePvpStats.Tactics, Is.EqualTo(new GothwaitePvpTacticsCount(1, 2, 1, 1, 1, 1)));
            GothwaitePvpStats.Reset();
            Assert.That(GothwaitePvpStats.Tactics, Is.EqualTo(default(GothwaitePvpTacticsCount)));
        }

        [Test]
        public void TheStatusPrintsTheTacticsLine()
        {
            var report = new GothwaitePvpStatusReport(true, 51, "gothwaite", 4000, Array.Empty<GothwaitePvpBotSample>(),
                GothwaitePvpGroupCensus.Empty,
                new GothwaitePvpStatsSnapshot(0, 60_000, GothwaitePvpStats.HeatWindowMilliseconds, 0, 0,
                    Array.Empty<GothwaitePvpBandCount>(), Array.Empty<GothwaitePvpConCount>())
                {
                    Tactics = new GothwaitePvpTacticsCount(3, 12, 4, 9, 2, 5),
                },
                null);

            Assert.That(GothwaitePvpStatus.Lines(report), Has.Some.EqualTo(
                "Tactics: tanks took the lead 3, openings refused (party too big) 12, retreats 4, regroups 9, adds 2, stealth openings 5."));
        }

        [Test]
        public void TheDoctrineOfAnAssassinAloneIsASoloAssassinAndOfAnEightWithHealersAndMezAnAssistTrain()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Doctrine(RvrDoctrineKind.SoloAssassin, eCharacterClass.Infiltrator).Kind, Is.EqualTo(RvrDoctrineKind.SoloAssassin));
                Assert.That(Doctrine(RvrDoctrineKind.AssistTrain, eCharacterClass.Warrior, eCharacterClass.Berserker, eCharacterClass.Savage,
                    eCharacterClass.Healer, eCharacterClass.Healer, eCharacterClass.Shaman, eCharacterClass.Skald, eCharacterClass.Runemaster).HasCaller,
                    Is.True, "an assist train has a caller");
            });
        }
    }
}
