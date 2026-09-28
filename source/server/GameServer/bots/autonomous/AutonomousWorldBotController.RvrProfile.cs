namespace DOL.GS;

/// <summary>
/// Bug 56 round 2: timed entry points for the parts of an RvR turn that ran
/// without their own profiler phase (live 0.125.0: single ExecuteRvr turns of
/// 260-480 ms showed no sub-phase). The wrapped methods are unchanged; only
/// their time appears in BOT_THINK_PROFILE and BOT_THINK_SLOW.
/// </summary>
public sealed partial class AutonomousWorldBotController
{
    private CampDestination ChooseRvrDestinationTimed(GameBot bot)
    {
        using var profile = BotThinkProfiler.Measure(BotThinkPhase.RvrChooseDestination);
        return ChooseRvrDestination(bot);
    }

    private GameLiving FindRvrTargetTimed(GameBot bot)
    {
        using var profile = BotThinkProfiler.Measure(BotThinkPhase.RvrKeepTarget);
        return FindRvrTarget(bot);
    }
}
