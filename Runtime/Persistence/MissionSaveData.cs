using System.Collections.Generic;
using Dreamy.Datasave;
using Newtonsoft.Json;

namespace Dreamy.Missions
{
    public sealed class MissionSaveData : SaveData
    {
        [JsonProperty("catalogId")] public string CatalogId { get; set; }
        [JsonProperty("missions")] public Dictionary<string, MissionProgressData> Missions { get; set; } = new();
        [JsonProperty("processedEventIds")] public List<string> ProcessedEventIds { get; set; } = new();
        public override int Version => 1;
    }

    public sealed class MissionProgressData
    {
        [JsonProperty("progress")] public long Progress { get; set; }
        [JsonProperty("claimed")] public bool Claimed { get; set; }
    }
}
