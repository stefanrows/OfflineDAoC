using DOL.GS.ServerRules;

namespace DOL.GS
{
    public static class BotRvrAmbush
    {
        public static bool IsStealthClass(eCharacterClass characterClass) => BotPoisonSupply.IsAssassin(characterClass) ||
            characterClass is eCharacterClass.Hunter or eCharacterClass.Scout or eCharacterClass.Ranger or eCharacterClass.Minstrel;

        public static bool IsEnemyCombatant(GameBot bot, GameLiving target) => bot != null && target != null &&
            target != bot && target.IsAlive && target.CurrentRegionID == bot.CurrentRegionID &&
            PvpCombatant.IsPlayerShaped(bot) && PvpCombatant.IsPlayerShaped(target) &&
            !PvpCombatant.AreAllied(bot, target);

        public static bool CanApproach(GameBot bot, GameLiving target) => CanBeginApproach(bot) &&
            IsStealthClass((eCharacterClass)bot.CharacterClass.ID) &&
            (bot.GetSpecializationByName(Specs.Stealth)?.Level ?? 0) > 0 &&
            bot.IsWithinRadius(target, 2_000) && (IsLegalPvpAmbush(bot, target) || IsPlayerLedAssassinPveTarget(bot, target));

        /// <summary>
        /// Keeps an already-stealthed approach intact if the target enters
        /// combat during the approach. It never starts stealth itself.
        /// </summary>
        public static bool CanContinueApproach(GameBot bot, GameLiving target) => bot?.IsStealthed == true &&
            bot.CharacterClass != null && bot.IsAlive && !bot.IsAttacking && !bot.IsCasting &&
            !bot.IsCrowdControlled && !bot.IsOnHorse && bot.castingComponent?.HasPendingSkillRequests != true &&
            IsStealthClass((eCharacterClass)bot.CharacterClass.ID) &&
            (bot.GetSpecializationByName(Specs.Stealth)?.Level ?? 0) > 0 &&
            bot.IsWithinRadius(target, 2_000) && (IsLegalPvpAmbush(bot, target) || IsPlayerLedAssassinPveTarget(bot, target));

        /// <summary>Checks a stealth-required assassin style against its current target.</summary>
        public static bool CanUseStealthOpener(GameBot bot, GameLiving target) => bot?.IsStealthed == true &&
            (HasSelectedAutomaticPlayerLedPlan(bot) || AutonomousRvrStealthLoop.Applies(bot)) &&
            bot.CharacterClass != null &&
            BotPoisonSupply.IsAssassin((eCharacterClass)bot.CharacterClass.ID) &&
            (bot.GetSpecializationByName(Specs.Stealth)?.Level ?? 0) > 0 &&
            (bot.GetSpecializationByName(Specs.Critical_Strike)?.Level ?? 0) > 1 &&
            bot.IsWithinRadius(target, 2_000) && (IsLegalPvpAmbush(bot, target) || IsPlayerLedAssassinPveTarget(bot, target));

        private static bool CanBeginApproach(GameBot bot) => bot != null && bot.CharacterClass != null &&
            bot.IsAlive && !bot.InCombat && !bot.IsAttacking && !bot.IsCasting && !bot.IsCrowdControlled &&
            !bot.IsOnHorse && bot.castingComponent?.HasPendingSkillRequests != true;

        private static bool IsLegalPvpAmbush(GameBot bot, GameLiving target) =>
            IsEnemyCombatant(bot, target) && AutonomousRvrTargetPolicy.ShouldEngageGrey(bot, target) &&
            GameServer.ServerRules.IsAllowedToAttack(bot, target, true);

        private static bool IsPlayerLedAssassinPveTarget(GameBot bot, GameLiving target) =>
            HasSelectedAutomaticPlayerLedPlan(bot) && bot.CharacterClass != null &&
            BotPoisonSupply.IsAssassin((eCharacterClass)bot.CharacterClass.ID) &&
            (bot.GetSpecializationByName(Specs.Critical_Strike)?.Level ?? 0) > 1 &&
            target is GameNPC && target is not GameBot && target is not GameSummonedPet &&
            target != bot && target.IsAlive && target.ObjectState == GameObject.eObjectState.Active &&
            target.CurrentRegionID == bot.CurrentRegionID && bot.IsWithinRadius(target, 2_000) &&
            CompanionEngagementMode.Allows(bot, target) &&
            GameServer.ServerRules.IsAllowedToAttack(bot, target, true);

        private static bool HasSelectedAutomaticPlayerLedPlan(GameBot bot) =>
            bot?.CharacterClass != null &&
            bot is { IsPersistentPlayerCompanion: true, IsPlayerLedGroup: true, IsAutonomousWorldBot: false } &&
            string.Equals(bot.PlayerCompanionRecord?.TrainingMode, "automatic", System.StringComparison.OrdinalIgnoreCase) &&
            CompanionBuildPlanCatalog.TryGetPlanById((eCharacterClass)bot.CharacterClass.ID,
                bot.PlayerCompanionRecord.TrainingPlanId, out _);
    }
}
