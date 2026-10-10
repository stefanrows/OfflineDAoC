using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.Database;
using DOL.GS.Keeps;

namespace DOL.GS
{
    /// <summary>Runtime keep guard that never writes a mob row. Mirrors the portal keep subclasses.</summary>
    public sealed class WallPostArcher : GuardArcher
    {
        public override void SaveIntoDatabase() { }
    }

    /// <summary>Runtime keep guard that never writes a mob row.</summary>
    public sealed class WallPostFighter : GuardFighter
    {
        public override void SaveIntoDatabase() { }
    }

    /// <summary>Runtime keep guard that never writes a mob row.</summary>
    public sealed class WallPostHealer : GuardHealer
    {
        public override void SaveIntoDatabase() { }
    }

    /// <summary>Runtime keep guard that never writes a mob row.</summary>
    public sealed class WallPostCaster : GuardCaster
    {
        public override void SaveIntoDatabase() { }
    }

    /// <summary>
    /// Guards for the campaign central keeps that have no native guards outside. Molvik Faste (keep 132) is the one campaign
    /// keep with native guard mobs. Its non-lord native guards are copied as a whole layout: each guard's offset from the keep
    /// centre, in the keep frame (the placement mapping of keep heading, with its mirrored Y), its height above the keep and its
    /// heading relative to the keep. Every other campaign central keep (except Proving Grounds, a single tower) receives the same
    /// layout in its own frame. Each point is snapped to the navigation floor, and kept only when it snaps close to the layout
    /// height, stays inside the keep, is reachable from a campaign camp and is spaced from every other guard. The guards are
    /// runtime-only: their rows are never written, so each server start derives and places them again.
    /// </summary>
    public static class BattlegroundKeepWallGuards
    {
        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(BattlegroundKeepWallGuards));

        public const int TemplateKeepId = 132;
        public const ushort TemplateRegion = 241;
        public const int ProvingGroundsKeepId = 140;
        public const int MaximumGuardsPerKeep = 40;
        /// <summary>No two guards, native or placed, stand closer than this (three-dimensional).</summary>
        public const float PostSpacing = 150f;
        /// <summary>A snapped floor further than this from the layout height is rejected.</summary>
        public const double FloorSnapTolerance = 300;
        /// <summary>Path queries one keep may spend on the camp reachability proof.</summary>
        public const int MaximumReachQueries = 1200;
        // Turning and heading units as GameKeepComponent and the 4096-unit mob heading use them.
        private const double HeadingUnitDegrees = 0.08789;
        private const int HeadingUnits = 4096;
        // Model of the GM-created "new mob" rows. A template carrying it takes the class default model instead.
        private const ushort PlaceholderModel = 408;
        // Garrison search reach for the snap, as BattlegroundNativeKeepData uses for the garrison.
        private const int SnapHorizontalRange = 64;
        private const int SnapVerticalRange = 256;

        private static readonly Dictionary<string, Type> PostClasses = new()
        {
            [typeof(GuardArcher).ToString()] = typeof(WallPostArcher),
            [typeof(GuardFighter).ToString()] = typeof(WallPostFighter),
            [typeof(GuardHealer).ToString()] = typeof(WallPostHealer),
            [typeof(GuardCaster).ToString()] = typeof(WallPostCaster),
        };
        // EnsureGarrison names its members "<keep name> <role>" with no guild. It never marks them otherwise.
        private static readonly string[] GarrisonRoles = { "Lord", "Guard", "Archer", "Healer" };
        private static readonly object TemplateLock = new();
        private static IReadOnlyList<PostTemplate> _templates;

        /// <summary>A keep's name, world position, Z and heading in degrees.</summary>
        public readonly record struct KeepSample(string Name, int X, int Y, int Z, ushort Heading);

        /// <summary>A mob row: class, name, guild, position, heading and model.</summary>
        public readonly record struct GuardSample(string ClassType, string Name, string Guild, int X, int Y, int Z, ushort Heading, ushort Model);

        /// <summary>A frame: origin, the heading in degrees, and the same heading in 4096 units.</summary>
        public readonly record struct PostFrame(double X, double Y, double Degrees, int HeadingUnits);

        /// <summary>A native guard in the keep frame: offset, height above the keep, heading relative to the keep, class and model.</summary>
        public readonly record struct PostTemplate(double LocalX, double LocalY, int DeltaZ, int RelativeHeading, Type GuardType, ushort Model);

        /// <summary>A planned guard: its template, the snapped point and heading.</summary>
        public readonly record struct PostPlacement(PostTemplate Template, Vector3 Point, ushort Heading);

        /// <summary>The planner's result. Candidates equal placed plus the three drop counts.</summary>
        public readonly record struct PostPlan(List<PostPlacement> Placements, int Candidates, int DroppedSnap, int DroppedUnreachable, int DroppedSpacing);

        /// <summary>The frame of a keep: its position, and the heading that turns the keep-local offsets.</summary>
        public static PostFrame KeepFrame(int keepX, int keepY, ushort keepHeading) =>
            new(keepX, keepY, keepHeading, (int)(keepHeading / HeadingUnitDegrees));

        // Turning by a heading is its own inverse: the placement mapping with a mirrored Y axis, so one function maps both ways.
        private static (double X, double Y) Turn(double degrees, double x, double y)
        {
            double radians = degrees * Math.PI / 180.0;
            return (x * Math.Cos(radians) + y * Math.Sin(radians), x * Math.Sin(radians) - y * Math.Cos(radians));
        }

        /// <summary>A world point in a frame's local offsets.</summary>
        public static (double X, double Y) ToLocal(PostFrame frame, double worldX, double worldY) =>
            Turn(frame.Degrees, worldX - frame.X, worldY - frame.Y);

        /// <summary>A frame-local offset in the world.</summary>
        public static (double X, double Y) ToWorld(PostFrame frame, double localX, double localY)
        {
            (double dx, double dy) = Turn(frame.Degrees, localX, localY);
            return (frame.X + dx, frame.Y + dy);
        }

        private static int NormalizeHeading(int units) => ((units % HeadingUnits) + HeadingUnits) % HeadingUnits;

        /// <summary>True for a row EnsureGarrison wrote: its keep name and a role, with no guild.</summary>
        public static bool IsGarrisonRow(string keepName, GuardSample guard) =>
            string.IsNullOrEmpty(guard.Guild) && GarrisonRoles.Any(role => guard.Name == $"{keepName} {role}");

        /// <summary>
        /// The layout of one keep's non-lord native guards, in its frame. A row qualifies when its class is an archer, fighter,
        /// healer or caster guard and it is not a garrison row. Lords, merchants and hasteners do not qualify.
        /// </summary>
        public static List<PostTemplate> DeriveTemplates(KeepSample keep, IReadOnlyList<GuardSample> guards)
        {
            PostFrame frame = KeepFrame(keep.X, keep.Y, keep.Heading);
            var templates = new List<PostTemplate>();
            foreach (GuardSample guard in guards)
            {
                if (!PostClasses.TryGetValue(guard.ClassType, out Type type) || IsGarrisonRow(keep.Name, guard)) continue;
                (double localX, double localY) = ToLocal(frame, guard.X, guard.Y);
                templates.Add(new PostTemplate(Math.Round(localX), Math.Round(localY), guard.Z - keep.Z,
                    NormalizeHeading(guard.Heading - frame.HeadingUnits), type, ModelFor(type, guard.Model)));
            }
            return templates;
        }

        private static ushort ModelFor(Type type, ushort model)
        {
            if (model != 0 && model != PlaceholderModel) return model;
            if (type == typeof(WallPostArcher)) return GuardTemplateMgr.SaracenMale;
            if (type == typeof(WallPostFighter)) return GuardTemplateMgr.BritonMale;
            return GuardTemplateMgr.AvalonianMale;
        }

        /// <summary>
        /// Plans the guards for one keep. Each template is placed in the keep frame, then snapped to the floor. A candidate is dropped
        /// when the snap fails (no floor, a floor more than FloorSnapTolerance from the layout height, or outside the keep area), when
        /// it is within PostSpacing of a guard already standing or planned, when the keep is at MaximumGuardsPerKeep, or when no camp
        /// reaches it. Candidates are taken in template order. The order is spacing and cap first, then reachability, so the path
        /// budget is spent only on candidates that could be placed.
        /// </summary>
        public static PostPlan PlanPlacements(KeepSample keep, IReadOnlyList<PostTemplate> templates, IReadOnlyList<Vector3> existing,
            Func<Vector3, Vector3?> snap, Func<Vector3, bool> reachable)
        {
            PostFrame frame = KeepFrame(keep.X, keep.Y, keep.Heading);
            var placements = new List<PostPlacement>();
            var taken = new List<Vector3>(existing);
            int dropSnap = 0, dropUnreachable = 0, dropSpacing = 0;
            foreach (PostTemplate template in templates)
            {
                (double x, double y) = ToWorld(frame, template.LocalX, template.LocalY);
                var hint = new Vector3((int)x, (int)y, keep.Z + template.DeltaZ);
                if (snap(hint) is not Vector3 point || Math.Abs(point.Z - hint.Z) > FloorSnapTolerance)
                {
                    dropSnap++;
                    continue;
                }
                if (placements.Count >= MaximumGuardsPerKeep || taken.Any(other => Vector3.Distance(other, point) < PostSpacing))
                {
                    dropSpacing++;
                    continue;
                }
                if (!reachable(point))
                {
                    dropUnreachable++;
                    continue;
                }
                placements.Add(new PostPlacement(template, point, (ushort)NormalizeHeading(frame.HeadingUnits + template.RelativeHeading)));
                taken.Add(point);
            }
            return new PostPlan(placements, templates.Count, dropSnap, dropUnreachable, dropSpacing);
        }

        /// <summary>The layout derived from Molvik's save rows, derived once per server run.</summary>
        public static IReadOnlyList<PostTemplate> Templates()
        {
            lock (TemplateLock)
            {
                return _templates ??= LoadTemplates();
            }
        }

        private static List<PostTemplate> LoadTemplates()
        {
            try
            {
                DbKeep keepRow = GameServer.Database.SelectObject<DbKeep>(DB.Column("KeepID").IsEqualTo(TemplateKeepId));
                if (keepRow == null)
                {
                    Log.Warn($"BATTLEGROUND_KEEP_WALL_GUARDS_TEMPLATE keep={TemplateKeepId} reason=no_keep_row");
                    return new List<PostTemplate>();
                }
                var keep = new KeepSample(keepRow.Name, keepRow.X, keepRow.Y, keepRow.Z, keepRow.Heading);
                var guards = GameServer.Database.SelectObjects<DbMob>(DB.Column("Region").IsEqualTo(TemplateRegion))
                    .Select(row => new GuardSample(row.ClassType, row.Name, row.Guild, row.X, row.Y, row.Z, row.Heading, row.Model)).ToList();
                List<PostTemplate> templates = DeriveTemplates(keep, guards);
                Log.Info($"BATTLEGROUND_KEEP_WALL_GUARDS_TEMPLATE guards={templates.Count}");
                return templates;
            }
            catch (Exception exception)
            {
                Log.Error("BATTLEGROUND_KEEP_WALL_GUARDS_TEMPLATE_FAILED", exception);
                return new List<PostTemplate>();
            }
        }

        /// <summary>
        /// Adds the Molvik layout to one campaign central keep. Called once per keep at campaign start, after the garrison is placed,
        /// with the campaign's camps as the outside positions for the reachability proof. A failure is logged and does not stop the campaign.
        /// </summary>
        public static void Ensure(BattlegroundDefinition definition, AbstractGameKeep keep, IReadOnlyList<Point3D> outside)
        {
            if (definition == null || keep == null || outside == null || outside.Count == 0) return;
            if (keep.KeepID == TemplateKeepId || keep.KeepID == ProvingGroundsKeepId || keep.IsPortalKeep || keep.IsRelic) return;
            if (!(keep.DBKeep?.CreateInfo ?? string.Empty).StartsWith(BattlegroundKeepLayouts.OfflineKeepPrefix, StringComparison.Ordinal)) return;
            try
            {
                IReadOnlyList<PostTemplate> templates = Templates();
                Zone zone = WorldMgr.GetZone(definition.ZoneId);
                var nav = PathfindingProvider.Instance;
                if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone) || keep.Area == null)
                {
                    Log.Warn($"BATTLEGROUND_KEEP_WALL_GUARDS_UNAVAILABLE region={keep.Region} keep={keep.KeepID} reason=no_navmesh_or_area");
                    return;
                }
                List<Vector3> existing;
                lock (keep.Guards)
                {
                    existing = keep.Guards.Values.Select(guard => new Vector3(guard.X, guard.Y, guard.Z)).ToList();
                }
                int budget = MaximumReachQueries;
                Vector3? SnapFloor(Vector3 hint)
                {
                    Vector3? floor = nav.GetClosestPoint(zone, hint, SnapHorizontalRange, SnapHorizontalRange, SnapVerticalRange, nav.DefaultFilters);
                    if (floor is not Vector3 point) return null;
                    return keep.Area.IsContaining((int)point.X, (int)point.Y, (int)point.Z, false) ? point : null;
                }
                bool Reachable(Vector3 point) => ReachableFromCamps(point, zone, outside, ref budget);
                var sample = new KeepSample(keep.Name, keep.X, keep.Y, keep.Z, keep.Heading);
                PostPlan plan = PlanPlacements(sample, templates, existing, SnapFloor, Reachable);
                int placed = 0;
                foreach (PostPlacement post in plan.Placements)
                {
                    if (SpawnPost(keep, post)) placed++;
                }
                Log.Info($"BATTLEGROUND_KEEP_WALL_GUARDS region={keep.Region} keep={keep.KeepID} candidates={plan.Candidates} placed={placed} " +
                    $"dropped_snap={plan.DroppedSnap} dropped_unreachable={plan.DroppedUnreachable} dropped_spacing={plan.DroppedSpacing}");
            }
            catch (Exception exception)
            {
                Log.Error($"BATTLEGROUND_KEEP_WALL_GUARDS_FAILED region={keep.Region} keep={keep.KeepID}", exception);
            }
        }

        // Same proof as BattlegroundNativeKeepData.ReachableFromCamps: default-filter reachability from any camp, spending the budget.
        private static bool ReachableFromCamps(Vector3 target, Zone zone, IReadOnlyList<Point3D> outside, ref int budget)
        {
            var nav = PathfindingProvider.Instance;
            var nodes = new WrappedPathfindingNode[512];
            foreach (Point3D camp in outside)
            {
                if (budget <= 0) return false;
                budget--;
                var origin = new Vector3(camp.X, camp.Y, camp.Z);
                if (nav.GetPathStraight(zone, origin, target, nav.DefaultFilters, nodes).Status == PathfindingStatus.PathFound) return true;
            }
            return false;
        }

        private static bool SpawnPost(AbstractGameKeep keep, PostPlacement post)
        {
            // The row lives in memory only. GameKeepGuard.LoadFromDatabase binds the guard to the keep area it stands in, like a stored guard.
            DbMob row = new()
            {
                ClassType = post.Template.GuardType.ToString(), Name = $"{keep.Name} Wall Guard", Guild = string.Empty,
                Region = keep.Region, X = (int)post.Point.X, Y = (int)post.Point.Y, Z = (int)post.Point.Z, Heading = post.Heading,
                Realm = 0, Level = 50, Model = post.Template.Model, Size = 50,
            };
            var guard = (GameKeepGuard)Activator.CreateInstance(post.Template.GuardType);
            try
            {
                guard.LoadFromDatabase(row);
                if (guard.Component?.Keep == keep && guard.AddToWorld())
                {
                    guard.ChangeGuild();
                    return true;
                }
            }
            catch (Exception exception)
            {
                Log.Error("BATTLEGROUND_KEEP_WALL_GUARD_FAILED", exception);
            }
            keep.Guards.Remove(row.ObjectId);
            guard.Delete();
            return false;
        }
    }
}
