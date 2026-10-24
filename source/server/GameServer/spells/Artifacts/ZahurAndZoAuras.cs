using System.Collections.Generic;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
    /// <summary>
    /// Crown of Zahur level 10 and Cloudsong "Arcane Leadership" (spell 30151): a pulsing realm
    /// aura (radius 1500) that raises spell range and resist pierce by Spell.Value percent while a
    /// realm mate stays inside it. Same bonuses as Cloudsong's aura, so it reuses that handler.
    /// Live: "5% spell range / 5% spell pierce like Cloudsong"; "the aura on Zahur's is the same as
    /// on Cloudsong". The 1.68 cap of twenty players is not modelled. No implementation of this type
    /// exists in DOL or OpenDAoC.
    /// </summary>
    [SpellHandler(eSpellType.ZahurAura)]
    public class ZahurAura : CloudsongAuraSpellHandler
    {
        public ZahurAura(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override string ShortDescription => $"Realm mates within range of this aura gain {Spell.Value}% spell range and resist piercing.";
    }

    /// <summary>
    /// Crown of Zahur level 5 for melee classes, "Efficient Endurance" (spell 30152, 10 minutes):
    /// every heal spell cast on the wearer also restores endurance equal to Spell.Value percent of
    /// the heal. The row says "A portion of all heals cast on the target restore endurance instead";
    /// the health part of the heal is kept, as in DOL's commented-out reader in HealSpellHandler
    /// ("600 / 10 = 60 end"). HealSpellHandler.HealTarget calls <see cref="OnHealReceived"/>.
    /// </summary>
    [SpellHandler(eSpellType.ZahurEnduProc)]
    public class ZahurEnduProc : SpellHandler, IArtifactEffectHandler
    {
        public ZahurEnduProc(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override string ShortDescription => $"{Spell.Value}% of every heal cast on the target also restores endurance.";

        public override bool HasPositiveEffect => true;

        public override ECSGameSpellEffect CreateECSEffect(in ECSGameEffectInitParams initParams)
        {
            return ArtifactSpellEffect.Create(initParams);
        }

        public void OnArtifactEffectStart(ECSGameSpellEffect effect) { }

        public void OnArtifactEffectStop(ECSGameSpellEffect effect) { }

        /// <summary>
        /// Called for every heal spell that lands on <paramref name="target"/>; restores the
        /// endurance share when the target wears the effect. Returns the endurance restored.
        /// </summary>
        public static int OnHealReceived(GameLiving target, int healAmount)
        {
            if (target?.effectListComponent == null || healAmount <= 0 || !target.IsAlive)
                return 0;

            // Artifact effects live in the eEffect.Unknown bucket; look only there, heals are frequent.
            List<ECSGameSpellEffect> effects = target.effectListComponent.GetSpellEffects(eEffect.Unknown);
            Spell spell = null;

            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].SpellHandler?.Spell?.SpellType is eSpellType.ZahurEnduProc && !effects[i].IsEnding)
                {
                    spell = effects[i].SpellHandler.Spell;
                    break;
                }
            }

            if (spell == null)
                return 0;

            int endurance = (int) (healAmount * spell.Value * 0.01);

            if (endurance <= 0)
                return 0;

            int restored = target.ChangeEndurance(target, eEnduranceChangeType.Spell, endurance);

            if (restored > 0)
                (target as GamePlayer)?.Out.SendMessage($"Your Efficient Endurance restores {restored} endurance.", eChatType.CT_Spell, eChatLoc.CL_SystemWindow);

            return restored;
        }
    }

    /// <summary>
    /// Bracer of Zo'arkat level 10 and Phoebus' Harp "Bedazzlement" (spell 30165): the Band of
    /// Stars Bedazzlement (fumble and spell fumble chance raised by Spell.Value percent) cast as an
    /// area spell (radius 700, 30 seconds). Live: "the bedazzle 25% miss rate". It does not stack
    /// with the single-target Bedazzlement (<see cref="ArtifactSpells.Family"/>). No
    /// implementation of this type exists in DOL or OpenDAoC.
    /// </summary>
    [SpellHandler(eSpellType.ZoAura)]
    public class ZoAuraSpellHandler : ZoDebuffSpellHandler
    {
        public ZoAuraSpellHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }

        public override string ShortDescription => $"Enemies in the area fumble melee attacks and miscast spells {Spell.Value}% more often.";
    }
}
