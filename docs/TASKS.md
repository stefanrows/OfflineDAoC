# Tasks and Ideas

Capture rapid-fire tasks, feature requests, and ideas here. Include the desired outcome, scope, and any constraints or acceptance checks that are known; leave unknown details explicit rather than inventing requirements. Recording an idea does not authorize implementing the backlog. Bugs belong in [BUGS.md](BUGS.md).

When a task is done and its required verification is complete, move it out of its current section into **Finished** immediately, mark it **Done**, and record a brief result and completion version (or date for tracking-only work). Do not leave completed items in **Open**. If implementation still awaits installation or real-client verification, keep it under **Implemented in source; installation verification pending**, recording the implementation version and outstanding checks, until those checks are complete. Preserve completed entries as history.

## Open

Agent sessions on items 45–48: take the role and context from
[ORCHESTRATOR_BRIEF.md](ORCHESTRATOR_BRIEF.md) first.

7. **Investigate the perceived XP slowdown from level 30 with 10x XP.** Check
   the level XP curve and XP awarded at different levels to determine whether
   the slowdown is expected progression or an unintended drop in rewards, then
   document the intended behavior and fix it if needed. Initial read-only check
   on 2026-09-26: installed save settings show `rates.xp_rate=10`; saved
   characters are levels 19 and 36. The server was not running locally, and the
   save has no XP-award history, so it cannot confirm when the slowdown began.
   Advisor finding for item 47 (2026-09-28): the player curve is faithful to
   1.65 (`XPLevel` in GamePlayer.cs); same-level kills per level at 10x are
   about 10 at 25, 13 at 30, 19 at 39, then 41 at 40 and 88 at 49. The real
   wall is at 40, not 30, and it is not the cause of slow world-bot levelling.

40. **Companion battlegroups: goal and plan.** Aaron, 2026-09-28: Aaron and
    Stefan share one battlegroup (`/bg`); each brings 2–3 (up to 5) groups of
    his own companions and they run RvR or PvE raids with it. Companions should
    look like normal players, like the autonomous bots, but are only logged in
    while their owner has them in his group or battlegroup. Delivered as tasks
    41–45; keep the existing `player_companions` storage (converting companions
    into autonomous world bots would pull them into crew guilds, objective
    reassignment, bot XP rates and population login).

45. **Battlegroup load check.** Measure server tick and pathing cost with two
    owners and 5 companion groups each in RvR before calling tasks 42–44 done.

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
      instead of being marked PvE completed. Package B remains open.

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
    fights. Real-client/live-log check pending. Point 6 (1.65 doctrine
    tuning) stays open. Cause e (empty RvR expiry) traced to the group task
    clock publishing an empty expiry while paused; not changed yet.

## Implemented in source; installation verification pending

46. **Pet pull as a group mode, not a pull command.** Aaron, 2026-09-28:
    `/petpull` (toggle) or `/petpull on|off` switches pet pull mode for the
    owner's group and squads; it ends at logout. Every pull then starts with
    the pet's own engage (attack order, pet attacking or in combat), not with a
    command target; the pet order no longer sends companions in. Companions
    hold real damage until the passive pet is back within 400 units, intercept
    only adds on or running at someone of the group, keep the HoT, Animist camp
    front and pet buff priority; if the pet is hurt (under 70 %, or under 90 % with
    three or more attackers), healers heal it and each tank taunts one add off it
    before the release. Release (pet under 45 %, pet dead, player attacks, 60 s)
    ends that pull, not the mode; a fresh pet order after it starts the next
    one (chain pull). Implemented in source; real-client check pending.

44. **Battlegroup combat in RvR and PvE raids.** All companion groups assist
    the battlegroup leader's (or their owner's) target, heal their own group
    first, share resurrection reservations across groups, and obey
    `/passive`, `/defensive`, `/aggressive` and `/petpull` holds from their
    owner. Keep the 1.65 feel: no perfect focus-fire.
    Source 0.117.0; installation and real-client checks pending.

43. **Battlegroup march formation.** Each companion group leader follows its
    owner at a small offset (about 2–4 body lengths, one slot per group) and its
    members follow that leader; portals and region changes bring every group
    along; stick runs sprint (task 36).
    Source 0.117.0; installation and real-client checks pending.

42. **Battlegroups with companion groups.** `/bg` accepts companions and
    companion-led groups. An owner forms up to 5 companion groups, each led by
    a chosen companion, and brings them into his battlegroup; two human owners
    (Aaron and Stefan) can share one battlegroup. Companions stay logged in
    while they are in their owner's group or in a group of his battlegroup and
    are benched when that ends or the owner quits. Owner-bound behavior (XP
    copy, gear rewards, portal/region follow, orders) resolves the owner, not
    "same group as the owner". Group assignment is saved per companion.
    Source 0.117.0; installation and real-client checks pending.

41. **Companions appear as players, with Realm Points.** Show active companions
    in `/who` and in the launcher's Active Population list (today: dash), with
    level, class, guild, zone and Realm Points; `/send` to a companion reaches
    its owner or gets a short in-character reply; companion names are unique
    against real characters and world bots for new recruits. Companions earn
    Realm Points (and realm rank) in RvR like autonomous bots, saved additively
    on `player_companions`; XP rules stay as they are.
    Source 0.117.0; installation and real-client checks pending.

39. **/petpull for Enchanter pet pulls.** Aaron, 2026-09-27: send the pet in,
    Mentalist HoT on it, healers careful until aggro is built, tanks peel
    adds. Researched against 1.65 focus-pull practice (Uthgard, FreddysHouse,
    Allakhazam). Source 0.114.0: see CHANGELOG. Source 0.115.0 (Aaron's
    follow-up): release once the passive pet is back at the player, Animist
    mushrooms in front of the group, pet-useful buffs to the pet first while
    pet pulling. Not modelled: the Enchanter's focus damage shield. Real-client
    check pending. Command semantics replaced by task 46: `/petpull` is now a
    group mode, and a pull starts with the pet's own attack.
38. **Companion Manager tab for companions in the group.** Aaron,
    2026-09-27: next to Roster and Recruit, an Active tab showing only the
    companions currently in the group. Source 0.113.0: server tab plus an
    XML-only click link (game.dll unchanged, offline client test passes).
    Real-client check pending.
37. **One companion roster for all characters of an account.** Aaron,
    2026-09-27: his character Ked should level with the same companions as
    Nova and join Nova's guild North Bomb. Source 0.112.0: the roster is
    shared by every character of the account (records keep their recruiting
    character; the 78 limit counts the whole account). Ked was added to
    North Bomb directly in the local database. Real-client check pending.
36. **Companions sprint on the stick in RvR.** Aaron, 2026-09-27: when the
    group runs with sprint and an endurance buff, the companions sticking
    behind should sprint too. Source 0.111.0: on a stick run behind a
    sprinting leader each companion turns on sprint and pays the player's
    endurance cost; sprint ends with the leader's sprint, the run, or the
    fight. The stick pace is unchanged (companions already matched the
    leader's speed). Real-client check pending.
34. **Level-50 life between RvR tours and raid sign-ups.** Aaron,
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
    consumables. Checks pending: raid announcement, sign-ups, muster, raid.

33. **Bots stack in Jordheim at the vault keeper.** Aaron, 2026-09-27: many
    bots stand in one spot next to the vault keeper. In town they should sell
    their items, buy what they need, then find a group, go solo, or return to
    their group. Cause: releases outside the own realm and watchdog recoveries
    all landed on one capital coordinate (Jordheim: beside Jarl Yuliwyf, the
    vault keeper); parties then sat in "Choosing group target" with no timer
    while non-leaders froze. Source 0.103.0 spreads capital arrivals, ends
    target choice after five minutes, walks members back to their leader, and
    sends bots to a merchant at 70 % backpack. Buying consumables and grouped
    town visits remain for task 34. Check pending: capitals after deaths.

35. **Smooth, individual group travel and field backfill.** Aaron on 0.101.0:
    groups walk jerkily in lines; groups should keep refilling (nearby
    guildmates, border-keep LFG, merging small groups). Source 0.102.0: see
    CHANGELOG. Checks pending: group walking, companions following, groups
    growing in the field.

32. **RvR groups form and stay together.** Aaron on 0.100.0: "almost only
    single bots running around". Cause from code and his log: leaders found
    0 free guildmates in 98 % of attempts because ungrouped RvR bots roamed
    and fought at once; roamers waited solo for a full eight; one death sent
    the whole group back to a town; three deaths pulled bots out of their
    group. Source 0.101.0: group seekers LFG at the border keep, viable groups
    of 4 leave, partial deaths are fought through and rezzed, wipes regroup at
    the border keep, deaths in a group no longer end the RvR tour. Research
    notes: scratchpad research-forming-death.md summarized in
    RVR_GROUP_DOCTRINE.md. Checks pending: groups visible in the frontier.

31. **Companion loot: fewer drops, no endlessly full backpacks.** Aaron,
    2026-09-27: one companion gets a reward per kill instead of every
    companion; when a companion's backpack is full, clear room in one go
    (e.g. sell two bags' worth of the worst unlocked items) instead of
    selling one item per drop. Respect [Keep] and manual locks. Also relieves
    the kill lag of bug 42.
    Source 0.99.0: one random eligible companion per owner rolls per kill;
    a full backpack sells up to 16 of the worst earned, unlocked items at once
    ([Keep], starter, player-supplied and legacy items are never sold);
    the owner's money cap still limits the batch. Check pending: drops and
    backpack clearing while levelling.

30. **Camlann guild cohesion and encounter memory.** Source 0.98.0: nearby
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
    checks pending: guild help, guard healing, a guild siege gathering.

29. **Varied RvR roaming.** Source 0.98.0 replaces the fixed west-to-east camp
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
    Checks pending: watch several warbands for distinct routes and lingering.

28. **Human-like RvR combat habits.** Source 0.98.0: autonomous RvR bots pick
    targets by habit (stick time from Patience, caller's target by the
    doctrine's follow chance, otherwise weighted toward healers, mezzers,
    wounded enemies and whoever is on their own healers, with random spread);
    fight appetite from doctrine, leader Aggression and recent history with a
    small chance to dare a bigger group; retreat is a decision (healer dead,
    half down, outnumbered, doctrine bias, RiskTolerance, 15 % stay anyway)
    that runs 2,200 units away for 25-40 s while only self-defense continues.
    Checks pending: fights look human, retreats end and regroup.

27. **RvR group doctrine for autonomous warbands.** Source 0.98.0 derives one
    of 14 doctrines from each warband's real classes (see
    [RVR_GROUP_DOCTRINE.md](RVR_GROUP_DOCTRINE.md)), including imperfect
    pickup groups. Group sizes still come from the existing warband rolls
    (solo, pairs, 3-5, 8); keep raids above 8 remain several 8-member parties
    in one siege event. Checks pending as for 28-29.

26. **Stun → bomb and owner assist for player-led companions.** Source
    0.97.0: companion bombers hold their first PBAoE on an enemy player clump
    up to 2.5 s while a group Healer has an area stun ready; roster
    companions use the PvP assist/defence engagement (owner's target, 30 s
    focus, threat memory). Real-client checks pending: RvR fight with the
    Midgard bomb group.

25. **Companion auto-levelling hits the key spell breakpoints.** Source
    0.97.0: plans carry ordered milestones (Skald Battlesongs 43, Shaman
    Mending 7 and Cave 27, Thane Stormcalling 34 and Shields 42, pac Healer
    Pacification 38, new Healer `support` build with Augmentation 18 and
    Pacification 23); points are saved for the next breakpoint; existing
    automatic companions are retrained for free when they load or level.
    Check pending: Aaron's level-45 companions after loading.

8. **All-class companion builds and caster area damage.** Source 0.96.0
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
   safety exclusions. See [the implementation and acceptance plan](COMPANION_BUILD_AOE_PLAN.md).

24. **Companions rebuff far too often.** Reported by Aaron on 0.92.0. Source
    0.93.0: player-led companions keep only long buffs (5 min or longer, or
    concentration) up out of combat and refresh them in their last minute;
    short buffs are not maintained out of combat; speed only while traveling;
    player-led Skalds sing only the speed song while the group travels.
    Real-client check pending: buff frequency and power use after a pull.

23. **Companion inventory like your own.** Source 0.92.0 adds worn slots
    (positions 1-19) and the backpack (21-60) to the companion inventory
    window with drag-to-equip from the owner's backpack, **[Info]** and a
    slimmer slot view in the Gear tab, and manual choices that give way only to
    clearly better earned loot. Real-client checks pending: vault grid
    columns and ring/wrist pairs side by side, dragging from your backpack
    onto a worn slot, the **[Info]** window, and the swap chat line.

22. **Companions rest with a sitting player.** Source 0.90.0 lets roster
    companions start fast recovery for any missing health, power, or endurance
    while their player-leader sits, and finish to full. Before, they rested only
    below 70% health, 45% power, or 35% endurance and otherwise regenerated at
    standing speed. Travel and combat still interrupt the rest. Real-client
    check that casters refill during a sit is pending.

21. **Remove level and ability prerequisites from realm ability training.** Source
    0.94.0 removes character-level gates and ignores ability-specific
    prerequisites in the trainer; a level 42 character can train Mastery of
    Pain without Augmented Dexterity II. Realm Point costs, maximum ranks, and
    class availability remain; RR5 abilities still unlock at Realm Level 40.
    Installation and real-client checks of low-level training and ability effects
    are pending.

20. **Remove the player logout timer.** Source 0.85.0 completes accepted player logout immediately, including during combat and while moving, without changing the dead, mounted, crafting, or instance restrictions. `/stuck` still uses its safe position on successful logout and clears the request if logout is refused. Installation and real-client checks of combat logout, normal logout, and `/stuck` remain pending.

19. **Enable companion area taunt.** Source 0.84.0 lets persistent Armsman, Hero, and Warrior companions cast their learned Taunting Shout when at least two attackable NPCs in the frontal cone are already fighting the group. The AI skips cones containing idle NPCs or protected mezzes and leaves single-target taunts in place. Installation and real-client checks of cone targeting, threat transfer, cooldown, and mezz/idle safety remain pending.

18. **Give group members buff priority over pets.** Source 0.83.0 selects missing buffs for eligible group members before pet buffs across routine upkeep and fallback selection. Realm buffs choose members before attached pets, while pet-only and spare pet coverage remain available once member needs are met. Installation and real-client checks with mixed player/companion/pet groups remain pending.

17. **Prioritize Shaman endurance buffs for the whole group.** Source 0.82.0 selects the highest learned endurance rank before other routine Shaman buffs, covers nearby group members before pets or other allies, and frees concentration from a lower priority buff when needed for a group member. Installation and real-client checks with Kiri and full autonomous groups remain pending, including coverage after travel and combat.

14. **Companion bombers open a pull with one loose volley.** Implemented in source 0.81.0. Requested so a group's bombs land together for maximum damage without constant re-syncing or cut casts. Result: the first companion bomber in position holds its first bomb for at most 1.2 s until the group's other companion bombers within 1,500 units are in position; then all fire and chain freely for 6 s after each bomb, so a new pull syncs again. Holding never starts while a cast is running; human bombers are not waited for. Unit tests cover the hold, release, chain and stale-wait cases; real-client check of volley timing pending.

13. **Join Friend remembers the connection details and, optionally, the password.** Implemented in source 0.77.0. Requested so guests do not retype everything each session. Result: the form pre-fills host address, host account and guest account from `%LOCALAPPDATA%\OfflineDAoC\join-friend.json`; "Remember my password" stores it encrypted with Windows DPAPI for the current Windows user (unreadable for other users or PCs), and unticking it removes the stored password. Details are saved after the client starts. Launcher tests cover the round trip, the absence of plaintext on disk and damaged files. Real-client check of pre-fill and rejoin pending.

12. **Diagnose recruitment eligibility and repair separated-party coordination at the existing population.** Source 0.75.0 adds exclusive PvP rejection categories, late-recruitment failure categories and throttled travel-hold diagnostics; repairs missing-zone town lookup, distant-combat travel holds, final-camp-ahead crossing waits and single-seat backfilling. No population setting was changed. The matched 0.74.0 observation showed 1,974 zero-eligible attempts versus 22 eligible-but-no-route failures, camp-selection endings down from twelve to three, and full eight-member PvE assemblies down from seven to one. These are uncontrolled observations. Exact exclusion causes and the complete explanation for smaller PvE parties need the new logs; changes await installation and real-client verification. Server and Windows launcher Release builds passed with zero errors; whitespace, conflict-marker, line-ending and version-pin checks passed. Automated tests were skipped under the project rules.

11. **Follow up on the 0.73.0 runtime observation.** Source 0.74.0 keeps initial PvP recruitment open for ten simulated minutes, advertises partial guild parties at new task boundaries without reversing coordinator/allocation locks, removes the eight-waiter invitation cutoff, rotates failed late-recruitment route probes, recalculates missing class roles, and rechecks the planned PvE camp before admitting late recruits. Diagnostics now separate candidate/route counts and capture camp-distance/movement state at group endings. Server and Windows launcher Release builds passed with zero errors; version, conflict-marker and line-ending checks passed. Automated tests were skipped under the project rules. Installation and gameplay acceptance remain pending. Pending: compare full PvP arrivals, healer/tank composition, camp-selection failures and travel expirations after deployment; the thirteen observed travel timeouts are not claimed fixed.

10. **Larger autonomous parties and viable guild assaults.** Source 0.73.0 fills spare seats during a bounded initial recruitment window, permits route-validated cross-region guild PvP invitations, favors full organized warbands, selects class support, and coordinates eligible guildmates only at new task boundaries. New keep assaults require eight members, healing and siege supplies. PvE remote recruitment can fill all seven follower seats. Installation and sustained high-population verification remain pending: compare party-size distribution, actual meetup arrivals, class composition, keep attendance and captures, routing cost, and small-population fallback. Existing tasks, solo-oriented types, guild hostility and eight-member capacity remain authoritative.

9. **Frontier PvE incentives and NPC-held keeps/relics.** Source version 0.72.0 adds +50% base monster XP in Old Frontiers/DF, on top of the configured rate; restores a manifest of 4,851 archived frontier spawns; and adds stationary Frontier Wardens garrisons, lord-unlocked claim stewards, 25 base guard RP and 1,500 base capture RP (30-minute reward cooldown per keep). Autonomous crews explicitly claim for their guild using native permissions and group checks. Fresh/reset worlds use NPC ownership; captured territory persists across restarts and existing guild claims are preserved. Server, launcher and Setup build verification are recorded in [FRONTIER_CAMPAIGN.md](FRONTIER_CAMPAIGN.md). Installation, backed-up spawn migration, world density/navigation, human/crew capture, rewards, restart persistence and relic raids await owner verification.

1. **Private Tailscale co-op between two home installations.** The launcher now
   has a client-only Join Friend shortcut with a Tailscale IPv4 login-port check,
   separate host and guest account names, and remote-session diagnostics. Source
   version: 0.60.0. Still needs host-side private network setup and the real
   two-home login, region transition, group, reconnect, save persistence, and
   reversed-host checks in [TAILSCALE_COOP.md](TAILSCALE_COOP.md).

2. **Autonomous dungeon activity, Darkness Falls and Camlann PvP behavior.** Implemented in source 0.61.0 after the owner's explicit DF implementation approval. DF now has a bounded entrance-stair repair, 1,417 proved spawn destinations and extra weight within the dungeon share. Hunters can patrol dungeons; ordinary pickup groups can choose connected dungeon camps. PvE aggression bypasses, patrol dwell timing and misleading RvR launcher labels are corrected. Source 0.79.0 modestly raises DF destination weight from two to three for eligible XP camps and Hunter PvP patrols, without changing the overall dungeon share. Awaiting installation, all-entrance client traversal and live PvE/PvP balance checks in [CAMLANN_PVP_REVIEW.md](CAMLANN_PVP_REVIEW.md).

3. **Population slider and Danger tooltips.** Expanded in source 0.62.0: all six type labels, sliders and percentage inputs explain the behavior, relevant levels and the effect on new versus existing bots; Danger explains its effect on existing Hunters after a restart. Source 0.70.0 further distinguishes saving new-bot weights from applying a live mix to the saved roster. Installation and launcher hover verification pending.

4. **Focus staff selection for casters.** Source 0.65.0 ranks earned and owned staves by the focus levels covering learned spell lines, including all-lines focus, before general item value. Installation and real-client inspection of caster loadouts and power use remain pending.

5. **Realm Points in Active Population.** Source version 0.67.0 adds a sortable Realm Points column using live points for active autonomous bots and saved points otherwise. Companions without a Realm Points record show a dash. Launcher display and live point updates await installation verification.

6. **Active Groups for large populations.** Source version 0.68.0 replaces the per-group card stack with a sortable, realm-filterable table. Search covers group, bot, class, zone, and task details; selecting a row shows member roles, locations, and live timers. Launcher installation and inspection with a large list (including the reported 82-group case) remain pending.

7. **Bard songs useful during combat.** Source version 0.69.0 keeps grouped Bards with an endurance song on their instrument during combat, preserves the endurance pulse, and lets them use the existing group-support actions. Mana and speed songs resume after combat; solo Bard behavior is unchanged. Installation and real-client verification of endurance upkeep alongside healing and control remain pending.

8. **Apply population-type changes to existing bots live.** Source version 0.70.0 adds a launcher action and server request/status flow for the entire non-retired saved autonomous roster, including offline bots. Player-led companions and temporary helpers are excluded. Offline changes run in bounded batches; active changes wait for safe task or group boundaries. The server saves the requested mix and bot types, and reports pending/applied/failed status. Installation and live acceptance remain pending: verify counts converge, active groups stay intact, repeated application is stable, and settings plus character progress survive restart.

## Finished

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
