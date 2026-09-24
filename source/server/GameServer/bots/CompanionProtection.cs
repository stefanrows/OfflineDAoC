using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.SkillHandler;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Distributes shield companions' Guard and Protect effects across the
    /// living members of their player-led group.
    /// </summary>
    internal static class CompanionProtection
    {
        private static readonly object Sync = new();

        public static void Maintain(GameBot companion)
        {
            if (companion?.IsPersistentPlayerCompanion != true && companion?.IsTemporaryGroupHelper != true)
                return;

            lock (Sync)
            {
                Group group = companion.Group;
                if (group == null || !group.IsInTheGroup(companion))
                {
                    EndOwnedEffects(companion);
                    return;
                }

                GameLiving[] groupMembers = group.GetMembersInTheGroup()
                    .Where(member => member != null)
                    .ToArray();
                GameLiving[] targets = groupMembers
                    .Where(member => member is GamePlayer or GameBot)
                    .Where(member => member.IsAlive && member.ObjectState == GameObject.eObjectState.Active &&
                                     member.CurrentRegion == companion.CurrentRegion)
                    .OrderBy(member => member.GroupIndex)
                    .ThenBy(member => member.ObjectID)
                    .ToArray();
                HashSet<GameLiving> targetSet = targets.ToHashSet();

                GameBot[] providers = groupMembers
                    .OfType<GameBot>()
                    .Where(IsManagedCompanion)
                    .Where(bot => targetSet.Contains(bot) && group.IsInTheGroup(bot))
                    .Where(bot => bot.GetAbilityLevel(Abilities.Guard) > 0 || bot.GetAbilityLevel(Abilities.Protect) > 0)
                    .OrderBy(bot => bot.GroupIndex)
                    .ThenBy(bot => bot.ObjectID)
                    .ToArray();
                HashSet<GameBot> providerSet = providers.ToHashSet();

                // Effects from a companion that died, left the group, or no
                // longer has the corresponding shield ability must not reserve
                // an ally indefinitely.
                foreach (GameBot member in groupMembers.OfType<GameBot>().Where(IsManagedCompanion))
                {
                    if (!providerSet.Contains(member) || member.GetAbilityLevel(Abilities.Guard) <= 0)
                        EndOwnedGuardEffects(member);
                    else
                        EndInvalidGuardEffects(member, targetSet);

                    if (!providerSet.Contains(member) || member.GetAbilityLevel(Abilities.Protect) <= 0)
                        EndOwnedProtectEffects(member);
                    else
                        EndInvalidProtectEffects(member, targetSet);
                }

                HashSet<GameLiving> externalGuards = FindExternallyCoveredTargets(targets, providerSet, eEffect.Guard);
                HashSet<GameLiving> externalProtects = FindExternallyCoveredTargets(targets, providerSet, eEffect.Protect);
                Dictionary<GameBot, GameLiving> guardPlan = PlanCoverage(
                    providers.Where(bot => bot.GetAbilityLevel(Abilities.Guard) > 0).ToArray(),
                    targets, externalGuards, externalProtects, eEffect.Guard);
                HashSet<GameLiving> guardedTargets = new(externalGuards);
                guardedTargets.UnionWith(guardPlan.Values);
                Dictionary<GameBot, GameLiving> protectPlan = PlanCoverage(
                    providers.Where(bot => bot.GetAbilityLevel(Abilities.Protect) > 0).ToArray(),
                    targets, externalProtects, guardedTargets, eEffect.Protect);

                ApplyGuardPlan(providers, guardPlan);
                ApplyProtectPlan(providers, protectPlan);
            }
        }

        private static bool IsManagedCompanion(GameBot bot) =>
            bot.IsPersistentPlayerCompanion || bot.IsTemporaryGroupHelper;

        private static Dictionary<GameBot, GameLiving> PlanCoverage(
            IReadOnlyList<GameBot> providers,
            IReadOnlyList<GameLiving> targets,
            HashSet<GameLiving> externallyCovered,
            HashSet<GameLiving> preferDifferentCoverage,
            eEffect effectType)
        {
            Dictionary<GameBot, GameLiving> assignments = new();
            GameLiving[] orderedTargets = targets
                .Where(target => !externallyCovered.Contains(target))
                .OrderBy(target => TargetPriority(target) + (preferDifferentCoverage.Contains(target) ? 2 : 0))
                .ThenBy(TargetPriority)
                .ThenBy(target => target.GroupIndex)
                .ThenBy(target => target.ObjectID)
                .ToArray();

            foreach (GameLiving target in orderedTargets)
                TryAssign(target, providers, assignments, new HashSet<GameBot>(), effectType);

            return assignments;
        }

        private static bool TryAssign(
            GameLiving target,
            IReadOnlyList<GameBot> providers,
            Dictionary<GameBot, GameLiving> assignments,
            HashSet<GameBot> visitedProviders,
            eEffect effectType)
        {
            foreach (GameBot provider in providers)
            {
                if (provider == target || !IsWithinEffectRange(provider, target, effectType))
                    continue;
                if (!visitedProviders.Add(provider))
                    continue;

                if (!assignments.TryGetValue(provider, out GameLiving assignedTarget) ||
                    TryAssign(assignedTarget, providers, assignments, visitedProviders, effectType))
                {
                    assignments[provider] = target;
                    return true;
                }
            }

            return false;
        }

        private static int TargetPriority(GameLiving target)
        {
            if (IsHealer(target)) return 0;
            if (IsBombCaster(target)) return 1;
            if (IsCaster(target)) return 2;
            if (!IsTank(target)) return 3;
            return 4;
        }

        private static bool IsHealer(GameLiving living)
        {
            if (living is GameBot bot && bot.CanCastHealSpells)
                return true;

            return TryGetCharacterClass(living, out eCharacterClass characterClass) &&
                   BotPartyRoles.IsHealingClass(characterClass);
        }

        private static bool IsBombCaster(GameLiving living) =>
            TryGetCharacterClass(living, out eCharacterClass characterClass) && characterClass is
                eCharacterClass.Spiritmaster or eCharacterClass.Wizard or eCharacterClass.Enchanter or eCharacterClass.Eldritch;

        private static bool IsCaster(GameLiving living) =>
            TryGetCharacterClass(living, out eCharacterClass characterClass) && characterClass is
                eCharacterClass.Cabalist or eCharacterClass.Necromancer or eCharacterClass.Sorcerer or
                eCharacterClass.Theurgist or eCharacterClass.Heretic or eCharacterClass.Bonedancer or
                eCharacterClass.Runemaster or eCharacterClass.Warlock or eCharacterClass.Animist or
                eCharacterClass.Bainshee or eCharacterClass.Mentalist;

        private static bool IsTank(GameLiving living)
        {
            if (living is GameBot bot)
                return BotPartyRoles.IsTank(bot);

            return TryGetCharacterClass(living, out eCharacterClass characterClass) &&
                   BotPartyRoles.For(characterClass) == BotPartyRole.Tank;
        }

        private static bool TryGetCharacterClass(GameLiving living, out eCharacterClass characterClass)
        {
            ICharacterClass playerClass = living switch
            {
                GameBot bot => bot.CharacterClass,
                GamePlayer player => player.CharacterClass,
                _ => null,
            };
            if (playerClass == null)
            {
                characterClass = eCharacterClass.Unknown;
                return false;
            }

            characterClass = (eCharacterClass)playerClass.ID;
            return true;
        }

        private static HashSet<GameLiving> FindExternallyCoveredTargets(
            IEnumerable<GameLiving> targets,
            HashSet<GameBot> managedProviders,
            eEffect effectType)
        {
            HashSet<GameLiving> covered = new();
            foreach (GameLiving target in targets)
            {
                if (effectType == eEffect.Guard)
                {
                    foreach (GuardECSGameEffect guard in target.effectListComponent.GetAbilityEffects(eEffect.Guard).OfType<GuardECSGameEffect>())
                        if (guard.Target == target && IsEffectiveExternalSource(guard.Source, target, managedProviders, eEffect.Guard))
                            covered.Add(target);
                }
                else
                {
                    foreach (ProtectECSGameEffect protect in target.effectListComponent.GetAbilityEffects(eEffect.Protect).OfType<ProtectECSGameEffect>())
                        if (protect.Target == target && IsEffectiveExternalSource(protect.Source, target, managedProviders, eEffect.Protect))
                            covered.Add(target);
                }
            }

            return covered;
        }

        private static bool IsEffectiveExternalSource(
            GameLiving source,
            GameLiving target,
            HashSet<GameBot> managedProviders,
            eEffect effectType) =>
            source != null &&
            (source is not GameBot sourceBot || !managedProviders.Contains(sourceBot)) &&
            IsWithinEffectRange(source, target, effectType);

        private static bool IsWithinEffectRange(GameLiving source, GameLiving target, eEffect effectType)
        {
            int range = effectType switch
            {
                eEffect.Guard => GuardAbilityHandler.GUARD_DISTANCE,
                eEffect.Protect => ProtectAbilityHandler.PROTECT_DISTANCE,
                _ => 0,
            };

            return source != null && target != null && range > 0 && source.IsWithinRadius(target, range);
        }

        private static void ApplyGuardPlan(
            IReadOnlyList<GameBot> providers,
            Dictionary<GameBot, GameLiving> plan)
        {
            foreach (GameBot provider in providers)
            {
                GameLiving desired = plan.TryGetValue(provider, out GameLiving target) ? target : null;
                EndOwnedGuardEffects(provider, desired);
            }

            foreach ((GameBot provider, GameLiving target) in plan)
            {
                if (HasOwnedGuard(provider, target) || HasOtherGuardSource(target, provider))
                    continue;

                GuardAbilityHandler.CancelOurEffectThenAddOnTarget(provider, target);
            }
        }

        private static void ApplyProtectPlan(
            IReadOnlyList<GameBot> providers,
            Dictionary<GameBot, GameLiving> plan)
        {
            foreach (GameBot provider in providers)
            {
                GameLiving desired = plan.TryGetValue(provider, out GameLiving target) ? target : null;
                EndOwnedProtectEffects(provider, desired);
            }

            foreach ((GameBot provider, GameLiving target) in plan)
            {
                if (HasOwnedProtect(provider, target) || HasOtherProtectSource(target, provider))
                    continue;

                ProtectAbilityHandler.CancelOurEffectThenAddOnTarget(provider, target);
            }
        }

        private static bool HasOwnedGuard(GameBot provider, GameLiving target) =>
            provider.effectListComponent.GetAbilityEffects(eEffect.Guard).OfType<GuardECSGameEffect>()
                .Any(effect => effect.Source == provider && effect.Target == target);

        private static bool HasOwnedProtect(GameBot provider, GameLiving target) =>
            provider.effectListComponent.GetAbilityEffects(eEffect.Protect).OfType<ProtectECSGameEffect>()
                .Any(effect => effect.Source == provider && effect.Target == target);

        private static bool HasOtherGuardSource(GameLiving target, GameBot provider) =>
            target.effectListComponent.GetAbilityEffects(eEffect.Guard).OfType<GuardECSGameEffect>()
                .Any(effect => effect.Target == target && effect.Source != provider &&
                               IsWithinEffectRange(effect.Source, target, eEffect.Guard));

        private static bool HasOtherProtectSource(GameLiving target, GameBot provider) =>
            target.effectListComponent.GetAbilityEffects(eEffect.Protect).OfType<ProtectECSGameEffect>()
                .Any(effect => effect.Target == target && effect.Source != provider &&
                               IsWithinEffectRange(effect.Source, target, eEffect.Protect));

        private static void EndOwnedEffects(GameBot bot)
        {
            EndOwnedGuardEffects(bot);
            EndOwnedProtectEffects(bot);
        }

        private static void EndOwnedGuardEffects(GameBot bot, GameLiving keepTarget = null)
        {
            foreach (GuardECSGameEffect effect in bot.effectListComponent.GetAbilityEffects(eEffect.Guard).OfType<GuardECSGameEffect>()
                         .Where(effect => effect.Source == bot && (keepTarget == null || effect.Target != keepTarget)).ToArray())
                effect.End();
        }

        private static void EndOwnedProtectEffects(GameBot bot, GameLiving keepTarget = null)
        {
            foreach (ProtectECSGameEffect effect in bot.effectListComponent.GetAbilityEffects(eEffect.Protect).OfType<ProtectECSGameEffect>()
                         .Where(effect => effect.Source == bot && (keepTarget == null || effect.Target != keepTarget)).ToArray())
                effect.End();
        }

        private static void EndInvalidGuardEffects(GameBot bot, HashSet<GameLiving> validTargets)
        {
            foreach (GuardECSGameEffect effect in bot.effectListComponent.GetAbilityEffects(eEffect.Guard).OfType<GuardECSGameEffect>()
                         .Where(effect => effect.Source == bot && !validTargets.Contains(effect.Target)).ToArray())
                effect.End();
        }

        private static void EndInvalidProtectEffects(GameBot bot, HashSet<GameLiving> validTargets)
        {
            foreach (ProtectECSGameEffect effect in bot.effectListComponent.GetAbilityEffects(eEffect.Protect).OfType<ProtectECSGameEffect>()
                         .Where(effect => effect.Source == bot && !validTargets.Contains(effect.Target)).ToArray())
                effect.End();
        }
    }
}
