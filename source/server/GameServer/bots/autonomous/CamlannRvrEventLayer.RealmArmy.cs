using System;
using System.Collections.Generic;
using System.Linq;
using static DOL.GS.AutonomousRvrEventLayer;

namespace DOL.GS;

/// <summary>
/// Realm armies (package R4b, docs/RVR_ARMIES.md). In the RvR world the event layer fights per realm instead of
/// per guild: the realm opens a siege, every warband of the realm is its army, the realm defends its keeps, its
/// attack plan decides how many sieges it runs and whether it joins another realm's fight, and its reserve hunts
/// invaders at home. Every helper here answers the Camlann rule unchanged outside the RvR world.
/// </summary>
public static partial class CamlannRvrEventLayer
{
    private static readonly DOL.Logging.Logger RealmArmyLog = DOL.Logging.LoggerManager.Create(typeof(AutonomousRealmArmyPolicy));

    /// <summary>
    /// RvR world: a keep of the force's realm under siege is the force's to defend, even though its own keeps are
    /// never in the force's objective list (they are never attack targets). Camlann: null (owners answer through
    /// the guild recall, as before).
    /// </summary>
    private static LiveObjective RealmDefenseTargetLocked(Force force, ActiveEvent active) =>
        WorldMode.IsRvr && force != null && RealmTravel.IsPlayable(force.Realm) && force.Realm == active.DefenderRealm &&
        !active.DefenseReaction ? active.Target : null;

    /// <summary>The third realm's cap: half the attacker cap in the RvR world, the full cap in Camlann.</summary>
    private static int ThirdRealmCapacity(ActiveEvent active) =>
        WorldMode.IsRvr ? AutonomousRealmArmyPolicy.ThirdRealmCapacity(Capacity(active)) : Capacity(active);

    /// <summary>RvR world: whether this force's realm contests another realm's siege now (its attack plan).</summary>
    private static bool RealmMayContestLocked(Force force, ActiveEvent active, long nowTick) =>
        AutonomousRealmArmyPolicy.MayContestAsThirdRealm(AutonomousRealmArmyPlans.PlanFor(force.Realm, nowTick),
            force.MemberCount, active.AttackerReachedKeep);

    /// <summary>An opportunist realm joins a keep fight that is on without a further roll.</summary>
    private static bool OpportunistJoinsLocked(Force force, ActiveEvent active, long nowTick) =>
        active.AttackerReachedKeep && AutonomousRealmArmyPlans.PlanFor(force.Realm, nowTick) == OpenBgAttackPlan.Opportunist;

    /// <summary>
    /// The next keep (or relic temple) to besiege. Camlann: a uniform draw among the keeps not chosen lately.
    /// RvR world: never an own-realm keep; neutral and enemy keeps weighted by the tunables, keeps in the home
    /// frontier first (<see cref="AutonomousRealmArmyPolicy.KeepWeight(eRealm, eRealm, ushort)"/>).
    /// </summary>
    private static LiveObjective ChooseKeepTarget(Force force, IEnumerable<LiveObjective> candidates, ISet<string> used)
    {
        if (!WorldMode.IsRvr) return ChooseVariedTarget(candidates, used);
        LiveObjective[] eligible = candidates.Where(candidate =>
            AutonomousRealmArmyPolicy.KeepWeight(force.Realm, candidate.OwningRealm, candidate.RegionId) > 0).ToArray();
        if (eligible.Length == 0) return null;
        if (eligible.All(candidate => used.Contains(candidate.Id))) used.Clear();
        return AutonomousRealmArmyPolicy.ChooseWeighted(eligible,
            candidate => AutonomousRealmArmyPolicy.KeepWeight(force.Realm, candidate.OwningRealm, candidate.RegionId),
            candidate => used.Contains(candidate.Id), Random.Shared.NextDouble());
    }

    /// <summary>RvR world: enemies standing in the force's own frontier are hunted first. Camlann: no preference.</summary>
    private static int InvaderRank(Force force, LiveObjective objective) =>
        WorldMode.IsRvr && objective.RegionId == AutonomousRealmArmyPolicy.HomeFrontierRegion(force.Realm) ? 0 : 1;

    /// <summary>
    /// RvR world: the realm's roaming reserve (<see cref="Force.RoamingReserve"/> only) stays home while invaders
    /// are counted there (<see cref="AutonomousRealmArmyCensus"/>, at least
    /// <see cref="AutonomousRealmArmyProperties.RVR_ARMY_INVADER_MIN"/>): it hunts a sighted invader, else patrols
    /// a clearing of its own frontier. Without invaders it roams as before (null), so enemy frontiers keep their
    /// roaming groups.
    /// </summary>
    private static Plan HomeReservePlan(Force force, IReadOnlyCollection<LiveObjective> objectives)
    {
        ushort home = AutonomousRealmArmyPolicy.HomeFrontierRegion(force.Realm);
        if (home == 0 || !AutonomousRealmArmyPolicy.InvadersAtHome(AutonomousRealmArmyCensus.Current(force.Realm),
                AutonomousRealmArmyProperties.RVR_ARMY_INVADER_MIN)) return null;
        LiveObjective invader = objectives.Where(objective => objective.Kind == Intent.HuntEnemy && !objective.IsRelicCarrier &&
                objective.EnemyCount > 0 && objective.RegionId == home && objective.OwningRealm != force.Realm)
            .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
        if (invader != null)
            return ToPlan(Intent.HuntEnemy, invader, false, "Home reserve: hunting invaders in our own frontier.");
        LiveObjective patrol = objectives.Where(objective => objective.Kind == Intent.Roam && objective.RegionId == home)
            .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
        return patrol == null ? null
            : ToPlan(Intent.Roam, patrol, false, "Home reserve: patrolling our own frontier while the armies are out.");
    }

    /// <summary>
    /// The realms that neither attack nor defend. Camlann and an RvR siege of an enemy keep: the one other realm.
    /// RvR siege of a neutral (Wardens) keep: both other realms, so both hear the news and may contest it.
    /// </summary>
    private static IEnumerable<eRealm> BystanderRealms(ActiveEvent active)
    {
        if (!WorldMode.IsRvr || RealmTravel.IsPlayable(active.DefenderRealm))
            return [OtherRealm(active)];
        return RealmTravel.PlayableRealms.Where(realm => realm != active.AttackerRealm);
    }

    /// <summary>RvR world: one line per realm siege, with the plan that allowed it.</summary>
    private static void NoteRealmSiegeOpenedLocked(ActiveEvent active, Force force)
    {
        if (!WorldMode.IsRvr || !RealmArmyLog.IsInfoEnabled) return;
        string running = string.Join(",", RealmTravel.PlayableRealms.Select(realm =>
            $"{GlobalConstants.RealmToName(realm)}:{Events.Values.Count(entry => !entry.DefenseReaction && entry.AttackerRealm == realm)}"));
        RealmArmyLog.Info($"RVR_REALM_SIEGE_OPENED target={active.TargetId} name=\"{active.Target.Name}\" " +
            $"realm={GlobalConstants.RealmToName(active.AttackerRealm)} defender={GlobalConstants.RealmToName(active.DefenderRealm)} " +
            $"plan={AutonomousRealmArmyPlans.PlanFor(active.AttackerRealm, active.CreatedTick)} opener={force.GroupId} " +
            $"openerGuild=\"{force.GuildName}\" running={running}");
    }
}
