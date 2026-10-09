using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS;

/// <summary>
/// In-memory coordination for already-assigned RvR groups. A committed force
/// remains attached through travel, deaths, and regrouping until the objective
/// resolves or its four-hour battle window expires. This layer neither writes
/// objective tenure nor changes keeps, relics, damage, or
/// rewards.  It only tells existing groups where real world combat can happen.
/// </summary>
public static partial class AutonomousRvrEventLayer
{
    public const long BattleLifetimeMilliseconds = 4 * 60 * 60_000L;
    public const long AttendanceFreshnessMilliseconds = 15_000;
    public const long StragglerTimeoutMilliseconds = 8 * 60_000L;
    public const long TargetCooldownMilliseconds = CamlannPopulationTuning.TargetCooldownMilliseconds;
    public const int OrdinaryAssaultCap = 128;
    public const int RelicAssaultCap = 192;
    public const int RelicCarrierRealmCap = 192;
    /// <summary>An automatic siege nobody reaches or fights at for this long is
    /// called off (the rvr-keep-75 siege of 2026-09-28 sat at 0/0/0 for hours
    /// and blocked every other assault meanwhile).</summary>
    public const long IdleBattleMilliseconds = 15 * 60_000L;

    public static bool IsIdleBattle(bool battleStarted, bool defenseReaction, bool playerLed, long lastActivityTick, long nowTick) =>
        battleStarted && !defenseReaction && !playerLed && nowTick - lastActivityTick >= IdleBattleMilliseconds;

    /// <summary>Live keep-combat probe; null-safe without a running world.</summary>
    internal static Func<string, bool> TargetInCombat = targetId =>
    {
        if (targetId == null || !targetId.StartsWith("rvr-keep-", StringComparison.Ordinal) ||
            !int.TryParse(targetId.AsSpan(9), out int keepId) || GameServer.Instance?.Configuration == null) return false;
        var keep = GameServer.KeepManager?.GetKeepByID(keepId);
        return keep?.CurrentRegion != null && keep.InCombat;
    };

    /// <summary>A participant reached the keep's assault approach or fought
    /// there. With a force id only an attacking or third-realm force counts;
    /// defenders holding their own walls do not keep an empty assault open.
    /// An attacking force at the approach (400-900 units from a gate) is also
    /// present at the keep.</summary>
    public static void ReportBattleActivity(string targetId, long nowTick, string forceId = null)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return;
        using (EnterSync())
            if (Events.TryGetValue(targetId, out var active) &&
                (forceId == null || active.Attackers.ContainsKey(forceId) || active.ThirdRealm.ContainsKey(forceId)))
            {
                active.LastActivityTick = Math.Max(active.LastActivityTick, nowTick);
                active.LastAttackerPresenceTick = Math.Max(active.LastAttackerPresenceTick, nowTick);
                active.AttackerReachedKeep = true;
            }
    }

    /// <summary>Minimum real approach progress that counts as a marching army.</summary>
    public const int MarchProgressUnits = 512;
    /// <summary>An attacker this close to the keep counts as present at the siege.</summary>
    public const int AttackerPresenceRadius = 3000;
    /// <summary>A started automatic siege that no attacking member has come within
    /// <see cref="AttackerPresenceRadius"/> of for this long is called off, even
    /// while members still report march progress somewhere. A 1.65 keep take
    /// lasted 10-40 minutes; a force that has not reached the walls after
    /// 45 minutes has given up. This frees its guild's siege slot.</summary>
    public const long AbsentAttackerMilliseconds = 45 * 60_000L;

    public static bool IsAbandonedByAttackers(bool battleStarted, bool defenseReaction, bool playerLed,
        long lastAttackerPresenceTick, long nowTick) =>
        battleStarted && !defenseReaction && !playerLed && nowTick - lastAttackerPresenceTick >= AbsentAttackerMilliseconds;

    /// <summary>
    /// A committed attacking (or third-realm) member reports where it is while
    /// the battle is on. Only real approach progress inside the keep's region
    /// keeps the siege alive: getting closer to the keep by
    /// <see cref="MarchProgressUnits"/>, or entering the keep's region (once per
    /// member and siege). Porting between other regions, pacing at a hub and
    /// dying/releasing in a loop earn nothing, so the porter carousel of
    /// 2026-09-28 can no longer keep an empty siege open. A member within
    /// <see cref="AttackerPresenceRadius"/> of the keep also refreshes the
    /// siege's attacker presence.
    /// </summary>
    public static void ReportMarch(string targetId, string forceId, long memberId, ushort region, Vector3 position,
        bool inCombat, long nowTick)
    {
        if (string.IsNullOrWhiteSpace(targetId) || string.IsNullOrWhiteSpace(forceId)) return;
        using (EnterSync())
        {
            if (!Events.TryGetValue(targetId, out var active) || !active.BattleStarted ||
                !(active.Attackers.ContainsKey(forceId) || active.ThirdRealm.ContainsKey(forceId))) return;
            bool sameRegion = region == active.Target.RegionId;
            double distance = sameRegion
                ? Vector2.Distance(new(position.X, position.Y), new(active.Target.X, active.Target.Y))
                : double.PositiveInfinity;
            if (distance <= AttackerPresenceRadius)
            {
                active.LastAttackerPresenceTick = Math.Max(active.LastAttackerPresenceTick, nowTick);
                active.AttackerReachedKeep = true;
                ClearKeepRouteBlockLocked(targetId);
            }
            if (!active.Travel.TryGetValue(memberId, out var previous))
            {
                active.Travel[memberId] = (forceId, nowTick, distance, position, region);
                return;
            }
            bool enteredTargetRegion = sameRegion && previous.Region != region && previous.Region != 0 &&
                active.RegionEntryCredited.Add(memberId);
            bool progress = enteredTargetRegion ||
                sameRegion && distance <= previous.BestDistance - MarchProgressUnits;
            if (!progress)
            {
                // Remember the region so a later entry into the keep's region is
                // recognized, without moving the best in-region distance.
                if (previous.Region != region)
                    active.Travel[memberId] = previous with { Position = position, Region = region };
                return;
            }
            active.Travel[memberId] = (forceId, nowTick, Math.Min(distance, previous.BestDistance), position, region);
            active.LastActivityTick = Math.Max(active.LastActivityTick, nowTick);
        }
    }

    public const long AbandonedTargetMilliseconds = 20 * 60_000L;
    private static readonly Dictionary<(string Force, string Target), long> AbandonedTargets = new();

    /// <summary>
    /// A force whose route to the objective failed repeatedly leaves the event
    /// and does not rejoin, reserve or reopen that objective for twenty
    /// minutes. Stored per force so every member of the warband honours it.
    /// </summary>
    public static void AbandonTarget(string forceId, string targetId, long nowTick)
    {
        if (string.IsNullOrWhiteSpace(forceId) || string.IsNullOrWhiteSpace(targetId)) return;
        using (EnterSync())
        {
            AbandonedTargets[(forceId, targetId)] = nowTick + AbandonedTargetMilliseconds;
            if (Events.TryGetValue(targetId, out var active))
            {
                active.Attackers.Remove(forceId);
                active.Defenders.Remove(forceId);
                active.ThirdRealm.Remove(forceId);
                active.Slots.Remove(forceId);
                active.Musters.Remove(forceId);
                foreach (long id in active.Travel.Where(pair => pair.Value.Force == forceId).Select(pair => pair.Key).ToArray())
                {
                    active.Travel.Remove(id);
                    active.RegionEntryCredited.Remove(id);
                }
                foreach (long id in active.Present.Where(pair => pair.Value.Force == forceId).Select(pair => pair.Key).ToArray())
                    active.Present.Remove(id);
            }
            if (CarrierEvents.TryGetValue(targetId, out var carrier))
                foreach (var realm in carrier.Participants.Values) realm.Remove(forceId);
        }
    }

    public static bool IsAbandoned(string forceId, string targetId, long nowTick)
    {
        using (EnterSync()) return IsAbandonedLocked(forceId, targetId, nowTick);
    }

    private static bool IsAbandonedLocked(string forceId, string targetId, long nowTick)
    {
        if (forceId == null || targetId == null || AbandonedTargets.Count == 0) return false;
        if (!AbandonedTargets.TryGetValue((forceId, targetId), out long until)) return false;
        if (until > nowTick) return true;
        AbandonedTargets.Remove((forceId, targetId));
        return false;
    }

    public enum Intent { Roam, HuntEnemy, AssaultKeep, AssaultRelicKeep, DefendEvent, ClaimKeep }

    public sealed record Force(string GroupId, eRealm Realm, int MemberCount, int AverageLevel, int HealerCount,
        bool CanSupplySiege = true, bool RoamingReserve = false, long[] MemberIds = null, int MinimumMemberLevel = 50,
        string GuildName = null, bool CampaignEligible = false, bool SingleGuild = true, bool CanClaim = true);
    /// <param name="Claimable">A guild could claim this keep after the lord falls
    /// (see <see cref="AutonomousRvrKeepPolicy.IsClaimableKeep(DOL.GS.Keeps.AbstractGameKeep)"/>).
    /// Automatic assaults open only on claimable keeps; forced or player-driven
    /// events on any keep remain joinable.</param>
    public sealed record LiveObjective(string Id, string Name, Intent Kind, eRealm OwningRealm, ushort RegionId,
        int X, int Y, int Z, bool IsRelicKeep, int EnemyCount, int FriendlyCount, int GuardStrength, int ClosedDoors,
        bool IsRelicCarrier = false, bool IsPortalKeep = false, bool UnderAttack = false, string OwningGuild = null,
        bool Claimable = true, bool AwaitingClaim = false);

    /// <summary>
    /// Only a whole warband of eight from one guild opens an autonomous keep
    /// siege. This is an AI commitment rule; the steward claim itself no longer
    /// has a group-size requirement. Smaller or mixed forces may
    /// reinforce an existing siege only on the opener's side (same guild); they
    /// do not contest a stranger's siege as a third party.
    /// </summary>
    public static bool IsWholeGuildWarband(Force force) =>
        force != null && force.MemberCount >= 8 && force.SingleGuild;
    public sealed record Plan(Intent Intent, string TargetId, string Name, ushort RegionId, int X, int Y, int Z,
        bool IsSharedEvent, string Reason);

    private sealed class ActiveEvent
    {
        public required string TargetId;
        public required LiveObjective Target;
        public required eRealm AttackerRealm;
        public required eRealm DefenderRealm;
        /// <summary>Camlann: the guild that opened the assault; its guild's warbands are the attackers.</summary>
        public string AttackerGuild;
        public required bool RelicKeep;
        public required long ExpiresTick;
        public bool BattleStarted;
        public bool PreparationNoticeSent;
        public long CreatedTick;
        public bool AttackObserved;
        public bool DefenseReaction;
        public long LastPressureTick;
        /// <summary>Last proof that anyone fought at or reached this keep.</summary>
        public long LastActivityTick;
        public string PlayerAccount;
        public readonly Dictionary<long, (string Force, eRealm Realm, long Tick, GameBot Bot, Vector3 Position, ushort Region)> Present = new();
        public readonly Dictionary<string, int[]> Slots = new(StringComparer.Ordinal);
        public readonly Dictionary<long, (string Force, long ProgressTick, double BestDistance, Vector3 Position, ushort Region)> Travel = new();
        /// <summary>Members already credited once for entering the keep's region.</summary>
        public readonly HashSet<long> RegionEntryCredited = new();
        /// <summary>Last time an attacking or third-realm member stood within
        /// <see cref="AttackerPresenceRadius"/> of the keep (or the battle start).</summary>
        public long LastAttackerPresenceTick;
        public bool AttackerReachedKeep;
        public readonly Dictionary<string, int> Attackers = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Defenders = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> ThirdRealm = new(StringComparer.Ordinal);
        /// <summary>Attacking warbands that muster before they march (bug 75).</summary>
        public readonly Dictionary<string, ForceMuster> Musters = new(StringComparer.Ordinal);
        public readonly Dictionary<string, GuildArmy> GuildArmies = new(StringComparer.Ordinal);
    }

    private static readonly object Sync = new();
    // Same monitor as a lock statement; the wait for it is timed as
    // BOT_THINK_PROFILE phase RvrEventLockWait (bug 56 round 2).
    private static BotThinkProfiler.MeasuredLock EnterSync() => BotThinkProfiler.Lock(Sync, BotThinkPhase.RvrEventLockWait);
    private static readonly Dictionary<string, ActiveEvent> Events = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (long ExpiresTick, Dictionary<eRealm, Dictionary<string, int>> Participants)> CarrierEvents = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, LiveObjective> CarrierTargets = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> Cooldowns = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> ReleasedForces = new(StringComparer.Ordinal);
    private static long _nextStragglerSweep;
    private static readonly HashSet<string> SelectedKeeps = new(StringComparer.Ordinal);
    private static readonly HashSet<string> SelectedRelics = new(StringComparer.Ordinal);

    public static bool ForceStart(LiveObjective target, eRealm attacker, long now, out string reason)
    {
        using (EnterSync())
        {
            if (target == null || target.IsPortalKeep || target.IsRelicCarrier ||
                attacker is not (eRealm.Albion or eRealm.Midgard or eRealm.Hibernia) ||
                target.OwningRealm == attacker && string.IsNullOrEmpty(target.OwningGuild))
            { reason = "Choose an enemy capturable keep and an attacking realm."; return false; }
            if (Events.ContainsKey(target.Id) || OnCooldown(target.Id, now))
            { reason = "This objective already has an event or an active cooldown."; return false; }
            if (!MayOpenSiegeLocked(null, attacker))
            { reason = "This realm already runs a forced siege, a relic event is active, or the server-wide siege cap is reached. Reinforce it before opening another."; return false; }
            Events[target.Id] = new ActiveEvent
            {
                TargetId = target.Id, Target = target, AttackerRealm = attacker,
                DefenderRealm = target.OwningRealm, RelicKeep = target.IsRelicKeep,
                CreatedTick = now, ExpiresTick = now + RealmEventPolicy.RecruitmentMilliseconds(Random.Shared.NextDouble())
            };
            RealmEventNotices.Queue(target.Id, attacker, $"Warbands are marching on {target.Name}. Join the assault as you arrive!");
            RealmEventRecords.Begin(target.Id, target.Name, target.IsRelicKeep ? "Relic keep" : "Keep", GlobalConstants.RealmToName(attacker), "Forced continuous siege; defender: " + GlobalConstants.RealmToName(target.OwningRealm));
            StartBattle(Events[target.Id], now, "forced assault opened; each attacking warband musters on its leader, then marches together");
            reason = "Assault opened. Reinforcements travel immediately; existing battles and roaming reserves are preserved.";
            return true;
        }
    }

    public static bool ResetCooldown(string id)
    {
        using (EnterSync())
        {
            if (Events.ContainsKey(id) || CarrierEvents.ContainsKey(id)) return false;
            Cooldowns.Remove(id);
            return true;
        }
    }

    public static long CooldownRemaining(string id, long now)
    {
        using (EnterSync()) return Math.Max(0, Cooldowns.GetValueOrDefault(id) - now);
    }

    public static Dictionary<string, string> ForceTargets()
    {
        using (EnterSync())
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var e in Events.Values)
                foreach (string force in e.Attackers.Keys.Concat(e.Defenders.Keys).Concat(e.ThirdRealm.Keys))
                    result[force] = e.TargetId;
            foreach (var e in CarrierEvents)
                foreach (string force in e.Value.Participants.Values.SelectMany(p => p.Keys)) result[force] = e.Key;
            return result;
        }
    }

    public static LiveObjective ChooseVariedTarget(IEnumerable<LiveObjective> candidates, ISet<string> used, Random random = null)
    {
        var eligible = candidates.ToArray();
        if (eligible.Length == 0) return null;
        var fresh = eligible.Where(candidate => !used.Contains(candidate.Id)).ToArray();
        if (fresh.Length == 0) { used.Clear(); fresh = eligible; }
        return fresh[(random ?? Random.Shared).Next(fresh.Length)];
    }

    public static double MajorAssaultWeight(int memberCount, int averageLevel)
    {
        double size = Math.Clamp((memberCount - 2d) / 6d, 0d, 1d);
        double level = Math.Clamp((averageLevel - 25d) / 25d, 0d, 1d);
        return Math.Clamp(size * 0.55d + level * 0.45d, 0d, 1d);
    }

    /// <summary>
    /// Event notices create an inclination, never conscription.  The ceiling
    /// deliberately leaves a roaming reserve even while an event has open
    /// capacity; a force that opened an assault is already recorded and is not
    /// evaluated by this gate again.
    /// </summary>
    public static bool ShouldJoinActiveEvent(Force force, int existingParticipants, int cap,
        bool relicImportance, bool defending, double roll)
    {
        if (force == null || cap <= 0 || existingParticipants + force.MemberCount > cap)
            return false;

        double forceStrength = MajorAssaultWeight(force.MemberCount, force.AverageLevel);
        double importance = relicImportance ? 0.33d : 0.19d;
        double defenderNeed = defending ? 0.12d : 0d;
        double attendance = Math.Clamp(existingParticipants / (double)cap, 0d, 1d);
        // Attendance raises the attraction so established rallies fill before
        // new forces scatter across sparse objectives. A roaming reserve still
        // remains outside the event system.
        double probability = Math.Clamp(0.18d + importance + defenderNeed + forceStrength * 0.20d + attendance * 0.36d,
            0.12d, 0.78d);
        return Math.Clamp(roll, 0d, 1d) < probability;
    }

    public static Intent ChooseIntent(Force force, bool hasEnemy, bool hasOrdinaryKeep, bool hasRelicKeep, double roll)
    {
        roll = Math.Clamp(roll, 0d, 1d);
        if (!IsWholeGuildWarband(force) || force.HealerCount < 1 || force.AverageLevel < 35 || !force.CanSupplySiege)
            return hasEnemy ? Intent.HuntEnemy : Intent.Roam;

        double weight = MajorAssaultWeight(force.MemberCount, force.AverageLevel);
        if (hasRelicKeep && force.MemberCount >= 6 && force.AverageLevel >= 45 && roll < weight * 0.15d)
            return Intent.AssaultRelicKeep;
        if (hasOrdinaryKeep && roll < weight)
            return Intent.AssaultKeep;
        return hasEnemy ? Intent.HuntEnemy : Intent.Roam;
    }

    public static Plan ChooseOrJoin(Force force, IReadOnlyCollection<LiveObjective> objectives, long nowTick, double roll)
    {
        using (EnterSync())
        {
            // Defensive boundary: protected hubs cannot become new events,
            // reinforcements or reserve patrols, even if a caller supplies one.
            // Objectives this force abandoned (repeated route failure) are
            // invisible to its join, reserve and new-target choices.
            if (AbandonedTargets.Count > 0)
                foreach (var stale in AbandonedTargets.Where(pair => pair.Value <= nowTick).Select(pair => pair.Key).ToArray())
                    AbandonedTargets.Remove(stale);
            Plan plan = ChooseCore(force, objectives?.Where(objective => !objective.IsPortalKeep &&
                (force == null || !IsAbandonedLocked(force.GroupId, objective.Id, nowTick))).ToArray(), nowTick, roll);
            if (force != null)
            {
                if (plan?.IsSharedEvent == true && force.MemberIds != null && Events.TryGetValue(plan.TargetId, out var joined))
                    foreach (long id in force.MemberIds)
                        joined.Travel.TryAdd(id, (force.GroupId, nowTick, double.PositiveInfinity, default, 0));
                // A warband occupies exactly one event reservation, including
                // when switching from the siege to a moving relic carrier.
                foreach (ActiveEvent active in Events.Values.Where(active => active.TargetId != plan?.TargetId || plan?.IsSharedEvent != true))
                {
                    active.Attackers.Remove(force.GroupId);
                    active.Defenders.Remove(force.GroupId);
                    active.ThirdRealm.Remove(force.GroupId);
                    active.Slots.Remove(force.GroupId);
                    active.Musters.Remove(force.GroupId);
                }
                foreach (var entry in CarrierEvents.Where(entry => entry.Key != plan?.TargetId || plan?.IsSharedEvent != true))
                    foreach (var realm in entry.Value.Participants.Values) realm.Remove(force.GroupId);
            }
            return plan;
        }
    }

    private static Plan ChooseCore(Force force, IReadOnlyCollection<LiveObjective> objectives, long nowTick, double roll)
    {
        if (force == null || string.IsNullOrWhiteSpace(force.GroupId) || force.Realm == eRealm.None || objectives == null)
            return null;

        using (EnterSync())
        {
            Cleanup(nowTick, objectives);
            if ((force.AverageLevel < 50 || force.MinimumMemberLevel < 50) && !force.CampaignEligible)
                return ReservePlan(force, objectives.Where(o => !o.IsRelicCarrier && o.Kind is not Intent.AssaultKeep and not Intent.AssaultRelicKeep).ToArray());
            if (ReleasedForces.ContainsKey(force.GroupId)) return null;

            Plan committed = CommittedPlan(force, objectives);
            if (committed != null)
                return committed;

            // A defeated keep needs a guild member at its steward, not a new
            // siege, full warband, ram or another random assault roll.
            Plan claim = ChooseClaimPlan(force, objectives, nowTick);
            if (claim != null) return claim;

            if (force.RoamingReserve)
                return ReservePlan(force, objectives);

            LiveObjective carrier = objectives.FirstOrDefault(objective => objective.IsRelicCarrier && !OnCooldown(objective.Id, nowTick));
            if (carrier != null)
            {
                if (TryJoinCarrier(carrier, force, nowTick, roll))
                    return ToPlan(force.Realm == carrier.OwningRealm ? Intent.DefendEvent : Intent.HuntEnemy, carrier, true,
                        force.Realm == carrier.OwningRealm
                            ? "Escorting the live allied relic carrier with capped support."
                            : "Contesting the live enemy relic carrier with capped three-realm participation.");
                return ReservePlan(force, objectives.Where(objective => objective.Id != carrier.Id).ToArray());
            }

            // Prefer completing the best-attended compatible rally instead of
            // spreading warbands across several nearly empty objectives.
            foreach (ActiveEvent active in Events.Values
                         .Where(active => !active.DefenseReaction)
                         .OrderByDescending(active => SameAttackingSide(force, active))
                         .ThenBy(active => active.BattleStarted)
                         .ThenByDescending(active => JoinPriority(active, force))
                         .ThenBy(active => active.TargetId, StringComparer.Ordinal))
            {
                LiveObjective target = objectives.FirstOrDefault(objective => objective.Id == active.TargetId);
                if (target == null)
                    continue;
                if (!IsLatestDefenseFocus(active)) continue;
                if (target.UnderAttack) active.AttackObserved = true;
                // The attacking commitment raises the alarm before combat. Defenders
                // form next; the third realm joins after both primary forces exist.
                // Camlann sides are guilds: the opener's guild attacks, the owner's
                // guild defends, every other guild contests on its own. The realm
                // byte only decides for realm-owned, guildless objectives.
                bool ownsTarget = OwnsObjective(force, target);
                bool attacksTarget = !ownsTarget && SameAttackingSide(force, active);
                bool defendsTarget = ownsTarget;
                if (!attacksTarget && !defendsTarget && !RealmEventPolicy.CanRecruitRealm(
                    defendsTarget, active.AttackObserved || active.BattleStarted,
                    active.Attackers.Values.Sum(), active.Defenders.Values.Sum(), Capacity(active)))
                    continue;
                if (attacksTarget)
                {
                    // Reinforcements of any size, but all of the opener's guild.
                    if (force.AverageLevel >= 35 && (force.SingleGuild || active.Attackers.ContainsKey(force.GroupId)) && (!active.BattleStarted || active.Attackers.ContainsKey(force.GroupId) ||
                        ShouldJoinActiveEvent(force, active.Attackers.Values.Sum(), Capacity(active), active.RelicKeep, false, roll)) &&
                        TryJoin(active.Attackers, force, Capacity(active)))
                        return ToPlan(Intent: active.RelicKeep ? Intent.AssaultRelicKeep : Intent.AssaultKeep, target, true,
                            $"Choosing to rally to the active {(active.RelicKeep ? "relic-keep" : "keep")} assault; participation is capped.");
                    continue;
                }
                if (defendsTarget)
                {
                    if (force.AverageLevel >= 35 && (!active.BattleStarted || active.Defenders.ContainsKey(force.GroupId) ||
                        ShouldJoinActiveEvent(force, active.Defenders.Values.Sum(), Capacity(active), active.RelicKeep, true, roll)) &&
                        TryJoin(active.Defenders, force, Capacity(active)))
                        return ToPlan(Intent.DefendEvent, target, true,
                            "Choosing to converge to defend the live opposing-realm assault; participation is capped.");
                    continue;
                }
                // The third realm contests the same event under its own cap;
                // it must never overwrite the first attacker's registration.
                // Only a whole one-guild warband contests a stranger's siege.
                if (!active.ThirdRealm.ContainsKey(force.GroupId) && !IsWholeGuildWarband(force)) continue;
                if (force.AverageLevel >= 35 && (!active.BattleStarted || active.ThirdRealm.ContainsKey(force.GroupId) ||
                    ShouldJoinActiveEvent(force, active.ThirdRealm.Values.Sum(), Capacity(active), active.RelicKeep, false, roll)) &&
                    TryJoin(active.ThirdRealm, force, Capacity(active)))
                    return ToPlan(active.RelicKeep ? Intent.AssaultRelicKeep : Intent.AssaultKeep,
                        target, true, "Third-realm force contesting the siege under its own participation cap.");
            }

            // One automatic siege per attacking guild (owner decision 2b): a
            // guild already besieging keeps its other warbands roaming unless
            // they joined above; other guilds open their own, up to the
            // server-wide safety cap (AutonomousRvrEventLayer.SiegeCap.cs).
            if (!MayOpenSiegeLocked(force.GuildName, force.Realm))
                return ReservePlan(force, objectives);

            bool hasEnemy = objectives.Any(objective => objective.Kind == Intent.HuntEnemy && objective.EnemyCount > 0);
            LiveObjective relic = ChooseVariedTarget(objectives.Where(objective => objective.Kind == Intent.AssaultRelicKeep && objective.IsRelicKeep && objective.Claimable &&
                                                               !OwnsObjective(force, objective) && !Events.ContainsKey(objective.Id) && !OnCooldown(objective.Id, nowTick) &&
                                                               !KeepRouteBlockedLocked(objective.Id, nowTick))
                , SelectedRelics);
            LiveObjective keep = ChooseVariedTarget(objectives.Where(objective => objective.Kind == Intent.AssaultKeep && !objective.IsRelicKeep && objective.Claimable && !objective.AwaitingClaim &&
                                                              !OwnsObjective(force, objective) && !Events.ContainsKey(objective.Id) && !OnCooldown(objective.Id, nowTick) &&
                                                              !KeepRouteBlockedLocked(objective.Id, nowTick))
                , SelectedKeeps);
            Intent intent = ChooseIntent(force, hasEnemy, keep != null, relic != null, roll);
            LiveObjective selected = intent switch
            {
                Intent.AssaultRelicKeep => relic,
                Intent.AssaultKeep => keep,
                Intent.HuntEnemy => objectives.Where(objective => objective.Kind == Intent.HuntEnemy && objective.EnemyCount > 0)
                    .OrderBy(objective => Math.Abs(objective.EnemyCount - force.MemberCount)).FirstOrDefault(),
                _ => objectives.Where(objective => objective.Kind == Intent.Roam).OrderBy(_ => Random.Shared.Next()).FirstOrDefault() ??
                     objectives.Where(objective => objective.Kind == Intent.HuntEnemy).OrderBy(_ => Random.Shared.Next()).FirstOrDefault(),
            };
            if (selected == null)
                return null;

            if (intent is Intent.AssaultKeep or Intent.AssaultRelicKeep)
            {
                ActiveEvent active = new()
                {
                    TargetId = selected.Id,
                    Target = selected,
                    AttackerRealm = force.Realm,
                    DefenderRealm = selected.OwningRealm,
                    AttackerGuild = force.GuildName,
                    RelicKeep = selected.IsRelicKeep,
                    ExpiresTick = nowTick + RealmEventPolicy.RecruitmentMilliseconds(Random.Shared.NextDouble()),
                    CreatedTick = nowTick,
                };
                active.Attackers[force.GroupId] = force.MemberCount;
                Events[selected.Id] = active;
                RealmEventRecords.Begin(selected.Id, selected.Name, selected.IsRelicKeep ? "Relic keep" : "Keep", GlobalConstants.RealmToName(force.Realm), "Automatic continuous siege; defender: " + GlobalConstants.RealmToName(selected.OwningRealm));
                StartBattle(active, nowTick, "automatic assault opened; each attacking warband musters on its leader, then marches together");
                RealmEventNotices.Queue(active.TargetId, force.Realm,
                    $"Warbands are marching on {selected.Name}. Reinforcements join the battle on arrival.");
                (selected.IsRelicKeep ? SelectedRelics : SelectedKeeps).Add(selected.Id);
                return ToPlan(intent, selected, true,
                    $"Opening a time-limited {(selected.IsRelicKeep ? "relic-keep" : "keep")} assault; same-realm warbands may rally and defenders may answer.");
            }

            return ToPlan(intent, selected, false,
                intent == Intent.HuntEnemy ? "Small or lower-level warband is hunting a live opposing force." :
                "No major assault selected; roaming a live frontier objective.");
        }
    }

    private static bool SameAttackingSide(Force force, ActiveEvent active) =>
        string.IsNullOrEmpty(active.AttackerGuild)
            ? force.Realm == active.AttackerRealm
            : string.Equals(force.GuildName, active.AttackerGuild, StringComparison.Ordinal);

    /// <summary>The bucket a force is registered in, or null. Lookups go by force, never by the asking bot's realm:
    /// Camlann crews mix realms, so a member's realm says nothing about its side.</summary>
    private static Dictionary<string, int> BucketOf(ActiveEvent active, string forceId) =>
        forceId == null ? null :
        active.Attackers.ContainsKey(forceId) ? active.Attackers :
        active.Defenders.ContainsKey(forceId) ? active.Defenders :
        active.ThirdRealm.ContainsKey(forceId) ? active.ThirdRealm : null;

    /// <summary>The side-realm a registered force counts under in attendance.</summary>
    private static eRealm SideRealm(ActiveEvent active, Dictionary<string, int> bucket) =>
        ReferenceEquals(bucket, active.Attackers) ? active.AttackerRealm :
        ReferenceEquals(bucket, active.Defenders) ? active.DefenderRealm : OtherRealm(active);

    public enum RallySide { Attacker, Defender, Contester }

    private static RallySide SideOf(ActiveEvent active, Dictionary<string, int> bucket) =>
        ReferenceEquals(bucket, active.Attackers) ? RallySide.Attacker :
        ReferenceEquals(bucket, active.Defenders) ? RallySide.Defender : RallySide.Contester;

    private static Plan ToPlan(Intent Intent, LiveObjective target, bool shared, string reason) =>
        new(Intent, target.Id, target.Name, target.RegionId, target.X, target.Y, target.Z, shared, reason);

    private static bool OwnsObjective(Force force, LiveObjective objective) =>
        objective != null && (string.IsNullOrEmpty(objective.OwningGuild)
            ? objective.OwningRealm != eRealm.None && objective.OwningRealm == force.Realm
            : string.Equals(objective.OwningGuild, force.GuildName, StringComparison.Ordinal));

    private static Plan ReservePlan(Force force, IReadOnlyCollection<LiveObjective> objectives)
    {
        LiveObjective target = objectives.Where(objective => objective.Kind == Intent.Roam)
            .OrderBy(_ => Random.Shared.Next()).FirstOrDefault()
            ?? objectives.Where(objective => objective.Kind == Intent.HuntEnemy && !objective.IsRelicCarrier && objective.EnemyCount > 0)
                .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
        return target == null ? null : ToPlan(target.Kind == Intent.HuntEnemy ? Intent.HuntEnemy : Intent.Roam, target, false,
            "Keeping a deliberate roaming reserve instead of automatically joining the active event.");
    }

    private static int Capacity(ActiveEvent active) => active.DefenseReaction ? 240 : active.RelicKeep ? RelicAssaultCap : OrdinaryAssaultCap;

    private static Plan CommittedPlan(Force force, IReadOnlyCollection<LiveObjective> objectives)
    {
        foreach (ActiveEvent active in Events.Values)
        {
            // A local route/candidate refresh is not an event cancellation.
            // Keep the committed destination while the normal route recovery
            // handles any temporary inability to reach it.
            LiveObjective target = objectives.FirstOrDefault(objective => objective.Id == active.TargetId) ?? active.Target;
            if (active.Attackers.ContainsKey(force.GroupId))
            {
                TryJoin(active.Attackers, force, Capacity(active));
                return ToPlan(active.RelicKeep ? Intent.AssaultRelicKeep : Intent.AssaultKeep, target, true,
                    "This force remains committed to the siege through defeat and regrouping.");
            }
            if (active.Defenders.ContainsKey(force.GroupId))
            {
                TryJoin(active.Defenders, force, Capacity(active));
                return ToPlan(Intent.DefendEvent, target, true,
                    "This force remains committed to defending the siege through defeat and regrouping.");
            }
            if (active.ThirdRealm.ContainsKey(force.GroupId))
            {
                TryJoin(active.ThirdRealm, force, Capacity(active));
                return ToPlan(active.RelicKeep ? Intent.AssaultRelicKeep : Intent.AssaultKeep, target, true,
                    "This third-realm force remains committed to the siege through defeat and regrouping.");
            }
        }

        foreach (var entry in CarrierEvents)
        foreach (var realm in entry.Value.Participants)
        {
            if (!realm.Value.ContainsKey(force.GroupId))
                continue;
            LiveObjective target = objectives.FirstOrDefault(objective => objective.Id == entry.Key) ?? CarrierTargets.GetValueOrDefault(entry.Key);
            if (target == null)
                continue;
            TryJoin(realm.Value, force, RelicCarrierRealmCap);
            return ToPlan(force.Realm == target.OwningRealm ? Intent.DefendEvent : Intent.HuntEnemy, target, true,
                force.Realm == target.OwningRealm
                    ? "This force remains committed to the relic escort through defeat and regrouping."
                    : "This force remains committed to intercepting the relic through defeat and regrouping.");
        }
        return null;
    }

    public sealed record BattleNotice(string TargetId, string Kind, eRealm Attacker, eRealm Defender,
        int Attackers, int Defenders, int ThirdRealm, int CapPerRealm, long RemainingMilliseconds,
        bool BattleStarted = false, int PresentAttackers = 0, int PresentDefenders = 0, int PresentThirdRealm = 0);

    public static BattleNotice[] Snapshot()
    {
        using (EnterSync())
        {
            Expire(GameLoop.GameLoopTime);
            return Events.Values.Where(entry => entry.ExpiresTick > GameLoop.GameLoopTime)
                .Select(entry => new BattleNotice(entry.TargetId, entry.DefenseReaction ? "SIEGE — immediate defense response" : entry.BattleStarted ? "SIEGE — ongoing" : entry.RelicKeep ? "Relic siege rally" : "Keep siege rally",
                    entry.AttackerRealm, entry.DefenderRealm, entry.Attackers.Values.Sum(), entry.Defenders.Values.Sum(),
                    entry.ThirdRealm.Values.Sum(), Capacity(entry),
                    Math.Max(0, entry.ExpiresTick - GameLoop.GameLoopTime), entry.BattleStarted,
                    Attendance(entry, entry.AttackerRealm, GameLoop.GameLoopTime),
                    Attendance(entry, entry.DefenderRealm, GameLoop.GameLoopTime),
                    Attendance(entry, OtherRealm(entry), GameLoop.GameLoopTime))).ToArray();
        }
    }

    public sealed record RallyOrder(string TargetId, eRealm Attacker, eRealm Defender, int[] Slots, long RemainingMilliseconds,
        RallySide Side = RallySide.Attacker);

    private static eRealm OtherRealm(ActiveEvent active) =>
        new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }.First(realm => realm != active.AttackerRealm && realm != active.DefenderRealm);

    /// <summary>The third wing's realm for camp orientation; also defined for guildless (realm None) defenders.</summary>
    public static eRealm ContesterRealm(eRealm attacker, eRealm defender) =>
        new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }.First(realm => realm != attacker && realm != defender);

    private static Dictionary<string, int> Participants(ActiveEvent active, eRealm realm) =>
        realm == active.AttackerRealm ? active.Attackers : realm == active.DefenderRealm ? active.Defenders : active.ThirdRealm;

    public static RallyOrder GetRallyOrder(string forceId, eRealm realm, long nowTick)
    {
        using (EnterSync())
        {
            Expire(nowTick);
            var active = Events.Values.FirstOrDefault(entry => !entry.BattleStarted && BucketOf(entry, forceId) != null);
            if (active == null) return null;
            var participants = BucketOf(active, forceId);
            int count = participants[forceId];
            // Reuse freed posts even when differently sized warbands leave
            // gaps. Exact 128 attendance must not depend on divisibility by eight.
            if (!active.Slots.TryGetValue(forceId, out int[] slots) || slots.Length != count)
            {
                var used = participants.Keys.Where(id => id != forceId && active.Slots.ContainsKey(id))
                    .SelectMany(id => active.Slots[id]).ToHashSet();
                slots = Enumerable.Range(0, Capacity(active)).Where(slot => !used.Contains(slot)).Take(count).ToArray();
                active.Slots[forceId] = slots;
            }
            return new(active.TargetId, active.AttackerRealm, active.DefenderRealm, slots, active.ExpiresTick - nowTick,
                SideOf(active, participants));
        }
    }

    public static bool IsRallying(string forceId, long nowTick)
    {
        using (EnterSync())
            return Events.Values.Any(entry => !entry.BattleStarted && entry.ExpiresTick > nowTick &&
                (entry.Attackers.ContainsKey(forceId) || entry.Defenders.ContainsKey(forceId) || entry.ThirdRealm.ContainsKey(forceId)));
    }

    public static bool IsBattleForce(string forceId, long nowTick)
    {
        using (EnterSync())
            return Events.Values.Any(entry => entry.BattleStarted && entry.ExpiresTick > nowTick &&
                (entry.Attackers.ContainsKey(forceId) || entry.Defenders.ContainsKey(forceId) || entry.ThirdRealm.ContainsKey(forceId))) ||
                CarrierEvents.Values.Any(entry => entry.ExpiresTick > nowTick && entry.Participants.Values.Any(realm => realm.ContainsKey(forceId)));
    }

    public static Plan KeepPlan(string forceId, eRealm realm, long nowTick)
    {
        using (EnterSync())
        {
            var active = Events.Values.FirstOrDefault(entry => entry.ExpiresTick > nowTick && BucketOf(entry, forceId) != null &&
                !IsAbandonedLocked(forceId, entry.TargetId, nowTick));
            return active == null ? null : ToPlan(ReferenceEquals(BucketOf(active, forceId), active.Defenders) ? Intent.DefendEvent :
                active.RelicKeep ? Intent.AssaultRelicKeep : Intent.AssaultKeep, active.Target, true,
                active.BattleStarted ? "Return to the ongoing siege" : "Assemble at the physical siege rally");
        }
    }

    private static int Attendance(ActiveEvent active, eRealm realm, long nowTick) => active.Present.Values
        .Where(entry => entry.Realm == realm && nowTick - entry.Tick <= AttendanceFreshnessMilliseconds && entry.Tick <= nowTick &&
            (entry.Bot == null || entry.Bot.IsAlive && entry.Bot.ObjectState == GameObject.eObjectState.Active &&
                entry.Bot.CurrentRegionID == entry.Region && (active.BattleStarted || !entry.Bot.InCombat && !entry.Bot.IsMoving &&
                (entry.Bot.Brain as BotBrain)?.HasAggro != true &&
                Vector3.DistanceSquared(new(entry.Bot.X, entry.Bot.Y, entry.Bot.Z), entry.Position) <= AutonomousRvrRally.ArrivalRadius * AutonomousRvrRally.ArrivalRadius)))
        .GroupBy(entry => entry.Force).Sum(group => Math.Min(group.Count(), Participants(active, realm).GetValueOrDefault(group.Key)));

    public static bool ProtectsParticipantFromInactivity(GameBot bot, long nowTick)
    {
        if (bot?.IsAutonomousWorldBot != true || !bot.IsAlive ||
            bot.ObjectState != GameObject.eObjectState.Active ||
            !AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR)) return false;
        string force = bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}";
        using (EnterSync())
        {
            foreach (var active in Events.Values)
            {
                Dictionary<string, int> bucket = BucketOf(active, force);
                if (active.ExpiresTick <= nowTick || bucket == null) continue;
                if (active.BattleStarted)
                {
                    // Real combat is intentional participation, even without a kill.
                    if (IsParticipatingInBattleCombat(bot)) return true;
                    // Waiting on the leader for the warband to gather is not idling.
                    if (active.Musters.TryGetValue(force, out var mustering) && !mustering.Departed) return true;
                    // The army may be waiting at its assigned rally post, or a
                    // defender may be holding an interior wall with no current
                    // attacker. Neither is an abandoned travel objective.
                    if (active.Present.TryGetValue(bot.DatabaseID, out var staged) &&
                        staged.Force == force && ReferenceEquals(staged.Bot, bot) &&
                        nowTick >= staged.Tick && nowTick - staged.Tick <= AttendanceFreshnessMilliseconds &&
                        bot.CurrentRegionID == staged.Region &&
                        Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), staged.Position) <=
                            AutonomousRvrRally.ArrivalRadius * AutonomousRvrRally.ArrivalRadius)
                        return true;
                    if (IsHoldingSiegeDefense(ReferenceEquals(bucket, active.Defenders), bot.CurrentRegionID == active.Target.RegionId,
                        Vector2.DistanceSquared(new(bot.X, bot.Y), new(active.Target.X, active.Target.Y)),
                        Math.Abs(bot.Z - active.Target.Z))) return true;
                    continue;
                }
                if (active.Present.TryGetValue(bot.DatabaseID, out var present) &&
                    present.Force == force && ReferenceEquals(present.Bot, bot) &&
                    present.Tick <= nowTick && nowTick - present.Tick <= AttendanceFreshnessMilliseconds &&
                    bot.CurrentRegionID == present.Region &&
                    Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), present.Position) <=
                        AutonomousRvrRally.ArrivalRadius * AutonomousRvrRally.ArrivalRadius)
                    return true;
            }
            return IsParticipatingInBattleCombat(bot) && CarrierEvents.Values.Any(entry =>
                entry.ExpiresTick > nowTick && entry.Participants.TryGetValue(bot.Realm, out var forces) && forces.ContainsKey(force));
        }
    }

    private static bool IsParticipatingInBattleCombat(GameBot bot) => bot.InCombat || bot.IsAttacking ||
        bot.Group?.GetMembersInTheGroup().Any(member => member != bot && member.IsAlive &&
            member.CurrentRegionID == bot.CurrentRegionID && bot.IsWithinRadius(member, 2000) &&
            (member.InCombat || member.IsAttacking)) == true;

    public static bool IsHoldingSiegeDefense(bool defender, bool sameRegion, float distanceSquared, int heightDifference) =>
        defender && sameRegion && float.IsFinite(distanceSquared) && distanceSquared >= 0 &&
        distanceSquared <= 3000 * 3000 && heightDifference >= 0 && heightDifference <= 1500;

    public static void ReportAttendance(string targetId, string forceId, eRealm realm, long botId, bool inPosition, long nowTick,
        GameBot bot = null, Vector3 position = default)
    {
        using (EnterSync())
        {
            Expire(nowTick);
            if (!Events.TryGetValue(targetId, out var active) || BucketOf(active, forceId) is not { } bucket) return;
            // Attendance counts under the force's side, whatever realm this member was born in.
            realm = SideRealm(active, bucket);
            if (inPosition) active.Present[botId] = (forceId, realm, nowTick, bot, position, bot?.CurrentRegionID ?? 0);
            else active.Present.Remove(botId);
            if (active.BattleStarted) return;
            if (nowTick - active.CreatedTick >= RealmEventPolicy.EarliestAssaultMilliseconds &&
                RealmEventPolicy.SiegeReady(active.RelicKeep, false,
                    Attendance(active, active.AttackerRealm, nowTick), Attendance(active, active.DefenderRealm, nowTick)))
                StartBattle(active, nowTick, "the attacking force is physically staged; defenders and third-realm reinforcements may respond");
        }
    }

    public static bool TryConsumeRelease(string forceId, long nowTick, out string reason)
    {
        using (EnterSync())
        {
            Expire(nowTick);
            return ReleasedForces.Remove(forceId, out reason);
        }
    }

    public static void ReportTravel(string targetId, string forceId, long memberId, double distance, bool arrived, long nowTick, GameBot bot = null)
    {
        using (EnterSync())
        {
            if (!Events.TryGetValue(targetId, out var active) || active.BattleStarted ||
                !(active.Attackers.ContainsKey(forceId) || active.Defenders.ContainsKey(forceId) || active.ThirdRealm.ContainsKey(forceId))) return;
            if (!active.Travel.TryGetValue(memberId, out var previous))
                previous = (forceId, nowTick, double.PositiveInfinity, default, 0);
            Vector3 position = bot == null ? previous.Position : new(bot.X, bot.Y, bot.Z);
            ushort region = bot?.CurrentRegionID ?? previous.Region;
            // Long multi-region journeys and real horse travel must not look
            // stalled merely because their coordinates are on another map.
            bool moved = bot?.IsAlive == true && (region != previous.Region ||
                Vector3.DistanceSquared(position, previous.Position) >= 256 * 256);
            if (arrived || moved || distance < previous.BestDistance - 128)
                active.Travel[memberId] = (forceId, nowTick, distance, position, region);
            else active.Travel[memberId] = previous;
        }
    }

    public static void RemoveForce(string forceId)
    {
        using (EnterSync())
        {
            foreach (var active in Events.Values)
            {
                active.Attackers.Remove(forceId);
                active.Defenders.Remove(forceId);
                active.ThirdRealm.Remove(forceId);
                active.Slots.Remove(forceId);
                active.Musters.Remove(forceId);
                foreach (long id in active.Travel.Where(pair => pair.Value.Force == forceId).Select(pair => pair.Key).ToArray())
                    active.Travel.Remove(id);
                foreach (long id in active.Present.Where(pair => pair.Value.Force == forceId).Select(pair => pair.Key).ToArray())
                    active.Present.Remove(id);
            }
            foreach (var active in CarrierEvents.Values)
                foreach (var realm in active.Participants.Values) realm.Remove(forceId);
            foreach (string targetId in ClaimForces.Where(pair => pair.Value.Force == forceId)
                         .Select(pair => pair.Key).ToArray())
                ClaimForces.Remove(targetId);
        }
    }

    public static long CarrierRemainingMilliseconds(string targetId, long nowTick)
    {
        using (EnterSync())
            return CarrierEvents.TryGetValue(targetId, out var active) ? Math.Max(0, active.ExpiresTick - nowTick) : 0;
    }

    private static void StartBattle(ActiveEvent active, long nowTick, string reason)
    {
        active.BattleStarted = true;
        active.ExpiresTick = nowTick + BattleLifetimeMilliseconds;
        active.LastActivityTick = Math.Max(active.LastActivityTick, nowTick);
        active.LastAttackerPresenceTick = Math.Max(active.LastAttackerPresenceTick, nowTick);
        RealmEventRecords.Progress(active.TargetId, "Battle", reason,
            active.Attackers.Values.Sum() + active.Defenders.Values.Sum() + active.ThirdRealm.Values.Sum(),
            Attendance(active, active.AttackerRealm, nowTick) + Attendance(active, active.DefenderRealm, nowTick) + Attendance(active, OtherRealm(active), nowTick));
        RealmEventNotices.Queue(active.TargetId, active.AttackerRealm,
            $"The assault on {active.Target.Name} is advancing. Reinforcements are welcome.");
        RealmEventNotices.Queue(active.TargetId, active.DefenderRealm,
            $"The assault on {active.Target.Name} has begun! Hold the walls and stand by your realm.");
        if (active.ThirdRealm.Count > 0) RealmEventNotices.Queue(active.TargetId, OtherRealm(active),
            $"The armies at {active.Target.Name} are committed to battle. Watch their flanks, warbands!");
        var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrEventLayer));
        if (log.IsInfoEnabled) log.Info($"RVR_SIEGE_STARTED target={active.TargetId} reason=\"{reason}\" " +
            $"present={Attendance(active, active.AttackerRealm, nowTick)}/{Attendance(active, active.DefenderRealm, nowTick)}/{Attendance(active, OtherRealm(active), nowTick)}");
    }

    private static void Expire(long nowTick)
    {
        foreach (var active in Events.Values)
        {
            if (!RealmEventBanter.ReminderDue(active.BattleStarted, active.PreparationNoticeSent, active.ExpiresTick - nowTick)) continue;
            active.PreparationNoticeSent = true;
            RealmEventNotices.Queue(active.TargetId, active.AttackerRealm, RealmEventBanter.SiegeReminder(active.Target.Name, false));
            RealmEventNotices.Queue(active.TargetId, active.DefenderRealm, RealmEventBanter.SiegeReminder(active.Target.Name, true));
            if (active.ThirdRealm.Count > 0) RealmEventNotices.Queue(active.TargetId, OtherRealm(active),
                $"The armies at {active.Target.Name} have about twenty minutes left to muster. Keep watch for an opening, scouts.");
        }
        foreach (var active in Events.Values.Where(entry => entry.BattleStarted && !entry.DefenseReaction).ToArray())
        {
            ExpireMusters(active, nowTick);
            if (!Events.ContainsKey(active.TargetId)) continue;
            // A warband still gathering has not yet had a chance to march: the
            // idle and absence clocks start when it departs (DepartLocked).
            if (!AutonomousRvrSiegeMuster.IdleClocksRun(AnyMustering(active))) continue;
            if (IsAbandonedByAttackers(true, false, !string.IsNullOrEmpty(active.PlayerAccount), active.LastAttackerPresenceTick, nowTick))
            {
                // Marching somewhere does not count here: nobody of the attacking
                // side has stood near the walls for the whole window.
                var absentLog = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrEventLayer));
                if (absentLog.IsInfoEnabled) absentLog.Info($"RVR_SIEGE_IDLE_CLOSED target={active.TargetId} reason=absent " +
                    $"absentMs={nowTick - active.LastAttackerPresenceTick} radius={AttackerPresenceRadius} " +
                    $"assigned={active.Attackers.Values.Sum()}/{active.Defenders.Values.Sum()}/{active.ThirdRealm.Values.Sum()}");
                EndEvent(active, nowTick, active.AttackerReachedKeep
                    ? "Siege abandoned: no attacker remained within reach of the keep for forty-five minutes"
                    : "Siege aborted: no attacker reached the keep within forty-five minutes");
                continue;
            }
            if (active.LastPressureTick > active.LastActivityTick) active.LastActivityTick = active.LastPressureTick;
            if (!IsIdleBattle(true, false, !string.IsNullOrEmpty(active.PlayerAccount), active.LastActivityTick, nowTick)) continue;
            if (Attendance(active, active.AttackerRealm, nowTick) + Attendance(active, active.DefenderRealm, nowTick) +
                Attendance(active, OtherRealm(active), nowTick) > 0 || SafeInCombat(active.TargetId))
            {
                active.LastActivityTick = nowTick;
                continue;
            }
            var idleLog = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrEventLayer));
            if (idleLog.IsInfoEnabled) idleLog.Info($"RVR_SIEGE_IDLE_CLOSED target={active.TargetId} reason=no_progress " +
                $"idleMs={nowTick - active.LastActivityTick} reachedKeep={active.AttackerReachedKeep} " +
                $"marching={active.Musters.Values.Count(muster => muster.Departed)} " +
                $"assigned={active.Attackers.Values.Sum()}/{active.Defenders.Values.Sum()}/{active.ThirdRealm.Values.Sum()}");
            EndEvent(active, nowTick, active.AttackerReachedKeep
                ? "Siege stalled: no recent attacking approach progress or keep combat for fifteen minutes"
                : "Siege aborted: attacking forces made no approach progress for fifteen minutes");
        }
        foreach (var active in Events.Values.Where(entry => entry.ExpiresTick <= nowTick).ToArray())
        {
            if (active.BattleStarted)
                EndEvent(active, nowTick, active.DefenseReaction ? "Siege defended: four-hour defense response expired" : "Siege defended: four-hour battle timer expired");
            else if (RealmEventPolicy.SiegeReady(active.RelicKeep, true,
                Attendance(active, active.AttackerRealm, nowTick), Attendance(active, active.DefenderRealm, nowTick)))
                StartBattle(active, nowTick, "preparation deadline reached with a viable attacking force");
            else
                EndEvent(active, nowTick, "Rally failed: insufficient physical attackers at the one-hour deadline");
        }
        if (nowTick >= _nextStragglerSweep)
        {
            _nextStragglerSweep = nowTick + 10_000;
            foreach (var active in Events.Values.Where(entry => !entry.BattleStarted).ToArray())
            foreach (var stalled in active.Travel.Where(pair => nowTick - pair.Value.ProgressTick >= StragglerTimeoutMilliseconds)
                         .GroupBy(pair => pair.Value.Force).ToArray())
            {
                // Missing members remain assigned so they can reinforce the
                // deadline-started battle. Keep bounded diagnostics, not eviction.
                string reason = $"Assigned members {string.Join(",", stalled.Select(pair => pair.Key))} made no approach progress for eight minutes; retaining siege assignment";
                foreach (var member in stalled)
                    active.Travel[member.Key] = member.Value with { ProgressTick = nowTick };
                var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrEventLayer));
                if (log.IsWarnEnabled) log.Warn($"RVR_RALLY_DELAY target={active.TargetId} force={stalled.Key} reason=\"{reason}\"");
            }
        }
        foreach (var entry in CarrierEvents.Where(pair => pair.Value.ExpiresTick <= nowTick).ToArray())
        {
            foreach (eRealm realm in entry.Value.Participants.Keys)
                RealmEventNotices.Queue(entry.Key, realm, $"Our time in the struggle for {CarrierTargets.GetValueOrDefault(entry.Key)?.Name ?? "the relic"} has run out. The escort and interception forces are standing down.");
            RealmEventRecords.Finish(entry.Key, "Timed out", "Relic escort/interception: four-hour battle timer expired.");
            foreach (string force in entry.Value.Participants.Values.SelectMany(realm => realm.Keys))
                ReleasedForces[force] = "Relic event ended: four-hour battle timer expired";
            CarrierEvents.Remove(entry.Key);
            CarrierTargets.Remove(entry.Key);
            Cooldowns[entry.Key] = nowTick + TargetCooldownMilliseconds;
        }
    }

    private static bool SafeInCombat(string targetId)
    {
        try { return TargetInCombat?.Invoke(targetId) == true; }
        catch (Exception) { return false; }
    }

    private static void EndEvent(ActiveEvent active, long nowTick, string reason, eRealm winner = eRealm.None)
    {
        foreach (eRealm realm in new[] { active.AttackerRealm, active.DefenderRealm, OtherRealm(active) })
            RealmEventNotices.Queue(active.TargetId, realm, RealmEventBanter.SiegeOutcome(active.Target.Name,
                realm == active.DefenderRealm, reason.StartsWith("Siege defended"), reason.StartsWith("Rally failed"), winner, realm));
        RealmEventRecords.Finish(active.TargetId, reason.StartsWith("Rally failed") ? "Failed rally" : reason.StartsWith("Keep captured") ? "Captured" :
            reason.StartsWith("Siege defended") ? "Defended (timeout)" : "Ended (unconfirmed)", reason,
            active.Attackers.Values.Sum() + active.Defenders.Values.Sum() + active.ThirdRealm.Values.Sum());
        foreach (string force in active.Attackers.Keys.Concat(active.Defenders.Keys).Concat(active.ThirdRealm.Keys))
            ReleasedForces[force] = reason;
        Events.Remove(active.TargetId);
        Cooldowns[active.TargetId] = nowTick + TargetCooldownMilliseconds;
        var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrEventLayer));
        if (log.IsInfoEnabled) log.Info($"RVR_EVENT_ENDED target={active.TargetId} reason=\"{reason}\"");
    }

    private static bool TryJoin(Dictionary<string, int> participants, Force force, int cap)
    {
        if (participants.Values.Sum() - participants.GetValueOrDefault(force.GroupId) + force.MemberCount > cap)
            return false;
        participants[force.GroupId] = force.MemberCount;
        return true;
    }

    private static int AssaultPressure(Force force, LiveObjective objective) =>
        objective.EnemyCount * 4 + objective.GuardStrength * 2 + objective.ClosedDoors * 3 - force.MemberCount * 2;

    private static double JoinPriority(ActiveEvent active, Force force)
    {
        Dictionary<string, int> compatible = force.Realm == active.AttackerRealm ? active.Attackers :
            force.Realm == active.DefenderRealm ? active.Defenders : active.ThirdRealm;
        double compatibleFill = compatible.Values.Sum() / (double)Capacity(active);
        double totalFill = (active.Attackers.Values.Sum() + active.Defenders.Values.Sum() + active.ThirdRealm.Values.Sum()) /
                           (double)(Capacity(active) * 3);
        return compatibleFill * 0.8d + totalFill * 0.2d;
    }

    private static bool OnCooldown(string targetId, long nowTick) => Cooldowns.TryGetValue(targetId, out long until) && until > nowTick;

    private static bool TryJoinCarrier(LiveObjective carrier, Force force, long nowTick, double roll)
    {
        string carrierId = carrier.Id;
        CarrierTargets[carrierId] = carrier;
        if (!CarrierEvents.TryGetValue(carrierId, out var active))
        {
            active = (nowTick + BattleLifetimeMilliseconds, new Dictionary<eRealm, Dictionary<string, int>>());
            CarrierEvents[carrierId] = active;
            RealmEventRecords.Begin(carrierId, carrier.Name, "Relic", GlobalConstants.RealmToName(carrier.OwningRealm), "Relic escort / interception");
            RealmEventRecords.Progress(carrierId, "Battle", "Relic escort / interception", 0, 0);
        }
        if (!active.Participants.TryGetValue(force.Realm, out Dictionary<string, int> realmParticipants))
            active.Participants[force.Realm] = realmParticipants = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!realmParticipants.ContainsKey(force.GroupId) &&
            !ShouldJoinActiveEvent(force, realmParticipants.Values.Sum(), RelicCarrierRealmCap, true, false, roll))
            return false;
        return TryJoin(realmParticipants, force, RelicCarrierRealmCap);
    }

    private static void Cleanup(long nowTick, IReadOnlyCollection<LiveObjective> objectives)
    {
        Expire(nowTick);
        foreach (LiveObjective carrier in objectives.Where(o => o.IsRelicCarrier && CarrierEvents.ContainsKey(o.Id)))
            CarrierTargets[carrier.Id] = carrier;
        foreach (ActiveEvent active in Events.Values)
        {
            if (objectives.Any(target => target.Id == active.TargetId && target.UnderAttack))
            {
                active.LastActivityTick = Math.Max(active.LastActivityTick, nowTick);
                if (!active.AttackObserved)
                {
                    active.AttackObserved = true;
                    RealmEventNotices.Queue(active.TargetId, active.DefenderRealm,
                        $"{active.Target.Name} is under attack! Available warbands can reinforce the defenders.");
                    RealmEventNotices.Queue(active.TargetId, OtherRealm(active),
                        $"Fighting has broken out at {active.Target.Name}. There is an opportunity to intervene.");
                }
                // Incidental combat still recruits reinforcements and is fought
                // normally, but cannot bypass the army attendance/start rules.
                // ReportAttendance and the preparation deadline own that clock.
            }
        }
        // Each caller supplies only objectives it can reach, not an authoritative
        // world inventory. An absent row cannot cancel another force's expedition.
        foreach (string id in Events.Where(pair => pair.Value.ExpiresTick <= nowTick ||
                     objectives.Any(target => target.Id == pair.Key && target.OwningRealm != pair.Value.DefenderRealm))
                     .Select(pair => pair.Key).ToArray())
        {
            var changed = objectives.FirstOrDefault(target => target.Id == id && target.OwningRealm != Events[id].DefenderRealm);
            EndEvent(Events[id], nowTick, changed == null ? "Siege objective is no longer available" : "Keep captured: ownership changed",
                changed?.OwningRealm ?? eRealm.None);
        }
        foreach (string id in Cooldowns.Where(pair => pair.Value <= nowTick).Select(pair => pair.Key).ToArray())
            Cooldowns.Remove(id);
        foreach (string id in CarrierEvents.Where(pair => pair.Value.ExpiresTick <= nowTick)
                     .Select(pair => pair.Key).ToArray())
        {
            RealmEventRecords.Finish(id, "Timed out", "Relic event expired during objective cleanup.");
            foreach (string force in CarrierEvents[id].Participants.Values.SelectMany(realm => realm.Keys))
                ReleasedForces[force] = "Relic captured or event no longer available";
            CarrierEvents.Remove(id);
            CarrierTargets.Remove(id);
            Cooldowns[id] = nowTick + TargetCooldownMilliseconds;
        }
    }

    public static bool IsForceCommitted(string forceId, long nowTick)
    {
        if (string.IsNullOrWhiteSpace(forceId))
            return false;
        using (EnterSync())
            return Events.Values.Any(active => active.ExpiresTick > nowTick &&
                       (active.Attackers.ContainsKey(forceId) || active.Defenders.ContainsKey(forceId) ||
                        active.ThirdRealm.ContainsKey(forceId))) ||
                   CarrierEvents.Values.Any(active => active.ExpiresTick > nowTick &&
                       active.Participants.Values.Any(realm => realm.ContainsKey(forceId)));
    }

    public static bool IsPlayerDefenseResponse(string targetId, long nowTick)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return false;
        using (EnterSync())
            return Events.TryGetValue(targetId, out var active) && active.ExpiresTick > nowTick &&
                (active.DefenseReaction || !string.IsNullOrEmpty(active.PlayerAccount));
    }

    public static bool IsTargetActive(string targetId, long nowTick)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            return false;
        using (EnterSync())
            return (Events.TryGetValue(targetId, out ActiveEvent active) && active.ExpiresTick > nowTick) ||
                   (CarrierEvents.TryGetValue(targetId, out var carrier) && carrier.ExpiresTick > nowTick);
    }

    public static void EndTarget(string targetId, long nowTick, eRealm winner = eRealm.None)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            return;
        using (EnterSync())
        {
            ClaimForces.Remove(targetId);
            if (Events.TryGetValue(targetId, out var active))
                EndEvent(active, nowTick, "Keep captured: siege ended", winner);
            if (CarrierEvents.TryGetValue(targetId, out var escort))
            {
                string name = CarrierTargets.GetValueOrDefault(targetId)?.Name ?? "The relic";
                foreach (eRealm realm in escort.Participants.Keys.Append(winner).Where(r => r != eRealm.None).Distinct())
                    RealmEventNotices.Queue(targetId, realm, winner == eRealm.None
                        ? $"{name} has been secured at a shrine. The struggle on the road has ended."
                        : realm == winner ? $"{name} is safe in our shrine! Honour to its bearers and defenders."
                        : $"{GlobalConstants.RealmToName(winner)} has secured {name} at a shrine. Our struggle on the road is over.");
            }
            bool removed = CarrierEvents.Remove(targetId, out var carrier);
            if (removed) RealmEventRecords.Finish(targetId, "Captured / returned", "Relic mounted or recovered; escort/interception ended.");
            CarrierTargets.Remove(targetId);
            if (removed)
                foreach (string force in carrier.Participants.Values.SelectMany(realm => realm.Keys))
                    ReleasedForces[force] = "Relic captured: event ended";
            if (removed)
                Cooldowns[targetId] = nowTick + TargetCooldownMilliseconds;
        }
    }

    public static void TransferToRelicCarrier(string keepId, string carrierId, long nowTick, LiveObjective carrierAnchor = null)
    {
        using (EnterSync())
        {
            if (!Events.TryGetValue(keepId, out var active) || active.ExpiresTick <= nowTick) return;
            if (!active.BattleStarted)
            {
                EndEvent(active, nowTick, "Relic removed before the rally completed");
                return;
            }
            CarrierEvents[carrierId] = (active.ExpiresTick, new()
            {
                [active.AttackerRealm] = new(active.Attackers, StringComparer.Ordinal),
                [active.DefenderRealm] = new(active.Defenders, StringComparer.Ordinal),
                [OtherRealm(active)] = new(active.ThirdRealm, StringComparer.Ordinal)
            });
            // Retain the last known native location through partial route
            // snapshots. Capture/expiry still releases the whole escort.
            CarrierTargets[carrierId] = carrierAnchor ?? active.Target with
                { Id = carrierId, IsRelicCarrier = true, OwningRealm = active.AttackerRealm };
            foreach (eRealm realm in new[] { active.AttackerRealm, active.DefenderRealm, OtherRealm(active) })
                RealmEventNotices.Queue(keepId, realm, $"The relic has left {active.Target.Name}! The battle continues on the road; its fate is not yet decided.");
            RealmEventRecords.Finish(keepId, "Relic taken", "Relic removed; the battle continued as an escort/interception event.");
            RealmEventRecords.Begin(carrierId, carrierAnchor?.Name ?? active.Target.Name, "Relic", GlobalConstants.RealmToName(active.AttackerRealm), "Continued from " + active.Target.Name);
            RealmEventRecords.Progress(carrierId, "Battle", "Relic escort / interception", active.Attackers.Values.Sum() + active.Defenders.Values.Sum() + active.ThirdRealm.Values.Sum(), 0);
            Events.Remove(keepId);
            Cooldowns[keepId] = nowTick + TargetCooldownMilliseconds;
        }
    }

    public static string RelicCarrierTargetId(GameRelic relic)
    {
        return relic == null ? string.Empty : $"rvr-relic-carrier-{(int)relic.OriginalRealm}-{(int)relic.RelicType}";
    }
}
