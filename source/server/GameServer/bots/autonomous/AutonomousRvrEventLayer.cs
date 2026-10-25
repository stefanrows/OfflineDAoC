using System;
using System.Collections.Generic;
using System.Numerics;
using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>
/// The RvR event layer every caller uses: keep sieges, relic events and the defense against them, for autonomous
/// world bots. Two implementations sit behind it (docs/RVR_PORT_MAP.md, port session S1):
/// <list type="bullet">
/// <item>the RvR world (save marker <c>Realm-1</c>, <see cref="WorldMode.IsRvr"/>) runs the original upstream layer,
/// <see cref="RealmRvrEventLayer"/> (shadowofze/OfflineDAoC 0.38c: one siege roll a minute, realm armies muster at
/// their portal outpost and march at 108/170 or 85 %, arrival quorum, repel, retake, defense rally);</item>
/// <item>the Camlann world keeps the fork's guild layer, <see cref="CamlannRvrEventLayer"/>, unchanged.</item>
/// </list>
/// The world is latched at startup and never changes at run time, so every call answers from one implementation.
/// The records and enums below are shared by both. A call only one implementation knows answers a neutral default
/// in the other world (no event, no muster, false, null).
/// </summary>
public static class AutonomousRvrEventLayer
{
    // ---- Shared records (union of both implementations; new fields carry defaults) -------------------------------

    public enum Intent { Roam, HuntEnemy, AssaultKeep, AssaultRelicKeep, DefendEvent, ClaimKeep }

    public sealed record Force(string GroupId, eRealm Realm, int MemberCount, int AverageLevel, int HealerCount,
        bool CanSupplySiege = true, bool RoamingReserve = false, long[] MemberIds = null, int MinimumMemberLevel = 50,
        string GuildName = null, bool CampaignEligible = false, bool SingleGuild = true, bool CanClaim = true);

    /// <param name="Claimable">Camlann: a guild could claim this keep after the lord falls
    /// (see <see cref="AutonomousRvrKeepPolicy.IsClaimableKeep(DOL.GS.Keeps.AbstractGameKeep)"/>).
    /// Automatic assaults open only on claimable keeps; forced or player-driven
    /// events on any keep remain joinable.</param>
    public sealed record LiveObjective(string Id, string Name, Intent Kind, eRealm OwningRealm, ushort RegionId,
        int X, int Y, int Z, bool IsRelicKeep, int EnemyCount, int FriendlyCount, int GuardStrength, int ClosedDoors,
        bool IsRelicCarrier = false, bool IsPortalKeep = false, bool UnderAttack = false, string OwningGuild = null,
        bool Claimable = true, bool AwaitingClaim = false);

    public sealed record Plan(Intent Intent, string TargetId, string Name, ushort RegionId, int X, int Y, int Z,
        bool IsSharedEvent, string Reason);

    /// <param name="MusterRemainingMilliseconds">RvR world: time to the march order; 0 = the army is marching;
    /// -1 = no muster stage (always -1 in Camlann).</param>
    public sealed record BattleNotice(string TargetId, string Kind, eRealm Attacker, eRealm Defender,
        int Attackers, int Defenders, int ThirdRealm, int CapPerRealm, long RemainingMilliseconds,
        bool BattleStarted = false, int PresentAttackers = 0, int PresentDefenders = 0, int PresentThirdRealm = 0,
        long MusterRemainingMilliseconds = -1);

    public enum RallySide { Attacker, Defender, Contester }

    /// <param name="HomeMuster">RvR world: the force gathers at its muster (attackers at the portal outpost, the
    /// defense rally at a nearby friendly keep) instead of the siege posts.</param>
    public sealed record RallyOrder(string TargetId, eRealm Attacker, eRealm Defender, int[] Slots, long RemainingMilliseconds,
        RallySide Side = RallySide.Attacker, bool HomeMuster = false);

    /// <summary>Camlann guild army order (fork).</summary>
    public sealed record GuildArmyOrder(string TargetId, long Generation, Vector3? Camp,
        bool Released, bool HoldColumn, bool Failed, int Present, int Required, int Parties, long Remaining);

    /// <summary>RvR world: the size of a player-started defense (upstream goal 11).</summary>
    public enum DefenseScale { Small, Standard, Large, Massive }

    /// <summary>RvR world: siege recruits who joined alone, for the coordinator to merge into parties (upstream).</summary>
    public sealed record SoloSiegeRoster(string EventId, eRealm Realm, bool Marching, long[] MemberIds);

    private static bool Rvr => WorldMode.IsRvr;

    // ---- Constants. Names both layers share answer the Camlann value; RvR values are on RealmRvrEventLayer. -------

    public const long BattleLifetimeMilliseconds = CamlannRvrEventLayer.BattleLifetimeMilliseconds;
    public const long AttendanceFreshnessMilliseconds = CamlannRvrEventLayer.AttendanceFreshnessMilliseconds;
    public const long StragglerTimeoutMilliseconds = CamlannRvrEventLayer.StragglerTimeoutMilliseconds;
    public const long TargetCooldownMilliseconds = CamlannRvrEventLayer.TargetCooldownMilliseconds;
    public const int OrdinaryAssaultCap = CamlannRvrEventLayer.OrdinaryAssaultCap;
    public const int RelicAssaultCap = CamlannRvrEventLayer.RelicAssaultCap;
    public const int RvrRelicAssaultCap = CamlannRvrEventLayer.RvrRelicAssaultCap;
    public const int RelicCarrierRealmCap = CamlannRvrEventLayer.RelicCarrierRealmCap;
    public const int SiegeGateAverageLevel = CamlannRvrEventLayer.SiegeGateAverageLevel;
    public const int SiegeGateMinimumLevel = CamlannRvrEventLayer.SiegeGateMinimumLevel;
    public const long IdleBattleMilliseconds = CamlannRvrEventLayer.IdleBattleMilliseconds;
    public const int MarchProgressUnits = CamlannRvrEventLayer.MarchProgressUnits;
    public const int AttackerPresenceRadius = CamlannRvrEventLayer.AttackerPresenceRadius;
    public const long AbsentAttackerMilliseconds = CamlannRvrEventLayer.AbsentAttackerMilliseconds;
    public const long AbandonedTargetMilliseconds = CamlannRvrEventLayer.AbandonedTargetMilliseconds;
    public const long DefenseResponseMilliseconds = CamlannRvrEventLayer.DefenseResponseMilliseconds;
    public const long DefenseQuietMilliseconds = CamlannRvrEventLayer.DefenseQuietMilliseconds;
    public const int MaxConcurrentSieges = CamlannRvrEventLayer.MaxConcurrentSieges;
    public const int MaxSiegesPerSide = CamlannRvrEventLayer.MaxSiegesPerSide;

    // ---- Both worlds ------------------------------------------------------------------------------------------------

    public static bool ForceStart(LiveObjective target, eRealm attacker, long now, out string reason) => Rvr
        ? RealmRvrEventLayer.ForceStart(target, attacker, now, out reason)
        : CamlannRvrEventLayer.ForceStart(target, attacker, now, out reason);

    public static bool ResetCooldown(string id) =>
        Rvr ? RealmRvrEventLayer.ResetCooldown(id) : CamlannRvrEventLayer.ResetCooldown(id);

    public static long CooldownRemaining(string id, long now) =>
        Rvr ? RealmRvrEventLayer.CooldownRemaining(id, now) : CamlannRvrEventLayer.CooldownRemaining(id, now);

    public static Dictionary<string, string> ForceTargets() =>
        Rvr ? RealmRvrEventLayer.ForceTargets() : CamlannRvrEventLayer.ForceTargets();

    public static LiveObjective ChooseVariedTarget(IEnumerable<LiveObjective> candidates, ISet<string> used, Random random = null) => Rvr
        ? RealmRvrEventLayer.ChooseVariedTarget(candidates, used, random)
        : CamlannRvrEventLayer.ChooseVariedTarget(candidates, used, random);

    public static double MajorAssaultWeight(int memberCount, int averageLevel) => Rvr
        ? RealmRvrEventLayer.MajorAssaultWeight(memberCount, averageLevel)
        : CamlannRvrEventLayer.MajorAssaultWeight(memberCount, averageLevel);

    public static bool ShouldJoinActiveEvent(Force force, int existingParticipants, int cap,
        bool relicImportance, bool defending, double roll) => Rvr
        ? RealmRvrEventLayer.ShouldJoinActiveEvent(force, existingParticipants, cap, relicImportance, defending, roll)
        : CamlannRvrEventLayer.ShouldJoinActiveEvent(force, existingParticipants, cap, relicImportance, defending, roll);

    public static Intent ChooseIntent(Force force, bool hasEnemy, bool hasOrdinaryKeep, bool hasRelicKeep, double roll) => Rvr
        ? RealmRvrEventLayer.ChooseIntent(force, hasEnemy, hasOrdinaryKeep, hasRelicKeep, roll)
        : CamlannRvrEventLayer.ChooseIntent(force, hasEnemy, hasOrdinaryKeep, hasRelicKeep, roll);

    public static Plan ChooseOrJoin(Force force, IReadOnlyCollection<LiveObjective> objectives, long nowTick, double roll) => Rvr
        ? RealmRvrEventLayer.ChooseOrJoin(force, objectives, nowTick, roll)
        : CamlannRvrEventLayer.ChooseOrJoin(force, objectives, nowTick, roll);

    public static BattleNotice[] Snapshot() => Rvr ? RealmRvrEventLayer.Snapshot() : CamlannRvrEventLayer.Snapshot();

    public static RallyOrder GetRallyOrder(string forceId, eRealm realm, long nowTick) => Rvr
        ? RealmRvrEventLayer.GetRallyOrder(forceId, realm, nowTick)
        : CamlannRvrEventLayer.GetRallyOrder(forceId, realm, nowTick);

    public static bool IsRallying(string forceId, long nowTick) =>
        Rvr ? RealmRvrEventLayer.IsRallying(forceId, nowTick) : CamlannRvrEventLayer.IsRallying(forceId, nowTick);

    public static bool IsBattleForce(string forceId, long nowTick) =>
        Rvr ? RealmRvrEventLayer.IsBattleForce(forceId, nowTick) : CamlannRvrEventLayer.IsBattleForce(forceId, nowTick);

    public static Plan KeepPlan(string forceId, eRealm realm, long nowTick) => Rvr
        ? RealmRvrEventLayer.KeepPlan(forceId, realm, nowTick)
        : CamlannRvrEventLayer.KeepPlan(forceId, realm, nowTick);

    public static bool ProtectsParticipantFromInactivity(GameBot bot, long nowTick) => Rvr
        ? RealmRvrEventLayer.ProtectsParticipantFromInactivity(bot, nowTick)
        : CamlannRvrEventLayer.ProtectsParticipantFromInactivity(bot, nowTick);

    public static bool IsHoldingSiegeDefense(bool defender, bool sameRegion, float distanceSquared, int heightDifference) => Rvr
        ? RealmRvrEventLayer.IsHoldingSiegeDefense(defender, sameRegion, distanceSquared, heightDifference)
        : CamlannRvrEventLayer.IsHoldingSiegeDefense(defender, sameRegion, distanceSquared, heightDifference);

    public static void ReportAttendance(string targetId, string forceId, eRealm realm, long botId, bool inPosition, long nowTick,
        GameBot bot = null, Vector3 position = default)
    {
        if (Rvr) RealmRvrEventLayer.ReportAttendance(targetId, forceId, realm, botId, inPosition, nowTick, bot, position);
        else CamlannRvrEventLayer.ReportAttendance(targetId, forceId, realm, botId, inPosition, nowTick, bot, position);
    }

    public static bool TryConsumeRelease(string forceId, long nowTick, out string reason) => Rvr
        ? RealmRvrEventLayer.TryConsumeRelease(forceId, nowTick, out reason)
        : CamlannRvrEventLayer.TryConsumeRelease(forceId, nowTick, out reason);

    public static void ReportTravel(string targetId, string forceId, long memberId, double distance, bool arrived, long nowTick, GameBot bot = null)
    {
        if (Rvr) RealmRvrEventLayer.ReportTravel(targetId, forceId, memberId, distance, arrived, nowTick, bot);
        else CamlannRvrEventLayer.ReportTravel(targetId, forceId, memberId, distance, arrived, nowTick, bot);
    }

    public static void RemoveForce(string forceId)
    {
        if (Rvr) RealmRvrEventLayer.RemoveForce(forceId);
        else CamlannRvrEventLayer.RemoveForce(forceId);
    }

    public static long CarrierRemainingMilliseconds(string targetId, long nowTick) => Rvr
        ? RealmRvrEventLayer.CarrierRemainingMilliseconds(targetId, nowTick)
        : CamlannRvrEventLayer.CarrierRemainingMilliseconds(targetId, nowTick);

    public static bool IsForceCommitted(string forceId, long nowTick) => Rvr
        ? RealmRvrEventLayer.IsForceCommitted(forceId, nowTick)
        : CamlannRvrEventLayer.IsForceCommitted(forceId, nowTick);

    public static bool IsPlayerDefenseResponse(string targetId, long nowTick) => Rvr
        ? RealmRvrEventLayer.IsPlayerDefenseResponse(targetId, nowTick)
        : CamlannRvrEventLayer.IsPlayerDefenseResponse(targetId, nowTick);

    public static bool IsTargetActive(string targetId, long nowTick) => Rvr
        ? RealmRvrEventLayer.IsTargetActive(targetId, nowTick)
        : CamlannRvrEventLayer.IsTargetActive(targetId, nowTick);

    public static void EndTarget(string targetId, long nowTick, eRealm winner = eRealm.None)
    {
        if (Rvr) RealmRvrEventLayer.EndTarget(targetId, nowTick, winner);
        else CamlannRvrEventLayer.EndTarget(targetId, nowTick, winner);
    }

    public static void TransferToRelicCarrier(string keepId, string carrierId, long nowTick, LiveObjective carrierAnchor = null)
    {
        if (Rvr) RealmRvrEventLayer.TransferToRelicCarrier(keepId, carrierId, nowTick, carrierAnchor);
        else CamlannRvrEventLayer.TransferToRelicCarrier(keepId, carrierId, nowTick, carrierAnchor);
    }

    public static string RelicCarrierTargetId(GameRelic relic) =>
        Rvr ? RealmRvrEventLayer.RelicCarrierTargetId(relic) : CamlannRvrEventLayer.RelicCarrierTargetId(relic);

    /// <summary>Keep damage from a door or guard hit. <paramref name="hit"/> names the part for the Camlann siege log.</summary>
    public static void ObserveKeepAttack(AbstractGameKeep keep, GameObject source, string hit = null)
    {
        if (Rvr) RealmRvrEventLayer.ObserveKeepAttack(keep, source);
        else CamlannRvrEventLayer.ObserveKeepAttack(keep, source, hit);
    }

    public static GamePlayer PlayerInstigator(GameObject source) =>
        Rvr ? RealmRvrEventLayer.PlayerInstigator(source) : CamlannRvrEventLayer.PlayerInstigator(source);

    public static int ResponseCap(bool defending) =>
        Rvr ? RealmRvrEventLayer.ResponseCap(defending) : CamlannRvrEventLayer.ResponseCap(defending);

    public static bool ResponseReserve(long id) =>
        Rvr ? RealmRvrEventLayer.ResponseReserve(id) : CamlannRvrEventLayer.ResponseReserve(id);

    public static bool CanRedirectDefense(eRealm previousDefender, eRealm nextDefender,
        string previousPlayer, string nextPlayer, long previousPressure, long nextPressure) => Rvr
        ? RealmRvrEventLayer.CanRedirectDefense(previousDefender, nextDefender, previousPlayer, nextPlayer, previousPressure, nextPressure)
        : CamlannRvrEventLayer.CanRedirectDefense(previousDefender, nextDefender, previousPlayer, nextPlayer, previousPressure, nextPressure);

    public static bool BeginDefenseResponse(LiveObjective target, eRealm attacker, string playerAccount, long now) => Rvr
        ? RealmRvrEventLayer.BeginDefenseResponse(target, attacker, playerAccount, now)
        : CamlannRvrEventLayer.BeginDefenseResponse(target, attacker, playerAccount, now);

    public static void PulseDefense(long now)
    {
        if (Rvr) RealmRvrEventLayer.PulseDefense(now);
        else CamlannRvrEventLayer.PulseDefense(now);
    }

    // ---- RvR world only (upstream); Camlann answers the neutral default --------------------------------------------

    /// <summary>A player's keep attack sized to the force that started it (RvR world; Camlann takes the 4-argument rule).</summary>
    public static bool BeginDefenseResponse(LiveObjective target, eRealm attacker, string playerAccount, long now, DefenseScale scale) => Rvr
        ? RealmRvrEventLayer.BeginDefenseResponse(target, attacker, playerAccount, now, scale)
        : CamlannRvrEventLayer.BeginDefenseResponse(target, attacker, playerAccount, now);

    public static int ResponseCap(bool defending, DefenseScale scale) => RealmRvrEventLayer.ResponseCap(defending, scale);
    public static DefenseScale ScaleOf(int forceSize) => RealmRvrEventLayer.ScaleOf(forceSize);
    public static int ForceSize(GamePlayer player) => RealmRvrEventLayer.ForceSize(player);
    public static string RallyLine(DefenseScale scale) => RealmRvrEventLayer.RallyLine(scale);
    public static int MarchThreshold(bool relic, int assigned) => RealmRvrEventLayer.MarchThreshold(relic, assigned);
    public static int ArrivalQuorum(int marchedAttackers) => RealmRvrEventLayer.ArrivalQuorum(marchedAttackers);
    public static int DefenseWaveSize(int called) => RealmRvrEventLayer.DefenseWaveSize(called);
    public static double SiegeStartChancePerMinute(int active, double idleMinutes) =>
        RealmRvrEventLayer.SiegeStartChancePerMinute(active, idleMinutes);
    public static string Place(string objectiveName) => RealmRvrEventLayer.Place(objectiveName);
    public static GameBot MainBodyLeader(GameBot[] marchers, LiveObjective target) => RealmRvrEventLayer.MainBodyLeader(marchers, target);

    public static void ReportArmyArrived(string targetId, long nowTick)
    {
        if (Rvr) RealmRvrEventLayer.ReportArmyArrived(targetId, nowTick);
    }

    public static GameBot MarchLeader(string targetId) => Rvr ? RealmRvrEventLayer.MarchLeader(targetId) : null;

    public static void ReportMarchLeading(string targetId, GameBot bot)
    {
        if (Rvr) RealmRvrEventLayer.ReportMarchLeading(targetId, bot);
    }

    public static GameBot[] ArmyMembers(string targetId) => Rvr ? RealmRvrEventLayer.ArmyMembers(targetId) : [];

    public static void ReportMusterAttendance(string targetId, string forceId, long botId, bool inPosition, long nowTick,
        GameBot bot, Vector3 position)
    {
        if (Rvr) RealmRvrEventLayer.ReportMusterAttendance(targetId, forceId, botId, inPosition, nowTick, bot, position);
    }

    public static bool TrySiegeRelease(string forceId, eRealm realm, long nowTick, out ushort region, out Point3D point)
    {
        if (Rvr) return RealmRvrEventLayer.TrySiegeRelease(forceId, realm, nowTick, out region, out point);
        region = 0;
        point = null;
        return false;
    }

    public static bool IsEventForce(string forceId, long nowTick) => Rvr && RealmRvrEventLayer.IsEventForce(forceId, nowTick);

    public static List<SoloSiegeRoster> SoloSiegeRosters(long nowTick) => Rvr ? RealmRvrEventLayer.SoloSiegeRosters(nowTick) : [];

    public static bool MergeSoloForces(string eventId, eRealm realm, long[] memberIds, string partyForceId) =>
        Rvr && RealmRvrEventLayer.MergeSoloForces(eventId, realm, memberIds, partyForceId);

    public static long LastKeepPressure(AbstractGameKeep keep) => Rvr ? RealmRvrEventLayer.LastKeepPressure(keep) : 0;

    // ---- Camlann world only (fork); the RvR world answers the neutral default --------------------------------------

    public static bool IsIdleBattle(bool battleStarted, bool defenseReaction, bool playerLed, long lastActivityTick, long nowTick) =>
        CamlannRvrEventLayer.IsIdleBattle(battleStarted, defenseReaction, playerLed, lastActivityTick, nowTick);

    public static bool IsAbandonedByAttackers(bool battleStarted, bool defenseReaction, bool playerLed,
        long lastAttackerPresenceTick, long nowTick) =>
        CamlannRvrEventLayer.IsAbandonedByAttackers(battleStarted, defenseReaction, playerLed, lastAttackerPresenceTick, nowTick);

    public static bool IsWholeGuildWarband(Force force) => CamlannRvrEventLayer.IsWholeGuildWarband(force);
    public static bool IsWholeGuildWarband(WorldModelKind world, Force force) => CamlannRvrEventLayer.IsWholeGuildWarband(world, force);
    public static eRealm ContesterRealm(eRealm attacker, eRealm defender) => CamlannRvrEventLayer.ContesterRealm(attacker, defender);
    public static string SiegeSideKey(string guildName, eRealm realm) => CamlannRvrEventLayer.SiegeSideKey(guildName, realm);
    public static string SiegeSideKey(WorldModelKind world, string guildName, eRealm realm) =>
        CamlannRvrEventLayer.SiegeSideKey(world, guildName, realm);
    public static bool MayOpenSiege(string sideKey, IEnumerable<string> runningSiegeSides, bool carrierEventActive) =>
        CamlannRvrEventLayer.MayOpenSiege(sideKey, runningSiegeSides, carrierEventActive);
    public static bool MayOpenSiege(string sideKey, IEnumerable<string> runningSiegeSides, bool carrierEventActive, int maxPerSide) =>
        CamlannRvrEventLayer.MayOpenSiege(sideKey, runningSiegeSides, carrierEventActive, maxPerSide);
    public static string RealmArmyMusterCall(string musterName, string keepName) => CamlannRvrEventLayer.RealmArmyMusterCall(musterName, keepName);
    public static bool LeadsItsParty(GameBot leader) => CamlannRvrEventLayer.LeadsItsParty(leader);

    public static void ReportBattleActivity(string targetId, long nowTick, string forceId = null)
    {
        if (!Rvr) CamlannRvrEventLayer.ReportBattleActivity(targetId, nowTick, forceId);
    }

    public static void ReportMarch(string targetId, string forceId, long memberId, ushort region, Vector3 position,
        bool inCombat, long nowTick)
    {
        if (!Rvr) CamlannRvrEventLayer.ReportMarch(targetId, forceId, memberId, region, position, inCombat, nowTick);
    }

    public static void NoteKeepHit(string targetId, string what, GameObject source, long nowTick)
    {
        if (!Rvr) CamlannRvrEventLayer.NoteKeepHit(targetId, what, source, nowTick);
    }

    public static bool RestartMarch(string forceId, long nowTick) => !Rvr && CamlannRvrEventLayer.RestartMarch(forceId, nowTick);

    public static void AbandonTarget(string forceId, string targetId, long nowTick)
    {
        if (!Rvr) CamlannRvrEventLayer.AbandonTarget(forceId, targetId, nowTick);
    }

    public static bool IsAbandoned(string forceId, string targetId, long nowTick) =>
        !Rvr && CamlannRvrEventLayer.IsAbandoned(forceId, targetId, nowTick);

    public static bool RenewClaimPlan(string forceId, string targetId, long now) =>
        !Rvr && CamlannRvrEventLayer.RenewClaimPlan(forceId, targetId, now);

    public static void ReleaseClaimPlan(string forceId, string targetId)
    {
        if (!Rvr) CamlannRvrEventLayer.ReleaseClaimPlan(forceId, targetId);
    }

    public static void WithdrawForKeepDefense(string forceId, long memberId, bool retireForce)
    {
        if (!Rvr) CamlannRvrEventLayer.WithdrawForKeepDefense(forceId, memberId, retireForce);
    }

    public static GuildArmyOrder GuildArmyPlan(string forceId, GameBot bot, long now) =>
        Rvr ? null : CamlannRvrEventLayer.GuildArmyPlan(forceId, bot, now);

    public static Vector3? SetGuildArmyCamp(string forceId, GameBot bot, long generation, Vector3 point, long now,
        string musterName = null) =>
        Rvr ? null : CamlannRvrEventLayer.SetGuildArmyCamp(forceId, bot, generation, point, now, musterName);

    public static void ReportGuildArmy(string forceId, GameBot leader, GameBot[] members,
        long[] equippedOperators, GameLiving[] sightings, long now)
    {
        if (!Rvr) CamlannRvrEventLayer.ReportGuildArmy(forceId, leader, members, equippedOperators, sightings, now);
    }

    public static AutonomousRvrSiegeMuster.Phase MusterPhaseOf(string forceId) =>
        Rvr ? AutonomousRvrSiegeMuster.Phase.None : CamlannRvrEventLayer.MusterPhaseOf(forceId);

    public static AutonomousRvrSiegeMuster.Phase ReportMuster(string forceId, int alive, int present, int assigned,
        bool skip, long nowTick, bool suppliesReady = true) => Rvr
        ? AutonomousRvrSiegeMuster.Phase.None
        : CamlannRvrEventLayer.ReportMuster(forceId, alive, present, assigned, skip, nowTick, suppliesReady);

    public static long? MusterTimeRemaining(string forceId, long nowTick) =>
        Rvr ? null : CamlannRvrEventLayer.MusterTimeRemaining(forceId, nowTick);

    public static (int Mustering, int Marching) MusterCounts(string targetId) =>
        Rvr ? (0, 0) : CamlannRvrEventLayer.MusterCounts(targetId);

    public static void NoteKeepRouteFailure(string targetId, long nowTick)
    {
        if (!Rvr) CamlannRvrEventLayer.NoteKeepRouteFailure(targetId, nowTick);
    }

    public static bool IsKeepRouteBlocked(string targetId, long nowTick) =>
        !Rvr && CamlannRvrEventLayer.IsKeepRouteBlocked(targetId, nowTick);
}
