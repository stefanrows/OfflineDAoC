# Tasks and Ideas

Capture rapid-fire tasks, feature requests, and ideas here. Include the desired outcome, scope, and any constraints or acceptance checks that are known; leave unknown details explicit rather than inventing requirements. Recording an idea does not authorize implementing the backlog. Bugs belong in [BUGS.md](BUGS.md).

When a task is done and its required verification is complete, move it out of its current section into **Finished** immediately, mark it **Done**, and record a brief result and completion version (or date for tracking-only work). Do not leave completed items in **Open**. If implementation still awaits installation or real-client verification, keep it under **Implemented in source; installation verification pending**, recording the implementation version and outstanding checks, until those checks are complete. Preserve completed entries as history.

## Open

1. **Review autonomous bot dungeon activity, especially Darkness Falls.** Check whether autonomous bots actively travel to Darkness Falls on Camlann for XP and PvP. If they do not, make it a prime destination. Keep a balanced mix of leveling locations and include the other dungeons too, while giving Darkness Falls somewhat higher focus. Current behavior has not yet been checked.

2. **Add a Realm Points column to the launcher's Active Population table.** Show each listed bot's current Realm Points alongside its existing population details. Requested from the Active Population screenshot; implementation has not started.
3. **Rework the Active Groups window for large group lists.** The window takes a long time to load when many groups are present, and scrolling through the groups is inconvenient. Improve loading and make large lists easier to navigate. The screenshot shows the current card-based view with 82 groups; the exact cause and preferred navigation design have not been determined.

## Implemented in source; installation verification pending

1. **Private Tailscale co-op between two home installations.** The launcher now
   has a client-only Join Friend shortcut with a Tailscale IPv4 login-port check,
   separate host and guest account names, and remote-session diagnostics. Source
   version: 0.60.0. Still needs host-side private network setup and the real
   two-home login, region transition, group, reconnect, save persistence, and
   reversed-host checks in [TAILSCALE_COOP.md](TAILSCALE_COOP.md).

## Finished

None.
