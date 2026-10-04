# Companion Bots: `/spawn` and Persistent Roster

For the staged design and remaining work, see the [companion roadmap](COMPANION_ROADMAP.md).

This document records the behavior and persistence boundaries for owned
companions so they stay distinct from autonomous world bots and real players.

## Identity and lifetime

`/spawn` creates a `GameBot` with `IsTemporaryGroupHelper = true` and
`IsAutonomousWorldBot = false`. The helper is owned by the invoking
`GamePlayer`, joins that player's group, is fully equipped for the session, and
is removed with the group. It is not saved as an autonomous bot and does not
create a persistent character or database record.

The creation path is
`source/server/GameServer/commands/playercommands/TemporaryGroupCommands.cs`.
Keep the temporary-helper flag distinct from `IsAutonomousWorldBot`; the two
bot types intentionally have different persistence, reward, and tuning rules.

## Persistent roster

Bare `/companions` opens the Companion Manager window when the client has its
extension installed (see `docs/COMPANION_MANAGER_INTEGRATION.md`). Without it,
the server prints one line of command guidance. `/companions find <name or
class>` searches the window from the chat line. The explicit
`/companions list`, `/companions recruit <class>`,
`/companions invite <name>`, and `/companions bench <name>` manage generated,
persistent recruits. Class-only recruitment searches all realms, so commands
such as `/companions recruit Warden` and `/companions recruit Healer` work
without a realm argument. Use `/companions recruit <realm> <class>` when a class
name is shared by multiple realms. Type `/classes` for class names grouped by
realm. A character can recruit anywhere for free, starts each recruit at level
1, and can store up to 78 companions. `/companions list` shows names grouped by
realm without exposing internal IDs. Ownership is per character. Each recruit
has a stable ID, saved identity/build state, and an independent inventory
stored under a `playercompanion:` owner ID. Same-class recruits therefore
remain separate.

Generated recruits receive a new saved identity and personality. Authored
recruits are named individuals with written backgrounds and dialogue, each
available once per owning character. Both use the same persistent progression,
gear, and player controls. Temporary `/spawn` helpers are a separate system.

The Companion Manager roster marks generated recruits **Regular** and authored
recruits **Story** in both the list and the selected companion's detail heading.
On Overview, **[Delete]** opens a confirmation for that companion. Confirming
permanently removes the saved companion and all equipped and carried items,
including starter, earned, traded, and unclassified gear. Items never block
deletion; move anything you want to keep first. An active companion is
benched before deletion. Deleting a story companion makes that authored person
available for recruitment again.

An active, living recruit follows its owner through an accepted portal or
region teleport, including same-region moves. Only that owner's companion in
the current group relocates; ordinary group members keep their own position.

The roster lives in the additive `player_companions` table. Existing `bot_profiles`
are not converted. Active companions attempt to rejoin at owner login when group
space is available. Removing a companion, leaving the group, or disbanding saves
and benches that companion. `/spawn` still creates a temporary helper and has
not been redirected to the persistent roster.

The Stage 5 cast adds two authored people for each of the 39 Classic + SI
classes. Each has a stable key, name, eligible race and gender, size,
background, personality, and dialogue. Browse them with
`/companions cast [realm] [page]`; recruit each once per owner with
`/companions recruit authored <name> [build]`. Generated recruitment by class remains
available. New generated recruits receive a saved personality template. Existing
records keep their previous identity and class behavior. The current catalog
contains 118 plans across all 39 Classic + SI classes, with at least three
choices and a selectable default for every class. The original 33
`general-pve-v1-*` default IDs remain stable.

`/companions profile <name>` shows identity, background, preferred build, role,
stance, and a short line of dialogue. `/companions role <name> tank|healer|buffer|attacker|cc` saves a class-legal
party job. Choosing a build also sets its role. A companion in the Crowd
control role (Healer, Sorcerer, Bard, Mentalist, or Spiritmaster), or on a
Healer Tri-spec build, mezzes extra monsters in PvE once the group is fighting.
It never mezzes the group's or the owner's target. Companions in a player-led
group leave mezzed monsters alone while another enemy is left;
`/companions stance <name> aggressive|defensive|passive` saves an individual
engagement preference. Personality sets the first stance. Aggressive companions
assist attacks; defensive companions engage threats near the leader; passive
companions drop combat and return to formation without attacking. `/aggressive`,
`/defensive`, and `/passive` issue group orders for helpers and persistent
companions; `/companions group default` returns to individual stances. In every
mode, a companion more than 2100 units from its leader breaks off and returns
until it is within 650 units. A direct `/pull` cannot override passive or the
distance leash. Dialogue appears on recruit, invite, bench, and requested
profile views; it does not trigger during combat. Autonomous bots do not read
these preferences. See [Stage 5 decisions](COMPANION_STAGE5_DECISIONS.md).

Persistent Armsman, Hero, and Warrior companions use their learned Taunting
Shout on a frontal pack of at least two NPCs already attacking their group.
They skip a cone that would hit an idle NPC, a protected mezz, or an attackable
player. The usual single-target taunt and taunt style remain available when a
safe area shout is unavailable. Temporary helpers and autonomous gamebots do
not use this companion area-taunt decision.

The roster preserves the existing generated level-1 equipment as a one-time,
protected starter loadout; fresh recruits receive gear aligned with their
selected build. Switching an existing companion build only activates an
already-equipped compatible item. Owner-supplied gear and manual equipment
locks are preserved; incompatible or locked weapons need owner adjustment
before the companion can use that build's weapon line. Persistent inventory
is saved across benching and restart. Persistent recruits earn PvE progression while actively adventuring
with an eligible owner; benched companions do not gain catch-up XP. All 39
Classic + SI classes now have an enabled default whose lines and ranks are
checked against the local static class and skill data. New recruits use
automatic training by default; all pre-existing records stay manual until their
owner selects a build.
`/companions recruit <class> [build]` picks a build at recruitment; without one,
the class default build is used. Authored characters and
their once-per-owner recruitment rules are Stage 5.

## Companion squads and battlegroups

An owner may organize up to 5 companion-led squads besides his own group, so two
owners can share one battlegroup (`/bg`) while each keeps his own companions under
his own control. `/companions squad <1-5> add <name>` moves a companion into that
squad, spawning it first if it is benched; the first companion to join an empty
squad becomes its leader. `/companions squad <1-5> remove <name>` benches it and
returns it to the owner's own group next time it is invited. `/companions squad
<1-5> lead <name>` makes an existing squad member the new leader.
`/companions squad <1-5> disband` benches every member. `/companions squad list`
shows every squad, its leader, and its members. A companion's squad assignment
and leader flag are saved on its `PlayerCompanionRecord` and restored at the
owner's next login, independent of whether it is currently active.

A companion stays in the world while it is in the owner's own group or in one of
his squads; moving it between those groups (including promoting or replacing a
squad leader) never benches or deletes it. Every reward, follow, and order system
that used to require "in the owner's own group" (experience copy, personal gear
rolls, group orders such as `/aggressive`/`/defensive`/`/passive`, region/portal
follow, and the Companion Manager's Active tab) now resolves the owner directly,
so it applies the same way to squad companions.

A squad's leader marches behind the owner at its own standoff distance (200-400
units, farther out per squad number, so squads fan out rather than stack); its
members follow their own squad leader at the ordinary companion follow distance.
When a squad's leader is removed, benched, or logs out with the owner, its
remaining members promote a new leader automatically.

Squad members fight with the owner (task 44). A squad member's own Group is its
squad's, never the owner's, so `CompanionSquads.SharesOwnerForce`/`OwnerForceBots`
generalize every gate that used to compare Group equality against the owner's own
Group: `PlayerLedPullCoordinator.Available`/`Engage`, `CompanionPvpEngagement.Leader`/
`Observe` (the RvR assist train), and the owner's attack broadcast
(`BotBrain.NotifyNearbyGroupBots`/`OnGroupMemberAttacked`). In practice a
squad member assists the owner's PvE and RvR target exactly like a companion in
his own group, including `/petpull` holds, and defends its own squad, the owner,
and the owner's own group within the usual radius; squads also defend each other.
A squad heals its own members first and, once that squad is fine, also covers the
owner and his other squads; group heals still only reach the caster's own group.
Out of combat, a squad with nobody left to raise reaches into the owner's other
squads, or the owner himself, for a resurrector, using the same reservation
pattern as ordinary group rez so two squads cannot cast on the same corpse. A
squad member's single-target realm buff also reaches the owner when he is
missing it; group buffs stay scoped to the caster's own group. `/aggressive`,
`/defensive`, and `/passive` group orders already resolve the owner directly
(task 42) and apply to squads unchanged. The owner's own-group tank-contact wait
gate for a manual `/pull` (the ordered pull's tank-contact handshake) stays scoped
to his own group; squads assist immediately instead of waiting on that gate, the
same way healers and buffers already do when no tank is available.

Joining a battlegroup with `/bg` brings an owner's live squad companions along
automatically, and leaving takes them with him; `/bg who` lists them under their
owner. A battlegroup's chat, loot, and treasurer features remain human-only, as
before; a companion is never a battlegroup member of its own.

## Enchanter companion pets

Persistent Enchanter companions prefer the highest learned, castable
Underhill Ally summon across their selected builds. If no Underhill Ally
spell is available, they use the normal pet selection. An existing
alternate pet is replaced only while the owner and pet are idle.
Temporary `/spawn` helpers and autonomous world bots keep their
existing selection rules.

## Pet pull mode (`/petpull`)

`/petpull` switches a mode for the owner's whole force, his own group and his
companion squads (task 46; it replaced the one-pull command of task 39).
`/petpull` toggles, `/petpull on|off` sets it; the reply states the mode. The
mode lives on the logged-in character object, so it ends at logout and is not
saved. Temporary `/spawn` helpers in the group follow it too, because every
player-led bot resolves the owner (`PlayerGroupLeader`); autonomous world bots
never see it.

While the mode is on, the 1.65 pet-puller routine runs on every pull:

- **Start.** A pull starts when the player's own pet engages: an attack order
  (`ControlledMobBrain.OrderedAttackTarget`), the pet attacking, or the pet in
  combat. `PlayerLedPullCoordinator.LeaderEngaged` holds while the pull is on
  the pet, so the pet order no longer sends the companions in. While a released
  pull is still being killed, only an order onto a fresh monster that is not in
  combat (a chain pull) starts the next one. An order onto a monster that is
  already in combat (for example a `/pull` whose tank sent the pet in) and pets
  sent at enemy players (RvR had no pet pulls) start no pet pull.
- **Hold.** Companions do no real damage. Non-tanks keep a heal-over-time on the
  pet and heal the group; an Animist plants turrets at the camp front toward the
  pull. Attackers intercept only adds that are on, or running at, someone of the
  owner's force (never the pulling pet's own attackers); support companions do
  not go for adds that are still on their way. Tanks keep their ordinary peel of
  adds on the group.
- **Pet in danger.** Under 70 % health, or under 90 % with three or more live
  attackers (a fresh pack pull is the pet's job), healers heal the pet before the release and each tank taunts one attacker off
  it (an add first; the pet's own target only when nothing else is on it; no
  two tanks take the same one). The player gets one line when this starts.
- **Release.** The pull is at camp when the pet is within 400 units of the player
  and set passive (or never left camp); the group also opens at once when the
  pet drops below 45 % or dies, when the player attacks (any harmful player
  spell that lands counts, even a mez on an add), or after 60 s of contact. A pet that never makes contact within 30 s cancels only that pull.
  After the release every companion heals the pet; the pull ends once the fight
  has been quiet for 8 s, and the mode stays on.
- **Buffs.** While the mode is on, buffs that work on pets (strength,
  constitution, dexterity, quickness, damage add, shields, ablative, resists,
  heal-over-time) go to the pet before the group.

Code: `CompanionPetPull` (mode and pull state), `PlayerLedPullCoordinator`
(engage guard), `BotBrain` (hold branch, pet-danger taunt and heal).

## Death recovery and raids

Persistent companions use the existing GameBot corpse-recovery behavior. Their
corpse and group are retained while dead; recovery follows the general GameBot
timers (20 seconds when no viable resurrector is present, 90 seconds when one
is present), then the existing bind/release behavior. They do not use the
`/spawn` helper's release-to-owner path. Death recovery is runtime GameBot state;
this policy adds no companion death fields or save migration. Use `/companions reset`
to recreate every active roster companion beside you, or `/companions reset <name>`
for one. Reset saves the companion before recreating them, including a dead actor
that is still active in the roster.

`/raid 40` and `/raid 80` remain raids for the owner's temporary `/spawn`
helpers. The owner counts toward the selected capacity. Persistent companions
and autonomous world bots are not eligible raid helpers; invite persistent
companions to an ordinary party instead. Offline policy coverage does not replace
the owner-run client check listed in the [Stage 6 acceptance brief](COMPANION_STAGE6_ACCEPTANCE.md).

## Temporary `/spawn` XP contract

The current contract is:

| Area | Temporary companion behavior |
| --- | --- |
| Eligible damage | Direct damage dealt by the companion earns companion XP. |
| Controlled pets | Damage from a pet controlled by a companion keeps the existing human-owner attribution. |
| Human owner XP | The owner still receives the same full owner/group credit as before. |
| Party divisor | A companion does not increase the real-player group count or reduce the owner's XP share. |
| XP rate | Companion XP uses `ServerProperties.Properties.XP_RATE`, the normal player rate. Persistent autonomous bots use `BOT_XP_RATE`. |
| Bonuses and cap | The normal bot award path still applies damage percentage, XP cap, camp, group, guild, and BAF calculations. |
| Realm points | Companions do not receive realm points. |
| Loot | Companions do not become loot owners or receive extra loot rolls. |
| Persistence | XP and levels exist only for the current helper session. |

The temporary-helper kill-credit flow is in
`source/server/GameServer/serverrules/AbstractServerRules.cs`:

1. `ProcessXpGainers` keeps a direct temporary helper in the bot contribution
   map, while also crediting its damage to the human owner with no additional
   group-member count.
2. The helper is therefore eligible for `AwardBotOnNpcKill`, but remains out of
   the real-player XP divisor and loot-owner selection.
3. `AwardBotOnNpcKill` grants the helper XP but records autonomous objective
   progress only for persistent autonomous bots.

`ResolveNpcRewardOwner` preserves direct temporary helpers and persistent
companions while resolving controlled pets to their appropriate reward actor.
If that policy changes, update the attribution tests in
`source/server/Tests/UnitTests/UT_AutonomousLootFlow.cs` at the same time.

## Leveling and tuning points

`GameBot.GainExperience` handles autonomous bots, temporary helpers, and
persistent player companions. Keep the `IsAutonomousWorldBot` branches separate:
autonomous bots use `BOT_XP_RATE`, temporary helpers use `XP_RATE` and spend
points on level-up, and persistent companions use `XP_RATE` with separate
automatic/manual training metadata. Automatic plans apply each gained level,
including multi-level gains; manual companions hold earned points. Persistent
companions save XP progress through a coalescing record-only queue; their
inventory is not rewritten for each kill.
Level gains refresh the group window so the displayed companion level matches
the level used by combat and training immediately.

Persistent companions receive the smaller of the owner's base NPC award and
their own-level XP cap, multiplied by `XP_RATE`. A companion at least five
levels behind its owner gets a 1.5× catch-up boost after the rate. Active
companions must also be in range and pass the grey-con
check; support companions qualify without dealing damage. A damage-dealing
companion can also receive a separate damage-based NPC award when its eligible
owner has no player damage share. Companion and controlled-pet damage stays out
of player damage percentages, group divisors, and loot-owner selection. The
companion's XP is clipped at the owner's current absolute XP total, preserving
any higher saved companion XP. Only NPC kill rewards are accepted for
companion XP, so a companion still gains no XP from a PvP kill. RvR Realm
Points are a separate, additive reward: see
[PvP kill rewards for persistent companions](#pvp-kill-rewards-for-persistent-companions).

The `/companions train` command accepts only a specialization from the
companion's class career and applies normal point costs and level limits. It
requires a compatible trainer unless `ALLOW_TRAIN_ANYWHERE` is enabled. The
menu exposes the same training, mode, plan, and respec services.
`/companions respec` requires owner full-skill respec eligibility, honors
`FREE_RESPEC`, confirms before resetting, and resets only the selected
companion. The 118 enabled builds carry stable versioned IDs. Before applying
a build, the catalog checks its specialization ranks against local career and
skill tables and checks the class point multiplier. A changed or missing plan
never changes saved allocations; earned points stay manual until a valid plan
is explicitly selected. `/companions build <name>` lists a companion's builds;
`/companions build <name> <build>` switches builds and automatic training. The
switch is free, needs no trainer or respec eligibility, resets that companion's
specializations, and retrains the new build to its current level. See
[Build choice (M1a)](COMPANION_BUILD_RESEARCH.md#build-choice-m1a).

The pre-application data check confirms each plan's class multiplier,
specialization careers, and available ranked skills. It does not by itself
make the combat profile select every learned weapon mode, style, spell, or pet
behavior. The selected-build skill-use and caster AoE plan is in
[COMPANION_BUILD_AOE_PLAN.md](COMPANION_BUILD_AOE_PLAN.md); source implementation is complete in 0.96.0; installation and real-client
verification remain pending in [TASKS.md](TASKS.md). Necromancer plans use Deathsight
and Painworking, not a Death Servant farming allocation. Source paths for
Death Servant summon and commands exist, but real-client behavior is
unverified. Harmful servant `PetSpell` wrappers with a zero range
`ENEMY`-target area damage payload use the same saved ranged-AoE threshold
and safety checks centered on the servant. This is separate from
ordinary ranged AoE, which excludes PBAoE and cones, and it does not
make the current Necromancer builds Death Servant farming builds.

The companion manager also offers a separate saved Ranged AoE threshold:
Off, 2+ through 8+, or the 3+ default. It switches learned ranged area damage
after that many eligible enemies are in the blast. Committed mobs and hostile
guards from the selected hostile keep count; the policy refuses a cast that
would hit idle bystanders, unrelated guards, players, or protected mezzes.
Ordinary ranged AoE excludes PBAoE and cones. Necromancer servant area
wrappers use the same threshold centered on the servant. See the linked plan
for source status and real-client acceptance.

## PvP kill rewards for persistent companions

Persistent `/companions` members still receive no XP from a PvP kill, even
if they are grouped with the player, deal damage, or land the killing blow.
`GameBot.GainExperience` accepts a persistent companion's NPC rewards only;
a companion's `eXPSource.Player` award from the paths below is always a
no-op. Persistent companions do earn Realm Points from a PvP kill, through
the same `GameBot.GainRealmPoints` path, reward formula and repeat-kill
window used for autonomous world bots, against both human and
autonomous-bot victims. A companion's realm rank is derived the same way an
autonomous bot's is (not tracked separately), and a companion never becomes
a PvP kill's loot owner; its personal gear still comes only from
`PlayerCompanionGearRewards`. Temporary `/spawn` helpers remain excluded
from both PvP XP and Realm Points.

The owner can earn PvP XP and Realm Points from qualifying personal damage or
damage by a controlled pet credited to that player. Damage dealt by a
persistent companion stays in the encounter's damage total but is not
transferred to the owner as kill credit; the companion earns its own Realm
Points (saved additively in `PlayerCompanionRecord.RealmPoints`) instead.
The owner can spend the resulting realm ability points in the Companion
Manager's Training & Tactics pane. Passive purchases use the same class list
and rank costs as player training and persist separately from career skills;
timed active abilities are not offered because companion AI cannot fire them.
Consequently, a companion's damage can reduce the owner's percentage of a
shared PvP reward while the companion earns its own share. Group membership
alone does not award PvP progression to a companion or to an owner who has no
credited contribution. A player can therefore level from PvP kills while
companions retain their levels until they earn eligible PvE XP or a PvP kill
they contributed damage to. Individual reward amounts still depend on victim
eligibility, level, damage share, caps, and the configured Realm Point rate.

## Personal gear and inventory

Each eligible owner's NPC kill selects one eligible active companion to roll
for a personal PvE item at `min(100%, 25% × XP_RATE)`. PvP reward-eligible deaths
attempt one item per eligible companion, including support companions in the
owner's group or squads. Generation skips a reward if it cannot produce gear
matching the chosen category and weapon policy. Owner XP caps and maximum level
do not suppress the gear roll. Player loot and autonomous-bot handling remain
separate.

Personal items use the companion's existing inventory namespace and are marked
as earned, starter, or player-supplied in additive companion metadata. Starter
items stay with their original companion; player-supplied items remain
recoverable; legacy items without provenance stay protected. Manual equipment
locks the affected slot. Automatic upgrades require a strictly greater
class-legal score, respect hand conflicts and locks, and keep displaced items in
the backpack. When a loot reward needs space, up to 16 positive-value earned
backpack items may be sold, prioritizing unusable gear and then the lowest keep
value. Transfers and upgrades free only the space they need. Equipped, kept,
starter, supplied, and unclassified items are excluded. Sale proceeds use normal
merchant appraisal and go to the owner.

Transfers require an active nearby companion and idle inventory state. Only
persisted, tradable, droppable ordinary items can move; quest, relic, siege, and
other restricted items are rejected. Full-bag transfers, upgrades, and rewards
save item ownership, companion metadata, sale removal, and owner proceeds in one
database transaction. The menu validates each choice against current owner,
companion, item, and slot. The companion backpack can also open in the client's
native external-inventory window. This view keeps the player's inventory
separate and routes drag/drop transfers through the same protected companion
transaction; worn equipment remains in the compact popup menu. The owner must
verify the native window's appearance and drag behavior in the real client.

The main tuning points are:

- `XP_RATE` and `BOT_XP_RATE` in server properties control the two bot classes'
  base multipliers.
- `AwardBotOnNpcKill` controls damage-share, level-cap, and bonus calculations.
- `ProcessXpGainers` controls owner credit, party divisor behavior, and loot
  ownership.
- The temporary-helper constructor path controls the companion's starting
  level, equipment, and initial XP floor.

When adjusting companion rewards, test direct companion damage separately from
companion-controlled pets, and verify that autonomous bots, real players,
loot, realm points, and saved autonomous progress remain unchanged unless the
new behavior explicitly intends otherwise.
