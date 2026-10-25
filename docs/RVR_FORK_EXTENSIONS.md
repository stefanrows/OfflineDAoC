# RvR world: fork bot extensions it does not run

Status 2026-10-25 (0.345.0). Owner decision 2026-10-25 16:10: the RvR world
(`Realm-1`) runs the original upstream 0.38c RvR bots; fork code only where
it adds real value, and only on the owner's explicit yes
([RVR_MODE_PLAN.md](RVR_MODE_PLAN.md)). Port sessions S1-S3 and the R6 fixes
therefore switched the fork's own RvR bot extensions off in Realm-1
([RVR_PORT_MAP.md](RVR_PORT_MAP.md) sections 6, 7, "S3 done", "R6 fixes").
Camlann still runs all of them.

This is the list of what the RvR world is **missing compared with Camlann**.
The owner picks entries one at a time; each picked entry is built properly
into the upstream flow, then checked live before the next one. Nothing here
is planned work until it is picked.

**How they are off today.** None of them has a switch of its own. One world
gate turns them off together: `AutonomousRvrWaves.ActiveFor` (false for
Realm-1 world bots; battleground participants in a battleground region or
assigned to one keep the fork's crews), plus a handful of `WorldMode.IsRvr`
checks at the entry points (named in the table). Most of the waves hang off
one predicate, `AutonomousRvrDoctrineRuntime.Applies`, so integrating one
wave means giving it its own condition first. The code of every extension
stays and is still tested through Camlann.

Risk is the risk for the upstream siege flow: **low** = roaming bots only;
**medium** = can keep bots from the muster or delay them; **high** = touches
the march, the column or the battle. Effort: **small** (a day's session or
less, the code exists), **medium** (a session plus a new condition and
tests), **large** (several sessions or a design change).

## Roaming groups (the "livelier RvR" waves)

| Extension | Player saw in Camlann | RvR world does instead (upstream) | Gated at | Proper integration into the upstream flow | Risk | Effort |
|---|---|---|---|---|---|---|
| Group doctrine (14 doctrines from the group's classes: stealth pack, small-man, bomb group, assist train ...; fight appetite) | Groups play to their setup, pick fights by size, avoid bigger groups ([RVR_GROUP_DOCTRINE.md](RVR_GROUP_DOCTRINE.md)) | Warbands engage what the frontier threat scan finds; no composition doctrine | `AutonomousRvrDoctrineRuntime.Applies` → `AutonomousRvrWaves.ActiveFor` | Derive the doctrine only for warbands that are not an event force (`AutonomousRvrEventLayer.IsEventForce` false); prerequisite for the next six rows | medium | medium |
| Observe before engaging | A group holds at about 2,200 units, watches a fight, adds after three enemies are down | Engages on sight | doctrine predicate; `AutonomousRvrObserve`, `AutonomousWorldBotController.RvrObserve.cs` | Hook into `TryEngageFrontierThreatRealm` for roaming warbands only; never in muster, march or battle | low | medium |
| Retreat and after-fight rest | A group runs from a lost fight, sits to heal and regain power off the road | No retreat; bots recover by the normal rules | doctrine predicate; `AutonomousRvrGroupPause`, `AutonomousWorldBotController.RvrPause.cs` | Roaming warbands only; a muster call or rally order ends the rest at once | medium | small |
| Stealther loop | Infiltrators, Shadowblades, Nightshades lurk beside roads, open on stragglers, re-hide | Stealthers roam and fight like the others | doctrine predicate; `AutonomousRvrStealthLoop.Applies` | Solo and stealth-pack bots that hold no siege slot; leaves armies untouched | low | small |
| Speed habits (speed class recruited, song twisting, group moves as one body, sprint to close gaps) | Groups travel at speed behind their Bard/Skald/Minstrel; the leader waits for stragglers | Army pace from the march column (regroup, push, detour); upstream AutonomousGroupPace not ported yet (S4) | doctrine predicate; `AutonomousRvrSpeed`, `AutonomousGroupMotion` caps | Speed song while travelling fits everywhere; the leader waits and motion caps stay off inside the march column | high | medium |
| Support offense and healer area stun | Smite Clerics and nature Druids nuke when a second healer covers; pac Healers and cave Shamans mez first | Healers heal, and raise the army's fallen in a siege | doctrine predicate; `BotBrain.SupportOffense.cs` | Combat rule for roaming fights first; in battle only below the siege resurrection; fix the `_supportLastTurn` MinValue overflow first (S4) | medium | small |
| Combat habits and assist (caller weighting, 1-2 s assist delay, missed calls, no DoT on a mezzed enemy) | Bots follow the caller imperfectly, prefer healers and casters | Upstream target choice | doctrine predicate; `AutonomousRvrCombatHabits`, `CompanionAddControl` | Target weighting is battle-wide; build behind the doctrine row and measure kills per siege before and after | high | medium |
| PvP opportunity hunts | Levelling and solo bots attack enemies they meet nearby | Bots engage through the frontier threat scan only | `TryEngageOpenWorldPvpOpportunity` (`AutonomousRvrWaves.ActiveFor`) | Only bots without an RvR event assignment and outside muster | low | small |
| LFG wait at the portal keep | Healers, casters and tanks wait with LFG at their border keep until a leader picks them up | The coordinator forms warbands; solo siege recruits merge into full parties | the fork RvR turn (`ExecuteRvr`, not run in Realm-1; `AutonomousRvrLfg`) | Waiting step before the coordinator forms a warband; never holds a bot the event layer has drafted | medium | medium |
| Hub muster for fresh groups | A new group waits a few minutes at the portal keep for stragglers, then leaves together | The warband meets at its rendezvous and leaves when everyone is there; sieges muster at the portal outpost | `IsRvrHubMuster` (`AutonomousRvrWaves.Active`) | Non-siege warbands only; a siege muster always wins | medium | small |
| Danger memory and repeated-loss avoidance | Groups avoid where they died for about an hour, or come back bigger | No memory | `GameBot` death record (`AutonomousRvrWaves.ActiveFor`); fork planner | A weight on upstream's `rvr-roam-` anchors in `ChooseRvrDestinationRealm`; never on siege targets | low | small |
| Route variants and hub fan | Groups leave the hub by different sides and take flank or cover routes | Direct legs and the proven road | `VaryKeepDeparture` (KeepTravel, `AutonomousRvrWaves.ActiveFor`); fork planner | Roaming legs only; keep travel and the march column stay on the proven road | medium | small |
| Mob bends | Warbands step around big monster camps on the road | Route threat awareness (upstream, on) for PvE leaders; the column fights through | KeepTravel `realmTravel` branch (`AutonomousRvrMobAvoidance.TryWalkBend`) | Overlaps upstream's route threat awareness; only for roaming legs if at all | high | medium |
| Roam tastes, hotspots and fight heat | Groups drift to Emain Macha's fields, Hadrian's Wall and recent fights | Upstream roam anchors at keeps, clearings, enemy bots and relic carriers | `ChooseRvrDestinationTimed` (`WorldMode.IsRvr` → `ChooseRvrDestinationRealm`) | Extra weights on upstream's roam anchor choice | low | small |

## Guilds, keeps and armies

| Extension | Player saw in Camlann | RvR world does instead (upstream) | Gated at | Proper integration into the upstream flow | Risk | Effort |
|---|---|---|---|---|---|---|
| Grudge and revenge trips (KOS call) | A guild hunts the guild or player that killed its members, with a KOS line in chat | One retake after a player capture, nothing else; grudges are still recorded per realm | `AutonomousWorldBotController.cs` revenge target (`WorldMode.IsRvr ? null`) | A roaming destination for warbands outside an event force, at most two warbands per target (R4b cap exists) | medium | small |
| Grey-con sparing | Bots spare players far below their level | Any enemy is a target | `AutonomousRvrTargetPolicy` (`WorldMode.IsRvr`) | One check in the target policy, off for siege battles | low | small |
| Bot keep claims | Bot guilds claim captured keeps; keeps wait at level 1 | No bot claims; a capture leaves the keep at level 4, a player guild claim makes it 5 | `ExecuteRvr` claim point (`WorldMode.IsRvr ? null`) | A bot guild of the realm claims after a capture when no player guild claims within a while; keep levels stay upstream | low | small |
| Guild keep defense | Guild bots drop their task to defend their guild's keep | The event layer's realm defense rally answers | `AutonomousGuildKeepDefense.Applies` | Would duplicate the realm defense; at most the claiming guild's bots join that defense rally first | medium | medium |
| March restraint | A marching army ignores fights far from its keep and drops pursuits | The column fights on the way; a stalled leader pushes after 4 minutes | `AutonomousSiegeMarch.RestrainsMarch` | Conflicts with upstream's push and regroup rules; not recommended | high | small |
| Guild armies, muster and march rules, siege cap, attack plans, home reserve (fork event layer; R4b realm armies) | Guild armies gather and march by the fork's bug-75 rules, a siege cap, per-army plans | One siege roll a minute, one attack per realm, muster at the portal outpost, fixed 30 % roaming reserve | facade `AutonomousRvrEventLayer` → `CamlannRvrEventLayer` only | Replaces upstream's core; this is a redesign, not an integration | high | large |
| Fork siege doctrine (rams bought at the hub, casters ride the ram, melee on the gate, lord last) | Warbands work a gate as a 2003 group | Synthetic engines on the spot, rams at the gate then the keep door, artillery rings | `AutonomousWorldBotController.Siege.cs` vs `RealmSiege.cs` (world branch) | "Lord last" is upstream's lord shield (S4); the rest replaces the upstream siege floor | high | medium |

## Population and battlegrounds

| Extension | Player saw in Camlann | RvR world does instead (upstream) | Gated at | Proper integration into the upstream flow | Risk | Effort |
|---|---|---|---|---|---|---|
| Bracket battleground campaign | Bots of 15-35 tour Abermenai, Thidranki, Murdaigean and Caledonia | No bracket tours (upstream's "Battlegrounds %" goal defaults to 0 %); the open BG crew in Thidranki stays | `AutonomousBattlegroundParticipation.BracketCampaignRuns` | Port upstream's opt-in battleground goal (S4, owner decision) instead of the fork campaign | low | medium |
| RvR below level 20 | Low-level bots roam the frontier for PvP | No RvR task below level 20 (upstream goal table) | `AutonomousActivityScheduler.MayTakeRvr` | Covered by the battleground goal above; no separate integration | low | small |

Not a difference: the fork's raid calendar is off in both worlds
(`RealmRaidRecruitmentPolicy.ScheduledRaidsEnabled`). Forced raids differ by
design: Camlann 45/90 minutes, RvR no time limit with STOP EVENT.

Not on this list because they run in the RvR world: companions, Trials of
Atlantis and Master Levels, Realm Exchange, guild cohesion (guildmates help
each other), the realm login balancer and realm guild names, Stefan's
movement port, the road-graph fallback, the fork's frontier transport (kept
and to be adapted to RvR, owner decision 2026-10-25 22:20), and the PvP
release immunity (kept, same decision). The R3b relic rule also still runs
in Realm-1; the owner decided to replace it there with upstream's (S4).

**Task mix and warband composition** (owner decision 2026-10-25 22:20: the
original by default). Today Realm-1 still uses the fork's player types to
pick tasks (shares differ from upstream's flat 20/40 %) and the fork's
warband composition (healer/tank/speed weights, backfill, remote meetup).
Bringing these back to upstream is an S4 question for the owner; fork pieces
stay only where the owner picks them from this list.

## Where to start

A cautious order, one at a time with a live check of the sieges after each:
low and small first (danger memory, roam tastes, opportunity hunts, grey-con
sparing, bot keep claims, stealther loop), then medium (hub muster, rest,
grudge trips, LFG), the doctrine core last. Leave out the high-risk rows
that conflict with upstream (march restraint, guild armies, mob bends) unless
a live run shows a gap.
