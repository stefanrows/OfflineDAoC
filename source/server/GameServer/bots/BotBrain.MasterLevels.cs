using System.Collections.Generic;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Master Level abilities in a fight (TOA_PLAN WP2.4). The class spell choice (CheckSpells)
    /// selects by spell type and knows none of the Master Level types, and pure melee classes
    /// never enter it, so the held abilities get this one small step of their own. It runs in the
    /// combat think after the priority taunt, and for healers in the support role right after
    /// their heals. It costs nothing for a bot without a usable Master Level ability (an empty
    /// list). Which abilities and when: BotMasterLevels.UseOf.
    /// </summary>
    public partial class BotBrain
    {
        /// <summary>Presses at most one ready Master Level ability whose moment has come. True if a cast started.</summary>
        private bool TryMasterLevelAbility()
        {
            GameBot bot = BotBody;
            IReadOnlyList<Spell> spells = bot?.MasterLevelCombatSpells;

            if (spells == null || spells.Count == 0 || Body.IsCasting ||
                Body.castingComponent?.HasPendingSkillRequests == true)
                return false;

            // Cures and the battle cry need no target; buffs and drains do (WantsMasterLevelAbility).
            GameLiving target = Body.TargetObject is GameLiving { IsAlive: true } living ? living : null;

            foreach (Spell spell in spells)
            {
                BotMasterLevelUse use = BotMasterLevels.UseOf(spell);

                if (spell.HasRecastDelay && Body.GetSkillDisabledDuration(spell) > 0)
                    continue;

                if (!Util.Chance(BotMasterLevels.ReactionChance(use)) || Body.Mana < bot.PowerCost(spell))
                    continue;

                if (WantsMasterLevelAbility(use, spell, target) && CastMasterLevelAbility(bot, use, spell))
                    return true;
            }

            return false;
        }

        private bool WantsMasterLevelAbility(BotMasterLevelUse use, Spell spell, GameLiving target)
        {
            switch (use)
            {
                case BotMasterLevelUse.FightBuff:
                    // Once the fight is on: the target is in reach, and the buff is not running.
                    return target != null && Body.IsWithinRadius(target, Body.MeleeAttackRange + 200) &&
                           !HoldsSpellEffect(Body, spell);

                case BotMasterLevelUse.HealBurst:
                    return AnyGroupMember(spell.Radius, member => member.HealthPercent < BotMasterLevels.HEAL_BURST_HEALTH_PERCENT);

                case BotMasterLevelUse.EnduranceDrain:
                    return target != null && BotMasterLevels.IsPlayerLike(target) && Body.IsWithinRadius(target, spell.Range) &&
                           target.EndurancePercent >= BotMasterLevels.DRAIN_MIN_PERCENT;

                case BotMasterLevelUse.PowerDrain:
                    return target != null && BotMasterLevels.IsPlayerLike(target) && Body.IsWithinRadius(target, spell.Range) &&
                           target.MaxMana > 0 && target.ManaPercent >= BotMasterLevels.DRAIN_MIN_PERCENT;

                case BotMasterLevelUse.CureDisease:
                    return AnyGroupMember(CureReach(spell), member => member.effectListComponent?.ContainsEffectForEffectType(eEffect.Disease) == true);

                case BotMasterLevelUse.CureNearsight:
                    return AnyGroupMember(CureReach(spell), member => member.effectListComponent?.ContainsEffectForEffectType(eEffect.Nearsight) == true);

                default:
                    return false;
            }
        }

        private static int CureReach(Spell spell) => spell.Range > 0 ? spell.Range : spell.Radius;

        /// <summary>The bot itself or a living group member within reach that matches.</summary>
        private bool AnyGroupMember(int reach, System.Func<GameLiving, bool> matches)
        {
            if (Body.Group == null)
                return Body.IsAlive && matches(Body);

            foreach (GameLiving member in Body.Group.GetMembersInTheGroup())
            {
                if (member is { IsAlive: true, ObjectState: GameObject.eObjectState.Active } &&
                    (member == Body || Body.IsWithinRadius(member, reach)) && matches(member))
                    return true;
            }

            return false;
        }

        private static bool HoldsSpellEffect(GameLiving living, Spell spell)
        {
            if (living?.effectListComponent == null)
                return false;

            foreach (ECSGameSpellEffect effect in living.effectListComponent.GetSpellEffects())
            {
                if (effect?.SpellHandler?.Spell?.ID == spell.ID)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Casts on the Master Level line, so effects that look the line up find it. Drains go at the
        /// current target; buffs, the battle cry and cures centre on the bot, so the target is the
        /// bot for the request and the enemy again right after.
        /// </summary>
        private bool CastMasterLevelAbility(GameBot bot, BotMasterLevelUse use, Spell spell)
        {
            SpellLine line = SkillBase.GetSpellLine(bot.MasterLevelPath, false) ?? m_mobSpellLine;

            if (use is BotMasterLevelUse.EnduranceDrain or BotMasterLevelUse.PowerDrain)
                return Body.CastSpell(spell, line);

            GameObject enemy = Body.TargetObject;
            Body.TargetObject = Body;

            try
            {
                return Body.CastSpell(spell, line);
            }
            finally
            {
                Body.TargetObject = enemy;
            }
        }
    }
}
