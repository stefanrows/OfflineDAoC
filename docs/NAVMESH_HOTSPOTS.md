# Navmesh route give-up hotspots (task 139, step 1)

Evidence collected 2026-10-24, read-only. Nothing was changed in the
installation, the save or any mesh. No server was started. Probe fixture:
`source/server/Tests/UnitTests/UT_Task139HotspotProbe.cs` (`[Explicit]`).

**Short answer.** Most give-ups are not caused by mesh defects. 961 of 1,307
are `NoPathFound` for one of two reasons. The goal or the bot stands more than
the server's 32/64-unit query box from any polygon (521). Or the goal lies
behind a keep gate the bot may not pass (440). Upstream's road fallback acts
only on `PartialPathFound`. It would fix the 87 "dead-end detour" give-ups
(Odin's Gate, Emain Macha, the edge of Connacht). It would not fix the three
top hotspots.

## Data sources

| Source | Content |
| --- | --- |
| `D:\Games\OfflineDAoC\runtime\logs\server-console.log` | 2026-10-23 20:40:41–23:47:20; 1,307 `AUTONOMOUS_ROUTE_RECOVERY` lines (NoPathFound 977, PartialPathFound 295, PathFound 35); 792 with an RvR goal ("Assemble, then roam frontier keeps" 473, "Active PvP" 319). These are the task's numbers. |
| `runtime/server/logs/server.2026-10-23.0.log` + `server.log` | Whole session from 19:46: 1,895 lines (NoPath 1,384, Partial 447). Not used for the counts. |
| Save `runtime/data/opendaoc.sqlite3.db` | Opened read-only (`immutable=1`, or `Read Only=True` in the fixture): zones, keeps, doors, zone points, bind points. |

Emitter: `AutonomousWorldBotController.FinishFailedLocalRouteRecovery`. It runs
after three verified local recovery attempts and then selects a new goal.

## Mesh files probed

| Set | Location | Notes |
| --- | --- | --- |
| Ours (v0.3, 2026-09-18) | `runtime/server/navmesh/zoneNNN.nav` | 99 world meshes. Byte-identical to `tools/NavmeshBuilder/base/zones/`. |
| Dev BG set | `D:\Games\OfflineDAoC-dev\battleground-nav\meshes` | Only the eleven battleground zones (165, 234–238, 240, 241, 251, 252, 254). None of the hotspot zones are in it, and releases ship only these eleven. |
| Upstream 0.35b | Scratch copy only, never installed | Seven zone meshes plus `lib/Detour.dll`, read with HTTP range requests from `OfflineDAoC-v0.35b.zip.00{1..3}`. The 0.35 release is an edition of 0.35b. Its `Detour.dll` is identical to ours (`fb3b51ee…`). It has no `roads.json`; that first ships in 0.36/0.37. |

| Zone | Ours: bytes / sha256 | Upstream 0.35b: bytes / sha256 |
| --- | --- | --- |
| 011 Forest Sauvage | 33,466,256 / `368e71d7…` | 33,973,372 / `586e0368…` |
| 015 Hadrian's Wall | 33,393,860 / `56ff890b…` | 33,568,328 / `41cc9422…` |
| 111 Uppland | 32,592,140 / `d0cc09bb…` | 33,164,136 / `4859a43d…` |
| 115 Odin's Gate | 34,806,984 / `2d8bfcf7…` | 35,069,256 / `ddb48250…` |
| 207 Connacht | 32,422,944 / `6ffd98af…` | 30,283,528 / `e816b64a…` |
| 210 Mount Collory | 29,986,644 / `d4721d33…` | 30,107,192 / `94eed385…` |
| 214 Emain Macha | 32,312,676 / `f6297669…` | 32,644,144 / `e27e102d…` |
| 155 Munin Sound | 30,512,020 / `9f485751…` | not fetched |

Every upstream mesh differs from ours.

## Method

The probe uses the server's own `LocalPathfindingMgr` and the installed
`Detour.dll`. It does not register doors, so "open" means every door
passable and "gates closed" excludes every door polygon (`AnyDoor`).

- `Probe` checks 22 hand-picked start/goal pairs. Steps:
  - snap at the server's 32/32/64 box, then 96/96/512 and 256/256/1024;
  - the raw server query;
  - a chained corridor walk, gates open and closed (it replans from each partial end, so the 256-polygon cap is not taken as a disconnection);
  - the reverse walk;
  - reach of the walkable area (random points within 40,000);
  - legs to the zone's keeps, bind points and zone-point landings, and two-leg routes through them;
  - 48–384 unit offsets of both ends.
- `ClassifyLoggedGiveUps` sorts all 720 distinct logged start/goal pairs
  (weighted to 1,307 give-ups) into these classes:

| Class | Rule | Task class |
| --- | --- | --- |
| goal-off-mesh | No polygon within 32/32/64 of the goal; with the snapped goal the walk completes in 416 of 421 | (a) |
| start-off-mesh | The same for the bot's own position (100) | (a) |
| behind-doors | Complete with doors open, fails with gates closed | (d) |
| one-way-goal | The goal walks out to known ground, but nothing walks in | (b), (c) at a seam |
| start-cut-off, disconnected | No route through the nearest six references | (b) |
| dead-end-detour | Direct search stalls, but start → reference → goal completes | (e) |
| corridor-cap-only, path-found-now | Chained walk or raw query succeeds statically (live doors or timing) | (e) |
| cross-zone | Goal in another zone | (c) |

## Results: all 1,307 give-ups on our meshes

| Class | Give-ups | Logged as | Road fallback helps? |
| --- | --- | --- | --- |
| (a) goal off-mesh | 421 | NoPath | No: NoPath never reaches `TryRoadPath`, and a leg cannot end off the mesh |
| (d) behind keep doors | 440 | NoPath | No: legs cannot pass a closed gate either |
| (a) start off-mesh | 100 | NoPath | No |
| (b) start cut off or disconnected | 127 | Partial | Only if a farther road links them (not proven) |
| (b)/(c) one-way goal | 62 (18 at a zone seam) | Partial | No: the exit leg anchor → goal does not prove |
| (e) dead-end detour | 87 (12 at a seam) | Partial | **Yes**: this is upstream's Sheeroe Hills case |
| (e) corridor cap, path found now | 67 | Partial / PathFound | Partly |
| (c) cross-zone | 3 | — | No |

## Hotspots

### 1. Forest Sauvage, zone 11 (367 give-ups; 249 with RvR goals)

| Cluster | Give-ups | Bots | Finding | Class |
| --- | --- | --- | --- | --- |
| DF portal (zone point 79) in Castle Excalibur, goal 601721,430860,5856 | 89 | 72 | Path found with doors open (4 door polygons). Gates closed, it stalls at the gate. The keep is held by the **Frontier Wardens** garrison (`ClaimedGuildName`), so `CanPassKeep` is false for every bot. | (d) |
| Active PvP targets on and in Caer Renaris (579126,429197,5491; 580657,429203,5044; …) | 146 | 3–6 per cluster | Path found through 1–2 door polygons. Gates closed, it ends 277–916 units short. The keep belongs to guild "Bandit Farmers 2" and the targets stand on its walls and towers (Z 5,491 against a floor at 4,640). The same few bots retry every ~20 s while walking. | (d) |
| Castle Sauvage / BG portal area (586k,474k–477k) | 49 | 21 | The goal snaps 120–360 units off at 3,146, above or beside the floor (16 give-ups). The start is off-mesh or cut off (16). | (a), (b) |
| Rest | 83 | — | 30 start off-mesh, 30 disconnected, 19 path found now | mixed |

### 2. Odin's Gate, zone 115 (93 give-ups; 90 RvR)

| Cluster | Give-ups | Finding | Class |
| --- | --- | --- | --- |
| 606721,600858,6688 → 604966,618990,4654 | 18 (6 bots) | The direct search stalls at the start (2 nodes). Start → Bledmeer Faste → goal completes, and the start's area reaches 36,000 units. The ground is connected, but Detour's 4,096-node search dead-ends. | (e) |
| Other dead-end detours | 27 | Same pattern, mostly Active PvP | (e) |
| 610374,636723 → 612306,638963,8147 | 15 (5 bots) | The goal is 13 units from the zone's north edge (Y 638,976). It walks out to the Albion Portal Keep, but nothing walks in. | (c) seam, one-way |
| 618980,596077 → 620051,596708,7331 | 5 | No polygon within 32/64; the floor is 123–206 units away. Snapped, the path completes. | (a) |
| Rest | 28 | 12 start off-mesh, 9 other off-mesh goals, 3 corridor cap, 2 disconnected, 2 doors or path found now | mixed |

### 3. Connacht 311960,470002 → 335449,421910 (223 give-ups, six bots)

- The goal is **210 units above the floor** (5,401 against the polygon at
  5,191), next to Druim Ligen door 207156901. With the server's 32/64 box,
  Detour finds no end polygon, so every start fails, the near ones too
  (2,661 units away). Snapped, the walk completes from the stable, from the
  field and from the gate, also with gates closed. The point is not in any
  world table, and the probe did not trace which RvR planner computes it.
- The start is stable master Carsten's exact position. Each of the six
  Hibernians (Aedingan 47, Muirinella 42, Conelaelan 42, Caeleaeren 36,
  Roisinaera 30, Branelaeren 26) failed three times near Druim Ligen. Then
  `AUTONOMOUS_ROUTE_POCKET_ESCAPE` sent them to Tir na Nog. The horse route
  brought them back to Carsten, and from 23:36 to 23:47 they re-picked **the
  same destination** every ~22 s without moving. This is a goal loop on top of
  an off-mesh goal, not a mesh problem.
- Class (a). Two more Connacht clusters, other starts:
  - The goal 342246,417812,8671 is 20 units from the southern zone edge and
    1,900 units higher. It walks out but cannot be walked in (42 give-ups):
    (c) seam, one-way.
  - 334517,483317 → 351918,483314: both ends lie on the northern edge (Y
    483,328), and the start → bind → goal route exists (12 give-ups): (e) at a
    seam.

### Next five (by zone)

| Zone | Give-ups | Finding | Class |
| --- | --- | --- | --- |
| 210 Mount Collory | 182 | DF portal (zone point 85) inside **Dun Dagda**, held by the Frontier Wardens. Open doors: path found from the north and the south. Gates closed: 754–884 units short. 161 DF and 20 RvR give-ups. | (d) |
| 111 Uppland | 54 | 25 off-mesh goals (762102,662825,6838 is 977 units above the nearest polygon), 12 off-mesh starts, 8 path found now | (a) |
| 214 Emain Macha | 32 | 25 dead-end detours on the Albion Portal Keep approach (474010,318918 → 475538,343121): connected via Dun Crauchon | (e) |
| 15 Hadrian's Wall | 29 | 9 off-mesh goals, 10 off-mesh starts, 3 one-way, 3 cut off | (a), (b) |
| 151 Aegir's Landing | 23 | 10 starts cut off, 9 off-mesh goals | (b), (a) |

## Top 8 goal clusters

| # | Zone | Goal | Give-ups | Bots | Logged | Class | Roads fix it? |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 207 Connacht | Druim Ligen 335449,421910,5401 | 244 | 7 | NoPath | (a) goal 210 above the floor | No |
| 2 | 210 Mount Collory | Dun Dagda DF portal | 181 | 117 | NoPath | (d) garrison keep | No |
| 3 | 11 Forest Sauvage | Caer Renaris PvP targets | 146 | 16 | NoPath | (d) guild keep | No |
| 4 | 11 Forest Sauvage | Castle Excalibur DF portal | 89 | 72 | NoPath | (d) garrison keep | No |
| 5 | 11 Forest Sauvage | Castle Sauvage area | 49 | 21 | NoPath/Partial | (a), (b) | No |
| 6 | 207 Connacht | South seam 342246,417812 | 42 | 4 | Partial | (c) one-way | No |
| 7 | 115 Odin's Gate | Field 604966,618990 | 18 | 6 | Partial | (e) dead end | **Yes** |
| 8 | 115 Odin's Gate | North seam 612306,638963 | 15 | 5 | Partial | (c) one-way | No |

## Upstream 0.35b meshes at the same points

21 pairs were probed on both sets (zone 155 not fetched). The outcome is the
same for 16. Upstream is worse for 5 and better for none:

- Caer Renaris yard: partial, 7,506 units short; ours finds a path.
- Dun Dagda from the north: partial, 15,041 units short; ours finds a path.
- Castle Sauvage start 583286,478003: the start sits on an island that
  reaches 1,471 units.
- Castle Sauvage goal approach: needs 4 door polygons; ours needs none.
- Connacht east: both ends snap 86–91 units off, and no two-leg route exists.

The off-mesh goals (Druim Ligen, Uppland, Odin's Gate hill) and the
behind-door goals are the same on upstream. **Do not replace meshes for these
hotspots.**

## Step 2 plan: port the road fallback

**Upstream design** (read with `git show upstream/main:…`):

- `AutonomousZoneRoads` loads `navmesh/roads.json`: per zone, anchors
  `[x,y,z,area]` and legs `[from,to,walkedLength]`. It runs A* over legs that
  were proven offline. Each call proves at most 4 nearest endpoint legs and
  remembers routes for 5 minutes.
- `Pathfinder.TryRoadPath` replaces a `PartialPathFound` that ends more than
  256 units from the target (never in DF). It joins the Detour paths of the
  road legs.
- `HasCompleteCorridor` also accepts a road route.
- The door rule lives in `Solve`. An entry anchor reached only back through a
  door, and no nearer the goal, is dropped (`CrossesDoor`: run 24, 292
  give-ups at the Odin's Gate portal keep).

**Data.** `roads.json` is built only from the zone meshes plus the `Zones`
table (offsets): `roadgraph.py` → `navcomp.py` → `navdiff.py` (stdlib-only
MSET parsers). Steps:

1. One anchor per 2,048-unit cell and walkable area, at the centre of that
   area's largest polygon. Tiny pockets get none.
2. Candidate legs go to the 8 neighbouring cells.
3. `UT_ZoneRoadBuild` proves each leg in both directions with the server's
   corridor check and writes the walked length.

Upstream covered 63 zones of regions 1, 100, 200, 51, 151 and 181. We can
generate it from our own meshes. Run it against a copy of the save and the
installed `navmesh/` folder, read-only, and write the output to the dev
folder.

**Files to port or adapt.**

| File | Change |
| --- | --- |
| `GameServer/bots/autonomous/AutonomousZoneRoads.cs` | New. Stub or drop `NavQueryProfile`. Use our `HasCompleteCorridor` (no roads) as `HasDirectCorridor`. |
| `GameServer/world/Pathfinding/Pathfinder.cs` | `CalculatePath(useRoads)` and `TryRoadPath`, only for autonomous bots on the replot path |
| `AutonomousZoneItinerary.HasCompleteCorridor` | Optional road acceptance. Decide separately: it also changes route-recovery proofs. |
| `GameLoopWorkMetrics` / startup | `AutonomousZoneRoads.Preload()` off the game loop |
| `source/server/tools/navmesh_splice/{navdiff,navcomp,roadgraph}.py` | New, unchanged |
| `Tests/UnitTests/UT_ZoneRoadBuild.cs` | New `[Explicit]` build tool |
| New unit tests | `Solve`/`TryRoute` against a fake `IPathfindingMgr` (dead end routed; door re-entry refused; NoPath and DF untouched; missing JSON keeps today's behaviour) |

Add a `meshes` sha256 map to `roads.json` so that a road file built for other
meshes is ignored with one warning.

**Deploy and release.**

- `deploy.sh` / `OfflineDAoC.Deploy.psm1`: `-NavmeshBuild` accepts only the
  eleven BG meshes and targets `runtime\server\pathing`. Add `-RoadsBuild
  <roads.json>` → `runtime\server\navmesh\roads.json` as kind `navigation`,
  with a JSON shape check, the same backup, hash and rollback, and a
  self-check case in `Test-DeployOfflineDAoC.ps1`.
- `build_fork_release.py`: add `runtime/server/navmesh/roads.json` with guard
  `['absent', <previous hashes>]` and a `--roads` argument (default: the dev
  folder). `publish-fork-release.sh` passes it through.
- `AutonomousZoneRoads.FilePath` already looks in
  `<base or cwd>/navmesh/roads.json`, which is the installed world-mesh folder.

**What it fixes.**

| Hotspot | Fixed? |
| --- | --- |
| All dead-end detours: Odin's Gate 45, Emain Macha portal keep 25, Connacht 13 (12 on the east seam), others 4 | Yes, 87 give-ups |
| Corridor-cap and cut-off starts | Maybe, part of 21 + 127 |
| Connacht Druim Ligen (244) | No, needs a goal fix |
| Forest Sauvage keeps and both DF portals (440) | No, needs a goal fix |
| One-way seam goals (62), off-mesh starts (100) | No |

**Cheaper fixes for the rest** (1-3 implemented in 0.314.0; 4 open):

1. Snap route destinations onto the mesh (96/512, then 256/1024) when a goal
   is created. The probe shows complete walks for 416 of 421 off-mesh goals,
   including all 244 at Druim Ligen.
2. Do not plan a goal that requires a keep gate the bot may not pass:
   - DF entrances inside Frontier Warden keeps;
   - Active PvP targets inside or on an enemy keep.

   This covers 440 give-ups. In 1.65 terms, a decent player does not run at a
   closed enemy gate to reach a DF portal.
3. After a give-up, do not re-pick the same destination. The pocket escape
   should not fire when the goal, not the bot, is off-mesh (the Connacht loop).
4. Reject or move goals within about 128 units of a zone edge to the zone
   interior.

**Risks.**

- CPU: `TryRoute` and endpoint proofs on long walks. Upstream counts them in
  `SERVER_MEMORY`. Measure on a live run.
- `roads.json` goes stale when any covered mesh is rebuilt; the mesh hash
  check is the guard.
- Roads reshape long walks. Check them together with our
  `AutonomousRvrRoutePolicy` flank/cover dog-legs and
  `BotTravelHandoff.CanContinuePartial`.
- Static-probe limits: doors are not registered, keep ownership is the
  23:47 save, and the classifier tested only the 6 nearest references. Some
  "cut off" pairs may still be dead ends through farther ground.

## How to rerun

```bash
# Windows SDK from WSL; the server stays stopped; everything is read-only.
tools/dev/winnet.sh build source/server/Tests/Tests.csproj -c Release --no-restore
export OFFLINE_DAOC_NAV_ROOT='D:\Games\OfflineDAoC\runtime\server'
export OFFLINE_DAOC_NAV_ALT='upstream035b=<Windows path to a scratch runtime\server with navmesh\>'   # optional
export OFFLINE_DAOC_TASK139_GIVEUPS='<Windows path to giveups.tsv>'  # region sx sy sz gx gy gz status kind count bots
export WSLENV="$WSLENV:OFFLINE_DAOC_NAV_ROOT:OFFLINE_DAOC_NAV_ALT:OFFLINE_DAOC_TASK139_GIVEUPS"
tools/dev/winnet.sh test source/server/Tests/Tests.csproj -c Release --no-build --no-restore \
  --filter "FullyQualifiedName~UT_Task139HotspotProbe" --logger "console;verbosity=detailed"
```

The TSV is one row per distinct `(region, position, failedDestination,
pathStatus, goal kind)` from the `AUTONOMOUS_ROUTE_RECOVERY` lines.
