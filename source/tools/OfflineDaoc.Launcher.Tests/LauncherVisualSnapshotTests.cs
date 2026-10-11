using System.Reflection;
using System.Text.Json;
using NUnit.Framework;

// Renders every launcher tab with representative mock data (no database, no server,
// no timers) and saves the PNGs beside the test output for visual review.
[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class LauncherVisualSnapshotTests
{
    private static readonly Assembly Launcher = Assembly.Load("OfflineDAoC");
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestCase(960, 620, 1f)]
    [TestCase(1100, 700, 1f)]
    [TestCase(960, 620, 1.25f)]
    [TestCase(960, 620, 1.5f)]
    public void EveryTabRendersWithMockDataAtCompactAndDefaultSizes(int width, int height, float scale)
    {
        Type mainFormType = Launcher.GetType("OfflineDaoc.Launcher.MainForm")!;
        using var form = (Form)Activator.CreateInstance(mainFormType)!;
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-20_000, -20_000);
        form.Size = new Size(width, height);
        form.Show();
        // Grids need their handles before bound rows can be selected, as in the real window.
        PopulateMockData(form, mainFormType);
        if (scale != 1f) form.Scale(new SizeF(scale, scale));
        form.PerformLayout();
        Application.DoEvents();

        TabControl tabs = Descendants(form).OfType<TabControl>().Single(tab => tab.TabPages.Count == 9);
        foreach (TabPage page in tabs.TabPages.Cast<TabPage>().ToList())
        {
            tabs.SelectedTab = page;
            form.PerformLayout();
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            string slug = string.Concat(page.Text.Where(char.IsLetterOrDigit)).ToLowerInvariant();
            string file = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                $"launcher-{width}x{height}-{(int)(scale * 100)}pct-{slug}.png");
            bitmap.Save(file);
            TestContext.AddTestAttachment(file);
        }
    }

    private static void PopulateMockData(Form form, Type mainFormType)
    {
        Type botRowType = mainFormType.GetNestedType("BotRow", BindingFlags.NonPublic)!;
        ConstructorInfo botConstructor = botRowType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 20);
        var bots = (System.Collections.IList)mainFormType.GetField("_bots", Hidden)!.GetValue(form)!;
        string[] realms = ["Albion", "Midgard", "Hibernia"];
        string[] classes = ["Paladin", "Warrior", "Eldritch", "Cleric", "Hunter", "Druid"];
        string[] zones = ["Dartmoor", "Mularn", "Breifine", "Sheeroe Hills", "Tuscaran Glacier", "Caer Sidi"];
        var groupMembers = new Dictionary<string, List<object>>();
        for (int index = 0; index < 18; index++)
        {
            string realm = realms[index % 3];
            string group = index < 12 ? $"group-{index / 4 + 1}" : string.Empty;
            bool online = index % 4 != 3;
            object row = botConstructor.Invoke(new object[]
            {
                (long?)(1000 + index), $"Crewbot{index:00}", realm, index % 2 == 0 ? "Briton" : "Highlander",
                index % 2 == 0 ? "Male" : "Female", classes[index % classes.Length], 10 + index * 2, zones[index % zones.Length],
                online ? "Hunting near " + zones[index % zones.Length] : "Parked", online, true, false,
                group, group.Length > 0 ? "Meeting up" : string.Empty, group.Length > 0 ? "Shared PvE" : string.Empty,
                group.Length > 0 ? "Traveling to the town rendezvous" : string.Empty, "GroupPve", string.Empty, string.Empty, string.Empty,
            });
            SetProperty(row, "PlayerType", index % 3 == 0 ? "Leveler" : "Hunter");
            SetProperty(row, "GuildName", index % 5 == 0 ? "North Bomb" : string.Empty);
            SetProperty(row, "GuildCharter", index % 5 == 0 ? "PvP" : string.Empty);
            SetProperty(row, "RealmPoints", (long?)(index * 12_345L));
            bots.Add(row);
            if (group.Length > 0)
            {
                if (!groupMembers.TryGetValue(group, out List<object>? members))
                    groupMembers[group] = members = new List<object>();
                members.Add(row);
            }
        }
        mainFormType.GetMethod("ApplyFilter", Hidden)!.Invoke(form, null);

        Type groupRowType = mainFormType.GetNestedType("GroupRow", BindingFlags.NonPublic)!;
        ConstructorInfo groupConstructor = groupRowType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 9);
        var groups = (System.Collections.IList)mainFormType.GetField("_groups", Hidden)!.GetValue(form)!;
        var typedMembersType = typeof(List<>).MakeGenericType(botRowType);
        int groupIndex = 0;
        foreach (var (groupId, members) in groupMembers)
        {
            var typedMembers = (System.Collections.IList)Activator.CreateInstance(typedMembersType)!;
            foreach (object member in members) typedMembers.Add(member);
            string leader = (string)botRowType.GetProperty("Name")!.GetValue(members[0])!;
            groups.Add(groupConstructor.Invoke(new object[]
            {
                groupId, realms[groupIndex % 3], "Meeting up", "Shared PvE", "Traveling to the town rendezvous",
                leader, "Camelot", leader, typedMembers,
            }));
            groupIndex++;
        }
        mainFormType.GetMethod("ApplyGroupSearch", Hidden)!.Invoke(form, new object?[] { null });

        Type auctionType = mainFormType.GetNestedType("AuctionRow", BindingFlags.NonPublic)!;
        ConstructorInfo auctionConstructor = auctionType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.GetParameters().Length == 10);
        var auctions = (System.Collections.IList)mainFormType.GetField("_auctions", Hidden)!.GetValue(form)!;
        for (int index = 0; index < 9; index++)
        {
            auctions.Add(auctionConstructor.Invoke(new object?[]
            {
                $"Test cloth gloves {index + 40}", $"Crewbot{index:00}", 40 + index, 1 + index % 3, "Fixed price",
                $"{index + 2}g 50s", 25_000L * (index + 2), (DateTime?)null, "24h from first load", realms[index % 3],
            }));
        }
        mainFormType.GetMethod("ApplyAuctionFilter", Hidden)!.Invoke(form, null);

        Type snapshotType = mainFormType.GetNestedType("RvrWorldSnapshot", BindingFlags.NonPublic)!;
        string json = JsonSerializer.Serialize(new
        {
            UpdatedUtc = DateTime.UtcNow,
            Running = true,
            Objectives = new[]
            {
                new { Kind = "Dragon", Name = "Golestandt", Owner = "Albion", State = "Raid rally", Location = "Dartmoor", Carrier = "", Forces = "Assigned: 240/240 · Present: 192", Id = "dragon-albion", CooldownMilliseconds = 0L, Phase = "Staging", PhaseRemainingMilliseconds = 1_800_000L },
                new { Kind = "Keep", Name = "Dun Ailinne", Owner = "Hibernia", State = "SIEGE — under attack", Location = "Breifine", Carrier = "", Forces = "Albion 48 / Hibernia 32", Id = "keep-1", CooldownMilliseconds = 0L, Phase = "", PhaseRemainingMilliseconds = 0L },
                new { Kind = "Relic", Name = "Thor's Hammer", Owner = "In transit", State = "ESCORT / INTERCEPTION", Location = "Odin's Gate", Carrier = "Test carrier", Forces = "Original realm: Midgard · Strength", Id = "relic-1", CooldownMilliseconds = 0L, Phase = "", PhaseRemainingMilliseconds = 0L },
            },
        });
        mainFormType.GetField("_rvrWorld", Hidden)!.SetValue(form, JsonSerializer.Deserialize(json, snapshotType));
        mainFormType.GetField("_rvrServerRunning", Hidden)!.SetValue(form, true);
        mainFormType.GetMethod("RenderActiveRvr", Hidden)!.Invoke(form, null);
    }

    private static void SetProperty(object row, string name, object? value) =>
        row.GetType().GetProperty(name)!.SetValue(row, value);

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
                yield return descendant;
        }
    }
}
