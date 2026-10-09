# Classic Quest Pilot: Miari's Seed

This is a short guide to the proposed pilot, based on its clean release
`DataQuest` journal fields. Isolated migration checks passed on temporary clean
release copies in 0.214.1; the pilot has not been applied to an installed or
played database or verified in the ordinary client.

The quest is for Guardians, levels 1-5. Speak with Searlait in the Grove of
Domnann and accept **Miari's Seed**. Talk to Mirari, then find a lesser
sylvanshade northwest of the Grove entrance. Defeat lesser sylvanshades until
the journal records the pouch of seeds, return to Mirari, and turn it in.

The imported row keeps its configured reward fields. Actual XP can depend on
the server's level-stage handling, so this guide does not promise a numeric
payout. The pilot adds no gear reward and no quest markers or Quest Guide
integration.

## Development Migration

The allowlisted source is the hash-verified clean v0.35b release database.
The tool defaults to dry-run and only accepts an existing, marked disposable
SQLite copy inside the operating system temporary directory. Never point it at
an installed or played database. `--apply` and `--rollback` are explicit
development-only operations. Prepare the copy and place a regular file named
`.offline-daoc-disposable` beside it containing exactly this line:

```text
OfflineDAoC disposable quest-pilot database copy.
```

The target must be a separate file under the OS temporary directory, have no
WAL or SHM sidecars, and have only one filesystem link. The known
`D:\Games\OfflineDAoC` installation path is always refused. The source copy is
opened read-only; nothing is written unless `--apply` is explicit. Rollback
removes only this migration's rows in place while holding an exclusive lock,
after checking quest progress, inventory, and other item references. It keeps
the named backup and does not rewind SQLite's `DataQuest` sequence. If commit
recovery is ambiguous, automatic rollback is disabled and the backup is
retained; use a fresh disposable copy instead of overwriting later edits.

The focused regression harness requires an explicit clean-release fixture:

```bash
OFFLINE_DAOC_QUEST_PILOT_SOURCE=/path/to/clean/runtime/data/opendaoc.sqlite3.db \
  python3 tools/dev/test_classic_quest_pilot_migration.py
```

It verifies the pinned source hash, opens that fixture read-only and creates
fresh marked temporary backups for every mutation. Seven checks passed on
2026-10-09, including interruption/recovery, partial ownership and preservation
of unrelated synthetic progress, inventory and currency. These are database
operation checks, not quest-engine or client acceptance.

Gameplay acceptance remains with the owner: verify the offer and journal,
interact/kill/item/turn-in flow, failed and repeated steps, restart persistence,
bot non-interference, the route, and the actual client display before calling
the pilot complete or selecting another batch.
