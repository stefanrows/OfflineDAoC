# Companion Stage 6 acceptance

Last updated: 2026-09-23.

## Status

**Offline acceptance complete. Owner-run real-client acceptance pending.** No
client or server was started, and no live save or game installation was opened
or deployed for this work. The Stage 6 gate stays pending until the checklist
below is run and its actual observations are recorded here.

## Decisions carried into Stage 6

- Persistent companions use the existing GameBot corpse and death-recovery
  behavior. Their corpse and party membership are retained while dead. Recovery
  uses the current 20-second path when no viable resurrector is present and the
  90-second path when one is present, followed by the existing bind/release
  behavior. They do not use temporary `/spawn` helper release-to-owner behavior.
- `/raid 40` and `/raid 80` remain limited to the owner's temporary `/spawn`
  helpers; the owner counts toward the selected capacity. Persistent companions
  and autonomous world bots are not eligible raid helpers.
- The existing additive companion roster schema remains save-compatible. Stage 6
  adds no death-state fields, schema migration, or save conversion.
- The six manual-only class plans remain manual-only.

## Offline verification

All database fixtures use disposable GUID-named SQLite files under the system
temporary directory. They do not read or write the live installation's save.

- `UT_PlayerCompanionStage6Integration`: **12 passed**. Coverage includes stable
  identity and owner isolation, same-class recruits and inventory namespaces,
  invite/bench/group removal, save-failure rollback, logout/restart persistence,
  XP and player-reward boundaries, cross-realm grouping and confirmed-transfer gating, existing
  corpse recovery timers, and separation of persistent, temporary, and
  autonomous group members. Raid acceptance/rejection is checked at both 40 and
  80 members.
- Combined companion, persistence, travel, recovery, raid, and defensive-pull
  selection: **278 passed, 0 failed**.
- Full Release server suite: **2,008 passed, 0 failed, 0 skipped**.

The broader selection exposed a defensive-pull integration defect: remembering
an explicit pull target bypassed the 350-unit defensive range check. The gate
now keeps a target remembered while waiting for the owner to bring it within
range. Both the PvE and PvP regressions pass. This runtime behavior correction
is included in version 0.28.0.

Commands used:

```sh
dotnet test source/server/Tests/Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~UT_PlayerCompanionStage6Integration|FullyQualifiedName~UT_PlayerCompanionGearRewards|FullyQualifiedName~UT_AtomicExchangePersistence|FullyQualifiedName~UT_TemporaryGroupStableTravel|FullyQualifiedName~UT_TemporaryCompanionRecovery|FullyQualifiedName~UT_CompanionRaidResurrectionReservations|FullyQualifiedName~UT_CompanionEquipmentDispatch|FullyQualifiedName~UT_AutonomousDeathRecoveryPolicy|FullyQualifiedName~UT_BotMobileSongs|FullyQualifiedName~UT_PlayerLedPullCoordinator' --logger 'console;verbosity=minimal' -clp:ErrorsOnly
dotnet test source/server/Tests/Tests.csproj -c Release --no-restore
```

## Owner-run real-client checklist

Record the date, installed build/version, character used, and observations for
each item. Do not mark Stage 6 complete based on offline results alone.

- [ ] Recruit a level-1 companion; earn NPC XP, level, and inspect training. Keep
  the six manual-only plans manual.
- [ ] Equip or replace companion gear and use its inventory; verify that the
  player's own items, loot, and coin remain unchanged.
- [ ] Recruit two companions of the same class. Verify their identity,
  progression, equipment, and training remain independent.
- [ ] Bench and reinvite each companion; log out and restart, then verify the
  active roster restores without duplication and that offline catch-up behaves
  as expected.
- [ ] Group with a companion from another realm. Confirm travel and verify the
  companion moves only after the owner confirms a successful transfer.
- [ ] Verify death and recovery with and without a viable resurrector. Check
  corpse retention, party membership, the existing recovery delay, and that the
  persistent companion does not return to its owner as a temporary helper.
- [ ] Try `/raid 40` and `/raid 80` with temporary `/spawn` helpers, including
  the owner-inclusive capacity boundary. Verify persistent companions and
  autonomous world bots are rejected as raid helpers.

**Owner observations:** _Pending._ Add the actual result and any failure details
beside each checked item; do not infer gameplay results from these unit tests.

**Owner progress, 2026-09-23:** The owner reports Step 1 preparation complete.
The installed version and backup location still need to be recorded here.
Four owner screenshots of Step 2 show roster/cast/recruit pages accumulating in
the NPC popup, raw action tokens beside clickable links, and an equipment page
crowded with per-slot controls. The owner could no longer select an older page's
choices after navigating. The owner prefers a native inventory-style window if
supported by the client. These Step 2 tasks are tracked as open in the
[companion roadmap](COMPANION_ROADMAP.md#owner-acceptance-tasks-2026-09-23).
Retest on a build containing the menu changes; record whether page transitions
clear the old popup, older visible choices behave safely, the companion bag
opens as a native external inventory, item delves and drag/drop work, and
equipment actions remain usable. Do not mark Step 2 complete before that pass.

## Companion Manager window (0.32.0)

The Step 2 findings led to the native `Custom8` Companion Manager described in
[COMPANION_MANAGER_INTEGRATION.md](COMPANION_MANAGER_INTEGRATION.md). Offline
results on 2026-09-23:

- `test_companion_manager_client.py` x86 emulation of the staged client passed:
  adapter registration ahead of the unchanged raid adapters; packet validation;
  token, show, and hide; clicks sent as `&companions ui <token> <control>`; the
  Search chat prefill; and raid and stock DebugMode passthrough.
- The installer dry run accepted the stage against the installed raid-patched
  client (baseline SHA-256 `67dcf68a…de21e99`, staged `c36faf71…acf2ee`).
- `UT_CompanionManager` (13 cases) and two new manager cases in this Stage 6
  fixture passed. The fixture now passes all 14 cases. The companion, raid, and
  command selection passed 317/317.

**Owner observations, 2026-09-24 (0.32.0):** `/companions` opened the window,
but no button could be clicked. The click areas used event names the client's
`OnClickEvent` parser rejects; 0.32.1 uses numeric event IDs instead (details
in the integration handoff). Recheck the four-step combined check on 0.32.1
first. If it passes, repeat the Step 2 items above in the window instead of the
NPC popup.

**Owner observations, 2026-09-24 (0.32.1):** Gate passed. The window opened,
clicking worked, and the chat-line search worked. The owner's screenshot shows
the roster (four active companions), the Overview tab, and the companion bag
open beside the window. Not yet reported: raid windows and 800×600 (gate step
4), and the individual Step 2 items. Follow-ups are in
[COMPANION_MANAGER_ROADMAP.md](COMPANION_MANAGER_ROADMAP.md).

## Backup, restore, and rollout guidance

Before an owner-run session that can change saved progression, stop the server
and make a fresh backup of the active installation state. Use a new folder
outside the repository and game install, for example
`D:\Games\OfflineDAoC-backups\pre-companion-stage6-<timestamp>\`. Include
`runtime/account.txt`, `runtime/data/opendaoc.sqlite3.db`,
`runtime/server/config/serverconfig.xml`, `runtime/server/bot-goals.json`, and
`runtime/server/rvr-world.json` as applicable. Run `PRAGMA integrity_check` on
the stopped save and verify the backup files against a `SHA256SUMS` manifest.
An earlier backup may not contain the owner's latest progress.

If deployment is separately authorized after acceptance, follow the existing
`tools/dev/deploy.sh` dry-run and apply workflow against the stopped installation.
Its `deploy-<timestamp>` backup and restore manifest cover binaries it replaces;
the deploy workflow protects save/configuration files. The restore script restores
those manifest-listed binaries only. It does not restore the SQLite save or
`account.txt`; use the fresh named save backup and the owner's recovery steps for
those files, with the server stopped.

Record client results before rollout. If any check reveals save incompatibility,
stop and plan that migration separately. No client startup, deployment, or live
save restore was performed for this offline acceptance work.
