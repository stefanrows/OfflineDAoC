using System;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;

namespace DOL.GS;

// RvR world (Realm-1): the siege rally and the march of the upstream layer (shadowofze/OfflineDAoC 0.38c,
// AutonomousWorldBotController.Rally.cs HandleSiegeRally / TryMarchWithArmy / TryArmyDetour), copied verbatim for port
// session S2 (docs/RVR_PORT_MAP.md). HandleSiegeRally hands the RvR world over to HandleSiegeRallyRealm; the Camlann
// world keeps the fork's rally (Rally.cs). Local changes are marked "Fork:": friend and foe go through PvpCombatant
// (map section 4b), the fork's TravelToDefensivePost (friendly doors by the keep rule) and the upstream direct leg
// (IssueVariedRvrPathRealm) are used.
public sealed partial class AutonomousWorldBotController
{
    private bool HandleSiegeRallyRealm(GameBot bot, string forceId, AutonomousRvrEventLayer.RallyOrder order)
    {
        // The muster is the realm's portal outpost inside the target's frontier, so roamers already
        // there gather with the army too (see AutonomousRvrStaging.TryGetSiegeMuster).
        // Attackers muster at their portal outpost; defenders gather as one quick rally at a nearby friendly keep.
        if (order.HomeMuster && HandleSiegeMuster(bot, forceId, order)) return true;
        var keep = GameServer.KeepManager.GetKeepsOfRegion(_rvrDestination.RegionId)
            .FirstOrDefault(candidate => $"rvr-keep-{candidate.KeepID}" == order.TargetId);
        if (keep == null) return true;
        // Fork: the attacking side by the realm the bot fights for (map section 4b).
        if (PvpCombatant.AllianceRealm(bot) == order.Attacker && TryMarchWithArmy(bot, keep, order.TargetId)) return true;
        // A distant responder must use the same bounded, retained keep route.
        // Do not run a fresh full-country reachability test for every rally slot.
        if (bot.CurrentRegionID == keep.Region &&
            Vector2.DistanceSquared(new(bot.X,bot.Y),new(keep.X,keep.Y)) > 11_500 * 11_500)
        {
            FollowKeepTravel(bot, _rvrDestination);
            return true;
        }
        bool operatorBot = _groupDirective?.IsDynamic != true || _groupDirective.Leader == bot;
        // Equipment procurement belongs to the bounded per-keep job pool.
        // Do not send every arriving leader (and their followers) shopping.
        bot.TempProperties.RemoveProperty("RvrSupplying");
        if (operatorBot && bot.CurrentRegionID != keep.Region && TryFrontierTransport(bot,_rvrDestination)) return true;
        GameBot leader = _groupDirective?.Leader;
        if (leader != null && leader != bot && leader.IsAlive)
        {
            if (bot.CurrentRegionID != keep.Region && TryFrontierTransport(bot,_rvrDestination)) return true;
            if (leader.CurrentRegionID != keep.Region ||
                Vector2.Distance(new(leader.X,leader.Y),new(keep.X,keep.Y)) > AutonomousRvrRally.CampRadius+2500)
                return FollowDynamicGroupLeader(bot,_groupDirective);
        }
        if (operatorBot && _groupDirective?.IsDynamic == true &&
            (bot.CurrentRegionID != keep.Region || Vector2.Distance(new(bot.X,bot.Y),new(keep.X,keep.Y)) > AutonomousRvrRally.CampRadius+2500) &&
            !AutonomousBotGroupCoordinator.IsCohesive(_groupDirective))
        {
            bot.StopMovingOnPath(); bot.StopMoving();
            SetRvrStatus(bot,"Regrouping en route to rally",keep.Name,"Waiting for nearby warband members before continuing together");
            return true;
        }
        var members = bot.Group?.GetMembersInTheGroup().OfType<GameBot>().OrderBy(member => member.DatabaseID).ToArray() ?? [bot];
        int memberIndex = Array.IndexOf(members, bot);
        if (memberIndex < 0 || memberIndex >= order.Slots.Length) return true;
        string key = $"{order.TargetId}:{forceId}:{order.Slots[memberIndex]}";
        if (_physicalRallyKey != key)
        {
            _physicalRallyKey = key;
            _physicalRallyPost = null;
            _nextRallyProjection = 0;
        }
        long now = GameLoop.GameLoopTime;
        if (!_physicalRallyPost.HasValue && now >= _nextRallyProjection)
        {
            _nextRallyProjection = now + 15_000;
            if (AutonomousRvrRally.TryPost(bot, keep, order, memberIndex, out Vector3 point))
                _physicalRallyPost = point;
        }
        if (!_physicalRallyPost.HasValue)
        {
            // Run 19: 27 of 64 Hibernian attackers stood still at Odin's Gate, beside the marching column on the same
            // navmesh, because the full corridor check to a post 10-11k away failed. Keep closing in on the keep's
            // retained route (as distant responders do) and retry the post every 15 s; never stand idle.
            FollowKeepTravel(bot, _rvrDestination);
            SetRvrStatus(bot, "Closing on the rally", keep.Name,
                "No validated rally post yet; walking the keep route and retrying (not counted as present)");
            return true;
        }
        Vector3 post = _physicalRallyPost.Value;
        bool atPost = bot.CurrentRegionID == keep.Region &&
            Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), post) <= AutonomousRvrRally.ArrivalRadius * AutonomousRvrRally.ArrivalRadius;
        // Different regions have unrelated coordinates; use a large sentinel
        // until the actual approach starts instead of rewarding a wrong map.
        AutonomousRvrEventLayer.ReportTravel(order.TargetId, forceId, bot.DatabaseID,
            bot.CurrentRegionID == keep.Region ? Vector3.Distance(new(bot.X, bot.Y, bot.Z), post) : double.PositiveInfinity,
            atPost, now, bot);
        if (atPost)
        {
            bot.StopMovingOnPath();
            bot.StopMoving();
        }
        AutonomousRvrEventLayer.ReportAttendance(order.TargetId, forceId, bot.Realm, bot.DatabaseID,
            atPost && bot.IsAlive && !bot.InCombat && (bot.Brain as BotBrain)?.HasAggro != true, now, bot, post);
        // Fork: the defending side by the realm the bot fights for (map section 4b).
        bool defender = PvpCombatant.AllianceRealm(bot) == order.Defender;
        if (atPost)
        {
            AutonomousStuckWatchdog.MarkProgress(bot, eAutonomousProgressKind.Objective);
            SetRvrStatus(bot, "Holding siege rally", keep.Name,
                $"{(defender ? "Holding an interior defensive post" : "Holding the realm's separated defensive camp")}; " +
                $"attackers advance when their recruited force is ready after at least three minutes; no enemy attendance requirement; " +
                $"preparation deadline in {Math.Max(0, (int)Math.Ceiling(TimeSpan.FromMilliseconds(order.RemainingMilliseconds).TotalMinutes))}m " +
                $"({(keep.IsRelic ? 192 : 128)} cap per realm; actual fighting can start the battle earlier)");
            return true;
        }

        var destination = _rvrDestination with { X = (int)post.X, Y = (int)post.Y, Z = (int)post.Z };
        if (bot.CurrentRegionID != keep.Region)
            TravelRvrObjective(bot, destination);
        else if (defender)
            TravelToDefensivePost(bot, keep, post);
        else
        {
            // Keep the outbound warband around its leader until close to the
            // siege camp, then let each member settle into its reserved post.
            if (leader != null && leader != bot && leader.IsAlive && leader.CurrentRegionID == bot.CurrentRegionID &&
                Vector3.DistanceSquared(new(leader.X, leader.Y, leader.Z), post) > 2200 * 2200)
                FollowDynamicGroupLeader(bot, _groupDirective);
            else IssueVariedRvrPathRealm(bot, post);
        }
        SetRvrStatus(bot, "Traveling to siege rally", keep.Name,
            defender ? "Entering the friendly keep to take a defensive post" :
                "Joining this realm's separate rally camp outside the keep");
        return true;
    }

    private long _armyRegroupUntil;
    private long _nextArmyRegroup;
    public const int ArmyAssistRadius = 2500;
    public const int ArmyCombatHoldRadius = 8000;
    private long _armyCombatHoldUntil;
    private long _nextArmyCombatHold;
    public const int ArmyKeepUpDistance = 900;
    public const int ArmyArrivalRadius = 3000;
    // Run 10 (2026-10-15): Dun Dagda's siege opened with 11 of the army present after five minutes; the leader now waits
    // up to twelve minutes at the camp edge for the column.
    public const long ArmyArrivalHoldMilliseconds = 12 * 60_000;
    private long _armyArrivalHoldSince;
    private Vector3? _marchDetour;
    private const string ArmyDetourProperty = "ArmyMarchDetour";
    private const string ArmyPushProperty = "ArmyMarchPush";
    private AutonomousArmyPush.State _armyPush;
    private long _marchDetourUntil, _nextMarchDetourCheck;

    /// <summary>The march leader's way around the first enemy keep ahead (AutonomousArmyDetour). True when it walks the detour.</summary>
    private bool TryArmyDetour(GameBot bot, AbstractGameKeep keep, string targetId)
    {
        long now = GameLoop.GameLoopTime;
        Vector2 here = new(bot.X, bot.Y);
        if (_marchDetour is Vector3 detour)
        {
            if (Vector2.Distance(here, new(detour.X, detour.Y)) <= AutonomousArmyDetour.ReachedRadius || now > _marchDetourUntil)
            {
                _marchDetour = null;
                _nextMarchDetourCheck = 0;
            }
            else
            {
                TravelRvrObjective(bot, _rvrDestination with { Id = $"rvr-detour-{targetId}", X = (int)detour.X, Y = (int)detour.Y, Z = (int)detour.Z });
                return true;
            }
        }
        if (now < _nextMarchDetourCheck) return false;
        _nextMarchDetourCheck = now + 20_000;
        Vector2? point = AutonomousArmyDetour.FirstDetour(here, new(keep.X, keep.Y),
            AutonomousArmyDetour.HazardsFor(bot.CurrentRegionID, bot.Realm, keep));
        if (point == null) return false;
        Zone zone = bot.CurrentRegion?.GetZone((int)point.Value.X, (int)point.Value.Y);
        IPathfindingMgr nav = PathfindingProvider.Instance;
        if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone)) return false;
        Vector3? floor = nav.GetClosestPoint(zone, new(point.Value.X, point.Value.Y, bot.Z), 2_000, 2_000, 4_000, nav.DefaultFilters);
        if (floor == null) return false;
        _marchDetour = floor;
        _marchDetourUntil = now + 10 * 60_000;
        Log.Info($"RVR_MARCH_DETOUR target={targetId} leader={bot.Name} from={(int)bot.X},{(int)bot.Y} " +
                 $"via={(int)floor.Value.X},{(int)floor.Value.Y},{(int)floor.Value.Z}");
        TravelRvrObjective(bot, _rvrDestination with { Id = $"rvr-detour-{targetId}", X = (int)floor.Value.X, Y = (int)floor.Value.Y, Z = (int)floor.Value.Z });
        return true;
    }

    /// <summary>
    /// The march from the muster to the siege camp as one column: the leader walks the keep route and holds briefly
    /// when most of the army has fallen behind; everyone else keeps within reach of the leader; a marcher who is not
    /// fighting joins the fight of any army member under attack nearby. Run 17: Midgard's 64 on Caer Sursbrooke and
    /// Albion's 64 on Blendrake Faste each walked 50,000+ units one by one and lost half their strength to roaming
    /// enemies before either reached its camp. Only for the long march; inside the camp radius each bot takes its post.
    /// </summary>
    private bool TryMarchWithArmy(GameBot bot, AbstractGameKeep keep, string targetId)
    {
        // Any march turn counts as leading, also while the leader is still travelling to the keep's region (run 3:
        // reporting only inside that region replaced every leader of a cross-realm march every 30 seconds).
        AutonomousRvrEventLayer.ReportMarchLeading(targetId, bot);
        if (bot.CurrentRegionID != keep.Region) return false;
        Vector2 here = new(bot.X, bot.Y);
        if (Vector2.Distance(here, new(keep.X, keep.Y)) <= AutonomousRvrRally.CampRadius + 2500)
        {
            // Run 19: the march ended here and handed over to rally posts the bots could not validate, so the army
            // stood still 11k from Bledmeer. The army's arrival opens the battle (owner: start once the threshold is
            // met, no waiting at the keep); the bots then go straight to the assault and their siege jobs.
            if (AutonomousRvrEventLayer.MarchLeader(targetId) == bot)
            {
                AutonomousRvrEventLayer.ReportMarchLeading(targetId, bot);
                // Run 2026-10-13: the leader reached Dun Lamfhota alone and opened the relic siege with one attacker
                // present while its column trailed 6,000-30,000 units behind. The leader now waits at the camp edge
                // (at most five minutes) until most of the column has closed up, then the army attacks together.
                GameBot[] column = AutonomousRvrEventLayer.ArmyMembers(targetId)
                    .Where(member => member.CurrentRegionID == bot.CurrentRegionID).ToArray();
                int closed = column.Count(member => bot.IsWithinRadius(member, ArmyArrivalRadius) ||
                    Vector2.Distance(new(member.X, member.Y), new(keep.X, keep.Y)) <= AutonomousRvrRally.CampRadius + 2500);
                long tick = GameLoop.GameLoopTime;
                if (_armyArrivalHoldSince == 0) _armyArrivalHoldSince = tick;
                if (column.Length >= 8 && closed < column.Length * 6 / 10 && tick - _armyArrivalHoldSince < ArmyArrivalHoldMilliseconds)
                {
                    bot.StopMovingOnPath(); bot.StopMoving();
                    SetRvrStatus(bot, "Army closing up before the assault", keep.Name, $"{closed} of {column.Length} at the camp");
                    return true;
                }
                _armyArrivalHoldSince = 0;
                AutonomousRvrEventLayer.ReportArmyArrived(targetId, tick);
            }
            return false;
        }
        GameBot leader = AutonomousRvrEventLayer.MarchLeader(targetId);
        if (leader == null || leader.CurrentRegionID != bot.CurrentRegionID) return false;
        GameBot[] army = AutonomousRvrEventLayer.ArmyMembers(targetId);
        long now = GameLoop.GameLoopTime;

        // A column that has stood in one place too long pushes on (AutonomousArmyPush).
        if (leader == bot)
        {
            bool push = AutonomousArmyPush.Update(ref _armyPush, here, now, out bool started);
            bot.TempProperties.SetProperty(ArmyPushProperty, push);
            if (started)
                Log.Info($"RVR_MARCH_PUSH target={targetId} leader={bot.Name} at={(int)bot.X},{(int)bot.Y} " +
                         $"stalledSeconds={AutonomousArmyPush.StallMilliseconds / 1000} column={army.Length}");
        }
        bool pushing = leader.TempProperties.GetProperty<bool>(ArmyPushProperty);
        // Fork: an enemy by the realm rule (map section 4b), so a companion or pet of the bot's realm is never dropped.
        if (pushing && bot.TargetObject is GameLiving engaged && !PvpCombatant.AreAllied(bot, engaged) &&
            (leader == bot || engaged.TargetObject != bot))
        {
            if (bot.Brain is BotBrain pushBrain) pushBrain.ClearAggroList();
            bot.StopAttack();
            bot.TargetObject = null;
        }

        // Help a member under attack nearby before anything else (not while pushing on, and not against an enemy this
        // bot already gave up on as unreachable).
        GameBot fighting = pushing ? null : army.FirstOrDefault(member => member != bot && member.CurrentRegionID == bot.CurrentRegionID &&
            (member.InCombat || member.IsAttacking) && member.TargetObject is GameLiving foe && foe.IsAlive &&
            !PvpCombatant.AreAllied(bot, foe) && !IgnoresTarget(foe) && bot.IsWithinRadius(member, ArmyAssistRadius));
        if (fighting?.TargetObject is GameLiving enemy && bot.Brain is BotBrain brain &&
            GameServer.ServerRules.IsAllowedToAttack(bot, enemy, true))
        {
            bot.TargetObject = enemy;
            brain.AddToAggroList(enemy, Math.Max(100, enemy.EffectiveLevel * 12));
            brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
            SetRvrStatus(bot, "Defending the army column", keep.Name, $"Helping {fighting.Name} against {enemy.Name}");
            return true;
        }

        if (leader == bot)
        {
            AutonomousRvrEventLayer.ReportMarchLeading(targetId, bot);
            int near = army.Count(member => member.CurrentRegionID == bot.CurrentRegionID && bot.IsWithinRadius(member, ArmyAssistRadius));
            int marching = army.Count(member => member.CurrentRegionID == bot.CurrentRegionID); // the fallen who released home don't hold the column
            if (pushing)
            {
                _armyRegroupUntil = 0;
                _armyCombatHoldUntil = 0;
            }
            if (now < _armyRegroupUntil && near < marching * 6 / 10)
            {
                bot.StopMovingOnPath(); bot.StopMoving();
                SetRvrStatus(bot, "Army regrouping", keep.Name, $"{near} of {marching} with the leader; waiting for the column");
                return true;
            }
            // Run 18: 13 of 50 marchers fought a hunter's avatar 10k behind while the leader walked on, and
            // the column stretched to 10k before they caught up. The leader holds while a quarter of the
            // column is fighting nearby (60 seconds at most, then two minutes before it holds again).
            int fightingBehind = army.Count(member => member.CurrentRegionID == bot.CurrentRegionID &&
                (member.InCombat || member.IsAttacking) && bot.IsWithinRadius(member, ArmyCombatHoldRadius));
            if (!pushing && marching >= 8 && fightingBehind >= Math.Max(3, marching / 4) &&
                (now < _armyCombatHoldUntil || now >= _nextArmyCombatHold))
            {
                if (now >= _armyCombatHoldUntil)
                {
                    _armyCombatHoldUntil = now + 60_000;
                    _nextArmyCombatHold = now + 180_000;
                }
                bot.StopMovingOnPath(); bot.StopMoving();
                SetRvrStatus(bot, "Army holding for the column", keep.Name, $"{fightingBehind} of {marching} fighting behind the leader");
                return true;
            }
            if (!pushing && now >= _nextArmyRegroup && marching >= 8 && near < marching / 2)
            {
                // At most 45 seconds of waiting, then two minutes of marching before the next wait.
                _armyRegroupUntil = now + 45_000;
                _nextArmyRegroup = now + 165_000;
                bot.StopMovingOnPath(); bot.StopMoving();
                SetRvrStatus(bot, "Army regrouping", keep.Name, $"{near} of {marching} with the leader; waiting for the column");
                return true;
            }
            bool detouring = TryArmyDetour(bot, keep, targetId);
            bot.TempProperties.SetProperty(ArmyDetourProperty, detouring);
            if (detouring)
            {
                SetRvrStatus(bot, "Leading the army around an enemy keep", keep.Name, $"{near} of {marching} marching with the leader");
                return true;
            }
            FollowKeepTravel(bot, _rvrDestination);
            SetRvrStatus(bot, "Leading the army", keep.Name, $"{near} of {marching} marching with the leader");
            return true;
        }

        if (!bot.IsWithinRadius(leader, ArmyKeepUpDistance))
        {
            IssuePath(bot, new Vector3(leader.X, leader.Y, leader.Z));
            SetRvrStatus(bot, "Marching with the army", keep.Name, $"Catching up with {leader.Name}");
            return true;
        }
        if (!leader.IsMoving)
        {
            // The leader is holding (regroup or a fight behind); walking on past it splits the column.
            bot.StopMovingOnPath(); bot.StopMoving();
            SetRvrStatus(bot, "Waiting with the army", keep.Name, $"Holding with {leader.Name}");
            return true;
        }
        // During the leader's detour around an enemy keep the column walks with the leader, not its own keep route.
        if (leader.TempProperties.GetProperty<bool>(ArmyDetourProperty))
        {
            IssuePath(bot, new Vector3(leader.X, leader.Y, leader.Z));
            SetRvrStatus(bot, "Marching with the army", keep.Name, $"Around an enemy keep with {leader.Name}");
            return true;
        }
        FollowKeepTravel(bot, _rvrDestination);
        SetRvrStatus(bot, "Marching with the army", keep.Name, $"In the column with {leader.Name}");
        return true;
    }

    /// <summary>
    /// RvR world: the upstream roaming leg (IssueVariedRvrPath of 0.38c), one direct via-point
    /// (<see cref="AutonomousRvrTravel.ChooseWaypoint"/>). The Camlann world keeps the fork's route variants, mob bends
    /// and observe re-plans (<see cref="IssueVariedRvrPath"/>).
    /// </summary>
    private bool IssueVariedRvrPathRealm(GameBot bot, Vector3 destination)
    {
        if (!_rvrTravelWaypoint.HasValue || Vector3.DistanceSquared(_rvrTravelGoal, destination) > 500 * 500 ||
            Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), _rvrTravelWaypoint.Value) < 180 * 180)
        {
            _rvrTravelGoal = destination;
            _rvrTravelWaypoint = AutonomousRvrTravel.ChooseWaypoint(bot, destination);
        }
        // ChooseWaypoint validates both legs when the final goal is in the
        // same zone. Continue that existing route without a full AI wake.
        // A changed goal or cross-zone route still needs its own planning.
        Vector3? continuation = _rvrTravelGoal == destination && _rvrTravelWaypoint.Value != destination &&
            bot.CurrentZone != null &&
            bot.CurrentRegion.GetZone((int)destination.X, (int)destination.Y) == bot.CurrentZone &&
            bot.CurrentRegion.GetZone((int)_rvrTravelWaypoint.Value.X, (int)_rvrTravelWaypoint.Value.Y) == bot.CurrentZone
                ? destination : null;
        return IssuePath(bot, _rvrTravelWaypoint.Value, continuation);
    }
}
