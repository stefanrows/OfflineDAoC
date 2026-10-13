using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// The Gothwaite PvP mode driver (docs/GOTHWAITE_PVP_MODE.md section 6). Stage 1 is a stub: a mode bot stands in the
/// castle and the class routine (heals, buffs, native FSM) runs as usual. Rest, groups (stage 2) and arena roaming
/// (stage 3) extend <see cref="Turn"/> and <see cref="State"/>.
/// </summary>
public static class GothwaitePvpDriver
{
    private static readonly Logger Log = LoggerManager.Create(typeof(GothwaitePvpDriver));
    private static readonly ConditionalWeakTable<GameBot, State> States = new();

    /// <summary>Per-bot driver state. Later stages add rest, group and roaming fields here.</summary>
    private sealed class State
    {
        public bool Entered;
    }

    /// <summary>
    /// Runs one driver turn. Returns false when the bot takes no action this turn (no bot, dead or incapacitated).
    /// True lets the class routine run this turn.
    /// </summary>
    public static bool Turn(BotBrain brain, GameBot bot)
    {
        if (brain == null || bot == null || !bot.IsAlive || bot.IsIncapacitated)
            return false;

        State state = States.GetValue(bot, _ => new State());
        if (!state.Entered)
        {
            state.Entered = true;
            Log.Info($"GOTH_PVP_ENTER bot=\"{bot.Name}\" level={bot.Level} realm={(int)bot.Realm} " +
                $"region={bot.CurrentRegionID} x={bot.X} y={bot.Y}");
        }

        // Hold still only while nothing engages the bot, so a fight it is in is never cut short. Stage 1 forms no
        // groups, so no native follow is cut short; stage 2 must give followers their own rule before it adds groups.
        if (!brain.HasAggro && !bot.InCombat)
            Stand(bot);
        return true;
    }

    private static void Stand(GameBot bot)
    {
        bot.StopMovingOnPath();
        bot.StopMoving();
    }
}
