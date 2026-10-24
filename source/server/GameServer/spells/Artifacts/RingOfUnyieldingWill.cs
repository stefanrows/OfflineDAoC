namespace DOL.GS.Spells
{
    /// <summary>
    /// Ring of Unyielding Will, level 10 (spell 30162, 10 minutes): a defensive proc buff. A melee
    /// or archery hit on the wearer has a Spell.Frequency / 100 percent chance (20 % in our row) to
    /// cast two spells at once: the damage over time named by Spell.Damage (30161, 40 spirit) on
    /// the attacker and the heal over time named by Spell.Value (30160) on the wearer.
    /// Live: "a 10 minute defensive proc buff that procs a 40 delve Spirit DoT and also casts a
    /// reverse HoT proc on you" (IGN Vault, 2004). The row's description says the same. No
    /// implementation of this type exists in DOL or OpenDAoC.
    /// </summary>
    [SpellHandler(eSpellType.UnyeldingProc)]
    public class UnyeldingProc : DefensiveProcSpellHandler
    {
        private readonly Spell _attackerSpell;

        public UnyeldingProc(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line)
        {
            _attackerSpell = SkillBase.GetSpellByID((int) spell.Damage);

            if (_attackerSpell == null)
                return;

            _attackerSpell.Level = spell.Level;

            if (caster is GameNPC npc)
                _attackerSpell = npc.GetScaledSpell(_attackerSpell);
        }

        public override string ShortDescription =>
            $"When the wearer is hit, {Spell.Frequency / 100}% chance to cast damage over time on the attacker and a heal over time on the wearer.";

        /// <summary>The proc spell cast on the wearer (Spell.Value) and the one cast on the attacker (Spell.Damage).</summary>
        public Spell WearerSpell => _procSpell;

        public Spell AttackerSpell => _attackerSpell;

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            // Same proc spell line rule as BaseProcSpellHandler.CreateECSEffect.
            _procSpellLine = initParams.Target == Caster ? _buffSpellLine : SkillBase.GetSpellLine(GlobalSpellsLines.Item_Effects);
            return ECSGameEffectFactory.Create(initParams, static (in i) => new ArtifactDefensiveProcEffect(i));
        }

        public override void EventHandler(AttackData ad)
        {
            if (ad.AttackResult is not eAttackResult.HitUnstyled and not eAttackResult.HitStyle)
                return;

            if (!Caster.Chance(RandomDeckEvent.DefensiveProcChance, Spell.Frequency / 100))
                return;

            CastProc(_procSpell, ad);
            CastProc(_attackerSpell, ad);
        }

        private void CastProc(Spell spell, AttackData ad)
        {
            if (spell == null)
                return;

            ISpellHandler handler = ScriptMgr.CreateSpellHandler(ad.Target, spell, _procSpellLine);
            handler?.StartSpell(spell.Target is eSpellTarget.ENEMY ? ad.Attacker : ad.Target);
        }
    }
}
