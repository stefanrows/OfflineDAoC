# Trials of Atlantis on the launcher

Status: implemented in source, real-launcher check pending (WP1.4 of
`docs/TOA_PLAN.md`).

The launcher's **XP Settings** tab has a second block, **TRIALS OF ATLANTIS**,
below the experience and realm point rates. It edits six server properties
that already exist on the server. Scroll the tab down to reach it on a short
window.

## Controls

| Control | Server property | Writes | Code default |
|---|---|---|---|
| Atlantis open to players (checkbox) | `atlantis_teleport_plvl` | on 1, off 4 | 1 |
| ToA loot in Atlantis (checkbox) | `toa_loot_in_atlantis` | `True` / `False` | true |
| ToA cap-bonus chance (0-100) | `rog_toa_item_chance` | the number | 25 |
| ML XP per mob level (0-100) | `ml_xp_per_level` | the number; 0 turns Master Level kill experience off | 10 |
| Artifact XP rate (0-10,000) | `artifact_xp_rate` | the number; DOL default 350, lower is faster, 35 is ten times faster, 0 stops artifact experience | 350 |
| World data packs enabled (checkbox) | `world_data_packs_enabled` | `True` / `False` | true |

What each property does in game: `docs/TOA_TRAVEL.md` (Atlantis access, ToA
loot, cap-bonus chance), `docs/TOA_MASTER_LEVELS.md` (`ml_xp_per_level`),
`docs/TOA_ARTIFACTS.md` (`artifact_xp_rate`) and `docs/TOA_DATA_PACKS.md`
(world data packs).

## Behaviour

- **Server stopped only.** Like the XP rates and "Make Me a GM", the block is
  locked while the server runs or starts. A change writes its
  `ServerProperty` row and applies at the next server start.
- **Loading.** Every launcher snapshot reads the six rows. A missing row shows
  the code default, which the server also writes for a missing row. A value
  the server cannot parse also shows the code default, but the server does
  not use that default: its parse fails and the field keeps its current value,
  mostly 0 or false (`ml_xp_per_level` 10). Saving the control again from the
  launcher repairs such a row. A number outside the launcher's range is shown
  clamped; nothing is written back.
- **Writing.** A checkbox writes its row at once. A number writes its row
  after it has rested for about a second, so arrow clicks give one write.
  Each change writes only its own row; the other rows stay as they are. An
  existing row keeps its category, description and default; a new row gets
  the server's category, description and default.
- **Start guard.** The server does not start while a number is still waiting
  to be saved, and a check right before the commit refuses the write if the
  server started meanwhile.
- **Closing.** Closing the launcher commits a number still being typed and
  writes every number still waiting for its delayed save, while the server is
  stopped. With the server running the change is discarded with the usual
  "only while the server is fully stopped" notice.
- **Atlantis level 2 or 3.** A value set with `/serverproperty` that limits
  Atlantis to GMs (2) or admins (3) shows the checkbox off and says so beside
  it. Ticking it writes 1; unticking writes 4.

## Limits

- Pack 027 (`docs/TOA_TRAVEL.md`) lowers `atlantis_teleport_plvl` from above 1
  to 1 on the first boot that applies it. A save that closes Atlantis with the
  launcher before that boot is opened again once. Saves that already applied
  pack 027 keep the launcher's value.
- Unticking "World data packs enabled" stops new packs only; packs already
  applied stay in the save.
- Not checked in a running launcher on Windows by a person; the automated
  tests render the tab and write a temporary database.
