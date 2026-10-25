using System;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS;

// RvR world (Realm-1): the staged-assault muster of the upstream layer (shadowofze/OfflineDAoC 0.38c,
// AutonomousWorldBotController.Rally.cs HandleSiegeMuster / TryGetDefenseMuster), copied verbatim for port session S1
// (docs/RVR_PORT_MAP.md). HandleSiegeRally calls it in the RvR world while the rally order is a home muster; the march
// column behind a march leader (TryMarchWithArmy) follows in session S2.
public sealed partial class AutonomousWorldBotController
{
    private string _musterKey;
    private Vector3? _musterPost;

    /// <summary>
    /// Staged assault, first stage: an attacker gathers at a reserved post around its realm's portal
    /// outpost in the target frontier (else its border keep) until the army's march order. Warbands used to walk
    /// into the enemy frontier one at a time and were picked off before the siege camp filled.
    /// </summary>
    private bool HandleSiegeMuster(GameBot bot, string forceId, AutonomousRvrEventLayer.RallyOrder order)
    {
        AutonomousRvrStaging.BorderKeep home;
        if (bot.Realm == order.Attacker ? !AutonomousRvrStaging.TryGetSiegeMuster(bot.Realm, _rvrDestination.RegionId, out home)
                : !TryGetDefenseMuster(bot, order.TargetId, out home)) return false;
        var members = bot.Group?.GetMembersInTheGroup().OfType<GameBot>().OrderBy(member => member.DatabaseID).ToArray() ?? [bot];
        int memberIndex = Array.IndexOf(members, bot);
        if (memberIndex < 0 || memberIndex >= order.Slots.Length) return true;
        int slot = order.Slots[memberIndex];
        string key = $"{order.TargetId}:{forceId}:{slot}";
        if (_musterKey != key) { _musterKey = key; _musterPost = null; }
        Region region = WorldMgr.GetRegion(home.RegionId);
        if (!_musterPost.HasValue && region != null)
        {
            // A sunflower spread around the keep: every slot its own spot, 120 units per ring.
            double angle = slot * 2.39996323;
            float radius = 450 + 120 * MathF.Sqrt(slot);
            var raw = home.Position + new Vector3((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius, 0);
            Zone zone = region.GetZone((int)raw.X, (int)raw.Y);
            var nav = PathfindingProvider.Instance;
            Vector3? floor = zone != null && nav.IsAvailable && nav.HasNavmesh(zone)
                ? nav.GetClosestPoint(zone, raw, 160, 160, 800, nav.DefaultFilters) : raw;
            _musterPost = floor ?? home.Position;
        }
        Vector3 post = _musterPost ?? home.Position;
        long now = GameLoop.GameLoopTime;
        bool atPost = bot.CurrentRegionID == home.RegionId &&
            Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), post) <= 160 * 160;
        AutonomousRvrEventLayer.ReportMusterAttendance(order.TargetId, forceId, bot.DatabaseID,
            atPost && bot.IsAlive && !bot.InCombat && (bot.Brain as BotBrain)?.HasAggro != true, now, bot, post);
        if (atPost)
        {
            bot.StopMovingOnPath();
            bot.StopMoving();
            AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Objective);
            SetRvrStatus(bot, $"Mustering at {home.Name}", order.TargetId,
                "Gathering with the realm's army; it marches on the keep together once enough have assembled");
            return true;
        }
        // A plain point, not the target keep's id: with "rvr-keep-N" the walk took the keep-assault
        // approach planner (gates of a keep in another region) and nobody reached the muster (run 10).
        TravelRvrObjective(bot, _rvrDestination with { Id = $"rvr-muster-{home.RegionId}", RegionId = home.RegionId,
            X = (int)post.X, Y = (int)post.Y, Z = (int)post.Z });
        // Abroad, the frontier transport's own status (medallion, boarding) stays visible.
        if (bot.CurrentRegionID == home.RegionId)
            SetRvrStatus(bot, $"Joining the muster at {home.Name}", order.TargetId,
                "Walking to the army's muster at the border keep before the march on the siege target");
        return true;
    }

    /// <summary>
    /// The defense rally gathers at the friendly keep or tower nearest the attacked keep (at least 3,000 units
    /// away, same region), else the realm's border keep, then marches in together.
    /// </summary>
    private bool TryGetDefenseMuster(GameBot bot, string targetId, out AutonomousRvrStaging.BorderKeep muster)
    {
        muster = default;
        var keeps = GameServer.KeepManager.GetKeepsOfRegion(_rvrDestination.RegionId);
        var target = keeps.FirstOrDefault(keep => $"rvr-keep-{keep.KeepID}" == targetId);
        if (target == null) return false;
        var friendly = keeps.Where(keep => keep != target && keep.Realm == bot.Realm &&
                Vector2.DistanceSquared(new(keep.X, keep.Y), new(target.X, target.Y)) >= 3_000f * 3_000f)
            .OrderBy(keep => Vector2.DistanceSquared(new(keep.X, keep.Y), new(target.X, target.Y))).FirstOrDefault();
        if (friendly != null)
        {
            muster = new(friendly.Region, new(friendly.X, friendly.Y, friendly.Z), friendly.Name);
            return true;
        }
        return AutonomousRvrStaging.TryGetBorderKeepNear(bot.Realm, target.Region, new(target.X, target.Y), out muster);
    }
}
