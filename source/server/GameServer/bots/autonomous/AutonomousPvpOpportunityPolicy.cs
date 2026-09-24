using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>Shared Camlann checks for local PvP hunts and PvE-party opportunities.</summary>
public static class AutonomousPvpOpportunityPolicy
{
    public const int PreferredLevelDifference = 5;

    public static bool CanSeekOpportunity(eAutonomousObjectiveKind objective, int partySize) =>
        objective == eAutonomousObjectiveKind.RvR;

    public static bool CanUseMatchmakingCamp(bool isDungeon, bool isFrontier,
        ushort actorRegion, ushort campRegion) =>
        !isDungeon && !isFrontier && actorRegion == campRegion;

    public static int ChooseRoamIndex(int count, int previousIndex, int roll)
    {
        if (count <= 1) return 0;
        int index = Math.Abs(roll % (count - (previousIndex >= 0 && previousIndex < count ? 1 : 0)));
        return previousIndex >= 0 && index >= previousIndex ? index + 1 : index;
    }

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

    public static bool IsHunterHuntArea(int partyLevel, IEnumerable<int> areaLevels,
        bool isDungeon, bool isFrontier, bool isSafe, bool reachable) =>
        partyLevel >= 10 && !isDungeon && (!isFrontier || partyLevel >= 35) && !isSafe && reachable &&
        areaLevels?.Any(level => LevelsPreferred(partyLevel, level)) == true;

    public static int HunterPatrolWeight(int activeCampPopulation, bool nearOutdoorRoute) =>
        1 + Math.Min(4, Math.Max(0, activeCampPopulation)) * 2 + (nearOutdoorRoute ? 2 : 0);

    public static (int Count, int AverageLevel) VisibleParty(GameLiving living)
    {
        GameLiving identity = PvpCombatant.Resolve(living);
        if (identity == null)
            return (0, 0);
        GameLiving[] members = identity.Group?.GetMembersInTheGroup()
            .Where(member => member?.IsAlive == true && member.CurrentRegionID == identity.CurrentRegionID &&
                member.IsWithinRadius(identity, 2000))
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
                (retaliation || AutonomousPlayerBehavior.TypeOf(actor.PersistentRecord) != AutonomousPlayerType.Hunter ||
                    (PvpCombatant.Resolve(target)?.EffectiveLevel ?? target.EffectiveLevel) <= actor.Level + PreferredLevelDifference) &&
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
