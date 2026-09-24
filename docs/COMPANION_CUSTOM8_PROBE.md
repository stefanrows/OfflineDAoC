# Companion Custom8 control gate

The first Companion Manager gate uses an isolated, hash-guarded `Custom8` probe.
It is not a playable Companion Manager. The installed client has been restored
to the verified raid-patched build, and bare `/companions` gives command guidance.
The existing `/companions` subcommands remain available. The accumulating speech
menu is not opened while this gate is unresolved.

`source/server/tools/probe_companion_custom8.py` accepts only the verified
Windows x86 client with the native raid extension (SHA-256
`67dcf68a37b95a93946a943b99d5e19b4a03e08cd6469275e25c7b909de21e99`).
It adds a separate `.cmp8` PE section, chains the four raid hooks, and stages
`game.dll`, `uimain.xml`, and `custom8_window.xml` for Atlantis and Isles in a
fresh output directory. The installer verifies input and output hashes, requires
the game to be stopped, and backs up the files for rollback. Unknown client
builds fail closed.

The fixed 128-byte server DebugMode body uses marker `0x43`, version `1`, and
operations `1` (update a short label), `2` (show Custom8), and `3` (hide
Custom8). Raid messages keep marker `0x52`. The experimental client action
uses opcode `0x5E`, with a versioned click or typed-search body. The staged
source and offline x86 emulation are research artifacts; the server does not
register this unproven action channel in the normal installation.

## Real-client result, 2026-09-23

- `/companions` opened Custom8, and the server changed its label. A visible
  button successfully returned a click through the existing target-selection
  path; the server then changed the label to “Click reached server” and sent a
  chat acknowledgment. These three controls passed in the normal game.
- A subsequent probe with a dedicated action packet did not get a server
  acknowledgment. Its search field was visible but could not be focused or
  typed into. The dedicated action and typed search controls therefore failed
  the required client gate. Offline emulation of the hook and packet payload
  did not predict this in-client result.
- The failed probe was removed. Installed `game.dll` again matches the raid
  baseline hash above. Restoration occurred with the game, server, and launcher stopped.

The immediate blocker was the client action and editable-search path. The
manager needs reliable server-bound controls for selecting rows, filtering, and
item actions. Do not replace these with speech pages or a reduced page-button
interface.

## Follow-up, 2026-09-23

The offline repair replaced both failed paths with stock client mechanisms.
Clicks call the client's own slash-command sender, which the right-click menu
uses for `/talk`. The **[Search]** link opens the ordinary chat line prefilled
with `/companions find `. No new opcode or native edit box remains. The probe
builder is kept for reference only. See the protocol, the staged build, and the
single combined real-client check in
[COMPANION_MANAGER_INTEGRATION.md](COMPANION_MANAGER_INTEGRATION.md).
