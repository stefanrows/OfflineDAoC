using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    // open BG roles (spec 2026-10-11 §6): bridge watch, roam, lurk, assault, defend, retreat; light player drift and
    // the low-level protection of humans below level 30.
    [TestFixture]
    public class UT_OpenBattlegroundRoles
    {
        private static readonly eRealm[] Realms = { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia };
        private static readonly IReadOnlyList<BridgePoint> Bridges = OpenBattlegroundRoles.ThidrankiBridges;
        private static readonly Point3D Keep = new(33280, 38272, 3720);

        private static OpenBgRole Role(OpenBgPhase phase, eRealm realm, int size, int index = 0,
            eRealm attacker = eRealm.Midgard, eRealm owner = eRealm.Albion) =>
            OpenBattlegroundRoles.Assign(new OpenBgContext(phase, attacker, owner, realm, size, true), index);

        private static double Nearest(Point3D point, IEnumerable<Point3D> others) =>
            others.Min(other => OpenBattlegroundRoles.Distance2D(point, other));

        // Field points around the keep, some deliberately next to a bridge.
        private static Point3D[] Field() =>
            Enumerable.Range(0, 16).Select(i => new Point3D((int)(Keep.X + Math.Cos(i * Math.PI / 8) * 4_800),
                (int)(Keep.Y + Math.Sin(i * Math.PI / 8) * 4_800), 3500))
            .Concat(Bridges.Select(bridge => new Point3D(bridge.Point.X + 200, bridge.Point.Y, bridge.Point.Z)))
            .ToArray();

        [Test]
        public void QuietGivesOneBridgeWatchPerRealmThenRoam()
        {
            foreach (eRealm realm in Realms)
                Assert.Multiple(() =>
                {
                    Assert.That(Role(OpenBgPhase.Quiet, realm, 8, 0), Is.EqualTo(OpenBgRole.BridgeWatch));
                    Assert.That(Role(OpenBgPhase.Quiet, realm, 3, 1), Is.EqualTo(OpenBgRole.Roam));
                    Assert.That(Role(OpenBgPhase.Quiet, realm, 2, 2), Is.EqualTo(OpenBgRole.Roam));
                });
        }

        [Test]
        public void SolosLurkInQuietAndPush()
        {
            foreach (eRealm realm in Realms)
                Assert.Multiple(() =>
                {
                    Assert.That(Role(OpenBgPhase.Quiet, realm, 1), Is.EqualTo(OpenBgRole.Lurk));
                    Assert.That(Role(OpenBgPhase.Push, realm, 1), Is.EqualTo(OpenBgRole.Lurk), "even the attacker's solos");
                    Assert.That(Role(OpenBgPhase.KeepFight, realm, 1), Is.EqualTo(OpenBgRole.Roam));
                    Assert.That(Role(OpenBgPhase.Retreat, realm, 1), Is.EqualTo(OpenBgRole.Roam), "solos keep roaming");
                });
        }

        [Test]
        public void AttackerGroupsAssaultInPushAndFight()
        {
            Assert.Multiple(() =>
            {
                foreach (int index in new[] { 0, 1, 2 })
                {
                    Assert.That(Role(OpenBgPhase.Push, eRealm.Midgard, 8, index), Is.EqualTo(OpenBgRole.Assault));
                    Assert.That(Role(OpenBgPhase.KeepFight, eRealm.Midgard, 3, index), Is.EqualTo(OpenBgRole.Assault));
                }
                Assert.That(Role(OpenBgPhase.Quiet, eRealm.Midgard, 8, 0), Is.EqualTo(OpenBgRole.BridgeWatch), "no assault in quiet");
                Assert.That(Role(OpenBgPhase.Push, eRealm.Hibernia, 8, 0), Is.EqualTo(OpenBgRole.BridgeWatch), "the others keep watch");
                Assert.That(Role(OpenBgPhase.Push, eRealm.Albion, 8, 1), Is.EqualTo(OpenBgRole.Roam));
                // A realm that took the keep never assaults its own keep.
                Assert.That(Role(OpenBgPhase.KeepFight, eRealm.Midgard, 8, 0, eRealm.Midgard, eRealm.Midgard), Is.EqualTo(OpenBgRole.Defend));
            });
        }

        [Test]
        public void OwnerGroupsDefendInFight()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Role(OpenBgPhase.KeepFight, eRealm.Albion, 8, 0), Is.EqualTo(OpenBgRole.Defend));
                Assert.That(Role(OpenBgPhase.KeepFight, eRealm.Albion, 2, 3), Is.EqualTo(OpenBgRole.Defend));
                Assert.That(Role(OpenBgPhase.KeepFight, eRealm.Hibernia, 8, 0), Is.EqualTo(OpenBgRole.Roam), "the third realm roams");
                Assert.That(Role(OpenBgPhase.KeepFight, eRealm.Albion, 8, 0, eRealm.Midgard, eRealm.None), Is.EqualTo(OpenBgRole.Roam),
                    "a garrison-held keep has no realm defending it");
            });
        }

        [Test]
        public void LosersRetreatWinnersHold()
        {
            Assert.Multiple(() =>
            {
                // Failed attack: Midgard attacked, Albion still owns the keep.
                Assert.That(Role(OpenBgPhase.Retreat, eRealm.Midgard, 8), Is.EqualTo(OpenBgRole.Retreat));
                Assert.That(Role(OpenBgPhase.Retreat, eRealm.Albion, 8), Is.EqualTo(OpenBgRole.Defend));
                Assert.That(Role(OpenBgPhase.Retreat, eRealm.Hibernia, 8), Is.EqualTo(OpenBgRole.Roam));
                // Successful attack: Midgard now owns it and holds.
                Assert.That(Role(OpenBgPhase.Retreat, eRealm.Midgard, 8, 0, eRealm.Midgard, eRealm.Midgard), Is.EqualTo(OpenBgRole.Defend));
                Assert.That(Role(OpenBgPhase.Retreat, eRealm.Albion, 8, 0, eRealm.Midgard, eRealm.Midgard), Is.EqualTo(OpenBgRole.Roam));
            });
        }

        [Test]
        public void BridgeWatchAndRetreatTakeTheRealmsOwnBridge()
        {
            var rng = new Random(1);
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundRoles.RolePoint(OpenBgRole.BridgeWatch, eRealm.Midgard, Bridges, Keep, Field(), rng), Is.SameAs(Bridges[0].Point));
                Assert.That(OpenBattlegroundRoles.RolePoint(OpenBgRole.BridgeWatch, eRealm.Hibernia, Bridges, Keep, Field(), rng), Is.SameAs(Bridges[1].Point));
                Assert.That(OpenBattlegroundRoles.RolePoint(OpenBgRole.BridgeWatch, eRealm.Albion, Bridges, Keep, Field(), rng), Is.SameAs(Bridges[2].Point));
                Assert.That(OpenBattlegroundRoles.RolePoint(OpenBgRole.Retreat, eRealm.Albion, Bridges, Keep, Field(), rng), Is.SameAs(Bridges[2].Point));
                Assert.That(OpenBattlegroundRoles.RolePoint(OpenBgRole.Assault, eRealm.Albion, Bridges, Keep, Field(), rng), Is.SameAs(Keep));
                Assert.That(OpenBattlegroundRoles.RolePoint(OpenBgRole.Defend, eRealm.Albion, Bridges, Keep, Field(), rng), Is.SameAs(Keep));
                Assert.That(Bridges.Select(bridge => bridge.Name), Is.EqualTo(new[] { "E", "NW", "SW" }));
            });
        }

        [Test]
        public void RoamPointsStayAwayFromBridges()
        {
            var rng = new Random(2);
            Point3D[] field = Field();
            var seen = new HashSet<Point3D>();
            for (int i = 0; i < 300; i++)
            {
                Point3D point = OpenBattlegroundRoles.RolePoint(OpenBgRole.Roam, eRealm.Hibernia, Bridges, Keep, field, rng);
                Assert.That(point, Is.Not.Null);
                Assert.That(Nearest(point, Bridges.Select(bridge => bridge.Point)), Is.GreaterThanOrEqualTo(OpenBattlegroundRoles.RoamBridgeDistance));
                seen.Add(point);
            }
            Assert.That(seen.Count, Is.GreaterThan(3), "roamers spread over the field");
            Point3D[] onlyBridges = Bridges.Select(bridge => new Point3D(bridge.Point.X + 100, bridge.Point.Y, bridge.Point.Z)).ToArray();
            Assert.That(OpenBattlegroundRoles.RolePoint(OpenBgRole.Roam, eRealm.Hibernia, Bridges, Keep, onlyBridges, rng), Is.Null);
        }

        [Test]
        public void LurkPointsAreNearABridge()
        {
            var rng = new Random(4);
            int own = 0;
            for (int i = 0; i < 300; i++)
            {
                Point3D point = OpenBattlegroundRoles.RolePoint(OpenBgRole.Lurk, eRealm.Albion, Bridges, Keep, Field(), rng);
                double distance = Nearest(point, Bridges.Select(bridge => bridge.Point));
                Assert.That(distance, Is.InRange(OpenBattlegroundRoles.LurkMinDistance - 1, OpenBattlegroundRoles.LurkMaxDistance + 1));
                if (OpenBattlegroundRoles.Distance2D(point, Bridges[2].Point) <= OpenBattlegroundRoles.LurkMaxDistance + 1) own++;
            }
            Assert.That(own, Is.InRange(150, 280), "half own bridge, the rest any bridge");
        }

        [Test]
        public void DriftMovesRoamPointWithinSightOfAHuman()
        {
            var rng = new Random(6);
            Point3D[] humans = { new(20_000, 20_000, 3000) };
            for (int i = 0; i < 200; i++)
            {
                var chosen = new Point3D(40_000 + i * 10, 40_000, 3300);
                Point3D drifted = OpenBattlegroundRoles.DriftedPoint(chosen, humans, true, rng);
                double distance = OpenBattlegroundRoles.Distance2D(drifted, humans[0]);
                Assert.That(distance, Is.InRange(OpenBattlegroundRoles.DriftMinDistance, OpenBattlegroundRoles.DriftSightRange));
            }
            // A point already in sight stays as chosen.
            var inSight = new Point3D(23_000, 20_000, 3000);
            Assert.That(OpenBattlegroundRoles.DriftedPoint(inSight, humans, true, rng), Is.SameAs(inSight));
        }

        [Test]
        public void DriftNeverLandsOnTheHuman()
        {
            var rng = new Random(8);
            Point3D[] humans = { new(20_000, 20_000, 3000), new(21_000, 20_000, 3000), new(20_500, 21_000, 3000) };
            for (int i = 0; i < 300; i++)
            {
                Point3D chosen = i % 2 == 0 ? new Point3D(20_000, 20_000, 3000) : new Point3D(20_400, 20_300, 3000);
                Point3D drifted = OpenBattlegroundRoles.DriftedPoint(chosen, humans, true, rng);
                Assert.That(drifted, Is.Not.Null);
                Assert.That(Nearest(drifted, humans), Is.GreaterThanOrEqualTo(OpenBattlegroundRoles.DriftMinDistance));
                Assert.That(Nearest(drifted, humans), Is.LessThanOrEqualTo(OpenBattlegroundRoles.DriftSightRange));
            }
        }

        [Test]
        public void DriftOffWhenDisabled()
        {
            var rng = new Random(9);
            var chosen = new Point3D(40_000, 40_000, 3300);
            Point3D[] humans = { new(20_000, 20_000, 3000) };
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundRoles.DriftedPoint(chosen, humans, false, rng), Is.SameAs(chosen));
                Assert.That(OpenBattlegroundRoles.DriftedPoint(chosen, Array.Empty<Point3D>(), true, rng), Is.SameAs(chosen));
                Assert.That(OpenBattlegroundRoles.DriftedPoint(chosen, null, true, rng), Is.SameAs(chosen));
            });
        }

        [Test]
        public void FortyPlusBotIgnoresLowHumanUnlessAttacked()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundRoles.MayHuntHuman(45, 20, false), Is.False);
                Assert.That(OpenBattlegroundRoles.MayHuntHuman(50, 29, false), Is.False);
                Assert.That(OpenBattlegroundRoles.MayHuntHuman(40, 1, false), Is.False);
                Assert.That(OpenBattlegroundRoles.MayHuntHuman(45, 20, true), Is.True, "self-defence");
                Assert.That(OpenBattlegroundRoles.MayHuntHuman(45, 30, false), Is.True);
                Assert.That(OpenBattlegroundRoles.MayHuntHuman(50, 50, false), Is.True);
                Assert.That(OpenBattlegroundRoles.MayHuntHuman(25, 20, false), Is.True, "only the 40-50 crew holds back");
            });
        }

        [Test]
        public void KeepFriendsDefendInsteadOfAssaulting()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundRoles.ForKeepFriendship(OpenBgRole.Assault, true, true), Is.EqualTo(OpenBgRole.Assault));
                Assert.That(OpenBattlegroundRoles.ForKeepFriendship(OpenBgRole.Assault, true, false), Is.EqualTo(OpenBgRole.Defend),
                    "a mixed-realm member of the owning guild");
                Assert.That(OpenBattlegroundRoles.ForKeepFriendship(OpenBgRole.Assault, false, false), Is.EqualTo(OpenBgRole.Assault), "no keep");
                Assert.That(OpenBattlegroundRoles.ForKeepFriendship(OpenBgRole.Roam, true, false), Is.EqualTo(OpenBgRole.Roam));
                Assert.That(OpenBattlegroundRoles.ForKeepFriendship(OpenBgRole.Retreat, true, false), Is.EqualTo(OpenBgRole.Retreat));
            });
        }

        [Test]
        public void RoleNamesMatchTheLogKeys()
        {
            Assert.That(Enum.GetValues<OpenBgRole>().Select(OpenBattlegroundRoles.Describe),
                Is.EqualTo(new[] { "bridge_watch", "roam", "lurk", "assault", "defend", "retreat" }));
        }

        [Test]
        public void SnappedDriftOnTopOfAHumanFallsBackToAPointClearOfHumans()
        {
            var human = new Point3D(10_000, 10_000, 3_000);
            var humans = new[] { human };
            var ownFar = new Point3D(14_000, 10_000, 3_000);
            var ownOnTop = new Point3D(10_300, 10_000, 3_000);
            var snappedNear = new Point3D(10_500, 10_000, 3_000);
            var snappedClear = new Point3D(11_500, 10_000, 3_000);
            var undrifted = new Point3D(20_000, 20_000, 3_000);
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundRoles.AfterDrift(ownFar, snappedClear, humans, () => undrifted, out bool drifted), Is.SameAs(snappedClear));
                Assert.That(drifted, Is.True);
                Assert.That(OpenBattlegroundRoles.AfterDrift(ownFar, snappedNear, humans, () => undrifted, out drifted), Is.SameAs(ownFar),
                    "a snap that lands within 800 of the human is dropped");
                Assert.That(drifted, Is.False);
                Assert.That(OpenBattlegroundRoles.AfterDrift(ownOnTop, snappedNear, humans, () => undrifted, out _), Is.SameAs(undrifted),
                    "own point and snap both on the human: a non-drift point");
                Assert.That(OpenBattlegroundRoles.AfterDrift(ownOnTop, null, humans, () => undrifted, out drifted), Is.SameAs(undrifted),
                    "DriftedPoint null: never the < 800 point");
                Assert.That(drifted, Is.False);
            });
        }

        [Test]
        public void NonDriftPointIsClearOfHumansWhereOneIs()
        {
            var humans = new[] { new Point3D(0, 0, 0) };
            var near = new Point3D(500, 0, 0);
            var mid = new Point3D(700, 0, 0);
            var far = new Point3D(2_000, 0, 0);
            Assert.Multiple(() =>
            {
                for (int seed = 0; seed < 20; seed++)
                    Assert.That(OpenBattlegroundRoles.PointClearOfHumans(new[] { near, far, mid }, humans, new Random(seed)), Is.SameAs(far));
                Assert.That(OpenBattlegroundRoles.PointClearOfHumans(new[] { near, mid }, humans, new Random(1)), Is.SameAs(mid),
                    "every point near a human: the farthest one");
                Assert.That(OpenBattlegroundRoles.PointClearOfHumans(Array.Empty<Point3D>(), humans, new Random(1)), Is.Null);
                Assert.That(OpenBattlegroundRoles.ClearOfHumans(far, humans), Is.True);
                Assert.That(OpenBattlegroundRoles.ClearOfHumans(mid, humans), Is.False);
            });
        }
    }
}
