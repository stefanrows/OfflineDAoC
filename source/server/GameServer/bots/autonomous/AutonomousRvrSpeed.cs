using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// P6, "a retreat or a trip is a speed-class decision": every 2003 8-man had
/// its essential speed class (Bard, Skald, Minstrel) who kept speed up by song
/// twisting while the group travelled; a Mid Healer's augmentation speed was
/// the weaker fallback; groups without speed stayed near keeps and milegates.
/// The group moved as one body: the leader waited for a straggler, members
/// sprinted to close a gap and nobody ran past the leader.
/// Autonomous RvR world bots only; companions and player-led groups are unchanged.
/// </summary>
public static class AutonomousRvrSpeed
{
    /// <summary>Groups this big look for a speed class; duos and trios of two do not.</summary>
    public const int MinimumSpeedGroupSize = 3;
    /// <summary>Recruitment weight of a real speed class when the group has none (a missing healer weighs 4, a tank 1).</summary>
    public const int PrimarySpeedBonus = 3;
    /// <summary>Recruitment weight of a Healer who knows the group speed (the fallback).</summary>
    public const int FallbackSpeedBonus = 1;

    /// <summary>The leader waits when a member falls this far behind out of combat.</summary>
    public const float HoldGap = 1_200;
    /// <summary>A waiting leader walks on once every member is this close again.</summary>
    public const float ResumeGap = 600;
    public const long MaximumHoldMilliseconds = 6_000;
    /// <summary>After a hold the leader walks at least this long before it waits again.</summary>
    public const long HoldCooldownMilliseconds = 10_000;
    /// <summary>Members farther than this are rejoining (released, ported) and do not stop the group.</summary>
    public const float RejoinGap = 4_000;

    /// <summary>A member sprints when its formation slot is farther than this.</summary>
    public const float SprintStartGap = 150;
    /// <summary>A sprinting member stops sprinting when its slot is this close.</summary>
    public const float SprintStopGap = 80;
    public const int SprintMinimumEndurancePercent = 20;

    /// <summary>A group without speed prefers roaming spots this close to a keep or hub.</summary>
    public const float NoSpeedAnchorRange = 8_000;
    public const double NoSpeedNearAnchorFactor = 1.35;
    public const double NoSpeedFarFactor = 0.8;

    // ------------------------------------------------------------ pure rules

    /// <summary>Bard, Skald and Minstrel: the realm's primary speed.</summary>
    public static bool IsSpeedClass(eCharacterClass characterClass) =>
        AutonomousRvrDoctrine.TraitsOf(characterClass).HasFlag(RvrClassTraits.Speed);

    /// <summary>A Healer (Midgard) with the augmentation group speed is the fallback speed.</summary>
    public static bool IsFallbackSpeed(eCharacterClass characterClass, bool knowsGroupSpeed) =>
        characterClass == eCharacterClass.Healer && knowsGroupSpeed;

    public static bool ProvidesSpeed(eCharacterClass characterClass, bool knowsGroupSpeed) =>
        IsSpeedClass(characterClass) || IsFallbackSpeed(characterClass, knowsGroupSpeed);

    /// <summary>
    /// How much a candidate is wanted for speed: strongly when the group of
    /// <paramref name="plannedSize"/> (3+) has no speed and the candidate is a
    /// speed class, a little for a Healer with speed, nothing once speed is in.
    /// A weight, not a requirement: the group still leaves without one.
    /// </summary>
    public static int RecruitmentSpeedBonus(eCharacterClass candidate, bool candidateKnowsGroupSpeed,
        bool groupHasSpeed, int plannedSize)
    {
        if (groupHasSpeed || plannedSize < MinimumSpeedGroupSize)
            return 0;
        if (IsSpeedClass(candidate))
            return PrimarySpeedBonus;
        return IsFallbackSpeed(candidate, candidateKnowsGroupSpeed) ? FallbackSpeedBonus : 0;
    }

    /// <summary>
    /// An autonomous RvR performer in a travelling group sings only its speed
    /// (a Bard twists endurance with it); never in immediate combat.
    /// </summary>
    public static bool FocusTravelSongs(bool autonomousRvrGroup, bool traveling, bool immediateCombat) =>
        autonomousRvrGroup && traveling && !immediateCombat;

    /// <summary>
    /// A marching group does not stop for cast-time buffs, except that an
    /// autonomous RvR speed caster (a Healer) stops once to start its group
    /// speed; not in combat.
    /// </summary>
    public static bool CastsSpeedOnTheMarch(bool autonomousRvr, bool groupSpeedSpell, bool inCombat) =>
        autonomousRvr && groupSpeedSpell && !inCombat;

    /// <summary>
    /// Whether the leader stands and waits. It starts when the worst gap of a
    /// member (within <see cref="RejoinGap"/>) is over 1,200 out of combat, and
    /// lasts until everyone is within 600 or six seconds pass; then the group
    /// walks at least ten seconds before the next wait.
    /// </summary>
    public static bool LeaderHolds(bool holding, long heldMilliseconds, long sinceLastHoldMilliseconds,
        float worstGap, bool anyCombat) =>
        LeaderHolds(holding, heldMilliseconds, sinceLastHoldMilliseconds, worstGap, anyCombat,
            MaximumHoldMilliseconds, HoldCooldownMilliseconds);

    /// <summary>The same rule with this hold's rolled limits (see <see cref="Jitter"/>).</summary>
    public static bool LeaderHolds(bool holding, long heldMilliseconds, long sinceLastHoldMilliseconds,
        float worstGap, bool anyCombat, long maximumHoldMilliseconds, long cooldownMilliseconds)
    {
        if (anyCombat)
            return false;
        if (holding)
            return worstGap > ResumeGap && heldMilliseconds < maximumHoldMilliseconds;
        return worstGap > HoldGap && sinceLastHoldMilliseconds >= cooldownMilliseconds;
    }

    /// <summary>A human leader's timing is never exact: a base time within ±25 %.</summary>
    public static long Jitter(long baseMilliseconds, double roll) =>
        (long)Math.Round(baseMilliseconds * (0.75 + 0.5 * Math.Clamp(roll, 0, 1)));

    /// <summary>
    /// The leader stops waiting for one member (stuck in a nav pocket, say)
    /// after it waited three times for it without the member closing up.
    /// </summary>
    public const int MaximumHoldsPerStraggler = 3;

    public static bool GivesUpOnStraggler(int holdsForThisMember) => holdsForThisMember >= MaximumHoldsPerStraggler;

    /// <summary>
    /// The companion stick-run cleanup ends a sprint that has outlived the
    /// stick run, except for an autonomous RvR world bot, whose sprint the
    /// group speed rules own (else it was ended and restarted every think).
    /// </summary>
    public static bool StickCleanupEndsSprint(bool sprinting, long sinceStickSprintMilliseconds, long limitMilliseconds,
        bool rvrOwnsSprint) =>
        sprinting && !rvrOwnsSprint && sinceStickSprintMilliseconds > limitMilliseconds;

    /// <summary>
    /// RvR recruitment weight (higher first): a missing healer 4, a missing
    /// speed class 3 (see <see cref="RecruitmentSpeedBonus"/>), a missing tank 1.
    /// A healer that also brings speed (a Bard) adds both.
    /// </summary>
    public const int MissingHealerWeight = 4;
    public const int MissingTankWeight = 1;

    public static int RvrRecruitmentWeight(bool candidateHeals, bool groupNeedsHealer, bool candidateTanks,
        bool groupNeedsTank, int speedBonus) =>
        (candidateHeals && groupNeedsHealer ? MissingHealerWeight : 0) +
        (candidateTanks && groupNeedsTank ? MissingTankWeight : 0) + Math.Max(0, speedBonus);

    /// <summary>
    /// Whether a follower sprints: to close a gap to its slot (over 150, with
    /// at least 20 % endurance) while the group travels, stopping at 80 or when
    /// the endurance runs low; never in combat.
    /// </summary>
    public static bool FollowerSprints(float gapToSlot, bool inCombat, int endurancePercent, bool sprinting)
    {
        if (inCombat || gapToSlot > RejoinGap)
            return false;
        if (sprinting)
            return gapToSlot > SprintStopGap && endurancePercent > 10;
        return gapToSlot > SprintStartGap && endurancePercent >= SprintMinimumEndurancePercent;
    }

    /// <summary>A follower ahead of its leader never runs faster than the leader (speed song or not).</summary>
    public static short CapFollowerSpeed(short speed, short leaderSpeed, bool aheadOfLeader) =>
        aheadOfLeader && leaderSpeed > 0 ? Math.Min(speed, leaderSpeed) : speed;

    /// <summary>
    /// The "walk to the camp after a release" habit (on foot, no horse) is a
    /// solo PvE habit; a bot on an RvR objective or in an RvR group never uses it.
    /// </summary>
    public static bool WalksAfterRelease(bool walkFlag, bool rvrObjective, bool inRvrGroup) =>
        walkFlag && !rvrObjective && !inRvrGroup;

    /// <summary>
    /// A monster below a third of its health slows down; a player does not.
    /// Autonomous RvR world bots move like players.
    /// </summary>
    public static bool KeepsPaceAtLowHealth(bool autonomousWorldBot, bool playerLed, bool rvrObjective) =>
        autonomousWorldBot && !playerLed && rvrObjective;

    /// <summary>
    /// The "no speed" travel habit: a group of three or more without speed
    /// favours roaming spots within 8,000 of a keep or hub a little and the
    /// open field a little less. A weight, not a rule.
    /// </summary>
    public static double NoSpeedRoamFactor(bool groupHasSpeed, int groupSize, double nearestAnchorDistance)
    {
        if (groupHasSpeed || groupSize < MinimumSpeedGroupSize)
            return 1.0;
        return nearestAnchorDistance <= NoSpeedAnchorRange ? NoSpeedNearAnchorFactor : NoSpeedFarFactor;
    }

    /// <summary>Sieges recover the column instead of silently dropping distant
    /// members after three short waits. Combat never becomes a travel permit.</summary>
    public enum SiegeCohesionDecision { Advance, Hold, Fail }
    public const long SiegeNoProgressMilliseconds = 120_000;
    public const long SiegeMaximumRegroupMilliseconds = 300_000;

    public static SiegeCohesionDecision SiegeCohesion(bool holding, float worstGap,
        long heldMilliseconds, long stalledMilliseconds)
    {
        if (worstGap <= (holding ? ResumeGap : HoldGap)) return SiegeCohesionDecision.Advance;
        return heldMilliseconds >= SiegeMaximumRegroupMilliseconds || stalledMilliseconds >= SiegeNoProgressMilliseconds
            ? SiegeCohesionDecision.Fail : SiegeCohesionDecision.Hold;
    }

    /// <summary>
    /// Bug 75: a 2003 raid leader moved out once the core of the group and the
    /// ram carriers stood with him; a straggler in another zone caught up on
    /// his own. <paramref name="together"/> counts the leader and the living
    /// members beside him; a group no larger than the quorum still needs all.
    /// </summary>
    public static bool SiegeQuorumMarch(bool enabled, int quorum, int living, int together, bool carriersTogether) =>
        enabled && carriersTogether && together >= Math.Max(Math.Min(Math.Clamp(quorum, 2, 8), living), living / 2 + 1);

    /// <summary>The column closed up by at least this much since its best gap: real regroup progress.</summary>
    public const float SiegeProgressStep = 250;

    /// <summary>
    /// The worst straggler gap measured from a fixed point (the hold centre of
    /// a looping leader), so the leader's own loop movement can neither fake
    /// nor hide regroup progress. A member in another region is infinitely far.
    /// </summary>
    public static float WorstGapFrom(System.Numerics.Vector3 anchor,
        IEnumerable<(bool SameRegion, System.Numerics.Vector3 Position)> stragglers)
    {
        float worst = 0;
        foreach (var (sameRegion, position) in stragglers ?? [])
            worst = Math.Max(worst, sameRegion ? System.Numerics.Vector3.Distance(anchor, position) : float.PositiveInfinity);
        return worst;
    }

    public static bool SiegeColumnClosedUp(float bestGap, float worstGap) => worstGap < bestGap - SiegeProgressStep;

    // ----------------------------------------------------------- live glue

    private sealed class GroupSpeedState
    {
        public bool Holding;
        public long HoldStarted;
        public long HoldLimit = MaximumHoldMilliseconds;
        public long Cooldown = HoldCooldownMilliseconds;
        public int StragglerId;
        public int StragglerHolds;
        public int IgnoredId;
        public long LastHoldEnd = long.MinValue / 2;
        public long SpeedCheckedUntil;
        public bool HasSpeed;
        public long NextTravelSample;
    }

    private static readonly ConditionalWeakTable<Group, GroupSpeedState> Groups = new();

    private static GroupSpeedState StateOf(Group group) => Groups.GetOrCreateValue(group);

    /// <summary>The bot knows a group speed spell at its level (Healer augmentation, songs).</summary>
    public static bool KnowsGroupSpeed(GameBot bot) => bot != null &&
        (bot.MiscSpells ?? []).Concat(bot.InstantMiscSpells ?? [])
            .Any(spell => spell != null && spell.SpellType == eSpellType.SpeedEnhancement &&
                spell.Target == eSpellTarget.GROUP && spell.Level <= bot.Level);

    public static bool ProvidesSpeed(GameBot bot) => bot?.CharacterClass != null &&
        (IsSpeedClass((eCharacterClass)bot.CharacterClass.ID) ||
         (eCharacterClass)bot.CharacterClass.ID == eCharacterClass.Healer && KnowsGroupSpeed(bot));

    /// <summary>The group's speed: a speed class first, else a Healer with speed, else null.</summary>
    public static GameBot SpeedProvider(IEnumerable<GameBot> members)
    {
        GameBot fallback = null;
        foreach (GameBot member in members ?? [])
        {
            if (member?.CharacterClass == null)
                continue;
            if (IsSpeedClass((eCharacterClass)member.CharacterClass.ID))
                return member;
            if (fallback == null && (eCharacterClass)member.CharacterClass.ID == eCharacterClass.Healer &&
                KnowsGroupSpeed(member))
                fallback = member;
        }
        return fallback;
    }

    public static bool HasSpeed(IEnumerable<GameBot> members) => SpeedProvider(members) != null;

    /// <summary>Whether the group has speed; cached for a minute per group.</summary>
    public static bool GroupHasSpeed(Group group)
    {
        if (group == null)
            return false;
        GroupSpeedState state = StateOf(group);
        long now = GameLoop.GameLoopTime;
        lock (state)
        {
            if (now < state.SpeedCheckedUntil)
                return state.HasSpeed;
        }
        bool hasSpeed = HasSpeed(group.GetMembersInTheGroup().OfType<GameBot>());
        lock (state)
        {
            state.HasSpeed = hasSpeed;
            state.SpeedCheckedUntil = now + 60_000;
        }
        return hasSpeed;
    }

    /// <summary>
    /// The leader of an autonomous RvR group decides whether to stand and wait
    /// for a straggler before its next travel order.
    /// </summary>
    public static bool ShouldLeaderHold(GameBot leader)
    {
        if (!AutonomousRvrDoctrineRuntime.Applies(leader) || leader.Group?.LivingLeader != leader ||
            AutonomousRvrDoctrineRuntime.IsRetreating(leader, out _))
            return false;
        bool combat = leader.InCombat || leader.IsAttacking;
        GroupSpeedState state = StateOf(leader.Group);
        int ignored;
        lock (state)
            ignored = state.IgnoredId;
        float worst = 0;
        int worstId = 0;
        foreach (GameLiving living in leader.Group.GetMembersInTheGroup())
        {
            if (living == leader || living is not GameBot member || !member.IsAlive)
                continue;
            if (member.InCombat || member.IsAttacking)
                combat = true;
            if (member.CurrentRegionID != leader.CurrentRegionID || member.IsOnStableMasterRoute)
                continue;
            float gap = member.GetDistanceTo(leader);
            if (member.ObjectID == ignored)
            {
                // A given-up straggler counts again once it has caught up.
                if (gap <= ResumeGap)
                    lock (state)
                        state.IgnoredId = 0;
                continue;
            }
            if (gap <= RejoinGap && gap > worst)
            {
                worst = gap;
                worstId = member.ObjectID;
            }
        }

        long now = GameLoop.GameLoopTime;
        bool started;
        bool hold;
        lock (state)
        {
            if (!state.Holding && worst > HoldGap && worstId == state.StragglerId &&
                GivesUpOnStraggler(state.StragglerHolds))
            {
                // Three waits did not bring this member closer: walk on without it.
                state.IgnoredId = worstId;
                state.StragglerId = 0;
                state.StragglerHolds = 0;
                return false;
            }
            hold = LeaderHolds(state.Holding, now - state.HoldStarted, now - state.LastHoldEnd, worst, combat,
                state.HoldLimit, state.Cooldown);
            started = hold && !state.Holding;
            if (started)
            {
                state.HoldStarted = now;
                state.HoldLimit = Jitter(MaximumHoldMilliseconds, Random.Shared.NextDouble());
                if (worstId == state.StragglerId)
                    state.StragglerHolds++;
                else
                {
                    state.StragglerId = worstId;
                    state.StragglerHolds = 1;
                }
            }
            else if (!hold && state.Holding)
            {
                state.LastHoldEnd = now;
                state.Cooldown = Jitter(HoldCooldownMilliseconds, Random.Shared.NextDouble());
                // Closed up: the count for that member starts over.
                if (worst <= ResumeGap)
                    state.StragglerHolds = 0;
            }
            state.Holding = hold;
        }
        if (started)
            TravelStats.Hold();
        return hold;
    }

    /// <summary>
    /// A follower of a travelling autonomous RvR group sprints to close the gap
    /// to its slot and stops once there; the new pace applies at once.
    /// </summary>
    public static void UpdateFollowerSprint(GameBot bot, float gapToSlot)
    {
        if (!AutonomousRvrDoctrineRuntime.Applies(bot) || bot.Group == null)
            return;
        bool inCombat = bot.InCombat || bot.IsAttacking;
        int endurance = bot.MaxEndurance > 0 ? bot.Endurance * 100 / bot.MaxEndurance : 0;
        bool sprinting = bot.IsSprinting;
        bool wanted = FollowerSprints(gapToSlot, inCombat, endurance, sprinting);
        if (wanted == sprinting)
            return;
        bot.Sprint(wanted);
        if (bot.IsSprinting != sprinting)
            bot.OnMaxSpeedChange();
    }

    /// <summary>Ends a world bot's sprint when a fight starts.</summary>
    public static void EndSprintForCombat(GameBot bot)
    {
        if (bot?.IsSprinting == true && AutonomousRvrDoctrineRuntime.Applies(bot))
        {
            bot.Sprint(false);
            bot.OnMaxSpeedChange();
        }
    }

    /// <summary>Counts a travel moment of a group leader for the five-minute summary (sampled every 2 s).</summary>
    public static void NoteTravel(GameBot leader)
    {
        if (!AutonomousRvrDoctrineRuntime.Applies(leader) || leader.Group?.LivingLeader != leader ||
            leader.InCombat || !leader.IsMoving)
            return;
        GroupSpeedState state = StateOf(leader.Group);
        long now = GameLoop.GameLoopTime;
        lock (state)
        {
            if (now < state.NextTravelSample)
                return;
            state.NextTravelSample = now + 2_000;
        }
        bool underSpeed = leader.BuffBonusMultCategory1.Get((int)eProperty.MaxSpeed) > 1.0;
        TravelStats.Sample(RuntimeHelpers.GetHashCode(leader.Group), GroupHasSpeed(leader.Group), underSpeed);
    }

    /// <summary>One line when an RvR group sets out: its speed class or none.</summary>
    public static void LogDeparture(string groupId, IReadOnlyCollection<GameBot> members)
    {
        if (!Log.IsInfoEnabled)
            return;
        GameBot provider = SpeedProvider(members);
        string speedClass = provider?.CharacterClass != null
            ? ((eCharacterClass)provider.CharacterClass.ID).ToString()
            : "none";
        Log.Info($"RVR_SPEED_STATE group={groupId} speed_class={speedClass} members={members?.Count ?? 0}");
    }

    /// <summary>
    /// The nearest keep, tower or border hub (and its bindstone landing) to a
    /// point in <paramref name="region"/>, for the no-speed habit.
    /// </summary>
    public static double NearestAnchorDistance(ushort region, int x, int y)
    {
        double best = double.MaxValue;
        void Consider(double ax, double ay)
        {
            double distance = Math.Sqrt((ax - x) * (ax - x) + (ay - y) * (ay - y));
            if (distance < best)
                best = distance;
        }
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            if (AutonomousRvrStaging.TryGetBorderKeep(realm, out AutonomousRvrStaging.BorderKeep hub) && hub.RegionId == region)
                Consider(hub.Position.X, hub.Position.Y);
        foreach (PvpCombatant.SafeHubLanding landing in PvpCombatant.SafeHubLandings)
            if (landing.RegionId == region)
                Consider(landing.X, landing.Y);
        foreach (AbstractGameKeep keep in GameServer.KeepManager.GetKeepsOfRegion(region))
            Consider(keep.X, keep.Y);
        return best;
    }

    private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrSpeed));

    /// <summary>
    /// Five-minute summary: groups seen travelling, how many had speed, the
    /// share of leader travel samples under a speed buff, and leader holds.
    /// </summary>
    private static class TravelStats
    {
        private const long WindowMilliseconds = 300_000;
        private static readonly object Sync = new();
        private static readonly HashSet<int> GroupsSeen = [];
        private static readonly HashSet<int> GroupsWithSpeed = [];
        private static int _samples;
        private static int _underSpeed;
        private static int _holds;
        private static long _windowStart;

        public static void Sample(int group, bool hasSpeed, bool underSpeed)
        {
            lock (Sync)
            {
                GroupsSeen.Add(group);
                if (hasSpeed)
                    GroupsWithSpeed.Add(group);
                _samples++;
                if (underSpeed)
                    _underSpeed++;
                FlushIfDue();
            }
        }

        public static void Hold()
        {
            lock (Sync)
            {
                _holds++;
                FlushIfDue();
            }
        }

        private static void FlushIfDue()
        {
            long now = GameLoop.GameLoopTime;
            if (_windowStart == 0)
                _windowStart = now;
            if (now - _windowStart < WindowMilliseconds)
                return;
            if (Log.IsInfoEnabled)
                Log.Info($"RVR_SPEED_TRAVEL window_s={(now - _windowStart) / 1000} groups={GroupsSeen.Count} " +
                    $"with_speed={GroupsWithSpeed.Count} " +
                    $"travel_under_speed_pct={(_samples == 0 ? 0 : _underSpeed * 100 / _samples)} leader_holds={_holds}");
            GroupsSeen.Clear();
            GroupsWithSpeed.Clear();
            _samples = 0;
            _underSpeed = 0;
            _holds = 0;
            _windowStart = now;
        }
    }
}
