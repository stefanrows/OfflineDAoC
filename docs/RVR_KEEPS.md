# RvR mode: keeps by realm (package R3a)

Package R3a of [RVR_MODE_PLAN.md](RVR_MODE_PLAN.md). Owner decisions 12, 13
and 17 in [RVR_MODE_ANALYSIS.md](RVR_MODE_ANALYSIS.md); spec
`docs/superpowers/specs/2026-10-24-rvr-mode-design.md` section 4. Status:
implemented in source, real-client check pending.

Everything below applies to the RvR world (save marker `Realm-1`). The
Camlann world (`Camlann-1`) keeps its guild keep campaign unchanged; the last
section compares the two.

## Start state

- Written by the world reset (R5a, `KeepRelicReset`): portal keeps and relic
  keeps carry their original realm, every other keep is realm-less and
  unclaimed, no lord is down.
- At startup (`PvpKeepCampaign.Initialize`) a realm-less Old Frontiers or
  battleground campaign keep goes to the **Frontier Wardens** garrison, as in
  Camlann: level 1, neutral guards (realm None), hostile to every realm.
- A keep of a realm (portal, relic, or captured and not yet claimed) gets no
  garrison. A portal keep row without a realm takes its original realm, so its
  guards and its hostility agree.

## Capture

- When a keep lord falls, `RvrServerRules.ResetKeep` sends a campaign keep to
  `PvpKeepCampaign.CaptureForRealm`. The keep flips at once to the realm the
  killer fights for. Pets, companions and `/spawn` helpers count as their
  owner's realm; players and world bots alike capture.
- The flip runs the upstream Normal reset (`AbstractGameKeep.Reset`): the
  previous holder (a guild claim or the Wardens) is released, walls are
  repaired, siege weapons on hookpoints die, gates close as the new realm's,
  banners change, and the guards die and respawn as the new realm's guards.
  The lord stands again after one second; the other guards after
  `guard_respawn` minutes (15 by default), as upstream.
- The keep then stands at **level 1 without a claim**, and the Keep Claim
  Steward waits beside the lord. In the save, `LordDefeated` means "awaiting a
  guild claim" in this world; the lord is not held down, so another realm can
  take the keep back at any time.
- The capture is saved in the keep row (realm, no claimed guild, level 1) and
  never returns to the Wardens after a restart.
- Capture reward: 1,500 realm points to the living members of the killer's
  group and battlegroup who fight for the capturing realm and stand in the
  keep area, once per keep in 30 minutes. Battleground campaign keeps keep
  their own bracket-scaled objective rewards. The claim pays nothing more.
- No capture: a realm-less killer (a mob, a neutral encounter bot) or the
  keep's own realm. The lord simply stands again.
- Relic keeps change realm like any campaign keep but are never claimed and
  get no steward; relics themselves are R3b.
- **Portal keeps never change hands**, as the 1.65 border keeps: a portal-keep
  lord kill only counts the statistic. Other keeps outside the campaign reset
  to the killer's realm as upstream.

Log lines: `KEEP_REALM_CAPTURED keep= name= from= previousOwner= to= by= bot=
level=` and `KEEP_REALM_CAPTURE_SKIPPED`.

## Claim

- Only a guild of the keep's realm may claim it, at the steward, as today:
  any ranked member of a solo player guild, or a bot guild of that realm.
  `CheckForClaim` refuses another realm with "<keep> belongs to <realm>; only
  a guild of that realm can claim it." A Warden or neutral keep must be
  captured first.
- The claimer must fight for the keep's realm; the guild must be of that
  realm or realm-less. The RvR world seeds 15 single-realm bot guilds per
  realm at first start (R5a, [RVR_WORLDS.md](RVR_WORLDS.md)); a realm-less
  guild claims through its member's realm.
- No claim while the keep is in combat (the upstream rule). Camlann lets a
  defeated lord's steward claim through siege damage; in the RvR world the new
  lord already stands, so a mid-siege claim would repair a keep under attack.
  Bots wait the same way.
- The steward stands 200 units in front of the standing lord. `/ck`,
  `/battleground` and the OPEN BG window read "captured, awaiting claim"
  ("Midgard holds it, awaiting a claim") instead of the Camlann "lord
  defeated, unclaimed".
- A claim raises the level as today: start level 1, then the keep-level
  ladder (level 10 after 32 gameplay hours).
- Bot claims use the existing flow: after a capture the capturing realm's
  bot guilds see the keep as "awaiting claim" and send a claimant to the
  steward. Bots of other realms see it as an enemy keep to assault at its
  gates instead.
- The open BG director treats the keep as "lord down" only while the lord
  really lies dead, not for the whole wait for a claim, so the raid does not
  count as a friend of a keep its realm just lost.
- A guild that releases its keep leaves it with its realm, and the steward
  returns for another claim of that realm.

## Guards and hostility

`KeepManager.IsEnemy` answers by realm for every keep, portal keeps included:

| Keep | Own realm | Other realms |
|---|---|---|
| Frontier Wardens (neutral) | enemy | enemy |
| Realm-less, no garrison | enemy | enemy |
| Realm keep, unclaimed or awaiting a claim | friend | enemy |
| Realm keep claimed by a guild | friend | enemy |
| Portal keep (border keep, battleground portal keep, open BG portal keep) | friend | enemy |

- Guards take their keep's realm when they spawn, so the client sees their
  true realm (the keep-guard realm spoof of R1b is gone). Warden guards are
  realm-less.
- Guards of a keep awaiting a claim pay their 25 realm points, as any
  defended realm keep.
- Guild alliances and `pvp_unclaimed_keeps_enemy` play no part.
- World bots never siege their own realm's keeps; an own-realm keep is a bot
  objective only while it awaits a claim the bot's guild may make.

## Darkness Falls

- Before R3a both worlds let every realm in: the entrance rule applied only on
  GameType Normal, and both worlds run GameType PvP.
- The RvR world now takes the Normal branch (`WorldMode.RulesBranch`): only
  the owner enters, and the previous owner for a 15-minute grace period.
  `allow_all_realms_df` still opens it for all.
- The owner is the realm holding the most Old Frontiers keeps; towers, portal
  keeps, relic keeps and battleground keeps do not count, as upstream. The
  owner keeps Darkness Falls until another realm holds strictly more; of two
  realms tied above the owner, the one that just took a keep wins. The
  check runs on every capture (`DFEnterJumpPoint.NextOwner`).
- A fresh RvR world starts with every keep neutral, so nobody owns Darkness
  Falls until the first realm captures a keep. Bots respect the same rule.
- The owner is not saved: at startup it is seeded from the keep counts with
  the same rule (a tie goes to the first realm in the order Albion, Midgard,
  Hibernia), and the grace period starts fresh.

## Per world

| | Camlann (`Camlann-1`) | RvR (`Realm-1`) |
|---|---|---|
| Unheld keep at start | Frontier Wardens | Wardens only if realm-less; realm keeps keep their realm |
| Lord falls | Keep turns neutral, lord stays down, waits for any guild | Keep flips to the killer's realm at once, lord stands again |
| Claim | Any guild with claim rank | A guild of the keep's realm only |
| Level without claim | 1 | 1 |
| Capture reward | At the claim, claiming guild's group | At the capture, captor's group and battlegroup of that realm |
| Portal keeps | Never hostile, never captured | Hostile to other realms, never captured |
| Darkness Falls | Open to all | Owner realm (most keeps), grace for the previous owner |

## Open for later packages

- **R3b:** relic pads and the realm relic bonus. A claimed keep in the RvR
  world still creates the Camlann guild relic pad.
- **R4a:** bot login per realm and the remaining guild work on top of the
  R5a seeding.
- **R4b:** realm armies, realm keep defence (only guild-claimed keeps call
  their defenders today), and the open BG roster's keep owner.
- **Real client:** guard realm colours and names, the steward beside a
  standing lord, the capture and Darkness Falls messages.
