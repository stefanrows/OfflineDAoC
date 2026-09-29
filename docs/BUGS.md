# Known Bugs

Track confirmed, unresolved bugs here. Include the affected version, steps to reproduce, expected and actual behavior, impact, and any workaround. Fixes are handled in a separate task unless noted below. When a bug is fixed and its required verification is complete, move it out of its current section into **Finished** immediately, recording a brief resolution and version. Do not leave completed items in **Open**. If a source fix still awaits installation or real-client verification, keep it under **Fixed in source; installation verification pending** until that check is complete.

Tasks, feature requests, and ideas belong in [TASKS.md](TASKS.md).

## Open

65b. **RvR bots die to named frontier mobs far above their level.** Split
    from 65. Live 0.125.0, 2026-09-28 04:41–15:46: 7,614 level-50 PvE deaths,
    mostly to named frontier mobs: Illusion of Aidon the Archwizard (level 75,
    589), Black Lady (65, 457), reanimated guardian (58, 337). Expected:
    roaming warbands walk around named mobs far above their level. Not yet
    investigated (RvR route and aggro avoidance).

64. **Player and companion overhead names sometimes do not appear.** Reported
    while source was 0.109.0; installed version and client build are
    unconfirmed. Seen after login, zoning, or when entities reappear or
    visibility refreshes. Expected: nearby player and companion name labels
    remain visible whenever their models are visible. Actual: entity models
    appear but some overhead labels are intermittently absent. Exact scope and
    workaround are unknown. Source audit on 2026-09-27 found player names in
    `SendPlayerCreate` and companion names in `SendNPCCreate`; no shared
    hide condition was found in those packet paths. A client/packet capture is
    needed to locate whether a create was omitted or ignored.

60. **`Ability 'ConfusionImmunity' unknown` is logged 939 times per run.**
    Seen in the installed 0.115.0 log (2026-09-27/28, 3 h 26 min) from
    `DOL.GS.SkillBase`, together with 240 `LineXSpell Spell Adding Error`
    warnings. Impact unknown: the ability or spell line is not granted, so a
    class that should have confusion immunity or the affected spells may be
    missing them. Expected: no unknown-ability or spell-adding warnings on a
    clean start. Not yet investigated.

61. **`REALM_RAID_HUB_ROUTE_FAILED event=epic-albion` about 175 times per
    run.** Seen in the installed 0.115.0 log (3 h 26 min): the realm-raid rally
    path for the Albion epic event cannot route to its hub. Expected: the raid
    hub is reachable or the event is skipped instead of retried. Not yet
    investigated.

62. **`SortStyles NULL style` and `Unhandled spell ... Bladeturn` warnings,
    about 1,967 per run.** Seen in the installed 0.115.0 log (3 h 26 min) from
    `DOL.GS.GameNPC`. Impact: styles or the Bladeturn effect may be skipped on
    the affected NPCs or bots. Expected: no null styles in a bot's style list
    and Bladeturn handled by an effect class. Not yet investigated.

63. **Bots die repeatedly at a bindstone that is also an RvR rendezvous.**
    Seen in the installed 0.115.0 log (3 h 26 min): the Midgard skald
    Sivildrid died 33 of 40 times at the Svasud Faste bind (100: 765147,668315),
    where RvR groups gather, and other bots (Sigiarfrid, Yrenborg, Livardis)
    show the same spot. Each release returns the bot to the same bind and the
    next fight kills it again. Task 48 makes the three border hubs safe, which
    should remove this loop; verify after deployment. Related: bug 29.

5. **Some helmets render oversized or glitched.** The supplied screenshots
    (2026-09-28) show Eilis, a level-47 Elf female Enchanter from Hibernia,
    wearing a level-46, quality-95 Matterbender Cloth Cap. A read-only lookup
    of the installed save found a likely match: a level-49 Hibernian Elf
    Enchanter named Eilis wears the same level-46 / quality-95 cap. It has
    model 825 and Realm Midgard; its saved appearance size is 50. The level-47
    screenshot and this saved record are a strong, but not conclusive, match.
    The local `game.dll` metadata reports 1.127, but the screenshot's client
    process is unverified.
    Source maps the ordinary Midgard cloth cap to model 825 and the ordinary
    Hibernian cloth cap to 826 (the wizard-hat variants are 1280 and 1279).
    The equipment packet sends the stored item model, texture and effect
    fields without a realm conversion or helmet-scale field. Current companion
    reward generation uses the companion's realm; owner-to-companion transfers
    preserve the original item template. The leading explanation is that this
    Hibernian Elf is wearing a cross-realm Midgard model. The item's origin and
    the model's rendered fit still need confirmation in the client. Reproduce
    by viewing Eilis in-world with this cap equipped. Expected: the cap fits
    the wearer's head at normal scale. Actual: the helmet appears oversized or
    visually glitched. The reported impact is cosmetic; no gameplay effect or
    workaround was reported. Installed server version is unknown.

## Fixed in source; installation verification pending

69. **Player-led group falls behind during speed-song runs.** Reported
    2026-09-29 while running with Bard speed without sprint; installed
    version and whether other speed sources show the same issue are unknown.
    Repro: run continuously with companions under a Bard speed song.
    Expected: companions close the initial gap and keep pace. Actual: the
    group appears to lose the player. Source investigation found the fast
    follow style waited 3 s to engage, following a possible 1.5 s hold,
    and then requested at most 40 extra speed; a song-speed head start
    therefore took many seconds to recover. Source fix 0.145.0 starts the
    stick route after 0.5 s and uses the existing bounded 20% catch-up
    allowance by 300 units behind its slot. Workaround before install:
    pause briefly after starting a fast run. Installation and real-client
    verification pending: check Bard and other speed effects with a full
    group on straight and turning routes, without and with sprint.

68. **`/gc claim` at the Keep Claim Steward shows nothing.** Reproduced
    2026-09-29 at Fensalir Faste with an eight-member player/companion group.
    Five installed-server `KEEP_CLAIM_ATTEMPT` lines selected keep 80 and
    returned `allowed=False`; the owner was empty, the lord was defeated, and
    the steward existed. The live save showed Ceovage had claim rank, an
    eight-member claim requirement, and a three-keep guild limit with no
    RAGE-owned keeps. The old log
    omitted the refusal reason, so the exact failing predicate cannot be
    established from this run. Source 0.153.0 lets a defeated-lord PvP keep
    be claimed despite later siege damage refreshing its five-minute combat
    timer, removes the Camlann group-size requirement at the owner's request,
    and repeats the exact refusal in main chat and the attempt log. Version
    0.153.0 was installed on 2026-09-29; a real-client retry, including a solo
    guild claim, remains pending. If rejected, use the new `refusal` field to
    identify the remaining check. Successful claim broadcast was fixed
    earlier in source 0.141.1.

66. **Warbands re-port to the frontier every 3–4 minutes after a wipe.** Live
    0.125.0, 11 h: 7,560 `RVR_FRONTIER_DEPARTURE` (about 690 per hour; 0.115.0
    had about 94 per hour), 1,999 of them full eight-member parties; single
    forces departed 160–191 times. Bots die in the field, release at their hub
    (0.123.0) and board again at once. Expected: a 2003 group rezzed, buffed and
    regrouped for a few minutes before porting back. Investigation (04:41–15:46
    log): `party=8` is the group size, not the number boarding; 5,979 of 7,558
    departures moved one bot, and 3,646 of the 5,224 warband departures were a
    single released member going back alone; 4,360 of 6,847 repeat departures
    of a force came less than 3 minutes after its previous one. Fixed in
    source: an autonomous RvR bot does not port out within 75 s of its own
    release (it sits and recovers at the porter); a warband with a freshly
    released member at the porter waits until all its members are alive and
    within 1,500 units of the porter, or until the first of them has waited
    3 minutes; a warband leaves at most once per 5 minutes (the rest of a
    departure under way may follow for 20 s; a keep-defence call skips only
    the cap); home passages are never held. The 0.123.0 one-minute muster is
    unchanged. `RVR_FRONTIER_DEPARTURE` now logs `since_release_s` and
    `force_gap_s`. Real-client and live-log check pending.
65. **Bots die to mobs inside the border hubs.** Live 0.125.0, 2026-09-28
    04:41–15:46 (11 h, 1,210 bots): 5,508 deaths inside the 3,500-unit safe
    hubs, 4,997 of them PvE (phantom magi 739, savage dragonfly 522, thrawn
    ogre thresher 412, snowshoe bandit 308, orc lure 275, defiled skeleton 270,
    pollen spore 250), rising from about 200 to 770 per hour. Cause: not the
    world data. The save has no hostile spawn within any hub radius (only
    guards, merchants, trainers and ambient critters), and the 0.72.0
    frontier restore placed none there. The killers are generated charm
    pets: Sorcerer, Minstrel and Mentalist bots create a body from a real mob
    row of their realm's template regions (Sorcerer and Minstrel: Albion
    regions 1–62, killers here from Shrouded Isles region 51; Mentalist:
    Hibernia regions 180–224, killers here from 181 and 200) next to themselves while idle, typically at the hub
    bindstone. That body keeps the template row's respawn interval. When the
    pet or an uncharmed candidate died, GameNPC death wiped the pet tag and
    started the respawn; the deferred charm stop and the owner's cleanup then
    no longer recognised it, and it came back as an ordinary aggressive
    template mob (template level spread, aggro range 500) at its creation
    spot, respawning there until the next server restart. 1,590 of the hub
    deaths are on the Castle Sauvage bindstone (585891,476614) itself. The same
    leak made 13,523 of 28,962 PvE mob kills in the run (47 %) come from mobs
    that have no spawn in that region, rising from 201 to 2,101 per hour.
    Fix in source: a generated charm body carries no respawn and keeps its
    identity after death (a weak table besides the tag), so death, the charm
    stop and owner cleanup delete it; human players' generated charms use the
    same path. The ghost camps live only in server memory, so no save
    migration is needed; the next server start clears the existing ones.
    Not explained: the 308 snowshoe bandit deaths on the Svasud Faste hub
    bindstone (765147,668315); no generated-charm class uses Midgard
    templates, and the bandit camp lies about 21,000 units away. Measure after
    deployment. Verification: `AUTONOMOUS_BOT_DEATH` lines with
    `classification=pve` inside the hub radius should drop toward 0 per hour,
    and PvE kills by mobs without a spawn in that region should stay near 0
    over a whole day. Real-client check pending.

67. **Mobs BAF toward the group during a held `/petpull`.** Reported on
    2026-09-28 while pet pulling as a Necromancer: mobs headed toward group
    members, though the expected pull remained on the pet and usually did
    not group. Cause: a mob attacked by the owner's controlled pull pet can
    select a group member as its highest-threat target;
    `StandardMobBrain.BringFriends` then applies ordinary group BAF. Source
    fix 0.133.0 checks the mob's attacker tracker for the exact controlled
    pet whose owner has an active held pet pull, disables BAF for that mob,
    and returns before normal group BAF calculation. Mode-off fights and
    fights without the held pulling pet among the attackers keep normal BAF.
    Installation and real-client verification pending: reproduce with a
    Necromancer pet and BAF mobs; verify the held pull does not recruit an
    ordinary BAF wave toward the group, and normal BAF still works with mode
    off and fights without that pet among the attackers. Installed 0.133.0
    reduced, but did not eliminate, mobs turning toward the group during a
    Necromancer pet pull. Source audit found further ways to cause this:
    during the hold, tanks took attackers off the pet, and
    direct pet heals gave the healer threat against each pet attacker;
    temporary companion healers could also select the pet through their own
    triage and shared-heal paths. A group heal on the Necromancer shade can
    be redirected to the servant and generate the same threat. Source
    follow-up 0.138.0 holds those direct actions until release, with a guard
    at heal application, while preserving the non-aggro pet HoT and the
    emergency release below 45% pet health. Real-client check pending:
    confirm mobs remain on the pet before release, group-bound mobs can still
    be intercepted, and companions heal and defend the pet after release.

59. **Launcher BotGoalsSettings tests cannot construct the control.** Reopened
    2026-09-28 (formerly numbered 33 under Finished): the four
    `BotGoalsSettingsTests` (LegacyFileExplainsMappingBeforeRewriting,
    MeasuredRecommendationAndPanelRenderWithoutLaunchingServer,
    MixTotalAndServerStateGateSaving, PresetAndWorldShapeSaveAndUndo) failed
    in SetUp with `MissingMethodException: Constructor on type
    'OfflineDaoc.Launcher.BotGoalsSettingsControl' not found` on 0.117.0
    (launcher suite 122 passed, 5 failed). Cause: `BotGoalsSettingsControl`'s
    constructor grew from two parameters to five (`path`,
    `worldSpeedStatusPath`, `mixRequestPath`, `serverStopped`, `rosterCount`,
    added for the live population-mix and world-speed features), but the test
    fixture's `Activator.CreateInstance` call still passed only the original
    two, so .NET could no longer resolve any constructor overload. Fix: the
    fixture now passes all five arguments, with the two new path parameters
    pointing at files under its temp folder (both protocol readers already
    treat a missing file as "no live data", so the extra paths do not need to
    exist) and a `rosterCount` stub returning 0.
    The fifth failure, `PlayerAndBotRatesPersistIndependently`, was
    environmental: `MainForm.PersistXpRate` refuses whenever
    `IsServerRunning()` (a TCP listener check on port 10300) or
    `FindExactServerProcess()` sees a real local server, which is true
    whenever the owner's installed server happens to be running — confirmed
    live during this fix (`CoreServer.exe` listening on port 10300). Fix:
    added a private `_persistXpRateServerRunningOverride` field that
    `PersistXpRate` consults before probing the port or process; production
    code never sets it, so live behavior is unchanged, and the test sets it to
    `false` via reflection (the same pattern the file already uses for other
    private fields) so the assertion no longer depends on machine state.
    Launcher test suite: 127 passed, 0 failed, run while the local server was
    live on port 10300 (confirmed via `CoreServer.exe`), the failing case.
    Real-client check pending (the launcher itself was not changed in
    behavior; this is a test-only fix plus one inert seam field).

58. **The live bot dashboard snapshot fails intermittently.** Every 30–60
    minutes the log shows `Live bot dashboard snapshot failed
    System.UnauthorizedAccessException: Access to the path is denied` at
    `AutonomousBotDashboard.Publish` (`File.Move` over the published file,
    AutonomousBotDashboard.cs:121). Cause: a reader that briefly holds the
    destination file open without delete sharing (an antivirus scan or file
    indexer are the likely candidates; the launcher's own reader already
    opens the file with `FileShare.ReadWrite | FileShare.Delete`, so it is
    not itself the blocker) makes the atomic `File.Move` throw for the few
    milliseconds the hold lasts. Reproduced with a unit test that opens the
    published file with `FileShare.Read` only and releases it shortly after
    `Publish` starts. Fix: `Publish` retries the move up to six times with a
    short growing backoff (25 ms per attempt, roughly a quarter second total)
    before giving up and logging the warning, so a lock released within that
    window no longer drops the snapshot for a cycle. Real-client check
    pending.

57. **`/tc` was registered twice.** `TeleportToExchangeCommand` claims `&tc`
    as its own command (teleport to the capital's Realm Exchange), and
    `scripts/commands/TransferCorpse.cs` also listed `&tc` as an alias of its
    own `&transfercorpse` command. `ScriptMgr.LoadCommands` adds a type's
    primary command first and its aliases after; whichever of the two loaded
    second hit the duplicate key on `Dictionary.Add` and logged
    `ArgumentException: An item with the same key has already been added.
    Key: &tc`, silently dropping that alias (proven both ways: this key
    collision reproduces regardless of load order). Cause confirmed by
    reading `ScriptMgr.LoadCommands` and both command classes; `&tc` had no
    other role in `TransferCorpse`, so removing it from that alias list keeps
    `/tc` on the Realm Exchange command and leaves `/transfercorpse` (its
    only other name) unaffected. `ALL SERVER COMMANDS.txt` no longer lists
    `/transfercorpse (aliases: /tc)`. Added
    `UT_PlayerCommandAvailability.NoCommandHandlerRegistersADuplicateCommandKey`,
    which scans every `ICommandHandler` type across the loaded assemblies for
    a command key claimed by more than one handler; it fails with this exact
    collision before the fix and passes after. Full server test suite: 2330
    passed, 1 skipped (pre-existing, unrelated), 0 failed. Real-client check
    pending: confirm `/tc` still teleports to the Realm Exchange,
    `/transfercorpse` still moves a dead player to a claimed keep, and the
    startup log no longer shows the `LoadCommands` `&tc` exception.
56. **Bot AI ticks are slow and stall the NPC service.** Seen in the
    installed 0.115.0 log on 2026-09-27/28 (about 600 world bots): 43,914
    `Long NpcService.Tick` warnings, 99.7 % of them `BotBrain`, about 5,000
    per hour; median 94 ms, 95th percentile 224 ms, maximum 3,967 ms. The
    game loop fell to about 1,500 of 1,800 ticks per minute (NpcService
    average 12-15 ms, 95th percentile tick 60 ms).
    - Profile (session from 2026-09-27 23:54, 19,849 slow bot ticks): 81 %
      came from world bots in planning mode (think interval 8-12 s, median
      122 ms). 80 read-only `dotnet-stack` samples of the running server
      caught 42 brain turns: 20 in Detour corridor checks (11 in the stalled
      route side-step search, 5 keep-route slices, 3 stable-route planning
      with one SQLite read, 1 zone-point approach), 12 waiting on locks (9 on
      the group coordinator lock, 3 on crowd-control claims), 3 in
      reflection-based hashing of disabled-skill keys, 7 other. In most
      samples the whole NPC service waited for one worker doing a corridor
      search. Bot Leofismund alone logged 1,876 slow ticks of about 160 ms
      while stuck on a route for 53 minutes. Nothing like bug 31 (per-tick
      SQLite saves) was found.
    - Fixed in source: the side-step search of a stalled route now runs in
      8 ms slices across brain turns (same candidates, order and choice; the
      bot stands still and thinks again after 250 ms). The stable-network
      cache no longer rebuilds under one global lock: it is kept 30 minutes,
      refreshed by one bot while the others use the old copy, and a first
      build blocks only its own region and realm. Disabled-skill lookups use
      an explicit key comparer. Solo bots skip the group-coordinator lock in
      the stuck watchdog check.
    - New always-on timing: once per minute the log shows
      `BOT_THINK_PROFILE` (turns, time, turns over 25/100/1,000 ms, top phases
      with total/count/max) and up to five `BOT_THINK_SLOW` lines with the
      slowest turns and their own phase breakdown. `DeathRewards` and
      `CompanionGearGrant` time the reward work of NPC deaths.
    - Still open: 1,165 `Long ReaperService.Tick` warnings come almost only
      from kills by real players with companions (Ked in Darkness Falls,
      average 425 ms; Nova, average 1,432 ms). Suspected cause: synchronous
      SQLite writes on the reward path (companion gear drops and loot) on
      the D: drive, a 5,400 rpm hard disk where 499 slow SQL statements took
      a median of 253 ms. Companions also show single turns of 1-2 s that do
      not coincide with slow SQL. Both are now timed; the group-coordinator
      lock and keep-route slices remain as they were.
    - Live 0.125.0, 11 h (2026-09-28): long BotBrain ticks 1,400–2,500 per
      hour in most hours, median 42 ms, but spikes of about 5,000 per hour
      (06:00, 10:00), p99 1,189 ms and 455 ticks over one second. New hot
      spots in `BOT_THINK_SLOW`: `ExecuteRvr` single turns of 260–480 ms
      (about 42 s total in the spike hours) and the 30-minute
      `StableNetworkCache` rebuild (about 1 s, one turn per region and realm).
    - Round 2 cause (log of the 0.125.0 run, 04:41–18:18, top five slow turns
      per minute): 515 turns with `ExecuteRvr` of 200 ms or more, 486 of them
      without any sub-phase that explains even 30 % of the time, and without
      path queries; the 149 slow `StableNetworkCache` rebuilds spent 129 s,
      again with almost no path queries (the corridor part stayed under 20 ms).
      Both waited for SQLite: a merchant list (`MerchantTradeItems`) reloaded
      its SELECT every five minutes on the brain thread while holding the
      merchant's lock, and every SQLite connection open waits for the single
      write gate, which the slow saves on the old 5,400 rpm disk held for
      hundreds of milliseconds. Every RvR bot without a frontier medallion reads
      the porter merchant's list each turn (7,560 frontier teleports in 11 h),
      and the stable network rebuild reads every stable master's list. After the
      move to the SSD the same first 48 minutes show `ExecuteRvr` peaks of at
      most 255 ms in 3 minutes (0.125.0 on the HDD: 1,234 ms, 16 minutes).
    - Round 2, fixed in source (real-client and live check pending; behavior
      unchanged, only cost and timing): merchant lists load once and then
      refresh in the background on one low-priority worker thread, keeping the
      old list meanwhile; the 30-minute stable network rebuild runs on that
      worker and bots keep the previous network (only the first build per
      region and realm still runs in a turn). The first-leg corridor checks of
      a world bot's stable-route plan run in 8 ms slices, one per turn, with
      the same checks and the same choice; the bot keeps walking and thinks
      again after 250 ms. Solo world bots no longer take the group-coordinator
      lock in `Pulse`. New `BOT_THINK_PROFILE` phases: `RvrChooseDestination`,
      `RvrKeepTarget`, `RvrFrontierTransport`, `RvrEventLockWait`,
      `CoordinatorLockWait`, `CoordinatorSessionUpdate` (lock held in Pulse),
      `CoordinatorMaintenance`, `MerchantItemsLoad`, `DatabaseOpen` (connection
      open including the write-gate wait, brain turns only; nested, so its
      time also counts in the enclosing phase such as `MerchantItemsLoad`) and
      `StableRouteSearchSlice`; the minute line lists 16 phases. A pending
      stable-route search starts over when the bot is more than 1,000 units from
      its start, the goal moves, or the route state resets. Tests:
      `UT_BotThinkCostBoundsRound2`.
    - Round 2 live check: `ExecuteRvr` and `StableNetworkCache` maxima per
      minute well under 100 ms after the first half hour; `DatabaseOpen` and
      `MerchantItemsLoad` rare in `BOT_THINK_SLOW`; `StableRouteSearchSlice`
      max about 8 ms plus one corridor check; compare `CoordinatorLockWait`
      with `GroupCoordinatorPulse` to decide whether the coordinator lock needs
      a per-session split. Still open: the SSD-run peaks of 150–255 ms in
      `ExecuteRvr` (the new phases should name them) and `SelectCamp` /
      `ZoneItineraryStep` path queries of up to 1.1 s.
    - Live measurement pending: compare the hourly count and median of
      `Long NpcService.Tick ... BotBrain`, `SERVER_WORK stage=NpcService`
      and the `BOT_THINK_PROFILE` phase `RouteRecoverySearch` (expected
      maximum well under 100 ms) with the numbers above.
    - High-population live investigation, 2026-09-29 (reported 6,241 bots
      online): the server registry reached 6,210 active world bots. During the
      08:15–08:32 ramp from 1,223 to 6,210, the 95th-percentile logical tick
      rose from 33 ms to roughly 60–90 ms against a 33 ms budget. At 6,210,
      the loop completed about 1,200–1,500 instead of 1,800 ticks/minute;
      world-speed status showed about 0.78–0.83x with one client connected.
      `NpcService` averaged 19–28 ms/tick, with 10–19 ms spent waiting for
      workers. Roughly 1,900–2,000 bots were fighting, with 16,000–19,000
      new PvP engagements and about 1,000 PvP deaths per logical minute.
      Forty read-only managed-stack snapshots caught 19 active BotBrain
      stacks: 13 were in navigation/corridor work, including
      `AutonomousZoneItinerary.CalculateCompleteCorridor` ->
      `LocalPathfindingMgr.PathStraight`. A logged turn made 563 path queries
      in 443 ms. The seam-candidate and town-route callers can still make many
      corridor checks in one turn despite the sliced recovery/stable searches.
      Two snapshots caught `BotBrain.TryHandleAutonomousRealmExchange` ->
      `AutonomousBotEconomy.TryList`: one waited on a monitor, another was
      inside a synchronous item save/SQLite connection close. `TryList` takes
      the Exchange transaction and status-write locks on the NPC worker;
      background status and clock saves use the same write gate.
      `DatabaseOpen` measures connection opening only, so it does not account
      for the outer lock wait or the rest of that transaction. This is a
      confirmed blocking path and a plausible source of the 1–3.7 s bot turns
      with only 3–6 ms attributed to `DatabaseOpen`; the snapshots do not
      prove every such turn uses it. Next: measure the Exchange lock and full
      transaction, move durable listing work off NPC workers without changing
      item/coin outcomes, and bound ordinary itinerary seam checks across
      turns without changing route choice. No source fix or deployment yet;
      post-fix live and real-client verification remain pending.

55. **Companions buff before resurrecting a dead player.** Reported by Aaron
    on 0.116.0 (2026-09-28). Cause: resurrection only tried the strongest
    known rank; when it was unaffordable the healer treated it as "nothing to
    do" and went on buffing. Source fix 0.117.0: in combat companions cast the
    strongest resurrection their power allows at once (the smallest when
    power is low); out of combat they cast only their best resurrection and
    spend no power on buffs until it is affordable, then resurrect, then buff.
    Covers the owner, his group and his squads. Real-client check pending.

54. **Companions occasionally hang at the Darkness Falls entrance stairs.**
    Reported by Aaron on 2026-09-28 (0.115.0): single companions stay at the
    tall entrance steps that a player simply walks down. The bug 12 stair
    links load (no DF navigation warning, repaired mesh cached), so the steps
    are linked, but any follow path that still fails left the companion
    standing: with a formation order, NoPath/PartialPath only paused and
    turned it toward its slot, while native pets are placed at their owner's
    feet out of combat. Exact failing positions are not logged. Fixed in
    source 0.116.0: after 2 s of failed follow paths, out of combat and
    within 1,024 units, the companion joins the floor beneath its leader.
    Real-client check at the DF entrances pending.

53. **Bomber companions burn their power on debuffs.** Reported by Aaron on
    2026-09-27 (playing on Stefan's server, version not confirmed): a
    Suppression Spiritmaster bomber ran out of power quickly. Cause: pure
    debuffs (instant strength, strength/constitution area, combat speed and
    dexterity debuffs) are applied to every mob that lacks them, before the
    ordinary damage rotation and whenever no bomb is ready, and again after
    each 60 s expiry. Debuffs pay full power (the caster discount covers
    damage only): one set cost about 113 power at level 50, about seven
    bombs. Expected: debuff once or twice per fight. Fixed in source
    0.110.0: player-led companions cast each pure debuff type at most once
    per fight, area debuffs at most twice; the budget renews after 5 s out
    of combat. Installation and real-client verification pending.

52. **Companions can stand idle after the build expansion.** Reported
    2026-09-27, Paladin given as an example; installed version and equipment
    not yet confirmed. Source regression in 0.96.0 (`1a50ffb`): automatic
    plans replace the seeded weapon profile but preserve saved equipment and
    disable weapon reconciliation. Reproduce with an automatic thrust Paladin
    retaining only a slash/crush weapon and no invested line for that weapon:
    both equipped slots fail the build filter, so combat clears its target.
    Expected: use the legal equipped weapon while awaiting matching gear.
    Fixed in source 0.109.0: only when neither equipped weapon matches the
    build, allow a functional, class-legal equipped weapon as a combat fallback.
    Equipment, locks, and upgrade eligibility are unchanged. Workaround:
    equip a weapon matching the selected build. Installation and real-client
    verification of the reported Paladin and other affected companions pending;
    automated tests skipped per project instructions.

51. **Companion bombers keep waiting in PvE before they bomb.** Reported by
    Aaron on 0.107.0. Cause: three waits stacked and restarted on every
    target switch: the 2.5 s tank-aggro grace (per focused mob, and reset
    whenever the bombs were on cooldown), the run into the pile's centre
    (up to 3 s, restarted per target and on every drift of the pile), and
    the 1.2 s volley hold. Fixed in source 0.108.0: the tank grace runs once
    per fight and ends as soon as the tank holds the pull or a groupmate has
    bombed; a bomber already in the knot stays in position for the next mob,
    keeps its run clock across target switches, and a drifting pile gets a
    1.5 s catch-up run. Real-client check pending.

50. **Companion combat is mirrored into the owner's own combat chat.**
    Reported by Aaron on 0.105.0: every companion's damage showed in his
    chat. Cause: companions use the pet brain interface, so the pet-owner
    message paths (`SpellHandler.MessageToCaster`, `GameLiving` pet hit and
    block messages) forwarded their hits, resists and blocks. Fixed in source
    0.107.0: companions and their pets are treated as groupmates, the
    player's own pets keep their messages. Real-client check pending.

49. **Companion rewards feel like weapons only.** Reported by Aaron on
    0.105.0. Cause: armor and jewelry upgrades are worn at once, weapons of
    types the class never uses pile up (33 staves), and a full backpack sold
    the lowest `EquipmentValue` first, which ranks by DPS/AF and so sold
    jewelry before weapons; drops were 50 % armor, 30 % weapons, 20 %
    jewelry. Fixed in source 0.107.0: 45 % armor, 35 % jewelry, 20 %
    weapon/shield, weapons only of usable types, and unusable gear is sold
    first, then the lowest level/quality/bonus value. Real-client check
    pending: more armor and jewelry in the companion inventories.

48. **DF weekly quests never count.** Reported by Aaron on 0.105.0 at the
    Midgard DF entrance (Patrick). Cause: "Darkness Falls Invasion" counted
    companions as group members, so an 8-member companion group needed purple
    mobs; "Femurs" counted only real enemy players, never enemy bots. A
    companion-led group also had no player leader (null). Fixed in source
    0.107.0 for all three realms: only real players raise the con bar (alone
    with companions yellow and above counts), con is checked from the player,
    and enemy-realm bots count for Femurs. Real-client check pending.

47. **Companions crowd onto the player in dungeons (DF).** Reported by Aaron
    on 0.105.0: in Darkness Falls the companions stood almost exactly where
    he stood. Cause: the indoor formation radius was 40-54 units, less than
    a body width, and indoors they also jumped after every first step.
    Fixed in source 0.106.0: indoors they keep 90-160 units (a step or two)
    and ignore the leader's first steps like outdoors. Real-client check
    pending: companions spread around the player in DF.

46. **Healer speed replaces the skald's speed song.** Reported by Aaron on
    0.105.0: the buff bar showed Flow of Movement instead of Heavenly Song of
    Travel. Cause: the healer's new support build (Augmentation) maintained
    its own weaker speed pulse (131 vs 204), which replaced the song. Fixed in
    source 0.106.0: a bot does not run a speed that a living groupmate bot
    covers with a stronger one, and ends its own weaker speed pulse. Real-client
    check pending: Heavenly Song of Travel in the buff bar with the healer
    grouped.

45. **Bots never got their top spec spells (Skald stuck on speed 4).**
    Reported by Aaron on 0.99.0: the buff bar showed Magnificent Song of
    Travel (speed 4) although Freunborg has Battlesongs 46. Cause:
    `Specialization.GetSpellLinesForLiving` capped every bot spec line at
    75 % of the character level (38 at level 50), ignoring the trained spec,
    so Heavenly Song of Travel (43) and every higher spec spell never entered
    any bot's spell list. Fixed in source 0.101.0: bots use their trained spec
    level; untrained lines keep the fallback. Real-client check pending:
    Heavenly Song of Travel in the buff bar.

44. **Companion inventory window jumps back to the top after taking an item.**
    Reported by Aaron on 0.96.0: after dragging an item out of a companion's
    **[Open inventory]** window, the window scrolls to the top, so taking
    several items from the lower backpack means scrolling down every time.
    Expected: the window keeps its scroll position. Cause: after every move
    the server re-sent all 100 slots with the house-vault window type, which
    the client treats as opening the window. Fixed in source 0.99.0: only
    opening uses that type; moves send a slot update. Real-client check
    pending: take several items from the lower backpack in a row.

42. **Every player kill freezes the server for about 0.4-6 s (companion gear
    rewards).** Reported on 0.89.0/0.91.0-dev by Aaron: "when several mobs die
    at once it almost always lags". Evidence from the installed logs: 524 of
    536 `Long ReaperService.Tick` warnings name the player as killer (today
    min 415 ms, median 1.4 s, max 6.2 s); autonomous-bot kills barely appear.
    Cause (code-read, timing split inferred): `AbstractServerRules.OnNpcKilled`
    (line ~1092) calls `PlayerCompanionGearRewards.AwardPvePartyGear`, which
    grants an item to **every** companion in the group. The chance is
    `min(1, XP_RATE * 0.25)`, so at `xp_rate=10` it is 100 %: 7 companions =
    7 items per kill. Each grant runs synchronously on the game loop inside
    `ReaperService` under `DatabaseWriteLock` (shared with the 2 s bot
    persistence flush) and writes an atomic transaction (INSERT ItemUnique,
    INSERT Inventory, UPDATE player_companions). With full backpacks it also
    sells surplus (DELETE Inventory, UPDATE DOLCharacters). `Pooling=False`
    plus WAL makes each connection close checkpoint and fsync, which is slow on
    Windows. Several simultaneous deaths queue behind each other.
    Workaround: none short of fewer companions or a lower XP rate.
    **Decision 2026-09-27 (Aaron):** only one companion receives an item per
    kill, and a full backpack must not keep filling up (see task 31).
    Source 0.99.0 rolls once per kill per owner for one random eligible
    companion (one database write instead of up to seven), and a full backpack
    sells up to 16 surplus items in the same write. The per-write cost itself
    (options A-C below) is unchanged. Real-client check pending: kill lag with
    several mobs dying at once.
    **Original questions for Stefan (owner decision):**
    - Is one item per companion per kill intended, and should the chance really
      scale to 100 % with the XP rate?
    - Aaron's proposal: a **loot pool per fight** instead of per-companion
      rolls. The kill (or the whole pull) rolls once into a shared pool, and
      the pool is handed out when the fight ends, e.g. to the companion who
      benefits most. Fewer items, fairer spread, and the DB writes happen once
      after combat instead of during it.
    - Technical fix options regardless of the gameplay answer: (A) roll during
      the kill but persist the grant off the game loop; (B) keep one SQLite
      connection open (pooling) to stop checkpoint-per-close; (C) batch all
      grants of one tick in one transaction.

43. **Companion bombers PBAoE from the edge of the pile, not its centre.**
    Reported by Aaron on 0.96.0 while levelling with two Suppression
    Spiritmasters: the bombers often cast outside the pack instead of running
    into the middle. Cause (code-read): `BotBrain.ApproachForOffensiveSpell`
    follows the focus mob to `min(160, radius/2)` (150 units for a radius-300
    Soul spell) and casts from wherever that leaves the caster, while
    `SpellHandler` applies linear falloff `1 - distance/radius` from the
    caster. A mob at 150 takes about half damage; the far side of the pile
    takes almost none. Fixed in source 0.97.0: bombers path to the centroid of the pull
    (the knot on the tank) and bomb within 50 units of it; a blocked run
    bombs where it stands after 3 s. Real-client check pending: PvE pulls
    with two Suppression Spiritmasters.

41. **Casters run out of mana too quickly, especially while bombing.** The
    2026-09-26 source audit found bot damaging spells use native mana costs and
    continuous high-rank PBAoE could outpace combat regeneration. Source 0.89.0
    reduced damaging spell mana costs by 50% for offensive caster classes.
    Source 0.95.0 raises that reduction to 70% and reduces mana costs by 90%
    for all buffs and pet summons. Other spells keep their existing costs.
    Installation and real-client checks across companion and
    player caster groups remain pending.

40. **Grouped bot Healers do not cast their learned Celerity buff.** Reported while source was 0.87.0 (installed version unknown): a grouped Healer bot with Celerity available does not cast it. Expected: Celerity is applied to the Healer and eligible group members when missing, including during combat. The defensive selector omitted Celerity from its target choices, the in-combat filter skipped maintained buffs, and effect handling returned Unknown for Celerity, preventing reliable coverage checks. Source fix 0.88.0 recognizes Celerity group targets, permits this buff during combat, and tracks its dedicated melee-speed effect. Installation and real-client verification pending.

39. **Hostile keep guards attack companions but cannot be attacked by a player; hostile doors admit players.** Reported at Fensalir Faste while source was 0.85.0 (installed version unconfirmed): guards attack companions and players, but the player cannot attack them; a normal-player friend right-clicked all doors to traverse them while the lord was alive. The keep enemy check accepted companion `IGamePlayer` objects but excluded real `GamePlayer` objects, so the server treated humans as friendly at both guards and doors. The PvP NPC-create packet also gave realm NPCs the viewer's guild ID; neutral Warden guards need an enemy display realm. Source fix 0.86.0 evaluates human and bot players against keep ownership, withholds friendly guild presentation from hostile guards, and displays them as enemies. Installation and real-client guard targeting, player/companion attack, hostile door blocking, and friendly-owned keep checks pending.

37. **Tanks queue a taunt their wielded weapon cannot execute.** Reported on 0.80.0 for an Armsman companion that barely held aggro. `TryPriorityTaunt` chose the highest-level learned taunt regardless of the weapon in hand, e.g. Distract (Polearm 12) while wielding sword and shield; the swing then dropped the style and no taunt fired. Affects every tank with taunts in several lines: Paladin, Armsman, Reaver, Thane, Warrior, Hero and Champion, companions and world bots alike. Source fix 0.81.0: taunt selection only considers styles the active weapons can execute. Unit test covers polearm, sword-and-shield, missing line and level cases; real-client aggro check pending.

36. **Gear trades with a nearby companion fail with one generic message.** Reported on 0.78.0: items could not be taken although the companion stood close. Cause: trades required 256 units (loot pickup distance) while companions follow up to 400 units away, plus 10 s without combat for owner and companion, no casting, no aggro and no fighting pet; every case showed the same "nearby … out of combat" message. Source fix 0.80.0: trades reach the 400-unit follow distance, and the refusal names the actual blocker (distance with both values, remaining combat seconds, casting, pet, group or zone). Unit tests cover each blocker; real-client check pending.

35. **Companion bag window shows gear the owner cannot take out.** Reported on 0.78.0: some items in the [Open bag] window cannot be dragged into the player's inventory. Cause: the window listed every backpack item, but starter gear, items of unknown legacy origin and untradable or special items are deliberately blocked from returning to the owner. Source fix 0.80.0: the bag window lists only items the owner can take out; the Gear tab still shows everything. Moving within the bag onto a slot that holds hidden gear now explains why. Unit test covers the visibility rule; real-client check pending.

34. **Companion bombers stay at range instead of bombing PvE pulls.** Reported on 0.76.0 in a player group with Spiritmaster companions: bombers rarely moved into the pack and mostly cast from a distance. Cause: the PvE bomb pull counted only the per-member focus set (each member's current NPC target). Healers target party members and damage dealers assist one target, so the set rarely reached the three targets that Auto requires; the bomb then counted as not ready, so no approach started. Source fix 0.78.0: NPCs inside the bomb radius that are already fighting a group member or a member's pet also count; idle spawns still do not. Unit test covers the engaged-add filter; real-client check of approach and bomb frequency pending.

32. **A multiplayer /pull transfers companions to the wrong player.** With two real players in one group, each with three summoned companions, either player can use /pull and all nearby companions follow that player after combat. Expected: each companion keeps following its summoner. Actual: the pulling player becomes their leader until /companions reset. Source 0.76.0 limits the pull roster and wait gate to the issuing player's assigned companions. Installation and real-client two-player verification pending.

31. **Melee companions swap shield and two-hander every tick, stalling the server.** Reproduce with a persistent Thane or Skald companion carrying both a shield and a two-handed weapon. Expected: the better setup stays equipped. Actual: `TryGetEquipmentUpgrade` compared a candidate only against its own target slot; with a two-hander worn the shield slot is empty and vice versa, so each counted as an upgrade and `TryApplyPendingPersistentCompanionUpgrade` swapped them on every think with an atomic SQLite save. Observed on 0.73.0 with two Thanes and a Skald (level 30): about 16,000 "Long NpcService.Tick" warnings, average 80 ms and up to 1.4 s, felt in the client as periodic freezes. Source fix: the upgrade check also subtracts the weapons the move displaces (two-hander versus right and left hand). Local installation removed the stall pattern; sustained real-client observation remains pending.

29. **Separated PvE parties can wait on combat or arrivals in another region.** The 0.74.0 observation caught safe members paused by a distant party member's combat; source also only recognized arrivals immediately across the leader's next region edge, ignoring members already in the final camp region. Source 0.75.0 scopes ordinary combat/recovery holds to nearby living members and permits leaders to advance toward members at the destination region. Actual routes, combat defense and existing deadlines remain required. Installation, multi-edge travel and combat/recovery verification pending; other travel failures are not claimed fixed.

    **Still occurring on installed 0.115.0 (live log, run from 2026-09-27 23:54).**
    Group `1e7771ca…-2b4d33-166` waited in West Downs (region 1) while its
    Skald Sivildrid kept dying at the Svasud Faste bind in region 100:
    75 travel holds, 37 resurrection waits and 40 `release-and-rejoin`
    timeouts (33 at the same corpse spot) from 01:21 to 03:04. The task
    started 01:00:52 and expired 03:04:53 with no camp. Five other parties
    (`c0e34e19…-134`, `1e7771ca…-270`, `1e7771ca…-087`, `ab173b5f…-142`,
    `91108e2d…-266`) logged 508–2,700 resurrection waits each for one corpse
    20,000–125,000 units from the camp, with no timeout at all; `-134` waited
    1 h 46 min for its dead leader until the task expired.
    Causes proven in `AutonomousBotGroupCoordinator.cs`:
    - Members at the camp call `MarkGrinding`/`PublishCamp` on every AI pulse
      and overwrote the "Waiting for resurrection" phase. The next pulse
      counted a new casualty and reset the 60-second release window, so the
      corpse was never released (a new wait every 7 s on average).
    - The corpse hold and its combat check used every party member, in any
      region, and even a stale combat flag on the corpse itself.
    - A released member that died again on its way back had no limit; the
      post-wipe regroup waited for it until the task expired.
    Source fix (no version yet): camp pulses only record the phase to
    resume during a casualty hold; a corpse with no living member in its
    region within visibility range releases at once
    (`AUTONOMOUS_GROUP_REMOTE_CORPSE_RELEASE`); combat only holds a corpse
    when it happens near that corpse; after the first death plus two more
    deaths without getting back to the leader, the party drops the member
    (`AUTONOMOUS_GROUP_REJOIN_FAILED`) and plays on; a pair ends and both
    return to matchmaking. Tests: `UT_AutonomousSeparatedPartyResurrection`.
    Not fixed: bots dying repeatedly at a bind point used as an RvR staging
    spot, and a regroup that waits for a member who never dies and never
    arrives. Real-client / live-log check pending.

26. **World-speed status publication intermittently fails.** Reopened: this
    was marked Finished without a fix. The currently running install logged
    another `System.UnauthorizedAccessException: Access to the path is
    denied` at `OfflineWorldSpeedControl.PublishStatus`
    (OfflineWorldSpeedControl.cs:571) at 01:37:16 while replacing
    `world-speed.status.json`, the same failure as the original 2026-09-26
    report. Cause: same family as bug 58 (the live bot dashboard) — a reader
    that briefly holds the destination file open without delete sharing (an
    antivirus scan or file indexer are the likely candidates) makes the
    atomic `File.Move` throw for the few milliseconds the hold lasts; the
    launcher's own status reader (`WorldSpeedProtocol.ReadFreshStatus`) used
    plain `File.ReadAllText`, which does not grant `FileShare.Delete`
    either, so it could itself have been a contributing blocker. Fix:
    `PublishStatus` now retries the move (via the new shared
    `AtomicFilePublish.MoveWithRetry` helper, also used by bug 58's fix) up
    to six times with a short growing backoff before giving up and logging
    the error; the launcher's `WorldSpeedProtocol.ReadFreshStatus` now opens
    the status file with `FileShare.ReadWrite | FileShare.Delete`, matching
    the dashboard reader. Reproduced and covered by a unit test that locks
    the status file with `FileShare.Read` and releases it during the retry
    window. Real-client check pending.

24. **PvP opponent evaluation throws when a group has no living nearby members.** Confirmed in the 0.72.0 live-session audit: `VisibleParty` filtered all members out and then called `Average`, interrupting NPC AI processing. Source 0.73.0 falls back to the resolved combatant's effective level for an empty visible group. Installation and sustained PvP observation remain pending.

22. **Cruachan Gorge mob camps are sparse near Druim Ligen.** Reproduce by walking out of Druim Ligen into Cruachan Gorge and looking for ordinary mobs. Expected: visible PvE activity along the approach. Actual: the installed world has only 26 neutral mobs across Cruachan Gorge; the closest live mob is about 8,500 world units from the border keep. Teleport fix 0.66.0 lands nearer an existing camp. Source audit 2026-09-26: the nearest archived candidate is about 7,300 units away but falls outside the mapped period-location witness radius, and no native route proof exists for it. Zone-level period reports do not establish a closer camp. No uncorroborated spawn was added. Workaround: travel farther into the zone to an existing camp. Follow-up source 0.72.0 restores 219 archived Cruachan Gorge rows using retained zone/species/level rosters, increasing its ordinary mob population from 26 to 245. This is roster-backed density restoration, not independent period-map or native-path proof for every location; installation and the border-keep approach require real-client verification.

23. **Keep doors require repeated clicks.** The door-request handler skipped ordinary keep doors, and overlapping door/object interactions could traverse twice. Source fix 0.72.0 dispatches ordinary keep-door requests and suppresses duplicate traversal for 750 ms; bot access uses guild hostility instead of realm. The Fensalir Faste report in entry 39 exposed a separate human hostility bypass, fixed in source 0.86.0. Neutral/defeated and friendly-owned keep entry/exit, blocked hostile gates, and client packet behavior await installation and real-client verification.

Source inventory audit 2026-09-26: the implementations cited in entries 1–17 remain in this checkout. Their installation and real-client checks were not performed in this source-only pass, so all remain pending.

1. **Companions and autonomous bots appear undergeared.** Sparse template tables no longer leave new persistent companions' armor and shield slots empty. Existing saved companions refresh only their bound starter armor and weapons as they level, retaining earned and manually equipped items. Autonomous bots repair missing starter armor and appropriate shields when loaded. Mixed-realm drops now pair the selected member's class with their realm and prefer a bot that can equip the item. Source fix: 0.56.0; installation and real-client equipment inspection pending.
2. **Group members become enemy-selectable and lose group colors.** NPC create packets now apply the friendly guild ID only to allied gamebots. Group membership changes refresh the friendly IDs of all remaining grouped gamebots for each human viewer. Source fix: 0.56.0; installation and real-client Tab/color verification pending.
3. **Tanks do not reliably peel adds off healers and bombers.** Tanks prioritize attackers of healers, then bomb casters, then leaders; group attacks raise tank threat immediately, and an offensive cast on a different target yields to an urgent peel. Source fix: 0.56.0; installation and real-client combat verification pending.
4. **Refresh buffs after upgrades.** Companion buff maintenance compares active buff strength and allows a stronger rank to replace a weaker effect. Source fix: 0.49.0; installation and gameplay verification pending.
5. **Prioritize specialization buffs while covering base buffs.** Player-led companions prefer specialization-line buffs. A base buff is skipped only while another live group member has an equal or stronger compatible buff active on the same target; a human player's known spell alone does not count. Source fixes: 0.49.0 and 0.52.0; installation and gameplay verification pending.
6. **Automatically use Guard and Protect intelligently.** Companion protection assignments distribute Guard and Protect across uncovered group members, prioritize healers and bomb casters, and respect the native ranges (256 and 1,000 units). Existing effects reserve coverage only while their source remains in range. Source fixes: 0.49.0 and 0.52.0; installation and gameplay verification pending.
7. **Update and choose summoned pets.** Idle player-led companions upgrade to stronger learned summons, and Enchanters prefer Underhill Ally when available. Repeating the same summon is allowed only after the owner levels enough to improve that pet. Source fixes: 0.49.0, 0.51.0, and 0.52.0; installation and gameplay verification pending.
8. **Use bomb spells and coordinate bomb groups.** Eligible player-led casters prioritize PBAoE spells on sufficiently large focused pulls. The Companion Manager saves an Auto/Bomb/Off preference per companion, and bombing waits up to 2.5 seconds for tank aggro, restarting that wait for each newly focused target. Source 0.65.0 makes Bomb preference use the highest learned rank and permits clustered PvP opponents already fighting the group, without bombing idle or mezzed players. Source fixes: 0.49.0, 0.52.0 and 0.65.0; installation and real-client PvE/PvP verification pending.
9. **Make mobs form groups and award group bonuses.** Mob BAF now resolves companion pullers and controlled pets to their player-led group, counts companion members for add selection, and preserves the existing add-based experience bonus. Source fix: 0.49.0; installation and gameplay verification pending.
10. **Explain the Server population controls.** Preset, type-mix, danger, and world-shape controls now have plain-language tooltips. Source fix: 0.49.0; launcher installation and hover verification pending.

11. **Dungeon mobs were missing and populations were thin.** A read-only audit of all 29 supported dungeon zones found 2,310 levelled neutral mob records archived by the Classic 1.65 population profile and absent from the current world database. The new Setup migration restores the exact archived rows for all 15 Classic realm dungeons and four supported Old Frontiers dungeons. Shrouded Isles and Darkness Falls rows were already restored. The [2002 map compilation](https://www.scribd.com/document/144573276/DAOC-Map-Compilation-Book) and [Prima atlas](https://www.scribd.com/document/131856275/Dark-Age-of-Camelot-the-Atlas-Prima) document period dungeon layouts and rosters; the archived world records provide the numeric spawn baseline. This source fix is version 0.48.0 and still needs deployment and real-client verification.

12. **Companions get stuck atop the Midgard Darkness Falls entrance stairs.** Source fix 0.61.0 adds three bidirectional stair links at each DF entrance, packaged as nine hash-checked tile replacements in a temporary cached mesh, and preserves exact stair endpoints during bot path following. All other installed mesh tiles remain unchanged. Native complete return routes passed for 1,417 monster spawns. Installation and real-client up/down movement with companions and autonomous groups remain pending.
13. **PvE bots initiate unwanted PvP in frontiers and shared dungeons.** The early frontier scan ignored durable PvE objectives and normal recovery/strength checks; the separate DF scan also initiated fights while on PvE work. Source fix 0.61.0 gates frontier hunts through the common opportunity policy and removes the redundant dungeon scan, retaining real defense and committed siege combat. Installation and live activity/death balance verification remain pending.
14. **Player-led companions do not fully support keep door attacks and ram boarding.** Source fix 0.63.0 allows aggressive and defensive companions to assist the player's attack on a closed enemy keep door, commands class pets to it, lets Theurgists repeatedly summon against it, and boards nearby companions into available ram seats with dismount cleanup. Installation and real-client verification of door damage, pet casts, seat visuals, capacity, and dismount behavior remain pending.
15. **Grouped Healers do not use their learned area stuns.** The support path did not select Pacification area stuns. Source fix 0.63.0 adds a cast decision for at least two already engaged enemies, protects mezzed targets and idle bystanders, and favors clusters near an active PBAoE caster. Installation and real-client verification with Tri-spec and Pacification Healers, including a bomb group and urgent healing, remain pending.
16. **`/gc form` stalled with companions in the group.** Source fix 0.64.0 counts only human founders for confirmation and also allows founding alone at a registrar. `/gc invite` immediately joins an owned companion to the guild; persistent companions retain membership, and equipped cloaks and shields show the chosen guild emblem. Installation and real-client checks of founding, invitations, emblem updates, and relog persistence remain pending.

17. **Keep Chief claim prompt is silent and realm frontier teleports enter New Frontiers.** At the claimable keep near Druim Ligen with an eight-member group, the legacy Chief interaction rejects the player before offering a claim; cached group area membership can also miscount nearby companions. The shared teleporter sends Forest Sauvage, Uppland, and Cruachan Gorge to region 163. Source fix 0.66.0 aligns the Chief with `/gc claim`, counts group members at their actual positions, removes Agramon travel, and routes those three destinations to Old Frontiers regions 1, 100, and 200. The installed world database already contains neutral Old Frontiers mobs, though camps near border keeps are sparse. Installation and real-client claim, teleport, and mob-visibility verification remain pending.

19. **Tank companions may not use styles or their specced weapons.** Legacy saved companions without a valid persisted build plan now align their seeded weapon plan with invested weapon specializations before restoring saved equipment. A valid saved plan and all saved equipment still take precedence. Source fix: 0.71.0; installation and real-client style and equipment verification pending.
20. **Many autonomous groups expire while traveling to their camp.** Camp selection now estimates travel for the slowest member across region crossings, rejects camps beyond a 20-minute planning budget, and checks candidate corridors before travel. The 30-minute deadline is unchanged. Source fix: 0.71.0; installation and real-client route and deadline verification pending. The installed 0.73.0 observation still recorded thirteen camp-travel expirations across formation, combat, recovery and dungeon-staging states. Version 0.74.0 adds camp coordinates and member movement/combat/distance diagnostics; route failures remain pending rather than being declared resolved.
    Log check 2026-09-28 on installed 0.115.0 (3 h 26 min): 24 parties expired
    before reaching camp (about 7 per hour) against 140 that reached camp and
    started (85 %). Reduced, not eliminated; stays pending. Task 47 package C
    (task clock from camp arrival, camps near the rendezvous) targets the rest.

21. **Server freezes when simultaneous effect changes deadlock.** Effect transitions now release their state lock before processing the owner's effect list, and an expiring same-spell effect can be replaced without waiting on its state lock. A bounded concurrency regression covers the expiration/replacement cycle. Source fix: 0.71.0; installation and sustained real-client server verification pending.

## Finished

33. **Autonomous bots cross Darkness Falls without staying to work there.** Observed from inside DF with Astreunhild, Dagunildveig and Egiliunulf passing through; the logs do not record their exact in-dungeon goals, so individual routes remain unproven. Source audit found the shared region search could use DF as an intermediate shortcut to unrelated destinations. Source 0.79.0 excludes that transit path while retaining explicit DF destinations and egress from DF. Installation and real-client observation of these bots, DF camp arrivals, and cross-realm travel remain pending.
    Log check 2026-09-28 on installed 0.115.0 (23:54–03:20, 3 h 26 min): all 38 group snapshots with a member in region 249 also had their camp in Darkness Falls (explicit DF camps), no transit-only crossing found. Proven fixed.

28. **Rendezvous recovery throws while an actor has no current zone.** Installed 0.74.0 logged a `NullReferenceException` through `GameObject.CurrentAreas`, town detection and rendezvous reselection at 19:52:49 on 2026-09-26. The getter dereferenced a missing zone, interrupting that bot's goal processing. Source 0.75.0 uses a null-safe captured-zone area lookup in town detection/naming. Installation and zone-transition/recovery observation pending.
    Log check 2026-09-28 on installed 0.115.0 (3 h 26 min): zero `NullReferenceException` in the whole run; `Couldn't find a zone for` fired 352 times as a plain WARN, i.e. the exact scenario is now handled without interrupting the bot. Proven fixed.

30. **One free population seat cannot fill an assembling PvE party.** Source audit after the 0.74.0 party-size decline found a minimum-two free-seat return before the backfill pass. Source 0.75.0 permits that pass with one free seat and applies the two-seat gate only to new-party creation. This is one confirmed source defect, not an established explanation for the whole observed decline. Live roster-size verification pending.
    Log check 2026-09-28 on installed 0.115.0 (3 h 26 min): `AUTONOMOUS_GROUP_ASSEMBLED size=8` 28 times, `AUTONOMOUS_GROUP_FORMED size=8` 13 times, 558 recruitment results with `added>0`; backfill is not stuck at a two-seat gate. Proven fixed.

25. **Initial guild recruitment loses invitations and late PvE recruitment skips camp validation.** Source audit following the installed 0.73.0 observation found that invitations ignored existing partial parties and stopped when eight peers were waiting; late PvE joins checked the rendezvous but not whether the planned camp remained valid. Source 0.74.0 advertises bounded initial-party vacancies at safe task boundaries, removes the waiter cutoff, and repeats the camp usability check before a late join. Failed probes yield briefly to other candidates, and missing roles are recalculated after each join. Installation and sustained full-party/route verification remain pending; these source defects do not establish the cause of every observed travel failure.
    Log check 2026-09-28 on installed 0.115.0 (3 h 26 min): no hard stop in guild recruitment (statuses only "Waiting for preferred party size" 1,435 and "No reachable candidate" 518), parties grow to eight, and the late-join camp re-validation is active (`AUTONOMOUS_GROUP_CAMP_REJECTED` 29 times with real reasons). The two named defects are proven fixed; other travel failures stay in entry 20.

38. **Server unit tests fail in bulk depending on filter and order.** On 0.80.0, `dotnet test source/server/Tests/Tests.csproj -c Release --filter "FullyQualifiedName~Companion|FullyQualifiedName~Bomb|FullyQualifiedName~BotBrain|FullyQualifiedName~Style|FullyQualifiedName~Taunt|FullyQualifiedName~BotCombat|FullyQualifiedName~Tank"` fails 133 tests with `TypeInitializationException: The type initializer for 'DOL.GS.GameObject' threw` (inner NullReferenceException). Other filters pass or fail intermittently (19 tests), and `UT_BotWeaponStats` alone fails 4. Likely a test touches `GameObject` before any `EpicTestServerScope` exists, which poisons the type for the whole run. Impact: suite results depend on selection and order; product code unaffected. Not yet investigated.

27. **Missing NPC template 5232525.** Installed 0.73.0 logged one missing-template error during the 2026-09-26 autonomous session. Expected: the requested NPC template resolves; actual: lookup failed. The spawning caller and gameplay impact remain unidentified; no template was guessed or added. Reproduction beyond the observed log event and workaround are unknown.

18. **Hasteners still fail to give speed at Galpen.** Reproduce by using a Galpen hastener as a Troll. Expected: the allied player receives the speed effect when eligible. Actual: the hastener says `EN SpeedBlockedRealm` and gives no speed. The 0.71.0 source fix added realm-aware checks; this report suggests it may not cover the Galpen/Troll case, but the installed version, combat state, and other active speed effects were not provided.
    **Fixed in source 0.99.0 (tests only).** Cause: `UT_AutonomousLootFlow`
    touched `SkillBase` first without language strings; the static
    constructor failed silently and poisoned every later test (18 vs 84
    failures by order). An assembly `[SetUpFixture]` now initializes logging,
    language, `GameObject` and `SkillBase` once; the logger tolerates missing
    initialization; stale expectations (0.95.0 mana discounts, 0.96.0 builds,
    frontier-garrison spawn count, an optional parameter) were updated; the
    buff pet pass no longer scans the realm when nothing is affordable. Full
    server suite: 2,272 passed, 1 skipped (needs database class data).

None.
