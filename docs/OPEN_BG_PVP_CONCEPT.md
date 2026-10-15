# Open BG: PvP concept (many groups out, several keep raids)

Concept 2026-10-15, owner request: on the BG map with three starting keeps many
groups should roam and some should try to raid the keep. Nothing here is built.
M = measured (server log run B: 7 cycles, about 230 active minutes at 1x, and
run C from 18:10; save copy; code), A = assumption or design.
Companions: `docs/OPEN_BG_GROUP_PRESETS.md` (archetypes),
`docs/superpowers/specs/2026-10-15-open-bg-window-design.md` (window).

Parties: group, guild and battlegroup are the only friends (owner decision, no
realm alliance, players FFA, portal-keep guards passive). The realm only decides
the landing place and bridge.

## 1. Why it looks thin today (all M)

1. The crew is set to 1 eight-man + 2 small groups + 4 solos per realm, 54 in
   total (cap 72 unused). Present at 1x: 37-51. **Only 3 eight-mans on the
   whole map.**
2. In Quiet the first group of each realm gets `bridge_watch`, the rest `roam`,
   all 12 solos `lurk` (roles in run B: lurk 275, roam 93, bridge_watch 33).
3. Roam points lie on rings of 2,500 / 3,500 / 8,000 / 11,000 around the keep;
   16 of 35 field points are within 3,500 of it. Roamers circle the keep
   island, not the map.
4. Combat in the BG is one rule: every 1.5 s look for enemies within 2,400 and
   attack. The doctrine behaviour of the world (observe, third-party, break
   off, stealth loop, group pause) is not used by the BG driver.
5. Result is a grinder at the same rate in every phase: 1,246 crew deaths in
   run B (Quiet 5.7/min, Push 5.3, Keep fight 5.2, Retreat 5.5). A bot dies
   every 308 s on average and then runs 14,700 (Albion) to about 25,000
   (Hibernia/Midgard) units back.
6. One raid per cycle, chosen by the director: a third of the crew, at least 8
   (8-21 bots in 2-10 units measured). No second attacker.
7. Other groups keep at least 3,000 from a keep fight (since 0.243.0): a third
   party falling on the raid, as in 2003 reports, is switched off. Only 137 of
   411 keep-fight deaths (33%) fell within 3,400 of the keep.
8. The keep falls early, already in the push: 18 claims in 7 cycles, 10 of them
   within 3 minutes of the previous one (the allied claim flip). `TryClaim`
   runs for every leader with claim rank, whatever the role.
9. A claim tears mixed-guild units apart: 115 splits, then 19 reliefs as
   `below_minimum`. Guard kills: 0 of 1,246 deaths.
10. Forming does not fail for lack of candidates: 598-610 bots from level 40
    per realm, 451-490 on an RvR target, 85-119 stealthers, 85-150 healers.
    507 `OPEN_BG_FORM_FAILED` lines, mostly `in_group:36-38`; only bots
    without a group are recruited, and composition is only "1-2 healers first"
    (`OpenBattlegroundRoster.cs:917`).

## 2. Target picture (A, after 2003-2005 play)

With cap 72 (24 per realm, or the same number cross-realm): **6 eight-mans, 3
small groups, 12 solos**, instead of 3 eight-mans.

| Unit | Count | Where | Behaviour |
| --- | --- | --- | --- |
| Roamer 8 (Standard 8) | 1 per realm | loop of 2-4 field points on **both** river sides, also before the enemy bridge | fights groups up to its size; breaks off when the healer is dead, a second group arrives or it stands 3:1; regroups 1-3 min; third-party from 2,000-4,000 |
| Raider 8 | 1 per realm | core of the raid (push, keep fight), otherwise as roamer | tanks and healers for gate and lord, casters for the walls |
| Gank / stealth group (3-4) | 1 per realm | paths portal keep to bridge, edges of running fights | hunts solos, stragglers, cut-off healers; avoids eight-mans; burst 10-15 s then back |
| Solo stealther | 2 per realm | on and under the bridges (lurk as today) | long waits, last man of a column |
| Solo roamer (visible) | 2 per realm | roads portal keep to bridge | evades once a second enemy shows |

Cycle (30 game minutes as an example, shares as today):

- **Quiet 30%:** one group per realm watches a bridge, about 30% of the time the
  *neighbour's* bridge (bridge camping). Roamers loop, gank groups hunt, solos
  lurk.
- **Push 20%:** raid A forms around a lead unit that takes 1-2 partners of any
  realm or guild into a battlegroup; meeting point is its own bridge, then the
  gate. About 35% of cycles a **raid B** from other units joins 2-5 minutes
  later as its own battlegroup and the enemy of A.
- **Keep fight 35%:** gate, inner gate and lord. Defenders are the owning
  guild's units plus guards. Instead of the 3,000 bystander lock a **vulture
  rule**: at most one foreign group per realm approaches to 2,000-4,000 and
  only attacks once a gate falls or the raid is below 50% alive. That makes the
  three-way fight for the keep.
- **Retreat 15%:** the winner claims and holds, partners hold the approaches;
  losers fall back to the bridge, regroup, become roamers. Reliefs preferably
  happen here.

## 3. Levers

Existing (M): properties `open_bg_max_bots` (8-120), `groups8` (0-4),
`groups_small` (0-6), `solos` (0-12), `cycle_min/max_minutes`, `player_drift`
(`OpenBattlegroundProperties.cs:16-38`); director constants (phase shares
30/20/35/15 +-5%, `RaidCrewDivisor` 3, `RaidMinimumBots` 8, point rest 30-90 s,
lurk rest 120-300 s, `FieldRadii`); roles (`OpenBattlegroundRoles.cs`:
`BystanderKeepDistance` 3,000, approach 3,400-4,400); driver (`OpponentRange`
2,400, `AssaultGroupMinimum` 4); roster (`Group8Minimum` 6, relief every 20-30
min, healer priority).

New (A):

1. **Crew mode switch** (owner idea 2026-10-15): *per realm* (today, default)
   or *cross-realm*. Presets are role slots with class lists of all realms; per
   realm the roster filters the slots to the own realm, cross-realm it takes
   all three. One catalog serves both.
2. `open_bg_presets`: mix per realm or in total, e.g.
   `std8:1,raid8:1,gank4:1,stealth_solo:2,roam_solo:2`; when set it replaces
   `groups8/small/solos`.
3. Presets as input to `TryPickGroup` (see the presets document). Option: take
   over intact world groups whose `RvrDoctrine.Derive` fits the preset, which
   avoids the `in_group` rejections.
4. `open_bg_raids_max` (1-2) and `open_bg_second_raid_chance` (0-100).
5. The vulture rule replaces `BystanderKeepDistance`, parameters
   `open_bg_vulture_min/max`.
6. Claim right only for the raid's lead-guild leader who killed the lord (or the
   defenders); partners do not attack an ally's lord (fix for the flip).
7. The BG driver takes doctrine behaviour: observe and third-party, break off on
   healer death or odds, group pause, stealth for lurkers.
8. Defenders as their own unit kind: the owning guild fields a garrison unit
   from the pool instead of splitting raid units.

## 4. How to measure (M now, A target, at 1x)

| Figure | Now | Target | Measured by |
| --- | --- | --- | --- |
| Groups out (field or bridge, not resting or walking back) | not logged | at least 7 of 9 groups, 70% of bots | new `OPEN_BG_FIELD` every 60 s |
| Encounters per minute (two hostile units under 2,000, first damage) | not logged | 1-3, mixed pairings | new `OPEN_BG_ENCOUNTER kind_a= kind_b=` |
| Deaths per minute by phase | 5.2-5.7 everywhere | Quiet about 3, keep fight about 7 | `AUTONOMOUS_BG_DEATH` plus phase |
| Keep attacks per cycle | 1 raid, gate arrival not logged | at least 1 at the gate; second raid in about 1/3 of cycles | new `OPEN_BG_RAID_AT_GATE`, gate and lord fall |
| Claims | 18 in 7 cycles, 10 within 3 min | 1 per 1-2 cycles (about 20 min, owner), 0 flips between allies | `KEEP_CLAIMED keep=11` |
| Keep-fight deaths near the keep | 33% within 3,400 | at least 50% | position bins |
| Guard kills | 0 | above 0 | `killer_kind=guard` |

## 5. Impact and risks

- **Bot load (M):** think 0.15 ms on average, path queries 6-11 s per 60 s
  window server-wide, `OPEN_BG_TICK_SLOW` 5 times in about 4 h (at most 76
  ms). 54 to 72 crew bots is small next to about 1,800 world bots. Deaths drive
  the cost (long return routes), so less grinder lowers load.
- **World (A):** 72 bots are about 5% of the RvR bots. Taking over intact world
  groups removes them from guild armies and keep defense (Midgard pool, TASKS
  116); a per-guild limit is needed.
- **Players (A):** more eight-mans at levels 40-50 means more pressure on players
  at 30-39 (protection applies below 30). Two raids plus vulture can break the
  agreed keep pace of about 20 minutes, so `raids_max` must be switchable.
- **Owner decisions:** compatible with no realm alliance, pace as is, passive
  portal-keep guards. The vulture rule partly reverses the 0.243.0 "bystander
  3,000" decision and needs the owner's OK.

Not checked: why the 18 guards never kill (aggro, owner change or kill
attribution); who kills the lord, whether gates really fall, whether posterns
are used (no log lines); whether the world doctrine modules run in the BG driver
without rework; the age of the save copy; how it feels in the real client.

## 6. Build order (A)

1. Metric logs (`OPEN_BG_FIELD`, `OPEN_BG_ENCOUNTER`, `OPEN_BG_RAID_AT_GATE`).
2. Claim right and flip fix.
3. Presets, crew mode switch and cap 72.
4. Doctrine behaviour in the BG driver (break off, third-party).
5. Field points on both river sides, bridge camping.
6. Second raid and vulture rule (after the owner's OK).

Steps 1-2 are the open bug strand (allied claim flip) and do not depend on the
window.
