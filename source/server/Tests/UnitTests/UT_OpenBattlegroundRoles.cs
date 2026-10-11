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

        // A unit's role from the phase, its size, its index among its realm's groups, and whether it is in this cycle's
        // raid or a friend of the keep. The realm is geography only and never changes a role.
        private static OpenBgRole Role(OpenBgPhase phase, int size, int index = 0, bool raid = false, bool friend = false,
            eRealm realm = eRealm.Albion) =>
            OpenBattlegroundRoles.Assign(new OpenBgContext(phase, raid, friend, realm, size, true), index);

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
                    Assert.That(Role(OpenBgPhase.Quiet, 8, 0, realm: realm), Is.EqualTo(OpenBgRole.BridgeWatch));
                    Assert.That(Role(OpenBgPhase.Quiet, 3, 1, realm: realm), Is.EqualTo(OpenBgRole.Roam));
                    Assert.That(Role(OpenBgPhase.Quiet, 2, 2, realm: realm), Is.EqualTo(OpenBgRole.Roam));
                    // The raid keeps its quiet posts until the push.
                    Assert.That(Role(OpenBgPhase.Quiet, 8, 0, raid: true, realm: realm), Is.EqualTo(OpenBgRole.BridgeWatch));
                });
        }

        [Test]
        public void SolosLurkInQuietAndPush()
        {
            // Solos outside the raid lurk; raid solos join the assault instead (live run 0.243.0).
            Assert.Multiple(() =>
            {
                Assert.That(Role(OpenBgPhase.Quiet, 1), Is.EqualTo(OpenBgRole.Lurk));
                Assert.That(Role(OpenBgPhase.Push, 1), Is.EqualTo(OpenBgRole.Lurk), "solos outside the raid");
                Assert.That(Role(OpenBgPhase.KeepFight, 1), Is.EqualTo(OpenBgRole.Roam));
                Assert.That(Role(OpenBgPhase.Retreat, 1), Is.EqualTo(OpenBgRole.Roam), "solos keep roaming");
                Assert.That(Role(OpenBgPhase.Quiet, 1, raid: true), Is.EqualTo(OpenBgRole.Lurk), "no assault in quiet");
                Assert.That(Role(OpenBgPhase.Retreat, 1, raid: true), Is.EqualTo(OpenBgRole.Roam));
            });
        }

        [Test]
        public void EveryRaidUnitOfEveryRealmAssaultsInPushAndKeepFight()
        {
            // Live run 0.243.0: only 10 assault assignments in 5 cycles; solos lurked and roamed through the keep fight.
            // Unit kinds: solo (1), small groups (2-4), the 8-man (5-8); the first group of a realm has index 0.
            int[] sizes = { 1, 2, 3, 4, 6, 8 };
            Assert.Multiple(() =>
            {
                foreach (eRealm realm in Realms)
                    foreach (int size in sizes)
                        foreach (int index in new[] { 0, 1, 3 })
                        {
                            Assert.That(Role(OpenBgPhase.Push, size, index, raid: true, realm: realm), Is.EqualTo(OpenBgRole.Assault),
                                $"push, {realm}, size {size}, index {index}");
                            Assert.That(Role(OpenBgPhase.KeepFight, size, index, raid: true, realm: realm), Is.EqualTo(OpenBgRole.Assault),
                                $"keep fight, {realm}, size {size}, index {index}");
                            Assert.That(Role(OpenBgPhase.Quiet, size, index, raid: true, realm: realm), Is.Not.EqualTo(OpenBgRole.Assault), $"quiet, size {size}");
                            Assert.That(Role(OpenBgPhase.Retreat, size, index, raid: true, realm: realm), Is.Not.EqualTo(OpenBgRole.Assault), $"retreat, size {size}");
                        }
                // Keep friends (the claiming guild, of any realm): groups defend, solos roam; a friend never assaults the keep.
                foreach (int size in sizes.Where(size => size > 1))
                    Assert.That(Role(OpenBgPhase.KeepFight, size, 0, friend: true), Is.EqualTo(OpenBgRole.Defend), $"friend group {size}");
                Assert.That(Role(OpenBgPhase.KeepFight, 1, friend: true), Is.EqualTo(OpenBgRole.Roam), "friend solo");
                foreach (OpenBgPhase phase in Enum.GetValues<OpenBgPhase>())
                    Assert.That(Role(phase, 8, 0, raid: true, friend: true), Is.Not.EqualTo(OpenBgRole.Assault), $"a raid unit that is a keep friend, {phase}");
            });
        }

        [Test]
        public void BystandersNeverAssaultAndRoamAwayFromTheKeepInTheFight()
        {
            Point3D[] field = Field().Concat(new[]
            {
                new Point3D(Keep.X + 2_500, Keep.Y, 3500), new Point3D(Keep.X, Keep.Y - 2_900, 3500), new Point3D(Keep.X - 3_500, Keep.Y + 200, 3500),
            }).ToArray();
            Point3D[] away = OpenBattlegroundRoles.FieldAwayFromKeep(field, Bridges, Keep, OpenBattlegroundRoles.BystanderKeepDistance);
            var rng = new Random(11);
            Assert.Multiple(() =>
            {
                foreach (eRealm realm in Realms)
                {
                    foreach (int size in new[] { 1, 2, 4, 8 })
                        foreach (OpenBgPhase phase in Enum.GetValues<OpenBgPhase>())
                            Assert.That(Role(phase, size, 0, realm: realm), Is.Not.EqualTo(OpenBgRole.Assault), $"{phase}, {realm}, size {size}");
                    foreach (int size in new[] { 1, 2, 4, 8 })
                        Assert.That(Role(OpenBgPhase.KeepFight, size, 0, realm: realm), Is.EqualTo(OpenBgRole.Roam), $"bystander {realm} size {size}");
                }
                Assert.That(OpenBattlegroundRoles.IsBystanderInFight(OpenBgPhase.KeepFight, isRaid: false, isKeepFriend: false), Is.True);
                Assert.That(OpenBattlegroundRoles.IsBystanderInFight(OpenBgPhase.KeepFight, isRaid: true, isKeepFriend: false), Is.False, "the raid");
                Assert.That(OpenBattlegroundRoles.IsBystanderInFight(OpenBgPhase.KeepFight, isRaid: false, isKeepFriend: true), Is.False, "a keep friend");
                Assert.That(OpenBattlegroundRoles.IsBystanderInFight(OpenBgPhase.Push, isRaid: false, isKeepFriend: false), Is.False, "only in the fight");
                Assert.That(OpenBattlegroundRoles.IsBystanderInFight(OpenBgPhase.Retreat, isRaid: false, isKeepFriend: false), Is.False);
                Assert.That(OpenBattlegroundRoles.BystanderKeepDistance, Is.EqualTo(3_000));
                Assert.That(away, Is.Not.Empty);
                Assert.That(away.Length, Is.LessThan(field.Length), "the points near the keep and the bridges are dropped");
                for (int i = 0; i < 200; i++)
                {
                    Point3D point = OpenBattlegroundRoles.RolePoint(OpenBgRole.Roam, eRealm.Hibernia, Bridges, Keep, away, rng);
                    Assert.That(OpenBattlegroundRoles.Distance2D(point, Keep), Is.GreaterThanOrEqualTo(OpenBattlegroundRoles.BystanderKeepDistance));
                    Assert.That(Nearest(point, Bridges.Select(bridge => bridge.Point)), Is.GreaterThanOrEqualTo(OpenBattlegroundRoles.RoamBridgeDistance));
                }
                // The live keep-fight skirmish point (2,500 south of the keep centre) is no longer a bystander roam point.
                Assert.That(away.Any(point => point.X == Keep.X + 2_500 && point.Y == Keep.Y), Is.False);
            });
        }

        [Test]
        public void AnAttackerMustersOnlyInThePushAndNeverAfterAWipeInTheKeepFight()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundRoles.NeedsMuster(OpenBgRole.Assault, OpenBgPhase.Push, false), Is.True);
                Assert.That(OpenBattlegroundRoles.NeedsMuster(OpenBgRole.Assault, OpenBgPhase.Push, true), Is.False, "mustered once per cycle");
                Assert.That(OpenBattlegroundRoles.NeedsMuster(OpenBgRole.Assault, OpenBgPhase.KeepFight, false), Is.False,
                    "a unit that wiped in the keep fight goes back to the gate, not to its bridge");
                Assert.That(OpenBattlegroundRoles.NeedsMuster(OpenBgRole.Defend, OpenBgPhase.Push, false), Is.False);
                Assert.That(OpenBattlegroundRoles.NeedsMuster(OpenBgRole.Lurk, OpenBgPhase.Push, false), Is.False);
            });
        }

        [Test]
        public void ALurkPointIsAProvenFloorAndDeadSpotsAreDroppedForTheCycle()
        {
            var draws = new Queue<Point3D>(new[]
            {
                new Point3D(1_000, 1_000, 3300),  // no floor
                new Point3D(2_000, 2_000, 3300),  // floor on a dead-end ledge: no route from the landing
                new Point3D(3_000, 3_000, 3300),  // floor next to a spot that failed this cycle
                new Point3D(4_000, 4_000, 3300),  // proven
                new Point3D(5_000, 5_000, 3300),
            });
            var dead = new[] { new Point3D(3_050, 3_100, 3200) };
            Point3D Snap(Point3D raw) => raw.X == 1_000 ? null : new Point3D(raw.X + 10, raw.Y, 3200);
            bool Proven(Point3D floor) => floor.X != 2_010;
            var proved = new List<Point3D>();
            Point3D picked = OpenBattlegroundRoles.PickLurkPoint(() => draws.Count > 0 ? draws.Dequeue() : null, Snap,
                floor => { proved.Add(floor); return Proven(floor); }, dead, 6);
            Assert.Multiple(() =>
            {
                Assert.That((picked.X, picked.Y, picked.Z), Is.EqualTo((4_010, 4_000, 3200)), "the snapped floor of the first proven draw");
                Assert.That(proved.Select(point => point.X), Is.EqualTo(new[] { 2_010, 4_010 }), "a spot near a dead one is not even proved");
                Assert.That(OpenBattlegroundRoles.NearDeadSpot(new Point3D(3_000, 3_000, 0), dead), Is.True);
                Assert.That(OpenBattlegroundRoles.NearDeadSpot(new Point3D(3_400, 3_000, 0), dead), Is.False);
                Assert.That(OpenBattlegroundRoles.PickLurkPoint(() => new Point3D(2_000, 2_000, 3300), Snap, Proven, null, 4), Is.Null,
                    "only dead ends: the caller falls back to a camp");
                Assert.That(OpenBattlegroundRoles.PickLurkPoint(() => new Point3D(6_000, 6_000, 3300), Snap, null, null, 1)?.X, Is.EqualTo(6_010),
                    "without a proof (no landing, no navmesh) the snapped floor stands");
            });
        }

        [Test]
        public void RaidGroupsAssaultInPushAndFightOthersKeepTheirPosts()
        {
            Assert.Multiple(() =>
            {
                foreach (int index in new[] { 0, 1, 2 })
                {
                    Assert.That(Role(OpenBgPhase.Push, 8, index, raid: true), Is.EqualTo(OpenBgRole.Assault));
                    Assert.That(Role(OpenBgPhase.KeepFight, 3, index, raid: true), Is.EqualTo(OpenBgRole.Assault));
                }
                Assert.That(Role(OpenBgPhase.Push, 8, 0), Is.EqualTo(OpenBgRole.BridgeWatch), "units outside the raid keep watch");
                Assert.That(Role(OpenBgPhase.Push, 8, 1), Is.EqualTo(OpenBgRole.Roam));
                Assert.That(Role(OpenBgPhase.Push, 8, 0, friend: true), Is.EqualTo(OpenBgRole.BridgeWatch), "keep friends keep watch in the push");
                // A raid unit whose guild took the keep never assaults its own keep.
                Assert.That(Role(OpenBgPhase.KeepFight, 8, 0, raid: true, friend: true), Is.EqualTo(OpenBgRole.Defend));
            });
        }

        [Test]
        public void KeepFriendsDefendInFight()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Role(OpenBgPhase.KeepFight, 8, 0, friend: true), Is.EqualTo(OpenBgRole.Defend));
                Assert.That(Role(OpenBgPhase.KeepFight, 2, 3, friend: true, realm: eRealm.Hibernia), Is.EqualTo(OpenBgRole.Defend),
                    "friendship is the guild's, whatever the realm");
                Assert.That(Role(OpenBgPhase.KeepFight, 8, 0), Is.EqualTo(OpenBgRole.Roam), "a bystander roams");
            });
        }

        [Test]
        public void LosersRetreatWinnersHold()
        {
            Assert.Multiple(() =>
            {
                // Failed raid: the keep's friends still hold it.
                Assert.That(Role(OpenBgPhase.Retreat, 8, raid: true), Is.EqualTo(OpenBgRole.Retreat));
                Assert.That(Role(OpenBgPhase.Retreat, 8, friend: true), Is.EqualTo(OpenBgRole.Defend));
                Assert.That(Role(OpenBgPhase.Retreat, 8), Is.EqualTo(OpenBgRole.Roam));
                // Successful raid: its claiming guild is now the keep's friend and holds it.
                Assert.That(Role(OpenBgPhase.Retreat, 8, raid: true, friend: true), Is.EqualTo(OpenBgRole.Defend));
            });
        }

        [Test]
        public void RaidPartnersHoldTheApproachesOutsideGuardReachAfterTheClaim()
        {
            Point3D[] field =
            {
                new(Keep.X + 1_200, Keep.Y, 3500), new(Keep.X + 3_000, Keep.Y, 3500), new(Keep.X, Keep.Y - 3_500, 3500),
                new(Keep.X + 4_200, Keep.Y, 3500), new(Keep.X - 6_000, Keep.Y, 3500), null,
            };
            Point3D[] approaches = OpenBattlegroundRoles.ApproachPoints(field, Keep);
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundRoles.RaidPartnerHolds(true, true, false), Is.True, "a raid partner of the claiming guild");
                Assert.That(OpenBattlegroundRoles.RaidPartnerHolds(true, true, true), Is.False, "the claiming guild defends the keep itself");
                Assert.That(OpenBattlegroundRoles.RaidPartnerHolds(true, false, false), Is.False, "a bystander stays a bystander");
                Assert.That(OpenBattlegroundRoles.RaidPartnerHolds(false, true, false), Is.False, "no claim yet: the raid assaults");
                Assert.That(approaches.Select(point => point.X - Keep.X + point.Y - Keep.Y), Is.EquivalentTo(new[] { -3_500, 4_200 }),
                    "inside the archers' reach (1,200 and 3,000) and the far field (6,000) are no approaches");
                Assert.That(OpenBattlegroundRoles.ApproachMinDistance, Is.GreaterThan(3_200), "static archers reach 3,200 from the keep centre");
                Assert.That(OpenBattlegroundRoles.ApproachPoints(null, Keep), Is.Empty);
            });
        }

        [Test]
        public void TheRaidHoldsTheFieldWhileTheLordLiesUnclaimed()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OpenBattlegroundRoles.ActsAsKeepFriend(false, true, true, OpenBgPhase.KeepFight), Is.True, "lord down in the fight");
                Assert.That(OpenBattlegroundRoles.ActsAsKeepFriend(false, true, true, OpenBgPhase.Retreat), Is.True);
                Assert.That(OpenBattlegroundRoles.ActsAsKeepFriend(false, true, true, OpenBgPhase.Push), Is.False,
                    "before the fight the raid still goes to the keep as an assault (and claims it)");
                Assert.That(OpenBattlegroundRoles.ActsAsKeepFriend(false, false, true, OpenBgPhase.KeepFight), Is.False, "a bystander");
                Assert.That(OpenBattlegroundRoles.ActsAsKeepFriend(false, true, false, OpenBgPhase.KeepFight), Is.False, "lord standing");
                Assert.That(OpenBattlegroundRoles.ActsAsKeepFriend(true, false, false, OpenBgPhase.Quiet), Is.True, "the claiming guild");
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
