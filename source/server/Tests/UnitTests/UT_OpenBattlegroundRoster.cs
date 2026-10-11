using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    // open BG roster (spec 2026-10-11 §5): a fixed crew per realm (8-man groups, small groups, solos) from the
    // 40-50 band, under one hard cap, relieved in part every 20-30 game minutes; brackets are never touched.
    [TestFixture]
    public class UT_OpenBattlegroundRoster
    {
        private static readonly eRealm[] Realms = { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };
        private static readonly OpenBgSetup Defaults = new(1, 2, 4, 72);

        private static IReadOnlyList<RealmCount> EmptyMap() => Realms.Select(RealmCount.Empty).ToArray();

        // A realm at the default setup, every unit inside: one 8-man (oldest unless solosFirst), two small groups, four solos.
        private static RealmCount FullRealm(eRealm realm, int idBase, bool due, bool solosFirst = false, int solos = 4)
        {
            var units = new List<OpenBgUnit>
            {
                new(idBase + 1, OpenBgUnitKind.Group8, 8, true, solosFirst ? 5_000 : 1_000),
                new(idBase + 2, OpenBgUnitKind.Small, 3, true, 2_000),
                new(idBase + 3, OpenBgUnitKind.Small, 2, true, 3_000),
            };
            for (int i = 0; i < solos; i++)
                units.Add(new(idBase + 10 + i, OpenBgUnitKind.Solo, 1, true, solosFirst ? 500 + i : 4_000 + i));
            return new RealmCount(realm, units, 0, due);
        }

        [Test]
        public void PlanFillsEachRealmToSetup()
        {
            OpenBgPlan plan = OpenBattlegroundRoster.Plan(Defaults, EmptyMap());
            Assert.Multiple(() =>
            {
                foreach (eRealm realm in Realms)
                {
                    OpenBgRealmPlan mine = plan.For(realm);
                    Assert.That(mine.AddGroups8, Is.EqualTo(1), $"{realm} 8-man groups");
                    Assert.That(mine.AddSmall, Is.EqualTo(2), $"{realm} small groups");
                    Assert.That(mine.AddSolos, Is.EqualTo(4), $"{realm} solos");
                    Assert.That(mine.Relieve, Is.Empty, $"{realm} relieves nothing on an empty map");
                }
                Assert.That(plan.Admits, Has.Count.EqualTo(21));
                Assert.That(plan.CapBinds, Is.False, "54 bots (at most) fit the default cap of 72");
                Assert.That(plan.Cap, Is.EqualTo(72));
            });
        }

        [Test]
        public void PlanCompletesAPartlyFilledRealmOnly()
        {
            RealmCount albion = new(eRealm.Albion, new OpenBgUnit[]
            {
                new(1, OpenBgUnitKind.Group8, 8, true, 100),
                new(2, OpenBgUnitKind.Solo, 1, true, 100),
            });
            OpenBgPlan plan = OpenBattlegroundRoster.Plan(Defaults,
                new[] { albion, RealmCount.Empty(eRealm.Midgard), RealmCount.Empty(eRealm.Hibernia) });
            Assert.Multiple(() =>
            {
                Assert.That(plan.For(eRealm.Albion).AddGroups8, Is.Zero);
                Assert.That(plan.For(eRealm.Albion).AddSmall, Is.EqualTo(2));
                Assert.That(plan.For(eRealm.Albion).AddSolos, Is.EqualTo(3));
                Assert.That(plan.Present, Is.EqualTo(9));
            });
        }

        [Test]
        public void PlanStopsAtTheCap()
        {
            // Review focus 3: cap 24 below the setup sum (54): admits stop at the cap in realm round-robin, no exception.
            OpenBgPlan plan = null;
            Assert.DoesNotThrow(() => plan = OpenBattlegroundRoster.Plan(Defaults with { MaxBots = 24 }, EmptyMap()));
            int booked = plan.Admits.Sum(admit => OpenBattlegroundRoster.PlannedSize(admit.Kind));
            Assert.Multiple(() =>
            {
                Assert.That(booked, Is.LessThanOrEqualTo(24));
                Assert.That(booked, Is.EqualTo(24), "the cap is used up");
                Assert.That(plan.CapBinds, Is.True);
                Assert.That(plan.Admits.Take(3).Select(admit => admit.Realm), Is.EqualTo(Realms), "realm round-robin");
                Assert.That(plan.Admits.Take(3).Select(admit => admit.Kind), Is.All.EqualTo(OpenBgUnitKind.Group8));
            });

            // A small remainder still takes units that fit; a full map takes none.
            OpenBgPlan nearlyFull = OpenBattlegroundRoster.Plan(Defaults with { MaxBots = 19 },
                new[] { FullRealm(eRealm.Albion, 0, false), RealmCount.Empty(eRealm.Midgard), RealmCount.Empty(eRealm.Hibernia) });
            OpenBgPlan full = OpenBattlegroundRoster.Plan(Defaults with { MaxBots = 8 },
                new[] { FullRealm(eRealm.Albion, 0, false), RealmCount.Empty(eRealm.Midgard), RealmCount.Empty(eRealm.Hibernia) });
            Assert.Multiple(() =>
            {
                Assert.That(nearlyFull.Admits.Sum(admit => OpenBattlegroundRoster.PlannedSize(admit.Kind)), Is.LessThanOrEqualTo(19 - 17));
                Assert.That(nearlyFull.Admits, Is.Not.Empty, "two seats left still take a unit");
                Assert.That(full.Admits, Is.Empty, "above the cap: nothing is admitted");
                Assert.That(full.CapBinds, Is.True);
                Assert.That(OpenBattlegroundRoster.Plan(Defaults with { MaxBots = 0 }, EmptyMap()).Admits, Is.Empty);
            });
        }

        [Test]
        public void ReliefStillRotatesWhenTheCapBinds()
        {
            OpenBgPlan plan = OpenBattlegroundRoster.Plan(Defaults with { MaxBots = 17 },
                new[] { FullRealm(eRealm.Albion, 0, true), RealmCount.Empty(eRealm.Midgard), RealmCount.Empty(eRealm.Hibernia) });
            Assert.Multiple(() =>
            {
                Assert.That(plan.For(eRealm.Albion).Relieve, Is.EqualTo(new[] { 1 }), "the 8-man is relieved at the cap");
                Assert.That(plan.Admits, Is.Empty, "relieved bots hold their seats until they have left");
            });
        }

        [Test]
        public void PlanRelievesOneUnitPerRealmWhenDue()
        {
            OpenBgPlan plan = OpenBattlegroundRoster.Plan(Defaults with { Solos = 8 }, new[]
            {
                FullRealm(eRealm.Albion, 0, due: true),
                FullRealm(eRealm.Midgard, 100, due: true, solosFirst: true, solos: 8),
                FullRealm(eRealm.Hibernia, 200, due: false),
            });
            Assert.Multiple(() =>
            {
                Assert.That(plan.For(eRealm.Albion).Relieve, Is.EqualTo(new[] { 1 }), "the longest-serving group");
                Assert.That(plan.For(eRealm.Albion).AddGroups8, Is.EqualTo(1), "its seat is refilled");
                Assert.That(plan.For(eRealm.Midgard).Relieve, Is.EqualTo(new[] { 110, 111 }), "a quarter of eight solos, oldest first");
                Assert.That(plan.For(eRealm.Hibernia).Relieve, Is.Empty, "not due");
            });
        }

        [Test]
        public void ReliefNeverPicksAUnitStillTravelling()
        {
            RealmCount albion = new(eRealm.Albion, new OpenBgUnit[]
            {
                new(1, OpenBgUnitKind.Group8, 8, Inside: false),
                new(2, OpenBgUnitKind.Solo, 1, true, 5_000),
            }, 0, true);
            OpenBgPlan plan = OpenBattlegroundRoster.Plan(Defaults, new[] { albion });
            Assert.That(plan.For(eRealm.Albion).Relieve, Is.EqualTo(new[] { 2 }));
        }

        [Test]
        public void LoweredSetupRelievesTheSurplus()
        {
            OpenBgPlan plan = OpenBattlegroundRoster.Plan(new OpenBgSetup(0, 2, 2, 72),
                new[] { FullRealm(eRealm.Albion, 0, false) });
            Assert.That(plan.For(eRealm.Albion).Relieve, Is.EquivalentTo(new[] { 1, 13, 12 }),
                "the 8-man and the two most recently entered solos");
        }

        [Test]
        public void PlanShedsBotsWhenTheCapIsLowered()
        {
            // open_bg_max_bots lowered (72 -> 24) with 51 bots present at the setup: the surplus above the cap is
            // relieved, travelling units first, then the newest inside, spread over the realms.
            RealmCount albion = FullRealm(eRealm.Albion, 0, false);
            albion = albion with
            {
                Units = albion.Units.Select(unit => unit.Id == 13 ? unit with { Inside = false, InsideSinceTick = 0 } : unit).ToArray(),
            };
            RealmCount[] present = { albion, FullRealm(eRealm.Midgard, 100, false), FullRealm(eRealm.Hibernia, 200, false) };
            OpenBgPlan plan = OpenBattlegroundRoster.Plan(Defaults with { MaxBots = 24 }, present);
            int Kept(RealmCount realm) => realm.Units.Where(unit => !plan.For(realm.Realm).Relieve.Contains(unit.Id)).Sum(unit => unit.Size);
            Assert.Multiple(() =>
            {
                Assert.That(present.Sum(Kept), Is.LessThanOrEqualTo(24), "the bots that stay fit under the lowered cap");
                Assert.That(present.Sum(Kept), Is.GreaterThan(24 - OpenBattlegroundRoster.Group8Size), "no more than needed is shed");
                Assert.That(plan.For(eRealm.Albion).Relieve, Does.Contain(13), "the travelling solo goes first");
                Assert.That(plan.For(eRealm.Midgard).Relieve.First(), Is.EqualTo(113), "then the newest inside");
                foreach (RealmCount realm in present)
                {
                    Assert.That(Kept(realm), Is.EqualTo(8), $"{realm.Realm} keeps its share");
                    Assert.That(plan.For(realm.Realm).Relieve, Does.Not.Contain(realm.Units[0].Id), $"{realm.Realm} keeps its longest-serving 8-man");
                }
                Assert.That(plan.Admits, Is.Empty, "nothing is admitted above the cap");
            });

            // At or under the cap nothing is shed.
            OpenBgPlan atCap = OpenBattlegroundRoster.Plan(Defaults with { MaxBots = 51 }, present);
            Assert.That(atCap.Realms.SelectMany(realm => realm.Relieve), Is.Empty);
        }

        [Test]
        public void IsCandidateRejectsPlayerLedRelicAndLowLevel()
        {
            static bool Candidate(int level = 45, bool playerLed = false, bool relic = false, bool inBattleground = false,
                ushort region = 1, bool rvrTour = true, bool idle = false, bool autonomous = true) =>
                OpenBattlegroundRoster.IsCandidate(autonomous, level, playerLed, relic, inBattleground, region, rvrTour, idle);
            Assert.Multiple(() =>
            {
                Assert.That(Candidate(), Is.True, "level 45 on an RvR tour in Albion");
                Assert.That(Candidate(level: 40, region: 100), Is.True);
                Assert.That(Candidate(level: 50, region: 200), Is.True);
                Assert.That(Candidate(rvrTour: false, idle: true), Is.True, "idle bots are the second choice");
                Assert.That(Candidate(playerLed: true), Is.False, "player-led group");
                Assert.That(Candidate(relic: true), Is.False, "relic carrier");
                Assert.That(Candidate(level: 39), Is.False, "below the 40-50 band");
                Assert.That(Candidate(level: 20), Is.False);
                Assert.That(Candidate(inBattleground: true), Is.False, "already in a battleground");
                Assert.That(Candidate(region: 252), Is.False, "not on a home frontier");
                Assert.That(Candidate(region: 238), Is.False);
                Assert.That(Candidate(rvrTour: false, idle: false), Is.False, "busy with PvE work");
                Assert.That(Candidate(autonomous: false), Is.False, "companions, helpers and players never");
                Assert.That(OpenBattlegroundRoster.CandidateRank(true, true), Is.LessThan(OpenBattlegroundRoster.CandidateRank(false, true)),
                    "an RvR tour is preferred to idle");
            });
        }

        [Test]
        public void SmallGroupSizeIsTwoToFour()
        {
            var random = new Random(252);
            int[] sizes = Enumerable.Range(0, 2_000).Select(_ => OpenBattlegroundRoster.SmallGroupSize(random)).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(sizes, Is.All.InRange(2, 4));
                Assert.That(sizes.Distinct().OrderBy(size => size), Is.EqualTo(new[] { 2, 3, 4 }));
            });
        }

        [Test]
        public void DueForReliefBetween20And30Minutes()
        {
            const long minute = 60_000;
            long[] delays = Enumerable.Range(0, 500).Select(seed => OpenBattlegroundRoster.ReliefDelayMilliseconds(20, 30, seed)).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(delays, Is.All.InRange(20 * minute, 30 * minute));
                Assert.That(delays.Distinct().Count(), Is.GreaterThan(100), "random per draw, not one fixed interval");
                for (int seed = 0; seed < 500; seed++)
                {
                    Assert.That(OpenBattlegroundRoster.DueForRelief(1_000, 1_000 + 20 * minute - 1, 20, 30, seed), Is.False, $"seed {seed} early");
                    Assert.That(OpenBattlegroundRoster.DueForRelief(1_000, 1_000 + 30 * minute, 20, 30, seed), Is.True, $"seed {seed} late");
                }
            });
        }

        [Test]
        public void BracketCapsDoNotApplyToOpen()
        {
            OpenBgPlan plan = OpenBattlegroundRoster.Plan(Defaults, EmptyMap());
            BattlegroundDefinition open = BattlegroundCampaignCatalog.FindOpen();
            BattlegroundDefinition thidranki = BattlegroundCampaignCatalog.Find(238);
            Assert.Multiple(() =>
            {
                Assert.That(plan.Admits.Sum(admit => OpenBattlegroundRoster.PlannedSize(admit.Kind)),
                    Is.GreaterThan(AutonomousBattlegroundParticipation.MaximumAutonomousPerMap), "more than the bracket's 24 per map");
                Assert.That(AutonomousBattlegroundParticipation.AdmitsGroup(8, 8, 30), Is.False, "the bracket rule refuses");
                Assert.That(AutonomousBattlegroundParticipation.AdmitsOpenUnit(8, 30, 72), Is.True, "the open rule admits up to its own cap");
                Assert.That(AutonomousBattlegroundParticipation.AdmitsOpenUnit(8, 66, 72), Is.False, "never above open_bg_max_bots");
                Assert.That(AutonomousBattlegroundParticipation.AdmitsOpenUnit(0, 0, 72), Is.False);
                Assert.That(AutonomousBattlegroundParticipation.AssignmentTimeoutFor(open),
                    Is.EqualTo(AutonomousBattlegroundParticipation.OpenAssignmentTimeoutMilliseconds), "groups muster before they board");
                Assert.That(AutonomousBattlegroundParticipation.AssignmentTimeoutFor(thidranki),
                    Is.EqualTo(AutonomousBattlegroundParticipation.AssignmentTimeoutMilliseconds), "brackets unchanged");
            });
        }

        [Test]
        public void OpenRosterNeverTouchesBracketEntries()
        {
            BattlegroundDefinition thidranki = BattlegroundCampaignCatalog.Find(238);
            var entries = new[]
            {
                // Bracket entries: never counted, whatever unit id they might carry.
                new OpenRosterEntry(0, eRealm.Albion, OpenBgUnitKind.Solo, BattlegroundDefinitionKind.Bracket, true, false, 10),
                new OpenRosterEntry(7, eRealm.Midgard, OpenBgUnitKind.Group8, BattlegroundDefinitionKind.Bracket, true, false, 10),
                // An entry in the open region without a roster unit (bracket-style) is not the roster's either.
                new OpenRosterEntry(0, eRealm.Hibernia, OpenBgUnitKind.Solo, BattlegroundDefinitionKind.Open, true, false, 10),
            };
            IReadOnlyList<RealmCount> counts = OpenBattlegroundRoster.CountRealms(entries, _ => true);
            OpenBgPlan plan = OpenBattlegroundRoster.Plan(Defaults, counts);
            Assert.Multiple(() =>
            {
                Assert.That(counts.Sum(count => count.Bots + count.LeavingBots), Is.Zero);
                Assert.That(plan.Realms.SelectMany(realm => realm.Relieve), Is.Empty, "no bracket unit is ever relieved");
                foreach (BattlegroundDefinition definition in BattlegroundCampaignCatalog.Definitions)
                    Assert.That(AutonomousBattlegroundParticipation.IsOpenUnit(definition, 5),
                        Is.EqualTo(definition.Kind == BattlegroundDefinitionKind.Open), $"region {definition.RegionId}");
                Assert.That(AutonomousBattlegroundParticipation.IsOpenUnit(BattlegroundCampaignCatalog.FindOpen(), 0), Is.False);
                // The bracket leave rule is unchanged: above the bracket graduates, off tour ends the tour.
                Assert.That(AutonomousBattlegroundParticipation.LeaveReasonFor(25, 0, true, false, false, thidranki),
                    Is.EqualTo(AutonomousBattlegroundParticipation.LeaveReason.Graduated));
                Assert.That(AutonomousBattlegroundParticipation.LeaveReasonFor(22, 0, false, false, false, thidranki),
                    Is.EqualTo(AutonomousBattlegroundParticipation.LeaveReason.TourEnded));
            });
        }

        [Test]
        public void CountRealmsGroupsOpenEntriesIntoUnits()
        {
            var entries = new[]
            {
                new OpenRosterEntry(3, eRealm.Albion, OpenBgUnitKind.Small, BattlegroundDefinitionKind.Open, true, false, 4_000),
                new OpenRosterEntry(3, eRealm.Albion, OpenBgUnitKind.Small, BattlegroundDefinitionKind.Open, true, false, 3_000),
                new OpenRosterEntry(3, eRealm.Albion, OpenBgUnitKind.Small, BattlegroundDefinitionKind.Open, false, false, 0),
                new OpenRosterEntry(4, eRealm.Albion, OpenBgUnitKind.Solo, BattlegroundDefinitionKind.Open, false, false, 0),
                new OpenRosterEntry(5, eRealm.Albion, OpenBgUnitKind.Solo, BattlegroundDefinitionKind.Open, true, true, 100),
            };
            RealmCount albion = OpenBattlegroundRoster.CountRealms(entries, realm => realm == eRealm.Albion)
                .Single(count => count.Realm == eRealm.Albion);
            Assert.Multiple(() =>
            {
                Assert.That(albion.Units, Has.Count.EqualTo(2));
                Assert.That(albion.Units[0], Is.EqualTo(new OpenBgUnit(3, OpenBgUnitKind.Small, 3, true, 3_000)), "inside since its first member");
                Assert.That(albion.Units[1].Inside, Is.False, "a solo still travelling");
                Assert.That(albion.LeavingBots, Is.EqualTo(1), "a relieved bot holds a seat, not a setup slot");
                Assert.That(albion.ReliefDue, Is.True);
            });
        }

        [Test]
        public void PlanRefillsAUnitBelowItsMinimum()
        {
            // Live run 0.241.0: a group8 of three and a small group of one still counted 1/1 and were never refilled.
            RealmCount albion = new(eRealm.Albion, new OpenBgUnit[]
            {
                new(1, OpenBgUnitKind.Group8, 3, true, 100),
                new(2, OpenBgUnitKind.Small, 1, true, 200),
                new(3, OpenBgUnitKind.Small, 2, false),
                new(10, OpenBgUnitKind.Solo, 1, true, 300),
                new(11, OpenBgUnitKind.Solo, 1, true, 300),
                new(12, OpenBgUnitKind.Solo, 1, true, 300),
                new(13, OpenBgUnitKind.Solo, 1, true, 300),
            });
            RealmCount midgard = new(eRealm.Midgard, new OpenBgUnit[]
            {
                new(20, OpenBgUnitKind.Group8, OpenBattlegroundRoster.Group8Minimum, true, 100),
                new(21, OpenBgUnitKind.Small, OpenBattlegroundRoster.SmallGroupMin, true, 100),
                new(22, OpenBgUnitKind.Small, OpenBattlegroundRoster.SmallGroupMax, true, 100),
            });
            OpenBgPlan plan = OpenBattlegroundRoster.Plan(Defaults, new[] { albion, midgard, RealmCount.Empty(eRealm.Hibernia) });
            Assert.Multiple(() =>
            {
                Assert.That(plan.For(eRealm.Albion).Relieve, Is.EquivalentTo(new[] { 1, 2 }), "the 8-man of three and the small group of one go");
                Assert.That(plan.For(eRealm.Albion).AddGroups8, Is.EqualTo(1), "a new 8-man replaces the shrunken one");
                Assert.That(plan.For(eRealm.Albion).AddSmall, Is.EqualTo(1), "a new small group replaces the lone member");
                Assert.That(plan.For(eRealm.Albion).AddSolos, Is.Zero, "a solo is a whole unit");
                Assert.That(plan.For(eRealm.Midgard).Relieve, Is.Empty, "an 8-man formed with six and small groups of two to four are whole");
                Assert.That(plan.For(eRealm.Midgard).AddGroups8, Is.Zero);
                Assert.That(OpenBattlegroundRoster.IsBelowMinimum(new OpenBgUnit(5, OpenBgUnitKind.Group8, 5)), Is.True);
                Assert.That(OpenBattlegroundRoster.IsBelowMinimum(new OpenBgUnit(6, OpenBgUnitKind.Solo, 1)), Is.False);
            });
        }

        [Test]
        public void OpenCrewStaysUntilRelievedOrSwitchedOff()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBattlegroundParticipation.OpenLeaveReasonFor(true, false, false, false), Is.Null,
                    "no graduation, no tour end; a bot that died and released inside stays");
                Assert.That(AutonomousBattlegroundParticipation.OpenLeaveReasonFor(false, false, false, false),
                    Is.EqualTo(AutonomousBattlegroundParticipation.LeaveReason.Unassigned), "open BG switched off");
                Assert.That(AutonomousBattlegroundParticipation.OpenLeaveReasonFor(true, true, false, false),
                    Is.EqualTo(AutonomousBattlegroundParticipation.LeaveReason.Unassigned), "a player took it into a group");
                Assert.That(AutonomousBattlegroundParticipation.OpenLeaveReasonFor(true, false, false, true),
                    Is.EqualTo(AutonomousBattlegroundParticipation.LeaveReason.Unassigned), "it joined another party before boarding");
                Assert.That(AutonomousBattlegroundParticipation.Describe(AutonomousBattlegroundParticipation.LeaveReason.Relieved),
                    Is.EqualTo("relieved"));
            });
        }
    }
}
