using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.Scripts;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Bounded autonomous-gamebot population for the battleground campaign.
/// Only RvR-tour bots standing on a frontier (regions 1, 100, 200) and fitting a
/// bracket are assigned to it. Assigned bots buy the free battlegrounds medallion
/// from a real merchant, board a real porter, then fight the keep through
/// <see cref="AutonomousBattlegroundDriver"/>. Players, companions and temporary
/// helpers are never participants. Assignments live in memory only.
/// </summary>
public static class AutonomousBattlegroundParticipation
{
    public const int MaximumAutonomousPerMap = 24;
    public const int SharedActorCap = 40;
    public const int ReconcileIntervalMilliseconds = 60_000;
    public const int StuckMilliseconds = 5 * 60_000;
    /// <summary>Wall-clock floor for the stuck and no-turn rules, so a fast world speed cannot eject bots in seconds.</summary>
    public const long StuckWallMilliseconds = 90_000;
    public const int AssignmentTimeoutMilliseconds = 15 * 60_000;
    /// <summary>An open-BG unit musters as a group before it boards, so it gets longer to reach its porter.</summary>
    public const int OpenAssignmentTimeoutMilliseconds = 30 * 60_000;
    /// <summary>A relieved open-BG bot walks toward its own portal keep for at most this long before it leaves.</summary>
    public const int ReliefWalkMilliseconds = 5 * 60_000;
    /// <summary>
    /// An open-BG group waits this long (game time) after its first member stood ready in the boarding circle; then
    /// the ready members board without the stragglers, which leave the unit (the roster refills a group below its minimum).
    /// </summary>
    public const int OpenBoardingWaitMilliseconds = 3 * 60_000;
    public const int DepartureLimit = 8;
    public const string BattlegroundMedallionId = "battlegrounds_necklace";
    private const int DirectorMaximum = 24;
    public const int MerchantReach = 3000;
    private const int ProgressDistance = 96;
    private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousBattlegroundParticipation));
    private static readonly object Sync = new();
    private static readonly Dictionary<GameBot, Entry> Entries = new(ReferenceEqualityComparer.Instance);
    private static ECSGameTimer _timer;

    public enum LeaveReason { Graduated, TourEnded, Unassigned, Stuck, NoTurn, Relieved }

    private sealed class Entry
    {
        public GameBot Bot;
        public BattlegroundDefinition Definition;
        public string Group = "solo";
        public bool Entered;
        public LeaveReason? Leaving;
        public long AssignedTick;
        // Game-loop and wall-clock (Environment.TickCount64) stamps of the last real progress.
        public long LastProgressTick;
        public long LastProgressWall;
        // Driver turns taken inside the battleground; the stuck rule applies only after one.
        public int DriverTurns;
        public HashSet<string> IdleReasons;
        // open BG: the roster unit (0 for every bracket entry), the group it was formed as (null for a solo),
        // when it entered, and the relief walk home.
        public int Unit;
        public Group GroupRef;
        public long EnteredTick;
        public long LeaveStartTick;
        public Point3D ReliefHome;
        // open BG: game time this member first stood ready in its porter's boarding circle (0 = not yet).
        public long ReadySinceTick;
    }

    /// <summary>One open-BG crew member for the roster and the director; <paramref name="Group"/> is the unit's label.</summary>
    public readonly record struct OpenCrewMember(GameBot Bot, int Unit, bool Entered, bool Leaving, long EnteredTick, string Group = null);

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        lock (Sync)
        {
            _timer?.Stop();
            _timer = null;
            Entries.Clear();
        }
        if (!BattlegroundCampaignPolicy.IsEnabled) return;
        _timer = new ECSGameTimer(null, Reconcile, ReconcileIntervalMilliseconds);
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        lock (Sync)
        {
            _timer?.Stop();
            _timer = null;
            Entries.Clear();
        }
    }

    public static string Describe(LeaveReason reason) => reason switch
    {
        LeaveReason.Graduated => "graduated",
        LeaveReason.TourEnded => "tour_ended",
        LeaveReason.Stuck => "stuck",
        LeaveReason.NoTurn => "no_turn",
        LeaveReason.Relieved => "relieved",
        _ => "unassigned",
    };

    /// <summary>
    /// A participant that has taken driver turns and has recorded no real progress for both five game
    /// minutes and ninety wall-clock seconds. Without a turn it is not stuck; see <see cref="IsNoTurn"/>.
    /// </summary>
    public static bool IsStuck(long gameNow, long gameLast, long wallNow, long wallLast, bool hadTurn) =>
        hadTurn && SilentForBothClocks(gameNow, gameLast, wallNow, wallLast);

    /// <summary>A participant that never took a driver turn within the same two-clock window.</summary>
    public static bool IsNoTurn(long gameNow, long gameLast, long wallNow, long wallLast, bool hadTurn) =>
        !hadTurn && SilentForBothClocks(gameNow, gameLast, wallNow, wallLast);

    private static bool SilentForBothClocks(long gameNow, long gameLast, long wallNow, long wallLast) =>
        gameNow - gameLast > StuckMilliseconds && wallNow - wallLast >= StuckWallMilliseconds;

    /// <summary>Whether the last real progress is still inside the stuck wall-clock window (the complement of its wall test).</summary>
    public static bool IsRecentProgress(long wallNow, long wallLast) => wallNow - wallLast < StuckWallMilliseconds;

    /// <summary>Square-distance reach test in the 2D plane, used for merchants around a porter.</summary>
    public static bool IsWithinReach2D(int dx, int dy, int reach) =>
        (long)dx * dx + (long)dy * dy <= (long)reach * reach;

    public static bool IsFrontierRegion(ushort region) => region is 1 or 100 or 200;

    private static bool IsEligible(byte level, int realmLevel, bool rvrTour, bool playerLed, bool relic, BattlegroundDefinition definition) =>
        definition != null && rvrTour && !playerLed && !relic &&
        level >= definition.MinLevel && level <= definition.MaxLevel &&
        (definition.MaxRealmLevel == 0 || realmLevel < definition.MaxRealmLevel);

    /// <summary>True only for an RvR-tour bot on a frontier that fits the bracket.</summary>
    public static bool IsCandidate(byte level, int realmLevel, bool rvrTour, ushort region, bool playerLed, bool relic, BattlegroundDefinition d) =>
        IsFrontierRegion(region) && IsEligible(level, realmLevel, rvrTour, playerLed, relic, d);

    /// <summary>
    /// Why an assigned or present bot must leave its bracket, or null to keep it.
    /// A character above the bracket or at its Realm Rank ceiling graduates.
    /// </summary>
    public static LeaveReason? LeaveReasonFor(byte level, int realmLevel, bool rvrTour, bool playerLed, bool relic, BattlegroundDefinition d)
    {
        if (d == null) return LeaveReason.Unassigned;
        if (level > d.MaxLevel || d.MaxRealmLevel != 0 && realmLevel >= d.MaxRealmLevel) return LeaveReason.Graduated;
        if (!rvrTour) return LeaveReason.TourEnded;
        return IsEligible(level, realmLevel, rvrTour, playerLed, relic, d) ? null : LeaveReason.Unassigned;
    }

    /// <summary>
    /// Why an open-BG crew member must leave, or null to keep it. No graduation and no tour end: the roster relieves.
    /// It leaves when the open BG is switched off, when a player leads it, or when it carries a relic or left its unit
    /// before boarding (<see cref="LeftOpenUnitBeforeEntry"/>).
    /// </summary>
    public static LeaveReason? OpenLeaveReasonFor(bool openEnabled, bool playerLed, bool relic, bool leftUnitBeforeEntry) =>
        !openEnabled || playerLed || relic || leftUnitBeforeEntry ? LeaveReason.Unassigned : null;

    /// <summary>
    /// A real leave before boarding: the member (or a solo) is now in a party that is not its unit's group. Losing
    /// its group is not a leave: a member removed by death, position recovery or the muster stays in its unit and
    /// travels to the porter alone.
    /// </summary>
    public static bool LeftOpenUnitBeforeEntry(bool entered, bool inAGroup, bool inItsUnitGroup) =>
        !entered && inAGroup && !inItsUnitGroup;

    /// <summary>The open BG has its own hard cap (open_bg_max_bots); the bracket's 24 per map does not apply.</summary>
    public static bool AdmitsOpenUnit(int memberCount, int openCrew, int cap) =>
        memberCount > 0 && openCrew + memberCount <= cap;

    /// <summary>Only an Open definition with a roster unit is an open-BG entry; every bracket entry has unit 0.</summary>
    public static bool IsOpenUnit(BattlegroundDefinition definition, int unit) =>
        definition?.Kind == BattlegroundDefinitionKind.Open && unit > 0;

    public static int AssignmentTimeoutFor(BattlegroundDefinition definition) =>
        definition?.Kind == BattlegroundDefinitionKind.Open ? OpenAssignmentTimeoutMilliseconds : AssignmentTimeoutMilliseconds;

    /// <summary>A group is admitted only when every member is eligible and the map has room for all of them.</summary>
    public static bool AdmitsGroup(int memberCount, int eligibleCount, int assignedOnMap) =>
        memberCount > 0 && eligibleCount == memberCount && assignedOnMap + memberCount <= MaximumAutonomousPerMap;

    /// <summary>Director actors allowed beside the autonomous participants already present in the region.</summary>
    public static int DirectorCap(int autonomousPresent) => Math.Clamp(SharedActorCap - autonomousPresent, 0, DirectorMaximum);

    public static bool HasBattlegroundMedallion(GameBot bot) =>
        bot?.Inventory?.GetItem(eInventorySlot.Mythical)?.Id_nb == BattlegroundMedallionId;

    /// <summary>
    /// Real merchants that sell the medallion within <see cref="MerchantReach"/> (2D) of the porter, nearest first.
    /// Scans the region's object list rather than the sub-zone radius index: that index is empty or stale
    /// until objects have been relocated, so a startup check missed a merchant standing 500 units away.
    /// </summary>
    public static GameMerchant[] MedallionMerchantsInReach(OFTeleporter porter)
    {
        if (porter == null) return Array.Empty<GameMerchant>();
        return WorldMgr.GetNPCsFromRegion(porter.CurrentRegionID).OfType<GameMerchant>()
            .Where(npc => npc.ObjectState == GameObject.eObjectState.Active &&
                IsWithinReach2D(npc.X - porter.X, npc.Y - porter.Y, MerchantReach) && SellsMedallionItem(npc))
            .OrderBy(npc => (long)(npc.X - porter.X) * (npc.X - porter.X) + (long)(npc.Y - porter.Y) * (npc.Y - porter.Y))
            .ToArray();
    }

    private static bool SellsMedallionItem(GameMerchant merchant) =>
        merchant.TradeItems?.GetAllItems().Values.OfType<DbItemTemplate>().Any(item => item.Id_nb == BattlegroundMedallionId) == true;

    /// <summary>A real merchant within reach of the porter sells the medallion (the free source players use).</summary>
    public static bool SellsMedallion(OFTeleporter porter) => MedallionMerchantsInReach(porter).Length > 0;

    /// <summary>Active porters in the region whose medallion merchant is in reach. Empty where no legal source exists.</summary>
    public static OFTeleporter[] SourcePorters(ushort region) =>
        AutonomousFrontierTransport.PortersInRegion(region).Where(SellsMedallion).ToArray();

    /// <summary>A bot admitted to the campaign and standing inside its battleground.</summary>
    public static bool IsParticipant(GameNPC npc)
    {
        if (npc is not GameBot { IsAutonomousWorldBot: true } bot) return false;
        // Cheap region test first: this runs on every movement step of every autonomous bot.
        if (BattlegroundCampaignCatalog.Find(bot.CurrentRegionID) == null) return false;
        lock (Sync)
            return Entries.TryGetValue(bot, out Entry entry) && entry.Entered;
    }

    /// <summary>Assigned to a bracket and travelling to its porter, not yet inside.</summary>
    public static bool IsAssigned(GameBot bot)
    {
        lock (Sync)
            return bot != null && Entries.TryGetValue(bot, out Entry entry) && !entry.Entered && entry.Leaving == null;
    }

    /// <summary>
    /// Whether a bot may enlist in a world RvR event (siege, defence, carrier hunt): never while it is assigned to a
    /// battleground and still travelling, nor while its group is an open-BG roster unit. Such a force would be
    /// registered for a frontier siege while its members board for the battleground.
    /// </summary>
    public static bool MayEnlistInWorldRvrEvents(bool assignedToBattleground, bool openBattlegroundParty) =>
        !assignedToBattleground && !openBattlegroundParty;

    public static bool MayEnlistInWorldRvrEvents(GameBot bot) =>
        MayEnlistInWorldRvrEvents(IsAssigned(bot), AutonomousBotGroupCoordinator.IsOpenBattlegroundParty(bot?.Group));

    public static bool IsLeaving(GameBot bot)
    {
        lock (Sync)
            return bot != null && Entries.TryGetValue(bot, out Entry entry) && entry.Leaving != null;
    }

    /// <summary>Assigned to or standing in any campaign battleground, bracket or open.</summary>
    public static bool IsInAnyBattleground(GameBot bot) =>
        bot != null && (BattlegroundCampaignCatalog.Find(bot.CurrentRegionID) != null || IsRegistered(bot));

    /// <summary>A member of the open BG crew (travelling, inside or leaving).</summary>
    public static bool IsOpenCrew(GameBot bot)
    {
        lock (Sync)
            return bot != null && Entries.TryGetValue(bot, out Entry entry) && IsOpenUnit(entry.Definition, entry.Unit);
    }

    /// <summary>Every open-BG crew member. Bracket entries are never part of it.</summary>
    public static OpenCrewMember[] OpenCrew()
    {
        lock (Sync)
            return Entries.Values.Where(entry => IsOpenUnit(entry.Definition, entry.Unit))
                .Select(entry => new OpenCrewMember(entry.Bot, entry.Unit, entry.Entered, entry.Leaving != null, entry.EnteredTick, entry.Group))
                .ToArray();
    }

    /// <summary>
    /// Sends every open-BG crew member out now (open BG mode switch): bots inside leave straight from where they
    /// stand, bots still travelling are released. A bot whose exit fails stays marked and the next pass retries it.
    /// Returns the number of bots let go.
    /// </summary>
    public static int SendOutOpenCrew()
    {
        Entry[] crew;
        lock (Sync) crew = Entries.Values.Where(entry => IsOpenUnit(entry.Definition, entry.Unit)).ToArray();
        int sent = 0;
        foreach (Entry entry in crew)
        {
            if (!entry.Entered)
            {
                lock (Sync) Entries.Remove(entry.Bot);
                Log.Info($"AUTONOMOUS_BG_LEFT region={entry.Definition.RegionId} bot=\"{entry.Bot.Name}\" reason={Describe(LeaveReason.Unassigned)}");
                sent++;
                continue;
            }
            lock (Sync)
            {
                entry.Leaving = LeaveReason.Unassigned;
                entry.ReliefHome = null;
            }
            if (TryFinishLeaving(entry.Bot)) sent++;
        }
        return sent;
    }

    /// <summary>The open-BG roster unit of a crew member, or 0 for a bracket participant or any other bot.</summary>
    public static int OpenUnitOf(GameBot bot)
    {
        lock (Sync)
            return bot != null && Entries.TryGetValue(bot, out Entry entry) && IsOpenUnit(entry.Definition, entry.Unit) ? entry.Unit : 0;
    }

    /// <summary>The label (group id or "solo") an open-BG roster unit was registered with; "refill" when no member is left.</summary>
    public static string OpenUnitLabel(int unit)
    {
        lock (Sync)
            return Entries.Values.FirstOrDefault(entry => entry.Unit == unit && IsOpenUnit(entry.Definition, entry.Unit))?.Group ?? "refill";
    }

    /// <summary>An open-BG crew member assigned and still travelling to its porter (not inside, not leaving).</summary>
    public static bool IsTravellingOpenCrew(GameBot bot)
    {
        lock (Sync)
            return bot != null && Entries.TryGetValue(bot, out Entry entry) &&
                WorldRvrSuspended(IsOpenUnit(entry.Definition, entry.Unit), entry.Entered, entry.Leaving != null);
    }

    /// <summary>
    /// Live run 0.242.0: once the roster assigned a bot to an open-BG unit, the world RvR layer kept driving it until
    /// it boarded (guild keep recalls, observe/retreat runs, frontier hunts), and the unit dropped it as a straggler.
    /// From the assignment until it boards, a crew member is out of the world RvR layer: no event-layer orders, guild
    /// keep recall, doctrine (observe, retreat, stealth hunt) or opportunity hunting; only self-defence and the walk
    /// to its porter. Inside the battleground its driver owns it; a leaving bot is no longer travelling.
    /// </summary>
    public static bool WorldRvrSuspended(bool openUnit, bool entered, bool leaving) => openUnit && !entered && !leaving;

    /// <summary>Open-BG crew members against the open cap, including those still travelling or leaving.</summary>
    public static int OpenCrewCount()
    {
        lock (Sync)
            return Entries.Values.Count(entry => IsOpenUnit(entry.Definition, entry.Unit));
    }

    /// <summary>
    /// Registers one open-BG roster unit (a group or a solo). The members then travel like any assigned participant:
    /// medallion, porter, and <see cref="Depart"/> boards them for the open BG. Refused when a member is registered already.
    /// </summary>
    public static bool AssignOpenUnit(int unit, string label, IReadOnlyList<GameBot> members, Group group, long now)
    {
        // Gothwaite PvP mode: a unit is assigned to the open battleground only when every member is reserved (section 5.4).
        if (GothwaitePvpRules.RefusesOpenUnit(members)) return false;
        BattlegroundDefinition open = BattlegroundCampaignCatalog.FindOpen();
        if (!IsOpenUnit(open, unit) || members == null || members.Count == 0 || members.Any(member => member == null)) return false;
        lock (Sync)
        {
            if (members.Any(Entries.ContainsKey)) return false;
            foreach (GameBot member in members)
                Entries[member] = new Entry { Bot = member, Definition = open, Group = label ?? "solo", Unit = unit, GroupRef = group,
                    AssignedTick = now, LastProgressTick = now, LastProgressWall = Environment.TickCount64 };
        }
        foreach (GameBot member in members)
            Log.Info($"AUTONOMOUS_BG_ASSIGNED region={open.RegionId} bot=\"{member.Name}\" level={member.Level} group=\"{label}\" open_unit={unit}");
        return true;
    }

    /// <summary>
    /// Relieves one open-BG unit: members inside leave as <see cref="LeaveReason.Relieved"/> after walking to their
    /// own portal keep; members still travelling are released at once. Returns the relieved bots.
    /// </summary>
    public static GameBot[] RelieveOpenUnit(int unit, long now) => RelieveOpen(unit, null, now);

    /// <summary>
    /// Relieves only these members of an open-BG unit (a unit split because the keep's owner guild changed under it):
    /// like <see cref="RelieveOpenUnit"/>, and each also leaves the unit's group, because the keep's friend check reads
    /// the whole group. The rest of the unit stays. Returns the relieved bots.
    /// </summary>
    public static GameBot[] RelieveOpenMembers(int unit, IReadOnlyCollection<GameBot> members, long now) =>
        members == null || members.Count == 0 ? [] : RelieveOpen(unit, members, now);

    private static GameBot[] RelieveOpen(int unit, IReadOnlyCollection<GameBot> only, long now)
    {
        var relieved = new List<(GameBot Bot, string Group, long After, bool Inside, Group Party)>();
        lock (Sync)
            foreach (Entry entry in Entries.Values.Where(entry => entry.Unit == unit && IsOpenUnit(entry.Definition, entry.Unit) && entry.Leaving == null &&
                         (only == null || only.Contains(entry.Bot))).ToArray())
            {
                if (!entry.Entered)
                {
                    Entries.Remove(entry.Bot);
                    relieved.Add((entry.Bot, entry.Group, 0, false, entry.GroupRef));
                    continue;
                }
                entry.Leaving = LeaveReason.Relieved;
                entry.LeaveStartTick = now;
                entry.ReliefHome = null;
                relieved.Add((entry.Bot, entry.Group, now - entry.EnteredTick, true, entry.GroupRef));
            }
        foreach ((GameBot bot, string group, long after, bool inside, Group party) in relieved)
        {
            // A split member leaves its unit's party (or the open-BG party it boarded with); a whole relief keeps the group.
            Group current = bot.Group;
            if (only != null && current != null && (current == party || AutonomousBotGroupCoordinator.IsOpenBattlegroundParty(current)))
                current.RemoveMember(bot);
            Log.Info($"OPEN_BG_RELIEF bot=\"{bot.Name}\" group=\"{group}\" unit={unit} after_s={after / 1000} inside={inside}");
            if (!inside) Log.Info($"AUTONOMOUS_BG_LEFT region={BattlegroundCampaignCatalog.FindOpen()?.RegionId} bot=\"{bot.Name}\" reason={Describe(LeaveReason.Relieved)}");
        }
        return relieved.Select(item => item.Bot).ToArray();
    }

    /// <summary>
    /// A relieved open-BG bot first walks to its own realm's portal keep (its landing), as a player would before
    /// leaving, for at most <see cref="ReliefWalkMilliseconds"/>; then it leaves. True while that walk lasts.
    /// </summary>
    public static bool TryGetReliefWalk(GameBot bot, out Point3D home)
    {
        home = null;
        if (bot == null) return false;
        BattlegroundDefinition definition;
        lock (Sync)
        {
            if (!Entries.TryGetValue(bot, out Entry entry) || entry.Leaving != LeaveReason.Relieved || !entry.Entered ||
                GameLoop.GameLoopTime - entry.LeaveStartTick > ReliefWalkMilliseconds)
                return false;
            home = entry.ReliefHome;
            definition = entry.Definition;
        }
        if (home == null)
        {
            GameLocation landing = BattlegroundCampaignPolicy.GetLanding(bot, definition);
            if (landing == null || landing.RegionID != bot.CurrentRegionID)
            {
                EndReliefWalk(bot);
                return false;
            }
            home = new Point3D(landing.X, landing.Y, landing.Z);
            lock (Sync)
                if (Entries.TryGetValue(bot, out Entry entry)) entry.ReliefHome = home;
        }
        return !bot.IsWithinRadius(home, ReliefHomeRadius);
    }

    /// <summary>Ends a relief walk early (no route home): the bot leaves from where it stands.</summary>
    public static void EndReliefWalk(GameBot bot)
    {
        lock (Sync)
            if (bot != null && Entries.TryGetValue(bot, out Entry entry) && entry.Leaving == LeaveReason.Relieved)
                entry.LeaveStartTick = long.MinValue / 2;
    }

    /// <summary>Within this distance of its portal keep landing a relieved bot has arrived home.</summary>
    public const int ReliefHomeRadius = 400;

    /// <summary>A participant still inside the battleground whose last real progress is recent.</summary>
    public static bool IsProgressing(GameBot bot)
    {
        lock (Sync)
            return bot != null && Entries.TryGetValue(bot, out Entry entry) && entry.Entered && entry.Leaving == null &&
                IsRecentProgress(Environment.TickCount64, entry.LastProgressWall);
    }

    /// <summary>Autonomous participants inside a campaign region, for the director cap.</summary>
    public static int PresentCount(ushort region)
    {
        lock (Sync)
            return Entries.Values.Count(entry => entry.Entered && entry.Bot.CurrentRegionID == region);
    }

    /// <summary>Records real progress for the participant's own stuck clock and the existing watchdog.</summary>
    public static void NoteProgress(GameBot bot, eAutonomousProgressKind kind)
    {
        lock (Sync)
            if (bot != null && Entries.TryGetValue(bot, out Entry entry))
            {
                Touch(entry, GameLoop.GameLoopTime);
                entry.Leaving = AfterProgress(entry.Leaving);
            }
        AutonomousStuckWatchdog.MarkProgress(bot, kind);
    }

    /// <summary>
    /// The leave mark after real progress: a stuck mark that could not be carried out (the bot was in combat)
    /// is withdrawn once the bot acts again; every other leave reason stands.
    /// </summary>
    public static LeaveReason? AfterProgress(LeaveReason? leaving) =>
        leaving == LeaveReason.Stuck ? null : leaving;

    /// <summary>
    /// Departure from a porter's cast: up to <see cref="DepartureLimit"/> waiting
    /// participants, leaders first, that stand inside its boarding radius and hold
    /// the real medallion. Each goes through the same admission as players.
    /// </summary>
    public static void Depart(OFTeleporter porter)
    {
        if (porter == null || !BattlegroundCampaignPolicy.IsEnabled) return;
        GameBot[] waiting;
        (GameBot Bot, int Unit, Group Group)[] openWaiting;
        Dictionary<int, int> openUnitSizes;
        lock (Sync)
        {
            waiting = Entries.Values
                .Where(entry => !entry.Entered && entry.Leaving == null && !IsOpenUnit(entry.Definition, entry.Unit))
                .Select(entry => entry.Bot)
                .ToArray();
            openWaiting = Entries.Values
                .Where(entry => !entry.Entered && entry.Leaving == null && IsOpenUnit(entry.Definition, entry.Unit))
                .Select(entry => (entry.Bot, entry.Unit, entry.GroupRef))
                .ToArray();
            openUnitSizes = openWaiting.Length == 0 ? null : OpenUnitSizes();
        }
        GameBot[] boarders = waiting
            .Where(bot => bot.ObjectState == GameObject.eObjectState.Active && bot.CurrentRegion == porter.CurrentRegion &&
                bot.IsWithinRadius(porter, AutonomousFrontierTransport.BoardingRadius))
            .OrderBy(bot => bot.Group?.LivingLeader == bot ? 0 : 1)
            .ThenBy(bot => bot.DatabaseID)
            .Take(DepartureLimit)
            .ToArray();
        Board(porter, boarders);
        if (openWaiting.Length == 0) return;
        GameBot[] open = OpenBoarders(porter, openWaiting, openUnitSizes, GameLoop.GameLoopTime,
            out List<(int Unit, GameBot[] Stragglers)> stragglers);
        Board(porter, open);
        DropStragglers(stragglers);
    }

    /// <summary>
    /// The open crew's own cast of up to <see cref="DepartureLimit"/>, unit by unit and never splitting one: a solo
    /// boards alone; a group boards whole, leader first, and only once <see cref="OpenGroupMayBoard"/> allows it, so
    /// the group arrives as one and lands at its leader's portal keep. A group that does not fit this cast waits.
    /// Once its boarding wait is over (<see cref="OpenBoardingWaitOver"/>) the ready members of a group board
    /// without its live stragglers (<see cref="OpenGroupMayBoardWithoutStragglers"/>); the stragglers are returned
    /// in <paramref name="stragglers"/> and leave the unit. A group boards only while its unit, dead members and members
    /// already inside included (<paramref name="unitSizes"/>), still makes its kind's minimum
    /// (<see cref="OpenUnitMeetsBoardingMinimum"/>); a unit below it waits at the porter for the roster's refill.
    /// </summary>
    private static GameBot[] OpenBoarders(OFTeleporter porter, (GameBot Bot, int Unit, Group Group)[] waiting,
        IReadOnlyDictionary<int, int> unitSizes, long now, out List<(int Unit, GameBot[] Stragglers)> stragglers)
    {
        bool Live(GameBot bot) => bot.ObjectState == GameObject.eObjectState.Active && bot.IsAlive;
        bool Ready(GameBot bot) => Live(bot) && bot.CurrentRegion == porter.CurrentRegion &&
            bot.IsWithinRadius(porter, AutonomousFrontierTransport.BoardingRadius) && HasBattlegroundMedallion(bot);
        var boarders = new List<GameBot>();
        stragglers = new List<(int Unit, GameBot[] Stragglers)>();
        foreach (var unit in waiting.GroupBy(member => member.Unit)
                     .Select(unit => (Id: unit.Key, Group: unit.Select(member => member.Group).FirstOrDefault(group => group != null),
                         Members: unit.Select(member => member.Bot).ToArray()))
                     .OrderBy(unit => (unit.Group?.LivingLeader as GameBot ?? unit.Members[0]).DatabaseID))
        {
            // Members of another region than the leader (preset Server PvP unit) board at their own region's porter
            // after the leader; they never hold the leader's whole-group boarding back.
            GameBot[] ownPorter = unit.Group == null ? [] : unit.Members.Where(AutonomousBotGroupCoordinator.IsOpenOwnPorterMember).ToArray();
            GameBot[] groupMembers = ownPorter.Length == 0 ? unit.Members : unit.Members.Except(ownPorter).ToArray();
            if (ownPorter.Length > 0)
            {
                GameBot leaderBot = unit.Group.LivingLeader as GameBot;
                long leaderEnteredTick = OpenLeaderEnteredTick(leaderBot);
                foreach (GameBot member in ownPorter.Where(Live))
                {
                    bool leaderEntered = member == leaderBot || leaderEnteredTick > 0;
                    if (OpenOwnPorterMayBoard(leaderEntered, Ready(member)))
                    {
                        if (boarders.Count < DepartureLimit) boarders.Add(member);
                    }
                    else if (leaderEnteredTick > 0 && member.CurrentRegion == porter.CurrentRegion &&
                        OpenBoardingWaitOver(leaderEnteredTick, now))
                        stragglers.Add((unit.Id, [member]));
                }
            }
            GameBot[] live = groupMembers.Where(Live).ToArray();
            GameBot[] ready = live.Where(Ready).ToArray();
            if (ready.Length == 0) continue;
            if (unit.Group != null)
            {
                bool assembling = AutonomousBotGroupCoordinator.IsOpenBattlegroundAssembling(unit.Group);
                // Live run 0.242.0: the wait ran from the first member at the porter while the others still mustered,
                // so whole groups were dropped as stragglers in their "Meeting up" phase. The muster has its own clock.
                if (!OpenBoardingWaitRuns(assembling, ready.Length)) continue;
                long readySince = MarkReady(ready, now);
                int minimum = OpenBattlegroundRoster.KindOf(unit.Id) is OpenBgUnitKind kind ? OpenBattlegroundRoster.MinimumSize(kind) : 1;
                int members = unitSizes != null && unitSizes.TryGetValue(unit.Id, out int size) ? size : unit.Members.Length;
                bool whole = OpenGroupMayBoard(assembling, live.Length, ready.Length);
                if (!whole)
                {
                    if (!OpenBoardingWaitOver(readySince, now)) continue;
                    // The wait is over: the stragglers leave whether or not the ready members still make a unit.
                    GameBot[] late = live.Except(ready).ToArray();
                    stragglers.Add((unit.Id, late));
                    if (!OpenGroupMayBoardWithoutStragglers(true, members - late.Length, minimum)) continue;
                }
                // Live run 0.246.0: the remainder of a straggler drop boarded one cast later below its minimum and the
                // roster relieved it seconds after landing. Below its minimum a unit waits here for the porter refill
                // instead; its assignment timeout bounds the wait.
                else if (!OpenUnitMeetsBoardingMinimum(members, minimum)) continue;
            }
            if (boarders.Count + ready.Length > DepartureLimit) continue;
            boarders.AddRange(ready.OrderBy(bot => unit.Group?.LivingLeader == bot ? 0 : 1).ThenBy(bot => bot.DatabaseID));
        }
        return boarders.ToArray();
    }

    // The game time the unit's leader entered the battleground; 0 while it has not (or has no entry any more).
    private static long OpenLeaderEnteredTick(GameBot leader)
    {
        lock (Sync)
            return leader != null && Entries.TryGetValue(leader, out Entry entry) && entry.Entered ? Math.Max(1, entry.EnteredTick) : 0;
    }

    // Stamps the members standing ready now; returns the game time the first of them was ready.
    private static long MarkReady(GameBot[] ready, long now)
    {
        long first = 0;
        lock (Sync)
            foreach (GameBot bot in ready)
                if (Entries.TryGetValue(bot, out Entry entry))
                {
                    if (entry.ReadySinceTick <= 0) entry.ReadySinceTick = now;
                    first = first == 0 ? entry.ReadySinceTick : Math.Min(first, entry.ReadySinceTick);
                }
        return first;
    }

    /// <summary>Whether a group's boarding wait is over: <see cref="OpenBoardingWaitMilliseconds"/> of game time since its first member stood ready.</summary>
    public static bool OpenBoardingWaitOver(long readySinceTick, long now) =>
        readySinceTick > 0 && now - readySinceTick >= OpenBoardingWaitMilliseconds;

    /// <summary>
    /// A group's boarding wait (and with it the straggler drop) only starts once its coordinator muster at the porter
    /// has ended and a member stands ready; while the group still musters nobody boards and nobody is dropped.
    /// </summary>
    public static bool OpenBoardingWaitRuns(bool assemblyPhase, int readyWaiting) => !assemblyPhase && readyWaiting > 0;

    /// <summary>A refilled unit waits for its new members again: the boarding wait of every member restarts.</summary>
    public static void RestartOpenBoardingWait(int unit)
    {
        lock (Sync)
            foreach (Entry entry in Entries.Values)
                if (entry.Unit == unit && IsOpenUnit(entry.Definition, entry.Unit) && !entry.Entered)
                    entry.ReadySinceTick = 0;
    }

    /// <summary>
    /// After the boarding wait the ready members of a group board without its stragglers, but only while the unit
    /// left behind (<paramref name="remainingMembers"/>: every member that is not a straggler, dead members and members
    /// already inside included) still makes a unit of its kind (<see cref="OpenBattlegroundRoster.MinimumSize"/>).
    /// Fewer board nobody; the stragglers leave anyway, and the rest waits at the porter for the roster's refill.
    /// </summary>
    public static bool OpenGroupMayBoardWithoutStragglers(bool waitOver, int remainingMembers, int minimum) =>
        waitOver && OpenUnitMeetsBoardingMinimum(remainingMembers, minimum);

    /// <summary>
    /// Whether an open-BG unit may board at all: its members (dead ones and those already inside count, a dropped
    /// straggler does not) make its kind's minimum. Six with one dead member board; five after a straggler drop wait
    /// at the porter for the refill instead of landing short and being relieved at once.
    /// </summary>
    public static bool OpenUnitMeetsBoardingMinimum(int unitMembers, int minimum) =>
        unitMembers > 0 && unitMembers >= Math.Max(1, minimum);

    // Callers hold Sync. Members per open-BG unit that are not leaving, entered or not, dead or alive.
    private static Dictionary<int, int> OpenUnitSizes() =>
        Entries.Values.Where(entry => entry.Leaving == null && IsOpenUnit(entry.Definition, entry.Unit))
            .GroupBy(entry => entry.Unit).ToDictionary(unit => unit.Key, unit => unit.Count());

    // Live members still outside the circle when their group's wait ran out leave the unit and its group. Dead
    // members are not stragglers: they stay in the unit and travel to the porter alone after their release.
    private static void DropStragglers(List<(int Unit, GameBot[] Stragglers)> stragglers)
    {
        foreach ((int unit, GameBot[] bots) in stragglers)
            foreach (GameBot bot in bots)
            {
                Group group;
                lock (Sync)
                {
                    if (!Entries.TryGetValue(bot, out Entry entry) || entry.Unit != unit || entry.Entered || entry.Leaving != null) continue;
                    group = entry.GroupRef;
                    Entries.Remove(bot);
                }
                if (group != null && bot.Group == group) group.RemoveMember(bot);
                OpenBattlegroundRoster.NoteLeftBeforeEntry(bot, GameLoop.GameLoopTime);
                Log.Info($"OPEN_BG_STRAGGLER_DROPPED bot=\"{bot.Name}\" unit={unit} waited_s={OpenBoardingWaitMilliseconds / 1000}");
                Log.Info($"AUTONOMOUS_BG_LEFT region={BattlegroundCampaignCatalog.FindOpen()?.RegionId} bot=\"{bot.Name}\" reason={Describe(LeaveReason.Unassigned)}");
            }
    }

    /// <summary>
    /// Whether an open-BG group may board at a porter's cast: its coordinator assembly (leader staging, meeting up)
    /// has ended and every live waiting member stands in the boarding circle with the medallion. Otherwise nobody of
    /// the group boards, so a leader never lands alone while its group still musters (or rests) around the porter.
    /// </summary>
    public static bool OpenGroupMayBoard(bool assemblyPhase, int liveWaiting, int readyWaiting) =>
        !assemblyPhase && liveWaiting > 0 && readyWaiting >= liveWaiting;

    /// <summary>
    /// A member of another region than its group leader (preset Server PvP unit) boards at the porter of its own region
    /// once its leader has entered the battleground and it stands ready there; the porter then lands it at the leader's landing.
    /// </summary>
    public static bool OpenOwnPorterMayBoard(bool leaderEntered, bool memberReady) => leaderEntered && memberReady;

    private static void Board(OFTeleporter porter, GameBot[] boarders)
    {
        foreach (GameBot bot in boarders)
        {
            if (!HasBattlegroundMedallion(bot)) continue;
            // An open-BG crew member boards for the open BG, never for its level bracket.
            BattlegroundDefinition assigned;
            lock (Sync) assigned = Entries.TryGetValue(bot, out Entry pending) ? pending.Definition : null;
            string reason;
            ushort destination;
            bool boarded = assigned?.Kind == BattlegroundDefinitionKind.Open
                ? BattlegroundCampaignPolicy.TryEnterBotOpen(bot, out reason, out destination)
                : BattlegroundCampaignPolicy.TryEnterBot(bot, out reason, out destination);
            if (!boarded)
            {
                Log.Debug($"AUTONOMOUS_BG_ENTRY_REFUSED region={porter.CurrentRegionID} bot=\"{bot.Name}\" reason=\"{reason}\"");
                continue;
            }
            ushort from = porter.CurrentRegionID;
            // A new visit starts with a fresh driver state, so its first turn records progress again.
            AutonomousBattlegroundDriver.ForgetState(bot);
            lock (Sync)
                if (Entries.TryGetValue(bot, out Entry entry))
                {
                    entry.Entered = true;
                    entry.EnteredTick = GameLoop.GameLoopTime;
                    Touch(entry, GameLoop.GameLoopTime);
                }
            Log.Info($"AUTONOMOUS_BG_ENTERED region={destination} from_region={from} bot=\"{bot.Name}\"");
        }
    }

    /// <summary>Records one driver turn. The first turn of an entry is logged with the action it chose.</summary>
    public static void RecordDriverTurn(GameBot bot, ushort region, string action)
    {
        bool first = false;
        lock (Sync)
            if (bot != null && Entries.TryGetValue(bot, out Entry entry) && entry.Entered)
                first = entry.DriverTurns++ == 0;
        if (first)
            Log.Info($"AUTONOMOUS_BG_DRIVER_FIRST_TURN region={region} bot=\"{bot.Name}\" action={action}");
    }

    /// <summary>Why the driver took no action this turn; logged at most once per entry and reason.</summary>
    public static void RecordDriverIdle(GameBot bot, ushort region, string reason)
    {
        bool log = false;
        lock (Sync)
            if (bot != null && Entries.TryGetValue(bot, out Entry entry))
            {
                entry.IdleReasons ??= new HashSet<string>(StringComparer.Ordinal);
                log = entry.IdleReasons.Add(reason);
            }
        if (log)
            Log.Info($"AUTONOMOUS_BG_DRIVER_IDLE region={region} bot=\"{bot.Name}\" reason={reason}");
    }

    /// <summary>
    /// Why the driver could not prove a route to a goal. The driver logs each goal once per visit; in the open BG at
    /// most one line per bot per game minute, with the number of lines it held back since the last one.
    /// </summary>
    public static void RecordRouteFailure(GameBot bot, ushort region, string action, string goal, string from, string reason, int suppressed = 0) =>
        Log.Info($"AUTONOMOUS_BG_ROUTE_FAILED region={region} bot=\"{bot.Name}\" action={action} goal={goal} from={from} reason={reason}" +
            (suppressed > 0 ? $" suppressed={suppressed}" : string.Empty));

    // Callers hold Sync. Real progress restarts both stuck clocks.
    private static void Touch(Entry entry, long gameNow)
    {
        entry.LastProgressTick = gameNow;
        entry.LastProgressWall = Environment.TickCount64;
    }

    /// <summary>
    /// Leaves the battleground (bind-equivalent exit, out of combat only). Returns
    /// true once the bot is outside and its participation has ended.
    /// </summary>
    public static bool TryFinishLeaving(GameBot bot)
    {
        LeaveReason reason;
        BattlegroundDefinition definition;
        lock (Sync)
        {
            if (bot == null || !Entries.TryGetValue(bot, out Entry entry) || entry.Leaving == null) return false;
            reason = entry.Leaving.Value;
            definition = entry.Definition;
        }
        if (!BattlegroundCampaignPolicy.TryExitBot(bot, out _)) return false;
        lock (Sync) Entries.Remove(bot);
        Log.Info($"AUTONOMOUS_BG_LEFT region={definition.RegionId} bot=\"{bot.Name}\" reason={Describe(reason)}");
        return true;
    }

    /// <summary>Battleground death: the participant releases at its campaign arrival camp.</summary>
    public static bool TryBattlegroundRelease(GameBot bot, ushort deathRegion, out GameLocation landing)
    {
        landing = null;
        BattlegroundDefinition definition = BattlegroundCampaignCatalog.Find(deathRegion);
        if (definition == null || bot == null || bot.CurrentRegionID != deathRegion || !IsParticipant(bot)) return false;
        landing = BattlegroundCampaignPolicy.GetLanding(bot, definition);
        return landing != null && landing.RegionID == deathRegion;
    }

    /// <summary>
    /// Whether a release starts the walk back to the party or death spot. Not at a battleground landing: the
    /// driver leads the participant from there, and a second movement owner pinned it at the portal-keep wall
    /// until the stuck rule ejected it (bug 127).
    /// </summary>
    public static bool StartsReleaseReturnWalk(bool returnToParty, bool battlegroundLanding) =>
        returnToParty && !battlegroundLanding;

    private static int Reconcile(ECSGameTimer timer)
    {
        try
        {
            ReconcileNow(GameLoop.GameLoopTime);
        }
        catch (Exception exception)
        {
            Log.Error("AUTONOMOUS_BG_RECONCILE_FAILED", exception);
        }
        return ReconcileIntervalMilliseconds;
    }

    private static void ReconcileNow(long now)
    {
        bool enabled = BattlegroundCampaignPolicy.IsEnabled;
        GameBot[] roster = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && !bot.IsPersistentPlayerCompanion)
            .OrderBy(bot => bot.DatabaseID)
            .ToArray();
        List<Entry> snapshot;
        lock (Sync) snapshot = Entries.Values.ToList();

        foreach (Entry entry in snapshot)
        {
            GameBot bot = entry.Bot;
            if (!enabled || bot.ObjectState != GameObject.eObjectState.Active)
            {
                Remove(entry, LeaveReason.Unassigned);
                continue;
            }
            if (entry.Leaving == null)
            {
                // An open-BG crew member stays until the roster relieves it: a died-and-released bot stays too.
                LeaveReason? reason = IsOpenUnit(entry.Definition, entry.Unit)
                    ? OpenLeaveReasonFor(OpenBattlegroundPolicy.IsEnabled, bot.IsPlayerLedGroup, GameRelic.IsPlayerCarryingRelic(bot),
                        LeftOpenUnitBeforeEntry(entry.Entered, bot.Group != null, bot.Group == entry.GroupRef))
                    : LeaveReasonFor(bot.Level, bot.RealmLevel,
                        AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR), bot.IsPlayerLedGroup,
                        GameRelic.IsPlayerCarryingRelic(bot), entry.Definition);
                if (!entry.Entered && now - entry.AssignedTick > AssignmentTimeoutFor(entry.Definition))
                    reason = LeaveReason.Unassigned;
                if (entry.Entered && bot.CurrentRegionID != entry.Definition.RegionId)
                    reason = LeaveReason.Unassigned;
                long wall = Environment.TickCount64;
                bool hadTurn;
                lock (Sync) hadTurn = entry.DriverTurns > 0;
                if (entry.Entered && reason == null && IsStuck(now, entry.LastProgressTick, wall, entry.LastProgressWall, hadTurn))
                    reason = LeaveReason.Stuck;
                if (entry.Entered && reason == null && IsNoTurn(now, entry.LastProgressTick, wall, entry.LastProgressWall, hadTurn))
                {
                    reason = LeaveReason.NoTurn;
                    LogNoTurn(bot, entry.Definition.RegionId);
                }
                if (reason != null && !entry.Entered)
                {
                    Remove(entry, reason.Value);
                    continue;
                }
                if (reason != null && bot.CurrentRegionID != entry.Definition.RegionId)
                {
                    Remove(entry, reason.Value);
                    continue;
                }
                if (reason != null)
                    lock (Sync) entry.Leaving = reason;
            }
            // A relieved bot walking home is let out by its driver once it arrives (or the walk times out).
            if (entry.Leaving != null && !TryGetReliefWalk(bot, out _)) TryFinishLeaving(bot);
        }

        // Bots already standing in a bracket map (after a restart) are re-registered
        // when they still qualify, otherwise sent out.
        foreach (GameBot bot in roster)
        {
            if (bot.ObjectState != GameObject.eObjectState.Active || IsRegistered(bot)) continue;
            BattlegroundDefinition inside = BattlegroundCampaignCatalog.Find(bot.CurrentRegionID);
            if (inside == null) continue;
            // The open BG's crew lives in the roster's memory only: a bot found inside it without an entry
            // (after a restart) is sent out and the roster refills.
            LeaveReason? reason = inside.Kind == BattlegroundDefinitionKind.Open
                ? LeaveReason.Unassigned
                : LeaveReasonFor(bot.Level, bot.RealmLevel,
                    AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR), bot.IsPlayerLedGroup,
                    GameRelic.IsPlayerCarryingRelic(bot), inside);
            if (!enabled || reason != null)
            {
                lock (Sync) Entries[bot] = new Entry { Bot = bot, Definition = inside, Entered = true, Leaving = reason ?? LeaveReason.Unassigned, AssignedTick = now, LastProgressTick = now, LastProgressWall = Environment.TickCount64 };
                TryFinishLeaving(bot);
                continue;
            }
            lock (Sync) Entries[bot] = new Entry { Bot = bot, Definition = inside, Entered = true, AssignedTick = now, LastProgressTick = now, LastProgressWall = Environment.TickCount64 };
            Log.Info($"AUTONOMOUS_BG_ENTERED region={inside.RegionId} bot=\"{bot.Name}\" reregistered=true");
        }

        if (enabled) Admit(roster, now);
    }

    /// <summary>Per pass: whether a region has a porter with a medallion merchant in reach.</summary>
    private static bool Sourced(ushort region, Dictionary<ushort, bool> cache)
    {
        if (!cache.TryGetValue(region, out bool sourced))
            cache[region] = sourced = SourcePorters(region).Length > 0;
        return sourced;
    }

    private static bool IsRegistered(GameBot bot)
    {
        lock (Sync) return Entries.ContainsKey(bot);
    }

    /// <summary>
    /// Once per no-turn exit: whether the brain is still scheduled, when it next thinks, and the body's
    /// life and crowd-control state. Separates "never scheduled" from "scheduled but returning idle".
    /// </summary>
    private static void LogNoTurn(GameBot bot, int regionId)
    {
        var brain = bot.Brain;
        bool scheduled = brain != null && brain.ServiceObjectId.IsSet;
        long untilThink = brain == null ? 0 : brain.NextThinkTick - GameLoop.GameLoopTime;
        string idle;
        lock (Sync)
            idle = Entries.TryGetValue(bot, out Entry entry) && entry.IdleReasons != null ? string.Join(",", entry.IdleReasons) : "none";
        Log.Info($"AUTONOMOUS_BG_NO_TURN_DIAGNOSTIC region={regionId} bot=\"{bot.Name}\" brain_scheduled={scheduled} " +
            $"next_think_ms={untilThink} alive={bot.IsAlive} crowd_controlled={bot.IsCrowdControlled} health={bot.Health} " +
            $"object_state={bot.ObjectState} idle_reasons={idle}");
    }

    private static void Remove(Entry entry, LeaveReason reason)
    {
        lock (Sync) Entries.Remove(entry.Bot);
        // An open-BG member that left its unit before boarding (timeout, another party) is not recruited again at once.
        if (IsOpenUnit(entry.Definition, entry.Unit) && !entry.Entered)
            OpenBattlegroundRoster.NoteLeftBeforeEntry(entry.Bot, GameLoop.GameLoopTime);
        Log.Info($"AUTONOMOUS_BG_LEFT region={entry.Definition.RegionId} bot=\"{entry.Bot.Name}\" reason={Describe(reason)}");
    }

    private static void Admit(GameBot[] roster, long now)
    {
        // Gothwaite PvP mode: no autonomous bot is admitted to a campaign battleground (section 5.4, G9).
        if (GothwaitePvpRules.SuppressesWorldRecruitment) return;
        Dictionary<ushort, int> used = new();
        lock (Sync)
            foreach (Entry entry in Entries.Values)
            {
                ushort region = (ushort)entry.Definition.RegionId;
                used[region] = used.GetValueOrDefault(region) + 1;
            }
        var handled = new HashSet<GameBot>(ReferenceEqualityComparer.Instance);
        var sourced = new Dictionary<ushort, bool>();
        foreach (GameBot bot in roster)
        {
            if (handled.Contains(bot) || IsRegistered(bot) || !IsFrontierRegion(bot.CurrentRegionID)) continue;
            GameLiving[] raw = bot.Group == null ? new GameLiving[] { bot } : bot.Group.GetMembersInTheGroup().ToArray();
            foreach (GameLiving member in raw) handled.Add(member as GameBot);
            // A group with a player, companion or helper member is never assigned whole.
            if (raw.Any(member => member is not GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false })) continue;
            GameBot[] members = raw.Cast<GameBot>().ToArray();
            GameBot anchor = bot.Group?.LivingLeader as GameBot ?? bot;
            BattlegroundDefinition definition = BattlegroundCampaignCatalog.ForLevel(anchor.Level);
            if (definition == null) continue;
            // A member may only be admitted where it can buy the medallion from a real merchant.
            int eligible = members.Count(member => member.IsAutonomousWorldBot && !member.IsTemporaryGroupHelper &&
                member.ObjectState == GameObject.eObjectState.Active && !IsRegistered(member) &&
                IsCandidate(member.Level, member.RealmLevel, AutonomousObjectiveAssignments.Is(member, eAutonomousObjectiveKind.RvR),
                    member.CurrentRegionID, member.IsPlayerLedGroup, GameRelic.IsPlayerCarryingRelic(member), definition) &&
                Sourced(member.CurrentRegionID, sourced));
            int present = used.GetValueOrDefault((ushort)definition.RegionId);
            if (!AdmitsGroup(members.Length, eligible, present)) continue;
            string label = bot.Group == null ? "solo" : anchor.Name;
            lock (Sync)
                foreach (GameBot member in members)
                    Entries[member] = new Entry { Bot = member, Definition = definition, Group = label, AssignedTick = now, LastProgressTick = now, LastProgressWall = Environment.TickCount64 };
            used[(ushort)definition.RegionId] = present + members.Length;
            foreach (GameBot member in members)
                Log.Info($"AUTONOMOUS_BG_ASSIGNED region={definition.RegionId} bot=\"{member.Name}\" level={member.Level} group=\"{label}\"");
        }
    }
}
