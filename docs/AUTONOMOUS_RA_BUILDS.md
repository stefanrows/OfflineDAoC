# Autonomous world bot realm ability builds

Autonomous world GameBots follow one fixed passive realm ability path per class.
They spend the same realm ability point pool as an ordinary player in this
fork's Camlann rules: a level-20 or higher character begins with one point, and
earned Realm Points raise Realm Level and add points. The bot never gains points
from this feature itself. On each new Realm Level, the next affordable rank is
purchased; a higher-cost rank waits for more points. Existing autonomous bots
restore their purchased ranks and can spend previously earned points at login.
The bot's saved `SerializedAbilities` field stores the ranks with its training
level, so existing saves need no new column or import.

| Classes | Fixed priority order |
| --- | --- |
| Paladin, Armsman, Warrior, Hero | Determination, Mastery of Pain, Augmented Dexterity, Toughness, Augmented Constitution, Physical Defense |
| Mercenary, Reaver, Berserker, Savage, Blademaster, Champion | Determination, Mastery of Pain, Toughness, Augmented Strength, Augmented Dexterity, Augmented Constitution |
| Thane, Valewalker | Mastery of Pain, Wild Power, Augmented Dexterity, Toughness, Augmented Constitution |
| Friar, Warden | Mastery of Healing, Mastery of Pain, Augmented Dexterity, Toughness, Augmented Constitution |
| Cleric, Healer, Shaman, Druid, Bard | Mastery of Healing, Wild Healing, Augmented Dexterity, Serenity, Toughness, Augmented Constitution |
| Minstrel, Skald | Mastery of Pain, Toughness, Augmented Constitution, Augmented Strength, Augmented Dexterity |
| Sorcerer, Mentalist | Wild Power, Augmented Dexterity, Mastery of Magery, Serenity, Augmented Acuity, Toughness |
| Wizard, Runemaster, Eldritch | Wild Power, Mastery of Magery, Augmented Dexterity, Augmented Acuity, Toughness |
| Theurgist, Cabalist, Necromancer, Spiritmaster, Bonedancer, Enchanter, Animist | Wild Power, Mastery of Magery, Augmented Dexterity, Serenity, Toughness |
| Infiltrator, Shadowblade, Nightshade | Mastery of Pain, Physical Defense, Toughness, Augmented Strength, Augmented Dexterity |
| Scout, Hunter, Ranger | Augmented Dexterity, Physical Defense, Toughness, Augmented Constitution |

The order is a priority list, not a grant list. The server's current
`SkillBase.GetClassRealmAbilities` decides what each class may buy. The bot
starts with one rank across the legal class path, enters the active purchase
phase below, then resumes that same path at rank 2 and higher. The runtime
ability's `MaxLevel` and `CostForUpgrade` decide rank caps and cost, including
old versus new passive scaling. Purchases stop at the first unaffordable rank,
banking the remaining points, which keeps the same eventual build whether RP
arrives in small or large awards. If the legal plan is fully trained,
remaining points stay unspent.

## Active abilities

Every class in the table above uses the same deterministic purchase phases:
first one round of legal passive rank-1 purchases in that class's current
order, then the active sequence below, then passive ranks 2 and higher in the
existing round-robin order. Higher First Aid ranks are considered after those
passive ranks.

| Classes | Active purchase priority |
| --- | --- |
| All 39 classes listed above | Purge rank 1; First Aid rank 2 if Ignore Pain is class-legal, otherwise rank 1; Ignore Pain rank 1; Second Wind rank 1 with its required Augmented Constitution ranks; resume passive ranks 2+; then remaining First Aid ranks |

Purge uses the class catalog's `AtlasOF_Purge` key. Its runtime handler type
(`AtlasOF_PurgeAbilityReduced` or the standard handler) supplies the current
4- or 10-point cost; the bot never buys both. Ignore Pain likewise uses the
catalog's `AtlasOF_IgnorePain` key, with its runtime handler type
(`AtlasOF_IgnorePainTank` or the standard handler) supplying the current 8- or
14-point cost. Saved ranks use the catalog's actual key, not synthetic variant
keys. First Aid advances one rank at a time to its runtime maximum of three.
Unavailable or class-illegal steps are skipped. The first currently
unaffordable legal step banks the remaining points until it can be purchased;
later steps do not consume those points out of order. The bot may buy only the
legal ranks needed to meet an active's prerequisites. It does not change,
remove, or refund an existing passive rank. No rank or effect is granted for
free. This fixed phase order makes purchases independent of RP award chunking.

Current Atlas handlers define the following point costs, prerequisites and
reuse delays. `CostForUpgrade` on the live class ability remains authoritative
at purchase time; these values document the current catalog and may change with
server rules.

| Ability | Current cost and prerequisite | Cooldown / execution |
| --- | --- | --- |
| Reduced Purge handler (`AtlasOF_PurgeAbilityReduced`) | 4 points; no prerequisite | 1 rank; 20 minutes |
| Standard Purge handler | 10 points; no prerequisite | 1 rank; 20 minutes |
| First Aid | 3 / 6 / 10 points for ranks 1 / 2 / 3; no prerequisite | 15 minutes; outside combat |
| Tank Ignore Pain handler (`AtlasOF_IgnorePainTank`) | 8 points; First Aid 2 | 1 rank; 30 minutes |
| Standard Ignore Pain handler | 14 points; First Aid 2 | 1 rank; 30 minutes |
| Second Wind | 10 points; Augmented Constitution 3 | 1 rank; 15 minutes |

Since 0.220.0, personal Purge has a 20-minute reuse delay for both standard
and reduced-cost handlers. Players, companions and autonomous world bots use
the same handler timer; trainer details also read that timer. Druid Group
Purge retains its separate 30-minute delay. Existing saved autonomous
cooldown deadlines are honored; new activations receive the shorter timer.

When the required passive prerequisite is in the class catalog but below the
required rank, its ordinary runtime upgrade cost is also charged to the same
point pool. For example, Second Wind may first buy the missing Augmented
Constitution ranks. Existing legal passive allocations count toward that
requirement.

Only a persistent autonomous world bot that is not a temporary helper, is not
in a player-led group, and is not on a stable-master route can execute these
abilities. Purge is checked before normal AI early returns so it can clear
mezz or stun. It responds to active/recent combat or critical health rather
than realm or battleground membership, which includes hostile same-realm
players under Camlann. Ignore Pain heals in combat at or below 30% health;
Second Wind restores endurance in combat at or below 30%; First Aid heals
outside combat at or below 30%. These conservative thresholds reserve the
long-reuse effects for meaningful need. First Aid remains out of combat because
its handler rejects combat use.

Cooldowns are stored as optional `ra-cooldown|<key>|<UTC gameplay ticks>` tokens
in the existing `SerializedAbilities` field, alongside the established
`ra|<key>|<rank>` tokens. Deadlines use `WorldSimulationClock.UtcNow`, not a
process-local tick counter, and are synchronously saved before the handler can
grant an effect. If persistence fails, the bot does not execute the ability;
the in-memory attempt remains blocked for that deadline. Old and malformed
cooldown tokens are retained verbatim, and an unknown or invalid cooldown token
disables autonomous active use rather than being rewritten or treated as
ready. No database column or save import is required.

Rollback requires no save migration. An older passive-only build does not
recognize active rank tokens; its existing fail-closed path preserves the whole
`SerializedAbilities` string but may suppress autonomous realm-ability
restoration while those tokens remain. Reapplying this stage restores the
saved ranks. Keep cooldown tokens in place when reapplying so already-used
abilities do not reset their reuse time.

These priorities are fork-specific recommendations based on the [official
realm ability descriptions](https://www.darkageofcamelot.com/realm-abilities/)
and [class library](https://www.darkageofcamelot.com/class-library/), with the
fork's combat property calculations and actual class data used for compatibility.
They are not official builds. Mastery of Blocking and Parrying remain outside
these priorities pending balance observations, although their bonuses now
apply to autonomous GameBots. Falcon's Eye is excluded because its main handler
currently grants spell rather than archery critical chance. No real-player or
companion realm ability training changes.

The 0.209.0 Release server and launcher builds were deployed locally on
2026-10-04, with 17 files replaced and the existing accounts, database, and
settings preserved. Real-client verification remains pending: inspect one bot
from each class after loading, confirm rank/point persistence across a restart,
observe effects in combat, and confirm a Realm Level gain spends only
legitimately available points. The source build validates compilation but
cannot establish those in-client effects.
