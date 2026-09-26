# Tailscale co-op: implementation and live verification plan

Status: Join Friend launcher implementation is in source at version 0.60.0;
host network setup and real two-home verification remain pending. This plan
covers two Windows players at different homes. Either player can host their own
existing Offline DAoC world while the other joins over Tailscale. The first live
check must use those two homes; a LAN test is not a prerequisite.

## First playable result

- The host starts the existing server and client through the normal launcher.
- The guest opens the client-only **Join Friend** window with
  `Join Friend.cmd` or `OfflineDAoC.exe --join`, enters the host PC's Tailscale
  IPv4 address, the host's local account name, and their own game account
  credentials, then enters the host's world without starting a local server or
  opening a local save. The launcher checks TCP 10300 before starting the client.
- The host can invite the guest into an ordinary eight-member group. Each human
  takes one slot; companions can fill the remaining slots. The guest can later
  host by reversing the same steps.
- Accounts, characters, companions, inventories, and world progress live in the
  current host's save. A character made on one host does not appear on the
  other's host. Reconnecting to the same host uses the same guest credentials.

## What the current code already provides

- The server accepts multiple network clients, can create a new account at
  login, and handles player-to-player group invitations. Camlann grouping is
  open across realms. Ordinary group capacity is eight.
- `source/tools/OfflineDaoc.Launcher/MainForm.cs` passes `127.0.0.1` and the
  local `account.txt` credentials to `connect.exe`. Its startup path checks the
  local database and may offer a world reset. The new `--join` entry bypasses
  `MainForm` entirely; `Join Friend.cmd` starts that client-only mode directly.
- `source/server/CoreServer/config/serverconfig.example.xml` currently listens
  on all IPv4 interfaces, with login TCP port 10300 and game UDP port 10400.
  It also enables UPnP and external IP detection. These settings need review
  before a co-op host is treated as private.
- `source/server/GameServer/packets/Server/PacketLib1126.cs` selects the local
  socket address when the region address is unspecified. It may therefore
  advertise the host's Tailscale address to a Tailscale guest; verify this on
  the actual client rather than assuming region transitions work.
- The special 40/80-member `/raid` companion group excludes another human.
  The first co-op release uses ordinary groups.

## Implement in this order

### 1. Establish the private route at the two homes

1. Both PCs run Tailscale. Share only the gaming PC with the other Tailscale
   account, in both directions, or put both PCs in one tailnet. Record each
   PC's Tailscale IPv4 address. The person joining must be able to reach the
   current host PC through Tailscale. See Tailscale's
   [device-sharing guide](https://tailscale.com/docs/features/sharing).
2. On the host, inspect the actual ignored `serverconfig.xml` without
   replacing it. Back it up before changing network settings. Turn off
   `EnableUPnP` and `DetectRegionIP`; leave the region address unspecified so
   the server can return the address of the interface the guest reached.
   Preserve the existing database and all other local configuration.
3. Allow the server's TCP 10300 and UDP 10400 through Windows Firewall for
   the Tailscale connection only. Do not create a public game-port forwarding
   rule. Check the effective firewall rule and Tailscale reachability from
   the other home before troubleshooting the client.

### 2. Add the smallest useful guest launcher path

1. Add a client-only `--join` entry in `Program.cs` that opens the Join Friend
   form directly. This is implemented: it does not construct `MainForm`, inspect
   or reset the local world, touch the local save, or start `CoreServer.exe`.
   `Join Friend.cmd` ships beside the launcher as a direct shortcut.
2. Ask for the host's Tailscale IPv4 address, the host's local account name, and
   the guest account name and password. Compare account names and reject a
   duplicate. Validate the IPv4 address and the legacy 20-character ASCII login
   fields. Do not reuse or overwrite the guest's local `account.txt`. Do not log
   credentials or put them in diagnostics. The form remembers the host address,
   host account and guest account per Windows user in
   `%LOCALAPPDATA%\OfflineDAoC\join-friend.json`. The password is stored only
   when the guest ticks "Remember my password", encrypted with Windows DPAPI for
   that Windows user; otherwise it is entered again each session.
3. Reuse the current compatible `game.dll` and `connect.exe` launch method,
   client profile isolation, and diagnostics. The client receives the host
   address instead of `127.0.0.1`; diagnostics observe TCP 10300 to that address.
   The launcher checks TCP reachability and shows setup guidance if the host is
   unavailable. The server can create the guest account on first login when
   account auto-creation remains enabled. The guest must retain the same
   credentials for later sessions on that host.

### 3. Verify only the server behavior the remote session exercises

1. From the guest's home, create a character on the host, enter the world,
   move and fight, cross a region boundary or teleport, and reconnect. If a
   region transition fails, inspect the address sent by `SendRegions` and the
   UDP path before changing networking code.
2. Invite the guest to the host's ordinary group. Check group colors,
   targeting, heals, XP/loot, and companion assistance with both humans
   present. Bench a companion with `/companions bench <name>` if all eight
   slots are occupied. Confirm that each player's companion commands and
   rewards remain scoped to their own character.
3. Stop the host server normally, back up the host save, and verify the guest
   character and inventory persist on reconnect. Repeat the login, zoning,
   group, and save checks after swapping which home hosts.
4. If Tailscale relays the connection and gameplay latency is poor, diagnose
   its connection type. Network optimization follows the first successful
   two-home playtest; it does not block the initial client-only launcher path.

## Completion gate

- Both directions pass a real two-home session with distinct accounts and a
  mixed human/companion group. The guest does not start or modify their own
  server or save when joining.
- Login, UDP movement, region transitions, disconnect/reconnect, and host-side
  save persistence work. The host's existing solo launch still works.
- No game port is publicly forwarded, UPnP is off for the hosting server, and
  credentials stay out of logs and tracked files.
- Record source changes under a MINOR fork version and keep all version pins
  synchronized. The launcher implementation is recorded as 0.60.0. Build
  without deploying until the owner asks to ship. The owner performs
  real-client verification after install.

The first release needs no account transfer, character synchronization, router
configuration, public server listing, or automatic Tailscale installation.
