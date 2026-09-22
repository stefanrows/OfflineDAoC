using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>Shared Camlann checks for local PvP hunts and PvE-party opportunities.</summary>
public static class AutonomousPvpOpportunityPolicy
{
    public const int PreferredLevelDifference = 5;

    public static bool LevelsPreferred(int actorLevel, int targetLevel) =>
        Math.Abs(actorLevel - targetLevel) <= PreferredLevelDifference;

    public static bool IsVisiblyStronger(int actorCount, int actorAverageLevel,
        int targetCount, int targetAverageLevel) =>
        targetCount > actorCount || targetAverageLevel > actorAverageLevel + PreferredLevelDifference;

    public static bool ShouldInitiate(int actorCount, int actorAverageLevel,
        int targetCount, int targetAverageLevel, bool retaliation) =>
        retaliation || !IsVisiblyStronger(actorCount, actorAverageLevel, targetCount, targetAverageLevel);

    public static bool IsLocalHuntArea(int partyLevel, IEnumerable<int> areaLevels,
        bool isDungeon, bool isFrontier, bool isSafe, bool reachable) =>
        partyLevel < 20 && !isDungeon && !isFrontier && !isSafe && reachable &&
        areaLevels?.Any(level => LevelsPreferred(partyLevel, level)) == true;

    public static (int Count, int AverageLevel) VisibleParty(GameLiving living)
    {
        GameLiving identity = PvpCombatant.Resolve(living);
        if (identity == null)
            return (0, 0);
        GameLiving[] members = identity.Group?.GetMembersInTheGroup()
            .Where(member => member?.IsAlive == true && member.CurrentRegionID == identity.CurrentRegionID)
            .ToArray() ?? [identity];
        return (Math.Max(1, members.Length), (int)Math.Round(members.Average(member => member.EffectiveLevel)));
    }

    public static GameLiving Select(GameBot actor, IEnumerable<GameLiving> candidates,
        Func<GameLiving, GameLiving, bool> visible, bool retaliation = false)
    {
        if (actor == null || candidates == null || PvpCombatant.IsSafeArea(actor))
            return null;
        (int ownCount, int ownLevel) = VisibleParty(actor);
        return candidates
            .Where(target => target != null && target != actor && target.IsAlive &&
                target.ObjectState == GameObject.eObjectState.Active &&
                target.CurrentRegionID == actor.CurrentRegionID &&
                PvpCombatant.IsPlayerShaped(target) && !PvpCombatant.IsSafeArea(target) &&
                (retaliation || AutonomousRvrTargetPolicy.ShouldEngageGrey(actor, target)) &&
                GameServer.ServerRules.IsAllowedToAttack(actor, target, true) && visible(actor, target))
            .Where(target => retaliation || !Stronger(target))
            .OrderBy(target => LevelsPreferred(actor.Level, PvpCombatant.Resolve(target)?.Level ?? target.EffectiveLevel) ? 0 : 1)
            .ThenBy(actor.GetDistanceTo)
            .FirstOrDefault();

        bool Stronger(GameLiving target)
        {
            (int count, int level) = VisibleParty(target);
            return IsVisiblyStronger(ownCount, ownLevel, count, level);
        }
    }
}
