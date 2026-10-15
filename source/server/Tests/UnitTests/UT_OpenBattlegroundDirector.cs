using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    // open BG phase machine (spec 2026-10-11 §6): quiet 30 %, push 20 %, keep fight 35 %, retreat 15 % of a random
    // 15-45 game-minute cycle. Each cycle's keep raid (owner decision 2026-10-11) is a set of units of any realm that
    // are not friends of the keep, the guilds with the fewest recent wins the likeliest; realm plays no part.
    [TestFixture]
    public class UT_OpenBattlegroundDirector
    {
        private static readonly IReadOnlyDictionary<string, int> NoWins = new Dictionary<string, int>();
        private const string Ashen = "Ashen Concord", Farmers = "Bandit Farmers", Gatekeepers = "Gatekeepers", Wardens = "Night Wardens";

        private static OpenBgRaidCandidate Unit(int id, int bots, string guild, eRealm realm, bool friend = false) =>
            new(id, bots, bots >= OpenBattlegroundRoster.Group8Minimum, guild, realm, friend);

        // A full crew of 54: per realm one 8-man, two small groups of three and four solos, spread over four guilds.
        // Bandit Farmers (Midgard units 20-26) holds the keep in the tests that pass farmersOwnKeep.
        private static OpenBgRaidCandidate[] Crew(bool farmersOwnKeep = false)
        {
            var units = new List<OpenBgRaidCandidate>();
            (eRealm Realm, int Base, string Guild)[] realms =
            {
                (eRealm.Albion, 10, Ashen), (eRealm.Midgard, 20, Farmers), (eRealm.Hibernia, 30, Gatekeepers),
            };
            foreach (var (realm, first, guild) in realms)
            {
                bool friend = farmersOwnKeep && guild == Farmers;
                units.Add(Unit(first, 8, guild, realm, friend));
                units.Add(Unit(first + 1, 3, guild, realm, friend));
                units.Add(Unit(first + 2, 3, Wardens, realm));
                for (int solo = 3; solo < 7; solo++) units.Add(Unit(first + solo, 1, solo % 2 == 0 ? guild : Wardens, realm, friend && solo % 2 == 0));
            }
            return units.ToArray();
        }

        [Test]
        public void CycleLengthIsWithinBounds()
        {
            var rng = new Random(11);
            for (int i = 0; i < 1_000; i++)
            {
                OpenBgCycle cycle = OpenBattlegroundDirector.NewCycle(rng, 15, 45);
                Assert.That(cycle.TotalMs, Is.InRange(15 * 60_000L, 45 * 60_000L));
                Assert.That(new[] { cycle.QuietMs, cycle.PushMs, cycle.FightMs, cycle.RetreatMs }, Has.All.GreaterThan(0));
            }
        }

        [Test]
        public void CycleBoundsAreOrderedAndUsable()
        {
            var rng = new Random(3);
            OpenBgCycle swapped = OpenBattlegroundDirector.NewCycle(rng, 30, 20);
            OpenBgCycle fixedLength = OpenBattlegroundDirector.NewCycle(rng, 5, 5);
            Assert.Multiple(() =>
            {
                Assert.That(swapped.TotalMs, Is.InRange(20 * 60_000L, 30 * 60_000L));
                Assert.That(fixedLength.TotalMs, Is.EqualTo(5 * 60_000L));
                Assert.That(swapped.StartTick, Is.EqualTo(0), "the caller sets the start tick");
                Assert.That(swapped.Raid, Is.SameAs(OpenBgRaid.None), "the raid forms at the push, not at the cycle start");
            });
        }

        [Test]
        public void PhaseSharesMatchSpec()
        {
            var rng = new Random(5);
            double[] expected = { 0.30, 0.20, 0.35, 0.15 };
            double[] mean = new double[4];
            const int draws = 1_000;
            for (int i = 0; i < draws; i++)
            {
                OpenBgCycle cycle = OpenBattlegroundDirector.NewCycle(rng, 15, 45);
                double[] shares = { cycle.QuietMs, cycle.PushMs, cycle.FightMs, cycle.RetreatMs };
                for (int phase = 0; phase < 4; phase++)
                {
                    double share = shares[phase] / cycle.TotalMs;
                    Assert.That(share, Is.EqualTo(expected[phase]).Within(0.05), $"phase {phase} draw {i}");
                    mean[phase] += share / draws;
                }
            }
            for (int phase = 0; phase < 4; phase++)
                Assert.That(mean[phase], Is.EqualTo(expected[phase]).Within(0.005), $"mean share of phase {phase}");
        }

        [Test]
        public void RaidNeverIncludesAKeepFriend()
        {
            // Bandit Farmers (a bot or a human guild alike) holds the keep: its units defend and never join the raid.
            var rng = new Random(7);
            OpenBgRaidCandidate[] crew = Crew(farmersOwnKeep: true);
            int[] friends = crew.Where(unit => unit.KeepFriend).Select(unit => unit.Unit).ToArray();
            Assert.That(friends, Is.Not.Empty);
            for (int i = 0; i < 500; i++)
            {
                OpenBgRaid raid = OpenBattlegroundDirector.PickRaid(rng, crew, NoWins);
                Assert.That(raid.Units.Intersect(friends), Is.Empty, $"pick {i}");
                Assert.That(raid.LeadGuild, Is.Not.EqualTo(Farmers));
                Assert.That(raid.Units, Does.Contain(raid.LeadUnit));
                Assert.That(raid.Bots, Is.EqualTo(crew.Where(unit => raid.Contains(unit.Unit)).Sum(unit => unit.Bots)));
            }
        }

        [Test]
        public void RaidMixesRealmsAndIgnoresThem()
        {
            var rng = new Random(19);
            OpenBgRaidCandidate[] crew = Crew();
            var leadRealms = new HashSet<eRealm>();
            int mixed = 0;
            for (int i = 0; i < 400; i++)
            {
                OpenBgRaid raid = OpenBattlegroundDirector.PickRaid(rng, crew, NoWins);
                leadRealms.Add(raid.LeadRealm);
                if (crew.Where(unit => raid.Contains(unit.Unit)).Select(unit => unit.Realm).Distinct().Count() > 1) mixed++;
            }
            Assert.Multiple(() =>
            {
                Assert.That(leadRealms, Is.EquivalentTo(new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }), "any realm may lead");
                Assert.That(mixed, Is.GreaterThan(300), "a raid takes units across realms");
            });
        }

        [Test]
        public void RaidPrefersGuildsWithFewestRecentWins()
        {
            var rng = new Random(13);
            OpenBgRaidCandidate[] crew = Crew();
            var wins = new Dictionary<string, int> { [Ashen] = 3, ["bandit farmers"] = 5, [Gatekeepers] = 0 };
            int gatekeepers = 0, ashen = 0;
            for (int i = 0; i < 1_000; i++)
            {
                string lead = OpenBattlegroundDirector.PickRaid(rng, crew, wins).LeadGuild;
                if (lead == Gatekeepers) gatekeepers++;
                else if (lead == Ashen) ashen++;
            }
            Assert.Multiple(() =>
            {
                Assert.That(gatekeepers, Is.GreaterThan(850), "the 8-man of the guild without recent wins leads most raids");
                Assert.That(ashen, Is.GreaterThan(0), "a weighted pick, never a fixed rotation");
                Assert.That(OpenBattlegroundDirector.WinsOf("Bandit Farmers", wins), Is.EqualTo(5), "guild names compare case-insensitively");
                Assert.That(OpenBattlegroundDirector.WinsOf(null, wins), Is.EqualTo(0), "an unguilded unit has no wins");
            });
        }

        [Test]
        public void RaidTakesAThirdOfTheCrewAndAtLeastEightBots()
        {
            var rng = new Random(23);
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundDirector.RaidTargetBots(54, 54), Is.EqualTo(18));
                Assert.That(OpenBattlegroundDirector.RaidTargetBots(55, 40), Is.EqualTo(19), "a third, rounded up");
                Assert.That(OpenBattlegroundDirector.RaidTargetBots(12, 10), Is.EqualTo(8), "at least eight where that many may raid");
                Assert.That(OpenBattlegroundDirector.RaidTargetBots(12, 5), Is.EqualTo(5), "never more than may raid");
                Assert.That(OpenBattlegroundDirector.RaidTargetBots(0, 0), Is.EqualTo(0));
                for (int i = 0; i < 300; i++)
                {
                    OpenBgRaid full = OpenBattlegroundDirector.PickRaid(rng, Crew(), NoWins);
                    Assert.That(full.Bots, Is.GreaterThanOrEqualTo(18), "a third of 54");
                    Assert.That(full.Bots, Is.LessThan(18 + 8), "the pick stops once the raid is big enough");
                }
                OpenBgRaidCandidate[] few = { Unit(1, 3, Ashen, eRealm.Albion), Unit(2, 2, Gatekeepers, eRealm.Hibernia), Unit(3, 1, null, eRealm.Midgard) };
                OpenBgRaid all = OpenBattlegroundDirector.PickRaid(rng, few, NoWins);
                Assert.That(all.Units, Is.EquivalentTo(new[] { 1, 2, 3 }), "fewer than eight may raid: all of them go");
                Assert.That(all.Bots, Is.EqualTo(6));
            });
        }

        [Test]
        public void RaidIncludesAnEightManWhenOneMayRaid()
        {
            var rng = new Random(29);
            OpenBgRaidCandidate[] crew = Crew(farmersOwnKeep: true);
            int[] eligibleEights = crew.Where(unit => unit.Group8 && !unit.KeepFriend).Select(unit => unit.Unit).ToArray();
            for (int i = 0; i < 300; i++)
            {
                OpenBgRaid raid = OpenBattlegroundDirector.PickRaid(rng, crew, NoWins);
                Assert.That(eligibleEights, Does.Contain(raid.LeadUnit), "an eligible 8-man leads");
            }
            OpenBgRaidCandidate[] smallOnly = { Unit(1, 3, Ashen, eRealm.Albion), Unit(2, 3, Gatekeepers, eRealm.Hibernia), Unit(3, 4, Wardens, eRealm.Midgard),
                Unit(4, 8, Farmers, eRealm.Midgard, friend: true) };
            OpenBgRaid small = OpenBattlegroundDirector.PickRaid(rng, smallOnly, NoWins);
            Assert.Multiple(() =>
            {
                Assert.That(small.Units, Does.Not.Contain(4), "the keep friend's 8-man stays out");
                Assert.That(small.Bots, Is.GreaterThanOrEqualTo(8));
                Assert.That(new[] { 1, 2, 3 }, Does.Contain(small.LeadUnit));
            });
        }

        [Test]
        public void NoCandidatesNoRaid()
        {
            var rng = new Random(31);
            OpenBgRaidCandidate[] allFriends = { Unit(1, 8, Farmers, eRealm.Midgard, true), Unit(2, 3, Farmers, eRealm.Albion, true) };
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundDirector.PickRaid(rng, Array.Empty<OpenBgRaidCandidate>(), NoWins), Is.SameAs(OpenBgRaid.None));
                Assert.That(OpenBattlegroundDirector.PickRaid(rng, null, null), Is.SameAs(OpenBgRaid.None));
                Assert.That(OpenBattlegroundDirector.PickRaid(rng, allFriends, NoWins), Is.SameAs(OpenBgRaid.None), "only keep friends inside");
                Assert.That(OpenBgRaid.None.IsEmpty, Is.True);
                Assert.That(OpenBgRaid.None.Contains(1), Is.False);
                Assert.That(OpenBgRaid.None.Summary, Is.EqualTo("none"));
            });
        }

        [Test]
        public void RaidSummaryFillsTheStatusAttackerField()
        {
            var raid = new OpenBgRaid(new[] { 10, 21, 32 }, 10, 18, Ashen, eRealm.Albion);
            Assert.Multiple(() =>
            {
                Assert.That(raid.Summary, Is.EqualTo("raid 3 units/18 bots, lead Ashen Concord"));
                Assert.That(new OpenBgRaid(new[] { 5 }, 5, 1, null, eRealm.Hibernia).Summary, Is.EqualTo("raid 1 units/1 bots, lead unguilded"));
            });
        }

        [Test]
        public void RaidIsFormedAnewOnlyWhenTheKeepChangedHandsToIt()
        {
            OpenBgRaidCandidate[] crew = Crew();
            var raid = new OpenBgRaid(new[] { 10, 11 }, 10, 11, Ashen, eRealm.Albion);
            OpenBgRaidCandidate[] ashenOwnKeep = crew.Select(unit => unit with { KeepFriend = unit.Guild == Ashen }).ToArray();
            OpenBgRaidCandidate[] raidGone = crew.Where(unit => !raid.Contains(unit.Unit)).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundDirector.RaidNeedsRepick(raid, crew), Is.False, "a standing raid is topped up, not replaced");
                Assert.That(OpenBattlegroundDirector.RaidNeedsRepick(raid, ashenOwnKeep), Is.True, "the keep changed hands to the raid");
                Assert.That(OpenBattlegroundDirector.RaidNeedsRepick(raid, raidGone), Is.False, "every raid unit relieved: a top-up refills it");
                Assert.That(OpenBattlegroundDirector.RaidNeedsRepick(OpenBgRaid.None, crew), Is.False);
                Assert.That(OpenBattlegroundDirector.RaidTookKeep(OpenBgPhase.KeepFight, raid, ashenOwnKeep), Is.True);
                Assert.That(OpenBattlegroundDirector.RaidTookKeep(OpenBgPhase.Retreat, raid, ashenOwnKeep), Is.True);
                Assert.That(OpenBattlegroundDirector.RaidTookKeep(OpenBgPhase.KeepFight, raid, crew), Is.False);
                Assert.That(OpenBattlegroundDirector.RaidTookKeep(OpenBgPhase.Push, raid, ashenOwnKeep), Is.False, "before the fight it is a re-pick");
            });
        }

        [Test]
        public void RaidFormsWhenThePushStartsNotOnTheFirstTick()
        {
            // Review 2026-10-11: the first director tick runs 10 s after a start with nobody inside; a raid picked then
            // was whatever unit entered first and never grew, and a raid under four never sieges.
            var rng = new Random(37);
            OpenBgRaidCandidate[] crew = Crew();
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundDirector.RaidForTick(rng, OpenBgPhase.Quiet, OpenBgRaid.None, Array.Empty<OpenBgRaidCandidate>(), NoWins,
                    out string reason), Is.SameAs(OpenBgRaid.None), "first tick, nobody inside");
                Assert.That(reason, Is.Null);
                Assert.That(OpenBattlegroundDirector.RaidForTick(rng, OpenBgPhase.Quiet, OpenBgRaid.None, crew, NoWins, out reason),
                    Is.SameAs(OpenBgRaid.None), "quiet has no raid roles, so no raid");
                Assert.That(OpenBattlegroundDirector.RaidForTick(rng, OpenBgPhase.Push, OpenBgRaid.None, Array.Empty<OpenBgRaidCandidate>(), NoWins,
                    out reason), Is.SameAs(OpenBgRaid.None), "push with nobody inside");
                OpenBgRaid full = OpenBattlegroundDirector.RaidForTick(rng, OpenBgPhase.Push, OpenBgRaid.None, crew, NoWins, out reason);
                Assert.That(reason, Is.EqualTo("push"));
                Assert.That(full.Bots, Is.GreaterThanOrEqualTo(18), "the push start picks a full raid: a third of 54");
                Assert.That(OpenBattlegroundDirector.RaidForTick(rng, OpenBgPhase.Push, full, crew, NoWins, out reason), Is.SameAs(full),
                    "a raid at its target stays as it is");
                Assert.That(reason, Is.Null);
            });
        }

        [Test]
        public void RaidIsToppedUpThroughThePushOnly()
        {
            var rng = new Random(41);
            OpenBgRaidCandidate[] crew = Crew();
            OpenBgRaidCandidate[] firstIn = crew.Where(unit => unit.Unit == 11).ToArray();
            OpenBgRaid small = OpenBattlegroundDirector.RaidForTick(rng, OpenBgPhase.Push, OpenBgRaid.None, firstIn, NoWins, out _);
            Assert.That(small.Units, Is.EqualTo(new[] { 11 }), "only one 3-man inside at the push start");

            OpenBgRaid grown = OpenBattlegroundDirector.RaidForTick(rng, OpenBgPhase.Push, small, crew, NoWins, out string reason);
            Assert.Multiple(() =>
            {
                Assert.That(reason, Is.EqualTo("top_up"));
                Assert.That(grown.Units, Does.Contain(11), "the standing raid keeps its units");
                Assert.That(grown.Bots, Is.GreaterThanOrEqualTo(18), "units that entered later join while the raid is below target");
                Assert.That(grown.LeadUnit, Is.EqualTo(11), "the lead stays while its unit is inside");
                Assert.That(crew.Where(unit => grown.Contains(unit.Unit)).Any(unit => unit.Group8), Is.True, "an 8-man joins a raid without one");
                foreach (OpenBgPhase phase in new[] { OpenBgPhase.KeepFight, OpenBgPhase.Retreat })
                {
                    Assert.That(OpenBattlegroundDirector.RaidForTick(rng, phase, small, crew, NoWins, out reason), Is.SameAs(small),
                        $"no top-up in {phase}");
                    Assert.That(reason, Is.Null);
                }
            });

            // Reliefs shrink the raid below target: the push tops it up again and drops the units that left.
            int[] relieved = grown.Units.Where(id => id != grown.LeadUnit).Take(2).ToArray();
            OpenBgRaidCandidate[] afterRelief = crew.Where(unit => !relieved.Contains(unit.Unit)).ToArray();
            OpenBgRaid refilled = OpenBattlegroundDirector.RaidForTick(rng, OpenBgPhase.Push, grown, afterRelief, NoWins, out reason);
            Assert.Multiple(() =>
            {
                if (OpenBattlegroundDirector.RaidBotsInside(grown, afterRelief) < OpenBattlegroundDirector.RaidTargetBots(
                        afterRelief.Sum(unit => unit.Bots), afterRelief.Sum(unit => unit.Bots)))
                {
                    Assert.That(reason, Is.EqualTo("top_up"));
                    Assert.That(refilled.Units.Intersect(relieved), Is.Empty, "relieved units leave the raid");
                }
                Assert.That(OpenBattlegroundDirector.RaidBotsInside(refilled, afterRelief), Is.GreaterThanOrEqualTo(
                    OpenBattlegroundDirector.RaidTargetBots(afterRelief.Sum(unit => unit.Bots), afterRelief.Sum(unit => unit.Bots))));
            });

            // Every raid unit relieved: a new raid forms from the units inside.
            OpenBgRaidCandidate[] noneLeft = crew.Where(unit => !grown.Contains(unit.Unit)).ToArray();
            OpenBgRaid fresh = OpenBattlegroundDirector.RaidForTick(rng, OpenBgPhase.Push, grown, noneLeft, NoWins, out reason);
            Assert.Multiple(() =>
            {
                Assert.That(reason, Is.EqualTo("no_raid_inside"));
                Assert.That(fresh.Units.Intersect(grown.Units), Is.Empty);
                Assert.That(fresh.Units, Does.Contain(fresh.LeadUnit));
            });

            // The keep changed hands to a raid unit in the push: a new raid without the new keep friends.
            OpenBgRaidCandidate[] ashenOwnKeep = crew.Select(unit => unit with { KeepFriend = unit.Guild == Ashen }).ToArray();
            OpenBgRaid changed = OpenBattlegroundDirector.RaidForTick(rng, OpenBgPhase.Push, small, ashenOwnKeep, NoWins, out reason);
            Assert.Multiple(() =>
            {
                Assert.That(reason, Is.EqualTo("keep_changed"));
                Assert.That(ashenOwnKeep.Where(unit => unit.KeepFriend).Select(unit => unit.Unit).Intersect(changed.Units), Is.Empty);
            });
        }

        [Test]
        public void PhaseAtWalksAllFourPhasesInOrder()
        {
            var cycle = new OpenBgCycle(10_000, 300, 200, 350, 150, OpenBgRaid.None);
            Assert.Multiple(() =>
            {
                Assert.That(cycle.EndTick, Is.EqualTo(11_000));
                Assert.That(OpenBattlegroundDirector.PhaseAt(cycle, 9_000), Is.EqualTo(OpenBgPhase.Quiet), "before the start");
                Assert.That(OpenBattlegroundDirector.PhaseAt(cycle, 10_000), Is.EqualTo(OpenBgPhase.Quiet));
                Assert.That(OpenBattlegroundDirector.PhaseAt(cycle, 10_299), Is.EqualTo(OpenBgPhase.Quiet));
                Assert.That(OpenBattlegroundDirector.PhaseAt(cycle, 10_300), Is.EqualTo(OpenBgPhase.Push));
                Assert.That(OpenBattlegroundDirector.PhaseAt(cycle, 10_499), Is.EqualTo(OpenBgPhase.Push));
                Assert.That(OpenBattlegroundDirector.PhaseAt(cycle, 10_500), Is.EqualTo(OpenBgPhase.KeepFight));
                Assert.That(OpenBattlegroundDirector.PhaseAt(cycle, 10_849), Is.EqualTo(OpenBgPhase.KeepFight));
                Assert.That(OpenBattlegroundDirector.PhaseAt(cycle, 10_850), Is.EqualTo(OpenBgPhase.Retreat));
                Assert.That(OpenBattlegroundDirector.PhaseAt(cycle, 10_999), Is.EqualTo(OpenBgPhase.Retreat));
                Assert.That(OpenBattlegroundDirector.PhaseAt(null, 10_000), Is.EqualTo(OpenBgPhase.Quiet));
            });
        }

        [Test]
        public void AKeepChangeEndsThePushAndTheFightInTheRetreat()
        {
            // Live run 2026-10-15: every claim re-picked a raid at once and the keep changed hands every few game minutes.
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundDirector.PhaseAfterKeepChange(OpenBgPhase.Push, true), Is.EqualTo(OpenBgPhase.Retreat));
                Assert.That(OpenBattlegroundDirector.PhaseAfterKeepChange(OpenBgPhase.KeepFight, true), Is.EqualTo(OpenBgPhase.Retreat));
                Assert.That(OpenBattlegroundDirector.PhaseAfterKeepChange(OpenBgPhase.Quiet, true), Is.EqualTo(OpenBgPhase.Quiet));
                Assert.That(OpenBattlegroundDirector.PhaseAfterKeepChange(OpenBgPhase.Retreat, true), Is.EqualTo(OpenBgPhase.Retreat));
                foreach (OpenBgPhase phase in new[] { OpenBgPhase.Quiet, OpenBgPhase.Push, OpenBgPhase.KeepFight, OpenBgPhase.Retreat })
                    Assert.That(OpenBattlegroundDirector.PhaseAfterKeepChange(phase, false), Is.EqualTo(phase), phase + " without a change");
            });

            // The cycle keeps its start and its retreat length; the phases before it end at the change.
            var cycle = new OpenBgCycle(10_000, 300, 200, 350, 150, OpenBgRaid.None);
            OpenBgCycle inPush = OpenBattlegroundDirector.CycleRetreatingAt(cycle, 10_400);
            OpenBgCycle inFight = OpenBattlegroundDirector.CycleRetreatingAt(cycle, 10_600);
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundDirector.PhaseAt(inPush, 10_399), Is.EqualTo(OpenBgPhase.Push));
                Assert.That(OpenBattlegroundDirector.PhaseAt(inPush, 10_400), Is.EqualTo(OpenBgPhase.Retreat));
                Assert.That(inPush.EndTick, Is.EqualTo(10_550), "the retreat lasts its normal length");
                Assert.That(OpenBattlegroundDirector.PhaseAt(inFight, 10_599), Is.EqualTo(OpenBgPhase.KeepFight));
                Assert.That(OpenBattlegroundDirector.PhaseAt(inFight, 10_600), Is.EqualTo(OpenBgPhase.Retreat));
                Assert.That(inFight.EndTick, Is.EqualTo(10_750));
                Assert.That(OpenBattlegroundDirector.CycleRetreatingAt(cycle, 10_900).EndTick, Is.EqualTo(11_000), "already in the retreat: unchanged");
                Assert.That(OpenBattlegroundDirector.CycleRetreatingAt(cycle, 10_100).EndTick, Is.EqualTo(10_250), "in the quiet: the phases up to now, then the retreat");
            });
        }

        [Test]
        public void OwnerFollowsTheClaimingGuild()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundDirector.OwnerRealm(false, false, eRealm.None, eRealm.Albion), Is.EqualTo(eRealm.None), "no guild");
                Assert.That(OpenBattlegroundDirector.OwnerRealm(true, true, eRealm.None, eRealm.Midgard), Is.EqualTo(eRealm.None), "Frontier Wardens");
                Assert.That(OpenBattlegroundDirector.OwnerRealm(true, false, eRealm.Midgard, eRealm.Albion), Is.EqualTo(eRealm.Midgard));
                Assert.That(OpenBattlegroundDirector.OwnerRealm(true, false, eRealm.None, eRealm.Albion), Is.EqualTo(eRealm.Albion), "realmless guild");
                Assert.That(OpenBattlegroundDirector.OwnerGuild(true, false, Farmers), Is.EqualTo(Farmers), "a human or bot guild");
                Assert.That(OpenBattlegroundDirector.OwnerGuild(true, true, "Frontier Wardens"), Is.Null, "the garrison is nobody's win");
                Assert.That(OpenBattlegroundDirector.OwnerGuild(false, false, null), Is.Null);
            });
        }

        [Test]
        public void CycleWinnerIsAGuild()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundDirector.CycleWinner(Ashen, false, Ashen), Is.EqualTo(Ashen), "the raid's guild took the keep");
                Assert.That(OpenBattlegroundDirector.CycleWinner(Gatekeepers, false, Ashen), Is.EqualTo(Gatekeepers),
                    "another raid guild claimed it, or the holder kept it");
                Assert.That(OpenBattlegroundDirector.CycleWinner(null, true, Ashen), Is.EqualTo(Ashen), "lord down, unclaimed: the raid's lead");
                Assert.That(OpenBattlegroundDirector.CycleWinner(null, false, Ashen), Is.Null, "the garrison held");
                Assert.That(OpenBattlegroundDirector.CycleWinner(null, true, null), Is.Null);
            });
        }

        [Test]
        public void UnchangedAssignmentIsReloggedOncePerFiveGameMinutes()
        {
            const long logged = 1_000_000;
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundDirector.LogsRoleAgain(false, logged, logged + 30_000), Is.True, "a new assignment always logs");
                Assert.That(OpenBattlegroundDirector.LogsRoleAgain(true, logged, logged + 30_000), Is.False,
                    "the unreachable bridge picked again after 30 s stays quiet");
                Assert.That(OpenBattlegroundDirector.LogsRoleAgain(true, logged, logged + 5 * 60_000), Is.True);
                Assert.That(OpenBattlegroundDirector.LogsRoleAgain(false, 0, 0), Is.True, "first assignment");
            });
        }
    }
}
