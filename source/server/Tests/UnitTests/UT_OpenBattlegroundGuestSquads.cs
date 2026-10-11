using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.PropertyCalc;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // open BG guest squads (spec 2026-10-11 §8, review focus 5): while a human under 40 is inside, one non-persistent
    // squad per realm (3-5, the lowest such human's level ±3); gone 5 game minutes after the last one left.
    [TestFixture, NonParallelizable]
    public class UT_OpenBattlegroundGuestSquads
    {
        private const long Minute = 60_000;
        private static readonly eRealm[] Realms = { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };
        private static readonly IReadOnlyList<(eRealm realm, int level)> NoHumans = Array.Empty<(eRealm, int)>();

        private static GuestSquad Squad(int id, eRealm realm, int level, long lastLowHumanAt, int alive = 4, bool inCombat = false,
            long emptySince = 0) => new(id, realm, level, 4, alive, inCombat, lastLowHumanAt, emptySince);

        [Test]
        public void SpawnsOneSquadPerRealmForALowHuman()
        {
            GuestPlan plan = OpenBattlegroundGuestSquads.Plan(new[] { (eRealm.Albion, 12) }, Array.Empty<GuestSquad>(), 0, new Random(1));
            Assert.Multiple(() =>
            {
                Assert.That(plan.LowHumanInside, Is.True);
                Assert.That(plan.Spawns.Select(spawn => spawn.Realm), Is.EquivalentTo(Realms));
                Assert.That(plan.Spawns.Select(spawn => spawn.Size), Has.All.InRange(3, 5));
                Assert.That(plan.Despawns, Is.Empty);
            });

            // A realm that already has a squad gets no second one; a second low human changes nothing.
            GuestPlan next = OpenBattlegroundGuestSquads.Plan(new[] { (eRealm.Albion, 12), (eRealm.Hibernia, 14) },
                new[] { Squad(1, eRealm.Midgard, 12, 0) }, 30_000, new Random(2));
            Assert.That(next.Spawns.Select(spawn => spawn.Realm), Is.EquivalentTo(new[] { eRealm.Albion, eRealm.Hibernia }));
        }

        [Test]
        public void SizesCoverThreeToFive()
        {
            var rng = new Random(7);
            int[] sizes = Enumerable.Range(0, 300).Select(_ => OpenBattlegroundGuestSquads.SquadSize(rng)).ToArray();
            Assert.That(sizes.Distinct().OrderBy(size => size), Is.EqualTo(new[] { 3, 4, 5 }));
        }

        [Test]
        public void NoSquadForHumansOverForty()
        {
            GuestPlan plan = OpenBattlegroundGuestSquads.Plan(new[] { (eRealm.Albion, 40), (eRealm.Midgard, 50), (eRealm.Hibernia, 44) },
                Array.Empty<GuestSquad>(), 0, new Random(3));
            Assert.Multiple(() =>
            {
                Assert.That(plan.LowHumanInside, Is.False);
                Assert.That(plan.Spawns, Is.Empty);
                Assert.That(OpenBattlegroundGuestSquads.IsGuestLevel(39), Is.True);
                Assert.That(OpenBattlegroundGuestSquads.IsGuestLevel(40), Is.False);
            });
        }

        // Review focus 5: a level-12 player enters, logs out 2 game minutes later; the squads go 5 minutes after that.
        [Test]
        public void DespawnsFiveMinutesAfterTheHumanLeft()
        {
            long entered = 10 * Minute, left = entered + 2 * Minute;
            GuestPlan first = OpenBattlegroundGuestSquads.Plan(new[] { (eRealm.Albion, 12) }, Array.Empty<GuestSquad>(), entered, new Random(4));
            GuestSquad[] squads = first.Spawns.Select((spawn, i) => new GuestSquad(i + 1, spawn.Realm, spawn.Level, spawn.Size, spawn.Size,
                false, left)).ToArray();

            GuestPlan stillThere = OpenBattlegroundGuestSquads.Plan(NoHumans, squads, left + 5 * Minute - 1, new Random(5));
            GuestPlan gone = OpenBattlegroundGuestSquads.Plan(NoHumans, squads, left + 5 * Minute, new Random(6));
            Assert.Multiple(() =>
            {
                Assert.That(stillThere.Despawns, Is.Empty);
                Assert.That(stillThere.Spawns, Is.Empty);
                Assert.That(gone.Despawns.Select(despawn => despawn.Id), Is.EquivalentTo(squads.Select(squad => squad.Id)));
                Assert.That(gone.Despawns.Select(despawn => despawn.Reason), Has.All.EqualTo("left"));
                Assert.That(gone.Spawns, Is.Empty);
            });

            // A human of 40 or more does not keep the squads alive.
            GuestPlan overForty = OpenBattlegroundGuestSquads.Plan(new[] { (eRealm.Midgard, 45) }, squads, left + 5 * Minute, new Random(7));
            Assert.That(overForty.Despawns, Has.Count.EqualTo(squads.Length));
        }

        [Test]
        public void AWipedSquadIsReplacedAfterTheRespawnDelayAndAtOnceWhenNobodyIsLeft()
        {
            GuestSquad wiped = Squad(1, eRealm.Hibernia, 12, 0, alive: 0, emptySince: 5 * Minute);
            GuestSquad[] squads = { wiped, Squad(2, eRealm.Albion, 12, 5 * Minute), Squad(3, eRealm.Midgard, 12, 5 * Minute) };
            var humans = new[] { (eRealm.Albion, 12) };

            GuestPlan waiting = OpenBattlegroundGuestSquads.Plan(humans, squads, 7 * Minute - 1, new Random(8));
            GuestPlan replaced = OpenBattlegroundGuestSquads.Plan(humans, squads, 7 * Minute, new Random(9));
            GuestPlan empty = OpenBattlegroundGuestSquads.Plan(NoHumans, squads, 5 * Minute + 1, new Random(10));
            Assert.Multiple(() =>
            {
                Assert.That(waiting.Despawns, Is.Empty);
                Assert.That(waiting.Spawns, Is.Empty);
                Assert.That(replaced.Despawns.Single().Reason, Is.EqualTo("wiped"));
                Assert.That(replaced.Spawns.Single().Realm, Is.EqualTo(eRealm.Hibernia));
                Assert.That(empty.Despawns.Single().Id, Is.EqualTo(1));
            });
        }

        [Test]
        public void SquadLevelFollowsTheLowestHuman()
        {
            var humans = new[] { (eRealm.Albion, 30), (eRealm.Midgard, 12), (eRealm.Hibernia, 45) };
            var levels = new List<int>();
            for (int seed = 0; seed < 200; seed++)
                levels.AddRange(OpenBattlegroundGuestSquads.Plan(humans, Array.Empty<GuestSquad>(), 0, new Random(seed)).Spawns.Select(spawn => spawn.Level));
            Assert.Multiple(() =>
            {
                Assert.That(levels, Has.All.InRange(9, 15));
                Assert.That(levels.Distinct().Count(), Is.EqualTo(7), "the whole ±3 spread occurs");
                Assert.That(OpenBattlegroundGuestSquads.Plan(new[] { (eRealm.Albion, 1) }, Array.Empty<GuestSquad>(), 0, new Random(11))
                    .Spawns.Select(spawn => spawn.Level), Has.All.InRange(1, 4));
            });

            // The level-12 human left, a level-30 one stays: an idle level-12 squad is replaced, one in a fight is not.
            var stay = new[] { (eRealm.Albion, 30) };
            GuestPlan idle = OpenBattlegroundGuestSquads.Plan(stay, new[] { Squad(1, eRealm.Midgard, 12, 0) }, Minute, new Random(12));
            GuestPlan fighting = OpenBattlegroundGuestSquads.Plan(stay, new[] { Squad(1, eRealm.Midgard, 12, 0, inCombat: true) }, Minute, new Random(13));
            GuestPlan close = OpenBattlegroundGuestSquads.Plan(stay, new[] { Squad(1, eRealm.Midgard, 27, 0) }, Minute, new Random(14));
            Assert.Multiple(() =>
            {
                Assert.That(idle.Despawns.Single().Reason, Is.EqualTo("level"));
                Assert.That(idle.Spawns.Single(spawn => spawn.Realm == eRealm.Midgard).Level, Is.InRange(27, 33));
                Assert.That(fighting.Despawns, Is.Empty);
                Assert.That(close.Despawns, Is.Empty);
            });
        }

        [Test]
        public void CampaignSquadsInTheOpenBgNeverScaleToALowHuman()
        {
            BattlegroundDefinition open = BattlegroundCampaignCatalog.FindOpen();
            BattlegroundDefinition bracket = BattlegroundCampaignCatalog.Find(238);
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundGuestSquads.CampaignSquadLevel(open, 12), Is.EqualTo(40));
                Assert.That(OpenBattlegroundGuestSquads.CampaignSquadLevel(open, 47), Is.EqualTo(47));
                Assert.That(OpenBattlegroundGuestSquads.CampaignSquadMayTarget(open, true, 12), Is.False);
                Assert.That(OpenBattlegroundGuestSquads.CampaignSquadMayTarget(open, true, 40), Is.True);
                Assert.That(OpenBattlegroundGuestSquads.CampaignSquadMayTarget(open, false, 12), Is.True, "bot participants stay targets");
                // Brackets are unchanged.
                Assert.That(OpenBattlegroundGuestSquads.CampaignSquadLevel(bracket, 12), Is.EqualTo(20));
                Assert.That(OpenBattlegroundGuestSquads.CampaignSquadLevel(bracket, 22), Is.EqualTo(22));
                Assert.That(OpenBattlegroundGuestSquads.CampaignSquadMayTarget(bracket, true, 21), Is.True);
            });
        }

        [Test]
        public void SquadsStartAtTheirOwnRealmsLanding()
        {
            var realms = new[] { eRealm.Hibernia, eRealm.Midgard, eRealm.Albion };
            var landings = new[] { new Point3D(18362, 18257, 0), new Point3D(54053, 24680, 0), new Point3D(37301, 52362, 0) };
            var anchorHib = new Point3D(19000, 19000, 0);
            IReadOnlyDictionary<eRealm, Point3D> origins = OpenBattlegroundGuestSquads.MergeOrigins(realms, landings,
                new[] { (0, anchorHib) });
            Assert.Multiple(() =>
            {
                Assert.That(origins[eRealm.Hibernia], Is.SameAs(anchorHib), "the camp anchor outside the portal keep wins");
                Assert.That(origins[eRealm.Midgard], Is.SameAs(landings[1]));
                Assert.That(origins[eRealm.Albion], Is.SameAs(landings[2]));
                Assert.That(OpenBattlegroundGuestSquads.OriginClear(anchorHib, new[] { new Point3D(19500, 19500, 0) }), Is.False);
                Assert.That(OpenBattlegroundGuestSquads.OriginClear(anchorHib, new[] { new Point3D(30000, 30000, 0) }), Is.True);
            });
        }

        [Test]
        public void RoamPointsStayInTheHumansHalfAndClearOfTheKeep()
        {
            var keep = new Point3D(33280, 38272, 0);
            var human = new Point3D(37000, 50000, 0);
            var rng = new Random(15);
            for (int i = 0; i < 500; i++)
            {
                Point3D point = OpenBattlegroundGuestSquads.RoamPoint(human, keep, rng);
                double distance = OpenBattlegroundGuestSquads.Distance2D(point, human);
                Assert.That(OpenBattlegroundGuestSquads.InHumansHalf(keep, human, point), Is.True);
                Assert.That(distance, Is.InRange(OpenBattlegroundGuestSquads.RoamMin - 1, OpenBattlegroundGuestSquads.RoamMax + 1));
                Assert.That(OpenBattlegroundGuestSquads.Distance2D(point, keep), Is.GreaterThanOrEqualTo(OpenBattlegroundGuestSquads.KeepClearance));
            }
            Assert.That(OpenBattlegroundGuestSquads.InHumansHalf(keep, human, new Point3D(30000, 30000, 0)), Is.False);
        }

        [Test]
        public void AFarSquadWalksTowardTheHumanInLegs()
        {
            var keep = new Point3D(33280, 38272, 0);
            var human = new Point3D(37000, 50000, 0);
            var from = new Point3D(18362, 18257, 0);
            IReadOnlyList<Point3D> candidates = OpenBattlegroundGuestSquads.GoalCandidates(from, human, keep, new Random(16));
            double start = OpenBattlegroundGuestSquads.Distance2D(from, human);
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundGuestSquads.Distance2D(candidates[0], from), Is.EqualTo(6000).Within(2));
                Assert.That(candidates.Take(3).Select(point => OpenBattlegroundGuestSquads.Distance2D(point, human)), Has.All.LessThan(start));
                Assert.That(OpenBattlegroundGuestSquads.NeedsNewGoal(from, candidates[0], human), Is.True, "a leg far from the human is re-chosen");
                Assert.That(OpenBattlegroundGuestSquads.NeedsNewGoal(from, null, human), Is.True);
                Assert.That(OpenBattlegroundGuestSquads.NeedsNewGoal(new Point3D(36000, 48000, 0), new Point3D(37500, 49000, 0), human), Is.False);
            });
        }

        [Test]
        public void AHumanInHisPortalKeepIsHuntedFromADistance()
        {
            var keep = new Point3D(33280, 38272, 0);
            var human = new Point3D(37301, 52362, 0);
            Point3D field = OpenBattlegroundGuestSquads.HuntAnchor(human, false, keep);
            Point3D rested = OpenBattlegroundGuestSquads.HuntAnchor(human, true, keep);
            Assert.Multiple(() =>
            {
                Assert.That(field, Is.SameAs(human));
                Assert.That(OpenBattlegroundGuestSquads.Distance2D(rested, human), Is.EqualTo(OpenBattlegroundGuestSquads.SanctuaryOffset).Within(2));
                Assert.That(OpenBattlegroundGuestSquads.Distance2D(rested, keep), Is.LessThan(OpenBattlegroundGuestSquads.Distance2D(human, keep)));
                // Close to the keep it stops half way.
                Point3D near = OpenBattlegroundGuestSquads.HuntAnchor(new Point3D(33280, 41272, 0), true, keep);
                Assert.That(near.Y, Is.EqualTo(39772));
            });
        }

        // The guest actors are BattlegroundEncounterActor bots: the encounter constructor marks them, they never save,
        // carry no loot or progress, and each realm's class set gives the squad its realm.
        [Test]
        public void GuestActorsAreNeverPersistent()
        {
            GameServer previous = GameServer.Instance;
            var language = new PetTestLanguageScope();
            var calculators = (IPropertyCalculator[])typeof(GameLiving).GetField("m_propertyCalc", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            IPropertyCalculator[] originalCalculators = calculators.ToArray();
            var built = new List<GameBot>();
            try
            {
                GameServer.LoadTestDouble((NoWriteServer)RuntimeHelpers.GetUninitializedObject(typeof(NoWriteServer)));
                GameLiving.LoadCalculators();
                foreach (eRealm realm in Realms)
                    for (int i = 0; i < OpenBattlegroundGuestSquads.MaxSize; i++)
                    {
                        var bot = new GameBot(OpenBattlegroundGuestSquads.ClassFor(realm, i), 12, OpenBattlegroundGuestSquads.NameFor(realm));
                        built.Add(bot);
                        Assert.Multiple(() =>
                        {
                            Assert.That(bot.IsBattlegroundEncounterBot, Is.True);
                            Assert.That(bot.SuppressLootAndProgress, Is.True);
                            Assert.That(bot.IsAutonomousWorldBot, Is.False);
                            Assert.That(bot.IsPersistentPlayerCompanion, Is.False);
                            Assert.That(bot.PersistentRecord, Is.Null);
                            Assert.That(bot.Realm, Is.EqualTo(realm));
                            Assert.That(bot.Level, Is.EqualTo(12));
                        });
                        // Any database write would throw in NoWriteServer.
                        Assert.DoesNotThrow(bot.SaveToDatabase);
                    }
            }
            finally
            {
                foreach (GameBot bot in built)
                {
                    if (bot.castingComponent != null) ServiceObjectStore.Remove(bot.castingComponent);
                    if (bot.effectListComponent != null) ServiceObjectStore.Remove(bot.effectListComponent);
                    if (bot.movementComponent != null) ServiceObjectStore.Remove(bot.movementComponent);
                }
                Array.Copy(originalCalculators, calculators, calculators.Length);
                GameServer.LoadTestDouble(previous);
                language.Dispose();
            }
        }

        private sealed class NoWriteServer : GameServer
        {
            private static readonly IObjectDatabase ReadOnly = DispatchProxy.Create<IObjectDatabase, ReadOnlyDatabase>();
            protected override IObjectDatabase DataBaseImpl => ReadOnly;
            protected override GS.ServerRules.IServerRules ServerRulesImpl => new GS.ServerRules.NormalServerRules();
        }

        public class ReadOnlyDatabase : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (!method.Name.StartsWith("Select") && !method.Name.StartsWith("Find"))
                    throw new InvalidOperationException("No database writes allowed: " + method.Name);
                Type type = method.ReturnType;
                return type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type)
                    ? Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0])) : null;
            }
        }
    }
}
