# PvE camp play of autonomous world bots (wave 7, 0.152.0)

Autonomous world bots only; companions, `/spawn` helpers and player-led groups are unchanged.
Principles P12 ("the camp, not the mob, is the unit of PvE") and P13 ("pull size follows the
group's control capacity"). Code: `AutonomousPveCampPlay.cs` (pure rules),
`AutonomousPveCampRuntime.cs`, `AutonomousWorldBotController.PveCamp.cs`.

## Rules

| Rule | Numbers |
|---|---|
| Solo con by class | pet casters orange; casters yellow, orange with root/snare; melee and hybrids yellow (blue next); stealthers, Clerics, Healers blue; below level 5 yellow at most |
| Solo ceiling | orange for pet casters and rooting casters from level 5, yellow for everyone else; each PvE defeat lowers it one step, 10 clean kills, a level or a new task raise it one step (task 47) |
| Solo rest at the camp | casters/healers to 75 % power and 60 % health; melee to 80 % health and 50 % endurance; hybrids/stealthers also 50 % power; each bot ±5; no sitting while above |
| Group pull style | Sorcerer/Mentalist/Bard present: mez group, one mob, adds mezzed and left alone; else 2+ pet classes: mass pull of min(6, 2 + pets) same-name mobs within 600 of the target, outdoors, height within 150, in line of sight, not nearer to another party (within 1,500) than to our camp, none while a wipe penalty is set; else single. Bombers are logged only (world bots do not bomb or area stun in PvE yet) |
| Leave a camp | rival party of 3+ fighting within 1,500 of the camp for 3 min (gaps up to 45 s), counted only if it was already fighting in our first 30 s there (the later arrival yields); outgrown (grey to the highest member, or green to the average without wipe penalty, only after levelling there); wipe; no return for 15 min |
| Enemy players | enemy-realm, attackable players at least blue to the group's average level within 2,000 of a member, group not fighting: the leader walks 300-600 away along the walkable surface, the group follows and does not pull; clear after 20 s without enemies; leave after 90 s of their presence; scans capped at 3,000 |

## Evidence (knowledge base `thinking/PVE.md`)

- Pet casters solo orange: "Necro can solo oranges readily" (D3561, O239); reds "not worth the effort" (D5860, O241).
- Casters take yellow then orange with root/snare and kiting (D5677 O244, D4964 O267).
- Rest after every pull to 75 % power, "12 seconds after every fight" (D16161, O249); melee rest is `[prior]`.
- Mez group: "Only ONE mob should make it to the group", never touch the mezzed add (D6297, O272/O273).
- Mass pull: SM pet pulls, AE stun capped at 8 mobs (O280-O282, D13845); here capped at 6 without AE.
- Camp is the unit, groups stay 45 min to hours per band (D5719, O286/O287).
- Never share a spawn with the group that was there first (D5719, O284); competition drops XP (D18536, O292).
- Enemy at the camp: move as a body to the stairs, or leave (D7493, O307; D5719, O289).

## Not modelled

Group rest thresholds (groups still wait until every member is full), PBAoE/area-stun mass pulls, camp bonus, "Incoming" calls,
solo reaction to enemy players, and defensive terrain beyond "away from the enemy, walls stop the walk".
