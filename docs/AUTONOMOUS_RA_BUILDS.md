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
`SkillBase.GetClassRealmAbilities` decides what each class may buy. Unavailable
abilities are skipped; purchases rotate through the legal abilities one rank
at a time, then repeat. The runtime ability's `MaxLevel` and `CostForUpgrade`
decide rank caps and cost, including old versus new passive scaling. Purchases
stop at the first unaffordable rank, banking the remaining points, which keeps
the same eventual build whether RP arrives in small or large awards. If the
legal plan is fully trained, remaining points stay unspent.

These priorities are fork-specific recommendations based on the [official
realm ability descriptions](https://www.darkageofcamelot.com/realm-abilities/)
and [class library](https://www.darkageofcamelot.com/class-library/), with the
fork's combat property calculations and actual class data used for compatibility.
They are not official builds. Timed active abilities need a bot decision policy
and player-specific execution paths, so they are outside this passive build.
Mastery of Blocking and Parrying are excluded because the current GameBot
defense calculators do not apply their ability bonuses. Falcon's Eye is
excluded because its main handler currently grants spell rather than archery
critical chance. No real-player or companion realm ability training changes.

Installation and real-client verification remain pending: inspect one bot from
each class after loading, confirm rank/point persistence across a restart,
observe effects in combat, and confirm a Realm Level gain spends only legitimately
available points. The source build validates compilation but cannot establish
those in-client effects.
