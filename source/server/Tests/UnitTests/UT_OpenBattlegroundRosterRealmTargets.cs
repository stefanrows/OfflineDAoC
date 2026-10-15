using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    // open BG roster, owner decision 2026-10-11 14:50: per-realm crew targets (open_bg_units_<realm>, "8-man/small/solos",
    // empty = the global targets), hand reliefs from the open BG window, and the last form failure the window shows.
    [TestFixture, NonParallelizable]
    public class UT_OpenBattlegroundRosterRealmTargets
    {
        private static readonly eRealm[] Realms = { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };
        private static readonly OpenBgSetup Defaults = new(1, 2, 4, 72);

        private static IReadOnlyList<RealmCount> EmptyMap() => Realms.Select(RealmCount.Empty).ToArray();

        private static RealmCount FullRealm(eRealm realm, int idBase)
        {
            var units = new List<OpenBgUnit>
            {
                new(idBase + 1, OpenBgUnitKind.Group8, 8, true, 1_000),
                new(idBase + 2, OpenBgUnitKind.Small, 3, true, 2_000),
                new(idBase + 3, OpenBgUnitKind.Small, 2, true, 3_000),
            };
            for (int i = 0; i < 4; i++)
                units.Add(new(idBase + 10 + i, OpenBgUnitKind.Solo, 1, true, 4_000 + i));
            return new RealmCount(realm, units);
        }

        [TearDown]
        public void Cleanup() => OpenBattlegroundRoster.Reset();

        [TestCase("1/2/4", 1, 2, 4)]
        [TestCase(" 0 / 6 / 12 ", 0, 6, 12)]
        [TestCase("9/-1/40", 4, 0, 12)]
        [TestCase("+2/3/0", 2, 3, 0)]
        public void UnitsTextParsesAndClampsLikeTheGlobals(string text, int groups8, int small, int solos)
        {
            Assert.That(OpenBgRealmTargets.TryParse(text, out OpenBgRealmTargets targets), Is.True);
            Assert.That(targets, Is.EqualTo(new OpenBgRealmTargets(groups8, small, solos)));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("1/2")]
        [TestCase("1/2/3/4")]
        [TestCase("a/b/c")]
        [TestCase("1,2,4")]
        [TestCase("1//4")]
        public void InvalidUnitsTextMeansTheGlobalTargets(string text)
        {
            Assert.That(OpenBgRealmTargets.TryParse(text, out _), Is.False);
        }

        [Test]
        public void TargetsFormatChangeAndBookSeats()
        {
            var targets = new OpenBgRealmTargets(1, 2, 4);
            Assert.Multiple(() =>
            {
                Assert.That(targets.Format(), Is.EqualTo("1/2/4"));
                Assert.That(targets.With(OpenBgUnitKind.Group8, 2), Is.EqualTo(new OpenBgRealmTargets(2, 2, 4)));
                Assert.That(targets.With(OpenBgUnitKind.Small, 9), Is.EqualTo(new OpenBgRealmTargets(1, 6, 4)), "clamped");
                Assert.That(targets.With(OpenBgUnitKind.Solo, -1), Is.EqualTo(new OpenBgRealmTargets(1, 2, 0)), "clamped");
                Assert.That(targets.PlannedBots, Is.EqualTo(8 + 2 * 2 + 4));
                Assert.That(targets.Count(OpenBgUnitKind.Small), Is.EqualTo(2));
                Assert.That(OpenBgRealmTargets.Max(OpenBgUnitKind.Solo), Is.EqualTo(OpenBattlegroundProperties.SolosMax));
            });
        }

        [Test]
        public void ARealmWithItsOwnSetupOverridesTheGlobalTargets()
        {
            OpenBgSetup setup = Defaults with { Albion = new OpenBgRealmTargets(2, 0, 1) };
            Assert.Multiple(() =>
            {
                Assert.That(setup.IsOverridden(eRealm.Albion), Is.True);
                Assert.That(setup.IsOverridden(eRealm.Midgard), Is.False);
                Assert.That(setup.TargetsFor(eRealm.Albion), Is.EqualTo(new OpenBgRealmTargets(2, 0, 1)));
                Assert.That(setup.TargetsFor(eRealm.Hibernia), Is.EqualTo(new OpenBgRealmTargets(1, 2, 4)), "the globals");
                Assert.That(setup.Groups8For(eRealm.Albion), Is.EqualTo(2));
                Assert.That(setup.SmallFor(eRealm.Albion), Is.EqualTo(0));
                Assert.That(setup.SolosFor(eRealm.Midgard), Is.EqualTo(4));
                Assert.That(setup.WithOverride(eRealm.Albion, null), Is.EqualTo(Defaults), "back to the global targets");
                Assert.That(setup.WithOverride(eRealm.Midgard, new OpenBgRealmTargets(0, 0, 0)).Midgard, Is.EqualTo(new OpenBgRealmTargets(0, 0, 0)));
            });

            OpenBgPlan plan = OpenBattlegroundRoster.Plan(setup, EmptyMap());
            Assert.Multiple(() =>
            {
                Assert.That((plan.For(eRealm.Albion).AddGroups8, plan.For(eRealm.Albion).AddSmall, plan.For(eRealm.Albion).AddSolos), Is.EqualTo((2, 0, 1)));
                Assert.That((plan.For(eRealm.Midgard).AddGroups8, plan.For(eRealm.Midgard).AddSmall, plan.For(eRealm.Midgard).AddSolos), Is.EqualTo((1, 2, 4)));
                Assert.That((plan.For(eRealm.Hibernia).AddGroups8, plan.For(eRealm.Hibernia).AddSmall, plan.For(eRealm.Hibernia).AddSolos), Is.EqualTo((1, 2, 4)));
            });
        }

        [Test]
        public void LoweringOneRealmRelievesOnlyItsSurplus()
        {
            OpenBgSetup setup = Defaults with { Midgard = new OpenBgRealmTargets(0, 2, 2) };
            OpenBgPlan plan = OpenBattlegroundRoster.Plan(setup, new[] { FullRealm(eRealm.Albion, 0), FullRealm(eRealm.Midgard, 100), FullRealm(eRealm.Hibernia, 200) });
            Assert.Multiple(() =>
            {
                Assert.That(plan.For(eRealm.Albion).Relieve, Is.Empty);
                Assert.That(plan.For(eRealm.Hibernia).Relieve, Is.Empty);
                // The 8-man and the two most recently entered solos go.
                Assert.That(plan.For(eRealm.Midgard).Relieve, Is.EquivalentTo(new[] { 101, 113, 112 }));
                Assert.That(plan.Admits, Is.Empty);
            });
        }

        [Test]
        public void AHandReliefGoesFirstAndIsReplaced()
        {
            OpenBgPlan plan = OpenBattlegroundRoster.Plan(Defaults, new[] { FullRealm(eRealm.Albion, 0), FullRealm(eRealm.Midgard, 100),
                FullRealm(eRealm.Hibernia, 200) }, forced: new[] { 101 });
            Assert.Multiple(() =>
            {
                Assert.That(plan.For(eRealm.Midgard).Relieve, Is.EqualTo(new[] { 101 }));
                Assert.That(plan.For(eRealm.Midgard).AddGroups8, Is.EqualTo(1), "the target still wants an 8-man");
                Assert.That(plan.For(eRealm.Albion).Relieve, Is.Empty);
                Assert.That(plan.Admits, Is.EqualTo(new[] { new OpenBgAdmit(eRealm.Midgard, OpenBgUnitKind.Group8) }));
            });

            // A hand relief of a unit already over a lowered target is no extra relief; an unknown id is ignored.
            OpenBgPlan lowered = OpenBattlegroundRoster.Plan(Defaults with { Midgard = new OpenBgRealmTargets(0, 2, 4) },
                new[] { FullRealm(eRealm.Midgard, 100) }, forced: new[] { 101, 999 });
            Assert.Multiple(() =>
            {
                Assert.That(lowered.For(eRealm.Midgard).Relieve, Is.EqualTo(new[] { 101 }));
                Assert.That(lowered.Admits, Is.Empty);
            });
        }

        [Test]
        public void AHandReliefRestsShortAndIsLoggedAsManual()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundRoster.RestAfterRelief(belowMinimum: false, manual: true),
                    Is.EqualTo(OpenBattlegroundRoster.RecruitCooldownMilliseconds));
                Assert.That(OpenBattlegroundRoster.RestAfterRelief(belowMinimum: false, manual: false),
                    Is.EqualTo(OpenBattlegroundRoster.RestAfterReliefMilliseconds));
                Assert.That(OpenBattlegroundRoster.ReliefReason(manual: true, belowMinimum: true), Is.EqualTo("manual"));
                Assert.That(OpenBattlegroundRoster.ReliefReason(false, true), Is.EqualTo("below_minimum"));
                Assert.That(OpenBattlegroundRoster.ReliefReason(false, false), Is.EqualTo("scheduled"));
            });
        }

        [Test]
        public void RequestReliefRefusesUnitsTheRosterDoesNotKnow()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundRoster.RequestRelief(0), Is.False);
                Assert.That(OpenBattlegroundRoster.RequestRelief(4711), Is.False);
                Assert.That(OpenBattlegroundRoster.IsReliefPending(4711), Is.False);
            });
        }

        [Test]
        public void TheLastFormFailureIsKeptPerRealmAndKindUntilOneForms()
        {
            Assert.That(OpenBattlegroundRoster.TryGetLastFormFailure(eRealm.Albion, OpenBgUnitKind.Group8, out _, out _), Is.False);
            string text = OpenBattlegroundRoster.FormFailureText(OpenBgUnitKind.Group8, 6, 3,
                new Dictionary<OpenBgReject, int> { [OpenBgReject.FarFromPorter] = 5, [OpenBgReject.Rest] = 2 });
            OpenBattlegroundRoster.RecordFormFailure(eRealm.Albion, OpenBgUnitKind.Group8, text, 120_000);
            Assert.Multiple(() =>
            {
                Assert.That(text, Is.EqualTo("needs 6, 3 RvR candidates in the pool; far from a porter 5, resting after a relief 2"));
                Assert.That(OpenBattlegroundRoster.TryGetLastFormFailure(eRealm.Albion, OpenBgUnitKind.Group8, out string got, out long tick), Is.True);
                Assert.That(got, Is.EqualTo(text));
                Assert.That(tick, Is.EqualTo(120_000));
                Assert.That(OpenBattlegroundRoster.TryGetLastFormFailure(eRealm.Albion, OpenBgUnitKind.Small, out _, out _), Is.False);
                Assert.That(OpenBattlegroundRoster.TryGetLastFormFailure(eRealm.Midgard, OpenBgUnitKind.Group8, out _, out _), Is.False);
            });
            OpenBattlegroundRoster.ClearFormFailure(eRealm.Albion, OpenBgUnitKind.Group8);
            Assert.That(OpenBattlegroundRoster.TryGetLastFormFailure(eRealm.Albion, OpenBgUnitKind.Group8, out _, out _), Is.False);
            Assert.That(OpenBattlegroundRoster.FormFailureText(OpenBgUnitKind.Solo, 1, 0, null),
                Is.EqualTo("needs 1, 0 candidates in the pool; no level 40-50 bot of the realm was free"));
        }

        [Test]
        public void RealmStatusShowsTargetsPresentUnitsAndFailures()
        {
            OpenBgBotStatus[] bots =
            {
                new("Albion", "A1", 50, "Cleric", 1, "group8", "A1", "inside", true),
                new("Albion", "A2", 50, "Armsman", 1, "group8", "A1", "inside", true),
                new("Albion", "A3", 45, "Scout", 2, "solo", null, "travelling", true),
                new("Albion", "A4", 45, "Scout", 3, "solo", null, "leaving", true),
                new("Midgard", "M1", 44, "Healer", 7, "small", "M1", "inside", true),
            };
            OpenBgSetup setup = Defaults with { Albion = new OpenBgRealmTargets(2, 0, 1) };
            OpenBgRealmStatus[] realms = OpenBattlegroundStatus.RealmStatuses(setup, bots,
                (realm, kind) => realm == eRealm.Albion && kind == OpenBgUnitKind.Group8 ? ("needs 6, 2 RvR candidates in the pool", 60_000L) : null,
                180_000);
            Assert.Multiple(() =>
            {
                Assert.That(realms.Select(realm => realm.Realm), Is.EqualTo(new[] { "Albion", "Midgard", "Hibernia" }));
                Assert.That(realms[0], Is.EqualTo(realms[0] with
                {
                    Groups8 = 2, Small = 0, Solos = 1, OwnSetup = true, Groups8Present = 1, SmallPresent = 0, SolosPresent = 1, Bots = 3,
                }));
                Assert.That(realms[0].FormFailures.Single(), Is.EqualTo(new OpenBgFormFailureStatus("group8", "needs 6, 2 RvR candidates in the pool", 120)));
                Assert.That((realms[1].Groups8, realms[1].Small, realms[1].Solos, realms[1].OwnSetup, realms[1].SmallPresent), Is.EqualTo((1, 2, 4, false, 1)));
                Assert.That(realms[2].Bots, Is.EqualTo(0));
                Assert.That(realms[2].FormFailures, Is.Empty);
            });
        }
    }
}
