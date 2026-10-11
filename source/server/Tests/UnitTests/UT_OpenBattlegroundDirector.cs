using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    // open BG phase machine (spec 2026-10-11 §6): quiet 30 %, push 20 %, keep fight 35 %, retreat 15 % of a random
    // 15-45 game-minute cycle; the attacker is never the keep owner (human or bot) and the realm with the fewest
    // recent wins is the likeliest.
    [TestFixture]
    public class UT_OpenBattlegroundDirector
    {
        private static readonly eRealm[] Realms = { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };
        private static readonly IReadOnlyDictionary<eRealm, int> NoWins = new Dictionary<eRealm, int>();

        [Test]
        public void CycleLengthIsWithinBounds()
        {
            var rng = new Random(11);
            for (int i = 0; i < 1_000; i++)
            {
                OpenBgCycle cycle = OpenBattlegroundDirector.NewCycle(rng, 15, 45, eRealm.None, NoWins);
                Assert.That(cycle.TotalMs, Is.InRange(15 * 60_000L, 45 * 60_000L));
                Assert.That(new[] { cycle.QuietMs, cycle.PushMs, cycle.FightMs, cycle.RetreatMs }, Has.All.GreaterThan(0));
            }
        }

        [Test]
        public void CycleBoundsAreOrderedAndUsable()
        {
            var rng = new Random(3);
            OpenBgCycle swapped = OpenBattlegroundDirector.NewCycle(rng, 30, 20, eRealm.None, NoWins);
            OpenBgCycle fixedLength = OpenBattlegroundDirector.NewCycle(rng, 5, 5, eRealm.None, NoWins);
            Assert.Multiple(() =>
            {
                Assert.That(swapped.TotalMs, Is.InRange(20 * 60_000L, 30 * 60_000L));
                Assert.That(fixedLength.TotalMs, Is.EqualTo(5 * 60_000L));
                Assert.That(swapped.StartTick, Is.EqualTo(0), "the caller sets the start tick");
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
                OpenBgCycle cycle = OpenBattlegroundDirector.NewCycle(rng, 15, 45, eRealm.Albion, NoWins);
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
        public void AttackerIsNeverTheOwner()
        {
            var rng = new Random(7);
            foreach (eRealm owner in Realms)
            {
                var seen = new HashSet<eRealm>();
                for (int i = 0; i < 300; i++)
                {
                    eRealm attacker = OpenBattlegroundDirector.NewCycle(rng, 15, 45, owner, NoWins).Attacker;
                    Assert.That(attacker, Is.Not.EqualTo(owner));
                    Assert.That(Realms, Does.Contain(attacker));
                    seen.Add(attacker);
                }
                Assert.That(seen, Has.Count.EqualTo(2), $"both other realms attack {owner} sometimes");
            }
            var unowned = new HashSet<eRealm>();
            for (int i = 0; i < 300; i++) unowned.Add(OpenBattlegroundDirector.NewCycle(rng, 15, 45, eRealm.None, NoWins).Attacker);
            Assert.That(unowned, Is.EquivalentTo(Realms), "a garrison-held keep is open to every realm");
        }

        [Test]
        public void AttackerPrefersFewestRecentWins()
        {
            var rng = new Random(13);
            var wins = new Dictionary<eRealm, int> { [eRealm.Midgard] = 3, [eRealm.Hibernia] = 0, [eRealm.Albion] = 5 };
            int hibernia = 0, midgard = 0;
            for (int i = 0; i < 1_000; i++)
            {
                eRealm attacker = OpenBattlegroundDirector.NewCycle(rng, 15, 45, eRealm.Albion, wins).Attacker;
                if (attacker == eRealm.Hibernia) hibernia++;
                else if (attacker == eRealm.Midgard) midgard++;
            }
            Assert.Multiple(() =>
            {
                Assert.That(hibernia, Is.GreaterThan(850), "the realm without recent wins attacks most cycles");
                Assert.That(midgard, Is.GreaterThan(0), "a weighted pick, never a fixed rotation");
                Assert.That(hibernia + midgard, Is.EqualTo(1_000), "the owner never attacks");
            });

            int unownedFewest = 0;
            var tied = new Dictionary<eRealm, int> { [eRealm.Albion] = 2, [eRealm.Midgard] = 2 };
            for (int i = 0; i < 1_000; i++)
                if (OpenBattlegroundDirector.PickAttacker(rng, eRealm.None, tied) == eRealm.Hibernia) unownedFewest++;
            Assert.That(unownedFewest, Is.GreaterThan(700));
        }

        [Test]
        public void PhaseAtWalksAllFourPhasesInOrder()
        {
            var cycle = new OpenBgCycle(10_000, 300, 200, 350, 150, eRealm.Midgard);
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
        public void HumanOwnedKeepIsStillATarget()
        {
            // A human guild of Hibernia claimed the keep: it is the owner like any bot guild, and the push targets it.
            eRealm owner = OpenBattlegroundDirector.OwnerRealm(realGuild: true, garrison: false, eRealm.Hibernia, eRealm.Hibernia);
            Assert.That(owner, Is.EqualTo(eRealm.Hibernia));
            var rng = new Random(17);
            for (int i = 0; i < 200; i++)
            {
                OpenBgCycle cycle = OpenBattlegroundDirector.NewCycle(rng, 15, 45, owner, NoWins);
                Assert.That(cycle.Attacker, Is.Not.EqualTo(eRealm.Hibernia));
                foreach (OpenBgPhase phase in new[] { OpenBgPhase.Push, OpenBgPhase.KeepFight })
                    Assert.That(OpenBattlegroundRoles.Assign(new OpenBgContext(phase, cycle.Attacker, owner, cycle.Attacker, 8, true), 0),
                        Is.EqualTo(OpenBgRole.Assault));
                Assert.That(OpenBattlegroundRoles.Assign(new OpenBgContext(OpenBgPhase.KeepFight, cycle.Attacker, owner, eRealm.Hibernia, 8, true), 0),
                    Is.EqualTo(OpenBgRole.Defend), "the owner's realm defends the human-held keep");
                // Retreat never sends the owner home, so no bot walks away from (or releases) a held keep.
                Assert.That(OpenBattlegroundRoles.Assign(new OpenBgContext(OpenBgPhase.Retreat, cycle.Attacker, owner, eRealm.Hibernia, 8, true), 0),
                    Is.EqualTo(OpenBgRole.Defend));
            }
        }

        [Test]
        public void OwnerRealmFollowsTheClaimingGuild()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundDirector.OwnerRealm(false, false, eRealm.None, eRealm.Albion), Is.EqualTo(eRealm.None), "no guild");
                Assert.That(OpenBattlegroundDirector.OwnerRealm(true, true, eRealm.None, eRealm.Midgard), Is.EqualTo(eRealm.None), "Frontier Wardens");
                Assert.That(OpenBattlegroundDirector.OwnerRealm(true, false, eRealm.Midgard, eRealm.Albion), Is.EqualTo(eRealm.Midgard));
                Assert.That(OpenBattlegroundDirector.OwnerRealm(true, false, eRealm.None, eRealm.Albion), Is.EqualTo(eRealm.Albion), "realmless guild");
            });
        }

        [Test]
        public void FallenLordGivesTheAttackerTheField()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundDirector.RoleOwner(eRealm.None, true, OpenBgPhase.KeepFight, eRealm.Midgard), Is.EqualTo(eRealm.Midgard));
                Assert.That(OpenBattlegroundDirector.RoleOwner(eRealm.None, true, OpenBgPhase.Retreat, eRealm.Midgard), Is.EqualTo(eRealm.Midgard));
                Assert.That(OpenBattlegroundDirector.RoleOwner(eRealm.Albion, false, OpenBgPhase.Retreat, eRealm.Midgard), Is.EqualTo(eRealm.Albion));
                Assert.That(OpenBattlegroundDirector.RoleOwner(eRealm.None, true, OpenBgPhase.Quiet, eRealm.Midgard), Is.EqualTo(eRealm.None));
                Assert.That(OpenBattlegroundDirector.CycleWinner(eRealm.Albion, eRealm.Midgard, false, eRealm.Midgard), Is.EqualTo(eRealm.Midgard), "capture");
                Assert.That(OpenBattlegroundDirector.CycleWinner(eRealm.Albion, eRealm.None, true, eRealm.Midgard), Is.EqualTo(eRealm.Midgard), "lord down");
                Assert.That(OpenBattlegroundDirector.CycleWinner(eRealm.Albion, eRealm.Albion, false, eRealm.Midgard), Is.EqualTo(eRealm.Albion), "held");
                Assert.That(OpenBattlegroundDirector.CycleWinner(eRealm.None, eRealm.None, false, eRealm.Midgard), Is.EqualTo(eRealm.None), "garrison held");
            });
        }
    }
}
