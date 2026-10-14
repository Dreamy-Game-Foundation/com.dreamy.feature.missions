using System.Collections.Generic;
using Dreamy.Datasave;
using Dreamy.Economy.Rewards;
using Newtonsoft.Json;

namespace Dreamy.Missions
{
    public sealed class MissionSaveData : SaveData
    {
        [JsonProperty("catalogId")] public string CatalogId { get; set; }
        [JsonProperty("missions")] public Dictionary<string, MissionProgressData> Missions { get; set; } = new();
        [JsonProperty("processedEventIds")] public List<string> ProcessedEventIds { get; set; } = new();
        [JsonProperty("activeWindow")] public MissionWindowData ActiveWindow { get; set; }
        [JsonProperty("pendingClaims")] public List<MissionPendingClaim> PendingClaims { get; set; } = new();
        public override int Version => 2;
    }

    public sealed class MissionProgressData
    {
        [JsonProperty("progress")] public long Progress { get; set; }
        [JsonProperty("claimed")] public bool Claimed { get; set; }
        [JsonProperty("distinctValues")] public List<string> DistinctValues { get; set; } = new();
    }

    public sealed class MissionWindowData
    {
        public string Id { get; set; }
        public long StartUtcTicks { get; set; }
        public long EndUtcTicks { get; set; }
        public string Revision { get; set; }
        public MissionPeriodPolicy Period { get; set; }
        public MissionSelectionPolicy Selection { get; set; }
        public List<MissionDefinition> Definitions { get; set; } = new();
        public List<string> SelectedIds { get; set; } = new();
        public Dictionary<string, MissionProgressData> Missions { get; set; } = new();
        public List<string> ProcessedEventIds { get; set; } = new();
        public int RerollsUsed { get; set; }
    }

    public sealed class MissionPendingClaim
    {
        public string CatalogId { get; set; }
        public string MissionId { get; set; }
        public MissionWindowData WindowSnapshot { get; set; }
        public ResolvedRewardBundle Bundle { get; set; }
        public RewardJournal Journal { get; set; }
    }
}
