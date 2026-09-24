# Offline DAoC quick commands

These are the everyday Offline DAoC commands, not the exhaustive list of every
inherited game/server command. They are registered for normal players in this
release; some still have level, target, party or state requirements.

## Camlann crew generation

Click **ADD LV.1 CREW** or **ADD LV.50 CREW** under a realm identity to add
autonomous bots to the Camlann population. These cards choose race, class and
starting realm; they do not create three allied RvR teams.
Hold **Ctrl** for **+100 bots** per click, or **Shift** for **+10**. Without a
modifier, add one bot. Start small and increase the population for your PC's
capacity. The Active Population tab shows autonomous crews and solo roamers.

## Travel and finding mobs

| Command | What it does |
|---|---|
| `/tele X` | Teleport to a gamebot; replace X with its name. You can also right-click bot names in the launcher to teleport. |
| `/mobs X` | List mob names at a level; replace X with the level number. |
| `/tele mob X` | Teleport to a mob spawn; use the mob's exact name for X. Dungeon targets use the configured entrance approach where applicable. |
| `/tc` | Teleport to your realm's Realm Exchange NPC; any realm can use any capital's local broker. |

## Companion groups, grinding, and raids

| Command | What it does |
|---|---|
| `/grind` | Start automated grinding with a companion-bot group, including for AFK use. |
| `/pull` | Order your companion group and pets to engage your selected enemy. When possible, a tank makes first contact before the rest of the group joins the fight. |
| `/train <line> <level>` | Train a specialization to the chosen level using your available specialization points. Select a valid trainer for your class first. |
| `/companions` | Open the Companion Manager window, if its client extension is installed. Without it, you get one line of command guidance. |
| `/companions find <name or class>` | Search the Companion Manager. The window's **[Search]** link types `/companions find ` into the chat line for you. |
| `/companions list` | List companion names grouped by realm. Recruit generated people with `/companions recruit Warden` or another class; use an explicit realm if needed. Recruits are free, start at level 1, and the roster holds 78 companions. |
| `/companions cast` | Browse the 78 authored people, eight at a time. Use `/companions cast <realm> <page>` to browse, then `/companions recruit authored <name> [build]`. Each authored individual can join your roster once. |
| `/companions profile <name>` | Show a saved companion's identity, background, preferred build, role, stance, and dialogue. |
| `/companions build <name> [build]` | List a companion's builds, or switch to one, for example `/companions build Astrid summoning`. Switching is free, works anywhere, retrains the new build to the companion's level, and sets the build's role. Recruit with a build: `/companions recruit healer pacification`. |
| `/companions role <name> <role>` | Set a class-legal tank, healer, buffer, attacker, or `cc` (crowd control) job. A crowd control companion mezzes extra monsters that are not the group's target; other companions leave mezzed monsters alone. |
| `/companions stance <name> aggressive|defensive|passive` | Set an individual's saved engagement preference. Group commands override it until `/companions group default`. |
| `/spawn` | Open the menu of valid companion bots to summon. |
| `/spawn X` | Summon a companion by class name from your realm instead of using the menu; useful for macros. |
| `/spawn Realm X` | Summon a named class from another realm, such as `/spawn Midgard Healer`. |
| `/raid 40` | Enable a 40-member companion raid. Use **before** `/spawn`. Requires level 50; the total includes you. |
| `/raid 80` | Enable an 80-member companion raid. Use **before** `/spawn`. Requires level 50; the total includes you. |
| `/aggressive` | Companions assist your attacks and defend the party. They break off and return if left far behind. |
| `/defensive` | Companions engage threats near you and return if left far behind. |
| `/passive` | Companions drop combat, recall their pets, and return to you without attacking. Choose another mode to resume fighting. |

`/spawn 40` and `/spawn 80` are **not** the raid-size commands. Use `/raid` first.
These modes control your companions, not autonomous gamebots. All three modes recall a companion beyond 2100 units until it reaches 650 units from you.

## Camlann PvP commands

| Command | What it does |
|---|---|
| `/safety off` | Permanently turn off the under-level-10 PvP safety flag. Capitals, housing and portal-keep hubs remain sanctuaries; leveling towns and the Old Frontiers are dangerous. |
| `/gc form <guild name>` | Start player guild formation with your current group. Companions and recruited bots can join the guild and count toward its keep claims. |
| `/gc invite <bot>` | Invite a targeted or named autonomous bot to your guild when you have the guild invite rank. |
| `/relics` | Inspect the current guild-owned keep and relic state. Relic bonuses belong to the carrying guild, not a realm. |

There is one full-PvP world: an ungrouped, unguilded or unallied player-shaped
actor can be hostile regardless of realm. `/level` is disabled, battleground
travel remains closed, and `/spawn <realm> <class>` is the cross-realm companion
choice.

For the complete advanced reference, see **ALL SERVER COMMANDS.txt** inside the
download. That file separates normal-player, GM and administrator registrations.
