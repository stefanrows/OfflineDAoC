# Camlann bot activity and Darkness Falls review

Reviewed 2026-09-25; source changes in **0.61.0**, awaiting installation and
real-client verification. The existing server was inspected read-only and left
running. No save, live settings, installed mesh, or running assembly was changed.

## What the launcher labels mean

**Type** is a character's personal playstyle. **Charter** is its guild's
preference and recruitment mix. **Activity** is what it is doing now. A PvP
charter does not mean every guild member hunts constantly, nor does a Leveler
in such a guild become a dedicated Hunter.

The persisted `Rvr` charter/objective identifiers are legacy implementation
names. They do not select the server ruleset. The inspected installation uses
`GameType=PvP`; hostility follows group/guild alliances rather than realm.
Version 0.61.0 displays the charter as **PvP**, updates population-setting copy,
and renders legacy RvR activity text as PvP without rewriting saved identities.

## Read-only session sample

The sampled run began around 20:30 local time; the frozen log window ends at
20:58:45. These are observations of the previous running code, not results of
the new source changes.

| Observation | Result |
| --- | --- |
| Active autonomous population | 600 |
| Types | 181 Leveler, 175 Hybrid, 94 Casual, 80 Hunter, 70 Roamer, 0 KeepWarrior |
| Current durable assignments | 328 SoloPve, 254 GroupPve, 18 PvP |
| Ages of characters | 46 below level 10, 485 at 10–19, 69 at 20+; maximum 28 |
| Guild charters | 5 Hunting, 5 Leveling, 4 PvP, 1 Keep |
| Observed deaths in log window | 504 PvP, 889 PvE |
| New attacker/victim engagement observations | 949; these are not distinct group battles |
| Last 30 activity summaries | Mean 222 fighting, 111 camping, 142 traveling, 16 meeting up, 8 dead, 4 in town, 98 other |
| Active dungeon residents in sampled snapshot | 10 across seven dungeons; none in Darkness Falls |

The configured type weights are Leveler 25, Casual 18, Hybrid 30, Hunter 15,
Roamer 12, KeepWarrior 0, with Authentic danger and Fresh launch. A saved guild
charter can still be Keep even when no active character has the KeepWarrior
type. Configured weights are not a quota on the current logged-in sample.
Both human and bot mob-XP multipliers are 10. The selected world speed was 3×,
with approximately 2.78× achieved and no connected clients at inspection.

There is already substantial fighting alongside leveling: roughly 55.5% of
the sampled population was fighting or camping. The 18 durable PvP assignments
understate actual PvP exposure because attacks interrupt PvE and victims defend
themselves. Most characters were still below 20, so this run cannot establish
the eventual level-50 balance.

## Findings and corrections

- **PvE aggression bypass:** the early frontier threat scan initiated fights
  regardless of a bot's durable PvE task, recovery needs, or opposing strength.
  Logged frontier first strikes included 88 Levelers, 83 Hybrids, 73 Roamers,
  27 Hunters and 23 Casuals in the reported categories. The scan now requires
  an eligible PvP hunt or an actual fight involving the bot/group. Committed
  siege forces retain their objective combat behavior.
- **Shared-dungeon bypass:** a second unconditional scan could make PvE bots
  initiate PvP in DF. It is removed; the normal local opportunity policy now
  covers that dungeon. Defense, allies, safety and legal-target checks remain.
- **Hunter patrol timing:** the three-minute dwell interval began when a trip
  was planned, so long travel consumed it. It now begins at the destination,
  including a floor-height check inside dungeons, and resets on reassignment.
  Failed dungeon patrol routes receive the existing 30-minute camp cooldown;
  random patrol steps must have a complete local corridor.
- **Visibility:** the generic hunt scan now explicitly excludes stealthed
  targets, consistent with the frontier scan.
- **Dungeon selection:** Hunters previously rejected every dungeon. Ordinary
  pickup groups also had a final selection constraint that confined them to
  their outdoor rendezvous region and preferred outdoor camp. Groups can now
  select proved camps in directly connected, accessible dungeons. The shared
  level/party checks and the environment draw still apply.

Hunters retain level-appropriate camp selection and the upper opponent level
limit of five levels above themselves. A visibly larger or substantially
higher-level party is avoided when starting a fight. Grey-target restraint
uses the configured chance; grudges do not waive these checks. Existing
autonomous safety below level 10 remains in force. This implements the owner's
level-10+ roaming target, rather than removing starter protection.

Levelers and Casuals keep their PvE focus; Hybrids alternate activities;
Hunters and Roamers supply deliberate PvP. Player-led companions continue to
follow their owner. The navigation repair below also benefits their movement.

## Darkness Falls implementation

DF was disabled in three destination gates. Its existing mesh also had
disconnected entrance platforms: three tall steps at each realm entrance
prevented complete paths into the dungeon. Earlier partial Detour paths could
echo a requested endpoint on another floor; endpoint proximity alone was not
sufficient evidence of a route.

The repair supplies nine bidirectional stair links on nine tiles. It retains
the baseline mesh header and **7,932 other tiles byte for byte**, including
the six original off-mesh connection tiles. The runtime verifies the baseline
hash, applies the embedded tile replacements to a derived copy, verifies its
result hash, and loads that copy from the OS temporary directory. The installed
`zone249.nav` is never overwritten. An unknown mesh or failed repair keeps
autonomous DF destinations disabled while preserving ordinary mesh loading.
Path smoothing retains the exact stair link endpoints for gamebots.

A native 64-bit Detour audit checked 2,283 existing neutral standard monster
spawns, level 1–65, against all four distinct portal landing coordinates.
**1,417 spawns have complete paths in both directions from at least one
entrance.** The entrance-specific counts are 465, 420, 546 and 406 (overlapping
sets). Missing floors and incomplete corridors remain excluded. Each proof
records its permitted entrances and exact world-spawn identity; changed spawns
do not silently inherit the proof. These are geometry proofs, not a guarantee
that a low-level party can survive every intervening monster.

DF now participates in the existing PvE dungeon share, with twice the normal
destination weight within that share. Existing capacity/population weighting
still applies. Hunters choose dungeons on 30% of destination draws when both
environments are available, leaving 70% outdoors; within dungeons DF gets twice
the region weight. These are selection probabilities, not population quotas.
Other supported dungeons remain eligible. Safe starter dungeons are not hunt
destinations. DF portal access already follows PvP rules regardless of the
realm-control setting `allow_all_realms_df`; no live property change is needed.

## Reproducing the offline navigation evidence

Use disposable output directories and a consistent SQLite backup, never a
running installation's database. No database or private log is checked in.

1. Export zone 249's geometry using the installed navigation builder's
   `OpenDAoC-BuildNav.exe` apphost in a scratch copy of its tool directory.
   Use `--daoc=<client app> --zones=249 --obj=true --ignorelist=<scratch list>`.
   The apphost locates its bundled `base` directory; invoking the DLL through
   `dotnet` selects the wrong executable directory. With redirected input the
   exporter may fail at its final `Console.ReadKey` after writing the geometry.
2. Preserve the original mesh grid origin when generating the scratch mesh.
   Append the unreferenced OBJ bounds vertex
   `v 303.53668212890625 404.81927490234375 290.1788330078125` to the exported OBJ.
   Create `base/zones/zone249.gset` with `f zones/zone249.obj` and the nine
   links in [darkness-falls-stairs.json](../tools/dev/darkness-falls-stairs.json).
   Each link line is `c ax az ay bx bz by 1 1 5 8`, with all six world
   coordinates divided by 32 (radius 1, bidirectional, area 5, Jump flag 8).
3. Run the scratch `RecastDemo.exe zones\zone249.gset zones\zone249.nav`
   with its working directory set to that scratch `base` directory. Its full
   rebuilt mesh is only an input for extracting the nine required tiles.
4. Package those tiles with the repository audit tool. It rejects a changed
   grid and checks that each audited tile has exactly one stair link:

   ```bash
   python3 tools/dev/audit-darkness-falls.py package \
     --baseline <original-zone249.nav> --rebuilt <scratch-zone249.nav> \
     --output <new-scratch-patch.json> --mesh-output <new-repaired.nav>
   ```

5. Build the offline native library and audit the repaired mesh:

   ```bash
   g++ -shared -fPIC -O2 -std=c++17 -DDT_POLYREF64 \
     -I source/server/Pathing/Detour/Include \
     source/server/Pathing/Detour/Source/*.cpp -o <scratch>/libDetour.so
   python3 tools/dev/audit-darkness-falls.py audit \
     --snapshot <copied-world.db> --mesh <new-repaired.nav> \
     --library <scratch>/libDetour.so --output <new-scratch-proofs.json>
   ```

The audit rejects partial and truncated paths, checks the final endpoint, and
requires the return route. The packaged patch and catalog are embedded in the
server assembly; deployment needs no client binary or native library change.

## Verification and remaining work

Offline native route audit and nine-tile package reproduction passed. Server
and Windows launcher Release builds passed. Automated test suites were
not run, following the local workflow. No new server was started and no
real-client traversal of the new code has been observed.

After installation, check all three entrances down and back up with companions,
solo Hunters and a PvE party; watch their selected entrance, stair movement,
floor selection, death/release and normal exit. Verify local hostile-player
encounters, stealth, alliance protection and starter safety. Compare the next
run's PvE/PvP deaths and travel/camp time at 10–19, 20–39 and 40–50 before
changing population settings.

The previous run also recorded **23 camp-travel deadline expirations among 49
reported group outcomes**, plus nine shared-task expirations. Reducing unwanted
PvP interruptions may help, but it does not prove the travel failures fixed.
This remains a tracked routing/activity issue in [BUGS.md](BUGS.md), not a
reason to declare the current balance fully validated.
