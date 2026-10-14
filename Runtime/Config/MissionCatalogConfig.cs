using System;
using System.Collections.Generic;
using Dreamy.DataConfig;
using Dreamy.Economy;
using Dreamy.Economy.Rewards;
using Newtonsoft.Json;

namespace Dreamy.Missions
{
    public sealed class MissionCatalogConfig : ConfigBase
    {
        [JsonProperty("catalogId", Required = Required.Always)] private string catalogId;
        [JsonProperty("missions", Required = Required.Always)] private List<MissionDefinition> missions = new();
        [JsonProperty("revision")] public string Revision { get; private set; }
        [JsonProperty("period")] public MissionPeriodPolicy Period { get; private set; } = new();
        [JsonProperty("selection")] public MissionSelectionPolicy Selection { get; private set; }
        [JsonIgnore] public string CatalogId => catalogId;
        [JsonIgnore] public IReadOnlyList<MissionDefinition> Missions => missions.AsReadOnly();

        public override void Initialize(string documentName)
        {
            if (string.IsNullOrWhiteSpace(catalogId) || missions == null || missions.Count == 0)
                throw new DataConfigException(documentName, "A catalogId and at least one mission are required.");
            try { (Period ?? throw new ArgumentException("Period is required.")).Validate(); }
            catch (ArgumentException e) { throw new DataConfigException(documentName, e.Message); }
            if (Selection != null && (Selection.Count <= 0 ||
                !Enum.IsDefined(typeof(MissionConflictPolicy), Selection.ConflictPolicy)))
                throw new DataConfigException(documentName, "Selection count and conflict policy must be valid.");
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (MissionDefinition mission in missions)
            {
                if (mission == null || string.IsNullOrWhiteSpace(mission.Id) || !ids.Add(mission.Id) ||
                    string.IsNullOrWhiteSpace(mission.EventKey) || string.IsNullOrWhiteSpace(mission.TitleKey) ||
                    mission.Target <= 0 || !Enum.IsDefined(typeof(MissionObjective), mission.Objective))
                    throw new DataConfigException(documentName, "Mission IDs must be unique; title, event, target and reward must be valid.");
                bool legacyReward = mission.RewardResourceId != null || mission.RewardAmount != 0;
                if (mission.RewardBundle != null ? legacyReward :
                    !ResourceId.TryParse(mission.RewardResourceId, out _) || mission.RewardAmount <= 0)
                    throw new DataConfigException(documentName, "Use a valid legacy reward or a bundle, never both.");
            }
        }
    }

    public sealed class MissionDefinition
    {
        [JsonProperty("id", Required = Required.Always)] public string Id { get; private set; }
        [JsonProperty("titleKey", Required = Required.Always)] public string TitleKey { get; private set; }
        [JsonProperty("eventKey", Required = Required.Always)] public string EventKey { get; private set; }
        [JsonProperty("target", Required = Required.Always)] public long Target { get; private set; }
        [JsonProperty("rewardResourceId")] public string RewardResourceId { get; private set; }
        [JsonProperty("rewardAmount")] public long RewardAmount { get; private set; }
        [JsonProperty("rewardBundle")] public ResolvedRewardBundle RewardBundle { get; private set; }
        [JsonProperty("objective")] public MissionObjective Objective { get; private set; }
        [JsonProperty("poolId")] public string PoolId { get; private set; }
        [JsonProperty("conflictGroup")] public string ConflictGroup { get; private set; }
        [JsonIgnore] public ResourceAmount Reward => new(new ResourceId(RewardResourceId), RewardAmount);
        internal ResolvedRewardBundle GetBundle() => RewardBundle ?? new ResolvedRewardBundle(1, "mission-fixed-v1",
            new[] { new ResolvedRewardEntry("reward", "currency", RewardResourceId, RewardAmount) });
    }
}
