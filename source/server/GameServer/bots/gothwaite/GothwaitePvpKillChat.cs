using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// The kill chat of the Gothwaite PvP mode (docs/GOTHWAITE_PVP_MODE.md section 8, stage 4). When a mode bot kills a player-shaped
/// target in the arena, the kill rolls a 15 per cent chance. A pass schedules one line: it waits for the bot to leave combat (up to
/// twelve seconds) and for a player within say range, then the bot says it in local say, the channel the bots' ambient chat uses
/// (<see cref="GameLiving.Say"/>). A bot speaks at most once per two minutes and the arena at most once per 30 seconds. The lines
/// are plain and pass <see cref="AutonomousChatSafetyPolicy"/>. With the mode off nothing is said.
/// </summary>
public static class GothwaitePvpKillChat
{
    /// <summary>The chance of a line for one eligible kill, in per cent.</summary>
    public const int ChancePercent = 15;
    /// <summary>A bot that spoke waits this long before its next line.</summary>
    public const int BotCooldownMilliseconds = 120_000;
    /// <summary>The arena (one region) waits this long between two lines of any bots.</summary>
    public const int RegionCooldownMilliseconds = 30_000;
    /// <summary>The first attempt runs this long after the kill, so the bot has a moment to finish its turn.</summary>
    public const int FirstAttemptMilliseconds = 2_500;
    /// <summary>While the bot is still in combat, the attempt is retried this often ...</summary>
    public const int CombatRetryMilliseconds = 1_500;
    /// <summary>... this many times, after which the line is dropped.</summary>
    public const int CombatRetries = 8;

    private static readonly GothwaitePvpChatGate Gate = new();

    /// <summary>
    /// A kill that may carry a line (pure): the mode is on, the killer is player-shaped and a mode bot, the victim is player-shaped, and
    /// the death is in the arena.
    /// </summary>
    public static bool Eligible(bool modeOn, bool playerKill, bool killerIsModeBot, bool victimPlayerShaped, bool inArena) =>
        modeOn && playerKill && killerIsModeBot && victimPlayerShaped && inArena;

    /// <summary>The chance roll of one eligible kill (pure): a roll below <see cref="ChancePercent"/> passes.</summary>
    public static bool ChancePasses(double roll) => roll < ChancePercent / 100d;

    /// <summary>
    /// One line for a kill (the only randomness is the draw). The victim's name is used only when it is clean and short; "rez pls" joins
    /// the choice only while a member of the speaker's group is dead. A line that would carry a blocked term falls back to "gg".
    /// </summary>
    public static string Line(string victimName, bool groupHasDeadMember, Random random)
    {
        random ??= Random.Shared;
        string name = CleanName(victimName);
        var options = new List<string> { "gg", "inc more from the harbor" };
        if (name != null)
        {
            options.Add($"{name} down");
            options.Add($"good fight, {name}");
        }
        if (groupHasDeadMember)
            options.Add("rez pls");
        return AutonomousChatSafetyPolicy.Sanitize(options[random.Next(options.Count)], "gg");
    }

    /// <summary>
    /// Called by <see cref="GothwaitePvpStats.RecordKill"/> for a counted arena fight whose killer is a mode bot. It rolls the chance now
    /// and, on a pass, schedules one attempt; the attempt checks the bot and the rate limits when it runs. Nothing happens with the mode off,
    /// because the stats hook is never reached then.
    /// </summary>
    public static void OnKill(GameBot killer, string victimName, Random random = null)
    {
        random ??= Random.Shared;
        if (killer == null || !ChancePasses(random.NextDouble()))
            return;
        var attempt = new SpeakAttempt(killer, victimName, CombatRetries);
        _ = new ECSGameTimer(killer, _ => RunAttempt(attempt, GameLoop.GameLoopTime), FirstAttemptMilliseconds);
    }

    // One attempt, on the game loop: the speaker must still be a live mode bot, out of combat and casting, with a player to hear it,
    // and the rate limits must allow a line. Returns the next delay for the timer, or 0 to stop.
    private static int RunAttempt(SpeakAttempt attempt, long now)
    {
        GameBot speaker = attempt.Speaker;
        if (!GothwaitePvpMode.IsModeBot(speaker) || !speaker.IsAlive || speaker.ObjectState != GameObject.eObjectState.Active)
            return 0;
        if (speaker.InCombat || speaker.IsCasting)
        {
            if (attempt.RetriesLeft <= 0)
                return 0;
            attempt.RetriesLeft--;
            return CombatRetryMilliseconds;
        }
        if (!HasAudience(speaker) || !Gate.TryClaim(BotIdOf(speaker), speaker.CurrentRegionID, now))
            return 0;
        speaker.Say(Line(attempt.VictimName, GroupHasDeadMember(speaker), Random.Shared));
        return 0;
    }

    private static string CleanName(string name)
    {
        string trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > 32 || AutonomousChatSafetyPolicy.ContainsBlockedTerm(trimmed))
            return null;
        return trimmed;
    }

    private static bool HasAudience(GameBot speaker) =>
        speaker.GetPlayersInRadius(WorldMgr.SAY_DISTANCE).Any(player => player.ObjectState == GameObject.eObjectState.Active);

    private static bool GroupHasDeadMember(GameBot speaker)
    {
        Group group = speaker.Group;
        if (group == null)
            return false;
        foreach (GameLiving member in group.GetMembersInTheGroup())
            if (member != null && member != speaker && !member.IsAlive)
                return true;
        return false;
    }

    private static long BotIdOf(GameBot bot) => bot.DatabaseID > 0 ? bot.DatabaseID : bot.ObjectID;

    // The state one scheduled line carries: the speaker, the victim's name as it was at the kill, and the combat retries left.
    private sealed class SpeakAttempt
    {
        public SpeakAttempt(GameBot speaker, string victimName, int retries)
        {
            Speaker = speaker;
            VictimName = victimName;
            RetriesLeft = retries;
        }

        public GameBot Speaker { get; }
        public string VictimName { get; }
        public int RetriesLeft { get; set; }
    }
}

/// <summary>
/// The kill chat's rate limits as data (pure, against the game clock): one line per bot per
/// <see cref="GothwaitePvpKillChat.BotCooldownMilliseconds"/> and one line per region per <see cref="GothwaitePvpKillChat.RegionCooldownMilliseconds"/>.
/// <see cref="TryClaim"/> checks both and records the line in one locked step, so two bots never both claim the same window.
/// </summary>
public sealed class GothwaitePvpChatGate
{
    private readonly object _sync = new();
    private readonly Dictionary<long, long> _botAllowedAt = new();
    private readonly Dictionary<ushort, long> _regionAllowedAt = new();

    /// <summary>True, and both limits are restarted from <paramref name="now"/>, when the bot and the region are both free; false otherwise.</summary>
    public bool TryClaim(long botId, ushort region, long now)
    {
        lock (_sync)
        {
            if (_botAllowedAt.TryGetValue(botId, out long botAllowed) && now < botAllowed)
                return false;
            if (_regionAllowedAt.TryGetValue(region, out long regionAllowed) && now < regionAllowed)
                return false;
            _botAllowedAt[botId] = now + GothwaitePvpKillChat.BotCooldownMilliseconds;
            _regionAllowedAt[region] = now + GothwaitePvpKillChat.RegionCooldownMilliseconds;
            return true;
        }
    }

    /// <summary>Forgets every limit. Tests use it.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _botAllowedAt.Clear();
            _regionAllowedAt.Clear();
        }
    }
}
