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

        public static bool CanApproach(GameBot bot, GameLiving target) => bot != null &&
            IsStealthClass((eCharacterClass)bot.CharacterClass.ID) &&
            (bot.GetSpecializationByName(Specs.Stealth)?.Level ?? 0) > 0 &&
            bot.IsAlive && !bot.InCombat && !bot.IsAttacking && !bot.IsCasting && !bot.IsCrowdControlled &&
            !bot.IsOnHorse && bot.castingComponent?.HasPendingSkillRequests != true &&
            IsEnemyCombatant(bot, target) && AutonomousRvrTargetPolicy.ShouldEngageGrey(bot, target) &&
            bot.IsWithinRadius(target, 2_000) &&
            GameServer.ServerRules.IsAllowedToAttack(bot, target, true);
    }
}
