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
  it actually runs. A retreat runs up to 3,000 units toward the nearest border
  hub or bindstone landing in the zone (all three are neutral safe hubs under
  Camlann) or a keep the group may pass (not hostile by guild, alliance or
  garrison), whichever is nearest and not toward the enemy (else back to the
  last roam spot, else 2,200 straight away); afterwards the leader picks a
  new destination instead of walking back into the same fight. It never
  blocks self-defense.
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

## After a fight

Evidence: "just 2 minutes to rebuff and reg" (1-2 min normal, about 5 after
deaths and long cooldowns), and "sitting down to regenerate power is an
invitation to be killed" (groups moved off the road or into their keep).

When the leader has been out of combat 8 seconds and any member within
3,000 is dead or below 70 % health or power, a roaming RvR group sits down:
it heals, rezzes and regains power until every living member is at 90 %, or
until a cap of 90 s (+60 s when someone is dead, +60 s when a healer is below
half power; the leader's Patience scales this by up to 30 % and a random
roll by up to 15 %, never less than 60 s or more than 5 minutes). A group
escorting a relic does not sit. If the group stands on a road leg or within
1,500 of the fight, the leader first leads it 600-1,200 units to the side
away from the fight (walkable, connected spot; otherwise it rests in place).
Any attack ends the rest at once; self-defense always comes first. Solo
assassins, stealth packs and gank squads do not sit: they hide again.

## Danger memory

Evidence: groups stayed away from where they died "for a good hour" and came
back "with twice the numbers"; plain avoidance of a place is only weakly
attested.

The group leader's guild (or, without a guild, the leader itself, so the
memory survives a re-formed group) remembers for 60 minutes, fading linearly,
the 1,500-unit cells where its RvR bots died or its leader called a retreat
(a retreat counts half), with the size of the group that lost there; at most
24 places each. A careful leader (RiskTolerance below 45, or a doctrine that
retreats early) rates such a place 0.2-0.5 as a roaming destination; a bold
one (Aggression above 65) goes back only with at least one more member than
it lost there (x1.3, else x0.7); everyone else x0.6. Fight heat inside a
remembered cell is rated the same way. A revenge hunt starts only with at
least two thirds of the group size that was lost. Cover routes keep to the
side away from the worst remembered place within 6,000 when it is fresher
than the latest fight heat.

## Camlann and guilds

Guildmates in the area help each other when attacked. Guild grudges (already
persisted, three hours) mark the enemy guilds a group hunts first. A guild
also remembers recent losses against a guild and avoids that guild's groups
for a while unless it feels strong. Realm-based leftovers in rally, siege and
guard-healing code are reviewed for guild ownership.
