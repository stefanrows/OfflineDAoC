using System;
using System.Linq;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// The standby of a reserved open-BG bot while the Gothwaite PvP mode is on: it logs in at its own border hub and parks
/// there between open battleground deployments, so neither the mode driver nor the world controller runs for it.
/// The predicates are pure; the live helpers only read.
/// </summary>
public static class GothwaitePvpOpenBgStandby
{
    /// <summary>Mode on, reserved, outside every battleground (not registered, not travelling, not inside), not in a player's group.</summary>
    public static bool IsStandby(bool modeActive, bool reserved, bool inAnyBattleground, bool playerLed) =>
        modeActive && reserved && !inAnyBattleground && !playerLed;

    public static bool IsStandby(GameLiving living) =>
        living is GameBot bot && GothwaitePvpOpenBgReserve.IsOpenBgReserved(bot) &&
        IsStandby(GothwaitePvpMode.IsActive, true, AutonomousBattlegroundParticipation.IsInAnyBattleground(bot), bot.IsPlayerLedGroup);

    /// <summary>The open BG roster accepts only an RvR objective as candidate.</summary>
    public static bool NeedsRvrStamp(string objectiveKind) =>
        !string.Equals(objectiveKind, "RvR", StringComparison.Ordinal);

    /// <summary>A bot-only group that is not an open-BG party would make the roster reject the bot as in_group.</summary>
    public static bool LeavesGroup(bool groupIsBotOnly, bool isOpenBgParty) => groupIsBotOnly && !isOpenBgParty;

    public static bool NeedsPark(bool atOwnHub, bool alive, bool inCombat, bool returningAfterRelease) =>
        !atOwnHub && alive && !inCombat && !returningAfterRelease;

    /// <summary>Exactly the fields the RvR branch of <c>AutonomousObjectiveAssignments.Assign</c> writes (plus the standby's own id).</summary>
    public static void Stamp(OfflineWorldBotRecord record, long botId, DateTime utcNow, TimeSpan tenure)
    {
        record.ObjectiveKind = "RvR";
        record.ObjectiveAssignmentId = $"gothpvp-openbg-{botId}";
        record.ObjectiveAssignedUtc = utcNow.ToString("O");
        record.ObjectiveExpiresUtc = utcNow.Add(tenure).ToString("O");
        record.ObjectivePveMode = string.Empty;
        record.ObjectivePveKills = 0;
        record.ObjectivePveKillTarget = 0;
        record.ObjectiveRvrEligibleUtc = string.Empty;
    }

    /// <summary>Within this distance of the hub release point a bot counts as parked (the spread is 240).</summary>
    public const int HubRadius = 1000;

    /// <summary>The own-realm border hub release point and the bot's spread point there (read-only).</summary>
    public static bool TryHub(GameBot bot, out ushort region, out Point3D anchor, out Point3D spread)
    {
        spread = null;
        if (!AutonomousRvrStaging.TrySafeRvrRelease(bot.Realm, true, out region, out anchor))
            return false;
        spread = BotReleaseBindPoints.SpreadSafe(region, anchor, bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID);
        return true;
    }

    public static bool IsAtHub(GameBot bot, ushort region, Point3D anchor) =>
        bot.CurrentRegionID == region && anchor != null &&
        bot.GetDistanceTo(anchor) <= HubRadius;

    public static bool GroupIsBotOnly(Group group) => group != null && group.GetPlayersInTheGroup().Count == 0 &&
        group.GetMembersInTheGroup().All(member => member is GameBot);
}
