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

## Support by spec

Evidence (P1, P8): the best-attested gate for a healer doing anything but
healing is a second healer in the group: "In a zerg fight, you can get away
with smiting if you have another cleric in your group to cover heals. In 8v8
there is no way" (D7668). Cave Shamans and pac Healers put CC first:
"Healing is nice, but CC is better" (D5086). Friars that "do not follow the
/assist train are the smart ones" (D7634); a group Bard gives up melee for
its songs (D5113). No source gives an HP or power number; the ones below are
priors.

Autonomous RvR world bots in a group only (companions and player-led groups
keep their behaviour):

- **Smite Cleric, nature Druid**: smite or nuke the leader's current PvP
  target (the Druid also sends its pet) only while a second heal-spec healer
  (not a smiter, cave Shaman, pac Healer, staff Friar or battle Warden) is
  within 2,000 units, free to act and above 20 % power, the lowest living
  member is at 70-80 % health or more (rolled per bot and fight), nobody is mezzed, diseased or poisoned, and the bot
  has at least 50 % power. One fight in three (30 %, rolled once per fight)
  the bot stays on heals anyway. Heals still come first when the ordinary
  heal check wants to top someone up.
- **Pac Healer, cave (Subterranean) Shaman**: when an enemy that can still be
  mezzed or stunned is hitting or casting at a group mate, CC comes before
  heals, without needing a second healer, unless a mate is below 40 % health
  or needs a cure. Otherwise they follow the smite rule (the Shaman keeps its
  usual attack when nobody needs a heal).
- **Staff Friar, battle Warden**: unchanged; they already melee the assist
  target and step out to heal when someone drops below 80 %.
- **Heal specs** (rejuv/enhance Cleric, mend/aug Healer, regrowth/nurture
  Druid): unchanged.
- **Healer area stun**: now also for autonomous Healers in a bomb-group
  doctrine (player-led groups as before).
- **Bard**: a grouped autonomous RvR Bard never melees, even before it has an
  endurance song.

Every five minutes the server logs one line. Counts are episodes per bot, not
ticks: offense and control count only when a cast or pet order actually
happened, heal_only when a Cleric, Druid or Healer falls back to heals: `RVR_SUPPORT_OFFENSE window_s=300 offense=n control=n
heal_only=n by_class=Cleric:offense/control,...` (only classes with offense
or control), plus `RVR_HEALER_AREA_STUN window_s=300 casts=n` when a stun was
cast. The window closes with the next support turn after five minutes, so
`window_s` can be larger than 300.

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
- **Hub-band peace (0.150.0, supersedes the truce inside the band).** The
  truce above only filtered opportunity picks; self-defence, assist, pets,
  area splash and the cc-sweep never asked it, so one opener pulled two
  8-man groups into a "retaliation" fight at the safe edge (0.146.0 live log:
  770 of 1,847 bot PvP deaths in one cell outside Svasud Faste, 98 % Mid
  killed by Mid). Now two same-realm autonomous world bots may not attack
  each other at all while *either* stands in its own realm's hub band:
  within 6,000 of the keep centre, or within the landing radius + 2,500 of an
  outer bindstone landing. There is no retaliation exception. The rule lives
  in the attack permission (`PvpCombatant.BlocksAutonomousPvp`, asked by
  `PvPServerRules.IsAllowedToAttack` and again at damage time), so melee,
  spells, pets, area splash, assist and retaliation are all covered. Humans,
  companions, player-led groups, other realms, monsters and guards are not
  affected; a bot attacked by any of them in the band defends normally. The
  departure truce code stays but is redundant inside the band.
  `RVR_HUB_PEACE` counts refused attack checks per hub every five minutes
  (`stray` = hits stopped at damage time), and the engage summary's
  `hub_band=` counts new fights that still start inside a band.
- **Departure clock and wider band (0.153.0).** The 0.152.0 live log moved
  the grinder to the band edge: 316 deaths (18 %) in one Forest Sauvage cell
  6.3 km from Castle Sauvage, Albion on Albion only. The peace now also
  holds while *either* bot left its own hub's safe circle (keep circle or
  bindstone landing) less than eight minutes ago, wherever it stands: groups
  that leave the same door within minutes of each other are one wave that
  travels out before it hunts. The keep band grows to 7,500 (landings keep
  radius + 2,500). Walking back into a safe circle clears the clock; a bot
  never seen inside has none. This is a rule, not a personality habit.
  `RVR_HUB_PEACE` adds `by_rule=band:<n>,recent:<n>`.

**Route variants.** Each new destination rolls one route: *road* (the
straight way), *flank* (one via-point 1,200-2,400 to the side at 40-60 % of
the leg) or *cover* (the same, on the side away from the latest fighting
within 6,000 units). Stealth doctrines leave the road 80 % of the time;
assist trains, melee trains and keep raids keep to it 70 % of the time;
everyone else 50/30/20. A cautious leader (RiskTolerance below 40) moves a
fifth of the chances to cover. Keep assaults and siege rallies keep their
straight approach. Legs under 3,500 units are walked directly.

## Speed and travel

Evidence (P6, thinking/GROUP_PLAY.md): every 2003 8-man had an "essential
speed class", kept speed up by twisting songs while travelling, and groups
without speed ("speed 4") stayed near keeps and milegates. Autonomous RvR
world bots only (0.154.0); companions and player-led groups are unchanged.

- **Recruiting speed.** When a group of three or more forms or backfills
  and has no Bard, Skald or Minstrel, a speed class gets a strong weight
  (3): after a missing healer (4), before a missing tank (1); a Healer who
  knows the augmentation group speed is the fallback (weight 1). It is not a requirement: the
  group still leaves under the form-up wait rules above.
- **Speed while travelling.** While the group travels out of combat (roam
  legs, hub fan, regroup, moving to a spot) the performer sings only its
  speed, a Bard twisting endurance with it; parked it keeps its usual
  songs; in combat no speed. A Healer who is the group's speed stops once
  on the march to start its pulse (other cast-time buffs still wait for a
  stop). A bot walking an order picks up a new max speed at once (song,
  sprint, stealth) instead of keeping the order's old speed.
- **One body.** Before each roam leg the leader checks its members (alive,
  same region, within 4,000; farther ones are rejoining). If one is more
  than 1,200 behind out of combat it stands until everyone is within 600,
  at most about 6 s, then walks about 10 s before it waits again (both
  ±25 %); after three waits for the same member that did not close up it
  walks on without it until that member catches up. Keep
  assaults, siege rallies and retreats do not wait. Members sprint to
  close a gap of more than 150 to their slot while they have 20 %
  endurance (stop at 80 or 10 %); a world bot's sprint is +30 % like a
  player's. A member ahead of its leader never runs faster than the
  leader.
- **No walking pace.** RvR bots keep full speed below a third of health
  (the monster slow-down does not apply to them) and never use the
  on-foot habit after a PvE release.
- **No-speed habit.** A group of three or more without speed weights
  roaming spots within 8,000 of a keep, tower or border hub by 1.35 and
  others by 0.8.

`RVR_SPEED_STATE group=<id> speed_class=<class|none> members=<n>` is logged
when a group sets out; every five minutes `RVR_SPEED_TRAVEL window_s=300
groups=n with_speed=n travel_under_speed_pct=n leader_holds=n` (leader
travel sampled every 2 s; `travel_under_speed_pct` is the share of samples
with a speed buff on the leader).

## Observe before engaging

Evidence: "let the battle develop a moment before showing your hand"; the
only counted add rule, "wait until at least THREE of the enemy group are
down, then rush in" (two for a higher-rank crew, one for a clearly bigger
group, never while all enemies are alive); groups held at milegate fights
"waiting for the order to rush" and pushed "on good CC". Era ranges: passive
stealth detection about 950, Druid root 1,875, archers 1,500-2,000, so the
hold sits at 2,200.

A party is an enemy group with two or more living members (stealthed ones
included), or any enemy visibly attacking or casting at another player or
bot; a lone enemy walking by is left to the ordinary fight appetite. When a
roaming RvR leader (or solo RvR bot) sees two or more such parties within
5,000 (under Camlann: anyone outside group, guild or battlegroup; the four
nearest visible parties are examined), or one party plus a running fight (a
party fighting other players, or fight heat younger than 60 s at a sighted
party and not within 1,500 of our own fight of the last 90 s), and nobody in
its group is fighting, the group holds. It is not a battle force, relic
escort, siege crew, fresh from its hub (truce band), retreating or resting;
holding and resting exclude each other. The leader steps back to 2,200 plus
a random 0-400 from the nearest enemy when it stands closer than 2,200,
600-1,200 to the side away from it when it stood on a road leg, and picks a
new spot when the fight drifts within 1,800 of it; members gather around
the hold point, face the fight and heal; stealth doctrines watch stealthed.
Nobody opens a fight on their own while the group holds; being attacked
ends the hold at once. "Down" counts members seen dead (a released one stays
down until it stands alive near us again); stealthed members count as alive.
Every 3 s the leader decides, in this order:

- **Leave** (wave-2 retreat run with a danger record, then a new
  destination): the watched party has closed in for 6 s and is bigger than
  the group's appetite; a new party shows up within 2,500 or behind us; or
  an enemy that is not busy stands within 950 and the group would not take
  it on. Early or patient leaves (below) write no danger record.
- **Imperfection:** one roll per hold; below 15 % a leader with
  RiskTolerance under 50 leaves early, a daring one adds early on an engaged
  party.
- **Straggler** first for stealth doctrines, after the add rules for the
  other small doctrines (caster duo, small-man): an enemy below 30 % health
  or more than 1,200 from the rest of its party (then only when the group
  would take on that party); never a mezzed target; only that target.
- **Engage** the watched party at once, busy or not, when the normal
  appetite already accepts it.
- **Add** on the watched (engaged) party when ceil(3/8 of its size) are down (2 for
  Aggression above 65, 1 when we are at least 1.5 times its survivors), and
  never into survivors more than twice our number.
- **Push on CC:** a leader with Aggression above 55 and a mezzer in the group
  pushes into an engaged party without waiting for the count.
- **Keep waiting** up to 60 s x Patience (x0.6 to x1.6) x a +-15 % roll, hard
  cap 150 s [prior: no source gives seconds]; then careful leaders
  (RiskTolerance under 40) leave and the others move on, the next leg bent
  away from the fight (a destination within 2,200 of it is replaced).

An add or push ends any rest and commits the group to that party for 45 s (a straggler call
to that one target), so members join the fight the leader opened. The same
leader does not observe again for 30 s after an add, 60 s after leaving and
90 s after moving on.

## Stealther loop

Evidence (P9): stealthers waited "pretty damn close to the main path" and
at milegates for "that lone victim", opened with Perforate Artery then
Creeping Death about 5 s later, took one kill "(don't be greedy)" and left;
assassins hid again about 6 s after an attack, others about 10 s [D10949,
D13053, D5653, D5231, D10941, D8409, D6118].

Autonomous RvR Infiltrators, Shadowblades and Nightshades in a solo
assassin, stealth pack or gank squad doctrine roam stealthed and at the
player stealth speed (a group member drops stealth when its visible leader
walks off more than 300 ahead, and hides again once the leader hides or
stands). A solo assassin or pack leader waits at its roaming spot 600-1,200
units beside the road it came in on (the hotspot list has no milegate or
portal-exit spots yet). It opens only on a soft victim, best first: a caster
or healer at the edge of its group, a sitting, resting or rezzing enemy (at
the edge, or in a party of up to 3), a
lone walker (no friend within 1,500), the last of a moving column, a
straggler (over 1,200 from the next friend or below 30 %); never a tank, or
anyone in the middle of a group. Anyone already fighting it is answered as
before. The opener is the class's stealth style from the normal style pick,
then the normal chain. It breaks off after the kill, when a second enemy
hits it or below 40 % health (these two at most once per 30 s; after a
break it no longer runs for low health from an enemy in melee range, it
fights): a 300-600 run turned 20-60
degrees from the threat, hide again when out of combat for the restealth
delay (6 s assassins, 10 s others, never sooner than the server's 10 s
combat timer, so 10 s here; after 20 s without success it hunts again
anyway), then a new roaming spot. Log:
`RVR_STEALTH_OPEN` and `RVR_STEALTH_BREAK` per event.

## Assist and interrupts

Evidence (P4, P10, P11): "/assist" took 1-2 s to pick up a target [D4274];
the MA takes casters and healers first [D6422]; any hit interrupts a cast
with a 3 s lock [D14122]; "DON'T BREAK MEZ" [O233, D5086].

In groups with a caller, a damage dealer (not a healer, mezzer, area
stunner or speed class) takes the caller's new target after a rolled 1-2 s,
unless its own target is below 30 %; it misses a call with 15 % chance at
Patience 15 down to 5 % at Patience 85. The caller weighs casters, healers
and mezzers x1.4 more. Archers, melee within 350 and bots with an instant
spell weigh a casting enemy caster or healer x1.8 and look again once a
second when one starts casting in reach (their current target still counts
double). No autonomous RvR bot puts a DoT on a mezzed enemy, or an area
spell over one, unless it is the assist target (the caller's target, or its
own when it leads or roams alone). Log: `RVR_ASSIST_SWITCH switches=...
ignored=... median_delay_ms=...` every five minutes.

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
