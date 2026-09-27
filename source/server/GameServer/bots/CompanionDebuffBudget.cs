using System;
using System.Collections.Generic;

namespace DOL.GS
{
    /// <summary>
    /// Player-led companions ration pure debuffs: one cast per debuff type per
    /// fight, two for area debuffs. Debuffs pay full power (the caster discount
    /// covers damage only), and re-applying four of them to every mob of a
    /// pull drained a bomber faster than its bombs did.
    /// </summary>
    public sealed class CompanionDebuffBudget
    {
        // Out of combat this long after the last debuff, the next pull is a new fight.
        public const long FightGapMilliseconds = 5_000;

        private readonly Dictionary<eSpellType, int> _casts = [];
        private long _lastCastTick;

        public static bool IsPureDebuff(Spell spell) =>
            spell != null && spell.Duration > 0 && spell.Damage <= 0 &&
            spell.SpellType.ToString().Contains("Debuff", StringComparison.OrdinalIgnoreCase);

        public static int CastsPerFight(Spell spell) => spell.Radius > 0 ? 2 : 1;

        public void EndFightIfIdle(bool inCombat, long now)
        {
            if (!inCombat && now - _lastCastTick >= FightGapMilliseconds)
                _casts.Clear();
        }

        public bool Allows(Spell spell) =>
            !IsPureDebuff(spell) || _casts.GetValueOrDefault(spell.SpellType) < CastsPerFight(spell);

        public void Record(Spell spell, long now)
        {
            if (!IsPureDebuff(spell))
                return;

            _casts[spell.SpellType] = _casts.GetValueOrDefault(spell.SpellType) + 1;
            _lastCastTick = now;
        }
    }
}
