using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_BattlegroundCampaignDirector
    {
        [Test]
        public void SponsorIsRestoredOnlyForARealUnexpiredGuild()
        {
            DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
            DateTime future = now.AddMinutes(30);
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundCampaignManager.ShouldRestoreSponsor("guild-id", true, future, now), Is.True);
                Assert.That(BattlegroundCampaignManager.ShouldRestoreSponsor(null, true, future, now), Is.False, "No guild id");
                Assert.That(BattlegroundCampaignManager.ShouldRestoreSponsor(string.Empty, true, future, now), Is.False, "Empty guild id");
                Assert.That(BattlegroundCampaignManager.ShouldRestoreSponsor("guild-id", false, future, now), Is.False, "Not a real guild");
                Assert.That(BattlegroundCampaignManager.ShouldRestoreSponsor("guild-id", true, now, now), Is.False, "Expiry equal to now");
                Assert.That(BattlegroundCampaignManager.ShouldRestoreSponsor("guild-id", true, now.AddMinutes(-1), now), Is.False, "Expired");
            });
        }

        [Test]
        public void CampTimersStartAtDistinctOffsetsForEveryDefinition()
        {
            var offsets = Enumerable.Range(0, 10).Select(BattlegroundCampaignManager.StaggerMs).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(offsets[0], Is.EqualTo(0));
                Assert.That(offsets.Distinct().Count(), Is.EqualTo(10));
                Assert.That(offsets.Zip(offsets.Skip(1), (a, b) => b - a).All(gap => gap >= 1_000), Is.True,
                    "Neighbouring campaigns stay more than one tick apart");
            });
        }

        [Test]
        public void NearestCampIsFoundInTwoDimensionsAndEmptyListIsMinusOne()
        {
            var camps = new List<Point3D> { new(0, 0, 9_000), new(4_000, 0, 0), new(0, 3_000, 0) };
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundCampaignManager.NearestIndex(camps, 3_500, 100), Is.EqualTo(1));
                Assert.That(BattlegroundCampaignManager.NearestIndex(camps, 10, 2_900), Is.EqualTo(2));
                Assert.That(BattlegroundCampaignManager.NearestIndex(camps, 10, 10), Is.EqualTo(0), "Z is not part of the distance");
                Assert.That(BattlegroundCampaignManager.NearestIndex(Array.Empty<Point3D>(), 0, 0), Is.EqualTo(-1));
            });
        }

        [Test]
        public void PatrolNeverStartsAtTheCampNearestItsParticipant()
        {
            Assert.That(BattlegroundCampaignManager.PatrolOriginIndex(0, 1, 7), Is.EqualTo(0));
            Assert.That(BattlegroundCampaignManager.PatrolOriginIndex(0, 0, 7), Is.EqualTo(0));
            for (int count = 2; count <= 4; count++)
                for (int nearest = 0; nearest < count; nearest++)
                    for (int rotation = -3; rotation <= 20; rotation++)
                    {
                        int origin = BattlegroundCampaignManager.PatrolOriginIndex(nearest, count, rotation);
                        Assert.That(origin, Is.InRange(0, count - 1));
                        Assert.That(origin, Is.Not.EqualTo(nearest), $"count={count} nearest={nearest} rotation={rotation}");
                    }
        }

        [Test]
        public void SquadTargetsPutRealPlayersFirstAndAdmitParticipantBotsOutsideSanctuaries()
        {
            // Temporary helpers are GameNPC and never targets. Participant bots follow the real players and
            // are dropped while standing in a sanctuary or portal keep; non-participant bots are never targets.
            var bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            var sanctuaryBot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            var helper = new GameNPC();
            var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
            Func<GameNPC, bool> participating = npc => ReferenceEquals(npc, bot) || ReferenceEquals(npc, sanctuaryBot);
            Func<GameLiving, bool> outside = actor => !ReferenceEquals(actor, sanctuaryBot);
            Assert.Multiple(() =>
            {
                Assert.That(BattlegroundCampaignManager.SquadTargets(new GameLiving[] { helper, bot, null, player, sanctuaryBot }, participating, outside),
                    Is.EqualTo(new GameLiving[] { player, bot }), "Real players first, then participant bots outside sanctuaries");
                Assert.That(BattlegroundCampaignManager.SquadTargets(new GameLiving[] { bot, player }, _ => false, _ => true),
                    Is.EqualTo(new GameLiving[] { player }), "A bot outside the battleground is never a target");
                Assert.That(BattlegroundCampaignManager.SquadTargets(new GameLiving[] { bot }, _ => true, _ => true),
                    Is.EqualTo(new GameLiving[] { bot }), "Participant bots are targets when no player is present");
                Assert.That(BattlegroundCampaignManager.SquadTargets(new GameLiving[] { sanctuaryBot }, _ => true, outside),
                    Is.Empty, "A bot in a sanctuary or portal keep is never a target");
            });
        }

        // Fake Detour from the Albion landing of Thidranki toward a door approach: each query walks at most `reach` units
        // (the 256-polygon cap), and stops `shortBy` units before the goal when a closed gate blocks the way.
        private static Func<System.Numerics.Vector3, AutonomousBattlegroundDriver.RouteLeg> Corridor(System.Numerics.Vector3 goal, float reach,
            float shortBy = 0) => start =>
        {
            System.Numerics.Vector3 toGoal = goal - start;
            float left = toGoal.Length() - shortBy;
            if (left <= 0) return new AutonomousBattlegroundDriver.RouteLeg(PathfindingStatus.PartialPathFound, start);
            float step = Math.Min(reach, left);
            System.Numerics.Vector3 end = start + System.Numerics.Vector3.Normalize(toGoal) * step;
            bool reached = shortBy == 0 && step >= left;
            return new AutonomousBattlegroundDriver.RouteLeg(reached ? PathfindingStatus.PathFound : PathfindingStatus.PartialPathFound,
                reached ? goal : end);
        };

        [Test]
        public void AGroupAtItsLandingGetsARouteToTheDoorApproach()
        {
            // Live run 0.242.0: the door approach accepted only one unchained query, which Detour caps at 256 polygons, so
            // keep 11's gate was never proved from the landings and the assault walked back to the camps instead.
            var landing = new System.Numerics.Vector3(37301, 52362, 3945);
            var approach = new System.Numerics.Vector3(34783, 38276, 3720);
            Func<System.Numerics.Vector3, AutonomousBattlegroundDriver.RouteLeg> capped = Corridor(approach, 3_000);
            Assert.Multiple(() =>
            {
                Assert.That(capped(landing).Status, Is.EqualTo(PathfindingStatus.PartialPathFound), "one query alone stops after 3,000 units");
                Assert.That(BattlegroundCampaignManager.ApproachRouteProven(landing, approach, capped), Is.True,
                    "chained legs prove the 14,300-unit way from the landing to the gate");
                Assert.That(BattlegroundCampaignManager.ApproachRouteProven(landing, approach, Corridor(approach, 3_000, shortBy: 30)), Is.True,
                    "a partial path ending within 48 units of the approach point reaches it");
                Assert.That(BattlegroundCampaignManager.ApproachRouteProven(landing, approach, Corridor(approach, 3_000, shortBy: 400)), Is.False,
                    "a point behind the closed gate dead-ends");
                Assert.That(BattlegroundCampaignManager.ApproachRouteProven(landing, approach,
                    _ => new AutonomousBattlegroundDriver.RouteLeg(PathfindingStatus.NoPathFound, landing)), Is.False);
            });
        }

        [Test]
        public void MurdaigeanNativeKeepRowIsAnOrdinaryBattlegroundKeep()
        {
            DbKeep row = BattlegroundNativeKeepData.MurdaigeanKeepRow();
            Assert.Multiple(() =>
            {
                Assert.That(row.KeepID, Is.EqualTo(BattlegroundNativeKeepData.MurdaigeanKeepId));
                Assert.That(BattlegroundNativeKeepData.MurdaigeanKeepId, Is.EqualTo(139));
                Assert.That(row.Region, Is.EqualTo((ushort)251));
                Assert.That(row.BaseLevel, Is.LessThan(100), "Not a portal keep");
                Assert.That(row.Name, Is.EqualTo("Murdaigean Keep"));
                Assert.That(row.X, Is.EqualTo(33280));
                Assert.That(row.Y, Is.EqualTo(38272));
                Assert.That(row.CreateInfo, Is.EqualTo("offline-native-bg:251:pending"));
            });
        }

        [Test]
        public void EncounterBrainTakesItsZoneFromTheSynchronizedPlacement()
        {
            // Bug 129: a GameBot built by Spawn reads (0, 0, 0) through its movement component until the
            // placement is synchronized. A brain created before that captured the zone at the origin, so every
            // patrol goal failed its zone check and each squad built and deleted its bots.
            var region = new Region(new RegionData { Id = 242, Name = "Leirvik test", Description = "Leirvik test", Mobs = [] });
            var zone = new Zone(region, 242, "Leirvik test", 524288, 524288, 65536, 65536, 2, false, 0, false, 0, 0, 0, 0, 0);
            region.Zones.Add(zone);
            var bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            bot.movementComponent = new NpcMovementComponent(bot);
            bot.CurrentRegion = region;
            bot.X = 540000;
            bot.Y = 540000;
            bot.Z = 5000;

            BattlegroundEncounterBrain brain = BattlegroundEncounterActor.AttachBrain(bot);

            object capturedZone = typeof(BattlegroundEncounterBrain).GetField("_zone", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(brain);
            Assert.Multiple(() =>
            {
                Assert.That(capturedZone, Is.SameAs(zone), "The brain must capture the zone of the placed actor");
                Assert.That(bot.X, Is.EqualTo(540000));
                Assert.That(bot.Y, Is.EqualTo(540000));
            });
        }
    }
}
