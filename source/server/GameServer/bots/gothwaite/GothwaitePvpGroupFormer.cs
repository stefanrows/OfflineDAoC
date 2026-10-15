using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// The Gothwaite PvP castle group former (docs/GOTHWAITE_PVP_MODE.md 6.3). It is the mode's own simple former, not the
/// world group coordinator, which the mode suppresses. A ready castle bot draws a shape and waits in the pool. A non-solo
/// bot leads a new open group, or joins one within the leader's level band (same guild, then an allied guild, then
/// strangers, with a healer before a second non-healer). The real <see cref="Group"/> exists once a second bot is in.
/// A group is due to leave when it is full, or at its wait limit with its minimum; a smaller group disbands and its ready members
/// return to the pool. Departures are staggered: one due group or solo leaves per interval (<see cref="GothwaitePvpDepartureGate"/>),
/// the others wait in the castle and keep their place. A departing group walks to the castle exit (the driver does the walking).
/// All state sits behind one lock. <see cref="TickIfDue"/> runs at most once per <see cref="TickMilliseconds"/>, from any
/// mode bot's turn, and an error in it is logged, never thrown into the bot's turn.
/// </summary>
public static class GothwaitePvpGroupFormer
{
    public const int TickMilliseconds = 2_000;
    /// <summary>How long an open group waits for members before its minimum decides (leave, or disband).</summary>
    public const int WaitMilliseconds = 90_000;
    /// <summary>How long a departing group may take to reach the castle exit before it gives up and goes home.</summary>
    public const int DepartureLimitMilliseconds = 180_000;

    private static readonly Logger Log = LoggerManager.Create(typeof(GothwaitePvpGroupFormer));
    private static readonly object Sync = new();
    private static long _nextTick;
    private static long _nextNumber;
    private static readonly Dictionary<GameBot, Entry> Entries = new();
    private static readonly List<Record> Records = new();
    // The departure stagger (docs/GOTHWAITE_PVP_MODE.md section 6.3): one departing group or solo per interval, across the former.
    private static readonly GothwaitePvpDepartureGate Departures = new();
    // Why a bot's group ended while it was not leaving it (read once by the bot's driver with TakeEndReason).
    private static readonly Dictionary<GameBot, string> EndReasons = new();

    /// <summary>
    /// What a driver needs to know about its bot's group. Stage 6 adds the group's doctrine (read from its members when it departed)
    /// and the members and healers it departed with, which the retreat rule compares with the living ones.
    /// </summary>
    public readonly record struct Membership(string Id, GothwaitePvpShape Shape, GameBot Leader, bool IsLeader, bool Departing, bool Arrived,
        RvrDoctrine Doctrine = null, int FormedMembers = 0, int FormedHealers = 0);

    // One bot in the former: waiting in the pool (Record null) or placed in a group or solo record.
    private sealed class Entry
    {
        public Entry(GameBot bot, GothwaitePvpShape shape, int cycle, long readyTick)
        {
            Bot = bot;
            Shape = shape;
            Cycle = cycle;
            ReadyTick = readyTick;
        }

        public GameBot Bot { get; }
        public GothwaitePvpShape Shape { get; }
        public int Cycle { get; }
        public long ReadyTick { get; set; }
        /// <summary>The bot's castle step is Ready. A released bot is not ready until it has rested and buffed again.</summary>
        public bool Ready { get; set; } = true;
        public Record Record { get; set; }
    }

    // A group (or a solo departure): its leader, its members, its real group once two bots are in, and its clock.
    private sealed class Record
    {
        public string Id { get; init; }
        public GothwaitePvpShape Shape { get; init; }
        public int Target { get; init; }
        public int Minimum { get; init; }
        /// <summary>The leader. A departed group passes it on when its leader returns or dies (stage 3).</summary>
        public GameBot Leader { get; set; }
        public long ExpiresAt { get; init; }
        public long DepartedAt { get; set; }
        public bool Departing { get; set; }
        public bool Arrived { get; set; }
        /// <summary>The tick the group became due to leave (full, or past its wait with its minimum); null while it is not due. Keeps its place in the queue.</summary>
        public long? DueSince { get; set; }
        public Group Real { get; set; }
        public readonly List<Entry> Members = new();
        /// <summary>Stage 6: the doctrine read from the members at departure, and the members and healers the group departed with.</summary>
        public RvrDoctrine Doctrine { get; set; }
        public int FormedMembers { get; set; }
        public int FormedHealers { get; set; }
    }

    /// <summary>
    /// The former's service-phase hook (NpcService.Tick, before the brains think in parallel): groups are created and
    /// changed here on one thread. Returns at once while the mode is off.
    /// </summary>
    public static void PrepareTick()
    {
        if (!GothwaitePvpMode.IsActive)
            return;
        // Once a real minute the load meter writes its GOTH_PVP_LOAD line (a read of one value otherwise).
        GothwaitePvpLoadMeter.PublishIfDue(Environment.TickCount64, GothwaitePvpStatus.CountOnline);
        TickIfDue(GameLoop.GameLoopTime);
    }

    /// <summary>Runs the former's tick if it is due (at most once per interval).</summary>
    public static void TickIfDue(long now)
    {
        lock (Sync)
        {
            if (now < _nextTick)
                return;
            _nextTick = now + TickMilliseconds;
            using (BotThinkProfiler.Measure(BotThinkPhase.GothwaitePvpFormer))
            using (GothwaitePvpLoadMeter.MeasureFormer())
            {
                try
                {
                    Prune(now);
                    Match(now);
                    DecideRecords(now);
                    ReleaseDeparture(now);
                }
                catch (Exception exception)
                {
                    Log.Error($"GOTH_PVP_FORMER_FAILED: the castle group tick failed; it retries in {TickMilliseconds} ms.", exception);
                }
            }
        }
    }

    /// <summary>
    /// A ready castle bot offers itself with the shape it drew for this ready cycle. A solo waits in the pool with the others; the departure
    /// stagger releases it (one departure per interval). A bot already in the former only becomes ready again. Called every ready turn, so
    /// it is idempotent.
    /// </summary>
    public static void Offer(GameBot bot, GothwaitePvpShape shape, int cycle, long now)
    {
        if (bot == null)
            return;
        lock (Sync)
        {
            if (Entries.TryGetValue(bot, out Entry existing))
            {
                existing.Ready = true;
                return;
            }
            Entries[bot] = new Entry(bot, shape, cycle, now);
        }
    }

    /// <summary>The bot is not ready (it was released and rests): it leaves the pool, and its group stops counting it.</summary>
    public static void Withdraw(GameBot bot)
    {
        lock (Sync)
        {
            if (bot == null || !Entries.TryGetValue(bot, out Entry entry))
                return;
            if (entry.Record == null)
                Entries.Remove(bot);
            else
                entry.Ready = false;
        }
    }

    /// <summary>
    /// The bot leaves its group for good: it was released with its leader outside the castle, or it was not ready when its
    /// group departed. A leader leaving dissolves the group; the other members go home or return to the pool.
    /// </summary>
    public static void Leave(GameBot bot, string reason)
    {
        lock (Sync)
        {
            if (bot == null || !Entries.TryGetValue(bot, out Entry entry))
                return;
            entry.Ready = false;
            Record record = entry.Record;
            if (record == null)
            {
                Entries.Remove(bot);
                return;
            }
            if (record.Leader == bot)
            {
                Close(record, GameLoop.GameLoopTime, reason, except: bot);
                return;
            }
            RemoveFromRecord(record, entry);
            Entries.Remove(bot);
        }
    }

    /// <summary>
    /// The bot returns to the castle from the arena (stage 3). A follower leaves its group. A leader hands the group to the
    /// first alive member in the arena, in join order, and its real group follows the new leader; with none, the group ends
    /// and its other members learn why (<see cref="TakeEndReason"/>).
    /// </summary>
    public static void Retire(GameBot bot, string reason)
    {
        lock (Sync)
        {
            if (bot == null || !Entries.TryGetValue(bot, out Entry entry))
                return;
            entry.Ready = false;
            Record record = entry.Record;
            if (record == null)
            {
                Entries.Remove(bot);
                return;
            }
            long now = GameLoop.GameLoopTime;
            if (record.Leader == bot && !PromoteFrom(record, now, reason, bot))
            {
                Close(record, now, reason, except: bot);
                Entries.Remove(bot);
                return;
            }
            RemoveFromRecord(record, entry);
            Entries.Remove(bot);
        }
    }

    /// <summary>
    /// The reason a bot's group ended while the bot was not leaving it: leader_lost, or wiped for any other end. Read once;
    /// null when there is none.
    /// </summary>
    public static string TakeEndReason(GameBot bot)
    {
        lock (Sync)
        {
            return bot != null && EndReasons.Remove(bot, out string reason) ? reason : null;
        }
    }

    /// <summary>
    /// The lowest level among the living members of the bot's group, the bot included. The bot's own level outside a group.
    /// A leader keeps the group inside the edge of its lowest member, so no follower is left beyond its own edge.
    /// </summary>
    public static int LowestAliveLevel(GameBot bot)
    {
        if (bot == null)
            return 0;
        lock (Sync)
        {
            int lowest = bot.Level;
            if (Entries.TryGetValue(bot, out Entry entry) && entry.Record is { } record)
                foreach (Entry member in record.Members)
                    if (member.Bot.IsAlive && IsActive(member.Bot) && member.Bot.Level < lowest)
                        lowest = member.Bot.Level;
            return lowest;
        }
    }

    /// <summary>
    /// The living members of the bot's group other than the bot: all of them, and those within <paramref name="nearRange"/>.
    /// Zero for a bot that is in no group.
    /// </summary>
    public static (int Others, int Near) CountAliveOthers(GameBot bot, int nearRange)
    {
        lock (Sync)
        {
            if (bot == null || !Entries.TryGetValue(bot, out Entry entry) || entry.Record is not { } record)
                return (0, 0);
            int others = 0, near = 0;
            foreach (Entry member in record.Members)
            {
                if (member.Bot == bot || !member.Bot.IsAlive || !IsActive(member.Bot))
                    continue;
                others++;
                if (member.Bot.IsWithinRadius(bot, nearRange))
                    near++;
            }
            return (others, near);
        }
    }

    /// <summary>The living members of a group as the stage 6 rules read them: how many, how many near the bot, how many healers, and the lowest health and power.</summary>
    public readonly record struct Vitals(int Alive, int Near, int AliveHealers, int LowestHealth, int LowestPower, bool AnyPowerUser);

    /// <summary>
    /// The living members of the bot's group, the bot included (stage 6): their count, those within <paramref name="nearRange"/> of the
    /// bot (the bot not counted), the healers among them, the lowest health and power, and whether any of them uses power. A bot in
    /// no group reads its own values.
    /// </summary>
    public static Vitals VitalsOf(GameBot bot, int nearRange = 0)
    {
        if (bot == null)
            return default;
        int alive = 1, near = 0, healers = IsHealerBot(bot) ? 1 : 0;
        int lowestHealth = bot.HealthPercent, lowestPower = bot.MaxMana > 0 ? bot.ManaPercent : 100;
        bool anyPower = bot.MaxMana > 0;
        lock (Sync)
        {
            if (Entries.TryGetValue(bot, out Entry entry) && entry.Record is { } record)
                foreach (Entry member in record.Members)
                {
                    GameBot other = member.Bot;
                    if (other == bot || !other.IsAlive || !IsActive(other))
                        continue;
                    alive++;
                    if (nearRange > 0 && other.CurrentRegionID == bot.CurrentRegionID && other.IsWithinRadius(bot, nearRange))
                        near++;
                    if (IsHealerBot(other))
                        healers++;
                    lowestHealth = Math.Min(lowestHealth, other.HealthPercent);
                    if (other.MaxMana > 0)
                    {
                        anyPower = true;
                        lowestPower = Math.Min(lowestPower, other.ManaPercent);
                    }
                }
        }
        return new Vitals(alive, near, healers, lowestHealth, lowestPower, anyPower);
    }

    /// <summary>
    /// The follower's place in its group's marching order (stage 6): the index of the bot among the living followers sorted by
    /// <see cref="GothwaitePvpTactics.MarchRank"/> then id, and their count. (-1, 0) for a leader or a bot in no departed group.
    /// </summary>
    public static (int Slot, int Count) MarchSlot(GameBot bot)
    {
        lock (Sync)
        {
            if (bot == null || !Entries.TryGetValue(bot, out Entry entry) || entry.Record is not { } record || record.Leader == bot)
                return (-1, 0);
            GameBot[] followers = record.Members.Select(member => member.Bot)
                .Where(member => member != record.Leader && member.IsAlive && IsActive(member))
                .OrderBy(member => GothwaitePvpTactics.MarchRank(GothwaitePvpCombat.ClassTraits(member)))
                .ThenBy(BotIdOf)
                .ToArray();
            return (Array.IndexOf(followers, bot), followers.Length);
        }
    }

    private static bool IsHealerBot(GameBot bot) =>
        bot.CharacterClass != null && GothwaitePvpCastleRules.IsHealer((eCharacterClass)bot.CharacterClass.ID);

    /// <summary>The bot's group, when it is in one (gathering, departing or arrived). False for a pooled or unplaced bot.</summary>
    public static bool TryGetMembership(GameBot bot, out Membership membership)
    {
        lock (Sync)
        {
            if (bot != null && Entries.TryGetValue(bot, out Entry entry) && entry.Record is { } record)
            {
                membership = new Membership(record.Id, record.Shape, record.Leader, record.Leader == bot, record.Departing, record.Arrived,
                    record.Doctrine, record.FormedMembers, record.FormedHealers);
                return true;
            }
        }
        membership = default;
        return false;
    }

    /// <summary>
    /// A read-only census for the status command (stage 4): the open groups still gathering and their members, the bots waiting in the
    /// castle pool, the departed groups by shape, and the members of the groups of two or more for their average size. One lock, once per call.
    /// </summary>
    public static GothwaitePvpGroupCensus Census()
    {
        lock (Sync)
        {
            int forming = 0, formingMembers = 0, multiGroups = 0, multiMembers = 0;
            var departed = new int[4];
            foreach (Record record in Records)
            {
                int size = record.Members.Count;
                if (record.Departing)
                    departed[(int)record.Shape]++;
                else
                {
                    forming++;
                    formingMembers += size;
                }
                if (record.Shape != GothwaitePvpShape.Solo)
                {
                    multiGroups++;
                    multiMembers += size;
                }
            }
            int waiting = Entries.Values.Count(entry => entry.Record == null && entry.Ready);
            return new GothwaitePvpGroupCensus(forming, formingMembers, waiting, departed, multiGroups, multiMembers);
        }
    }

    /// <summary>The leader reached the castle exit: its group holds there. Logged once per group.</summary>
    public static void MarkArrived(GameBot leader)
    {
        lock (Sync)
        {
            if (leader == null || !Entries.TryGetValue(leader, out Entry entry) || entry.Record is not { } record ||
                record.Leader != leader || record.Arrived)
                return;
            record.Arrived = true;
            Log.Info(GothwaitePvpLines.Arrive(record.Id, leader.Name, record.Members.Count));
        }
    }

    // Drops what no longer belongs: a pooled bot that is no longer eligible, a member that left its group, that left the
    // castle while gathering, or that is no longer active. A record whose leader is gone ends, unless a dead leader can hand
    // the group to a living member in the arena (stage 3).
    private static void Prune(long now)
    {
        foreach (Entry entry in Entries.Values.Where(entry => entry.Record == null).ToArray())
            if (!IsEligible(entry.Bot))
                Entries.Remove(entry.Bot);

        foreach (Record record in Records.ToArray())
        {
            if (record.Real != null && record.Leader.Group != record.Real)
                record.Real = null; // a native break-up left the leader alone; the record carries on without a real group

            if (!IsActive(record.Leader))
            {
                Close(record, now, GothwaitePvpArenaRules.ReasonLeaderLost);
                continue;
            }
            if (!record.Leader.IsAlive && !PromoteFrom(record, now, GothwaitePvpArenaRules.ReasonLeaderLost, null))
            {
                Close(record, now, GothwaitePvpArenaRules.ReasonLeaderLost);
                continue;
            }
            if (!record.Departing && !InCastle(record.Leader))
            {
                Close(record, now, "left_castle");
                continue;
            }
            if (record.Departing && !record.Arrived && now - record.DepartedAt > DepartureLimitMilliseconds)
            {
                Close(record, now, "depart_timeout");
                continue;
            }
            foreach (Entry member in record.Members.ToArray())
                if (member.Bot != record.Leader && !IsMember(record, member.Bot))
                {
                    RemoveFromRecord(record, member);
                    Entries.Remove(member.Bot);
                }
        }

        // An end reason no driver read (its bot left the world) is dropped, so the table stays small.
        foreach (GameBot bot in EndReasons.Keys.Where(bot => !IsActive(bot)).ToArray())
            EndReasons.Remove(bot);
    }

    // Pool bots that are eligible join open groups (oldest first); each leftover bot that is not a solo leads a new group.
    private static void Match(long now)
    {
        int levelBand = GothwaitePvpMode.Settings.LevelBand;
        List<Entry> pool = PoolEntries().ToList();
        foreach (Record record in Records.Where(record => !record.Departing).ToArray())
            FillFromPool(record, levelBand, pool);
        foreach (Entry entry in pool)
        {
            if (entry.Record != null || !Entries.ContainsKey(entry.Bot) || entry.Shape == GothwaitePvpShape.Solo ||
                !IsEligible(entry.Bot))
                continue;
            FillFromPool(OpenGroup(entry, now), levelBand, pool);
        }
    }

    // Every open group decides on this tick: a full group, or one past its wait with its minimum, is due to leave (it keeps the tick it
    // became due); a smaller group past its wait disbands. Nothing leaves here: ReleaseDeparture lets one due departure go per interval.
    private static void DecideRecords(long now)
    {
        foreach (Record record in Records.Where(record => !record.Departing).ToArray())
        {
            // A leader that is not ready cannot lead the group out, so no member counts as ready.
            int ready = Entries.TryGetValue(record.Leader, out Entry leader) && leader.Ready && IsReadyNow(record.Leader)
                ? record.Members.Count(member => member.Ready && IsReadyNow(member.Bot))
                : 0;
            switch (GothwaitePvpCastleRules.Decide(now, record.ExpiresAt, ready, record.Target, record.Minimum))
            {
                case GothwaitePvpFormationStep.Depart:
                    record.DueSince ??= now;
                    break;
                case GothwaitePvpFormationStep.Disband:
                    Disband(record, now, ready);
                    break;
                default:
                    record.DueSince = null;
                    break;
            }
        }
    }

    // The departure stagger (section 6.3, live-run fix 2026-10-13): one departing group or solo per interval on average, globally (two per 2-second
    // tick since the second live run). The earliest due one leaves; the others stay ready in the castle and keep their place. A solo is due from the
    // tick it became ready.
    private static void ReleaseDeparture(long now)
    {
        if (!Departures.CanRelease(now))
            return;
        var groups = Records.Where(record => !record.Departing && record.DueSince != null).ToList();
        var solos = PoolEntries().Where(entry => entry.Shape == GothwaitePvpShape.Solo && IsEligible(entry.Bot)).ToList();
        while (groups.Count + solos.Count > 0 && Departures.CanRelease(now))
        {
            var queue = new GothwaitePvpDeparture[groups.Count + solos.Count];
            for (int index = 0; index < groups.Count; index++)
                queue[index] = new GothwaitePvpDeparture(groups[index].DueSince.Value, Solo: false, BotIdOf(groups[index].Leader));
            for (int index = 0; index < solos.Count; index++)
                queue[groups.Count + index] = new GothwaitePvpDeparture(solos[index].ReadyTick, Solo: true, BotIdOf(solos[index].Bot));
            int chosen = Departures.Release(now, queue);
            if (chosen < 0)
                return;
            if (chosen < groups.Count)
            {
                Depart(groups[chosen], now);
                groups.RemoveAt(chosen);
            }
            else
            {
                DepartSolo(solos[chosen - groups.Count], now);
                solos.RemoveAt(chosen - groups.Count);
            }
        }
    }

    // A released solo departs alone: its own one-member record, logged like a group's departure.
    private static void DepartSolo(Entry entry, long now)
    {
        var solo = new Record
        {
            Id = GothwaitePvpLines.SoloId,
            Shape = GothwaitePvpShape.Solo,
            Target = 1,
            Minimum = 1,
            Leader = entry.Bot,
            ExpiresAt = now,
            DepartedAt = now,
            Departing = true,
        };
        AttachMember(solo, entry);
        Records.Add(solo);
        SetDoctrine(solo);
        LogDeparture(solo);
    }

    private static void Depart(Record record, long now)
    {
        // Members who are resting after a release stay behind: they rest and return to the pool on their own.
        foreach (Entry member in record.Members.ToArray())
            if (member.Bot != record.Leader && !(member.Ready && IsReadyNow(member.Bot)))
            {
                RemoveFromRecord(record, member);
                Entries.Remove(member.Bot);
            }
        ElectLeader(record);
        record.Departing = true;
        record.DepartedAt = now;
        SetDoctrine(record);
        LogDeparture(record);
    }

    // Stage 6: the main assist leads. The best-ranked member (a guard tank, then any melee, an archer, a caster, a healer last) takes
    // the lead before the group walks out, and the real group follows; players put a tank up front and assisted it. Logged when
    // the lead changes hands. Only members that are alive and active may lead.
    private static void ElectLeader(Record record)
    {
        GothwaitePvpLeaderCandidate[] candidates = record.Members.Select(member => new GothwaitePvpLeaderCandidate(BotIdOf(member.Bot),
            GothwaitePvpTactics.LeaderRank(GothwaitePvpCombat.ClassTraits(member.Bot)), member.Bot.IsAlive && IsActive(member.Bot))).ToArray();
        int index = GothwaitePvpTactics.ChooseLeader(candidates);
        if (index < 0)
            return;
        GameBot next = record.Members[index].Bot;
        if (next == record.Leader)
            return;
        GameBot previous = record.Leader;
        if (record.Real != null && next.Group == record.Real)
            record.Real.MakeLeader(next);
        record.Leader = next;
        GothwaitePvpStats.CountMainAssist();
        Log.Info(GothwaitePvpLines.GroupLeader(record.Id, next.Name, previous.Name, "main_assist"));
    }

    // Stage 6: what the group is, read once from the members it departs with (the world doctrine's own reading of a group's classes).
    private static void SetDoctrine(Record record)
    {
        GameBot[] bots = record.Members.Select(member => member.Bot).ToArray();
        record.Doctrine = GothwaitePvpCombat.Derive(bots, record.Leader);
        record.FormedMembers = bots.Length;
        record.FormedHealers = bots.Count(bot => bot.CharacterClass != null && GothwaitePvpCastleRules.IsHealer((eCharacterClass)bot.CharacterClass.ID));
    }

    private static void Disband(Record record, long now, int readyCount)
    {
        Log.Info(GothwaitePvpLines.Disbanded(record.Id, record.Shape, record.Members.Count, readyCount, "expired"));
        Records.Remove(record);
        Unplace(record, now);
    }

    // The group ends. Its members (but <paramref name="except"/>) learn why, for the driver's return line.
    private static void Close(Record record, long now, string reason, GameBot except = null)
    {
        Log.Info(GothwaitePvpLines.Dissolved(record.Id, record.Shape, record.Members.Count, reason));
        foreach (Entry member in record.Members)
            if (member.Bot != except)
                EndReasons[member.Bot] = EndReasonFor(reason);
        Records.Remove(record);
        Unplace(record, now);
    }

    // A lost leader ends a group as leader_lost; any other end leaves its members wiped.
    private static string EndReasonFor(string reason) =>
        reason == GothwaitePvpArenaRules.ReasonLeaderLost ? GothwaitePvpArenaRules.ReasonLeaderLost : GothwaitePvpArenaRules.ReasonWiped;

    // Hands the group to the best-ranked alive member in the arena (stage 6: a tank before a healer; ties by join order), skipping
    // the leader and <paramref name="leaving"/>. The real group's leader follows. False when no member qualifies, and the record is
    // left as it was.
    private static bool PromoteFrom(Record record, long now, string reason, GameBot leaving)
    {
        Entry[] members = record.Members.Where(member => member.Bot != record.Leader && member.Bot != leaving).ToArray();
        GothwaitePvpPromotionCandidate[] candidates = members.Select(member => new GothwaitePvpPromotionCandidate(
            BotIdOf(member.Bot), member.Bot.IsAlive && IsActive(member.Bot), OutsideCastle(member.Bot),
            GothwaitePvpTactics.LeaderRank(GothwaitePvpCombat.ClassTraits(member.Bot)))).ToArray();
        int index = GothwaitePvpArenaRules.BestAliveInArena(candidates);
        if (index < 0)
            return false;
        GameBot previous = record.Leader;
        GameBot next = members[index].Bot;
        if (record.Real != null && next.Group == record.Real)
            record.Real.MakeLeader(next);
        record.Leader = next;
        Log.Info(GothwaitePvpLines.GroupLeader(record.Id, next.Name, previous.Name, reason));
        return true;
    }

    // The group ends: its real group is disbanded and each member leaves it. A member that is ready and eligible returns
    // to the pool with its shape; any other member is dropped and comes back when its castle step is ready again.
    private static void Unplace(Record record, long now)
    {
        if (record.Real != null)
        {
            record.Real.DisbandGroup();
            record.Real = null;
        }
        foreach (Entry member in record.Members.ToArray())
        {
            member.Record = null;
            // A solo is never pooled: its next ready turn offers it again and it departs alone.
            if (member.Shape != GothwaitePvpShape.Solo && member.Ready && IsEligible(member.Bot))
            {
                member.ReadyTick = now;
                Entries[member.Bot] = member;
            }
            else
            {
                Entries.Remove(member.Bot);
            }
        }
        record.Members.Clear();
    }

    // Opens a group led by the entry's bot; the real group is made later, when the second bot joins.
    private static Record OpenGroup(Entry leader, long now)
    {
        var record = new Record
        {
            Id = (++_nextNumber).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Shape = leader.Shape,
            Target = GothwaitePvpShapes.TargetSize(leader.Shape, BotIdOf(leader.Bot), leader.Cycle),
            Minimum = GothwaitePvpShapes.MinimumSize(leader.Shape),
            Leader = leader.Bot,
            ExpiresAt = now + WaitMilliseconds,
        };
        AttachMember(record, leader);
        Records.Add(record);
        return record;
    }

    // Fills an open group from the pool snapshot of this tick, one best-ranked candidate at a time.
    private static void FillFromPool(Record record, int levelBand, List<Entry> pool)
    {
        // A due group (full, or past its wait) takes no more members: it keeps its place in the departure queue.
        if (record.Departing || record.DueSince != null || record.Members.Count >= record.Target || record.Leader == null)
            return;
        var byId = new Dictionary<long, Entry>();
        var candidates = new List<GothwaitePvpCandidate>();
        foreach (Entry entry in pool)
        {
            if (entry.Record != null || entry.Shape == GothwaitePvpShape.Solo || !IsEligible(entry.Bot) ||
                !GothwaitePvpCastleRules.InLevelBand(record.Leader.Level, entry.Bot.Level, levelBand))
                continue;
            long id = BotIdOf(entry.Bot);
            byId[id] = entry;
            candidates.Add(new GothwaitePvpCandidate(id, MemberOf(entry.Bot), entry.ReadyTick));
        }
        GothwaitePvpMember leader = MemberOf(record.Leader);
        while (record.Members.Count < record.Target && candidates.Count > 0)
        {
            var members = record.Members.Select(member => MemberOf(member.Bot)).ToArray();
            IReadOnlyList<GothwaitePvpCandidate> ranked = GothwaitePvpCastleRules.Rank(leader, members, candidates, levelBand);
            if (ranked.Count == 0)
                break;
            GothwaitePvpCandidate next = ranked[0];
            candidates.RemoveAll(candidate => candidate.BotId == next.BotId);
            if (byId.TryGetValue(next.BotId, out Entry chosen))
                TryJoin(record, chosen);
        }
    }

    // A bot joins a gathering group. The real group is made when the second bot joins; a native refusal leaves the bot in the pool.
    private static bool TryJoin(Record record, Entry entry)
    {
        if (record.Real == null)
        {
            if (record.Members.Count != 1)
                return false;
            var created = new Group(record.Leader);
            GroupMgr.AddGroup(created);
            if (!created.AddMember(record.Leader))
            {
                GroupMgr.RemoveGroup(created);
                return false;
            }
            record.Real = created;
        }
        if (!record.Real.AddMember(entry.Bot))
            return false;
        AttachMember(record, entry);
        return true;
    }

    private static void AttachMember(Record record, Entry entry)
    {
        record.Members.Add(entry);
        entry.Record = record;
        entry.Ready = true;
        Entries[entry.Bot] = entry;
    }

    private static void RemoveFromRecord(Record record, Entry entry)
    {
        if (record.Real != null)
        {
            record.Real.RemoveMember(entry.Bot, retainSingleRemainingMember: true);
            if (record.Real.MemberCount == 0)
                record.Real = null;
        }
        record.Members.Remove(entry);
        entry.Record = null;
    }

    // A non-leader member stays only while it is in the record's real group (and, while gathering, in the castle).
    private static bool IsMember(Record record, GameBot bot)
    {
        if (record.Real == null || !IsActive(bot) || bot.Group != record.Real)
            return false;
        return record.Departing || InCastle(bot);
    }

    // Pool bots: waiting, not in a group, alive, active and inside the castle.
    private static IEnumerable<Entry> PoolEntries() => Entries.Values
        .Where(entry => entry.Record == null && entry.Ready)
        .OrderBy(entry => entry.ReadyTick)
        .ThenBy(entry => BotIdOf(entry.Bot));

    private static bool IsEligible(GameBot bot) => IsReadyNow(bot) && bot.Group == null && !bot.IsPlayerLedGroup;

    private static bool IsReadyNow(GameBot bot) => IsActive(bot) && bot.IsAlive && InCastle(bot);

    private static bool IsActive(GameBot bot) =>
        bot != null && bot.ObjectState == GameObject.eObjectState.Active && !bot.IsPlayerLedGroup;

    private static bool InCastle(GameBot bot) => GothwaitePvpMode.Arena.InCastle(bot.CurrentRegionID, bot.X, bot.Y);

    // In the arena's region and outside the castle circle: where a group member can take over a group.
    private static bool OutsideCastle(GameBot bot) =>
        bot.CurrentRegionID == GothwaitePvpMode.Arena.RegionId && !InCastle(bot);

    private static long BotIdOf(GameBot bot) => bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;

    private static GothwaitePvpMember MemberOf(GameBot bot) => new(
        bot.Level,
        bot.CharacterClass != null && GothwaitePvpCastleRules.IsHealer((eCharacterClass)bot.CharacterClass.ID),
        GuildOf(bot));

    // The guild identity the former compares. Dummy and garrison guilds never count as a guild.
    private static GothwaitePvpGuild GuildOf(GameBot bot)
    {
        Guild guild = bot.Guild;
        if (guild == null || !PvpCombatant.IsRealGuild(guild))
            return default;
        return new GothwaitePvpGuild(guild.GuildID, guild.alliance?.DbAlliance?.ObjectId ?? guild.AllianceId);
    }

    private static void LogDeparture(Record record)
    {
        GameBot[] bots = record.Members.Select(member => member.Bot).ToArray();
        int minLevel = bots.Min(bot => (int)bot.Level);
        int maxLevel = bots.Max(bot => (int)bot.Level);
        Log.Info(GothwaitePvpLines.Formed(record.Id, record.Shape, bots.Length, minLevel, maxLevel,
            bots.Select(bot => GlobalConstants.RealmToName(bot.Realm)).Distinct(), bots.Select(bot => bot.Name)));
        Log.Info(GothwaitePvpLines.Depart(record.Id, record.Leader.Name, bots.Length, record.Doctrine?.Kind.ToString() ?? "none"));
    }
}
