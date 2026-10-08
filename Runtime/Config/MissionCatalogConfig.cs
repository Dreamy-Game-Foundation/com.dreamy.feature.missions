using System;
using System.Collections.Generic;
using Dreamy.DataConfig;
using Dreamy.Economy;
using Newtonsoft.Json;

namespace Dreamy.Missions
{
    public sealed class MissionCatalogConfig : ConfigBase
    {
        [JsonProperty("catalogId", Required = Required.Always)] private string catalogId;
        [JsonProperty("missions", Required = Required.Always)] private List<MissionDefinition> missions = new();
        [JsonIgnore] public string CatalogId => catalogId;
        [JsonIgnore] public IReadOnlyList<MissionDefinition> Missions => missions.AsReadOnly();

        public override void Initialize(string documentName)
        {
            if (string.IsNullOrWhiteSpace(catalogId) || missions == null || missions.Count == 0)
                throw new DataConfigException(documentName, "A catalogId and at least one mission are required.");
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (MissionDefinition mission in missions)
            {
                if (mission == null || string.IsNullOrWhiteSpace(mission.Id) || !ids.Add(mission.Id) ||
                    string.IsNullOrWhiteSpace(mission.EventKey) || string.IsNullOrWhiteSpace(mission.TitleKey) ||
                    mission.Target <= 0 || !ResourceId.TryParse(mission.RewardResourceId, out _) || mission.RewardAmount <= 0)
                    throw new DataConfigException(documentName, "Mission IDs must be unique; title, event, target and reward must be valid.");
            }
        }
    }

    public sealed class MissionDefinition
    {
        [JsonProperty("id", Required = Required.Always)] public string Id { get; private set; }
        [JsonProperty("titleKey", Required = Required.Always)] public string TitleKey { get; private set; }
        [JsonProperty("eventKey", Required = Required.Always)] public string EventKey { get; private set; }
        [JsonProperty("target", Required = Required.Always)] public long Target { get; private set; }
        [JsonProperty("rewardResourceId", Required = Required.Always)] public string RewardResourceId { get; private set; }
        [JsonProperty("rewardAmount", Required = Required.Always)] public long RewardAmount { get; private set; }
        [JsonIgnore] public ResourceAmount Reward => new(new ResourceId(RewardResourceId), RewardAmount);
    }
}
