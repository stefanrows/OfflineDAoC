using System;

namespace DOL.GS.Commands;

/// <summary>
/// /gothpvp (GM): the Gothwaite PvP mode's status (docs/GOTHWAITE_PVP_MODE.md section 8, stage 4). It prints the mode bots online by
/// level band and phase (castle resting, castle ready, departing, arena), the castle groups, the fights with kills and deaths by band and
/// the kills by con colour (self-defence included), and the think-time load the mode measures. With the mode off it prints one line.
/// It only reads; the status is a snapshot, so run it again to refresh.
/// </summary>
[CmdAttribute("&gothpvp", ePrivLevel.GM,
    "Gothwaite PvP mode status: bots by level band and phase, groups, kills and deaths, kills by con, turn load",
    "/gothpvp - the status (the same as /gothpvp status)",
    "/gothpvp status - the same status; a snapshot, run it again to refresh")]
public sealed class GothwaitePvpCommandHandler : AbstractCommandHandler, ICommandHandler
{
    public void OnCommand(GameClient client, string[] args)
    {
        if (client?.Player == null)
            return;
        if (args.Length > 2 || (args.Length == 2 && !args[1].Equals("status", StringComparison.OrdinalIgnoreCase)))
        {
            DisplaySyntax(client);
            return;
        }
        foreach (string line in GothwaitePvpStatus.Lines(GothwaitePvpStatus.Capture()))
            DisplayMessage(client, "{0}", line);
    }
}
