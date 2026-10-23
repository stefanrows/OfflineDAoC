using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.Effects;
using DOL.GS.Spells;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        /// <summary>Celerity is refreshed while this much of it is left, so a cast lands before it ends.</summary>
        private const int GroupCelerityRefreshMilliseconds = 6_000;
        private long _nextGroupCelerityCheck;

        /// <summary>
        /// Keeps the strongest group Celerity on every group member while a tank is in the
        /// group. Celerity lasts seconds, so the ordinary upkeep (which skips short buffs
        /// out of combat and runs after every other support action) lets it lapse.
        /// Heals and crowd control run before this.
        /// </summary>
        private bool TryMaintainGroupCelerity()
        {
            if (BotBody?.IsPlayerLedGroup != true || Body.Group == null || Body.IsCasting ||
                Body.IsIncapacitated || Body.IsSilenced || Body.castingComponent?.HasPendingSkillRequests == true ||
                GameLoop.GameLoopTime < _nextGroupCelerityCheck)
                return false;

            _nextGroupCelerityCheck = GameLoop.GameLoopTime + 500;
            if (!CompanionBombingPolicy.HasTank(BotBody))
                return false;

            Spell celerity = (Body.Spells ?? []).Where(spell => IsCombatCelerity(spell) &&
                    spell.Level <= Body.Level && spell.Target == eSpellTarget.GROUP)
                .OrderByDescending(spell => spell.Value).ThenByDescending(spell => spell.Level)
                .FirstOrDefault();
            if (celerity == null || Body.Mana < BotBody.PowerCost(celerity) ||
                !BotBody.CanAffordConcentration(celerity) || Body.GetSkillDisabledDuration(celerity) > 0 ||
                !celerity.Uninterruptible && !celerity.IsInstantCast && Body.IsBeingInterrupted)
                return false;

            GameLiving target = Body.Group.GetMembersInTheGroup()
                .Where(member => member != null && member.IsAlive && member.CurrentRegionID == Body.CurrentRegionID &&
                    (member == Body || Body.IsWithinRadius(member, celerity.Range)) &&
                    NeedsGroupCelerity(member, celerity))
                .OrderByDescending(member => member is GameBot bot ? BotPartyRoles.IsTank(bot) : false)
                .FirstOrDefault();
            if (target == null)
                return false;

            GameObject previous = Body.TargetObject;
            Body.TargetObject = target;
            bool cast = CastCoordinatedBuff(celerity);
            Body.TargetObject = previous;
            return cast;
        }

        private bool NeedsGroupCelerity(GameLiving member, Spell celerity)
        {
            if (member?.effectListComponent == null)
                return false;
            if (LivingHasQueuedOrCastingSpell(member, celerity))
                return false;

            foreach (ECSGameSpellEffect effect in member.effectListComponent.GetSpellEffects())
            {
                if (effect == null || !effect.IsActive || effect.IsEnding || effect.IsEnded ||
                    !IsCombatCelerity(effect.SpellHandler?.Spell))
                    continue;

                Spell held = effect.SpellHandler.Spell;
                if (held.Value * effect.Effectiveness < celerity.Value)
                    continue;
                // Another caster keeps this one up; only our own is refreshed early.
                if (effect.SpellHandler.Caster != Body)
                    return false;
                if (effect.Duration == 0 || effect.ExpireTick - GameLoop.GameLoopTime > GroupCelerityRefreshMilliseconds)
                    return false;
            }

            return true;
        }

        private bool LivingHasQueuedOrCastingSpell(GameLiving member, Spell spell)
        {
            ISpellHandler current = Body.castingComponent?.SpellHandler;
            ISpellHandler queued = Body.castingComponent?.QueuedSpellHandler;
            return current?.Spell?.ID == spell.ID && current.Target == member ||
                   queued?.Spell?.ID == spell.ID && queued.Target == member;
        }
    }
}
