using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// PvE add control for player-led companion groups. A companion with crowd
    /// control duty mesmerizes extra monsters that are not the group's focus,
    /// and companions leave a mesmerized monster alone while any other enemy
    /// is left. Real players are never restricted; an owner who attacks a
    /// mesmerized monster makes it a normal target again.
    /// </summary>
    public static class CompanionAddControl
    {
        public const int ScanRadius = 1500;

        /// <summary>Only companions in a player-led group protect mezzes; autonomous bots keep their rules.</summary>
        public static bool AppliesMezzProtection(GameBot bot) =>
            bot?.IsPlayerLedGroup == true && bot.Group != null;

        /// <summary>True when this companion must not hit a monster because it is mesmerized.</summary>
        public static bool ProtectsMezz(GameBot bot, GameLiving target)
        {
            if (!AppliesMezzProtection(bot) || target is not GameNPC || !target.IsMezzed ||
                BotPvpCrowdControl.PlayerLike(target))
                return false;
            return !(bot.Owner is GamePlayer owner && owner.IsAttacking && owner.TargetObject == target);
        }

        /// <summary>A harmful area spell centered here would wake a protected mezz.</summary>
        public static bool BreaksProtectedMezz(GameBot bot, Spell spell, GameLiving caster, GameLiving target)
        {
            if (spell == null || spell.Radius <= 0 || !spell.IsHarmful || spell.SpellType == eSpellType.Mesmerize ||
                !AppliesMezzProtection(bot))
                return false;
            GameLiving center = spell.Target == eSpellTarget.SELF || spell.Range <= 0 ? caster : target;
            if (center == null)
                return false;
            foreach (GameNPC npc in center.GetNPCsInRadius((ushort)spell.Radius))
            {
                if (ProtectsMezz(bot, npc))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Monsters the group is fighting now: every member's attack or cast
        /// target, each companion's ordered pull, and the owner's target.
        /// </summary>
        public static HashSet<GameLiving> FocusTargets(GameBot bot)
        {
            var focus = new HashSet<GameLiving>();
            Group group = bot?.Group;
            if (group == null)
                return focus;
            foreach (GameLiving member in group.GetMembersInTheGroup())
            {
                if (member == null || !member.IsAlive)
                    continue;
                if (member.TargetObject is GameNPC target && target.IsAlive &&
                    (member.IsAttacking || member is GamePlayer ||
                     member.castingComponent?.SpellHandler?.Spell?.IsHarmful == true))
                    focus.Add(target);
                if (member is GameBot other && other.Brain is BotBrain brain && brain.ActiveOrderedPullTarget is GameLiving pull)
                    focus.Add(pull);
                if (member.ControlledBrain?.Body is GameNPC pet && pet.IsAttacking && pet.TargetObject is GameNPC petTarget)
                    focus.Add(petTarget);
            }
            focus.RemoveWhere(target => target.IsMezzed || BotPvpCrowdControl.PlayerLike(target));
            return focus;
        }

        /// <summary>
        /// Living NPCs already fighting the group: their target is a member or a
        /// member's pet. Idle spawns and fights elsewhere are excluded.
        /// </summary>
        public static IEnumerable<GameNPC> EngagedWithGroup(Group group, IEnumerable<GameNPC> npcs) =>
            npcs.Where(npc => npc != null && npc.IsAlive && npc.TargetObject is GameLiving victim &&
                OnGroupSide(group, victim));

        /// <summary>True when the living is a group member or one of their pets.</summary>
        public static bool OnGroupSide(Group group, GameLiving living)
        {
            if (group == null || living == null)
                return false;
            if (group.IsInTheGroup(living))
                return true;
            return living is GameNPC npc && npc.Brain is IControlledBrain controlled &&
                   controlled.GetLivingOwner() is GameLiving owner && group.IsInTheGroup(owner);
        }

        /// <summary>
        /// Can this mesmerize take hold? Mirrors the native checks: permanent
        /// immunity, the NPC diminishing-return timer, and the rule that a
        /// monster below 75% health resists. A damage-over-time effect would
        /// wake it on the next tick, so that add is left alone.
        /// </summary>
        public static bool CanMezz(GameLiving target, Spell spell)
        {
            if (target == null || target.IsMezzed || target.HealthPercent < 75 ||
                target.HasAbility(Abilities.MezzImmunity) ||
                target.effectListComponent.ContainsEffectForEffectType(eEffect.MezImmunity) ||
                target.effectListComponent.ContainsEffectForEffectType(eEffect.DamageOverTime))
                return false;
            return EffectListService.GetEffectOnTarget(target, eEffect.NPCMezImmunity) is not NpcMezImmunityEffect immunity ||
                   immunity.CanApplyNewEffect(spell?.Duration ?? 0);
        }

        /// <summary>
        /// Picks the add to mesmerize: engaged with the group, not a focus
        /// target, mezzable, and not reserved by another companion. An area
        /// mezz is used only when no focus target is inside its radius.
        /// Adds that attack a non-tank come first, then the most adds caught,
        /// then the nearest.
        /// </summary>
        public static GameLiving ChooseAdd(GameBot caster, Spell spell, IReadOnlyCollection<GameLiving> adds,
            IReadOnlyCollection<GameLiving> focus, int range)
        {
            if (caster == null || spell == null || adds.Count == 0)
                return null;
            bool pointBlank = spell.Target == eSpellTarget.SELF || spell.Range <= 0;
            if (pointBlank && spell.Radius > 0 && focus.Any(target => caster.IsWithinRadius(target, spell.Radius)))
                return null;
            return adds
                .Where(add => caster.IsWithinRadius(add, range) && CanMezz(add, spell) &&
                              (spell.Radius <= 0 || pointBlank || !focus.Any(target => add.IsWithinRadius(target, spell.Radius))))
                .OrderByDescending(add => add.TargetObject is GameBot hit ? !BotPartyRoles.IsTank(hit) : add.TargetObject is GamePlayer)
                .ThenByDescending(add => spell.Radius <= 0 ? 1 : adds.Count(other => other.IsWithinRadius(add, spell.Radius)))
                .ThenBy(caster.GetDistanceTo)
                .FirstOrDefault();
        }
    }
}
