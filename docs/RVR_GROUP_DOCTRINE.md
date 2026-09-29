# RvR group doctrine for autonomous warbands

Tasks 27-30 in [TASKS.md](TASKS.md). This is the behaviour model the
autonomous RvR bots follow: how a real 2003 group *understood* its own setup,
not a script of perfect reactions. Era: patch 1.65, Old Frontiers, Shrouded
Isles in, pre-Trials of Atlantis. The server is Camlann-style: anyone outside
your group, guild or battlegroup is hostile, and guilds own keeps.

## Principles

1. **A group knows what it is.** Its doctrine comes from the classes it
   actually has, not from what it wished it had. A pickup group with one
   healer and speed 4 still roams; it just picks fights and places differently.
2. **Role before reaction.** A healer heals and controls, a tank peels and
   trains, a caster nukes the called target, a stealther hunts stragglers. The
   doctrine shapes habits; it does not make every bot optimal.
3. **Human imperfection.** Bots stick to a target a while, sometimes switch or
   scatter, sometimes ignore the call, sometimes take a fight they should have
   avoided or stay in one fight too long. Personality traits (Aggression,
   RiskTolerance, Patience, Sociability, 15-85) decide how much.
4. **A fallback everyone knows.** When the doctrine does not say, the group
   uses the default doctrine: roam the roads between hotspots, fight groups no
   bigger than yours, kill the healer first if you can, back off when the
   healer dies.

## Doctrines

Derived from the members of an autonomous RvR group (see
`AutonomousRvrDoctrine`). One line each: composition cue, idea, habits.

| # | Doctrine | Composition cue | Idea and habits |
|---|---|---|---|
| 1 | Solo assassin | 1 stealther | Hunts stragglers at portals, milegates and roads; lingers long; flees a second enemy. |
| 2 | Stealth pack | 2+ stealthers, no healer | Synchronized opener on one caster or healer; scatters when a group turns. |
| 3 | Caster duo | 2 members, a healer or controller and a caster | One controls, one nukes; stays near keeps; retreats at the first group. |
| 4 | Small-man | 3-5 with a healer | Fast, hunts solos, duos and other small-men; avoids full groups; retreats at 2:1. |
| 5 | Assist train | 6+ with healer, CC and melee | One caller (the leader), everyone assists; kill healer or mezzer first; column in travel. |
| 6 | Bomb group | a PBAoE caster and an area-stun Healer | Stun then bomb; loves chokepoints and clumped enemies; cannot fight a long 8v8, leaves early. |
| 7 | Melee train | speed plus mostly melee | Speed in, mass melee assist on casters at the edge; zerg-surfs; loses to kiting. |
| 8 | Pet casters | mostly pet classes | Slow and defensive; best at keeps and doors. |
| 9 | Pickup group | 3+ with at most one healer, weak CC | "Swims along": fights at chokes and doors, follows bigger fights, retreats early. |
| 10 | Zerg party / keep raid | an 8 committed to a siege event | Column on the roads to the objective; stacks at the door; siege roles as before. |
| 11 | Keep defense | defenders of their guild keep | Walls and door; the pac or bombers at the inner door. |
| 12 | Archer group | 2+ archers | Ridge and tower tops; volley casters and healers. |
| 13 | Gank squad | 3-5 incl. stealthers on a Hunter leader | Travel routes and levelling-zone edges; lower-level and solo targets. |
| 14 | Lone roamer | 1 non-stealther | Default doctrine alone: careful, picks only fair or better fights. |

What groups did without the perfect setup: no pac Healer means mez or no
opener and fighting at chokes; speed 4 means staying near keeps and milegates;
one healer means picking on solos and duos and retreating at the first add.

## What the doctrine drives

- **Target habits** (`AutonomousRvrCombatHabits`): soft priority for enemy
  healers and mezzers (a weight, never a rule), per-bot stickiness from
  Patience, a chance to follow the caller in assist-train doctrines, and a
  little randomness so no two bots pick identically.
- **Fight appetite**: how much bigger an enemy group may be before the group
  still engages, from doctrine and the leader's Aggression and RiskTolerance.
  Retaliation always stays allowed.
- **Retreat as an option**: healer dead, half the group down, or clearly
  outnumbered makes a group *consider* leaving; RiskTolerance decides whether
  it actually runs. A retreat is a short run back along the road, then a
  regroup; it never blocks self-defense.
- **Roaming**: weighted wandering between frontier hotspots (keeps, border
  keeps and frontier clearings), recent fight locations and the doctrine's
  favourite places, with a linger time from doctrine and Patience, never a
  fixed loop that every group walks the same way.
- **Formation**: travel shape by doctrine and role (column behind the speed,
  clump for bomb groups, loose for stealthers); melee on the edges, healers in
  the middle, casters behind.

## Forming up, dying, regrouping

- **Form-up:** group seekers wait at their realm's border keep (the portal
  keep of 2003, also bind and safe hub) with LFG; guild leaders there recruit
  them. A group leaves when it is viable (eight, or four after three minutes,
  three after eight). Soloist classes leave at once. Impatient bots give up
  after 8-20 minutes and go out alone.
- **A few die:** the living finish the fight; healers rez out of combat; the
  dead wait up to three minutes while a rezzer lives.
- **Wipe:** one survivor, or most dead with no rezzer: release, regroup at the
  border keep, recover, go again.
- **Deaths inside a group** do not end a bot's RvR tour.

## Leaving the hub

A 2003 group formed up at the portal keep and moved *out* before it looked
for a fight; nobody fought at their own door. Two rules do that here
(owner decision 2026-09-29, task 59):

- **Hub fan.** A leader (or solo RvR bot) that sets out from its own border
  hub first walks to a random point 1,000-2,500 units beyond the edge of the
  safe circle it stands in (4,500-6,000 from the keep centre; measured from
  the outer bindstone landing when it starts there), on a bearing within 120
  degrees of its goal, so groups leave by different sides instead of one
  corridor. The point must be walkable and connected;
  after four failed bearings the group takes the road. Followers keep their
  formation behind the leader.
- **Departure truce.** A bot is *departing* while it is outside the safe hub
  (keep circle and outer bindstone landing), left it less than 180 seconds
  ago, and is at most 2,500 units beyond the edge of the circle it left:
  6,000 from the keep centre, or 2,500 past the landing's edge (Castle
  Sauvage's landing lies about 9,100 units from its keep). A departing
  autonomous RvR bot does not start a fight with a same-realm autonomous RvR
  bot that is departing too. Answering an attack on itself or its group is
  always allowed; players, companions and other realms are not affected.

**Route variants.** Each new destination rolls one route: *road* (the
straight way), *flank* (one via-point 1,200-2,400 to the side at 40-60 % of
the leg) or *cover* (the same, on the side away from the latest fighting
within 6,000 units). Stealth doctrines leave the road 80 % of the time;
assist trains, melee trains and keep raids keep to it 70 % of the time;
everyone else 50/30/20. A cautious leader (RiskTolerance below 40) moves a
fifth of the chances to cover. Keep assaults and siege rallies keep their
straight approach. Legs under 3,500 units are walked directly.

## Camlann and guilds

Guildmates in the area help each other when attacked. Guild grudges (already
persisted, three hours) mark the enemy guilds a group hunts first. A guild
also remembers recent losses against a guild and avoids that guild's groups
for a while unless it feels strong. Realm-based leftovers in rally, siege and
guard-healing code are reviewed for guild ownership.
