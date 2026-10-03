using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.GS;

/// <summary>Temporary, guild-owned emergency orders. Damage callbacks only
/// publish observations; group/raid mutations run before the parallel brains.</summary>
public static class AutonomousGuildKeepDefense
{
    public const long ResponseMilliseconds = AutonomousRvrEventLayer.DefenseResponseMilliseconds;
    private sealed record Alarm(AbstractGameKeep Keep, Guild Owner, long Started, long LastHit,
        AutonomousRvrEventLayer.Plan Plan);
    private static readonly ConcurrentDictionary<int, Alarm> Alarms = new();
    private static readonly ConcurrentDictionary<Guild, Alarm> Focus = new();
    private static readonly Dictionary<GameBot, AbstractGameKeep> Prepared = new();
    private static long _nextPulse;

    public static bool Eligible(GameBot bot) => AutonomousSiegeMarch.IsWorldActor(bot);

    private static bool Current(Alarm alarm, long now) => alarm != null &&
        alarm.Keep.Guild == alarm.Owner && !alarm.Keep.DBKeep.LordDefeated &&
        now - alarm.LastHit < ResponseMilliseconds;

    public static void ObserveAttack(AbstractGameKeep keep, GameObject source)
    {
        Guild owner = keep?.Guild;
        if (!PvpKeepCampaign.Applies(keep) || owner == null ||
            PvpKeepCampaign.IsGarrison(owner) || keep.DBKeep.LordDefeated) return;
        GameLiving attacker = source is GameSiegeWeapon siege
            ? siege.Owner : source as GameLiving;
        attacker = AutonomousBotRealmPointRewards.ResolveRootRewardOwner(attacker);
        if (attacker == null || !GameServer.KeepManager.IsEnemy(keep, attacker) || keep.Guild != owner) return;
        long now = GameLoop.GameLoopTime;
        var plan = new AutonomousRvrEventLayer.Plan(AutonomousRvrEventLayer.Intent.DefendEvent,
            $"rvr-keep-{keep.KeepID}", keep.Name, keep.Region, keep.X, keep.Y, keep.Z, true,
            "All available guild troops: defend our owned keep before other work");
        Alarm alarm = Alarms.AddOrUpdate(keep.KeepID, new Alarm(keep, owner, now, now, plan),
            (_, previous) => previous.Owner == owner && Current(previous, now)
                ? previous with { LastHit = Math.Max(previous.LastHit, now) }
                : new(keep, owner, now, now, plan));
        // First pressure wins until that keep resolves. Simultaneous attacks
        // are queued, rather than sending every troop back and forth per hit.
        Focus.AddOrUpdate(owner, alarm, (_, previous) => !Current(previous, now) ||
            alarm.Started < previous.Started ||
            alarm.Started == previous.Started && alarm.Keep.KeepID < previous.Keep.KeepID ||
            previous.Keep == keep && alarm.LastHit > previous.LastHit ? alarm : previous);
    }

    public static AbstractGameKeep Target(GameBot bot) => bot?.Guild != null &&
        Focus.TryGetValue(bot.Guild, out Alarm alarm) && Eligible(bot) && Current(alarm, GameLoop.GameLoopTime) ? alarm.Keep : null;

    public static AutonomousRvrEventLayer.Plan PlanFor(GameBot bot)
    {
        return bot?.Guild != null && Focus.TryGetValue(bot.Guild, out Alarm alarm) && Eligible(bot) &&
            Current(alarm, GameLoop.GameLoopTime) ? alarm.Plan : null;
    }

    public static bool IsRecalled(GameBot bot) => PlanFor(bot) != null;

    public static bool IsPrepared(GameBot bot, AbstractGameKeep keep) =>
        Prepared.TryGetValue(bot, out var prepared) && prepared == keep;

    // Called by the existing serialized coordinator phase, never a damage hook.
    public static void Pulse(long now)
    {
        if (now < _nextPulse || Alarms.IsEmpty && Prepared.Count == 0) return;
        _nextPulse = now + 1000;
        foreach (var pair in Alarms.ToArray())
            if (!Current(pair.Value, now))
                ((ICollection<KeyValuePair<int, Alarm>>)Alarms).Remove(pair);
        foreach (var pair in Focus.ToArray())
            if (!Current(pair.Value, now))
                ((ICollection<KeyValuePair<Guild, Alarm>>)Focus).Remove(pair);
        foreach (var guild in Alarms.Values.Where(alarm => Current(alarm, now)).GroupBy(alarm => alarm.Owner))
            Focus.TryAdd(guild.Key, guild.OrderBy(alarm => alarm.Started).ThenBy(alarm => alarm.Keep.KeepID).First());

        GameBot[] roster = AutonomousBotRegistry.Snapshot();
        foreach (GameBot bot in Prepared.Keys.Where(bot => !IsRecalled(bot) ||
                     bot.ObjectState != GameObject.eObjectState.Active).ToArray())
            Prepared.Remove(bot);
        foreach (GameBot bot in roster)
        {
            AbstractGameKeep target = Target(bot);
            if (target == null || Prepared.TryGetValue(bot, out var previous) && previous == target) continue;
            string previousForce = bot.TempProperties.GetProperty<string>("RvrEventForce") ?? $"rvr-{bot.DatabaseID}";
            bool retireForce = AutonomousBotGroupCoordinator.RecallForKeepDefense(bot);
            AutonomousRvrEventLayer.WithdrawForKeepDefense(previousForce, bot.DatabaseID,
                retireForce || previousForce == $"rvr-{bot.DatabaseID}");
            Prepared[bot] = target;
            if (bot.Brain is BotBrain brain) brain.NextThinkTick = now;
            LoggerManager.Create(typeof(AutonomousGuildKeepDefense)).Info(
                $"GUILD_KEEP_RECALL guild=\"{bot.Guild.Name}\" keep={target.KeepID} bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level}");
        }
    }
}
