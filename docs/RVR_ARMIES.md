# RvR mode: realm armies (package R4b)

Package R4b of [RVR_MODE_PLAN.md](RVR_MODE_PLAN.md); spec
`docs/superpowers/specs/2026-10-24-rvr-mode-design.md` section 5.2; owner
rule 2026-10-25 00:25 (every bot extension carries over into the RvR world,
realm-based). Status: implemented in source, real-client check pending. No
live server run was part of this package.

Everything below applies only to the RvR world (save marker `Realm-1`,
`WorldMode.IsRvr`). The Camlann world (`Camlann-1`) keeps its guild armies,
guild siege slots and guild defence unchanged; the tests run every
event-layer rule in both worlds (`UT_RvrRealmArmies`).

## What a player sees, per realm

- **The realm goes to war, not a guild.** A whole warband of eight of one
  realm opens a keep siege, whatever guilds its members are in (Camlann:
  eight of one guild). Every other warband of that realm that takes the
  siege joins the same army; a warband of another realm never does.
- **Muster at the own portal outpost.** The army gathers at the realm's own
  portal outpost in the keep's frontier, where the frontier porter lands
  ("Odin Alb" for Albion in Midgard, "Hadrian Mid" for Midgard in Albion and
  so on). For a keep in the home frontier it gathers at the nearer of its two
  border keeps (Castle Sauvage or Snowdonia Fortress, Svasud Faste or
  Vindsaul Faste, Druim Ligen or Druim Cain). Only when no walkable spot
  there connects to the leader does it fall back to the forward camp 9,000
  units from the keep, as in Camlann.
- **The call goes out on realm chat.** When the first party sets the muster:
  "Army forming at the Albion portal outpost for Svasud Faste. Get there,
  buff up, we move out together."
- **Muster takes minutes.** The army waits at least two minutes, and up to
  fifteen, for parties, healers and ram carriers, then marches together.
  Each warband first musters on its own leader, as before. The march from a
  far outpost may take a quarter of an hour on foot; a launched party keeps
  its permission for the estimated walk (distance at running speed plus ten
  minutes, at most forty) instead of the Camlann five minutes.
- **Targets: never an own keep.** A realm army never besieges a keep of its
  own realm, claimed by any guild or none. It draws its target among neutral
  (Frontier Wardens) and enemy-held keeps by weight. A keep in the home
  frontier weighs double, so an enemy-held keep at home is the first retake.
  Keeps not chosen lately come first, as in Camlann.
- **Attack plans per realm.** Each realm keeps a plan for about an hour of
  game time, drawn with the open BG odds:

  | Plan | Odds | Own sieges at once | As third realm |
  |---|---|---|---|
  | Single | 40 % | 1 | whole warbands of eight, once the keep fight is on |
  | Double | 30 % | 2 | whole warbands of eight, once the keep fight is on |
  | Opportunist | 30 % | 1, only while no other realm besieges | any group of two or more joins a running keep fight without a roll |

  The server-wide cap of six sieges stays. Log line `RVR_REALM_PLAN`.
- **The whole realm defends.** When an enemy army besieges a keep of a realm,
  every warband of that realm may answer, not only the claiming guild; its
  keeps are never on its attack list, so the siege event itself calls them.
  The deterministic 30 % reserve that holds back responders does not apply to
  the own keep: the home guard comes. A claiming guild still recalls its own
  members as before.
- **Third realm.** The realm that neither attacks nor defends joins only once
  an attacker reached the walls, as its plan says, with at most half the
  attacker cap (64 of 128). On a neutral keep both other realms may contest.
  Notices for the bystanders go to both realms in that case.
- **Live reserve at home.** Part of each realm's warbands stays out of the
  armies as a roaming reserve: the leader's temperament share (roamer 40 %,
  hybrid 25 %, keep warrior 5 %, others 15 %), plus 20 % during an invasion,
  at most 60 %. An invasion means at least four enemies in the realm's home
  frontier: players, their companions (as their owner's realm) and world
  bots, counted every thirty seconds; one passing enemy is not enough.
  During an invasion a reserve warband stays home and hunts a sighted
  invader, else patrols its own frontier. Without one it roams as before,
  so enemy frontiers keep their roaming groups. Only the reserve stays home:
  low-level warbands and warbands without a free siege slot roam as before.
  Any warband hunting enemies prefers invaders at home.
- **Relic sieges** take up to 240 per side (upstream; Camlann 192).
- **Revenge trips are capped.** Grudges are per realm (R4a). At most two
  warbands of a realm ride after the same grudge target at once; a warband
  holds its lease for ten minutes while it hunts, and hunts one grudge at a
  time. A full target is skipped for the next one; log
  `RVR_GRUDGE_GATE ... result=capped`.
- **Human killers are remembered and hunted.** The grudge memory now finds
  online players through the client list; before it searched an empty
  scratch list, so a grudge against a human never led to a revenge trip
  (both worlds).
- **Status lines** read "Gathering the realm army", "Regrouping the realm
  army" and "Realm assault called off" (Camlann: guild army).
- **Open BG.** Units are no longer split by guild when a guild holds the
  open BG keep: the keep's friends are its realm.

## How often grudges are chosen

Every RvR destination pick is counted per realm. Every ten game minutes one
line shows the totals since start:

```
RVR_GRUDGE_SHARE cap=2 realm=Albion picks=… grudge=… capped=… share=0.0xx | realm=Midgard … | realm=Hibernia …
```

`share` is the part of all picks that were revenge trips; `capped` counts
picks where at least one grudge target was skipped because two warbands of
the realm already hunted it. A live run has not measured it yet.

## Tunables

Server properties, category `pvp`, read only in the RvR world, at every
decision (a reloaded property applies from the next one).

| Property | Default | Meaning |
|---|---|---|
| `rvr_army_neutral_keep_weight` | 1.0 | Draw weight of a neutral (Wardens) keep |
| `rvr_army_enemy_keep_weight` | 1.5 | Draw weight of an enemy-held keep |
| `rvr_army_home_frontier_weight` | 2.0 | Multiplier for a keep in the army's home frontier |
| `rvr_army_reserve_invader_percent` | 20 | Extra reserve share during an invasion |
| `rvr_army_invader_min` | 4 | Enemies in the home frontier that count as an invasion |
| `rvr_army_plan_minutes` | 60 | Average game minutes a realm keeps its plan (0.5x-1.5x) |
| `rvr_grudge_hunters_per_target` | 2 | Warbands of a realm per grudge target; 0 = no revenge trips |

Fixed in code: relic cap 240, third realm half the cap, reserve at most
60 %, muster at least 7,000 units from the keep, census every 30 s, grudge
lease 10 min, plan odds 40 / 30 / 30.

## Log lines

| Line | When |
|---|---|
| `RVR_REALM_PLAN realm= plan= previous= minutes=` | A realm draws a new attack plan |
| `RVR_REALM_SIEGE_OPENED target= realm= defender= plan= opener= running=` | A realm opens a siege |
| `RVR_REALM_ARMY_MUSTER target= realm= muster= camp= keepDistance=` | The army's muster is set |
| `RVR_GUILD_ARMY ... realm="Albion" ...` | Army gather, launch, reinforce, abandon (Camlann: `guild="…"`) |
| `RVR_GRUDGE_GATE ... result=capped` | A revenge trip turned away |
| `RVR_GRUDGE_SHARE` | Grudge share per realm, every ten game minutes |

## Code

- `AutonomousRealmArmyPolicy.cs`: pure rules and the tunables.
- `AutonomousRealmArmyState.cs`: realm plans, invader census, grudge leases
  and counts.
- `AutonomousRvrEventLayer.RealmArmy.cs`: the event-layer helpers; the realm
  branches in `AutonomousRvrEventLayer.cs`, `.GuildArmy.cs`, `.SiegeCap.cs`
  and `.Defense.cs` call them behind `WorldMode.IsRvr`.
- Controller: realm muster probe (`TryRealmArmyMuster`), live reserve and
  the grudge cap in `ChooseRvrDestination`, pick counting.
- Not touched: Stefan's movement files (`AutonomousBotGroupCoordinator`,
  `AutonomousRvrTravel` and the others listed in the spec).

## Open for R4c and later

- **Upstream siege flow (R4c):** march threshold 108 / 170 or 85 % of the
  assigned, arrival quorum 60 %, the siege start chance schedule (25 %
  rising, 4 %, 1.2 %), defence muster 6 + 4 minutes and the 80 % entry, the
  third realm's 50 % after five minutes, retake after a capture, army push,
  army detour, siege resurrection, ram placement. R4b keeps the fork's army
  readiness (2-15 minute gather, keep-level quorum).
- **Realm keep recall:** a claimed keep still recalls its guild member by
  member (Camlann flow); an unclaimed realm keep relies on the event-layer
  defence. A realm-wide recall of whole warbands with upstream timings
  belongs to R4c.
- **Neutral keep sides:** both contesting realms share the third-realm
  bucket, so attendance per realm on a neutral keep is counted under one
  realm in the dashboard.
- **Long marches:** a realm army of several parties walking a whole frontier
  can trip the existing column-split rule (parties more than 2,500 apart for
  the no-progress window). Watch `RVR_GUILD_ARMY action=abandon` in a live
  run.
- **Measure live:** a 10x run should show `RVR_REALM_ARMY_MUSTER` at the
  outposts, `RVR_GUILD_ARMY realm=` launches, sieges of all three realms,
  and the `RVR_GRUDGE_SHARE` lines.
