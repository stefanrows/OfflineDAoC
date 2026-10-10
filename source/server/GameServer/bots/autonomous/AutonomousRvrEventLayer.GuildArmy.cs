using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS;

public static partial class AutonomousRvrEventLayer
{
    private sealed class GuildArmy
    {
        public Guild Guild;
        public long Started, GatheredSince, NextEvaluation, NextLog, Launched, SplitSince;
        public Vector3? Camp;
        public readonly Dictionary<string, ArmyParty> Parties = new(StringComparer.Ordinal);
        public readonly HashSet<string> Released = new(StringComparer.Ordinal);
        public readonly Dictionary<GameLiving, long> Sightings = new();
        public int Present, ReadyParties, Healers, Operators, Required, Defenders;
        public int PlannedParties;
        public bool HoldColumn, Failed;
    }

    private sealed record ArmyParty(GameBot Leader, GameBot[] Members, long Tick, long Joined, long[] Operators);

    public sealed record GuildArmyOrder(string TargetId, long Generation, Vector3? Camp,
        bool Released, bool HoldColumn, bool Failed, int Present, int Required, int Parties, long Remaining);

    private static GuildArmy ArmyLocked(ActiveEvent active, string forceId, GameBot bot, long now)
    {
        if (!MustersLocked(active, forceId) || bot?.Guild == null || !AutonomousSiegeMarch.IsWorldActor(bot) ||
            GameRelic.IsPlayerCarryingRelic(bot) || AutonomousGuildKeepDefense.IsRecalled(bot)) return null;
        string key = bot.Guild.GuildID;
        if (string.IsNullOrEmpty(key)) return null;
        if (!active.GuildArmies.TryGetValue(key, out var army) ||
            army.Failed && now - army.Started >= AutonomousGuildAssault.MaximumTravelMilliseconds +
                AutonomousGuildAssault.MaximumGatherMilliseconds + AbandonedTargetMilliseconds)
            active.GuildArmies[key] = army = new GuildArmy { Guild = bot.Guild, Started = now,
                PlannedParties = AutonomousGuildAssault.PlannedParties(Random.Shared.NextDouble()) };
        return army;
    }

    public static GuildArmyOrder GuildArmyPlan(string forceId, GameBot bot, long now)
    {
        using (EnterSync())
        {
            var active = Events.Values.FirstOrDefault(e => MustersLocked(e, forceId));
            var army = active == null ? null : ArmyLocked(active, forceId, bot, now);
            return army == null ? null : ArmyOrder(active, army, forceId, now,
                !army.Parties.ContainsKey(forceId) && (bot?.Group?.MemberCount ?? 0) < 2);
        }
    }

    /// <summary>
    /// A lone bot is not a party and so can never be on the army's released list
    /// (ReportGuildArmy needs a group leader). Before the army has launched it must
    /// not join the siege at all; once a wave is out and the army has not failed it
    /// may follow as a loose helper, like a solo player joining a siege in 1.65.
    /// </summary>
    private static bool LoneBotMustWaitForArmyLocked(Force force, ActiveEvent active) =>
        force.MemberCount < 2 && GuildArmyGoverns(active) &&
        !ArmyLaunched(active.GuildArmies.Values.FirstOrDefault(army => army.Guild?.Name == force.GuildName));

    private static bool ArmyLaunched(GuildArmy army) => army is { Failed: false, Launched: > 0 };

    private static GuildArmyOrder ArmyOrder(ActiveEvent active, GuildArmy army, string forceId, long now, bool lone = false)
    {
        // Hold the leading parties; the rear must still be allowed to catch up.
        bool hold = army.HoldColumn && army.Parties.TryGetValue(forceId, out var own) &&
            army.Released.Any(id => army.Parties.TryGetValue(id, out var other) && other.Leader.IsAlive &&
                DistanceFromKeep(active, other.Leader) > DistanceFromKeep(active, own.Leader) + 600);
        return new(active.TargetId, army.Started, army.Camp, army.Released.Contains(forceId) || lone && ArmyLaunched(army), hold,
            army.Failed, army.Present, army.Required, army.ReadyParties,
            Math.Max(0, ArmyDeadline(army) - now));
    }

    private static long ArmyDeadline(GuildArmy army) => army.GatheredSince > 0
        ? Math.Min(army.Started + AutonomousGuildAssault.MaximumTravelMilliseconds + AutonomousGuildAssault.MaximumGatherMilliseconds,
            army.GatheredSince + AutonomousGuildAssault.MaximumGatherMilliseconds)
        : army.Started + AutonomousGuildAssault.MaximumTravelMilliseconds;

    private static double DistanceFromKeep(ActiveEvent active, GameBot bot) =>
        bot.CurrentRegionID == active.Target.RegionId
            ? Vector2.Distance(new(bot.X, bot.Y), new(active.Target.X, active.Target.Y)) : double.PositiveInfinity;

    /// <summary>Only a route-proved exterior camp can become the shared destination.</summary>
    public static Vector3? SetGuildArmyCamp(string forceId, GameBot bot, long generation, Vector3 point, long now)
    {
        using (EnterSync())
        {
            var active = Events.Values.FirstOrDefault(e => MustersLocked(e, forceId));
            var army = active == null ? null : ArmyLocked(active, forceId, bot, now);
            if (army == null || army.Started != generation || army.Failed) return null;
            army.Camp ??= point;
            return army.Camp;
        }
    }

    /// <summary>No database, region-wide roster or navigation work under the event lock.</summary>
    public static void ReportGuildArmy(string forceId, GameBot leader, GameBot[] members,
        long[] equippedOperators, GameLiving[] sightings, long now)
    {
        using (EnterSync())
        {
            var active = Events.Values.FirstOrDefault(e => MustersLocked(e, forceId));
            var army = active == null ? null : ArmyLocked(active, forceId, leader, now);
            if (army == null || army.Failed || leader.Group?.LivingLeader != leader) return;
            long joined = army.Parties.TryGetValue(forceId, out var previous) ? previous.Joined : now;
            army.Parties[forceId] = new(leader, members, now, joined, equippedOperators);
            foreach (var enemy in sightings) army.Sightings[enemy] = now;
            if (now < army.NextEvaluation) return;
            army.NextEvaluation = now + 5000;
            EvaluateArmy(active, army, now);
        }
    }

    private static bool ArmyMember(ActiveEvent active, GuildArmy army, string force, ArmyParty party, GameBot bot) =>
        bot?.IsAlive == true && bot.ObjectState == GameObject.eObjectState.Active &&
        bot.IsAutonomousWorldBot && !bot.IsPlayerLedGroup && !bot.IsTemporaryGroupHelper &&
        bot.Guild == army.Guild && bot.Group != null && bot.Group == party.Leader.Group &&
        bot.TempProperties.GetProperty<string>("RvrEventForce") == force &&
        !AutonomousGuildKeepDefense.IsRecalled(bot) && MustersLocked(active, force);

    private static bool NearKeep(ActiveEvent active, GameBot bot) => bot.CurrentRegionID == active.Target.RegionId &&
        Vector2.DistanceSquared(new(bot.X, bot.Y), new(active.Target.X, active.Target.Y)) <= 6500 * 6500;

    private static void EvaluateArmy(ActiveEvent active, GuildArmy army, long now)
    {
        foreach (var force in army.Parties.Keys.Where(id => !MustersLocked(active, id) ||
                     army.Parties[id].Leader.Guild != army.Guild).ToArray())
        { army.Parties.Remove(force); army.Released.Remove(force); }
        foreach (var enemy in army.Sightings.Keys.Where(enemy => !enemy.IsAlive ||
                     enemy.ObjectState != GameObject.eObjectState.Active ||
                     now - army.Sightings[enemy] > AutonomousGuildAssault.ObservationLifetime).ToArray())
            army.Sightings.Remove(enemy);
        army.Defenders = army.Sightings.Count;
        var keep = int.TryParse(active.TargetId.AsSpan(9), out int keepId)
            ? GameServer.KeepManager.GetKeepByID(keepId) : null;
        int guards = keep?.Guards.Values.Count(g => g.IsAlive) ?? active.Target.GuardStrength;
        int doors = keep?.Doors.Values.Count(d => d.IsAlive && d.IsAttackableDoor && d.State == eDoorState.Closed)
            ?? active.Target.ClosedDoors;
        army.Required = AutonomousGuildAssault.RequiredAttackers(army.Defenders, guards, doors, army.PlannedParties);

        // A released party must not retain permission after its wipe/respawn.
        // Keep living fighters' references even while combat suppresses reports.
        foreach (string force in army.Released.ToArray())
        {
            var party = army.Parties[force];
            if (!party.Members.Any(bot => ArmyMember(active, army, force, party, bot) &&
                    bot.CurrentRegionID == active.Target.RegionId &&
                    (now - army.Launched < 90_000 || NearKeep(active, bot) ||
                        now - army.Launched < 5 * 60_000 && army.Camp is Vector3 camp &&
                        Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), camp) >
                            AutonomousGuildAssault.RallyRadius * AutonomousGuildAssault.RallyRadius)))
                army.Released.Remove(force);
        }
        var deployed = army.Released.SelectMany(force => army.Parties[force].Members
            .Where(bot => ArmyMember(active, army, force, army.Parties[force], bot))).Distinct().ToArray();
        bool fighting = deployed.Any(bot => NearKeep(active, bot) &&
            (bot.InCombat || bot.IsAttacking || (bot.Brain as BotBrain)?.HasAggro == true));
        army.HoldColumn = false;
        if (deployed.Length > 0 && !fighting)
        {
            var leaders = army.Released.Select(force => army.Parties[force].Leader).Where(b => b.IsAlive).ToArray();
            army.HoldColumn = leaders.Any(a => leaders.Any(b => a.CurrentRegionID != b.CurrentRegionID || a.GetDistanceTo(b) > 2500));
            if (!army.HoldColumn) army.SplitSince = 0;
            else if (army.SplitSince == 0) army.SplitSince = now;
            else if (now - army.SplitSince >= AutonomousRvrSpeed.SiegeNoProgressMilliseconds)
            { FailGuildArmy(active, army, now, "Guild assault column remained separated"); return; }
        }
        if (army.Launched > 0 && army.Released.Count == 0)
        {
            // Fresh, bounded assembly after a defeated wave, never an automatic
            // solo retry with the old launch permission.
            army.Started = now; army.GatheredSince = army.Launched = army.SplitSince = 0;
        }

        foreach (string force in army.Parties.Keys.Where(id => !army.Released.Contains(id) &&
                     army.Released.Count > 0 && now - Math.Max(army.Started, army.Parties[id].Joined) >=
                     AutonomousGuildAssault.MaximumGatherMilliseconds).ToArray())
        {
            AbandonTarget(force, active.TargetId, now);
            ReleasedForces[force] = "Late guild reinforcement could not assemble before its deadline";
            army.Parties.Remove(force);
            MusterLog.Warn($"RVR_GUILD_ARMY target={active.TargetId} force={force} action=reinforcement-timeout");
        }
        var ready = new List<string>();
        army.Present = army.Healers = army.Operators = 0;
        if (army.Camp is Vector3 camp)
        foreach (var pair in army.Parties)
        {
            var party = pair.Value;
            if (army.Released.Contains(pair.Key) || now - party.Tick > AutonomousGuildAssault.ReportLifetime ||
                party.Leader.Group?.LivingLeader != party.Leader || !party.Leader.IsAlive) continue;
            var present = party.Members.Where(bot => ArmyMember(active, army, pair.Key, party, bot) &&
                bot.CurrentRegionID == active.Target.RegionId && !bot.IsOnStableMasterRoute &&
                !bot.InCombat && !bot.IsAttacking && !bot.IsMezzed && !bot.IsStunned &&
                (bot.Brain as BotBrain)?.HasAggro != true && bot.HealthPercent >= 60 &&
                Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), camp) <=
                    AutonomousGuildAssault.RallyRadius * AutonomousGuildAssault.RallyRadius).Distinct().ToArray();
            if (present.Length < AutonomousGuildAssault.PartyQuorum || !present.Contains(party.Leader) ||
                !AutonomousWorldBotController.SiegeSuppliesReadyForMarch(party.Leader, party.Members, active.TargetId)) continue;
            ready.Add(pair.Key);
            army.Present += present.Length;
            army.Healers += present.Count(bot => bot.CharacterClass != null &&
                BotPartyRoles.IsHealingClass((eCharacterClass)bot.CharacterClass.ID) && bot.ManaPercent >= 25);
            army.Operators += present.Count(bot => party.Operators.Contains(bot.DatabaseID));
        }
        army.ReadyParties = ready.Count;
        if (army.Present > 0 && army.GatheredSince == 0) army.GatheredSince = now;
        bool launch = army.Released.Count == 0 && AutonomousGuildAssault.Ready(ready.Count, army.Present,
            army.Healers, army.Operators, army.Required, doors > 0,
            army.GatheredSince == 0 ? 0 : now - army.GatheredSince, army.PlannedParties);
        // Late parties join only a still substantial living attack; merely
        // retaining an event reservation is never permission to march alone.
        bool reinforce = fighting && deployed.Length + army.Present >= army.Required && ready.Count > 0;
        if (launch || reinforce)
        {
            foreach (string force in ready) army.Released.Add(force);
            if (launch) army.Launched = now;
            army.NextLog = 0;
        }
        else if (army.Released.Count == 0 && now >= ArmyDeadline(army))
        { FailGuildArmy(active, army, now, "Guild army could not assemble sufficient physical strength and siege equipment"); return; }
        if (now >= army.NextLog)
        {
            army.NextLog = now + 30_000;
            MusterLog.Info($"RVR_GUILD_ARMY target={active.TargetId} guild=\"{army.Guild.Name}\" " +
                $"action={(launch ? "launch" : reinforce ? "reinforce" : army.HoldColumn ? "column-hold" : "gather")} " +
                $"camp={active.Target.RegionId}:{army.Camp} plannedParties={army.PlannedParties} parties={ready.Count} present={army.Present} " +
                $"healers={army.Healers} operators={army.Operators} sightedDefenders={army.Defenders} " +
                $"guards={guards} doors={doors} required={army.Required} deployed={deployed.Length} " +
                $"readyForces=\"{string.Join(",", ready)}\" forces=\"{string.Join(",", army.Released)}\" " +
                $"remainingMs={Math.Max(0, ArmyDeadline(army)-now)}");
        }
    }

    private static void FailGuildArmy(ActiveEvent active, GuildArmy army, long now, string reason)
    {
        army.Failed = true;
        foreach (string force in army.Parties.Keys.ToArray())
        {
            AbandonTarget(force, active.TargetId, now);
            ReleasedForces[force] = reason;
        }
        army.Released.Clear();
        MusterLog.Warn($"RVR_GUILD_ARMY target={active.TargetId} guild=\"{army.Guild.Name}\" action=abandon " +
            $"present={army.Present} required={army.Required} reason=\"{reason}\"");
    }
}
