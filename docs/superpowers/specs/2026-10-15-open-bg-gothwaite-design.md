# Open BG together with the Gothwaite PvP mode (design)

Date 2026-10-15. Owner request (urgent): the open BG must work on a server that runs the Gothwaite PvP world mode (Stefan's server is set to Gothwaite). Analysis by a read-only advisor (opus) against code at 0.278.0; decisions below are the advisor's recommendations, adopted by the controller because the owner asked to proceed fast and to ask only when unclear. The owner also asked that the crew **level band is configurable in /bgsetup**.

Related: `docs/GOTHWAITE_PVP_MODE.md` (sections 5, 11.1), `docs/OPEN_BG_PVP_CONCEPT.md`, plan `docs/superpowers/plans/2026-10-15-open-bg-gothwaite.md`.

## 1. Intent

With `gothwaite-pvp.json` enabled, every autonomous world bot is a Gothwaite *mode bot* and the open BG is switched off (no crew recruited, no director). Wanted: the open BG keeps working exactly as without the mode, while every bot outside a **reserved set** stays a Gothwaite mode bot. Mode off: nothing changes. Open BG without the mode: unchanged.

## 2. Owner decisions (defaults adopted 2026-10-15)

1. **Reserved set**: a deterministic, stable set of world bots that are NOT mode bots and are the only bots the open BG recruits while the mode is on. Level band configurable (`open_bg_band_min`, `open_bg_band_max`, default 40 and 50) via property, `/bgsetup band_min|band_max` and the launcher.
2. **Size**: per realm `ceil(2.5 x min(seats, ceil(max_bots / 3)))` with `seats = 8 x groups8 + 4 x small + solos` (maximal sizes), at most 30% of the bots of the band per realm. With defaults 20 seats give 50 per realm, 150 in total.
3. **Between deployments** reserved bots park at their own border hub (Castle Sauvage, Svasud Faste, Druim Ligen: safe areas) and do not run the world controller (no frontier roaming, no sieges or claims outside the open BG).
4. **Stable choice**: ordered by `unchecked((ulong)botId * 2654435761UL) & 0xFFFFFFFF` (ties by BotId): mixes guilds and classes, deterministic; a small churn at level thresholds is accepted; no persisted flag in this stage.
5. **Bracket battlegrounds** stay off for everyone in the mode.

## 3. Design

- One new pure unit `GothwaitePvpOpenBgReserve` in `bots/gothwaite/` (the contract: mode logic stays in that folder): `Choose(modeActive, openBgEnabled, setup, bots) -> IReadOnlySet<long>` (empty if the mode is off or the open BG is disabled; candidates: not retired, realm 1-3, level inside the band), `ReserveSize`, `Order`. Built once in `GothwaitePvpHomeStore.ReconcileAtStartup` (runs before any login), stored as an immutable set read with `Volatile`; `GothwaitePvpMode.Reset` clears it.
- `GothwaitePvpMode.IsModeBot` (GothwaitePvpMode.cs ~56) gets `&& !reserved`. Nearly everything else follows from this single predicate: brain branch (the open BG participant branch then runs), castle login, castle release, sub-10 rule, constitution loss and the combat filters are all keyed on `IsModeBot`.
- Home snapshot: reserved bots KEEP their `offline_gothwaite_pvp_home` row (they are moved too: hub, capital, Thidranki); mode off then restores everyone as today; `GothwaitePvpHomeStore.Plan` unchanged; a bot entering or leaving the set between two starts is harmless.
- Login: a reserved bot is placed at its own border hub (`AutonomousRvrStaging.TrySafeRvrRelease(realm, true, ...)` plus spread by BotId) instead of the castle (`GothwaitePvpHomeStore.TryPlaceAtCastle` gets a reserve branch).
- Objective: the coordinator's early return in the mode must stay (it would rewrite every mode bot's stored objective). The standby driver writes the RvR objective stamp itself for a reserved bot that lacks it: the same fields as the RvR branch of `AutonomousObjectiveAssignments.Assign` (`ObjectiveKind="RvR"`, `ObjectiveAssignmentId="gothpvp-openbg-{id}"`, assigned/expires with `RollRvrTenure()`, `ObjectivePveMode=""`, kills 0, `ObjectiveRvrEligibleUtc=""`, dirty + status persistence queue). The group formation of the open BG does not need the world coordinator tick (the roster forms its own parties; `Pulse` per bot keeps running).
- Standby driver (`BotBrain.ThinkCore`, one line after the participant branch): `IsStandby(active, reserved, inAnyBattleground, playerLed)`. Turn: (1) RvR stamp if missing; (2) leave a bot-only group that is not an open-BG party (else the roster rejects it as `in_group`); (3) if not at the own hub, alive, not in combat, not walking back after a release, not on a stall route: move to the hub release point with spread (`GOTH_PVP_OPENBG_PARK`); (4) at the hub: rest, buff, `FSM.Think`.
- Open BG guards: `GothwaitePvpRules.SuppressesOpenBattlegroundCrew(active, reservedCount)` (mode on and set empty) replaces `SuppressesWorldRecruitment` in `OpenBattlegroundRoster.Tick` and `OpenBattlegroundDirector.Tick`; `MayJoinOpenBattleground(active, reserved)` as the first line of the `CandidatePools` loop (covers refill and `TryPickGroup`); `RefusesOpenUnit(active, allReserved)` in `AssignOpenUnit`; the bracket `Admit` stays off; attack observers, alt trickle, coordinator tick stay off.
- Status: `/gothpvp` gets a line "open BG reserve N, standby a, crew b"; log `GOTH_PVP_OPENBG_RESERVE` at start; `GOTH_PVP_OPENBG_PARK`.
- Off contract: every new predicate starts with `IsActive`; with the mode off `SuppressesOpenBattlegroundCrew`, `RefusesOpenUnit` and the standby are false and `MayJoinOpenBattleground` is true.

## 4. Level band (owner request)

Properties `open_bg_band_min` / `open_bg_band_max` (defaults 40 / 50; ranges 1..50; min not above max, otherwise clamped like the other open BG values). Consumers: the crew selection of the open BG roster (today `OpenBattlegroundRoster.MinLevel..MaxLevel`, about line 178), the low-level protection rule that uses `OpenBattlegroundRoles.CrewMinimumLevel`, and the reserved-set choice. `/bgsetup band_min <n>`, `/bgsetup band_max <n>` and the launcher fields follow the existing key/validation pattern. Guest squads for players under the band are unchanged.

## 5. Risks and limits

- Reserve and Gothwaite bands compete for the same bots: a small population or a server whose bots are mostly below the band gets an empty reserve and therefore no open BG (the band setting is the lever; Stefan's server may run low levels).
- Parked bots stand in groups of up to ~50 per realm at the hubs (acceptable: LFG picture); they leave the capitals.
- A set change at run time is not applied before the next start (the set is frozen); `/bgsetup max_bots` or a band change applies after a restart while the mode is on.
- After mode off, the RvR stamp stays on former reserved bots; the normal reconcile resolves it.
- Not verified before building: whether a medallion merchant is in reach at each hub porter; whether `RepairInvalidLoginPosition` accepts the hub point; whether `RemoveBrokenSessions` leaks sessions of non-pulsing members.

## 6. Acceptance (offline, then live at high speed with the mode on)

- Pure tests per unit (set choice, size, order, determinism, empty when mode or open BG off, band from properties, `IsModeBot` with reserved, standby and stamp predicates, guards with mode off equal today's behaviour).
- Live: `GOTH_PVP_MODE active=True` together with `OPEN_BG_ROSTER total>0` and `AUTONOMOUS_BG_ENTERED`; no `GOTH_PVP_ENTER` for a name in the reserved set; mode off restores everyone as before; the Gothwaite arena still fills with mode bots.
