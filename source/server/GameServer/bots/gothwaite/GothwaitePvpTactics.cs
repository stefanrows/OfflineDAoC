using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS;

/// <summary>One member of a group as the leader choice sees it: its id, its leader rank (lower leads) and whether it may lead now.</summary>
public readonly record struct GothwaitePvpLeaderCandidate(long BotId, int Rank, bool Eligible);

/// <summary>
/// The player tactics of the Gothwaite PvP mode as pure functions (docs/GOTHWAITE_PVP_MODE.md section 13, stage 6). They are read from
/// how DAoC players fought in 2002–2004 group RvR and on the PvP servers (the sources are listed in the plan): the main assist is a
/// tank, groups pick fights they can win and leave fights they are losing, they regroup and rest after a fight instead of walking
/// home, they walk in a loose column with the healers behind the leader, a solo stealther hunts lone or wounded targets and leaves
/// after one kill, and nearby fights draw adds. Nothing here touches a game object; the driver and the former read the game and act
/// on the answers. Every rule applies only to mode bots.
/// </summary>
public static class GothwaitePvpTactics
{
    // ------------------------------------------------------------ main assist

    /// <summary>Leader ranks (lower leads): a guard tank, then any melee, an archer, a caster or a stealther, and a healer last.</summary>
    public const int RankTank = 0;
    public const int RankMelee = 1;
    public const int RankArcher = 2;
    public const int RankCaster = 3;
    public const int RankStealther = 3;
    public const int RankHealer = 5;

    /// <summary>
    /// The rank of a class as the group's leader and main assist. Players put a tank up front and let everyone assist it (the
    /// stick leader "is the only one who is running"); healers and casters never walk first. A healer leads only when nobody
    /// else can. A stealther is a poor leader for a visible group, but a stealth pack is all stealthers, so it ranks as a caster.
    /// </summary>
    public static int LeaderRank(RvrClassTraits traits)
    {
        if (traits.HasFlag(RvrClassTraits.Healer))
            return RankHealer;
        if (traits.HasFlag(RvrClassTraits.Stealth) && !traits.HasFlag(RvrClassTraits.Archer))
            return RankStealther;
        if (traits.HasFlag(RvrClassTraits.Melee) && traits.HasFlag(RvrClassTraits.Guard))
            return RankTank;
        if (traits.HasFlag(RvrClassTraits.Melee))
            return RankMelee;
        if (traits.HasFlag(RvrClassTraits.Archer))
            return RankArcher;
        return RankCaster;
    }

    /// <summary>The index of the eligible candidate with the lowest rank; ties keep the first (join order). -1 when none is eligible.</summary>
    public static int ChooseLeader(IReadOnlyList<GothwaitePvpLeaderCandidate> candidates)
    {
        int best = -1;
        for (int index = 0; candidates != null && index < candidates.Count; index++)
        {
            if (!candidates[index].Eligible)
                continue;
            if (best < 0 || candidates[index].Rank < candidates[best].Rank)
                best = index;
        }
        return best;
    }

    // ------------------------------------------------------------ fight appetite

    /// <summary>A target's party is counted within this distance of it (alive group members).</summary>
    public const int PartyRadius = 2_000;
    /// <summary>A refused fight is not weighed again against the same enemy party for this long (the dare roll is stable per minute).</summary>
    public const int AppetiteBucketMilliseconds = 60_000;

    /// <summary>
    /// Whether a group (or a solo) opens on an enemy party of <paramref name="enemyCount"/>. Players took fights they could win: a
    /// small-man hunted solos, duos and other small-mans and avoided full groups; an eight fought anything up to its size and a
    /// little more. Strength is own count times the doctrine's appetite times a level factor; a solo always takes a solo of its
    /// con; an eager group now and then dares a bigger one (<paramref name="dareRoll"/> in [0,1)).
    /// </summary>
    public static bool TakesFight(double appetite, int ownCount, int ownLevel, int enemyCount, int enemyLevel, double dareRoll)
    {
        if (enemyCount <= 0)
            return true;
        if (!(appetite > 0))
            appetite = 1;
        double levelFactor = Math.Clamp(1 + (ownLevel - enemyLevel) * 0.06, 0.5, 1.4);
        double strength = Math.Max(1, ownCount) * appetite * levelFactor;
        if (enemyCount <= Math.Max(1, strength) + 0.5)
            return true;
        double dare = Math.Clamp((appetite - 0.9) * 0.4, 0, 0.2);
        return enemyCount <= strength * 1.6 && dareRoll < dare;
    }

    /// <summary>A roll in [0,1) that is the same for the same pair and bucket, so a refused fight is not re-rolled every scan.</summary>
    public static double StableRoll(long first, long second, long bucket)
    {
        ulong hash = unchecked((ulong)first * 2654435761UL ^ (ulong)second * 40503UL ^ (ulong)bucket * 2246822519UL);
        hash ^= hash >> 29;
        hash *= 0x9E3779B97F4A7C15UL;
        hash ^= hash >> 32;
        return (hash % 1_000_000UL) / 1_000_000d;
    }

    // ------------------------------------------------------------ retreat

    /// <summary>A leader weighs the fight this often.</summary>
    public const int RetreatCheckMilliseconds = 3_000;
    /// <summary>A retreat runs this long (inclusive bounds, drawn per retreat), then the group decides between home and a new leg.</summary>
    public const int RetreatMinMilliseconds = 20_000;
    public const int RetreatMaxMilliseconds = 35_000;
    /// <summary>Enemies within this distance of the leader count against the group.</summary>
    public const int RetreatEnemyRange = 1_800;
    /// <summary>A solo flees below this health while its enemy is above <see cref="SoloFleeEnemyHealthPercent"/>.</summary>
    public const int SoloFleeHealthPercent = 40;
    public const int SoloFleeEnemyHealthPercent = 60;
    /// <summary>A solo with this many player-shaped attackers on it flees (it "got added on").</summary>
    public const int SoloFleeAttackers = 2;
    /// <summary>After a retreat a bot with at least this health takes a new leg; below it, it goes home.</summary>
    public const int RetreatResumeHealthPercent = 60;

    /// <summary>
    /// Whether a group leaves its fight now: the doctrine's retreat rule (a dead healer, half the group down, outnumbered; the
    /// leader's risk tolerance and a stay roll keep it a decision). Players "backed off when the healer died" and "retreated at 2:1".
    /// </summary>
    public static bool GroupRetreats(RvrDoctrine doctrine, int alive, int formed, int aliveHealers, int formedHealers, int enemiesNear,
        double stayRoll) =>
        AutonomousRvrDoctrine.ShouldRetreat(doctrine, alive, formed, aliveHealers, formedHealers, enemiesNear, stayRoll);

    /// <summary>The main pressure behind a group retreat, for the log.</summary>
    public static string GroupRetreatReason(int alive, int formed, int aliveHealers, int formedHealers, int enemiesNear) =>
        AutonomousRvrDoctrine.RetreatReason(alive, formed, aliveHealers, formedHealers, enemiesNear);

    /// <summary>
    /// Whether a solo breaks off: two or more player-shaped attackers on it, or low health against a healthy enemy. A bot with a
    /// high risk tolerance stays in more often (<paramref name="roll"/> in [0,1)): at risk 15 it almost always runs, at 85 it runs
    /// in about one case in three.
    /// </summary>
    public static bool SoloFlees(int healthPercent, int enemyHealthPercent, int attackers, int riskTolerance, double roll)
    {
        bool pressed = attackers >= SoloFleeAttackers ||
            healthPercent < SoloFleeHealthPercent && enemyHealthPercent > SoloFleeEnemyHealthPercent;
        if (!pressed)
            return false;
        double stay = (Math.Clamp(riskTolerance, 15, 85) - 15) / 70d * 0.65;
        return roll >= stay;
    }

    /// <summary>The reason a solo flees, for the log.</summary>
    public static string SoloFleeReason(int attackers) => attackers >= SoloFleeAttackers ? "added_on" : "low_health";

    /// <summary>A retreat length, drawn from the random source.</summary>
    public static int RetreatMilliseconds(Random random) => random.Next(RetreatMinMilliseconds, RetreatMaxMilliseconds + 1);

    /// <summary>After the retreat: a new outbound leg when the bot is healthy enough, else home.</summary>
    public static bool ResumesAfterRetreat(int healthPercent) => healthPercent >= RetreatResumeHealthPercent;

    // ------------------------------------------------------------ regroup

    /// <summary>A regroup after a fight lasts at most this long.</summary>
    public const int RegroupMaxMilliseconds = 45_000;
    /// <summary>Members within this distance of the leader have regrouped.</summary>
    public const int RegroupRange = 600;
    /// <summary>The regroup ends early once every living member is near and the lowest member is at these levels.</summary>
    public const int RegroupHealthPercent = 70;
    public const int RegroupPowerPercent = 50;

    /// <summary>
    /// The regroup is over: the time ran out, or every living member is near the leader and the lowest health (and power, when a
    /// member uses power) are back up. Players rested and rezzed after a fight before taking the next one.
    /// </summary>
    public static bool RegroupDone(long now, long since, int aliveOthers, int nearOthers, int lowestHealthPercent, int lowestPowerPercent,
        bool anyPowerUser)
    {
        if (now - since >= RegroupMaxMilliseconds)
            return true;
        return nearOthers >= aliveOthers && lowestHealthPercent >= RegroupHealthPercent &&
            (!anyPowerUser || lowestPowerPercent >= RegroupPowerPercent);
    }

    /// <summary>
    /// While a group regroups, only a lost leader or a wiped group sends a member home; low health and low power wait for the
    /// regroup to end, since the bot is resting right there.
    /// </summary>
    public static string ReturnWhileRegrouping(string reason) =>
        reason == GothwaitePvpArenaRules.ReasonLeaderLost || reason == GothwaitePvpArenaRules.ReasonWiped ? reason : null;

    // ------------------------------------------------------------ formation

    /// <summary>The leader's facing as a unit vector on the ground plane (DAoC heading: 4096 units per turn, 0 faces +Y).</summary>
    public static Vector2 Forward(ushort heading)
    {
        double angle = heading * Point2D.HEADING_TO_RADIAN;
        return new Vector2(-(float)Math.Sin(angle), (float)Math.Cos(angle));
    }

    /// <summary>
    /// The travel slot of a follower (by its place in the marching order: guards and melee first, healers in the middle, casters
    /// and archers behind, stealthers wide) relative to the leader. A clump doctrine (a bomb group, a pickup) keeps the leader's
    /// own spot. Players kept their healers behind the tanks and "350 locs" apart, so one area mez did not catch them all.
    /// </summary>
    public static Vector3 FormationPoint(RvrTravelShape shape, int slot, int count, Vector3 leader, Vector2 forward, long memberKey) =>
        AutonomousRvrDoctrineGeometry.TravelSlot(shape, slot, count, leader, forward, memberKey);

    /// <summary>The marching rank of a class (the slot order).</summary>
    public static int MarchRank(RvrClassTraits traits) => AutonomousRvrDoctrineGeometry.MarchRank(traits);

    // ------------------------------------------------------------ chase

    /// <summary>A follower whose target is farther than this from its leader is cut off from its healers and drops the chase.</summary>
    public const int FollowerChaseRange = 1_500;
    /// <summary>A leader or solo drops a chase this far from where the fight began ("don't chase into the adds").</summary>
    public const int ChaseFromStartRange = 2_500;
    /// <summary>A target below this health is finished whatever the distance.</summary>
    public const int FinishHealthPercent = 25;

    /// <summary>True when a follower is overextended: its target is beyond <see cref="FollowerChaseRange"/> from the leader and not about to die.</summary>
    public static bool Overextended(double targetToLeader, int targetHealthPercent) =>
        targetToLeader > FollowerChaseRange && targetHealthPercent >= FinishHealthPercent;

    /// <summary>True when a leader or solo has chased too far from the start of its fight and the target is not about to die.</summary>
    public static bool ChasedTooFar(double distanceFromStart, int targetHealthPercent) =>
        distanceFromStart > ChaseFromStartRange && targetHealthPercent >= FinishHealthPercent;

    // ------------------------------------------------------------ adds

    /// <summary>A roaming leader hears a fight within this distance.</summary>
    public const int AddRange = 5_000;
    /// <summary>A fight older than this no longer draws anyone.</summary>
    public const int AddMaxAgeMilliseconds = 45_000;
    /// <summary>The leader listens this often.</summary>
    public const int AddCheckMilliseconds = 10_000;
    /// <summary>A fight closer than this is the group's own (its last kill) and draws nobody.</summary>
    public const int AddOwnRange = 1_000;
    /// <summary>A formation slot the navmesh refused is not proved again for this long; the follower walks to the leader's spot meanwhile.</summary>
    public const int SlotRetryMilliseconds = 5_000;

    /// <summary>
    /// Whether a group walks to a nearby fight it has heard (<paramref name="roll"/> in [0,1)): about a third of the time at
    /// aggression 15, two thirds at 85. The PvP servers were "crowded with adds", and pickup groups "followed bigger fights".
    /// </summary>
    public static bool JoinsFight(int aggression, double roll) => roll < 0.25 + Math.Clamp(aggression, 0, 100) / 200d;

    // ------------------------------------------------------------ stealthers

    /// <summary>A hunting stealther waits this long at a point ("sit beside the road"), inclusive bounds.</summary>
    public const int AmbushDwellMinMilliseconds = 30_000;
    public const int AmbushDwellMaxMilliseconds = 90_000;
    /// <summary>After a fight a stealther breaks away for this long (an outbound leg with no opening).</summary>
    public const int BreakMilliseconds = 20_000;
    /// <summary>A hunting stealther opens on a wounded target below this health whatever its party.</summary>
    public const int StealthWoundedPercent = 50;

    /// <summary>A class that hunts stealthed: assassins and archers (Minstrels are visible group classes here).</summary>
    public static bool IsStealther(RvrClassTraits traits) => traits.HasFlag(RvrClassTraits.Stealth);

    /// <summary>A doctrine whose members hunt stealthed: a solo assassin, a stealth pack, a gank squad or an archer group.</summary>
    public static bool HuntsStealthed(RvrDoctrineKind kind) => kind is RvrDoctrineKind.SoloAssassin or RvrDoctrineKind.StealthPack
        or RvrDoctrineKind.GankSquad or RvrDoctrineKind.ArcherGroup;

    /// <summary>
    /// Whether a hunting stealther opens on a target: a lone one, a soft one (caster or healer) in at most a duo, or a wounded one.
    /// Thidranki infiltrators took solo hunters and casters, left targets "with a group around them", and waited for the drop.
    /// </summary>
    public static bool StealtherMayOpen(int partySize, int healthPercent, bool soft) =>
        partySize <= 1 || soft && partySize <= 2 || healthPercent < StealthWoundedPercent;

    /// <summary>An ambush dwell length, drawn from the random source.</summary>
    public static int AmbushDwellMilliseconds(Random random) => random.Next(AmbushDwellMinMilliseconds, AmbushDwellMaxMilliseconds + 1);
}
