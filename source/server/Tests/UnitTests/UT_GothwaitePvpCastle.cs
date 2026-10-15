using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OfflineDaoc.Configuration;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The Gothwaite PvP castle rules of stage 2 (docs/GOTHWAITE_PVP_MODE.md sections 6.2, 6.3 and 6.6): the shape draw,
    /// the level band, the candidate order, the departure decision, the rest and buff ends, the grey/purple rule, and the
    /// log line formats. Pure functions only: no GameBot and no database.
    /// </summary>
    [TestFixture]
    public class UT_GothwaitePvpCastle
    {
        private const int Draws = 10_000;
        private const double Tolerance = 0.02;
        private static readonly GothwaitePvpGroupShares DefaultShares = GothwaitePvpSettings.Defaults.GroupShares;

        [SetUp]
        public void SetUp() => GothwaitePvpMode.Reset();

        [TearDown]
        public void TearDown() => GothwaitePvpMode.Reset();

        private static GothwaitePvpGuild NoGuild => default;

        private static GothwaitePvpMember Member(int level, bool healer = false, GothwaitePvpGuild guild = default) =>
            new(level, healer, guild);

        private static GothwaitePvpCandidate Candidate(long botId, int level, bool healer = false, GothwaitePvpGuild guild = default,
            long readyTick = 0) => new(botId, Member(level, healer, guild), readyTick);

        [Test]
        public void TheShapeDrawFollowsTheDefaultSharesOverTenThousandDraws()
        {
            var counts = CountShapes(DefaultShares, botId: i => i + 1, cycle: _ => 1);

            Assert.Multiple(() =>
            {
                AssertShare(counts, GothwaitePvpShape.Solo, DefaultShares.Solo);
                AssertShare(counts, GothwaitePvpShape.Duo, DefaultShares.Duo);
                AssertShare(counts, GothwaitePvpShape.Small, DefaultShares.Small);
                AssertShare(counts, GothwaitePvpShape.Eight, DefaultShares.Eight);
            });
        }

        [Test]
        public void TheShapeDrawFollowsOtherSharesAcrossBotsAndReadyCycles()
        {
            var shares = new GothwaitePvpGroupShares(10, 20, 30, 40);

            var counts = CountShapes(shares, botId: i => 1_000_000 + (i % 500) * 7919L, cycle: i => 1 + i / 500);

            Assert.Multiple(() =>
            {
                AssertShare(counts, GothwaitePvpShape.Solo, shares.Solo);
                AssertShare(counts, GothwaitePvpShape.Duo, shares.Duo);
                AssertShare(counts, GothwaitePvpShape.Small, shares.Small);
                AssertShare(counts, GothwaitePvpShape.Eight, shares.Eight);
            });
        }

        [Test]
        public void TheShapeDrawIsDeterministicForTheSameBotAndCycle()
        {
            for (int cycle = 1; cycle <= 25; cycle++)
            {
                Assert.That(GothwaitePvpShapes.Draw(4242, cycle, DefaultShares), Is.EqualTo(GothwaitePvpShapes.Draw(4242, cycle, DefaultShares)));
                Assert.That(GothwaitePvpShapes.TargetSize(GothwaitePvpShape.Small, 4242, cycle),
                    Is.EqualTo(GothwaitePvpShapes.TargetSize(GothwaitePvpShape.Small, 4242, cycle)));
            }
        }

        [Test]
        public void ASingleShareOfOneHundredPercentDrawsOnlyThatShape()
        {
            var allEight = new GothwaitePvpGroupShares(0, 0, 0, 100);
            var allSolo = new GothwaitePvpGroupShares(100, 0, 0, 0);

            Assert.Multiple(() =>
            {
                for (int bot = 1; bot <= 200; bot++)
                {
                    Assert.That(GothwaitePvpShapes.Draw(bot, 1, allEight), Is.EqualTo(GothwaitePvpShape.Eight));
                    Assert.That(GothwaitePvpShapes.Draw(bot, 1, allSolo), Is.EqualTo(GothwaitePvpShape.Solo));
                }
            });
        }

        [Test]
        public void TheShapeDrawUsesTheModeSettingsWhenTheModeIsApplied()
        {
            GothwaitePvpMode.Apply(GothwaitePvpSettings.Defaults with { Enabled = true, GroupShares = new(0, 0, 0, 100) });

            Assert.That(GothwaitePvpShapes.Draw(77, 1, GothwaitePvpMode.Settings.GroupShares), Is.EqualTo(GothwaitePvpShape.Eight));
        }

        [Test]
        public void ShapeMinimumsAndTargetsMatchTheDesign()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpShapes.MinimumSize(GothwaitePvpShape.Solo), Is.EqualTo(1));
                Assert.That(GothwaitePvpShapes.MinimumSize(GothwaitePvpShape.Duo), Is.EqualTo(2));
                Assert.That(GothwaitePvpShapes.MinimumSize(GothwaitePvpShape.Small), Is.EqualTo(3));
                Assert.That(GothwaitePvpShapes.MinimumSize(GothwaitePvpShape.Eight), Is.EqualTo(5));
                Assert.That(GothwaitePvpShapes.TargetSize(GothwaitePvpShape.Solo, 1, 1), Is.EqualTo(1));
                Assert.That(GothwaitePvpShapes.TargetSize(GothwaitePvpShape.Duo, 1, 1), Is.EqualTo(2));
                Assert.That(GothwaitePvpShapes.TargetSize(GothwaitePvpShape.Eight, 1, 1), Is.EqualTo(8));
            });
        }

        [Test]
        public void ASmallGroupDrawsEachSizeFromThreeToFive()
        {
            var sizes = new HashSet<int>();
            for (int bot = 1; bot <= 1_000; bot++)
            {
                int size = GothwaitePvpShapes.TargetSize(GothwaitePvpShape.Small, bot, 1);
                Assert.That(size, Is.InRange(3, 5));
                sizes.Add(size);
            }

            Assert.That(sizes, Is.EquivalentTo(new[] { 3, 4, 5 }));
        }

        [Test]
        public void ShapeNamesAreTheLogWords()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpShapes.Name(GothwaitePvpShape.Solo), Is.EqualTo("solo"));
                Assert.That(GothwaitePvpShapes.Name(GothwaitePvpShape.Duo), Is.EqualTo("duo"));
                Assert.That(GothwaitePvpShapes.Name(GothwaitePvpShape.Small), Is.EqualTo("small"));
                Assert.That(GothwaitePvpShapes.Name(GothwaitePvpShape.Eight), Is.EqualTo("eight"));
            });
        }

        [Test]
        public void TheLevelBandAdmitsExactlyTheBandIncludingItsEdges()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpCastleRules.InLevelBand(10, 10, 5), Is.True);
                Assert.That(GothwaitePvpCastleRules.InLevelBand(10, 15, 5), Is.True, "+band is inside");
                Assert.That(GothwaitePvpCastleRules.InLevelBand(10, 5, 5), Is.True, "-band is inside");
                Assert.That(GothwaitePvpCastleRules.InLevelBand(10, 16, 5), Is.False, "+band+1 is outside");
                Assert.That(GothwaitePvpCastleRules.InLevelBand(10, 4, 5), Is.False, "-band-1 is outside");
                Assert.That(GothwaitePvpCastleRules.InLevelBand(1, 1, 0), Is.True);
                Assert.That(GothwaitePvpCastleRules.InLevelBand(1, 2, 0), Is.False);
            });
        }

        [Test]
        public void RankingDropsCandidatesOutsideTheLeadersBand()
        {
            var leader = Member(20);
            var candidates = new[] { Candidate(1, 25), Candidate(2, 26), Candidate(3, 16), Candidate(4, 15) };

            IReadOnlyList<GothwaitePvpCandidate> ranked = GothwaitePvpCastleRules.Rank(leader, new[] { leader }, candidates, levelBand: 5);

            Assert.That(ranked.Select(candidate => candidate.BotId), Is.EquivalentTo(new long[] { 1, 3, 4 }));
        }

        [Test]
        public void TheSameGuildComesBeforeAnAlliedGuildAndThenStrangers()
        {
            var leader = Member(20, healer: true, guild: new("guild-a", "alliance-x"));
            var candidates = new[]
            {
                Candidate(1, 20, guild: new("guild-b", "")),
                Candidate(2, 20, guild: new("guild-b", "alliance-x")),
                Candidate(3, 20, guild: new("guild-a", "alliance-x")),
            };

            IReadOnlyList<GothwaitePvpCandidate> ranked = GothwaitePvpCastleRules.Rank(leader, new[] { leader }, candidates, levelBand: 5);

            Assert.That(ranked.Select(candidate => candidate.BotId).ToArray(), Is.EqualTo(new long[] { 3, 2, 1 }));
        }

        [Test]
        public void AHealerComesBeforeASecondNonHealerWhileTheGroupHasNone()
        {
            // The leader is a non-healer alone: the next non-healer would be the second member of another role.
            var leader = Member(20, healer: false, guild: new("guild-a", ""));
            var candidates = new[]
            {
                Candidate(1, 20, healer: false, guild: new("guild-a", "")),
                Candidate(2, 20, healer: true, guild: new("guild-z", "")),
            };

            IReadOnlyList<GothwaitePvpCandidate> ranked = GothwaitePvpCastleRules.Rank(leader, new[] { leader }, candidates, levelBand: 5);

            Assert.That(ranked[0].BotId, Is.EqualTo(2), "the stranger healer is taken before the guildmate");
        }

        [Test]
        public void GuildPreferenceDecidesOnceTheGroupHasAHealer()
        {
            var leader = Member(20, healer: true, guild: new("guild-a", ""));
            var members = new[] { leader, Member(20, healer: false, guild: new("guild-q", "")) };
            var candidates = new[]
            {
                Candidate(1, 20, healer: true, guild: new("guild-z", "")),
                Candidate(2, 20, healer: false, guild: new("guild-a", "")),
            };

            IReadOnlyList<GothwaitePvpCandidate> ranked = GothwaitePvpCastleRules.Rank(leader, members, candidates, levelBand: 5);

            Assert.That(ranked[0].BotId, Is.EqualTo(2), "the guildmate ranks first when no healer is needed");
        }

        [Test]
        public void TiesBreakByLevelGapThenWaitThenBotId()
        {
            var leader = Member(20);
            var candidates = new[]
            {
                Candidate(9, 22, readyTick: 5),
                Candidate(8, 21, readyTick: 9),
                Candidate(7, 21, readyTick: 3),
                Candidate(6, 21, readyTick: 3),
            };

            IReadOnlyList<GothwaitePvpCandidate> ranked = GothwaitePvpCastleRules.Rank(leader, new[] { leader }, candidates, levelBand: 5);

            Assert.That(ranked.Select(candidate => candidate.BotId).ToArray(), Is.EqualTo(new long[] { 6, 7, 8, 9 }));
        }

        [Test]
        public void GuildAffinityNeedsARealSharedId()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpCastleRules.GuildAffinity(new("g1", "a1"), new("g1", "a2")), Is.EqualTo(2), "same guild");
                Assert.That(GothwaitePvpCastleRules.GuildAffinity(new("g1", "a1"), new("g2", "a1")), Is.EqualTo(1), "allied guild");
                Assert.That(GothwaitePvpCastleRules.GuildAffinity(new("g1", ""), new("g2", "")), Is.EqualTo(0), "two guildless");
                Assert.That(GothwaitePvpCastleRules.GuildAffinity(new("", ""), new("", "")), Is.EqualTo(0), "empty ids never match");
                Assert.That(GothwaitePvpCastleRules.GuildAffinity(default, default), Is.EqualTo(0), "null ids never match");
                Assert.That(GothwaitePvpCastleRules.GuildAffinity(new("g1", ""), new("g2", "")), Is.EqualTo(0), "different guilds, no alliance");
            });
        }

        [Test]
        public void ADepartureLeavesWhenFullWhateverTheWaitLeft()
        {
            Assert.That(GothwaitePvpCastleRules.Decide(now: 1_000, expiresAt: 90_000, readyCount: 8, target: 8, minimum: 5),
                Is.EqualTo(GothwaitePvpFormationStep.Depart));
        }

        [Test]
        public void AGroupWaitsWhileItIsNotFullAndNotExpired()
        {
            Assert.That(GothwaitePvpCastleRules.Decide(now: 89_999, expiresAt: 90_000, readyCount: 6, target: 8, minimum: 5),
                Is.EqualTo(GothwaitePvpFormationStep.Wait));
        }

        [Test]
        public void AnExpiredGroupLeavesWithItsMinimumAndDisbandsBelowIt()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpCastleRules.Decide(now: 90_000, expiresAt: 90_000, readyCount: 5, target: 8, minimum: 5),
                    Is.EqualTo(GothwaitePvpFormationStep.Depart), "eight at its minimum");
                Assert.That(GothwaitePvpCastleRules.Decide(now: 90_000, expiresAt: 90_000, readyCount: 4, target: 8, minimum: 5),
                    Is.EqualTo(GothwaitePvpFormationStep.Disband), "eight below its minimum");
                Assert.That(GothwaitePvpCastleRules.Decide(now: 90_000, expiresAt: 90_000, readyCount: 3, target: 5, minimum: 3),
                    Is.EqualTo(GothwaitePvpFormationStep.Depart), "small at its minimum");
                Assert.That(GothwaitePvpCastleRules.Decide(now: 90_000, expiresAt: 90_000, readyCount: 2, target: 5, minimum: 3),
                    Is.EqualTo(GothwaitePvpFormationStep.Disband), "small below its minimum");
                Assert.That(GothwaitePvpCastleRules.Decide(now: 90_000, expiresAt: 90_000, readyCount: 1, target: 2, minimum: 2),
                    Is.EqualTo(GothwaitePvpFormationStep.Disband), "a lone leader of a duo");
            });
        }

        [Test]
        public void ShapeMinimumsAreTheDepartureMinimumsOfTheDecision()
        {
            foreach (GothwaitePvpShape shape in new[] { GothwaitePvpShape.Duo, GothwaitePvpShape.Small, GothwaitePvpShape.Eight })
            {
                int minimum = GothwaitePvpShapes.MinimumSize(shape);
                int target = GothwaitePvpShapes.TargetSize(shape, 1, 1);
                Assert.That(minimum, Is.LessThanOrEqualTo(target), shape.ToString());
                Assert.That(GothwaitePvpCastleRules.Decide(90_000, 90_000, minimum, target, minimum),
                    Is.EqualTo(GothwaitePvpFormationStep.Depart), shape.ToString());
                Assert.That(GothwaitePvpCastleRules.Decide(90_000, 90_000, minimum - 1, target, minimum),
                    Is.EqualTo(GothwaitePvpFormationStep.Disband), shape.ToString());
            }
        }

        [Test]
        public void RestEndsAtTheRestLevelForAFighterAndAtBothLevelsForACaster()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpCastleRules.RestDone(80, 0, usesPower: false, restPercent: 80), Is.True, "fighter ignores power");
                Assert.That(GothwaitePvpCastleRules.RestDone(79, 100, usesPower: false, restPercent: 80), Is.False, "health short");
                Assert.That(GothwaitePvpCastleRules.RestDone(100, 79, usesPower: true, restPercent: 80), Is.False, "caster power short");
                Assert.That(GothwaitePvpCastleRules.RestDone(80, 80, usesPower: true, restPercent: 80), Is.True, "caster at the level");
                Assert.That(GothwaitePvpCastleRules.RestDone(100, 0, usesPower: true, restPercent: 80), Is.False, "caster without power");
            });
        }

        [Test]
        public void RestEndReasonIsRecoveredThenCapThenNothing()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpCastleRules.RestEndReason(90, 90, true, 80, now: 10, until: 5), Is.EqualTo("recovered"),
                    "recovered wins even past the cap");
                Assert.That(GothwaitePvpCastleRules.RestEndReason(40, 40, true, 80, now: 10, until: 100), Is.Null, "still resting");
                Assert.That(GothwaitePvpCastleRules.RestEndReason(40, 40, true, 80, now: 100, until: 100), Is.EqualTo("cap"),
                    "cap at the limit");
            });
        }

        [Test]
        public void TheBuffWindowWaitsForItsTimeAndForTheEndOfACast()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpCastleRules.BuffWindowDone(now: 99, until: 100, casting: false), Is.False, "window open");
                Assert.That(GothwaitePvpCastleRules.BuffWindowDone(now: 100, until: 100, casting: true), Is.False, "still casting");
                Assert.That(GothwaitePvpCastleRules.BuffWindowDone(now: 100, until: 100, casting: false), Is.True, "time and quiet");
            });
        }

        [Test]
        public void AReleasedFollowerKeepsItsGroupOnlyWhileTheLeaderIsInTheCastle()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpCastleRules.KeepsGroupOnRelease(isLeader: false, departing: false, leaderInCastle: true), Is.True,
                    "a follower of a gathering group whose leader is in the castle");
                Assert.That(GothwaitePvpCastleRules.KeepsGroupOnRelease(isLeader: false, departing: false, leaderInCastle: false), Is.False,
                    "the leader is outside");
                Assert.That(GothwaitePvpCastleRules.KeepsGroupOnRelease(isLeader: false, departing: true, leaderInCastle: true), Is.False,
                    "the group has left the castle");
                Assert.That(GothwaitePvpCastleRules.KeepsGroupOnRelease(isLeader: true, departing: false, leaderInCastle: true), Is.False,
                    "a released leader ends its group");
            });
        }

        [Test]
        public void OnlyGreenToRedIsOpenedOnUnderG4()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpCastleRules.ConMayOpen(ConColor.GREY), Is.False);
                Assert.That(GothwaitePvpCastleRules.ConMayOpen(ConColor.PURPLE), Is.False);
                Assert.That(GothwaitePvpCastleRules.ConMayOpen(ConColor.UNKNOWN), Is.False);
                Assert.That(GothwaitePvpCastleRules.ConMayOpen(ConColor.GREEN), Is.True);
                Assert.That(GothwaitePvpCastleRules.ConMayOpen(ConColor.BLUE), Is.True);
                Assert.That(GothwaitePvpCastleRules.ConMayOpen(ConColor.YELLOW), Is.True);
                Assert.That(GothwaitePvpCastleRules.ConMayOpen(ConColor.ORANGE), Is.True);
                Assert.That(GothwaitePvpCastleRules.ConMayOpen(ConColor.RED), Is.True);
            });
        }

        [Test]
        public void TheConTableGreysAndPurplesOutTheLowAndTheHighTargets()
        {
            Assert.Multiple(() =>
            {
                // A level 10 bot cons grey to a level 1 target and yellow to its own level.
                Assert.That(GothwaitePvpCastleRules.ConMayOpen(ConLevels.GetConColor(ConLevels.GetConLevel(10, 1))), Is.False, "grey");
                Assert.That(GothwaitePvpCastleRules.ConMayOpen(ConLevels.GetConColor(ConLevels.GetConLevel(10, 10))), Is.True, "yellow");
                // A level 1 bot cons purple to a level 4 target.
                Assert.That(GothwaitePvpCastleRules.ConMayOpen(ConLevels.GetConColor(ConLevels.GetConLevel(1, 4))), Is.False, "purple");
            });
        }

        [Test]
        public void HealersAreTheClassesThatCanHeal()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpCastleRules.IsHealer(eCharacterClass.Cleric), Is.True);
                Assert.That(GothwaitePvpCastleRules.IsHealer(eCharacterClass.Druid), Is.True);
                Assert.That(GothwaitePvpCastleRules.IsHealer(eCharacterClass.Shaman), Is.True);
                Assert.That(GothwaitePvpCastleRules.IsHealer(eCharacterClass.Warrior), Is.False);
                Assert.That(GothwaitePvpCastleRules.IsHealer(eCharacterClass.Sorcerer), Is.False);
            });
        }

        [Test]
        public void TheGroupLogLinesKeepTheOwnersGrepFormat()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GothwaitePvpLines.Formed("7", GothwaitePvpShape.Small, 4, 12, 15, new[] { "Albion", "Midgard" },
                        new[] { "Ana", "Bo", "Cy", "Di" }),
                    Is.EqualTo("GOTH_PVP_GROUP_FORMED group=7 shape=small size=4 levels=12-15 realms=Albion,Midgard members=\"Ana,Bo,Cy,Di\""));
                Assert.That(GothwaitePvpLines.Depart("7", "Ana", 4), Is.EqualTo("GOTH_PVP_DEPART group=7 leader=\"Ana\" size=4"));
                Assert.That(GothwaitePvpLines.Depart(GothwaitePvpLines.SoloId, "Ana", 1),
                    Is.EqualTo("GOTH_PVP_DEPART group=solo leader=\"Ana\" size=1"));
                Assert.That(GothwaitePvpLines.Disbanded("8", GothwaitePvpShape.Eight, 3, 3, "expired"),
                    Is.EqualTo("GOTH_PVP_GROUP_DISBANDED group=8 shape=eight size=3 ready=3 reason=expired"));
                Assert.That(GothwaitePvpLines.Dissolved("9", GothwaitePvpShape.Duo, 1, "leader_lost"),
                    Is.EqualTo("GOTH_PVP_GROUP_DISSOLVED group=9 shape=duo size=1 reason=leader_lost"));
                Assert.That(GothwaitePvpLines.RestStart("Ana", 42, 10), Is.EqualTo("GOTH_PVP_REST bot=\"Ana\" health=42 power=10"));
                Assert.That(GothwaitePvpLines.RestEnd("Ana", "cap", 60, 30),
                    Is.EqualTo("GOTH_PVP_REST_END bot=\"Ana\" reason=cap health=60 power=30"));
                Assert.That(GothwaitePvpLines.Return("Ana", "released", "left_group=true"),
                    Is.EqualTo("GOTH_PVP_RETURN bot=\"Ana\" reason=released left_group=true"));
                Assert.That(GothwaitePvpLines.Return("Ana", "outside_castle"), Is.EqualTo("GOTH_PVP_RETURN bot=\"Ana\" reason=outside_castle"));
            });
        }

        [Test]
        public void TheCastleExitIsJustOutsideTheSafeCircleNorthOfTheCentre()
        {
            GothwaitePvpArena arena = GothwaitePvpArena.Gothwaite;
            GothwaitePvpSpot exit = arena.CastleExit;

            Assert.Multiple(() =>
            {
                Assert.That(exit.Name, Is.EqualTo("castle approach N"));
                Assert.That(arena.InCastle(arena.RegionId, exit.X, exit.Y), Is.False, "outside the safe circle");
                Assert.That(arena.CastleDistance(exit.X, exit.Y), Is.InRange(arena.SafeRadius + 500.0, 5_000.0));
                Assert.That(exit.Y, Is.LessThan(arena.CastleY), "north of the centre (y grows southward)");
                Assert.That(arena.CastleLandings.All(landing => arena.InCastle(arena.RegionId, landing.X, landing.Y)), Is.True,
                    "the landings are inside the circle");
            });
        }

        [Test]
        public void EveryCastleLandingIsInsideTheCircleForAnyBot()
        {
            GothwaitePvpArena arena = GothwaitePvpArena.Gothwaite;
            for (long bot = 1; bot <= 2_000; bot++)
            {
                GothwaitePvpSpot landing = arena.CastleLanding(bot);
                Assert.That(arena.InCastle(arena.RegionId, landing.X, landing.Y), Is.True, $"bot {bot}");
                Assert.That(arena.CastleDistance(landing.X, landing.Y), Is.LessThanOrEqualTo(arena.SafeRadius - 300), $"bot {bot} has no margin");
            }
        }

        [Test]
        public void TheProvenCastleLandingsHaveThreeHundredUnitsOfMarginAndTwelveToSixteenOfThem()
        {
            GothwaitePvpArena arena = GothwaitePvpArena.Gothwaite;

            Assert.Multiple(() =>
            {
                Assert.That(arena.CastleLandings.Count, Is.InRange(12, 16), "twelve to sixteen landings");
                foreach (GothwaitePvpSpot landing in arena.CastleLandings)
                    Assert.That(arena.CastleDistance(landing.X, landing.Y), Is.LessThanOrEqualTo(arena.SafeRadius - 300), landing.Name);
            });
        }

        [Test]
        public void TheCastleExitLiesInsideTheGateRingSoAGroupArrivesThereWithoutFighting()
        {
            GothwaitePvpArena arena = GothwaitePvpArena.Gothwaite;
            GothwaitePvpSpot exit = arena.CastleExit;

            Assert.That(arena.InGateRing(arena.RegionId, exit.X, exit.Y, 1_500), Is.True,
                "the exit (about 4,760 from the centre) is inside 5,500, so the leader opens nothing there and roams on");
        }

        // The former's tick (every TickMilliseconds) over a queue of due departures: each release is taken off the queue. Returns the releases.
        private static List<(long Time, GothwaitePvpDeparture Departure)> RunDepartures(List<GothwaitePvpDeparture> pending, long from, long to)
        {
            var gate = new GothwaitePvpDepartureGate();
            var released = new List<(long Time, GothwaitePvpDeparture Departure)>();
            for (long now = from; now <= to; now += GothwaitePvpGroupFormer.TickMilliseconds)
            {
                // The former releases while the gate allows it on this tick.
                while (gate.CanRelease(now))
                {
                    int index = gate.Release(now, pending);
                    if (index < 0)
                        break;
                    released.Add((now, pending[index]));
                    pending.RemoveAt(index);
                }
            }
            return released;
        }

        [Test]
        public void ADepartureGateReleasesOneDepartureAndNoOtherUntilTheIntervalPasses()
        {
            var gate = new GothwaitePvpDepartureGate();
            var queue = new[] { new GothwaitePvpDeparture(0, false, 1), new GothwaitePvpDeparture(0, false, 2) };

            Assert.Multiple(() =>
            {
                Assert.That(gate.Release(0, queue), Is.EqualTo(0), "the first due departure leaves at once");
                Assert.That(gate.Release(999, queue), Is.EqualTo(-1), "no second departure within one second");
                Assert.That(gate.Release(1_000, queue), Is.EqualTo(0), "the next one leaves one second after the first");
                Assert.That(gate.NextAt, Is.EqualTo(2_000));
                Assert.That(gate.Release(1_500, Array.Empty<GothwaitePvpDeparture>()), Is.EqualTo(-1), "nobody is waiting, so the clock does not move");
                Assert.That(gate.NextAt, Is.EqualTo(2_000));
            });
        }

        [Test]
        public void EveryWaitingDepartureLeavesTwoPerFormerTickAndNoneIsLost()
        {
            var pending = Enumerable.Range(1, 10).Select(id => new GothwaitePvpDeparture(0, Solo: id % 2 == 0, BotId: id)).ToList();

            // A game clock that has run for a while (the gate's schedule starts at 0).
            List<(long Time, GothwaitePvpDeparture Departure)> released = RunDepartures(pending, 10_000, 130_000);

            Assert.Multiple(() =>
            {
                Assert.That(released, Has.Count.EqualTo(10), "ten departures");
                Assert.That(pending, Is.Empty, "every waiting departure leaves");
                Assert.That(released[0].Time, Is.EqualTo(10_000));
                Assert.That(released[^1].Time, Is.EqualTo(18_000), "two per 2-second tick: ten leave over five ticks");
                foreach (IGrouping<long, (long Time, GothwaitePvpDeparture Departure)> tick in released.GroupBy(entry => entry.Time))
                    Assert.That(tick.Count(), Is.EqualTo(GothwaitePvpGroupFormer.TickMilliseconds / GothwaitePvpDepartureGate.IntervalMilliseconds),
                        $"tick {tick.Key}");
            });
        }

        [Test]
        public void AnIdleGateBuildsNoBurst()
        {
            var gate = new GothwaitePvpDepartureGate();
            var queue = new[] { new GothwaitePvpDeparture(0, false, 1) };
            gate.Release(10_000, queue);
            int releasedAfterIdle = 0;
            // A minute with nobody waiting, then one tick with a long queue: still only two leave.
            while (gate.CanRelease(60_000) && gate.Release(60_000, queue) >= 0)
                releasedAfterIdle++;

            Assert.That(releasedAfterIdle, Is.EqualTo(2));
        }

        [Test]
        public void TheEarliestDueDepartureLeavesFirstAndAGroupGoesBeforeASoloOnATie()
        {
            var pending = new List<GothwaitePvpDeparture>
            {
                new(6_000, false, 9),   // a group that became due last
                new(2_000, true, 5),    // a solo that became ready first
                new(2_000, false, 7),   // a group due on the same tick as the solo: it goes first
            };

            List<(long Time, GothwaitePvpDeparture Departure)> released = RunDepartures(pending, 2_000, 20_000);

            Assert.That(released.Select(entry => entry.Departure.BotId), Is.EqualTo(new long[] { 7, 5, 9 }),
                "the group that was due first, then the solo, then the later group");
        }

        private static Dictionary<GothwaitePvpShape, int> CountShapes(GothwaitePvpGroupShares shares, Func<int, long> botId, Func<int, int> cycle)
        {
            var counts = Enum.GetValues<GothwaitePvpShape>().ToDictionary(shape => shape, _ => 0);
            for (int i = 0; i < Draws; i++)
                counts[GothwaitePvpShapes.Draw(botId(i), cycle(i), shares)]++;
            return counts;
        }

        private static void AssertShare(Dictionary<GothwaitePvpShape, int> counts, GothwaitePvpShape shape, int percent)
        {
            double actual = counts[shape] / (double)Draws;
            double expected = percent / 100.0;
            Assert.That(Math.Abs(actual - expected), Is.LessThanOrEqualTo(Tolerance),
                $"{shape}: {actual:P2} against the {expected:P0} share");
        }
    }
}
