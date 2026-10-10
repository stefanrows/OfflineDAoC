# Tasks and Ideas

Capture rapid-fire tasks, feature requests, and ideas here. Include the desired outcome, scope, and any constraints or acceptance checks that are known; leave unknown details explicit rather than inventing requirements. Recording an idea does not authorize implementing the backlog. Bugs belong in [BUGS.md](BUGS.md).

When a task is done and its required verification is complete, move it out of its current section into **Finished** immediately, mark it **Done**, and record a brief result and completion version (or date for tracking-only work). Do not leave completed items in **Open**. If implementation still awaits installation or real-client verification, keep it under **Implemented in source; installation verification pending**, recording the implementation version and outstanding checks, until those checks are complete. Preserve completed entries as history.

## Open

109. **Battleground server-built keeps and autonomous gamebot participation: real-client spike (idea, not authorized).** Owner, 2026-10-10, after bug 125.
     Only Murdaigean's native keep is restored from client data. The client
     zone data has no keep model for 234, 235, 236, 238 and 240, and only keep
     rows without a keep model for 237 and 242. Building component keeps (walls,
     gates, towers) there is a server-side build, but it needs a real-client
     rendering check first, because component skins and positions must look right
     in the client, and navmesh walls that match the built geometry, which the
     current meshes lack. Autonomous gamebots are also not participants yet:
     `NpcMovementComponent.cs` around line 652 refuses their paths through
     `AutonomousRealmBoundary`, and they have no porter route into these maps.
     Scope if authorized: one map spike for rendering, navmesh and gate
     interaction, then a decision. Do not build before the owner asks.

107. **Observe the owner-started 0.222.0 session at selected 10x speed.** Owner,
     2026-10-10. **Paused at the owner's request on 2026-10-10.** Captured
     evidence covers 09:05:35-10:03:48 CEST. Assess actual keep-raiding
     improvements, battlegrounds, keep progression, movement and realm abilities.
     Server PID/path/session and installed deployment were verified. Private
     collection outside Git retains dated logs, speed/RvR/live-roster snapshots
     and short Windows-native read-only queries of autonomous bots and keeps;
     the collector has exited and the five-minute chat heartbeat is paused.
     A private handover retains the review cursor and notification history.
     The game server remained running when observation paused. No gameplay actions,
     server lifecycle changes, settings/save writes, tests or deployment.
     Result so far: one coordinated two-party launch physically mustered 16
     living bots, four healers and two equipped ram operators. The attack
     failed after combat losses, with no recorded ram deployment/hit or capture;
     the keep owner and living lord were preserved. Single-party camp readiness
     was observed, but those undersized armies expired before launching.
     Physical ram purchase was observed; equipped return to muster remains
     unverified. Eighteen guild-held keeps progressed from level 2 to 5.
     Nine battleground maps have three saved camps each; Murdaigean has none
     (bug 122), and no autonomous battleground occupants were sampled.
     Reduced-Purge allocations grew from two to ten; new gameplay-clock
     deadlines support attempted activations and 20-minute personal reuse,
     without confirming effects. No actual runtime exceptions were captured.
     At 3,900 bots, selected/effective 10x finally achieved about 1.7x;
     evolving load prevents a controlled performance comparison. A brief client
     connection correctly reduced effective speed to 1x.
     Resume only at the owner's request, preserving this evidence and recording
     the pause interval. Successful ram damage/capture, battleground gameplay,
     RA effects and the remaining progression/travel checks are still pending.
     Tasks 95/100-106 and bugs 120/121 retain their required gameplay checks.


94. **Upstream adaptation Stage 4: demonstrated equipment appearance fixes.**
    Selected 2026-10-09 under task 90. Initial catalog audit is recorded in
    [UPSTREAM_CONTENT_EVIDENCE.md](UPSTREAM_CONTENT_EVIDENCE.md): the upstream
    name/realm classifier includes many weapons and shields, so it does not
    establish race-fit failures. No appearance filter or owned-equipment
    change was enabled. A demonstrated client/model-to-race fit list and
    usable alternatives are still required before future-selection changes.
    Bug 5 remains the existing helmet report and needs its client check.
    The owner cannot provide the screenshot/race/item evidence at present
    (2026-10-09); no blanket realm filter or speculative repair is authorized
    by that absence.

90. **Adapt selected upstream improvements to Camlann PvP (in progress).** Owner,
    2026-10-09: plan adaptations from the main `shadowofze/OfflineDAoC`
    repository; other forks are out of scope. The staged plan is
    [UPSTREAM_ADAPTATION_PLAN.md](UPSTREAM_ADAPTATION_PLAN.md): console input,
    navigation corners, bot defenses, equipment appearance, autonomous active
    realm abilities, outdoor travel threats, and a classic quest pilot.
    Owner authorized work through all seven roadmap stages on 2026-10-09,
    using Luna MAX implementation agents and SOL 6.1 High final review.
    Retain current Camlann rules and saves, each stage's evidence/dependency
    gates, and separate source completion from gameplay acceptance. The owner
    authorized focused isolated tests and disposable migration verification
    on 2026-10-09, then authorized shipping 0.214.1 to the fork and local
    installation. Leave the game stopped; quest save migration and enabling
    travel threats are not included. Related existing work: bugs 5 and 115;
    tasks 47, 48, 74 and 88.
    Owner-started live observation on 2026-10-09 confirmed the running server
    and launcher binaries match the shipped 0.214.1 outputs. Observation is
    recorded under finished task 99. Keep-raiding acceptance failed (bug 75);
    new runtime failures are bugs 116 and 117. This does not accept all seven
    stages or replace their remaining controlled/client checks.

Agent sessions on items 45–48: take the role and context from
[ORCHESTRATOR_BRIEF.md](ORCHESTRATOR_BRIEF.md) first.

74. **Reach the selected world speed at 10×/20× (open).** Owner, 2026-10-02:
    live 10× achieved about 6.7× (tick P95 11 ms vs 3.3 ms budget, about 6,000
    bots). Profile: the parallel brain stage is not busy, it is held at its
    barrier by a few heavy single turns: stable-route planning (~3.6 ms,
    ~55 a second; ~20% of wall time), `SelectCamp` (~20 ms, ~5 a second; ~9%),
    zone itinerary and `IssuePath` (~3% each). 0.167.0 moves the stable-route
    plan to a background pool above 1×; re-measure
    `BOT_THINK_PROFILE` / `SERVER_WORK` at 10×. Next candidates: make
    `SelectCamp` cheaper (per-bot LINQ over the whole camp catalog) or slice it,
    then itinerary/`IssuePath`; fewer active bots also helps directly.
    Read-only installed 0.214.1 observation, 2026-10-09: selected/effective
    speed stayed 10x with no connected clients and 3,900 active roster entries.
    A ten-minute sampled window achieved 1.88-2.77x (median 2.36x); the wider session also
    dipped to about 1.6x. Recent bot profiles were dominated by `FrontierThreat`,
    with substantial `NavPathQuery` work and occasional long itinerary turns.
    Phase timings are accumulated across threads and nested, not additive
    wall time. The optional outdoor PvE route-threat property remained False.
    This is an uncontrolled observation, not a regression or improvement claim;
    the historical stable-route profile is not this run's leading result.
    Source 0.215.0 gates expensive frontier work behind its existing cadence,
    reuses a party snapshot, and bounds full eligibility/LOS checks. Profiling
    retains its original outer scope for comparison. Release compilation passed;
    achieved-speed improvement remains unmeasured until installation.
    Supplied 0.215.0 observation: at selected 10x and 3,900 bots, achieved
    speed was 1.81-3.99x (median 2.67x versus baseline 2.36x), with median
    reported tick P95 still 28.1 ms. FrontierThreat median accumulated thread
    time fell from 283.6 to 33.7 s/window; NavPathQuery remained substantial
    (52.4 s/window versus 37.8). These are nested thread totals and different
    live workloads, not additive wall time or a controlled improvement.
    Revisited after the 0.217.0 correctness work: no further navigation/performance
    tuning or live run was performed. Task stays open; reprofile after the
    supply/cohesion/defense paths are accepted.

45. **Battlegroup load check.** Measure server tick and pathing cost with two
    owners and 5 companion groups each in RvR before calling tasks 42–44 done.
    Measuring tools in source (0.165.0), run pending: the server already logs
    `SERVER_WORK` (tick p95, over-budget ticks) and `BOT_THINK_PROFILE`
    (`NavPathQuery`, pathing) every minute; it now also logs `BATTLEGROUP_LOAD`
    (owners, companion groups, companions in RvR) while any player is in a
    battlegroup. `tools/dev/battlegroup-load.py` joins the three and compares
    those minutes with the baseline. Baseline from the live 0.16x log (79
    minutes, no companion battlegroup): tick p95 averages 13.2 ms, peaks at
    28.8 ms of the 33.3 ms budget; pathing about 4,700 ms/min. To finish:
    install, run the scene (two owners, 5 companion groups each, in RvR) for
    15–30 minutes, then run the script and record the verdict. Not run by an
    agent: starting the server needs the owner's go-ahead.

47. **Advisor, then build: world bots barely level.** Aaron, 2026-09-28:
    levelling "barely works". First a read-only advisor pass (autonomous world
    bot population; also note whether TASKS item 7 is the same cause):
    quantify level distribution, XP over time, how many bots actually fight
    vs travel/rest/idle/stuck/dead (live save via `playable-dev/dbquery.py`,
    `runtime/logs/server-console.log`), rank proven vs suspected root causes
    with evidence. Then an implementation agent fixes the proven causes.
    Advisor report: `docs/night/ADVISOR_47.md` (root causes A–E, packages A–C).
    - **Package A implemented in source; real-client/live-log check pending.**
      Solo con ceiling recovers one step after 10 clean kills, on level-up and
      on a new task (a real PvE death still lowers it at once). After a
      recovered step a solo bot moves to a camp above its old ceiling if one
      is within local reach, and otherwise keeps its camp. Solo camp choice
      weights blue/yellow camps twice as high as green ones. A death counts
      as PvP, leaving the ceiling alone, when a non-allied player, companion or
      bot damaged the bot within 30 s even if a mob finished it. Solo bots up to
      level 35 pick camps within 10 (then 20) minutes' travel, weighted by
      distance, region and crowding. One `AUTONOMOUS_BOT_DEATH` log line per
      observed death (killer type/level/class, pet/area/target flags,
      classification, resulting ceiling) and `AUTONOMOUS_CON_RECOVERY` per
      recovered step.
    - **Package C implemented in source (group time at camp); live-log check
      pending.** Two advisor premises did not hold in the 0.115.0 log. D1, the
      task clock running during matchmaking: the shared clock already started
      at the camp; task starts spread evenly over 45–120 min, and the
      54-minute party was a short roll. D3, reassignment in flight: the
      allocation pass never touched grouped bots. The "Reassigned before
      arrival" endings were normal party ends, and only the leader ever
      logged an arrival. Changes: a party within 2,500 units of its camp that
      fought or gained experience in the last 10 minutes starts its full task
      when the 30-minute travel window closes instead of disbanding; a party
      merely looping near the camp still ends. Pickup parties prefer outdoor
      camps within about 10 minutes of the meeting point. Members' camp
      arrival and party ends are logged truthfully
      (`AUTONOMOUS_GROUP_TRAVEL_DEADLINE_AT_CAMP`, "Group task ended: ...").
      Members of a party still meeting up or travelling carry a real future
      expiry instead of an empty one (advisor 48 cause e), so an RvR tour no
      longer ends at once after a restart or raid transfer. Side effect after
      a restart: groupless GroupPve members re-queue for up to 20 minutes
      instead of being marked PvE completed.
    - **Package B (collateral PvP in BotBrain) dropped by Aaron on 2026-09-28**
      (decision C, option a): the 11-hour live run of 0.125.0 showed 25,623
      targeted versus 2,180 collateral PvP deaths, so the advisor's collateral
      hypothesis did not hold once the border hubs were safe. Re-measure after
      24 hours before reconsidering.

48. **Advisor, then build: real RvR with roaming groups.** Aaron, 2026-09-28:
    he still sees no bot groups in RvR. First a read-only advisor pass: how
    many online bots hold the RvR objective per level bracket and realm, how
    many are actually in the frontiers vs travelling/staging/in town, RvR
    group sizes, task starts/ends/failures and why; ranked root causes with
    evidence. Then build toward "real RvR/PvP" as a 2003 player knew it:
    8-man groups with tank/healer/CC roaming, meeting enemy groups, fights at
    keeps, milegates and bridges, zergs around keep takes, solo/duo
    stealthers, resting and regrouping, realm-balanced presence; human-like,
    not perfect.
    Advisor report: `docs/night/ADVISOR_48.md` (causes a–f, build plan 1–6).
    Points 1–5 implemented in source: Castle Sauvage, Svasud Faste and Druim
    Ligen are safe hubs within 3,500 units for humans and bots (no attacks
    into or out of them); the keep-route planner and mover use the runtime
    door rule, so portal keeps are passable for every realm and guild keeps
    for their own guild; when any member of a warband fails the route to a
    keep three times, the whole warband leaves that siege, will not rejoin or
    reopen that keep for 20 minutes, and roams from where it stands; an
    automatic siege closes after 15 minutes without attacker progress
    (getting closer, porting over, reaching the walls or fighting at the
    keep); warbands wait up to a minute at the
    porter for nearby members and then port together; frontier PvP deaths of
    RvR world bots release at their own hub, and immune bots do not open
    fights. Real-client/live-log check pending. Decision B (teleporter
    landing outside the hub radius) taken by the owner on 2026-09-28: option
    b, a second safe circle. Implemented in source, real-client check
    pending: the outer bindstones and code-fallback landing of Castle Sauvage
    (radius 1,500) and Svasud Faste (radius 1,800) are safe for humans and
    bots. In the shipped save the Teleport rows already land players inside
    the hubs; Druim Ligen's landing and bindstone are inside its hub too
    (docs/CAMLANN.md decision 7). Point 6 (1.65 doctrine
    tuning) stays open. Cause e (empty RvR expiry) traced to the group task
    clock publishing an empty expiry while paused; not changed yet.
    Point 6, siege slice 1 (`docs/night/ADVISOR_SIEGE.md`), implemented in
    source; real-client and live-log check pending: world-bot siege work is
    wired again for warbands committed to a keep assault. Operators (tanks,
    melee fighters, Scouts and Rangers) buy a ram at the border hub or at
    home before the march, place it at the outer gate and operate it within
    6,000 units of the keep; casters
    of the same group ride it; melee classes hit the outermost standing gate
    (within 1,100 units of their approach) once no guard is in reach, then the
    inner gate; healers stay free; the lord is only attacked after every gate
    is down. Automatic assaults open only on claimable keeps (no relic keeps,
    base level 50 only). March credit counts only approach inside the keep's
    region (entering it once per member); a started automatic siege also
    closes after 45 minutes without any attacker within 3,000 units, beside
    the 15-minute no-progress rule. Only a whole one-guild warband of eight
    opens a siege; smaller forces join only their own guild's siege, and only
    whole warbands contest a stranger's siege. Garrison strength, one siege
    per server and the temporary ×10 ram (task 55) were left to Aaron's
    decisions (taken on 2026-09-28, see the siege balance below).
    Live re-measurement proposals 2 and 3 implemented in source
    (docs/NIGHT_REPORT.md, 15:46): RvR bots stay out of the shared frontier
    dungeons (Hall of the Corrupt, Summoner's Hall, Marfach Caverns, Dodens
    Gruva) as destinations, hunting grounds and roads; a released warband
    member rejoins a leader in another frontier through the porter instead
    of walking that dungeon tunnel (Albion and Midgard port home first where
    their foreign portal keep sells only the home medallion; relic carriers,
    and forces whose porter fails, keep the tunnel as a last resort) (the
    main way RvR bots met the
    Archwizard, Black Lady and reanimated guardians; Hunters and dungeon
    enemy targets were the rest); roaming and keep routes bend once,
    navmesh-checked, at most 4,000 units out of the way and dropped after
    40 s or a failed order, around
    aggressive named monsters of level 55+, red or purple monsters and dense
    camps (4+ aggressive monsters within 700 units); patrol spots skip such
    camps; a warband or solo attacked by a red or purple monster (or a named
    55+ above its level) breaks off and walks 2,800 units away instead of
    fighting, keep guards and lords excepted; warbands regroup at the hub
    before porting again (bug 66). New log lines `RVR_MOB_BYPASS` and
    `RVR_MOB_DISENGAGE`. Real-client/live-log check pending.
    Siege balance, owner decisions of 2026-09-28 evening (advisor causes d, f,
    g; slice 3), implemented in source; real-client and live-log check
    pending (details in docs/FRONTIER_CAMPAIGN.md, "Siege balance"):
    1a) keeps the Frontier Wardens hold are set to keep level 1 at every
    server start, like a 1.65 unclaimed keep: outer door 10,000 HP (was
    50,000), guards 52 (was 59), lord 63 (was 70) with the unchanged 1.6
    multiplier, so no Warden-only multiplier was needed; guild-claimed and
    relic keeps are untouched; `starting_keep_level` is 1 and a claim still
    raises a keep to level 5. 2b) at most one automatic siege per attacking
    guild instead of one per server, with a server-wide safety cap of six;
    `guilds_claim_limit` is 3 (was 1 in the save). Both property rows move on
    the next server start by a startup database update, only while they
    still hold the old shipped value. 3) The ×10 ram (task 55) is unchanged.
    4a) Relic raids stay off; later mode recorded as item 56. 5) As since
    1.46, single-target direct-damage spells and bolts hit keep doors at half
    effect after the door's level toughness; DoTs, debuffs, crowd control and
    area spells still do not; a caster with no free seat on its group's ram
    nukes the gate. Live check: `FRONTIER_WARDEN_KEEP_LEVEL` and
    `FRONTIER_BALANCE_PROPERTY` lines at start, `Keep.Level` 1 for the Warden
    keeps, two or more automatic sieges of different guilds at once, a first
    `LordDefeated` and guild claim.

56. **Relic-raid mode for world bots (idea, not authorized).** Aaron,
    2026-09-28 (decision 4a on item 48): relic raids stay off for now. A later
    mode would let a guild that holds a keep raid a relic keep and escort the
    relic home as in 1.65. Do not build it before Aaron asks; the relic
    carrier and escort code in AutonomousRvrEventLayer stays as it is.

## Implemented in source; installation verification pending

108. **AssistTrain companion mode and engagement review.** Owner, 2026-10-10.
     Implemented in source **0.223.0**: `/assisttrain` and manager group order,
     human damage-cast/weapon-attack calls, single-target damage and pet gates,
     spell/style ranking, independent healing/add control, and explicit
     petpull/stay reset. Research and existing-mode findings are recorded in
     [COMPANION_BOTS.md](COMPANION_BOTS.md#assist-train-assisttrain). Ordinary
     Aggressive/Defensive policies were retained in 0.223.0. The owner-authorized
     follow-up fixes their harmful-cast assist triggers and polling in 0.224.0
     (bug 123), retaining other behavior. Server Release build verified; automated tests skipped under
     project policy. Deployed with the combined 0.224.1 Release server and
     Windows launcher on 2026-10-10; the installation remains stopped.
     Caster/tank/CC/pet/target-switch real-client checks remain pending.

Installation checkpoint, 2026-10-10: all pending source changes, including
AssistTrain and fixes 123/124, merged/pushed and deployed as **0.224.1** to
`D:\Games\OfflineDAoC`. Server and Windows launcher Release builds passed;
17 files replaced with verified backups and protected accounts/save/settings
unchanged. No components were running; the installation remains stopped.
Automated tests and post-deploy monitoring skipped; real-client checks pending.
Paused observation task 107 was not resumed.

Earlier installation checkpoint, 2026-10-10: all local source changes and latest fork
main were merged/pushed, built together in Release with zero errors, and deployed
as **0.222.0** to `D:\Games\OfflineDAoC`. Server, launcher and ten campaign meshes
installed (27 files); verified backup: `D:\Games\OfflineDAoC-backups\deploy-20261010-001850`.
Accounts, save and settings passed protection checks. No game components were
running; the installation remains stopped. Automated tests and monitoring were
skipped. Tasks 100–106 remain here for their outstanding real-client checks.

106. **Build an Eden-inspired battleground campaign.** Owner, 2026-10-09,
     including the reply to retain guild/group alliances. **Done in source
     0.222.0, 2026-10-10; deployed; client verification pending.** SOL Medium
     subagents implemented ten bracket definitions, native admission/release/
     graduation, field contracts and personal tokens, funded physical assaults,
     captain sabotage, bounded 1–8 actor patrols/ambushes and opt-in grouping.
     Existing saves and Camlann alliances remain. All ten generated native
     meshes load, with three safe arrival floors and three outside-camp route
     proofs per map. Cathal central gates are restored from verified fixtures;
     incomplete native keep/lord/monster content gates impossible objectives.
     Staging/deploy support preserves the existing backup/protected-save flow.
     Server and Windows launcher Release builds passed with zero errors; existing
     warnings remain. Deployment scripts pass syntax checks.
     Pending: owner checks of travel/graduation and sub-10 safety,
     alliances, quest credit/persistence, funding/refunds/restarts and captain
     respawn, actual door/lord/steward capture, difficulty and optional grouping.
     Deployed with the ten campaign meshes on 2026-10-10; no server/client
     startup was performed. Automated tests skipped under project rules.

105. **Coordinate multiple guild parties for a viable keep assault.** Owner,
     2026-10-09, during live 0.217.0 observation: several parties should gather
     and attack together instead of sending eight attackers into several
     defending groups. Source 0.221.0 adds a guild-scoped exterior camp,
     shared physical readiness and release, locally sighted defender pressure,
     guard/door allowance, healing/equipment checks, bounded failure and
     post-defeat reassembly. Owner clarification, 2026-10-10: attacks must vary,
     including single-party sorties. Each new assault chooses a stable one-,
     two- or three-party preference (45%/35%/20%); observed opposition can raise
     the required strength. A single full party can attack a lightly defended
     keep. Each contributing party still needs at least six present members.
     Recruitment prefers the guild's own active assault. Other guilds sharing
     a target remain separate armies. No new bots, forced transport, save
     migration or combat/placement rule changes are included.
     Related bug 75/task 100 fixes allow ticket/porter approach during cohesion,
     guard cleared destinations, retain current march leases, release stale
     siege-job assignments, and reacquire a carried ram after lease expiry.
     Deployed in 0.222.0; gameplay verification is pending: observe both a
     one-party sortie and a shared multi-party launch, verify that strong
     observed opposition prevents an understrength single-party attack, measure
     simultaneous living attackers/defenders, check real ram/door outcomes,
     and exercise missing supplies, unreachable camp, stale reports, casualties,
     leader replacement, guild recall and defeat/reassembly. Defender sightings
     are bounded local observations, not omniscient knowledge of hidden troops;
     the initial strength margin needs live calibration. Automated tests are
     not run; the owner's installation was updated to 0.222.0 on 2026-10-10.
     Validation on 2026-10-10: isolated Release server build of this scope
     against shipped main passed with zero errors (warnings remain). The
     shared checkout build encountered unrelated in-progress battleground/keep
     errors; combined integration Release compilation passed on 2026-10-10.
     Version pins, conflict markers and preserved line endings were checked.
     Initial development performed no deployment or runtime changes; the
     later shipment is recorded above. Tests and game startup were skipped.

104. **Tune the existing Atlas Old Frontiers realm abilities.** Owner,
     2026-10-09: retain the current classic system and hybrid Determination;
     reduce personal Purge from 30 to 20 minutes. Implemented in source
     0.220.0 for the standard and reduced-cost tank handlers, shared by
     players, companions and autonomous world bots. Trainer cooldown
     details use the runtime timer; existing saved autonomous deadlines
     are honored. Costs, ranks, five-rank tank/three-rank hybrid
     Determination and the separate 30-minute Druid Group Purge remain.
     Nine-rank passive scaling is deferred. Server and Windows launcher
     Release builds passed with zero errors (warnings remain); automated
     tests skipped under project rules. Deployed in 0.222.0; pending owner
     checks of personal Purge cooldown/details for players and
     bots, Group Purge, saved cooldowns and retained ability ranks.

103. **Reaver damage aura helps hold PvE aggro at every rank.** Owner,
     2026-10-09: add extra threat against NPCs/mobs, excluding playerbots,
     to protect healers and bombers while retaining manual taunt styles;
     describe the behavior in-game. Implemented in source 0.219.0: all nine
     learned Soulrending damage-aura ranks add four times their spell level
     per damaging pulse (8/20/40/56/84/104/132/160/200 extra threat at spell
     levels 2/5/10/14/21/26/33/40/50). Normal damage threat and Protect still
     apply; the bonus never forces a target switch or catches up to another
     character's threat. Players, companion/autonomous playerbots and
     controlled pets are excluded; resisted/zero-damage pulses add no bonus.
     Existing damage, radius, six-second pulse, power and recast are preserved.
     Tooltip and detailed descriptions explain rank scaling and that taunt
     styles may still be needed. Server and Windows launcher Release builds
     passed; scoped diff, conflict and version checks passed. Automated tests
     skipped under project rules. Deployed in 0.222.0; pending owner checks of
     all ranks, descriptions,
     healer/bomber threat balance, manual taunts and playerbot exclusions.

102. **Automatic keep progression and existing-claim reset.** Owner, 2026-10-09.
     Implemented in source 0.218.0: human and autonomous-bot guild keeps start
     at level 1 and progress to 10 in 32 gameplay hours (level 5 at one hour).
     Persisted deadlines retain progress across restarts and count downtime
     at 1x. Existing guild holdings reset once on their first updated load;
     NPC/Warden, unowned, portal and relic keeps are excluded. Claims, relic
     placement and player/bot possessions remain intact. Legacy default
     settings migrate; later operator overrides remain possible. Automated
     tests skipped under the project workflow; server Release build passed.
     Deployed in 0.222.0; pending owner checks of the reset, timed upgrades,
     doors/guards and restart.

101. **Autonomous groups run properly; leaders loop instead of stopping.**
     Owner, 2026-10-09. No speed bug (bots have MaxSpeed 191), but heavy
     stop-and-go: siege-column, RvR roam and group travel holds stop the
     leader, and followers matched the leader's current speed, so a slow or
     stopped leader made the whole group crawl (siege-column member median
     about 75 units/s). Source 0.219.0: followers of a moving, unstealthed
     autonomous leader match its MaxSpeed (catch-up bonus and personal
     stride kept; a follower ahead of the leader is capped at its pace, +10 %
     toward a front slot). A holding leader runs a 4-6 point loop, 300-450
     units around the hold point, at full run speed; points are navmesh and
     path checked, built once per hold around a fixed centre (cleared when
     the hold ends, 750 ms think cadence while looping); without a valid loop
     it holds as before. Siege regroup progress is measured from the centre;
     circling is not movement for the stuck watchdog. All hold
     timers, gap thresholds and give-up rules are unchanged. No loop in
     combat, within 1,800 of a keep, while operating siege equipment, during
     expedition attendance, in stealth or dungeons, near the next zone
     crossing or within 2,500 of the PvE camp. Stealthed, companion and
     player-led groups are unchanged. A five-minute `MOVE_PACE` log line gives
     commanded-speed p10/p50/p90 by role and flag plus looping leaders.
     Deployed in 0.222.0 on 2026-10-10; check in game and in `MOVE_PACE`:
     follower p50 near 190 while marching,
     leaders circling instead of standing.

100. **Repair observed keep-raid coordination and bot turn failures.** Owner,
     2026-10-09, after task 99: prioritize reliable rally and march behavior;
     genuine losses to defenders remain legitimate. Source 0.215.0 fixes
     shared porter selection, intermediate passages, leader-first transfers,
     rally quorum, siege supply/march sequencing and bounded keep-origin
     planning. It also repairs bugs 116/117 and bounds frontier scan work.
     SOL High implementation agents were independently reviewed by the parent.
     Installation and a full rally-to-gate siege check remain pending. Keep
     combat outcomes separate from transport, rally, supply and placement
     failures; task 74 still requires measured achieved-speed improvement.
     No active-RA purchase defect was established: observed unspent points
     remain below the first active's cost, and its purchase reserves future
     earnings before higher passive upgrades. Preserve all purchased ranks.
     Development left live saves, configuration and server processes unchanged.
     Validation: server and Windows launcher Release builds passed, as did
     compilation of the server regression project, including added cases.
     Diff, conflict-marker, newline and four-version-pin checks passed. Automated
     tests were not executed under the project workflow. Owner authorized
     shipping 0.215.0 on 2026-10-09, with the installation left stopped for a
     subsequent monitoring chat. Runtime acceptance remains pending; use the
     shipping result/deployment manifest to confirm installation separately.
     Source 0.217.0 follows the owner's supplied observation and new partial
     retaliation report (bug 119). Supply routing now has a fixed merchant,
     explicit return, isolated legal porter legs and a return-time reserve
     within the existing rally deadline. Siege cohesion uses bounded regrouping
     without abandoning distant living operators; followers cannot reassign a
     force by giving up while its keep battle continues. Recall yields to actual
     incoming combat. New throttled logs diagnose ram execution without changing
     placement/LOS/door rules. Detailed force traces and remaining acceptance are
     in bug 75. Installation, intermediate Home hops, ram/elevation diagnosis and
     real-client end-to-end acceptance remain pending. Tests are not executed;
     no deployment, server process action, settings change or save edit is part
     of this development request. Validation: server and Windows launcher
     Release builds and server regression-project compilation passed with zero
     errors (warnings remain). Whitespace checks accounting for preserved CRLF,
     conflict-marker checks and all four 0.217.0 version pins passed. The
     pre-existing untracked `source/server/Tests/tmp/` was preserved. Owner
     authorized shipping on 2026-10-09. After integrating the fork's launcher
     GM fix (0.216.0), this task is versioned 0.217.0; deployment will leave
     the installation stopped. Runtime acceptance remains pending; use the
     shipping result/deployment manifest to confirm installation separately.


97. **Upstream adaptation Stage 7: Miari's Seed quest pilot.** Source
    0.214.0, 2026-10-09: verified clean v0.35b release inputs match the pinned
    code apart from its README-only successor. Use existing DataQuest steps,
    preserve Guardian/level eligibility and configured rewards, and import
    only quest 20054 plus its non-tradable pouch template. Required actors
    already exist; their exact template/spawn fields and zone-181 bounds are
    checked. Existing mesh presence is not proof of runtime reachability.
    The development utility defaults to dry-run and accepts only marked
    disposable targets, with collision checks, prepared manifests, immutable
    verified backups and transactional owned-row rollback. Ambiguous recovery
    disables automatic rollback. Seven isolated migration checks passed in
    0.214.1: dry-run/apply/rerun, partial ownership, interruption recovery,
    rollback and refusal guards; unrelated synthetic progress was preserved.
    No installed or played database was modified. Source
    evidence and original journal guide are in
    [UPSTREAM_CONTENT_EVIDENCE.md](UPSTREAM_CONTENT_EVIDENCE.md) and
    [CLASSIC_QUEST_PILOT.md](CLASSIC_QUEST_PILOT.md). Syntax/resource checks and
    SOL 6.1 High source/test review passed. Full quest/restart/client checks and bot
    non-interference remain pending. Later batches require pilot acceptance.

96. **Upstream adaptation Stage 6: outdoor PvE travel threats.** Source
    0.213.0, 2026-10-09: default-off independent switch; solo/leader-only
    bounded scans, designated-puller handoff and readiness, retained holds,
    two verified detour legs, bounded retry history and camp rejection.
    Existing meshes, frontier/dungeon travel, protected actors, safe hubs,
    companion orders and pet pulls are unchanged. SOL 6.1 High reviewed
    hold/cadence/recovery ordering and rejoin execution. Historical log
    aggregates and paired owner checks are in
    [UPSTREAM_TRAVEL_BASELINE.md](UPSTREAM_TRAVEL_BASELINE.md). Three isolated
    policy tests passed in 0.214.1, not an enabled-route/performance run.
    Server/launcher builds passed. Owner
    enabled-route, camp arrival, death/retry, 1x/accelerated-speed and cost
    observations remain pending; leave the switch off until accepted.
    Installed 0.214.1 observation on 2026-10-09 confirmed the persisted
    property is False and no `OutdoorRouteThreat` profiling phase was seen.
    Its disabled state does not verify enabled behavior or overhead.

95. **Upstream adaptation Stage 5: autonomous active realm abilities.** Source
    0.212.0, 2026-10-09: retain the 39 passive class paths and owned ranks;
    deterministic phases purchase class-legal Purge, First Aid, Ignore Pain
    and Second Wind with charged prerequisites and earned points only.
    Runtime handler types select variants while real catalog keys are saved.
    Optional cooldown tokens use gameplay UTC, save before execution and
    fail closed when malformed. Status snapshots and writes share the same
    database gate, preventing older queued state from overwriting cooldowns.
    Scoped Purge/First Aid support leaves player and companion training alone.
    Ten isolated policy, prerequisite and token tests passed in 0.214.1.
    Serialization round-trip does not verify database cooldown persistence.
    Release builds and SOL 6.1 High source/test review passed. Owner
    purchase, effect, PvP balance, reload and world-speed checks remain pending.
    Installed 0.214.1 observation on 2026-10-09 found saved passive ranks but
    no purchased Purge, First Aid, Ignore Pain or Second Wind and no cooldown
    tokens in the read-only roster samples. Existing purchases are retained
    and the initial active sequence needs spare earned points. Ability effects
    and database cooldown reload therefore remain unobserved, not accepted.
    Follow-up budget audit for task 100 found at most six spare points on
    ordinary paths and three on the reduced-Purge tank paths, below the
    ten/four purchase costs. Failed initial active purchases stop later
    passive upgrades, so future earned points are already reserved. No
    training bug or need to replace existing purchases was established.

93. **Upstream adaptation Stage 3: player-style bot defenses.** Source
    0.211.0, 2026-10-09: GameBots share player block/parry/evade arithmetic,
    ability/buff bonuses, facing and shield/weapon/spec gates, shield block
    rounds and PvP caps. Humans retain their existing formulas; monsters and
    pets retain NPC rules. Same-realm hostile bots use the same caps as
    cross-realm enemies. The existing shield quality/condition behavior,
    including its ranged baseline, was deliberately preserved. Formula and
    source paths reviewed; 25 isolated defense-resolution tests passed in
    0.214.1, not equivalent-stat live combat verification. Release builds and
    SOL 6.1 High source/test review passed. Installation,
    equivalent-stat class/equipment/facing PvE/PvP combat and
    balance observations remain pending.
    Installation was confirmed against the shipped 0.214.1 binary on
    2026-10-09. Ordinary logs do not expose equivalent-stat defense outcomes,
    so this live observation does not complete the combat/balance check.

91. **Upstream adaptation Stage 1: console EOF backoff.** Source 0.209.2,
    2026-10-09: focused port of upstream `c1c465c3`; closed console input
    sleeps one second before retrying without changing game-loop timing or
    normal commands. Release build and SOL 6.1 High source review passed.
    Installation,
    runtime CPU measurement after redirected input closes, and shutdown
    verification remain pending. Existing defect tracked as bug 115.
    Installation was confirmed in the owner-started 0.214.1 session on
    2026-10-09; closed-input and shutdown checks were not exercised.

92. **Upstream adaptation Stage 2: navigation route points and corners.**
    Source 0.210.0, 2026-10-09: merge coincident intermediate points with
    combined flags while retaining the endpoint and path status. Corner sight
    retry is restricted to a near-origin hit, a short verified surface step,
    reverse segment proof and door exclusion; upstream's unconditional retry
    was narrowed. Existing navigation meshes and DF preparation are preserved.
    Two isolated route-point tests passed in 0.214.1, not native mesh checks.
    Release builds and SOL 6.1 High source/test review passed. Real-client corner, wall,
    closed-door, disconnected-surface and ordinary-route checks remain pending.
    Installation was confirmed on 2026-10-09. The live run still contained
    disconnected exterior keep routes; aggregate travel activity is not a
    controlled check of this corner change or evidence to replace meshes.

88. **Autonomous world bots train one realm ability build per class.** Owner,
    2026-10-04. Implemented in source 0.209.0: all 39 generated classes have
    one fixed passive RA priority path. Earned RP now updates autonomous
    Realm Level; class-legal ranks spend only the available player-equivalent
    point pool at runtime cost, persist in the existing ability field, and
    restore across login. The plan banks points for its next rank. That initial
    release excluded timed actives and inert GameBot passives; Stage 3
    (0.211.0) integrates defensive bonuses, and Stage 5/task 95 (0.212.0)
    adds the reviewed initial actives without removing existing purchases.
    The class table,
    rationale, and validation boundaries are in
    [AUTONOMOUS_RA_BUILDS.md](AUTONOMOUS_RA_BUILDS.md). Server and launcher
    0.209.0 deployed locally on 2026-10-04 (17 files; backup
    `D:\Games\OfflineDAoC-backups\deploy-20261004-211347`). Real-client checks
    remain pending: class legality, effect use, point balance, and restart
    persistence. Automated tests were skipped per project rule.

87. **Research-led player experience and quality of life.** Owner, 2026-10-04:
    review the current game and other freeshards, then improve feel and flow
    autonomously. Implemented in source 0.208.0: an on-demand `/offline` guide
    with one login hint, companion status and topic help, `/train list` with
    unambiguous specialization selection, and monster listings with zones and
    a `/mobs nearby` current-zone filter. These address observed friction:
    the inherited command dump and starter help omit a practical fork-specific
    introduction; the companion roster does not explain live readiness or
    effective group orders; training requires knowing specialization names;
    monster listings previously contain names and levels without locations.
    Research, accessed 2026-10-04:
    - [Eden's hub](https://eden-daoc.net/hub) and
      [slash-command guide](https://eden-daoc.net/wiki?content=slash-commands)
      emphasize searchable world information and basic `/info` help.
    - [Phoenix's historical command reference](https://playphoenix.online/forum/server/wiki-pages/slash-commands-general-01F41S3TM8N4HN50060MBX2HSD)
      documents training and routine inventory/group conveniences; this is
      historical design evidence, not a claim about a currently running shard.
    - [Uthgard's FAQ](https://uthgard.net/howto) combines a 1.65 target with
      selective convenience such as potion shortcut handling. Its undated
      legacy content is a design reference, not verified live behavior.
    Assessment: this fork already provides extensive companion automation,
    travel and progression tools. This pass makes those tools understandable
    and reduces command trial and error. Saved party presets and protected
    inventory batching are larger possible follow-ups, not implemented here.
    The installed status/log snapshot was from an earlier 2026-10-04 session
    (09:18 UTC), with tick P95 around 35 ms at 1x; it is not a fresh gameplay
    observation and does not resolve performance task 74. No personal save
    contents were changed. Server and Windows launcher Release builds passed
    with existing warnings and no errors. Scoped review, version pins and
    newline checks passed. Automated tests were skipped under the project rules.
    Shipped on 2026-10-04 after fresh server and Windows launcher Release
    builds and push to fork main. Deployment replaced and hash-verified 17
    files, preserved accounts/database/settings, and left the game stopped.
    Backup: `D:\Games\OfflineDAoC-backups\deploy-20261004-183151`.
    Pending: check guide readability, one hint per login/no hint
    on zoning, companion dead/distant/squad/override states, trainer lists and
    ambiguous input, and both global/current-zone monster pagination in client.

86. **Bug sweep with Luna max subagents.** Owner, 2026-10-04: inspect the
    project and fix confirmed bugs using Luna at max reasoning. Source
    0.206.0 fixes bugs 92–102 and 104–105 across networking, weekly quests,
    game-loop sizing, autonomous RvR groups, companion rewards, launcher
    process/file handling, and setup/reset safeguards. Three Luna max
    subagents reviewed autonomous AI, combat/companions, and launcher/setup;
    the companion guide correction (bug 103) is Done. Server, Windows
    launcher, and Setup Release builds passed with warnings and no errors.
    Automated tests were skipped under the project rules. No server start,
    world reset, setup or migration was performed. Owner-authorized shipping
    on 2026-10-04 pushed all tracked changes to fork main and deployed server
    and launcher 0.206.0 (9 files), preserving accounts, database and settings.
    The game remains stopped. Setup changes are in source; the standard
    deployment does not install that tool. The relevant runtime/client and
    disposable-input checks listed in BUGS.md remain pending; this sweep
    does not establish that the project is bug-free.

85. **RvR groups muster at their realm's hub and march together.** Owner
    2026-10-03: many bots walk alone; groups should meet where one can port
    to and walk on together; depart at 75 % only if the rest is not
    expected within 5 minutes. Live 0.199.0 (19:09-20:23): 37 % of RvR
    rendezvous were enemy portal keeps in the open frontier, 89 of 132
    no-shows died on the way, 44 were released at their own hub in another
    region without a way back, and no-shows were expelled into solo play.
    Source 0.202.0 (`AutonomousRvrHubMuster`): an RvR group always meets at
    the leader realm's border hub (Castle Sauvage, Svasud Faste, Druim
    Ligen), members travel there by city porter, also after a death, and a
    grouped RvR bot releases at its group's hub. Departure: all present, or
    >= 75 % when no missing member is expected within 5 minutes (else wait at
    most 5 more), after 10 minutes half the group; under 2 present and nobody
    expected disbands back into hub LFG (RvR kept, no PvE turn); hard cap 20
    minutes. Nobody is expelled; latecomers follow. RvR meetup window 20
    minutes (PvE 45 unchanged). Logs: `RVR_HUB_MUSTER_STARTED`,
    `RVR_HUB_MUSTER_DEPARTED ... reason=`, `RVR_HUB_MUSTER_DISBANDED`. Check
    live: groups visibly gather at the hubs and port together; watch the
    number of RvR groups and `CoordinatorLockWait`.

84. **The group always runs its fastest speed.** Owner 2026-10-03: casters
    often run their own speed although a Bard is in the group. Speed values:
    performer songs 204, caster/healer/Warden speeds 153-154. Bots already
    skipped their speed when a bot groupmate knew a faster one, but the
    Warden's travel chant bypassed that check and real players never
    counted. Source 0.200.0: the Warden drops its speed chant for bladeturn
    when a groupmate has a faster speed; a real player running a faster
    speed pulse counts too. Check in the client: with a Bard (bot or you)
    in the group, no caster or Warden speed icon replaces the song.

83. **All guild troops defend their attacked keep before other work.** Owner,
    2026-10-03. Source 0.199.0 adds a temporary guild-owned recall on actual
    hostile wall, door, guard or lord damage, including autonomous attackers, pets
    and siege operators. All active independent autonomous guild members
    respond regardless of level/activity; no reserve, battle cap or combat
    exclusion. Same-guild parties stay together; mixed-guild raid members
    withdraw individually. Existing activity sessions/reservations retire;
    durable activity allocation, possessions and native combat/recovery stay.
    First attacked keep holds focus; later attacks queue, hits renew four
    hours, and owner loss/lord defeat ends the recall. Route failure retries
    while the order stays active. Player-led troops and companions are excluded.
    Investigation, behavior and client scenarios:
    [FRONTIER_CAMPAIGN.md](FRONTIER_CAMPAIGN.md#guild-keep-emergency-defense--01990).
    CoreServer and Windows launcher Release builds passed on 2026-10-03 with
    existing warnings and zero errors. Scoped whitespace, conflict-marker,
    line-ending and version-pin checks passed. Deployed as 0.199.0 on
    2026-10-03; real-client verification remains pending. Automated tests
    were skipped under the local workflow. The game was left stopped.

82. **Bot guilds secure free keeps; unlimited guild keep claims.** Owner,
    2026-10-03: Fensalir is neutral after a raid; guild bots should hold as
    many keeps as possible and the three-keep cap must go. Source 0.197.0
    removes the limit for humans and bots and updates saved property rows to
    the unlimited marker (-1). Eligible RvR guild forces prioritize free,
    defeated keeps over roaming/new assaults, reserve one force per journey,
    and use validated keep routes to the native steward; existing sieges
    retain their commitment. Saved defeated lords restore missing stewards
    (bug 85). Deployed with 0.199.0 on 2026-10-03. Pending: real-client
    confirmation that bot guilds claim unattended keeps and a guild can own
    a fourth keep and more.
    CoreServer and Windows launcher Release builds passed on 2026-10-03
    with existing warnings; scoped whitespace, conflict-marker and version-pin
    checks passed. Automated tests were skipped under the local workflow.
    Shipped with 0.199.0; the game was left stopped.

81. **More deliberate world-bot journeys, safe recovery and fewer repeated
    clusters.** Owner, 2026-10-03; planned with Astra and implemented with Sol
    in 0.195.0. Safe release for independent autonomous actors, siege-march
    acquisition/assist discipline with bounded pursuit, leader-first distant
    rallies with progress-sensitive wait limits, stable validated local keep
    departure variation, and per-actor repeated-site PvP loss avoidance.
    Plan and verification scenarios: [RVR_GROUP_DOCTRINE.md](RVR_GROUP_DOCTRINE.md#journeys-and-recovery-01950).
    Extends task 48 and the hub/departure/route work without closing their
    outstanding checks. Pending installation and real-client checks: repeated
    Mag Mell deaths, Svasud/Sauvage/Druim departures under attack, distant
    rallies with unreachable members, and full door/lord/capture/claim raids.
    Automated tests not requested; no server start or deployment authorized.

80. **Performers keep speed while playing another song.** Owner
    2026-10-02: the Bard switches songs and drops speed; best if it can play
    both at once (asked to check Eden / the local LLM). Research: the local
    knowledge base and the official class library say 1.65 allowed only one
    song at a time (twisting); no Eden source confirming a change was found.
    Owner's choice applied. Source 0.192.0: the engine's existing exception
    list `PulseSpellGroupsIgnoringOtherPulseSpells` now holds the speed song
    groups of all three performers (Minstrel 1101, Skald 3608, Bard 5151), so
    speed runs beside any other song for players and bots in every realm;
    performer bots start speed first and then twist only their other songs
    without touching speed. Warden and other chants are unchanged. Check in
    the client: a Bard keeps speed while its endurance/power song plays.

79. **Pet pull: the Mentalist walks along to keep its HoT on the pet.**
    Owner 2026-10-02: while the owner goes out for new mobs, the Mentalist
    should come along so the HoT on the pet stays up, without drawing aggro
    while walking and following; it should only do damage again once the
    group is back near the mushrooms. Source 0.186.0
    (`CompanionPetPullEscort`): with `/petpull` and `/stay`, once the owner
    is more than 400 units from the stayed camp, the Mentalist trails him
    250 units behind on the line back to camp, stepping toward camp until it
    is 150 units outside the aggro range of every idle monster that would
    attack it (monsters already fighting are ignored); with no safe spot it
    waits. Back within 400 units it holds its own camp spot again. Its
    offensive spells wait until it is within 600 units of the camp. Check in
    the client: walk out to pull, the Mentalist follows at a distance and
    keeps the HoT on the pet, draws no monster on the way, and casts damage
    only once back at the grove.

78. **Buffers buff in a sensible order and only where it helps.** Owner
    2026-10-02: buffers first give themselves dex and spec dex so they buff
    everyone faster; casters get strength only when concentration is spare
    or the player is overloaded; tanks without power get no acuity; every
    realm must hand out the same correct buffs (PvP). Source 0.184.0:
    `BotCastSpeedSelfBuff` puts the buffer's own dexterity and
    dexterity/quickness first in both buff paths (dex shortens every cast,
    `GameLiving.CalculateCastingTime`). `BotBuffTargetPolicy` filters
    single-target realm buffs from class data only: no `AcuityBuff` for a
    class without a power stat; a list caster (not Valewalker/Vampiir) gets
    base `StrengthBuff` only when encumbered or the buffer keeps at least
    that buff's concentration free afterwards. Strength/constitution stays
    for everyone (constitution helps casters). Check in the client: a
    Cleric/Healer/Druid first buffs itself with dex, an Armsman/Warrior/Hero
    gets no acuity, a Wizard gets base strength only when overloaded or the
    buffer has concentration to spare.
    0.188.0: acuity follows the server's own formula (StatCalculator adds it
    to list casters' casting stat only), so healers and hybrids no longer get
    a buff that does nothing; Cleric, Healer, Druid, Shaman and Bard count as
    casters for base strength. Full table: docs/BUFF_RULES.md.
    0.189.0 (owner: for Cleric and Druid it is a spec question): such a
    healer gets base strength when its highest weapon spec is at least half
    its level (battle Cleric crush 25+ at 50, melee Druid blades/blunt).
    0.190.0 (owner: give optional buffs while concentration lasts, take
    them back for newcomers, players always max buffs): base strength on a
    caster or healing-spec bot is optional and goes out whenever affordable;
    targets are chosen real players first, then required, then optional;
    when a required buff lacks concentration the buffer ends its largest
    optional concentration buff on a bot (never a player).

77. **Free buffbot on right-click.** Owner 2026-10-01: free buffs by
    right-clicking the NPC, all buffs including endurance, unlimited time.
    Source 0.178.0: `BuffMerchant` gives the full set plus endurance regen
    (value 5) on interact; timed buffs instead of concentration buffs, so
    they last until death or logout. 0.179.0: length 65,000 s (about 18 h);
    the 0.178.0 value overflowed `Duration * 4` and the buffs ended at once. Each realm
    teleporter (`LiveTeleporter`, 50 spawns) creates a runtime-only "Realm
    Enchanter" 120 units to its side; extras via GM
    `/mob create DOL.GS.BuffMerchant`. Check in the client: the Enchanter
    stands on open ground next to each teleporter, all icons appear, stats and endurance regen apply, a caster's own
    concentration is untouched, buffs survive zoning.

76. **RvR groups fight instead of watching (step 1 of 2).** Owner
    2026-10-01: "sehe keine gruppen zusammen arbeiten also zusammen etwas
    töten oder angreifen, fast nur passiv" — keeps fall only if every
    character uses its skills as the situation needs. Step 1, source 0.174.0:
    the observe layer attacks even or smaller parties (`IsEvenOrWeaker`) and
    no longer flees an even party closing in. Step 2 (open): check that
    members assist the leader's target and that healers, crowd control and
    siege roles act in combat. Check: share of `third_party` in
    `RVR_OBSERVE_DECISION`, PvP kills per warband, keep captures.

75. **Companion Manager: organised roster, larger text, bigger list.** Owner,
    2026-10-01: enlarging the window did not show more companions or detail
    lines (still paging with PgUp/PgDn, also in Training & Tactics), the font was
    too small, and an account-bound roster mixed realms, levels, classes and
    states ("Story L16 Skald, active"). Source 0.169.0: `arial14` text; list
    columns (name, level, class, type, state) with green Active and grey Bench;
    **Group** (Smart default, Realm, Role, Level, None) with foldable section
    headers, **Sort** (Level, Name, Class), and **Rows** (16, 22, 28, 34) that
    fill the list and the detail panel; the choice is saved per account. The
    client cannot report its window size, so a larger window needs one Rows
    click. This rebuilds the manager `game.dll` (protocol 3) and the window XML;
    stage `D:\Games\OfflineDAoC-dev\companion-manager-stage-0.169.0`.
    Installation and real-client check pending: with the game, server and
    launcher closed, run `tools/dev/Install-CompanionManager.ps1 -InstallRoot
    D:\Games\OfflineDAoC -Stage <stage>` (dry run, then `-Apply`; it upgrades
    the 0.33.0 manager in place and keeps a backup). Then confirm the window
    opens at 980×700 with readable arial14 text (never drawn by this client
    before), columns and labels do not overlap or clip, Rows 28 fills a
    resized window and Training & Tactics shows more lines, header clicks fold
    sections, clicks on blank rows below the window edge never hit the game
    world, the raid windows still work, and the grouping/sort/rows choice
    survives relog.

73. **Levelling bots go to Darkness Falls.** Aaron, 2026-10-01: "Make bots that
    are leveling much more go to Darkness Falls for leveling! Currently its very
    empty there. Note that the lowest mobs there are around lvl 16." Source
    0.165.0: with a Darkness Falls camp among the legal camps, a solo bot from
    level 20 takes a dungeon on 40% of its camp draws and a group from level 16
    on 60% (before 10% / 30%); solo levelers up to 35 consider Darkness Falls
    up to 30 minutes away. DF's own soft population limit and the existing
    level/con filters still apply; level 50 is unchanged. Live-log check
    pending: more bots and groups with a region-249 camp, DF population grows
    without crowding past its soft capacity. Tune
    `SoloDarknessFallsPreferencePermille` / `GroupDarknessFallsPreferencePermille`
    if it is still empty or too busy.

72. **No autonomous PvE raids; level-50 bots focus on RvR.** Aaron, 2026-10-01:
    "Disable that autonomous bots do PVE raids (dragon, epic dungeon, etc) and
    focus more on RvR when they are lvl 50." Source 0.163.0: bots no longer open
    or join realm raids and the raid calendar is silent
    (`RealmRaidRecruitmentPolicy.AutonomousRaidsEnabled`); owner-started raids
    from the event controls still run. Level-50 bots get an RvR floor per player
    type (35-90%). Live-log check pending: no `RAID_CALENDAR_*` lines and no
    autonomous raid start; the level-50 RvR share rises.

71. **RvR pickup groups across guilds and realms.** Aaron, 2026-09-29: "I
    only see groups standing around; hardly any full groups form." Live
    0.157.1 log: RvR groups formed after a median of 91 minutes on their
    task and mostly with 2–3 members (51 of 67), because RvR recruitment took
    only members of the leader's own guild. Owner decision: pickup groups
    over all realms, as on Camlann where anyone could group with anyone;
    group sizes stay mixed by player type. Source 0.158.0: new groups,
    assembling groups, the recruitment offer and field backfill accept any
    autonomous RvR bot, guildmates first, then the leader's realm, then other
    realms. Warband merges and siege openings stay one-guild. Check:
    `AUTONOMOUS_GROUP_FORMED ... objective=RvR` with a lower
    `formationWaitSeconds` and more groups of 5+; mixed-realm groups travel
    together and never attack each other.
    Follow-up: mixed warbands land together via the leader's realm passage
    (Stefan 0.161.0, bug 75); stragglers and human porter use in 0.162.0
    (bug 76).

70. **Bots play continuously, no timed breaks.** Aaron, 2026-09-29: "no more
    pauses, the time-limited RvR tour then a break or levelling; just keep
    running." Source 0.158.0, autonomous world bots only: an RvR tour no
    longer ends (solo roamers and whole warbands restart their clock and keep
    roaming together); a bot whose RvR group ended goes straight back to RvR
    and no longer owes a PvE task; productive PvE parties renew session after
    session instead of once; optional town breaks (15–30 minutes, up to 35 %
    for casual types) are gone. Training points and a full backpack still send
    a bot to town. Check: `AUTONOMOUS_GROUP_TASK_RENEWED kind=RvR`, no
    `Frontier tenure complete` or town-idle phases, RvR groups staying
    together for hours.

69. **RvR groups roam with a speed class and move as one body.** Aaron,
    2026-09-29 (live 0.152.0: bots at normal speed, some at walking pace,
    groups not roaming together; P6 "a retreat/travel is a speed-class
    decision"; docs/RVR_GROUP_DOCTRINE.md "Speed and travel"). Implemented
    in source; installation and real-client check pending. Autonomous RvR
    world bots only: forming and backfilling groups of 3+ strongly prefer a
    Bard/Skald/Minstrel when none is in, after a missing healer (Healer with augmentation speed as
    fallback; still leaves without one); the performer sings only speed
    while the group travels out of combat (Bard twists endurance), a speed
    Healer stops once on the march to start its speed; the leader waits up
    to about 6 s when a member (within 4,000) falls over 1,200 behind, until
    all are within 600, and gives up on a member after three waits; members sprint (now +30 % for world bots) to close a gap
    over 150; a member ahead of the leader never outruns it; a travelling
    bot picks up a new max speed (song, sprint) at once instead of keeping
    its order's old speed; RvR bots keep full pace below a third of health
    and never use the on-foot habit after a PvE release; groups without
    speed favour spots within 8,000 of a keep/tower/hub. Check in
    `server-console.log`: `RVR_SPEED_STATE group=... speed_class=...
    members=...` at each departure, most 5+ groups with a class, not
    `none`; `RVR_SPEED_TRAVEL window_s=300 groups=... with_speed=...
    travel_under_speed_pct=... leader_holds=...` every five minutes with
    `with_speed` close to `groups` and `travel_under_speed_pct` well above 0
    (roughly 50+ when most groups have speed) and `leader_holds` a few, not
    one per group per minute. In game: a roaming world-bot group runs
    together at song speed, its Skald/Minstrel/Bard sings speed while it
    moves, a straggler catches up at a sprint, the leader briefly stops for
    one far behind; companions and player-led groups behave as before.
    Live log 0.157.1 (2026-09-29 18:41–20:04): speed class in 14 of 23 groups of 5+ (20 of 45 overall); travel_under_speed_pct averages about 30 (0–58), up from 6–12 but below the 50+ target. Stays pending.

68. **PvE wave 7: camp play.** Aaron, 2026-09-29 (P12 "the camp, not the
    mob, is the unit of PvE", P13 "pull size follows control capacity";
    docs/PVE_BOT_PLAY.md). Implemented in source; real-client check
    pending. Autonomous world bots only: solo camp con by class (pet
    casters and rooting casters orange, other casters yellow, melee and
    hybrids yellow/blue, stealthers and Clerics/Healers blue); solo rest
    after a fight to about 75 % power (casters) or 80 % health (melee)
    instead of full; groups with a Sorcerer/Mentalist/Bard pull one mob
    and mez adds nobody touches, groups with two or more pet classes
    mass-pull up to min(6, 2 + pets) same-kind mobs outdoors (not after a
    wipe); groups leave a camp after 3 minutes of a rival party that was
    there first, when outgrown, after a wipe (15-minute memory), and walk
    300-600 units away from threatening enemy-realm players (leave after
    90 s of their presence). Check in `server-console.log`:
    `PVE_PULL_STYLE group=... style=...` once per group at camp with a mix
    of `mez_group`, `mass_pull` (pet groups only) and `single`;
    `PVE_CAMP_LEAVE ... reason=...` with some `rival`, `outgrown` and
    `wipe` but not a flood for the same camp; `PVE_REST window_s=600
    rests=... avg_power_pct_at_pull=...` every ten minutes with power at
    pull roughly 70-85 and health roughly 75-95. In game: a mez group's
    mezzed add stays asleep until the first mob dies; a pet group brings
    several mobs of the same kind at once, never through a wall; a Necromancer world bot hunts orange;
    companions behave as before.
    Live log 0.157.1 (2026-09-29 18:41–20:04): pull styles mixed (25 single, 21 mez_group, 7 mass_pull), but PVE_REST shows power 93–97 % at pull (target 70–85) and camps are left only after wipe/enemy, never rival/outgrown. Follow-up in BUGS.md 72. Stays pending.

67. **RvR wave 5: stealther loop, assist discipline, interrupts.** Aaron,
    2026-09-29 (P9, P4, P10, P11). Implemented in source; real-client check
    pending. Autonomous RvR assassins in stealth doctrines roam and wait
    stealthed (leader or solo 600-1,200 beside the road), open with their
    stealth style on a soft victim only, leave after one kill, a second
    attacker or below 40 % health (run 300-600, hide again after the 10 s
    combat timer, new roaming spot); stealthed world bots move at player
    stealth speed. Damage dealers in called groups take the caller's target
    after 1-2 s unless their own is below 30 % (5-15 % miss the call by
    Patience); archers, melee in reach and instant casters lean toward a
    casting caster or healer; no DoT or AoE on a mezzed non-assist enemy.
    Check in `server-console.log`: `RVR_STEALTH_OPEN bot=... doctrine=...
    target=... target_class=... reason=lone|back_line|resting|last_in_line|straggler
    dist=...` with mostly caster/healer classes and no Armsman/Warrior/Hero
    in `back_line`; `RVR_STEALTH_BREAK bot=... reason=kill|friends|low_hp
    seconds=...` shortly after each open, seconds mostly under 30;
    `RVR_ASSIST_SWITCH switches=... ignored=... median_delay_ms=...` every
    five minutes in group fights with the median between 1000 and 2500 and
    `ignored` about a tenth of `switches`; assassin share of PvP kills
    (`AUTONOMOUS_BOT_DEATH killer_class`) above the 6.2 % baseline; in game
    an Infiltrator/Shadowblade/Nightshade bot is invisible while it roams;
    companions unchanged.
    Live log 0.157.1 (2026-09-29 18:41–20:04): stealth opens only on lone targets and breaks mostly within 30 s, but assassins scored 2.7 % of PvP kills, below the 6.2 % baseline; RVR_ASSIST_SWITCH logged only 4 windows. Follow-up in BUGS.md 71. Stays pending.

65. **RvR wave 4: support by spec.** Aaron, 2026-09-29 (P1 "roles are
    fluid by spec", P8 "support jobs before the assist train"). Implemented
    in source; real-client check pending. Autonomous RvR groups only: smite
    Cleric and nature Druid attack the leader's target only behind a second
    heal-spec healer within 2,000 who can cast, group at 70-80 % or more,
    no cure needed, own power 50 % or more, and not in the 30 % of fights where they stay on heals;
    pac Healer and cave Shaman CC a group mate's attacker before healing
    unless a mate is below 40 %; Healer area stun for autonomous bomb groups;
    grouped autonomous Bards never melee. Check in `server-console.log`:
    `RVR_SUPPORT_OFFENSE window_s=... offense=... control=... heal_only=...
    by_class=...` about every five minutes while RvR groups fight, with
    `heal_only` well above `offense`, `Cleric:`/`Druid:` offense only in
    bigger groups, `Healer:`/`Shaman:` mostly control; `RVR_HEALER_AREA_STUN
    casts=...` only when a bomb group fought; no smiting Cleric in a group
    with one healer, no Bard in melee, companions unchanged.
    Live log 0.157.1 (2026-09-29 18:41–20:04): heal_only dominates, Healer/Shaman control appears, smite Cleric/nature Druid offense never fired (0). Bard melee is not in the log; in-game check pending.

63. **RvR wave 2: danger memory, rest after a fight, retreat with a
    destination.** Aaron, 2026-09-29 (P5-P7). Source 0.147.0: the group
    leader's guild (or the guildless leader) remembers for 60 minutes where it died or retreated
    (1,500-unit cells, at most 24) and weighs roam picks and fight heat there
    by the leader (careful 0.2-0.5, bold 1.3 only with one more member than it
    lost, else 0.6); revenge hunts need two thirds of the lost group size;
    cover routes avoid the worst remembered place. A roaming RvR group sits
    after a fight (leader quiet 8 s, anyone dead or below 70 %) until 90 % or
    a 60-300 s cap (Patience and a +-15 % roll), first moving 600-1,200 off the
    road; stealth doctrines hide again, relic escorts never sit. A PvP retreat
    runs toward the nearest border hub or landing in the zone or a passable
    keep (else the last roam spot) and forces a new destination afterwards.
    Installation and live check pending, from `server-console.log`:
    `RVR_DANGER_RECORD` after RvR deaths (at most one per cell per minute);
    `RVR_ROAM_PICK ... danger_factor=` below 1 for careful groups near their
    loss places; `RVR_GROUP_PAUSE ... reason=rest|after_retreat` followed by
    `RVR_GROUP_PAUSE_END ... reason=recovered` in most cases, `cap` or
    `attacked` sometimes (`relic` when a member picks one up), and no group standing still for more than five
    minutes; `RVR_RETREAT ... anchor=hub|keep|waypoint` more often than
    `anchor=away`, followed by a fresh `RVR_ROAM_PICK`; `RVR_GRUDGE_GATE` when a
    small group skips a revenge trip.
    Live log 0.157.1 (2026-09-29 18:41–20:04): rest pauses end recovered 18 of 22 (longest ~5 min) as expected, but 62 of 77 retreats use anchor=away instead of hub/keep and only 7 of 1,193 roam picks carry danger_factor below 1. Stays pending.

61. **Refresh the PvP & Co-op startup splash.** Source 0.144.0 adds an original
    breached-milegate battle with a mixed-realm party defending against a
    rival guild. The 2880×2160 master and 800×600 preview include a short
    description of rival guilds, besieged keeps and allies; the client archive
    keeps eight uncompressed 1024×768 TGA frames under the legacy internal
    name. Offline extraction and image checks passed. Installation and
    real-client check pending: confirm the new image appears during startup,
    text is readable at the active resolution, and no black splash occurs.


## Finished

99. **Done — Observe the latest installed adaptations and bot keep raids.**
    Completed 2026-10-09 in documentation version 0.214.2. Owner started
    installed 0.214.1 at selected 10x. Verified running installation paths
    and shipped main-assembly matches; reviewed the 20:28:05-21:05:34 CEST
    session and collected 21 fresh snapshot samples over ten minutes.
    All sampled client counts were zero; 3,900 roster entries; achieved speed
    1.88-2.77x (median 2.36x), tick P95 24.6-31.1 ms against 3.333 ms budget.
    Seven sieges opened, thirty forces departed siege muster (four full
    eight-member musters), two musters failed and five sieges timed out;
    no confirmed siege-engine hit, lord defeat or keep claim was logged.
    Reopened bug 75 and recorded 21 leader-promotion exceptions (116) and
    two PvE camp exceptions (117). Last snapshot: 2,046 bots defending keeps.
    Between the read-only save samples at 20:57 and 21:05, 346 matched bots
    gained XP, 62 gained levels and 566 gained RP. These are uncontrolled
    observations, not before/after performance or balance results.
    Route threats remained disabled; active-RA effects/cooldown reload,
    equivalent-stat defenses, appearance, native corner/door routes, EOF
    shutdown and quest-pilot acceptance stay pending. Private evidence and
    the detailed report remain outside Git. No gameplay repair, speed change,
    server/client start, restart, deployment or automated test run occurred.

98. **Done - Isolated upstream adaptation verification.** Owner authorized
    focused regressions and disposable quest migration checks on 2026-10-09.
    Completed in 0.214.1: 40/40 server cases (defense resolution, active-ability
    policy/prerequisites/token serialization, route-threat policy, route
    points), 7/7 clean-fixture migration cases (dry-run, apply, rerun, partial
    ownership, interruption recovery, rollback and refusal guards), and 1/1
    Windows launcher version/timer-constant case passed. SOL 6.1 High found
    no actionable test-source defects. Temporary clones preserved unrelated
    synthetic progress/inventory/currency and the clean source was unchanged.
    No installed game/save was changed or started; no deployment performed.
    Roadmap stages remain pending their appearance evidence and real-client,
    database cooldown reload, native routing and performance checks.

89. **Done — Plan selective upstream adaptations for Camlann.** Completed
    2026-10-09 in 0.209.1. Saved the pinned comparison, seven ordered stages,
    dependencies, Camlann boundaries and acceptance cases in
    [UPSTREAM_ADAPTATION_PLAN.md](UPSTREAM_ADAPTATION_PLAN.md). Other forks are
    excluded. Recorded the confirmed console-loop source defect as bug 115;
    implementation remains proposal 90. Documentation and version pins checked
    statically; no gameplay implementation, builds, automated tests, server
    start or deployment performed.

62. **Done — RvR wave 1: hub fan, departure truce, route variety.** Aaron,
    2026-09-29 (P3, "you leave the door before you hunt"). Source 0.146.0:
    a group leader or solo RvR bot leaving its own border hub first walks to
    a random point 1,000-2,500 units beyond the safe circle it leaves (keep or
    outer bindstone landing; within 120 degrees of its goal);
    each new destination rolls a road, flank or cover route by doctrine
    (keep assaults and siege rallies unchanged); a departing bot (outside the
    safe hub, at most 2,500 units beyond the circle it left, left it under
    180 s ago)
    does not open a fight on a same-realm autonomous bot that is departing
    too; retaliation stays allowed. Installation and live check pending, from
    `server-console.log`: `RVR_ROUTE_CHOSEN` shows `variant=hub_fan` for hub
    exits with mostly `fallback=false` and varied `via=` points;
    `RVR_HUB_TRUCE` appears at Svasud/Sauvage/Druim Ligen; `RVR_ROAM_PICK`
    and `RVR_RETREAT` are logged; the share of RvR PvP deaths 3,500-4,500
    from Svasud Faste's centre falls clearly below the 34 % baseline, and
    groups still reach the frontier (no stuck fan points).
    Source 0.147.0 fix after the first live run (about 90 % of off-road
    via-points fell back): `RVR_ROUTE_CHOSEN` now carries
    `reason=short_leg|no_nav|no_hub|zone|floor|corridor_a|corridor_b|budget|none`.
    Check that `fallback=true` drops to roughly 20-30 % of flank, cover and
    road lines, not counting `reason=short_leg`, and that most remaining
    reasons are `corridor_a` from bots standing inside a keep.
    Live log 0.157.1 (2026-09-29 18:41–20:04): 65 hub-fan exits; flank/cover/road via-points fall back in 19 % (target 20–30 %). Done: accepted from the live log.

64. **Done — RvR wave 3: observe before engaging.** Aaron, 2026-09-29 (P2, "let
    the battle develop a moment before showing your hand"). Source 0.148.0:
    a roaming RvR leader or solo RvR bot that sees two or more enemy parties
    (two or more members, or fighting other players) or a running fight
    within 5,000, with nobody in its group fighting, holds 2,200-2,600 from
    the nearest enemy and decides every 3 s: engage when appetite accepts,
    add at 3 of 8 seen dead (2 bold, 1 when clearly bigger), push on a
    mezzer (Aggression above 55), take a straggler (small doctrines), leave
    when charged, flanked or seen, else move on after 30-110 s (Patience);
    15 % of holds break the rule. Installation and live check pending, from
    `server-console.log`: `RVR_OBSERVE group=... doctrine=... parties=...
    fight_ongoing=... nearest=... ours=...` near busy fights (not at the hub
    edge, not for keep raids or relic escorts), each followed within about
    two and a half minutes by one `RVR_OBSERVE_DECISION ... decision=...`
    with a mix of `third_party`, `leave`, `roam_on` and `attacked`, and
    `straggler` only for small groups; `third_party` mostly with
    `enemy_down=` at or above 3 of 8 (2 for `bold=true`); a `leave` followed
    by `RVR_RETREAT ... reason=observe_leave`; no group standing still for
    more than 150 s while it watches; groups no longer walking straight into
    a fight they declined; no `RVR_OBSERVE` right after a won fight against a
    fleeing lone survivor; `RVR_DANGER_RECORD` after an observe leave only
    when it was charged, flanked or seen.
    Live log 0.157.1 (2026-09-29 18:41–20:04): 229 decisions (107 roam_on, 52 leave, 45 attacked, 16 third_party, 6 straggler); one hold over 150 s. third_party mostly against 0/1 seen enemies, not after 3 of 8 down. Done: accepted from the live log.

66. **Done — RvR wave 6: hub-band peace.** Aaron, 2026-09-29 ("you leave the door
    first, then you hunt"). Implemented in source; real-client check
    pending. Same-realm autonomous world bots cannot attack each other while
    either stands within 6,000 of its own border hub's keep centre (or
    landing radius + 2,500 around an outer bindstone landing); enforced in
    the attack permission, so AoE splash, pets, assist and retaliation are
    covered. Reason: 770 of 1,847 bot PvP deaths (0.146.0) in one cell
    outside Svasud Faste, 98 % Mid on Mid. Check in `server-console.log`:
    the Svasud cell (x 764000-766000, y 664000-666000) holds under 10 % of
    RvR bot PvP deaths; `RVR_HUB_PEACE ... blocked=... by_hub=Svasud:...`
    shows non-zero counts about every five minutes while groups leave the
    hubs, `stray` stays small; `AUTONOMOUS_PVP_ENGAGE_SUMMARY ... hub_band=`
    falls to the few fights with humans or other realms; a human (or an
    Albion/Hibernia bot) can still attack a Mid bot near Svasud and it
    fights back; guards and companions unchanged.
    Addendum (wave 6b, 2026-09-29): implemented in source, real-client check
    pending. The 0.152.0 log moved the grinder to the band edge (316 deaths,
    18 %, Alb on Alb, Forest Sauvage 6.3 km from Castle Sauvage). The peace
    now also holds for 8 minutes after either bot left its own hub's safe
    circle (re-entry clears the clock), and the keep band is 7,500. Check:
    no 1,000 x 1,000 cell within 10 km of a hub holds more than 10 % of RvR
    bot PvP deaths; `RVR_HUB_PEACE ... by_rule=band:...,recent:...` shows
    both counts non-zero while groups leave the hubs; same-realm fights
    still happen out on the frontier after the first minutes.
    Live log 0.157.1 (2026-09-29 18:41–20:04): 0 RvR bot PvP deaths in the Svasud cell (was 42 %); no cell near a hub above 10 %. Largest hotspot moved to Vale of Mularn (155 of those kills by the owner's own character). Done: accepted from the live log.

Bulk close on 2026-09-29 (0.157.2): entries ending in "Closed 2026-09-29
as accepted through use" were implemented in source 0.61.0–0.143.0 and have
been in live use since without an open bug. They are closed as accepted
through use, not through a targeted real-client check; regressions go to
BUGS.md.

7. **Done — Investigate the perceived XP slowdown from level 30 with 10x XP.** Check
   the level XP curve and XP awarded at different levels to determine whether
   the slowdown is expected progression or an unintended drop in rewards, then
   document the intended behavior and fix it if needed. Initial read-only check
   on 2026-09-26: installed save settings show `rates.xp_rate=10`; saved
   characters are levels 19 and 36. The server was not running locally, and the
   save has no XP-award history, so it cannot confirm when the slowdown began.
   Advisor finding for item 47 (2026-09-28): the player curve is faithful to
   1.65 (`XPLevel` in GamePlayer.cs); same-level kills per level at 10x are
   about 10 at 25, 13 at 30, 19 at 39, then 41 at 40 and 88 at 49. The real
   wall is at 40, not 30, and it is not the cause of slow world-bot levelling. Closed 2026-09-29: answered by the curve check above, no fix needed.

40. **Done — Companion battlegroups: goal and plan.** Aaron, 2026-09-28: Aaron and
    Stefan share one battlegroup (`/bg`); each brings 2–3 (up to 5) groups of
    his own companions and they run RvR or PvE raids with it. Companions should
    look like normal players, like the autonomous bots, but are only logged in
    while their owner has them in his group or battlegroup. Delivered as tasks
    41–45; keep the existing `player_companions` storage (converting companions
    into autonomous world bots would pull them into crew guilds, objective
    reassignment, bot XP rates and population login). Closed 2026-09-29: goal delivered as tasks 41–44; the load check stays open as task 45.

60. **Done — Bard travel songs.** Source 0.143.0 selects speed and endurance
    as the Bard travel pair for player-led companions and autonomous bots,
    including sprint starts. Speed stays the anchor, and endurance is
    recast when its child effect runs low. Installation and real-client
    check pending: run and sprint with a Bard, confirm both buffs
    cycle without interrupting follow and endurance holds up. Closed 2026-09-29 as accepted through use.

59. **Done — Name the player-facing world PvP & Co-op.** Source 0.142.0 replaces
    Camlann branding in the launcher, population controls, frontier panels,
    progress-import messages and the `/level` refusal. Historical documentation
    and save-facing identifiers remain intact. Installation and real-client
    check pending: confirm the launcher text fits at the active Windows DPI,
    the progress importer explains the fresh-save policy, and `/level` shows
    the updated message. Closed 2026-09-29 as accepted through use.

58. **Done — Pet pull: healers top the pet up after the fight.** Aaron, 2026-09-28.
    Source 0.141.0: with `/petpull` on, once no pull is running and owner and
    pet are out of combat, healer companions heal the owner's pet to full
    when the group needs no heal. Installation and real-client check pending:
    finish a pull with the pet hurt, confirm a healer tops it to 100% before
    the next pull. Closed 2026-09-29 as accepted through use.

57. **Done — Pet pull: /stay, Animist grove, Mentalist pet HoT, /passive clears
    mushrooms, more pet buffs.** Aaron, 2026-09-28. Source 0.140.0:
    `/stay [on|off]` (pet pull mode only) keeps every companion of the force
    on its spot; an Animist holds one main turret plus damage mushrooms
    (Forest's series, no tanglers) up to the turret caps in front of the camp
    toward the last pull and replants down to 10% power; a Mentalist keeps
    its HoT on the owner's pet, also out of combat. `/stay off`,
    `/petpull off`, `/passive`, logout or a region change end it. `/passive`
    (always) makes Animists delete all their mushrooms and main turret.
    Druid/Cleric base and spec AF and the Cleric heal proc now count as pet
    buffs (the pet armor calculation adds both AF kinds; group spells reach
    group members' pets). Installation and real-client check pending: stay
    and walk away, count mushrooms (10 around the grove), watch replanting and
    the HoT between pulls, `/passive` clears the grove, AF and heal proc land
    on the pet. Closed 2026-09-29 as accepted through use.

55. **Done — Temporarily increase Siege Ram damage for testing.** Source 0.132.0
    multiplies the rider-adjusted Siege Ram damage by 10; a source comment
    marks the multiplier for removal after testing. Installation and real-client
    check pending: compare Siege Ram hits against the prior damage at different
    rider counts and confirm the displayed damage matches the applied damage. Closed 2026-09-29 as accepted through use.

54. **Done — Spend companion RA points through the manager.** Source 0.131.0 shows
    each companion's earned RP, realm rank and unspent RA points. Training &
    Tactics buys one class-legal passive RA rank per click, for active or
    benched companions, and saves the allocation across logout and reinvite.
    Source 0.155.0 moves purchases to a focused view with a persistent point
    balance and visible costs, shortages and purchase results (bug 70).
    Timed active RAs remain unavailable because companion AI does not use them.
    Real-client check pending: earn RP, buy a passive rank,
    verify its effect and remaining points, bench/reinvite and relog, and
    confirm that purchases cannot exceed the earned pool. Closed 2026-09-29 as accepted through use.

53. **Done — Delete companions and identify regular recruits in the manager.** Source
    0.130.0 labels roster entries and details Regular or Story. Overview has
    [Delete] followed by [Confirm delete] or [Cancel]. Deletion removes the
    saved companion and starter gear in one transaction; earned, traded, or
    unclassified items must be cleared first. Installation and real-client
    check pending: identify both types, cancel and confirm deletion, verify an
    active companion leaves its group, and confirm protected gear blocks it. Closed 2026-09-29 as accepted through use.

52. **Done — Larger, adjustable Companion Manager window.** Source 0.129.0 grows
    the default client XML layout to 720×500 and enables the stock lower-right
    resize handle, with expanding backgrounds and aligned bottom controls.
    Installation and real-client check pending: open the manager at 800×600,
    drag it larger and confirm both skins keep text, hit areas, panels and
    close/scroll controls aligned. Closed 2026-09-29 as accepted through use.

51. **Done — Prefer Underhill Ally for Enchanter companions.** Source 0.128.0
    makes persistent Enchanter companions choose their highest learned,
    castable Underhill Ally across builds and replace an alternate idle
    main pet. If no Ally is available, normal pet selection remains.
    Real-client check pending: confirm automatic Light, Mana and
    Enchantment builds and manual companions choose Ally when learned,
    fallback before it is learned, and defer pet replacement during combat. Closed 2026-09-29 as accepted through use.

50. **Done — Join an account-owned guild as guild leader.** Source 0.127.0 adds
    `/gc join <guild name>` for an unguilded character when another
    character on the same account holds rank 0 in that guild. The new
    character joins at rank 0 through the normal guild membership path.
    Installation and real-client checks pending: join from a same-account
    alt, confirm rank and guild display after relog, and confirm an
    account without a rank 0 guild character cannot join. Closed 2026-09-29 as accepted through use.

49. **Done — Remove an owned companion from a former guild.** Source 0.126.0 adds
    `/companions guild leave <name>` for active or benched saved companions,
    including when the owner is no longer in that guild. It saves the
    roster membership and clears the active bot guild. Installation and
    real-client check pending: remove Eydis from Odins, confirm the
    guild display updates, and confirm she remains guildless after relog. Closed 2026-09-29 as accepted through use.

46. **Done — Pet pull as a group mode, not a pull command.** Aaron, 2026-09-28:
    `/petpull` (toggle) or `/petpull on|off` switches pet pull mode for the
    owner's group and squads; it ends at logout. Every pull then starts with
    the pet's own engage (attack order, pet attacking or in combat), not with a
    command target; the pet order no longer sends companions in. Companions
    hold real damage until the passive pet is back within 400 units, intercept
    only adds on or running at someone of the group, keep the HoT, Animist camp
    front and pet buff priority. If the pet is hurt (under 70 %, or under 90 %
    with three or more attackers), the owner gets a warning; direct pet heals
    and tank peels wait until release so they do not draw the pet's attackers
    toward the group. Release (pet under 45 %, pet dead, player attacks, 60 s)
    ends that pull, not the mode; a fresh pet order after it starts the next
    one (chain pull). Source 0.133.0 also suppresses ordinary group BAF for a
    mob actually attacked by the pulling pet while that pull is held, even
    if threat selects a group member; mode-off fights and fights without the
    held pulling pet among the attackers retain normal BAF. Source 0.138.0
    also closes the remaining pre-release companion heal and peel paths after
    a residual group-aggro report on installed 0.133.0. Real-client check pending. Closed 2026-09-29 as accepted through use.

44. **Done — Battlegroup combat in RvR and PvE raids.** All companion groups assist
    the battlegroup leader's (or their owner's) target, heal their own group
    first, share resurrection reservations across groups, and obey
    `/passive`, `/defensive`, `/aggressive` and `/petpull` holds from their
    owner. Keep the 1.65 feel: no perfect focus-fire.
    Source 0.117.0; installation and real-client checks pending. Closed 2026-09-29 as accepted through use.

43. **Done — Battlegroup march formation.** Each companion group leader follows its
    owner at a small offset (about 2–4 body lengths, one slot per group) and its
    members follow that leader; portals and region changes bring every group
    along; stick runs sprint (task 36).
    Source 0.117.0; installation and real-client checks pending. Closed 2026-09-29 as accepted through use.

42. **Done — Battlegroups with companion groups.** `/bg` accepts companions and
    companion-led groups. An owner forms up to 5 companion groups, each led by
    a chosen companion, and brings them into his battlegroup; two human owners
    (Aaron and Stefan) can share one battlegroup. Companions stay logged in
    while they are in their owner's group or in a group of his battlegroup and
    are benched when that ends or the owner quits. Owner-bound behavior (XP
    copy, gear rewards, portal/region follow, orders) resolves the owner, not
    "same group as the owner". Group assignment is saved per companion.
    Source 0.117.0; installation and real-client checks pending. Closed 2026-09-29 as accepted through use.

41. **Done — Companions appear as players, with Realm Points.** Show active companions
    in `/who` and in the launcher's Active Population list (today: dash), with
    level, class, guild, zone and Realm Points; `/send` to a companion reaches
    its owner or gets a short in-character reply; companion names are unique
    against real characters and world bots for new recruits. Companions earn
    Realm Points (and realm rank) in RvR like autonomous bots, saved additively
    on `player_companions`; XP rules stay as they are.
    Source 0.117.0; installation and real-client checks pending. Closed 2026-09-29 as accepted through use.

39. **Done — /petpull for Enchanter pet pulls.** Aaron, 2026-09-27: send the pet in,
    Mentalist HoT on it, healers careful until aggro is built, tanks peel
    adds. Researched against 1.65 focus-pull practice (Uthgard, FreddysHouse,
    Allakhazam). Source 0.114.0: see CHANGELOG. Source 0.115.0 (Aaron's
    follow-up): release once the passive pet is back at the player, Animist
    mushrooms in front of the group, pet-useful buffs to the pet first while
    pet pulling. Not modelled: the Enchanter's focus damage shield. Real-client
    check pending. Command semantics replaced by task 46: `/petpull` is now a
    group mode, and a pull starts with the pet's own attack. Closed 2026-09-29 as accepted through use.

38. **Done — Companion Manager tab for companions in the group.** Aaron,
    2026-09-27: next to Roster and Recruit, an Active tab showing only the
    companions currently in the group. Source 0.113.0: server tab plus an
    XML-only click link (game.dll unchanged, offline client test passes).
    Real-client check pending. Closed 2026-09-29 as accepted through use.

37. **Done — One companion roster for all characters of an account.** Aaron,
    2026-09-27: his character Ked should level with the same companions as
    Nova and join Nova's guild North Bomb. Source 0.112.0: the roster is
    shared by every character of the account (records keep their recruiting
    character; the 78 limit counts the whole account). Ked was added to
    North Bomb directly in the local database. Real-client check pending. Closed 2026-09-29 as accepted through use.

36. **Done — Companions sprint on the stick in RvR.** Aaron, 2026-09-27: when the
    group runs with sprint and an endurance buff, the companions sticking
    behind should sprint too. Source 0.111.0: on a stick run behind a
    sprinting leader each companion turns on sprint and pays the player's
    endurance cost; sprint ends with the leader's sprint, the run, or the
    fight. The stick pace is unchanged (companions already matched the
    leader's speed). Real-client check pending. Closed 2026-09-29 as accepted through use.

34. **Done — Level-50 life between RvR tours and raid sign-ups.** Aaron,
    2026-09-27: level-50 bots mostly take a town break (sell loot, buy what
    they need) and sometimes run Darkness Falls, spending seals at the DF seal
    merchants; they sign up for raids on the side, and when a raid's start
    time comes, the signed-up bots leave RvR or PvE and gather for it, as
    players did. Context: level-50 PvE today mostly fails (22 of 705 goal
    attempts reached a camp, 0 kills); raids need 200 simultaneous level-50
    GroupPve bots and never start; bots never spend seals; money is flat.
    Source 0.104.0 (partial): veterans take a town break instead of owing PvE,
    level-50 PvE prefers DF, bots spend seals at DF merchants. Source 0.105.0
    adds the raid calendar with sign-ups (see CHANGELOG). Still open: buying
    consumables. Checks pending: raid announcement, sign-ups, muster, raid. Closed 2026-09-29 as accepted through use.

33. **Done — Bots stack in Jordheim at the vault keeper.** Aaron, 2026-09-27: many
    bots stand in one spot next to the vault keeper. In town they should sell
    their items, buy what they need, then find a group, go solo, or return to
    their group. Cause: releases outside the own realm and watchdog recoveries
    all landed on one capital coordinate (Jordheim: beside Jarl Yuliwyf, the
    vault keeper); parties then sat in "Choosing group target" with no timer
    while non-leaders froze. Source 0.103.0 spreads capital arrivals, ends
    target choice after five minutes, walks members back to their leader, and
    sends bots to a merchant at 70 % backpack. Buying consumables and grouped
    town visits remain for task 34. Check pending: capitals after deaths. Closed 2026-09-29 as accepted through use.

35. **Done — Smooth, individual group travel and field backfill.** Aaron on 0.101.0:
    groups walk jerkily in lines; groups should keep refilling (nearby
    guildmates, border-keep LFG, merging small groups). Source 0.102.0: see
    CHANGELOG. Checks pending: group walking, companions following, groups
    growing in the field. Closed 2026-09-29 as accepted through use.

32. **Done — RvR groups form and stay together.** Aaron on 0.100.0: "almost only
    single bots running around". Cause from code and his log: leaders found
    0 free guildmates in 98 % of attempts because ungrouped RvR bots roamed
    and fought at once; roamers waited solo for a full eight; one death sent
    the whole group back to a town; three deaths pulled bots out of their
    group. Source 0.101.0: group seekers LFG at the border keep, viable groups
    of 4 leave, partial deaths are fought through and rezzed, wipes regroup at
    the border keep, deaths in a group no longer end the RvR tour. Research
    notes: scratchpad research-forming-death.md summarized in
    RVR_GROUP_DOCTRINE.md. Checks pending: groups visible in the frontier. Closed 2026-09-29 as accepted through use.

31. **Done — Companion loot: fewer drops, no endlessly full backpacks.** Aaron,
    2026-09-27: one companion gets a reward per kill instead of every
    companion; when a companion's backpack is full, clear room in one go
    (e.g. sell two bags' worth of the worst unlocked items) instead of
    selling one item per drop. Respect [Keep] and manual locks. Also relieves
    the kill lag of bug 42.
    Source 0.99.0: one random eligible companion per owner rolls per kill;
    a full backpack sells up to 16 of the worst earned, unlocked items at once
    ([Keep], starter, player-supplied and legacy items are never sold);
    the owner's money cap still limits the batch. Check pending: drops and
    backpack clearing while levelling. Closed 2026-09-29 as accepted through use.

30. **Done — Camlann guild cohesion and encounter memory.** Source 0.98.0: nearby
    autonomous guildmates who are not busy answer a guildmate's fight by
    their Sociability (decision stable for 30 s); guilds keep an in-memory,
    one-hour win/loss tally against other guilds that makes a crew bolder or
    warier (appetite x0.6-1.3); persisted grudges still decide whom a guild
    hunts. RvR healers heal only their own guild's keep guards. Source 0.99.0
    finishes the realm-leftover review: siege sides are guilds (the opener's
    guild attacks, the owner's guild defends, other guilds contest), rally
    orders, keep plans, attendance and inactivity protection look up the
    force instead of each member's realm, rally posts, friendly doors, siege
    engines and job pools follow the guild, and RvR warbands already in the
    frontier may gather at their guild keep or their realm's border keep.
    Carrier escorts and realm event notices still use realms. Real-client
    checks pending: guild help, guard healing, a guild siege gathering. Closed 2026-09-29 as accepted through use.

29. **Done — Varied RvR roaming.** Source 0.98.0 replaces the fixed west-to-east camp
    loop with weighted wandering: keeps, frontier clearings, enemy sightings
    and recent fight spots (in-memory heat, 12 minutes) weighted by the
    group's doctrine, distance and the last five spots visited. Groups now
    linger at a spot from arrival (doctrine time scaled by Patience) instead
    of leaving on arrival, and do not re-plan every minute while walking to a
    roaming spot. Travel formation follows the doctrine: two-file column
    (melee front, healers middle, casters back), clump, or a loose fan.
    Source 0.100.0 adds hotspot weights (Emain Macha x3, Hadrian's Wall and
    Odin's Gate x1.6) and raises other-frontier destinations from 0.35 to
    0.6, so groups meet across realm borders.
    Checks pending: watch several warbands for distinct routes and lingering. Closed 2026-09-29 as accepted through use.

28. **Done — Human-like RvR combat habits.** Source 0.98.0: autonomous RvR bots pick
    targets by habit (stick time from Patience, caller's target by the
    doctrine's follow chance, otherwise weighted toward healers, mezzers,
    wounded enemies and whoever is on their own healers, with random spread);
    fight appetite from doctrine, leader Aggression and recent history with a
    small chance to dare a bigger group; retreat is a decision (healer dead,
    half down, outnumbered, doctrine bias, RiskTolerance, 15 % stay anyway)
    that runs 2,200 units away for 25-40 s while only self-defense continues.
    Checks pending: fights look human, retreats end and regroup. Closed 2026-09-29 as accepted through use.

27. **Done — RvR group doctrine for autonomous warbands.** Source 0.98.0 derives one
    of 14 doctrines from each warband's real classes (see
    [RVR_GROUP_DOCTRINE.md](RVR_GROUP_DOCTRINE.md)), including imperfect
    pickup groups. Group sizes still come from the existing warband rolls
    (solo, pairs, 3-5, 8); keep raids above 8 remain several 8-member parties
    in one siege event. Checks pending as for 28-29. Closed 2026-09-29 as accepted through use.

26. **Done — Stun → bomb and owner assist for player-led companions.** Source
    0.97.0: companion bombers hold their first PBAoE on an enemy player clump
    up to 2.5 s while a group Healer has an area stun ready; roster
    companions use the PvP assist/defence engagement (owner's target, 30 s
    focus, threat memory). Real-client checks pending: RvR fight with the
    Midgard bomb group. Closed 2026-09-29 as accepted through use.

25. **Done — Companion auto-levelling hits the key spell breakpoints.** Source
    0.97.0: plans carry ordered milestones (Skald Battlesongs 43, Shaman
    Mending 7 and Cave 27, Thane Stormcalling 34 and Shields 42, pac Healer
    Pacification 38, new Healer `support` build with Augmentation 18 and
    Pacification 23); points are saved for the next breakpoint; existing
    automatic companions are retrained for free when they load or level.
    Check pending: Aaron's level-45 companions after loading. Closed 2026-09-29 as accepted through use.

8. **Done — All-class companion builds and caster area damage.** Source 0.96.0
   provides 118 static career/skill-checked plans for all 39 Classic + SI
   classes, at least three per class, with existing defaults preserved. The
   selected automatic build now drives learned specialization, weapon/style,
   spell, pet, and combat-role paths. A saved per-companion ranged-AoE choice
   (Off, 2+ through 8+, default 3+) counts engaged mobs and hostile guards
   belonging to the same hostile keep; a cast is refused if its area would hit
   an idle bystander, unrelated guard, player, illegal target, or protected
   mezz. Necromancer servant area wrappers use that threshold around the
   servant's payload center. Fresh recruits receive plan-aligned starter gear;
   switching an existing companion only activates compatible gear already
   equipped and preserves owner gear and manual locks. Static and runtime-contract
   test coverage was added. CoreServer and the test assembly compiled in Release
   with zero errors; automated tests were not run. Installation and real-client
   verification remain pending: exercise representative builds and learned
   skills, default and saved selection behavior, existing gear and
   locks, AoE thresholds against mobs and same-keep hostile guards, and all
   safety exclusions. See [the implementation and acceptance plan](COMPANION_BUILD_AOE_PLAN.md). Closed 2026-09-29 as accepted through use.

24. **Done — Companions rebuff far too often.** Reported by Aaron on 0.92.0. Source
    0.93.0: player-led companions keep only long buffs (5 min or longer, or
    concentration) up out of combat and refresh them in their last minute;
    short buffs are not maintained out of combat; speed only while traveling;
    player-led Skalds sing only the speed song while the group travels.
    Real-client check pending: buff frequency and power use after a pull. Closed 2026-09-29 as accepted through use.

23. **Done — Companion inventory like your own.** Source 0.92.0 adds worn slots
    (positions 1-19) and the backpack (21-60) to the companion inventory
    window with drag-to-equip from the owner's backpack, **[Info]** and a
    slimmer slot view in the Gear tab, and manual choices that give way only to
    clearly better earned loot. Real-client checks pending: vault grid
    columns and ring/wrist pairs side by side, dragging from your backpack
    onto a worn slot, the **[Info]** window, and the swap chat line. Closed 2026-09-29 as accepted through use.

22. **Done — Companions rest with a sitting player.** Source 0.90.0 lets roster
    companions start fast recovery for any missing health, power, or endurance
    while their player-leader sits, and finish to full. Before, they rested only
    below 70% health, 45% power, or 35% endurance and otherwise regenerated at
    standing speed. Travel and combat still interrupt the rest. Real-client
    check that casters refill during a sit is pending. Closed 2026-09-29 as accepted through use.

21. **Done — Remove level and ability prerequisites from realm ability training.** Source
    0.94.0 removes character-level gates and ignores ability-specific
    prerequisites in the trainer; a level 42 character can train Mastery of
    Pain without Augmented Dexterity II. Realm Point costs, maximum ranks, and
    class availability remain; RR5 abilities still unlock at Realm Level 40.
    Installation and real-client checks of low-level training and ability effects
    are pending. Closed 2026-09-29 as accepted through use.

20. **Done — Remove the player logout timer.** Source 0.85.0 completes accepted player logout immediately, including during combat and while moving, without changing the dead, mounted, crafting, or instance restrictions. `/stuck` still uses its safe position on successful logout and clears the request if logout is refused. Installation and real-client checks of combat logout, normal logout, and `/stuck` remain pending. Closed 2026-09-29 as accepted through use.

19. **Done — Enable companion area taunt.** Source 0.84.0 lets persistent Armsman, Hero, and Warrior companions cast their learned Taunting Shout when at least two attackable NPCs in the frontal cone are already fighting the group. The AI skips cones containing idle NPCs or protected mezzes and leaves single-target taunts in place. Installation and real-client checks of cone targeting, threat transfer, cooldown, and mezz/idle safety remain pending. Closed 2026-09-29 as accepted through use.

18. **Done — Give group members buff priority over pets.** Source 0.83.0 selects missing buffs for eligible group members before pet buffs across routine upkeep and fallback selection. Realm buffs choose members before attached pets, while pet-only and spare pet coverage remain available once member needs are met. Installation and real-client checks with mixed player/companion/pet groups remain pending. Closed 2026-09-29 as accepted through use.

17. **Done — Prioritize Shaman endurance buffs for the whole group.** Source 0.82.0 selects the highest learned endurance rank before other routine Shaman buffs, covers nearby group members before pets or other allies, and frees concentration from a lower priority buff when needed for a group member. Installation and real-client checks with Kiri and full autonomous groups remain pending, including coverage after travel and combat. Closed 2026-09-29 as accepted through use.

14. **Done — Companion bombers open a pull with one loose volley.** Implemented in source 0.81.0. Requested so a group's bombs land together for maximum damage without constant re-syncing or cut casts. Result: the first companion bomber in position holds its first bomb for at most 1.2 s until the group's other companion bombers within 1,500 units are in position; then all fire and chain freely for 6 s after each bomb, so a new pull syncs again. Holding never starts while a cast is running; human bombers are not waited for. Unit tests cover the hold, release, chain and stale-wait cases; real-client check of volley timing pending. Closed 2026-09-29 as accepted through use.

13. **Done — Join Friend remembers the connection details and, optionally, the password.** Implemented in source 0.77.0. Requested so guests do not retype everything each session. Result: the form pre-fills host address, host account and guest account from `%LOCALAPPDATA%\OfflineDAoC\join-friend.json`; "Remember my password" stores it encrypted with Windows DPAPI for the current Windows user (unreadable for other users or PCs), and unticking it removes the stored password. Details are saved after the client starts. Launcher tests cover the round trip, the absence of plaintext on disk and damaged files. Real-client check of pre-fill and rejoin pending. Closed 2026-09-29 as accepted through use.

12. **Done — Diagnose recruitment eligibility and repair separated-party coordination at the existing population.** Source 0.75.0 adds exclusive PvP rejection categories, late-recruitment failure categories and throttled travel-hold diagnostics; repairs missing-zone town lookup, distant-combat travel holds, final-camp-ahead crossing waits and single-seat backfilling. No population setting was changed. The matched 0.74.0 observation showed 1,974 zero-eligible attempts versus 22 eligible-but-no-route failures, camp-selection endings down from twelve to three, and full eight-member PvE assemblies down from seven to one. These are uncontrolled observations. Exact exclusion causes and the complete explanation for smaller PvE parties need the new logs; changes await installation and real-client verification. Server and Windows launcher Release builds passed with zero errors; whitespace, conflict-marker, line-ending and version-pin checks passed. Automated tests were skipped under the project rules. Closed 2026-09-29 as accepted through use.

11. **Done — Follow up on the 0.73.0 runtime observation.** Source 0.74.0 keeps initial PvP recruitment open for ten simulated minutes, advertises partial guild parties at new task boundaries without reversing coordinator/allocation locks, removes the eight-waiter invitation cutoff, rotates failed late-recruitment route probes, recalculates missing class roles, and rechecks the planned PvE camp before admitting late recruits. Diagnostics now separate candidate/route counts and capture camp-distance/movement state at group endings. Server and Windows launcher Release builds passed with zero errors; version, conflict-marker and line-ending checks passed. Automated tests were skipped under the project rules. Installation and gameplay acceptance remain pending. Pending: compare full PvP arrivals, healer/tank composition, camp-selection failures and travel expirations after deployment; the thirteen observed travel timeouts are not claimed fixed. Closed 2026-09-29 as accepted through use.

10. **Done — Larger autonomous parties and viable guild assaults.** Source 0.73.0 fills spare seats during a bounded initial recruitment window, permits route-validated cross-region guild PvP invitations, favors full organized warbands, selects class support, and coordinates eligible guildmates only at new task boundaries. New keep assaults require eight members, healing and siege supplies. PvE remote recruitment can fill all seven follower seats. Installation and sustained high-population verification remain pending: compare party-size distribution, actual meetup arrivals, class composition, keep attendance and captures, routing cost, and small-population fallback. Existing tasks, solo-oriented types, guild hostility and eight-member capacity remain authoritative. Closed 2026-09-29 as accepted through use.

9. **Done — Frontier PvE incentives and NPC-held keeps/relics.** Source version 0.72.0 adds +50% base monster XP in Old Frontiers/DF, on top of the configured rate; restores a manifest of 4,851 archived frontier spawns; and adds stationary Frontier Wardens garrisons, lord-unlocked claim stewards, 25 base guard RP and 1,500 base capture RP (30-minute reward cooldown per keep). Autonomous crews explicitly claim for their guild using native permissions and group checks. Fresh/reset worlds use NPC ownership; captured territory persists across restarts and existing guild claims are preserved. Server, launcher and Setup build verification are recorded in [FRONTIER_CAMPAIGN.md](FRONTIER_CAMPAIGN.md). Installation, backed-up spawn migration, world density/navigation, human/crew capture, rewards, restart persistence and relic raids await owner verification. Closed 2026-09-29 as accepted through use.

1. **Done — Private Tailscale co-op between two home installations.** The launcher now
   has a client-only Join Friend shortcut with a Tailscale IPv4 login-port check,
   separate host and guest account names, and remote-session diagnostics. Source
   version: 0.60.0. Still needs host-side private network setup and the real
   two-home login, region transition, group, reconnect, save persistence, and
   reversed-host checks in [TAILSCALE_COOP.md](TAILSCALE_COOP.md). Closed 2026-09-29 as accepted through use.

2. **Done — Autonomous dungeon activity, Darkness Falls and Camlann PvP behavior.** Implemented in source 0.61.0 after the owner's explicit DF implementation approval. DF now has a bounded entrance-stair repair, 1,417 proved spawn destinations and extra weight within the dungeon share. Hunters can patrol dungeons; ordinary pickup groups can choose connected dungeon camps. PvE aggression bypasses, patrol dwell timing and misleading RvR launcher labels are corrected. Source 0.79.0 modestly raises DF destination weight from two to three for eligible XP camps and Hunter PvP patrols, without changing the overall dungeon share. Awaiting installation, all-entrance client traversal and live PvE/PvP balance checks in [CAMLANN_PVP_REVIEW.md](CAMLANN_PVP_REVIEW.md). Closed 2026-09-29 as accepted through use.

3. **Done — Population slider and Danger tooltips.** Expanded in source 0.62.0: all six type labels, sliders and percentage inputs explain the behavior, relevant levels and the effect on new versus existing bots; Danger explains its effect on existing Hunters after a restart. Source 0.70.0 further distinguishes saving new-bot weights from applying a live mix to the saved roster. Installation and launcher hover verification pending. Closed 2026-09-29 as accepted through use.

4. **Done — Focus staff selection for casters.** Source 0.65.0 ranks earned and owned staves by the focus levels covering learned spell lines, including all-lines focus, before general item value. Installation and real-client inspection of caster loadouts and power use remain pending. Closed 2026-09-29 as accepted through use.

5. **Done — Realm Points in Active Population.** Source version 0.67.0 adds a sortable Realm Points column using live points for active autonomous bots and saved points otherwise. Companions without a Realm Points record show a dash. Launcher display and live point updates await installation verification. Closed 2026-09-29 as accepted through use.

6. **Done — Active Groups for large populations.** Source version 0.68.0 replaces the per-group card stack with a sortable, realm-filterable table. Search covers group, bot, class, zone, and task details; selecting a row shows member roles, locations, and live timers. Launcher installation and inspection with a large list (including the reported 82-group case) remain pending. Closed 2026-09-29 as accepted through use.

7. **Done — Bard songs useful during combat.** Source version 0.69.0 keeps grouped Bards with an endurance song on their instrument during combat, preserves the endurance pulse, and lets them use the existing group-support actions. Mana and speed songs resume after combat; solo Bard behavior is unchanged. Installation and real-client verification of endurance upkeep alongside healing and control remain pending. Closed 2026-09-29 as accepted through use.

8. **Done — Apply population-type changes to existing bots live.** Source version 0.70.0 adds a launcher action and server request/status flow for the entire non-retired saved autonomous roster, including offline bots. Player-led companions and temporary helpers are excluded. Offline changes run in bounded batches; active changes wait for safe task or group boundaries. The server saves the requested mix and bot types, and reports pending/applied/failed status. Installation and live acceptance remain pending: verify counts converge, active groups stay intact, repeated application is stable, and settings plus character progress survive restart. Closed 2026-09-29 as accepted through use.

3. **Done — Check XP and Realm Point rewards for `/companions` group members**
   (0.83.1, 2026-09-26). Source audit confirms persistent companions gain XP
   only from eligible NPC kills and gain no Realm Points. Both human-victim and
   autonomous-bot-victim PvP reward paths exclude persistent companions, even
   when they deal damage. The player receives PvP XP and Realm Points only for
   qualifying player or player-controlled-pet damage; companion damage does not
   become owner kill credit. Thus PvP kills can advance the player while leaving
   companions at their prior level, consistent with the reported level 17 versus
   13 gap after two kills. No pre-kill XP snapshots were available to quantify
   those two awards; real-client reward amounts were not measured. The intended
   reward boundary is documented in [COMPANION_BOTS.md](COMPANION_BOTS.md).

2. **Done — Thirty-minute observation of installed 0.73.0** (2026-09-26). Captured 61 snapshots at 600-bot peak population: seven PvE assemblies had eight members physically present; nine eight-member PvE rosters began tasks. PvP task starts remained two to four members and no keep ownership changed. Thirteen parties hit the camp-travel deadline and twelve found no usable camp. No observed freeze or empty-party exception recurrence; all samples had zero connected clients. Evidence supports partial PvE recruitment success, not a controlled before/after performance improvement. Raw logs and the detailed report remain outside Git; broader feature acceptance stays pending.

1. **Done — Review current Camlann bot setup and running-session activity** (2026-09-25). Confirmed the PvP ruleset, inspected type/charter meaning, sampled 600 active bots and recorded combat/activity aggregates without changing the running installation. Findings and offline navigation evidence are in [CAMLANN_PVP_REVIEW.md](CAMLANN_PVP_REVIEW.md). Implementation acceptance remains in the pending item above; camp-travel expirations remain in the bug tracker.
