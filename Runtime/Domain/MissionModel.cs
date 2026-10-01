using System;
using System.Collections.Generic;
using System.Linq;
using Dreamy.Datasave;
using Dreamy.Economy;
using Newtonsoft.Json;

namespace Dreamy.Missions
{
    public sealed class MissionModel : IMissionService
    {
        private readonly MissionCatalogConfig catalog;
        private readonly IDatasaveService datasave;
        private readonly IResourceWallet wallet;
        private readonly string saveKey;
        private readonly int historyLimit;
        private MissionSaveData save;
        private bool busy;

        public MissionModel(MissionCatalogConfig catalog, IDatasaveService datasave,
            IResourceWallet wallet, string saveKey = "missions", int historyLimit = 4096)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.datasave = datasave ?? throw new ArgumentNullException(nameof(datasave));
            this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            this.saveKey = string.IsNullOrWhiteSpace(saveKey) ? throw new ArgumentException("Save key is required.") : saveKey;
            this.historyLimit = historyLimit > 0 ? historyLimit : throw new ArgumentOutOfRangeException(nameof(historyLimit));
            catalog.Initialize("missionCatalog");
            save = Clone(datasave.Load<MissionSaveData>(saveKey));
            if (!string.Equals(save.CatalogId, catalog.CatalogId, StringComparison.Ordinal))
                save = new MissionSaveData { CatalogId = catalog.CatalogId };
            save.Missions ??= new Dictionary<string, MissionProgressData>();
            save.ProcessedEventIds ??= new List<string>();
            save.ProcessedEventIds = save.ProcessedEventIds.Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal).TakeLast(historyLimit).ToList();
            foreach (MissionDefinition mission in catalog.Missions)
            {
                if (!save.Missions.TryGetValue(mission.Id, out MissionProgressData progress) || progress == null)
                    save.Missions[mission.Id] = progress = new MissionProgressData();
                progress.Progress = Math.Max(0, Math.Min(mission.Target, progress.Progress));
                if (progress.Claimed) progress.Progress = mission.Target;
            }
            datasave.Save(Clone(save), saveKey);
        }

        public event Action StateChanged;

        public IReadOnlyList<MissionState> GetState() => catalog.Missions.Select(m =>
            new MissionState(m, save.Missions[m.Id].Progress, save.Missions[m.Id].Claimed)).ToList().AsReadOnly();

        public MissionProgressStatus ReportProgress(string eventKey, string eventId, long amount = 1)
        {
            if (busy) return MissionProgressStatus.Busy;
            if (string.IsNullOrWhiteSpace(eventKey) || string.IsNullOrWhiteSpace(eventId) || amount <= 0)
                return MissionProgressStatus.InvalidEvent;
            // Host event IDs are globally unique within a catalog, even across different event keys.
            if (save.ProcessedEventIds.Contains(eventId)) return MissionProgressStatus.AlreadyProcessed;
            List<MissionDefinition> matching = catalog.Missions.Where(m => m.EventKey == eventKey).ToList();
            if (matching.Count == 0) return MissionProgressStatus.NoMatchingMission;
            busy = true;
            try
            {
                MissionSaveData next = Clone(save);
                foreach (MissionDefinition mission in matching)
                {
                    MissionProgressData progress = next.Missions[mission.Id];
                    if (!progress.Claimed) progress.Progress += Math.Min(amount, mission.Target - progress.Progress);
                }
                next.ProcessedEventIds.Add(eventId);
                if (next.ProcessedEventIds.Count > historyLimit) next.ProcessedEventIds.RemoveAt(0);
                Commit(next);
            }
            finally { busy = false; }
            StateChanged?.Invoke();
            return MissionProgressStatus.Updated;
        }

        public MissionClaimStatus Claim(string missionId)
        {
            if (busy) return MissionClaimStatus.Busy;
            MissionDefinition mission = catalog.Missions.FirstOrDefault(m => m.Id == missionId);
            if (mission == null) return MissionClaimStatus.UnknownMission;
            MissionProgressData progress = save.Missions[mission.Id];
            if (progress.Claimed) return MissionClaimStatus.AlreadyClaimed;
            if (progress.Progress < mission.Target) return MissionClaimStatus.NotComplete;
            busy = true;
            try
            {
                // Length prefixes prevent catalog/mission ID delimiter collisions.
                string transactionId = $"mission:{catalog.CatalogId.Length}:{catalog.CatalogId}:{mission.Id.Length}:{mission.Id}:claim";
                if (!wallet.TryGrant(new ResourceGrantRequest(transactionId, mission.Reward)))
                    return MissionClaimStatus.GrantFailed;
                MissionSaveData next = Clone(save);
                next.Missions[mission.Id].Claimed = true;
                Commit(next);
            }
            finally { busy = false; }
            StateChanged?.Invoke();
            return MissionClaimStatus.Claimed;
        }

        private void Commit(MissionSaveData next)
        {
            // Persist a detached candidate so save failure does not alter current state.
            datasave.Save(Clone(next), saveKey);
            save = next;
        }

        private static MissionSaveData Clone(MissionSaveData data) =>
            data == null ? new MissionSaveData() : JsonConvert.DeserializeObject<MissionSaveData>(JsonConvert.SerializeObject(data));
    }
}
