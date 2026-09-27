# Known Bugs

Track confirmed, unresolved bugs here. Include the affected version, steps to reproduce, expected and actual behavior, impact, and any workaround. Fixes are handled in a separate task unless noted below. When a bug is fixed and its required verification is complete, move it out of its current section into **Finished** immediately, recording a brief resolution and version. Do not leave completed items in **Open**. If a source fix still awaits installation or real-client verification, keep it under **Fixed in source; installation verification pending** until that check is complete.

Tasks, feature requests, and ideas belong in [TASKS.md](TASKS.md).

## Open

## Fixed in source; installation verification pending

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

33. **Autonomous bots cross Darkness Falls without staying to work there.** Observed from inside DF with Astreunhild, Dagunildveig and Egiliunulf passing through; the logs do not record their exact in-dungeon goals, so individual routes remain unproven. Source audit found the shared region search could use DF as an intermediate shortcut to unrelated destinations. Source 0.79.0 excludes that transit path while retaining explicit DF destinations and egress from DF. Installation and real-client observation of these bots, DF camp arrivals, and cross-realm travel remain pending.

34. **Companion bombers stay at range instead of bombing PvE pulls.** Reported on 0.76.0 in a player group with Spiritmaster companions: bombers rarely moved into the pack and mostly cast from a distance. Cause: the PvE bomb pull counted only the per-member focus set (each member's current NPC target). Healers target party members and damage dealers assist one target, so the set rarely reached the three targets that Auto requires; the bomb then counted as not ready, so no approach started. Source fix 0.78.0: NPCs inside the bomb radius that are already fighting a group member or a member's pet also count; idle spawns still do not. Unit test covers the engaged-add filter; real-client check of approach and bomb frequency pending.

32. **A multiplayer /pull transfers companions to the wrong player.** With two real players in one group, each with three summoned companions, either player can use /pull and all nearby companions follow that player after combat. Expected: each companion keeps following its summoner. Actual: the pulling player becomes their leader until /companions reset. Source 0.76.0 limits the pull roster and wait gate to the issuing player's assigned companions. Installation and real-client two-player verification pending.

31. **Melee companions swap shield and two-hander every tick, stalling the server.** Reproduce with a persistent Thane or Skald companion carrying both a shield and a two-handed weapon. Expected: the better setup stays equipped. Actual: `TryGetEquipmentUpgrade` compared a candidate only against its own target slot; with a two-hander worn the shield slot is empty and vice versa, so each counted as an upgrade and `TryApplyPendingPersistentCompanionUpgrade` swapped them on every think with an atomic SQLite save. Observed on 0.73.0 with two Thanes and a Skald (level 30): about 16,000 "Long NpcService.Tick" warnings, average 80 ms and up to 1.4 s, felt in the client as periodic freezes. Source fix: the upgrade check also subtracts the weapons the move displaces (two-hander versus right and left hand). Local installation removed the stall pattern; sustained real-client observation remains pending.

28. **Rendezvous recovery throws while an actor has no current zone.** Installed 0.74.0 logged a `NullReferenceException` through `GameObject.CurrentAreas`, town detection and rendezvous reselection at 19:52:49 on 2026-09-26. The getter dereferenced a missing zone, interrupting that bot's goal processing. Source 0.75.0 uses a null-safe captured-zone area lookup in town detection/naming. Installation and zone-transition/recovery observation pending.

29. **Separated PvE parties can wait on combat or arrivals in another region.** The 0.74.0 observation caught safe members paused by a distant party member's combat; source also only recognized arrivals immediately across the leader's next region edge, ignoring members already in the final camp region. Source 0.75.0 scopes ordinary combat/recovery holds to nearby living members and permits leaders to advance toward members at the destination region. Actual routes, combat defense and existing deadlines remain required. Installation, multi-edge travel and combat/recovery verification pending; other travel failures are not claimed fixed.

30. **One free population seat cannot fill an assembling PvE party.** Source audit after the 0.74.0 party-size decline found a minimum-two free-seat return before the backfill pass. Source 0.75.0 permits that pass with one free seat and applies the two-seat gate only to new-party creation. This is one confirmed source defect, not an established explanation for the whole observed decline. Live roster-size verification pending.

25. **Initial guild recruitment loses invitations and late PvE recruitment skips camp validation.** Source audit following the installed 0.73.0 observation found that invitations ignored existing partial parties and stopped when eight peers were waiting; late PvE joins checked the rendezvous but not whether the planned camp remained valid. Source 0.74.0 advertises bounded initial-party vacancies at safe task boundaries, removes the waiter cutoff, and repeats the camp usability check before a late join. Failed probes yield briefly to other candidates, and missing roles are recalculated after each join. Installation and sustained full-party/route verification remain pending; these source defects do not establish the cause of every observed travel failure.

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
21. **Server freezes when simultaneous effect changes deadlock.** Effect transitions now release their state lock before processing the owner's effect list, and an expiring same-spell effect can be replaced without waiting on its state lock. A bounded concurrency regression covers the expiration/replacement cycle. Source fix: 0.71.0; installation and sustained real-client server verification pending.

## Finished

38. **Server unit tests fail in bulk depending on filter and order.** On 0.80.0, `dotnet test source/server/Tests/Tests.csproj -c Release --filter "FullyQualifiedName~Companion|FullyQualifiedName~Bomb|FullyQualifiedName~BotBrain|FullyQualifiedName~Style|FullyQualifiedName~Taunt|FullyQualifiedName~BotCombat|FullyQualifiedName~Tank"` fails 133 tests with `TypeInitializationException: The type initializer for 'DOL.GS.GameObject' threw` (inner NullReferenceException). Other filters pass or fail intermittently (19 tests), and `UT_BotWeaponStats` alone fails 4. Likely a test touches `GameObject` before any `EpicTestServerScope` exists, which poisons the type for the whole run. Impact: suite results depend on selection and order; product code unaffected. Not yet investigated.

33. **Launcher BotGoalsSettings tests cannot construct the control.** On 0.76.0, `BotGoalsSettingsTests` fails in SetUp with `MissingMethodException: Constructor on type 'OfflineDaoc.Launcher.BotGoalsSettingsControl' not found` for LegacyFileExplainsMappingBeforeRewriting, MeasuredRecommendationAndPanelRenderWithoutLaunchingServer, MixTotalAndServerStateGateSaving and PresetAndWorldShapeSaveAndUndo. Reproduce with `tools/dev/winnet.sh test source/tools/OfflineDaoc.Launcher.Tests/OfflineDaoc.Launcher.Tests.csproj -c Release`. Expected: the fixture creates the control with its current constructor. Impact: the population-settings tests do not run; the launcher itself is unaffected. Likely the test still reflects an older constructor signature; not yet investigated.

26. **World-speed status publication intermittently fails.** Installed 0.73.0 logged three `UnauthorizedAccessException` failures while replacing the status file during the 2026-09-26 observation. Later snapshots resumed and simulation kept progressing. Expected: continuous dashboard status updates; actual: transient publication failures. Root cause and gameplay impact are unconfirmed; concurrent diagnostic readers are a possible confound. Workaround: wait for the next status update. No fix included in 0.74.0.

27. **Missing NPC template 5232525.** Installed 0.73.0 logged one missing-template error during the 2026-09-26 autonomous session. Expected: the requested NPC template resolves; actual: lookup failed. The spawning caller and gameplay impact remain unidentified; no template was guessed or added. Reproduction beyond the observed log event and workaround are unknown.

5. **Some helmets render oversized or glitched.** Expected: equipped helmets display at the correct scale and appearance. Actual: some helmets appear oversized and visually buggy; the affected items and viewing context have not been recorded. Affected version and workaround were not provided. Source audit 2026-09-26: helmet packets send the item's model and normal texture/effect fields without a demonstrated scale or field error. No item-specific repair is justified until an affected item ID, wearer race/gender, client version, and screenshot or packet capture identify the failing model.


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
