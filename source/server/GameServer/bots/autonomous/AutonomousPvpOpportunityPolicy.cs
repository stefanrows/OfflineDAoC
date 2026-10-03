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
        partyLevel >= 10 && (!isFrontier || partyLevel >= 35) && !isSafe && reachable &&
        areaLevels?.Any(level => LevelsPreferred(partyLevel, level)) == true;

    public static int HunterPatrolWeight(int activeCampPopulation, bool nearOutdoorRoute) =>
        1 + Math.Min(4, Math.Max(0, activeCampPopulation)) * 2 + (nearOutdoorRoute ? 2 : 0);

    // Frontier scans must honor the same task and strength limits as outdoor hunts.
    // Actual self/group defense is checked separately by their caller.
    public static bool MayHunt(GameBot actor) => actor != null &&
        CanSeekOpportunity(AutonomousObjectiveAssignments.KindFor(actor), actor.Group?.MemberCount ?? 1) &&
        !AutonomousActivityScheduler.IsPveBlocked(actor.PersistentRecord, WorldSimulationClock.UtcNow) &&
        !actor.IsRecoveryResting && !AutonomousSiegeMarch.IsMarching(actor) && AutonomousBotGroupCoordinator.CanInitiateNewPull(actor);

    public static bool SuitableOpponent(GameBot actor, GameLiving target)
    {
        if (AutonomousSiegeMarch.IsMarching(actor)) return false;
        GameLiving identity = PvpCombatant.Resolve(target);
        if (identity == null) return false;
        if (AutonomousPlayerBehavior.TypeOf(actor.PersistentRecord) == AutonomousPlayerType.Hunter &&
            identity.EffectiveLevel > actor.Level + PreferredLevelDifference) return false;
        // Departure truce: two same-realm groups leaving the same border hub
        // move out first and do not jump each other at its edge (P3).
        if (AutonomousHubDeparture.TruceApplies(actor, target, WorldSimulationClock.UtcNow)) return false;
        // Observe before engaging (P2): the leader's add or straggler call
        // commits the group to that party or target; while it still watches,
        // nobody opens a fight on his own.
        if (AutonomousRvrObserve.IsCommittedTarget(actor, identity)) return true;
        if (AutonomousRvrObserve.IsObserving(actor)) return false;
        (int ownCount, int ownLevel) = VisibleParty(actor);
        (int count, int level) = VisibleParty(identity);
        // An RvR group's doctrine and its leader's nerve decide the odds it
        // takes; everyone else keeps the plain "not visibly stronger" rule.
        return AutonomousRvrDoctrineRuntime.AcceptsFight(actor, ownCount, ownLevel, count, level, identity) ??
            !IsVisiblyStronger(ownCount, ownLevel, count, level);
    }

    public static (int Count, int AverageLevel) VisibleParty(GameLiving living)
    {
        GameLiving identity = PvpCombatant.Resolve(living);
        if (identity == null)
            return (0, 0);
        GameLiving[] members = identity.Group?.GetMembersInTheGroup()
            .Where(member => member?.IsAlive == true && member.CurrentRegionID == identity.CurrentRegionID &&
                member.IsWithinRadius(identity, 2000))
            .ToArray() ?? [identity];
        return members.Length == 0
            ? (1, identity.EffectiveLevel)
            : (members.Length, (int)Math.Round(members.Average(member => member.EffectiveLevel)));
    }

    public static GameLiving Select(GameBot actor, IEnumerable<GameLiving> candidates,
        Func<GameLiving, GameLiving, bool> visible, bool retaliation = false) =>
        Suitable(actor, candidates, visible, retaliation).FirstOrDefault();

    /// <summary>Every opponent <see cref="Select"/> would accept, best first.</summary>
    public static GameLiving[] Suitable(GameBot actor, IEnumerable<GameLiving> candidates,
        Func<GameLiving, GameLiving, bool> visible, bool retaliation = false)
    {
        // A bot fresh from release (or a zone change) does not open a fight
        // while its PvP immunity lasts; the server rules would refuse it anyway.
        if (actor == null || candidates == null || PvpCombatant.IsSafeArea(actor) ||
            PvpCombatant.IsInvulnerableToAttack(actor))
            return [];
        DateTime nowUtc = WorldSimulationClock.UtcNow;
        GameLiving[] visibleCandidates = candidates
            .Where(target => target != null && target != actor && target.IsAlive &&
                target.ObjectState == GameObject.eObjectState.Active && !target.IsStealthed &&
                target.CurrentRegionID == actor.CurrentRegionID &&
                PvpCombatant.IsPlayerShaped(target) && !PvpCombatant.IsSafeArea(target) &&
                GameServer.ServerRules.IsAllowedToAttack(actor, target, true) && visible(actor, target))
            .ToArray();
        HashSet<GameLiving> grudges = visibleCandidates
            .Where(target => AutonomousGuildGrudgeMemory.IsActiveTarget(actor, target, nowUtc))
            .ToHashSet();
        // A grudge only moves a target to the front of the queue. It still has
        // to pass the level window, grey and "stronger party" checks.
        return visibleCandidates
            .Where(target => retaliation || SuitableOpponent(actor, target))
            .Where(target => retaliation || AutonomousRvrTargetPolicy.ShouldEngageGrey(actor, target))
            .OrderByDescending(target => grudges.Contains(target))
            .ThenBy(target => LevelsPreferred(actor.Level, PvpCombatant.Resolve(target)?.Level ?? target.EffectiveLevel) ? 0 : 1)
            .ThenBy(actor.GetDistanceTo)
            .ToArray();
    }
}
