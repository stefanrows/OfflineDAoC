using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DOL.GS.DataPacks;

namespace DOL.GS.Commands;

/// <summary>
/// /datapacks (GM): diagnostics for the world data packs (docs/TOA_DATA_PACKS.md). "list" shows every shipped pack and
/// whether this save has it; "apply &lt;name&gt;" applies one pending pack now (backup first, one transaction). The running
/// world does not reload: a pack applied here takes effect after the next server restart.
/// </summary>
[CmdAttribute("&datapacks", ePrivLevel.GM,
    "World data packs: which shipped packs this save has, and apply a pending one",
    "/datapacks list - every shipped pack with its state (applied, pending, failed) and whether its file is present",
    "/datapacks apply <name> - apply or retry one pack now, only while no other client is online (backup first); restart the server to load it")]
public sealed class DataPacksCommandHandler : AbstractCommandHandler, ICommandHandler
{
    private static int _applying;

    public void OnCommand(GameClient client, string[] args)
    {
        if (client?.Player == null)
            return;

        string verb = args.Length >= 2 ? args[1].ToLowerInvariant() : "list";
        if (verb == "list" && args.Length <= 2)
        {
            List(client);
            return;
        }

        if (verb == "apply" && args.Length == 3)
        {
            Apply(client, args[2]);
            return;
        }

        DisplaySyntax(client);
    }

    private void List(GameClient client)
    {
        if (!WorldDataPacks.TryCreateApplier(out WorldDataPackApplier applier, out string reason))
        {
            DisplayMessage(client, "World data packs: {0}", reason);
            return;
        }

        try
        {
            // Manifest and marker tables only: pending packs are not decompressed or hashed on the game server here.
            IReadOnlyList<WorldDataPackResult> status = applier.GetQuickStatus();
            DisplayMessage(client, "World data packs ({0}), property world_data_packs_enabled = {1}:",
                status.Count, ServerProperties.Properties.WORLD_DATA_PACKS_ENABLED);
            foreach (WorldDataPackResult result in status)
            {
                string rows = result.State is WorldDataPackState.Applied or WorldDataPackState.AppliedDifferentHash
                    ? $", {result.Rows} rows"
                    : string.Empty;
                DisplayMessage(client, "{0}: {1}{2}, file {3} - {4}", result.Name, result.State, rows,
                    result.FilePresent ? "present" : "missing", result.Message);
            }

            foreach (string unlisted in applier.UnlistedFiles(applier.ReadManifest()))
                DisplayMessage(client, "{0}: not in manifest.json, ignored", unlisted);
        }
        catch (Exception e)
        {
            DisplayMessage(client, "World data packs: cannot read the status: {0}", e.Message);
        }
    }

    private void Apply(GameClient client, string name)
    {
        if (!WorldDataPacks.TryCreateApplier(out WorldDataPackApplier applier, out string reason))
        {
            DisplayMessage(client, "World data packs: {0}", reason);
            return;
        }

        // Startup applies pending packs anyway; applying under other players' feet risks long write locks on their saves.
        int others = ClientService.Instance.GetClients().Count(other => other != null && other != client);
        if (others > 0)
        {
            DisplayMessage(client, "Refused: {0} other client(s) are online. Pending packs apply at the next server start; " +
                                   "use /datapacks apply only while you are alone on the server.", others);
            return;
        }

        if (Interlocked.CompareExchange(ref _applying, 1, 0) != 0)
        {
            DisplayMessage(client, "A data pack is being applied already; wait for it to finish.");
            return;
        }

        DisplayMessage(client, "Applying data pack {0} in the background (backup first). The world reloads it after a restart.", name);
        Task.Run(() =>
        {
            try
            {
                WorldDataPackRun run = applier.Apply(name);
                WorldDataPacks.Report(run);
                foreach (string warning in run.Warnings)
                    DisplayMessage(client, "{0}", warning);
                if (run.BackupPath != null)
                    DisplayMessage(client, "Backup: {0}", run.BackupPath);
                foreach (WorldDataPackResult result in run.Results)
                    DisplayMessage(client, "{0}: {1} - {2}", result.Name, result.State, result.Message);
                if (run.AppliedCount > 0)
                    DisplayMessage(client, "Restart the server to load the new world data.");
            }
            catch (Exception e)
            {
                DisplayMessage(client, "Data pack {0} was not applied: {1}", name, e.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _applying, 0);
            }
        });
    }
}
