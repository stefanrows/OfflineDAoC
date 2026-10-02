# Who gets which buff, and when (bots and companions)

State 0.188.0 (2026-10-02). The rules read only class data, so all three
realms hand out the same buffs, also against PvP opponents.

## Order a buffer works through

| # | What | Rule |
|---|---|---|
| 1 | Shaman endurance | A grouped Shaman puts its endurance regen on the group first. |
| 2 | Speed | Only while travelling. |
| 3 | **Own dex, then own dex/quickness** | Dexterity shortens every cast (1 % per 6 points), so the buffer buffs itself first (task 78). |
| 4 | Pet-pull pet | With `/petpull` on, the owner's pet gets every buff that helps a pet before anyone else. |
| 5 | Spec buffs before base buffs | A companion with spec-line buffs finishes those before its weaker base-line ones. |
| 6 | Group members before pets | Pets get buffs only once every member in range has them. |
| 7 | Stronger and higher rank first | Among the rest: value, then spell level. |

- Out of combat only, except Celerity. Long buffs (5 min or more, or
  concentration) are refreshed when less than 60 s are left; short buffs are
  not kept up out of combat.
- A base buff is skipped on a member who already carries an equal or
  stronger buff of that kind from another group member.

## Which member gets which single-target buff

| Buff | Gets it | Does not get it |
|---|---|---|
| Strength (base) | Melee classes, hybrids, Valewalker, Vampiir, Friar | Pure casters and the healers Cleric, Healer, Druid, Shaman, Bard, unless they are overloaded or the buffer keeps that buff's concentration free afterwards |
| Constitution, dexterity | Everyone | - |
| Strength/constitution, dexterity/quickness (spec) | Everyone (constitution helps casters too) | - |
| Acuity | List casters only: Wizard, Sorcerer, Cabalist, Theurgist, Necromancer, Runemaster, Spiritmaster, Bonedancer, Warlock, Eldritch, Enchanter, Mentalist, Bainshee, Animist, Valewalker, Vampiir | Tanks, hybrids and healers (Cleric, Druid, Healer, Shaman, Bard, Paladin, Thane, Champion, ...): the server's StatCalculator adds acuity to list casters' casting stat only |
| Armor factor, resists, procs, regen | Everyone | - |
| Pets | Everything that works on a pet: str, con, dex, str/con, dex/qui, both AF kinds, defensive proc, heal-over-time, regen, resists | Acuity and other player-only buffs |

## Not covered

- Group-target buffs (one cast for the whole group) reach every member;
  the class filter applies to single-target buffs only.
- Real-client verification is pending (docs/TASKS.md 78).
