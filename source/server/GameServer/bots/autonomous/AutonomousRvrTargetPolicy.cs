using System;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;

namespace DOL.GS;

public static class AutonomousRvrTargetPolicy
{
    public static bool IsEligible(bool enemyCombatant, bool targetAlive,
        bool sameRegion, bool targetInFrontier, bool eitherActorInSafeArea, bool serverAllowsAttack) =>
        enemyCombatant && targetAlive && sameRegion && targetInFrontier && !eitherActorInSafeArea && serverAllowsAttack;

    public static bool IsEnemyCombatant(GameLiving actor, GameLiving target) =>
        actor != null && target != null && actor != target &&
        PvpCombatant.IsPlayerShaped(actor) && PvpCombatant.IsPlayerShaped(target) &&
        !PvpCombatant.AreAllied(actor, target);

    /// <summary>
    /// Autonomous bots show Camlann restraint toward grey player-shaped
    /// targets. Retaliation against the bot or one of its allies is always
    /// allowed; otherwise the server property supplies the rare opportunistic
    /// attack chance.
    /// </summary>
    public static bool ShouldEngageGrey(GameLiving actor, GameLiving target, bool targetAttackedCrew = false)
    {
        if (actor is not GameBot { IsAutonomousWorldBot: true } bot || !PvpCombatant.IsPlayerShaped(target))
            return true;

        if (AutonomousGuildGrudgeMemory.IsActiveTarget(bot, target, DateTime.UtcNow))
            return true;

        ConColor con = ConLevels.GetConColor(bot.GetConLevel(target));
        if (con > ConColor.GREY || targetAttackedCrew || TargetedCrewMember(bot, target))
            return true;

        GameLiving victim = PvpCombatant.Resolve(target) ?? target;
        int chance = AutonomousPlayerBehavior.GreyEngageChance(
            AutonomousPlayerBehavior.TypeOf(bot.PersistentRecord), bot.Level, victim.EffectiveLevel,
            bot.PersistentRecord?.Aggression ?? 50,
            AutonomousBotGoalPolicy.Danger,
            Properties.CAMLANN_BOT_GREY_ENGAGE_CHANCE);
        // A scan runs every few seconds. Use one stable draw per target and
        // ten-minute window so a rare grey-gank chance stays rare in practice.
        long window = DateTime.UtcNow.Ticks / TimeSpan.FromMinutes(10).Ticks;
        int roll = (int)(unchecked((ulong)(bot.DatabaseID * 397L + victim.ObjectID * 7919L + window * 65537L)) % 100) + 1;
        return ShouldEngageGrey(true, false, chance, roll);
    }

    public static bool ShouldEngageGrey(bool greyTarget, bool targetAttackedCrew, int chance, int roll) =>
        !greyTarget || targetAttackedCrew || roll <= Math.Clamp(chance, 0, 100);

    private static bool TargetedCrewMember(GameBot bot, GameLiving target) =>
        target.IsAttacking && target.TargetObject is GameLiving victim &&
        PvpCombatant.AreAllied(bot, victim);
}
