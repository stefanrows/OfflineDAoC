using System;
using System.Linq;
using System.Numerics;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>Read the force's actual assignment, so combat and movement agree even
/// before an individual member's controller has observed a new order.</summary>
public static class AutonomousSiegeMarch
{
    public static bool IsWorldActor(GameBot bot) => bot is { IsAutonomousWorldBot: true,
        IsTemporaryGroupHelper: false, IsPersistentPlayerCompanion: false, IsPlayerLedGroup: false };

    private sealed record MarchCache(long Tick, string Force, bool Marching);

    public static bool IsMarching(GameBot bot)
    {
        if (!IsWorldActor(bot) || !AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR)) return false;
        string force = bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}";
        long now = GameLoop.GameLoopTime;
        var cached = bot.TempProperties.GetProperty<MarchCache>("SiegeMarchCache");
        if (cached != null && cached.Tick == now && cached.Force == force) return cached.Marching;
        var plan = AutonomousRvrEventLayer.KeepPlan(force, bot.Realm, now);
        bool marching = plan != null && plan.TargetId.StartsWith("rvr-keep-", StringComparison.Ordinal) &&
            (bot.CurrentRegionID != plan.RegionId ||
             Vector2.DistanceSquared(new(bot.X, bot.Y), new(plan.X, plan.Y)) > 6500 * 6500);
        bot.TempProperties.SetProperty("SiegeMarchCache", new MarchCache(now, force, marching));
        return marching;
    }

    public static bool HasRecentPartyAttack(GameBot bot) =>
        (bot.Group?.GetMembersInTheGroup() ?? [bot]).Any(member => member?.IsAlive == true &&
            member.CurrentRegionID == bot.CurrentRegionID && member.IsWithinRadius(bot, 2500) &&
            member.LastAttackedByEnemyTickPvP > 0 && GameLoop.GameLoopTime - member.LastAttackedByEnemyTickPvP < 15_000);

    public static bool IsPartyThreat(GameBot bot, GameLiving target)
    {
        GameLiving enemy = PvpCombatant.Resolve(target) ?? target;
        bool AttacksParty(GameLiving attacker) => attacker != null &&
            (attacker.IsAttacking || attacker.IsCasting) &&
            PvpCombatant.Resolve(attacker.TargetObject as GameLiving) is { } victim &&
            (victim == bot || bot.Group?.IsInTheGroup(victim) == true);
        return AttacksParty(target) || AttacksParty(enemy) ||
            HasRecentPartyAttack(bot) && BotPvpCrowdControl.IsInFightWith(bot, target, null);
    }

    public static bool MayAssist(GameBot bot, GameLiving target) =>
        !PvpCombatant.IsPlayerShaped(target) || !IsMarching(bot) || IsPartyThreat(bot, target);
}
