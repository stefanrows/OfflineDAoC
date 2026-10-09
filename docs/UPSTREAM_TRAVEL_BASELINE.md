# Outdoor Route Threat Baseline

## Source and default

Stage 6 adapts the pinned upstream route-threat work at
`c8b0dca0428666fbe2c6f68d0e4d0363b0d03caa`. The local implementation is
independently gated by `autonomous.bot_route_threat_awareness`, which defaults
to `false` until the owner accepts a controlled performance comparison.

The fork uses one complete native navmesh corridor with blocking-door filters
to find route threats. It caps the scan at 24 NPC candidates and six inline
route-corridor `GetPathStraight` queries per scan, verifies both legs of any
detour as complete corridors, and never substitutes a straight ray for a
missing or partial path. An actual level-50 defensive group pull separately
uses the existing bounded firing-point proof (up to two 16-step complete
corridor checks, plus floor and line-of-sight checks). `OutdoorRouteThreat`
profiles this work as a nested phase while native calls remain visible in
`NavPathQuery`; these counts are not additive or a six-total-query claim.

Eligible route actors are ordinary aggressive experience monsters. NPCs that
offer scripted/data quests, teleporters, service NPCs, pets and player-shaped
actors are not route monsters; quest kill targets remain eligible when they
are ordinary aggressive monsters rather than quest/service actors. Only solo
bots and autonomous group leaders on same-zone outdoor PvE camp travel are
eligible. Dungeon corridors, frontier travel, safe areas, RvR,
service/trainer routes, release returns and other objectives remain on their
existing policies.

## Deliberate deviations

The pinned implementation is a source reference, not a wholesale import. This
fork does not infer a usable path from a clear ray or substitute an unchecked
side point when a native corridor is missing. It does not scan every follower,
carry dungeon or frontier rules into this policy, or stop considering a threat
because a prior detour was attempted. Query work is explicitly capped and the
feature stays disabled until the owner accepts measured overhead. Existing pet
pull and reward behavior are unchanged.

## Historical observation

Read-only aggregates from an already-installed server log snapshot on
2026-10-09, before any controlled Stage 6 comparison:

- Selected/effective AI budget: 33.333 ms at 1x.
- Last three recorded one-minute tick P95 values: 44.143 ms, 43.379 ms,
  and 43.215 ms.
- Corresponding `SERVER_WORK` `NpcService` stage `avgMs` values: 13.062 ms,
  13.112 ms, and 13.766 ms per tick. These are server NPC-service stage
  averages, not individual bot think durations.
- Latest bot profile: `NavPathQuery` total 31,265 ms and 52,868 calls per
  60 seconds.

The separate latest `BOT_THINK_PROFILE` individual bot-turn `avgMs` was
0.74 ms; it is not the same metric or an exactly aligned observation window.

This is an uncontrolled historical sample, not a paired before/after run.
It does not establish a performance gain or acceptable overhead. No live
database, raw log, personal-data hash, or individual actor record is included.

## Owner-controlled acceptance

After a build and final source review, the owner should choose the live window
and accelerated speed. Compare the same population, settings, route set, and
observation duration with the property off and on at 1x, then repeat at the
requested accelerated speed. Record camp arrivals, travel deaths, repeated
route pulls, camp rejections/route failures, achieved simulation speed, tick
P95, `NpcService` stage averages, individual bot-turn profile, `NavPathQuery`,
and `OutdoorRouteThreat` phase cost and call counts. Keep the switch off if the
comparison is unavailable or the owner has not accepted the measured
tradeoff. Source-only work does not verify existing-mesh routing or real-client
behavior.
