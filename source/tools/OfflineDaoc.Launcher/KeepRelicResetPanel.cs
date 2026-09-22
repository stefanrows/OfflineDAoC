namespace OfflineDaoc.Launcher;

internal sealed partial class MainForm
{
    private Button? _resetKeepsRelics;
    private bool _resettingKeepsRelics;
    private DateTime? _keepRelicResetUtc;

    private Control BuildActiveRvrHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = DaocTheme.Panel, Font = Font };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label { Text = "CAMLANN FRONTIER — guild-owned keeps and guild-only relics · see Realm Events", Dock = DockStyle.Fill,
            ForeColor = DaocTheme.GoldLight, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, 0);
        Button resetKeepsRelics = ActionButton("Reset Keeps && Relics", DaocTheme.Gold);
        _resetKeepsRelics = resetKeepsRelics;
        resetKeepsRelics.AccessibleName = "Reset Keeps & Relics";
        resetKeepsRelics.AutoSize = true;
        resetKeepsRelics.MinimumSize = new Size(220, 32);
        resetKeepsRelics.Anchor = AnchorStyles.Right;
        resetKeepsRelics.Enabled = false;
        resetKeepsRelics.Click += async (_, _) => await ResetKeepsRelicsAsync();
        header.Controls.Add(resetKeepsRelics, 1, 0);
        return header;
    }

    private async Task ResetKeepsRelicsAsync()
    {
        if (_resettingKeepsRelics || !BotGoalsServerStopped())
        {
            MessageBox.Show(this, "Stop the server completely before resetting keeps and relics.", "Server must be stopped");
            return;
        }
        if (MessageBox.Show(this, "Reset the Camlann frontier: clear all keep guild claims, return keeps to the unclaimed state, and return all six relics to their temple shrines?\n\nCharacters, bots, inventories, coins, Realm Exchange and event records are not changed. A small backup of the keep/relic rows will be saved.",
            "Reset Keeps & Relics", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        if (_resetKeepsRelics is null) return;
        _resettingKeepsRelics = true;
        _resetKeepsRelics.Enabled = _startButton.Enabled = false;
        try
        {
            var result = await Task.Run(() => KeepRelicReset.Apply(_database,
                Path.Combine(_root, "data", "keep-relic-reset-backups"), BotGoalsServerStopped));
            _keepRelicResetUtc = result.UpdatedUtc;
            _rvrWorld = null; // Never display old ownership or rally attendance after a reset.
            MessageBox.Show(this, $"Reset complete: {result.Keeps} keeps and {result.Relics} relics.\n\nStart the server to load the restored ownership and refresh Realm Events.\n\nBackup: {result.Backup}", "Keeps & relics restored");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Reset failed — no partial reset", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _resettingKeepsRelics = false; await RefreshDashboardAsync(); }
    }
}
