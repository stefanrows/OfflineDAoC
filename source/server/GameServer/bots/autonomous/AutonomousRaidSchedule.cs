using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// The raid calendar, the way guilds ran dragons in 2003: a raid is announced
/// with a start time, level-50 players sign up "on the side" and carry on with
/// RvR or PvE, and when the time comes everyone who signed up drops what they
/// are doing and gathers at the muster. Too few sign-ups cancel the raid.
/// </summary>
public static class AutonomousRaidSchedule
{
    private sealed class Planned
    {
        public AutonomousRealmRaid.Definition Definition;
        public long StartTick;
        public readonly HashSet<long> SignUps = new();
        public readonly HashSet<long> Rolled = new();
        public bool ReminderSent;
    }

    private static readonly object Sync = new();
    private static Planned _planned;
    private static long _nextPulse;
    private static long _nextSchedule = -1;
    private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRaidSchedule));

    /// <summary>How likely a level-50 player of this kind signs up for a raid.</summary>
    public static double SignUpChance(AutonomousPlayerType type, int sociability) =>
        Math.Clamp(type switch
        {
            AutonomousPlayerType.Leveler => 0.45,
            AutonomousPlayerType.Hybrid => 0.35,
            AutonomousPlayerType.Casual => 0.25,
            AutonomousPlayerType.Roamer => 0.20,
            AutonomousPlayerType.KeepWarrior => 0.15,
            _ => 0.15,
        } * (0.7 + Math.Clamp(sociability, 0, 100) / 100d * 0.6), 0, 0.9);

    public static bool IsSignedUp(GameBot bot)
    {
        lock (Sync) return bot != null && _planned?.SignUps.Contains(bot.DatabaseID) == true;
    }

    public static void Pulse(long now)
    {
        if (!RealmRaidRecruitmentPolicy.AutonomousRaidsEnabled) return;
        Planned start = null;
        lock (Sync)
        {
            if (now < _nextPulse) return;
            _nextPulse = now + 10_000;
            if (_nextSchedule < 0) _nextSchedule = now + 20 * 60_000L;

            if (_planned == null)
            {
                if (now < _nextSchedule || AutonomousRealmRaid.HasActiveEvent) return;
                AutonomousRealmRaid.Definition definition = AutonomousRealmRaid.PickAvailableDefinition();
                if (definition == null) { _nextSchedule = now + 10 * 60_000L; return; }
                _planned = new Planned { Definition = definition, StartTick = now + RealmRaidRecruitmentPolicy.ScheduledLeadMilliseconds };
                RealmEventNotices.Queue(definition.Id, definition.Realm,
                    $"Raid call: {definition.Name} in {RealmRaidRecruitmentPolicy.ScheduledLeadMilliseconds / 60_000} minutes. Level 50s, sign up and keep fighting; we meet at the muster when it starts.");
                Log.Info($"RAID_CALENDAR_ANNOUNCED raid={definition.Id} startsInMinutes={RealmRaidRecruitmentPolicy.ScheduledLeadMilliseconds / 60_000}");
                return;
            }

            if (now < _planned.StartTick)
            {
                int rolls = 0;
                foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
                {
                    if (rolls >= 200) break;
                    if (!AutonomousRealmRaid.IsEligible(bot) || bot.PersistentRecord == null || !_planned.Rolled.Add(bot.DatabaseID))
                        continue;
                    rolls++;
                    if (_planned.SignUps.Count < RealmRaidRecruitmentPolicy.MaximumBots &&
                        Random.Shared.NextDouble() < SignUpChance(AutonomousPlayerBehavior.TypeOf(bot.PersistentRecord),
                            bot.PersistentRecord.Sociability))
                        _planned.SignUps.Add(bot.DatabaseID);
                }
                if (!_planned.ReminderSent && _planned.StartTick - now <= 10 * 60_000L)
                {
                    _planned.ReminderSent = true;
                    RealmEventNotices.Queue(_planned.Definition.Id, _planned.Definition.Realm,
                        $"{_planned.Definition.Name} starts in 10 minutes: {_planned.SignUps.Count} signed up so far.");
                }
                return;
            }

            start = _planned;
            _planned = null;
        }
        Launch(start, now);
    }

    private static void Launch(Planned planned, long now)
    {
        AutonomousRealmRaid.Definition definition = planned.Definition;
        GameBot[] signed = AutonomousBotRegistry.Snapshot()
            .Where(bot => planned.SignUps.Contains(bot.DatabaseID) && AutonomousRealmRaid.IsEligible(bot) && bot.IsAlive)
            .ToArray();
        if (signed.Length < RealmRaidRecruitmentPolicy.ScheduledMinimumSignUps || !AutonomousRealmRaid.IsAvailable(definition.Id))
        {
            RealmEventNotices.Queue(definition.Id, definition.Realm,
                $"{definition.Name} is called off: only {signed.Length} signed up.");
            Log.Info($"RAID_CALENDAR_CANCELLED raid={definition.Id} signedUp={signed.Length}");
            lock (Sync) _nextSchedule = now + 45 * 60_000L;
            return;
        }

        // Everyone who signed up leaves their current group; the rest carry on.
        GameBot[] detached = signed.Where(AutonomousBotGroupCoordinator.DetachForRaid).ToArray();
        GameBot[][] parties = AutonomousBotGroupCoordinator.PlanSignedRaid(detached);
        int total = parties.Sum(p => p.Length);
        bool started = total >= RealmRaidRecruitmentPolicy.ScheduledMinimumSignUps &&
            AutonomousRealmRaid.StartScheduled(definition.Id, parties,
                RealmRaidRecruitmentPolicy.ScheduledMinimumPresent(total), out string reason);
        Log.Info($"RAID_CALENDAR_START raid={definition.Id} signedUp={signed.Length} detached={detached.Length} " +
                 $"parties={parties.Length} bots={total} started={started}");
        lock (Sync) _nextSchedule = now + (started ? (90 + Random.Shared.Next(61)) : 45) * 60_000L;
    }
}
