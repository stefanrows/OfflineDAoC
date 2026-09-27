using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// On Camlann your guild is your realm. A guildmate in trouble nearby pulls
/// in autonomous guild bots that are not already busy, the way someone on
/// guild chat shouts "inc on me at the bridge". Sociability decides who
/// answers; the answer is stable for a while so a helper does not re-roll on
/// every hit.
/// </summary>
public static class AutonomousGuildCohesion
{
    public const int HelpRadius = 2_000;
    private const int DecisionWindowMilliseconds = 30_000;
    private const int ScanIntervalMilliseconds = 2_000;
    private static readonly ConditionalWeakTable<GameLiving, StrongBox<long>> NextScan = new();

    /// <summary>Pure decision: does a guildmate with this sociability come to help now?</summary>
    public static bool Answers(int sociability, long helperKey, long attackerKey, long now)
    {
        long window = now / DecisionWindowMilliseconds;
        uint roll = (uint)HashCode.Combine(helperKey, attackerKey, window) % 100;
        // 15 -> about a third come, 85 -> nearly everyone.
        int chance = Math.Clamp(15 + sociability, 20, 95);
        return roll < chance;
    }

    public static IEnumerable<GameBot> Helpers(GameLiving victim, GameLiving attacker)
    {
        GameLiving member = CompanionPvpEngagement.Character(victim) ?? victim;
        Guild guild = member switch
        {
            GameBot bot => bot.Guild,
            GamePlayer player => player.Guild,
            _ => null,
        };
        if (guild == null || attacker == null || !PvpCombatant.IsPlayerShaped(attacker) ||
            PvpCombatant.IsSafeArea(member))
            yield break;

        long now = GameLoop.GameLoopTime;
        // Called on every hit; one area scan per victim every two seconds is plenty.
        StrongBox<long> next = NextScan.GetValue(member, _ => new StrongBox<long>(0));
        lock (next)
        {
            if (now < next.Value)
                yield break;
            next.Value = now + ScanIntervalMilliseconds;
        }
        foreach (GameBot helper in member.GetNPCsInRadius(HelpRadius).OfType<GameBot>())
        {
            if (helper == member || !helper.IsAlive || helper.ObjectState != GameObject.eObjectState.Active ||
                !helper.IsAutonomousWorldBot || helper.IsPlayerLedGroup || helper.IsTemporaryGroupHelper ||
                helper.Guild != guild || helper.Group != null && helper.Group == member.Group ||
                helper.InCombat || helper.IsAttacking || helper.IsOnStableMasterRoute ||
                helper.Brain is not BotBrain brain || brain.HasAggro ||
                !Answers(helper.PersistentRecord?.Sociability ?? 50, helper.ObjectID, attacker.ObjectID, now))
                continue;
            yield return helper;
        }
    }
}
