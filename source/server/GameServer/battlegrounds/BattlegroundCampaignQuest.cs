using System;
using DOL.Database;
using DOL.Events;
using DOL.GS.Quests;

namespace DOL.GS
{
    /// <summary>One reusable native quest row; region and counters survive character logout.</summary>
    public sealed class BattlegroundCampaignQuest : AbstractQuest
    {
        private readonly object _gate = new();
        public BattlegroundCampaignQuest() { }
        public BattlegroundCampaignQuest(GamePlayer player) : base(player) { }
        public BattlegroundCampaignQuest(GamePlayer player, int step) : base(player, step) { }
        public BattlegroundCampaignQuest(GamePlayer player, DbQuest row) : base(player, row) { }
        public override string Name => "Battleground field contracts";
        public override int MaxQuestCount => int.MaxValue;
        public ushort Region => ushort.TryParse(GetCustomProperty("region"), out ushort value) ? value : (ushort)0;
        private int Read(string name) => int.TryParse(GetCustomProperty(name), out int value) ? Math.Clamp(value, 0, 20) : 0;
        public int Monsters => Read("monsters");
        public int Enemies => Read("enemies");
        public int Captures => Read("captures");
        public override string Description
        {
            get
            {
                BattlegroundDefinition definition = BattlegroundCampaignCatalog.Find(Region);
                string capture = !BattlegroundCampaignManager.HasCaptureObjectives(Region) ? "" : $", or help claim the central keep ({Captures}/1)";
                string monsters = BattlegroundCampaignManager.HasMonsterObjectives(Region) ? $"kill monsters ({Monsters}/10), " : "";
                return $"{definition?.Name ?? "Battleground"}: {monsters}defeat hostile players or encounter squads ({Enemies}/5){capture}. Return to a camp commander to turn in any completed contract. Each contract repeats independently, with a one-minute turn-in cooldown.";
            }
        }
        public override bool CheckQuestQualification(GamePlayer player) => BattlegroundCampaignPolicy.IsEligible(player, BattlegroundCampaignCatalog.Find(player.CurrentRegionID));
        public override void Notify(DOLEvent e, object sender, EventArgs args) { }
        public void AssignRegion(ushort region)
        {
            lock (_gate)
            {
                SetCustomProperty("region", region.ToString());
                SetCustomProperty("monsters", "0");
                SetCustomProperty("enemies", "0");
                SetCustomProperty("captures", "0");
                Step = 1;
            }
        }
        internal void Credit(string objective)
        {
            lock (_gate)
            {
                int cap = objective == "monsters" ? 10 : objective == "enemies" ? 5 : 1;
                SetCustomProperty(objective, Math.Min(cap, Read(objective) + 1).ToString());
                QuestPlayer.Out.SendQuestUpdate(this);
            }
        }
        internal int TurnIn()
        {
            lock (_gate)
            {
                int awarded = 0;
                foreach (string objective in new[] { "monsters", "enemies", "captures" })
                {
                    int threshold = objective == "monsters" ? 10 : objective == "enemies" ? 5 : 1;
                    int reward = objective == "captures" ? 10 : 5;
                    if (objective == "monsters" && !BattlegroundCampaignManager.HasMonsterObjectives(Region)) continue;
                    if (objective == "captures" && !BattlegroundCampaignManager.HasCaptureObjectives(Region)) continue;
                    long now = WorldSimulationClock.UtcNow.Ticks;
                    if (long.TryParse(GetCustomProperty(objective + "cooldown"), out long ready) && now < ready) continue;
                    if (Read(objective) < threshold || !BattlegroundCampaignManager.GiveTokens(QuestPlayer, reward)) continue;
                    SetCustomProperty(objective, "0");
                    SetCustomProperty(objective + "cooldown", (now + TimeSpan.FromMinutes(1).Ticks).ToString());
                    QuestPlayer.GainExperience(eXPSource.Quest, Math.Max(1, (QuestPlayer.ExperienceForNextLevel - QuestPlayer.ExperienceForCurrentLevel) / 20));
                    awarded += reward;
                    if (QuestPlayer.CurrentRegionID != Region || !BattlegroundCampaignPolicy.IsEligible(QuestPlayer, BattlegroundCampaignCatalog.Find(Region))) break;
                }
                QuestPlayer.Out.SendQuestUpdate(this);
                return awarded;
            }
        }
    }
}
