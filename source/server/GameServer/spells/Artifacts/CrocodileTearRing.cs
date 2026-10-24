using System;
using DOL.Events;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Crocodile Tear Ring, level 6 "Conversion" (spell 30154, 10 minutes): Spell.Value percent
    /// of every hit the wearer takes is taken off the hit and restores the same amount of power
    /// and of endurance. Melee, archery, spells and damage over time all count.
    /// Live: players describe it as "converts 5% of damage received into pow/end"; the 1.68 notes
    /// fixed the converted damage to update the power and endurance bars, and a later patch made
    /// damage over time convert as well. No implementation of this type exists in DOL or OpenDAoC.
    /// </summary>
    [SpellHandler(eSpellType.CrocodileTearsProc)]
    public class CrocodileTearsProc : SpellHandler, IArtifactEffectHandler
    {
        public CrocodileTearsProc(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override string ShortDescription => $"{Spell.Value}% of all damage taken restores power and endurance instead.";

        public override bool HasPositiveEffect => true;

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ArtifactSpellEffect.Create(initParams);
        }

        public void OnArtifactEffectStart(ECSGameSpellEffect effect)
        {
            GameEventMgr.AddHandler(effect.Owner, GameLivingEvent.AttackedByEnemy, new DOLEventHandler(OnAttacked));
        }

        public void OnArtifactEffectStop(ECSGameSpellEffect effect)
        {
            GameEventMgr.RemoveHandler(effect.Owner, GameLivingEvent.AttackedByEnemy, new DOLEventHandler(OnAttacked));
        }

        private void OnAttacked(DOLEvent e, object sender, EventArgs arguments)
        {
            if (sender is GameLiving owner && arguments is AttackedByEnemyEventArgs args)
                Convert(owner, args.AttackData);
        }

        /// <summary>
        /// Converts the wearer's share of one hit. GameLiving raises AttackedByEnemy before the
        /// damage is dealt (melee and spells alike), so lowering the attack data lowers the hit.
        /// Returns the converted amount.
        /// </summary>
        public int Convert(GameLiving owner, AttackData ad)
        {
            if (owner == null || ad == null || ad.Target != owner || ad.Attacker == owner || !owner.IsAlive)
                return 0;

            if (ad.AttackResult is not eAttackResult.HitUnstyled and not eAttackResult.HitStyle)
                return 0;

            int total = ad.Damage + ad.CriticalDamage;

            if (total <= 0)
                return 0;

            int converted = (int) (total * Spell.Value * 0.01);

            if (converted <= 0)
                return 0;

            int fromDamage = Math.Min(converted, Math.Max(0, ad.Damage));
            ad.Damage -= fromDamage;
            ad.CriticalDamage -= Math.Min(converted - fromDamage, Math.Max(0, ad.CriticalDamage));

            owner.ChangeMana(owner, eManaChangeType.Spell, converted);
            owner.ChangeEndurance(owner, eEnduranceChangeType.Spell, converted);
            (owner as GamePlayer)?.Out.SendMessage($"Conversion turns {converted} damage into power and endurance.", eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
            return converted;
        }
    }
}
