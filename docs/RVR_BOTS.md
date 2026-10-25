# Bots in the RvR world

Packages R4a-R4c of [RVR_MODE_PLAN.md](RVR_MODE_PLAN.md); spec
`docs/superpowers/specs/2026-10-24-rvr-mode-design.md` section 5. Owner rule
(2026-10-25 00:25): every bot extension of the fork carries over into the RvR
world, realm-based; only the friend/enemy key changes from guild/crew to
realm. Every rule below is gated on the world marker (`WorldMode.IsRvr`); the
Camlann world behaves exactly as before.

## Guilds and login (R4a)

### Guild identity per realm

The crew manager (`AutonomousCrewManager.Reconcile`) seeds 15 single-realm
guilds per realm at startup, 45 in total (R5a,
[RVR_WORLDS.md](RVR_WORLDS.md)). R4a gives them a realm identity:

- **Realm names.** `AutonomousRealmGuildNames` holds eight names per charter
  and realm in the style of 2003 guilds: Arthurian orders and village
  fellowships in Albion ("Lions of Albion", "Wardens of Benowyc", "Ye Olde
  Alehouse"), Norse warbands and mead halls in Midgard ("Valhalla Bound",
  "Shieldwall of Bledmeer", "the dwarf bar"), Gaelic clans and fianna in
  Hibernia ("Clan Cuchulain", "Dun Bolg Defenders", "Lurikeen Social Club").
  Siege names point at an enemy frontier (Albion's "Emain Breakers",
  Hibernia's "Odins Gate Breakers").
  A new guild takes the first free name of its realm and charter; a number
  is added only when all eight are taken. Names use letters and spaces only,
  as the player `/gc form` rule requires.
- **No "Camlann Crew" prefix.** A guild being created carries the temporary
  marker `Realm Crew <ordinal>`. It makes an interrupted create discoverable,
  and the same reconcile renames the guild before any bot logs in.
- **Charter mix per realm.** R5a drew the charter from the global ordinal
  (0-44), so the realms got uneven mixes and Midgard had no Keep guild.
  `AutonomousGuildSeeding.PlanCharters` now plans the fifteen slots of a realm
  pool from the owner's `bot-goals.json` mix. Each slot is weighted by its
  guild size (1:2:4) and goes to the charter furthest below its share of the
  weight planned so far. Every realm uses the same plan. A realm with fewer
  than fifteen guilds uses the start of the plan, and a slot keeps its
  charter when the realm grows. With the default mix (Camlann 2003 preset)
  every realm gets this plan:

  | Charter | Guilds | Size weight of 35 | Share asked by the mix |
  |---|---|---|---|
  | Leveling | 5 | 10 | 10.4 |
  | RvR | 4 | 11 | 8.8 |
  | Hunting | 2 | 8 | 7.1 |
  | Social | 3 | 4 | 5.2 |
  | Keep | 1 | 2 | 3.6 |

- **Guilds seeded before R4a.** A managed RvR guild whose name is not a realm
  name (an R5a-era Camlann-style name) adopts its slot's charter and gets a
  realm name once. Keep claims follow the rename. Its members keep the player
  types they already have. A guild that already has a realm name keeps its
  charter, so a later change of the mix never renames a guild.
- **Guild realm.** Every RvR guild is created with its realm (R5a).

### No guild crosses realms

| Path | RvR rule |
|---|---|
| Seeding and consolidation | One pool per realm; a bot is only offered guilds of its realm (R5a) |
| Empty obsolete guild | Its keep claims and alliance pass to a guild of its own realm. Only when that realm has no guild left does it fall back to any survivor, so the guild can still be removed |
| Alt trickle | The new level-one alt joins the realm with the fewest bots, in a guild of that realm, with that realm |
| Player invites a bot | `/guild` checks the inviter's realm (R1a); `TryAcceptGuildInvite` also checks the guild's realm, staff invites included |
| Login | A bot whose record points at another realm's guild logs in without a guild |

Companions need nothing new: they come from the owner's realm (R2) and join
the owner's guild through the companion path.

### Realm login balancer

The Camlann world logs in the whole roster and balances by crew. The RvR
world puts the realm login balancer in front of it:

1. **Targets.** Each realm's online target is the server property
   `rvr_realm_active_target` (category `autonomous_population`, default 600,
   owner decision 9), capped by that realm's non-retired roster. 0 means
   every bot of each realm. The controller rereads it with the roster, about
   every ten seconds, so a change takes effect without a restart.
2. **Ramp.** The startup ramp (33 % in five minutes, the rest by
   `startup_ramp_minutes`) runs per realm on its own target. The logins still
   owed are the sum of each realm's deficit below its ramped target, so a
   realm above its target never hides another realm's gap.
3. **Realm pick.** Each login goes to the realm with the fewest online and
   pending roster bots among the realms below their ramped target that still
   have a bot to log in, ties in Albion, Midgard, Hibernia order (upstream
   rule). Companions and `/spawn` helpers do not count.
4. **Bot pick.** Within that realm the crew balancer chooses as before: the
   least-loaded guild, then a bot that joins an existing cohort (same guild,
   region and level band), then a missing healer or tank.

- **Shortfall.** A realm with fewer bots than the target logs in all it has.
  The other realms do not fill its gap, so the realms stay even rather than
  the total. The log line `RVR_REALM_POPULATION` shows the targets and any
  shortfall whenever they change, as a warning when a realm is short.
- **Lowering the target** stops further logins for a realm above it. Nobody
  is logged out; the population controller never parks live bots.
- **Growing roster.** The alt trickle adds a bot every 72 hours up to its cap.
  The realm target keeps the online count per realm at 600 while the roster
  grows.

### Grudges and banter by realm

- **Grudges.** `AutonomousGuildGrudgeMemory` keys a grudge by the victim's
  realm in the RvR world (`realm:1` Albion, `realm:2` Midgard, `realm:3`
  Hibernia, stored in the `GuildId` column of `offline_guild_grudges`). A
  killer of one Albion bot is a known enemy to every Albion bot for three
  hours. One realm remembers its 48 latest killers (Camlann: 16 per guild).
  The rules of what is worth a grudge are unchanged.
- **KOS call.** The guild-chat KOS call after a grudge kill drops "Watch for
  the crew" in the RvR world: "KOS Ked, last seen in Emain Macha. Pass it on;
  watch your backs out there."
- **Unchanged.** The realm event banter (raid and siege reminders, outcomes)
  already addresses the realm. The open chat lines use "crew" as player slang
  for a small group and stay. `AutonomousGuildEncounterMemory`, which tunes
  how bold a guild is against another guild over one hour, stays guild
  against guild: an enemy guild is always another realm in this world, and a
  realm-wide tally of about 600 bots would swing a whole realm's appetite
  after a handful of deaths.

### Open points for R4b

- **Revenge hunts (must fix in R4b).** A realm-wide grudge makes every RvR
  group of the realm a revenge hunter: the world bot controller picks the
  nearest reachable grudge target before any objective. Only the existing
  gate (a revenge trip needs two thirds of the lost group size) limits it.
  R4b must cap revenge trips (only the victim's guild, or at most N groups
  per target) and measure the share of `grudge` picks in the `LogRoamPick`
  lines of a live run.
- **Armies.** Armies, siege slots, muster and defence recall are still keyed by
  guild (`GuildArmy`, `SiegeSideKey`); R4b makes them realm armies.
- **Launcher.** The world reset does not write `rvr_realm_active_target`; the
  server creates it with its default 600 on first start.

### Verification without a server

`UT_RvrBotGuildsAndLogin` (both worlds): realm names, charter plan per preset,
the same charter sequence per realm, a Keep guild per realm with the default
mix, Camlann charters unchanged, no cross-realm join, alt trickle realm,
obsolete-guild target, realm targets, even ramp, shortfall, a realm above its
target, the property default, grudge owner keys and the KOS line. The
existing seeding, crew policy and realm balancer tests stay green.

Not checked: a live start on an RvR save (45 guild names in game, login
ramp per realm in the log), and the in-game KOS call.

## Realm armies (R4b)

Not started.

## Upstream siege flow (R4c)

Not started.
