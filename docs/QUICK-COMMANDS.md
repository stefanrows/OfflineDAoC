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

Use **Server population** while the server is stopped to choose a preset or
six-type mix, leveling-zone danger, and Fresh launch or Established world
shape. **ADD LV.1 CREW** follows the chosen shape: Fresh launch makes level-1
bots; Established creates a spread of levels. **ADD LV.50 CREW** always creates
level-50 bots. New higher-level bots receive suitable gear and training on
first login. Existing bots keep their levels and possessions. The alt interval
and total-roster cap control later level-1 additions to managed guilds.
The launcher gives a numeric size recommendation only after this PC records
stable samples at 500, 1,000, and 1,500 active bots.

## World Speed while away

With the server running, use the launcher's **World Speed** control to choose
1×, 2×, or 3×. Autonomous bots still travel, fight, recover, and earn their
normal per-event rewards in the live world. A connected game client makes the
server run at 1×; the chosen speed resumes five seconds after the last client
disconnects. The launcher shows the speed actually achieved if the PC cannot
keep up. Each server start selects 1× again.

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
| `/petpull [on\|off]` | Pet pull mode for your group and your squads (no argument toggles; it ends when you log out). While it is on, every pull starts with your pet's normal attack: the pet goes in alone, companions wait at camp, keep a heal-over-time and their pet buffs on it, and only take adds that are on or running at someone of the group; an Animist plants mushrooms in front of the group. If the pet gets hurt or swarmed, set it passive to bring it back: direct pet heals and tank peels wait until the pull is released. Companions open up once the pet is beside you, or at once if it drops below 45%, dies or you attack. The next pet attack starts the next pull. |
| `/stay [on\|off]` | Only in pet pull mode (no argument toggles). Your companions hold the spots they stand on instead of following you; tanks still meet adds at camp and walk back. An Animist keeps one main turret and as many damage mushrooms as the caps allow (10 around the grove) in front of the camp, toward your last pull, and replants them down to 10% power; a Mentalist keeps its heal-over-time on your pet, also out of combat. `/stay off`, `/petpull off`, `/passive`, logout or leaving the region end it. |
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
| `/companions reset [name]` | Recreate all active persistent companions beside you, or bring one roster member back by name, even if dead or benched. Saved progress and gear are retained. |
| `/companions squad <1-5> add <name>` | Move a companion into that companion-led squad (spawning it first if it is benched). The first companion in an empty squad leads it; its leader marches behind you at its own standoff distance (200-400 units, farther per squad number), and its members follow their own leader. |
| `/companions squad <1-5> remove <name>` | Take a companion out of that squad and bench it. |
| `/companions squad <1-5> lead <name>` | Make an existing squad member its new leader. |
| `/companions squad <1-5> disband` | Bench every member of that squad. |
| `/companions squad list` | Show every squad, its leader, and its members. |
| `/spawn` | Open the menu of valid companion bots to summon. |
| `/spawn X` | Summon a companion by class name from your realm instead of using the menu; useful for macros. |
| `/spawn Realm X` | Summon a named class from another realm, such as `/spawn Midgard Healer`. |
| `/raid 40` | Enable a 40-member companion raid. Use **before** `/spawn`. Requires level 50; the total includes you. |
| `/raid 80` | Enable an 80-member companion raid. Use **before** `/spawn`. Requires level 50; the total includes you. |
| `/aggressive` | Companions assist your attacks and defend the party. They break off and return if left far behind. |
| `/defensive` | Companions engage threats near you and return if left far behind. |
| `/passive` | Companions drop combat, recall their pets, and return to you without attacking. Animists take down all their mushrooms (main turret included) and plant none while passive; `/stay` ends. Choose another mode to resume fighting. |
| Companion Manager: Group orders | The first roster row sets these orders, shows each companion's effective stance, and offers Pull, Invite all, Bench all, and Grind. |
| Companion Manager: Realm abilities | Select a companion, open Training & Tactics, then scroll to Passive realm abilities. Each click buys one rank; Overview shows earned RP and unspent RA points. |

`/spawn 40` and `/spawn 80` are **not** the raid-size commands. Use `/raid` first.
These modes control your companions, not autonomous gamebots. All three modes recall a companion beyond 2100 units until it reaches 650 units from you.

## Camlann PvP commands

| Command | What it does |
|---|---|
| `/safety off` | Permanently turn off the under-level-10 PvP safety flag. Capitals, housing and portal-keep hubs remain sanctuaries; leveling towns and the Old Frontiers are dangerous. |
| `/gc form <guild name>` | Found your guild near a registrar, alone or as group leader. Other human group members confirm; owned companions in the group join automatically. Companions and recruited bots can join the guild and count toward its keep claims. |
| `/gc invite <bot or companion>` | Invite a targeted or named autonomous bot or your own active companion when you have the guild invite rank. Your companion joins immediately and displays the chosen guild emblem on their equipped cloak and shield. |
| `/companions guild leave <name>` | Remove one of your own saved companions from their guild, even if you have left it. Works for active and benched companions. |
| `/relics` | Inspect the current guild-owned keep and relic state. Relic bonuses belong to the carrying guild, not a realm. |

There is one full-PvP world: an ungrouped, unguilded or unallied player-shaped
actor can be hostile regardless of realm. `/level` is disabled, battleground
travel remains closed, and `/spawn <realm> <class>` is the cross-realm companion
choice.

For the complete advanced reference, see **ALL SERVER COMMANDS.txt** inside the
download. That file separates normal-player, GM and administrator registrations.
