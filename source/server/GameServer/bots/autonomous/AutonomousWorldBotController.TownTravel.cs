using System;
using System.Numerics;

namespace DOL.GS;

public sealed partial class AutonomousWorldBotController
{
    private AllRealmsTeleporter _townMeetupPorter;
    private AllRealmsTeleporter _failedTownMeetupPorter;
    private string _townMeetupGroupId = string.Empty;
    private ushort _townMeetupRegion;
    private long _nextTownMeetupRouteSearch;
    private bool _townMeetupNoRoute;

    private bool HandleTownMeetupTravel(GameBot bot, AutonomousBotGroupCoordinator.Directive directive)
    {
        if (bot == null || directive?.IsDynamic != true ||
            !AutonomousBotGroupCoordinator.IsRemoteMeetupMember(bot, directive))
            return false;

        if (!string.Equals(_townMeetupGroupId, directive.GroupId, System.StringComparison.Ordinal) ||
            _townMeetupRegion != directive.RendezvousRegion)
        {
            _townMeetupGroupId = directive.GroupId;
            _townMeetupRegion = directive.RendezvousRegion;
            _failedTownMeetupPorter = null;
            _townMeetupPorter = null;
            _nextTownMeetupRouteSearch = 0;
            _townMeetupNoRoute = false;
            ResetRouteOrderState();
        }

        // An RvR group musters at a border hub. A member with no porter in its
        // region (an enemy frontier, a dungeon) walks the zone connections
        // there instead of standing until the muster is over.
        bool rvr = directive.ObjectiveKind == eAutonomousObjectiveKind.RvR;
        long now = GameLoop.GameLoopTime;
        if (_townMeetupPorter == null && now < _nextTownMeetupRouteSearch)
            return rvr && _townMeetupNoRoute
                ? TravelToGroupRegion(bot, directive, directive.RendezvousRegion,
                    (int)directive.Rendezvous.X, (int)directive.Rendezvous.Y, directive.RendezvousName)
                : true;

        bool porterMovedOrStopped = _townMeetupPorter != null &&
            (_townMeetupPorter.ObjectState != GameObject.eObjectState.Active ||
             _townMeetupPorter.CurrentRegionID != bot.CurrentRegionID);
        if (_townMeetupPorter == null || porterMovedOrStopped || now >= _nextTownMeetupRouteSearch)
        {
            _nextTownMeetupRouteSearch = now + 30_000;
            if (!AutonomousBotTownTravel.TryGetTownRoute(bot, directive.RendezvousRegion, directive.Rendezvous,
                    out AllRealmsTeleporter porter, out _, out _, _failedTownMeetupPorter, includeBorderHub: rvr))
            {
                _townMeetupPorter = null;
                _townMeetupNoRoute = true;
                if (rvr)
                    return TravelToGroupRegion(bot, directive, directive.RendezvousRegion,
                        (int)directive.Rendezvous.X, (int)directive.Rendezvous.Y, directive.RendezvousName);
                bot.StopMovingOnPath();
                bot.StopMoving();
                SetStatus(bot, "Waiting for town teleporter", directive.SharedGoal,
                    $"No active AllRealmsTeleporter route from region {bot.CurrentRegionID} reaches {directive.RendezvousName}; the meetup deadline remains authoritative");
                return true;
            }
            _townMeetupPorter = porter;
            _townMeetupNoRoute = false;
        }

        if (!AutonomousBotTownTravel.IsAtInteractionDistance(bot, _townMeetupPorter))
        {
            Vector3 porterPoint = new(_townMeetupPorter.X, _townMeetupPorter.Y, _townMeetupPorter.Z);
            if (TryBeginFasterStableRoute(bot, porterPoint, _townMeetupPorter.Name))
            {
                SetStatus(bot, "Riding toward town teleporter", directive.SharedGoal,
                    $"Using a real stablemaster route toward {_townMeetupPorter.Name}");
                return true;
            }
            if (!TryResolveConnectedApproach(bot, porterPoint, Math.Max(32, WorldMgr.INTERACT_DISTANCE / 2),
                    out Vector3 approach) || !IssuePath(bot, approach, preciseArrival: true))
            {
                _failedTownMeetupPorter = _townMeetupPorter;
                _townMeetupPorter = null;
                _nextTownMeetupRouteSearch = 0;
                ResetRouteOrderState();
                bot.StopMovingOnPath();
                bot.StopMoving();
                SetStatus(bot, "Replanning town teleporter route", directive.SharedGoal,
                    $"The active AllRealmsTeleporter in region {bot.CurrentRegionID} has no usable approach; the meetup deadline remains authoritative");
                return true;
            }

            SetStatus(bot, $"Traveling to {directive.RendezvousName} teleporter", directive.SharedGoal,
                $"Approaching {_townMeetupPorter.Name} through the world; no catch-up transfer is used");
            return true;
        }

        bot.StopMovingOnPath();
        bot.StopMoving();
        if (AutonomousBotTownTravel.TryTeleportToTown(bot, _townMeetupPorter, directive.RendezvousRegion, directive.Rendezvous,
                includeBorderHub: rvr))
        {
            _townMeetupPorter = null;
            _nextTownMeetupRouteSearch = 0;
            ResetRouteOrderState();
            SetStatus(bot, $"Arrived in {directive.RendezvousName}", directive.SharedGoal,
                "Using the validated town destination; walking to the assigned formation slot next");
            return true;
        }

        SetStatus(bot, "Waiting to use town teleporter", directive.SharedGoal,
            "The porter transfer is rechecked for NPC proximity, life, and combat state before every attempt");
        return true;
    }
}
