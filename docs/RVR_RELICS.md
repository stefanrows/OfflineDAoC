# RvR mode: relics by realm (package R3b)

Package R3b of [RVR_MODE_PLAN.md](RVR_MODE_PLAN.md). Owner decisions 7, 12
and 17 in [RVR_MODE_ANALYSIS.md](RVR_MODE_ANALYSIS.md) (relics on realm pads
with a realm-wide bonus, relic sieges by bot armies on, 1.65 as the reference
while RvR may follow the newer upstream); spec
`docs/superpowers/specs/2026-10-24-rvr-mode-design.md` section 4.3. Status:
implemented in source, real-client check pending.

Everything below applies to the RvR world (save marker `Realm-1`). The
Camlann world (`Camlann-1`) keeps its guild relics unchanged (CAMLANN.md
decision 3); the last section compares the two.

## Start state

- The world reset (R5a, `KeepRelicReset`) leaves all six relics on their home
  pads in the six relic temples, each owned by its realm, and the six relic
  keeps with their realm. Every other Old Frontiers keep starts neutral
  (Frontier Wardens).
- No guild keep mounts exist in this world: a claimed keep never creates the
  Camlann guild relic pad (`AbstractGameKeep.EnsureRelicPad`).

## When a relic can be taken (the exact condition)

A relic mounted on a temple pad of realm R and type T can be lifted by a
fighter of realm X when all of these hold:

1. **X is another playable realm than R** (a mob or a realm-less encounter bot
   never lifts a relic; nobody lifts a relic from its own realm's pad).
2. **The temple's first milegate has fallen:** every keep of the temple's
   first milegate chain (table below) is held by a playable realm other than
   R. The holders may be different enemy realms. A neutral keep (Frontier
   Wardens, or no realm) counts as not taken, so a fresh world starts with
   every temple sealed. Retaking one chain keep seals the temple again.
3. **X holds its own relic of type T** (mounted on one of X's pads). A realm
   that wins back its own relic is exempt. This is the upstream rule ("You
   need to capture your realm's strength relic first").
4. **The pad crowd** of the upstream pickup: at least `rvr_relic_players_required_on_pad`
   (8 by default, upstream 0.38c's value; port S3. The Camlann world keeps
   `relic_players_required_on_pad`, 16) living, visible enemies of R within 500 units of the pad,
   players and player-shaped bots alike. Staff accounts skip this check.

A dropped relic on the ground is free for anyone, as upstream.

The second 1.65 milegate (a fourth keep) only opened another route to the
same temple, so it does not seal the relic. The temple's own walls, gates and
guards still have to be beaten on the ground; the milegate rule is what keeps
a relic out of reach while its realm still holds its frontier.

| Temple (relic keep) | Relic | First milegate (all three in enemy hands) | Second milegate |
|---|---|---|---|
| Castle Excalibur (57) | Scabbard of Excalibur, strength | Caer Benowyc, Caer Berkstead, Caer Boldiam | Caer Renaris |
| Castle Myrddin (58) | Merlin's Staff, magic | Caer Benowyc, Caer Erasleigh, Caer Sursbrooke | Caer Hurbury |
| Mjollner Faste (198) | Thor's Hammer, strength | Bledmeer, Blendrake, Hlidskialf Faste | Fensalir Faste |
| Grallarhorn Faste (82) | Horn of Valhalla, magic | Bledmeer, Nottmoor, Glenlock Faste | Arvakr Faste |
| Dun Lamfhota (111) | Lug's Spear of Lightning, strength | Dun Crauchon, Dun Bolg, Dun Da Behnn | Dun Ailinne |
| Dun Dagda (110) | Cauldron of Dagda, magic | Dun Crauchon, Dun Crimthain, Dun nGed | Dun Scathaig |

Source of the chains: the 1.65 Camelot Herald relic guide as kept in
`RelicGateMgr.cs`, keyed to the temples by geography and by the stock
relic-temple guard table (`RelicGuardsOnKeepTaken.cs`). In Albion the Herald's
"power" and "strength" wall names are swapped against this world's pads:
Caer Renaris stands next to Castle Excalibur, which holds the Scabbard (a
strength relic), and Caer Hurbury next to Castle Myrddin with Merlin's Staff.
The table follows the geography. A temple without chain data (another map)
falls back to the upstream rule: no milegate.

## Carrying and mounting

- A carrier brings the relic to its own realm's temple pad of the same type:
  strength to strength, magic to magic (`GameRelicPad.CanReceiveRelic`, the
  upstream realm pad rule). A pad holds up to three relics.
- Mounting turns the relic into the carrier realm's relic at once; a realm
  that wins back its own relic simply mounts it at home again.
- A dropped relic returns to the pad it was taken from after
  `Relic_Return_Time` (20 minutes), as upstream.

## Realm-wide bonus

- Upstream values: every enemy relic of a type on a realm's pads adds
  `relic_owning_bonus` per cent (10 by default), only while the realm also
  holds its own relic of that type. Strength relics raise melee damage; magic
  relics raise spell damage, heals and archery (the existing callers of
  `RelicMgr.GetRelicBonusModifier`).
- It reaches every fighter of the realm: players, their pets, and in this
  world the realm's world bots, companions and `/spawn` helpers and their pets
  (`GameBot.BenefitsFromRelics`, `GameNPC.BenefitsFromRelics`). It ends the moment the relic leaves the pad.

## Relic guards

- The stock relic-temple guards (`GuardFighterRK`, "Renaris Knight",
  "Fensalir Jarl", ...) run again in this world (they stay off in Camlann,
  bug 146). As upstream, each frontier keep its own realm holds adds its pair
  of guards at the temples, and the count shrinks by 25 per cent per relic
  above two that the realm holds.
- A guard stands only while its temple is its realm's; a realm-less or
  captured temple never spawns the level-66 "Renegade" guards of bug 146.
  Guards take the temple's realm, are friends of that realm and attack the
  other realms (keep hostility by realm, R1b/R3a). The bug 146 leash keeps
  them inside.

## Examine text

- "It is owned by your realm" or "It is owned by <realm>", as upstream
  (Camlann still names the holding guild).
- An enemy also reads "Its milegate still stands." or "Its milegate has
  fallen; the temple stands open."
- A refused pickup names the reason, for example "Its milegate still stands
  until Caer Berkstead is in enemy hands."

## Bots (autonomous RvR layer)

- **Target selection by realm:** an enemy relic keep becomes a raid objective
  for a bot army only when `RelicMgr.IsRelicRaidTarget` says so: the temple
  belongs to another realm, its milegate has fallen and it holds a relic the
  bot's realm may lift. The objective then enters the existing relic-keep
  assault path of the event layer (`Intent.AssaultRelicKeep`, relic muster
  thresholds 170 or 48 at the deadline, relic cap 192), which was dead before
  because relic keeps were never "claimable".
- **Capture and return:** a bot that lifts a relic carries it to its realm's
  temple pad of the relic's type (`RelicMgr.FindDeliveryPad`) and mounts it
  there; escorts and interceptors keep using the realm-shaped relic-carrier
  events. Dropped relics are recovered for the realm.
- **Left to R4b:** the march itself. Armies are still keyed by guild
  (`MayOpenSiegeLocked` per guild, `IsWholeGuildWarband` in `ChooseIntent`),
  the relic assault chance is the old low weight, the relic cap is 192 (spec:
  240 in RvR), and the muster point is not yet the realm's portal outpost.
  The carrier's route home runs from the enemy frontier region into its own
  and is not verified live; R4b checks it.

## Per world

| | Camlann (`Camlann-1`) | RvR (`Realm-1`) |
|---|---|---|
| Where relics mount | The carrying guild's claimed keep | The carrier realm's temple pad of the type |
| Who may lift a mounted relic | A guild with a claimed keep, after the lord or shrine guards fall | An enemy realm behind a fallen milegate, holding its own relic of the type |
| Bonus | The holding guild, uncapped | The whole realm, upstream rule |
| Relic-temple guards | Off (bug 146) | On, of the temple's realm |
| Bot relic raids | Off (decision 10) | On, by realm |
| Examine | Holding guild | Owning realm and milegate state |

## Tests

`UT_RvrRelics` (50 cases): the six milegate chains, fresh world sealed,
opening and resealing by keep state, Wardens keeps count for nobody, pickup
verdicts, the bonus rule, delivery to the right pad, the realm-wide bonus for
players and bots ending on loss, raid targets, guard switch and guard realm,
guard hostility by realm, no guild mount in RvR, Camlann answers unchanged.

## Open for later packages

- **Relic keeps are never captured** (team-lead decision 2026-10-25):
  `PvpKeepCampaign.CaptureForRealm` skips the six relic temples
  (`KEEP_REALM_CAPTURE_SKIPPED ... reason=relic_temple`), and a realm-less
  relic keep row, or one of another realm (a temple captured under 0.337.0),
  takes its original realm back at start (`RelicKeepStartRealm`). Their
  pads, gates and temple guards stay the temple realm's. Not handled: a relic
  keep row that already carries the Frontier Wardens claim (only a save booted
  on pre-R3a code); a fresh R5a world has none.
- **R4b:** realm armies, the realm relic cap 240, the march to an open temple
  and the carrier's cross-region route home.
- **Real client:** pickup refusals, the milegate examine line, the relic
  bonus in the client's realm window, relic guards' colours.
