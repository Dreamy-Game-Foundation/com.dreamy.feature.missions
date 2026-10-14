using System;
using System.Collections.Generic;
using System.Linq;
using Dreamy.Datasave;
using Dreamy.Economy;
using Dreamy.Economy.Rewards;
using Newtonsoft.Json;

namespace Dreamy.Missions
{
    // Synchronous main-thread owner. All callbacks run while Busy; save candidates are detached.
    public sealed class MissionModel : IMissionService
    {
        private readonly MissionCatalogConfig catalog;
        private readonly IDatasaveService datasave;
        private readonly IResourceWallet wallet;
        private readonly IRewardDeliveryGateway gateway;
        private readonly IMissionClock clock;
        private readonly IMissionRandom random;
        private readonly string saveKey;
        private readonly int historyLimit;
        private MissionSaveData save;
        private bool busy;

        public MissionModel(MissionCatalogConfig catalog, IDatasaveService datasave,
            IResourceWallet wallet, string saveKey = "missions", int historyLimit = 4096,
            IMissionClock clock = null, IMissionRandom random = null)
            : this(catalog, datasave, wallet ?? throw new ArgumentNullException(nameof(wallet)),
                null, saveKey, historyLimit, clock, random) { }

        public MissionModel(MissionCatalogConfig catalog, IDatasaveService datasave,
            IRewardDeliveryGateway gateway, string saveKey = "missions", int historyLimit = 4096,
            IMissionClock clock = null, IMissionRandom random = null)
            : this(catalog, datasave, null, gateway ?? throw new ArgumentNullException(nameof(gateway)),
                saveKey, historyLimit, clock, random) { }

        private MissionModel(MissionCatalogConfig catalog, IDatasaveService datasave,
            IResourceWallet wallet, IRewardDeliveryGateway gateway, string saveKey, int historyLimit,
            IMissionClock clock, IMissionRandom random)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.datasave = datasave ?? throw new ArgumentNullException(nameof(datasave));
            this.wallet = wallet;
            this.gateway = gateway;
            this.clock = clock;
            this.random = random;
            this.saveKey = string.IsNullOrWhiteSpace(saveKey) ? throw new ArgumentException("Save key is required.") : saveKey;
            this.historyLimit = historyLimit > 0 ? historyLimit : throw new ArgumentOutOfRangeException(nameof(historyLimit));
            catalog.Initialize("missionCatalog");
            if (catalog.Period.Period != MissionPeriod.Permanent && clock == null)
                throw new ArgumentException("Periodic missions require an injected clock.");
            if (catalog.Selection != null && random == null)
                throw new ArgumentException("Selection requires an injected random source.");
            if (wallet != null && catalog.Missions.Any(m => m.RewardBundle != null))
                throw new ArgumentException("Reward bundles require a strict delivery gateway.");
            save = Clone(datasave.Load<MissionSaveData>(saveKey));
            save.PendingClaims ??= new List<MissionPendingClaim>();
            if (!string.Equals(save.CatalogId, catalog.CatalogId, StringComparison.Ordinal))
            {
                // Preserve the released Permanent catalog-reset contract only in legacy mode.
                if (!string.IsNullOrEmpty(save.CatalogId) &&
                    (catalog.Period.Period != MissionPeriod.Permanent || catalog.Revision != null || catalog.Selection != null ||
                     (save.ActiveWindow != null && (save.ActiveWindow.Period.Period != MissionPeriod.Permanent ||
                         save.ActiveWindow.Revision != null || save.ActiveWindow.Selection != null)) ||
                     save.PendingClaims.Any(p => p.Journal.State != RewardJournalState.Completed)))
                    throw new InvalidOperationException("Catalog identity changed; explicit migration required.");
                save = new MissionSaveData { CatalogId = catalog.CatalogId };
            }
            busy = true;
            try
            {
                if (save.ActiveWindow == null)
                {
                    MissionSaveData next = Clone(save);
                    next.ActiveWindow = CreateWindow();
                    if (catalog.Period.Period == MissionPeriod.Permanent)
                    {
                        next.ActiveWindow.Missions = next.Missions ?? new Dictionary<string, MissionProgressData>();
                        next.ActiveWindow.ProcessedEventIds = (next.ProcessedEventIds ?? new List<string>())
                            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal)
                            .TakeLast(historyLimit).ToList();
                        NormalizeProgress(next.ActiveWindow);
                    }
                    Commit(next);
                }
                else if (catalog.Period.Period == MissionPeriod.Permanent && catalog.Revision == null &&
                    save.ActiveWindow.Revision == null && catalog.Selection == null && save.ActiveWindow.Selection == null &&
                    save.ActiveWindow.Period.Period == MissionPeriod.Permanent)
                {
                    MissionSaveData next = Clone(save);
                    next.ActiveWindow.Definitions = Copy(catalog.Missions.ToList());
                    next.ActiveWindow.SelectedIds = Select(next.ActiveWindow.Definitions, catalog.Selection);
                    next.ActiveWindow.ProcessedEventIds = next.ActiveWindow.ProcessedEventIds
                        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal)
                        .TakeLast(historyLimit).ToList();
                    NormalizeProgress(next.ActiveWindow);
                    Commit(next);
                }
                else
                {
                    if (save.ActiveWindow.Period.Period != catalog.Period.Period)
                        throw new InvalidOperationException("Period changed; explicit migration required.");
                    if (save.ActiveWindow.Period.OffsetMinutes != catalog.Period.OffsetMinutes ||
                        save.ActiveWindow.Period.CutoffMinutes != catalog.Period.CutoffMinutes ||
                        save.ActiveWindow.Period.WeekStart != catalog.Period.WeekStart)
                        throw new InvalidOperationException("Window policy changed; explicit migration required.");
                    if (catalog.Period.Period == MissionPeriod.Permanent)
                        ApplyPermanentRevision();
                }
                if (gateway == null && save.PendingClaims.Any(p => p.Journal.State != RewardJournalState.Completed))
                    throw new InvalidOperationException("Pending strict claims require a delivery gateway.");
                if (gateway != null) RecoverPending();
                AdvanceWindow();
            }
            finally { busy = false; }
        }

        public event Action StateChanged;
        public string WindowId => save.ActiveWindow.Id;
        public MissionWindow Window => new(save.ActiveWindow.Id,
            save.ActiveWindow.StartUtcTicks, save.ActiveWindow.EndUtcTicks);
        public int RerollsUsed => save.ActiveWindow.RerollsUsed;
        public RewardDeliveryResult LastDeliveryResult { get; private set; }

        public IReadOnlyList<MissionState> GetState()
        {
            if (!busy) Refresh();
            MissionWindowData active = save.ActiveWindow;
            return active.SelectedIds.Select(id =>
            {
                MissionDefinition mission = active.Definitions.First(m => m.Id == id);
                MissionProgressData progress = active.Missions[id];
                return new MissionState(mission, progress.Progress, progress.Claimed);
            }).ToList().AsReadOnly();
        }

        public MissionRefreshStatus Refresh()
        {
            if (busy) return MissionRefreshStatus.Busy;
            busy = true;
            try { return AdvanceWindow() ? MissionRefreshStatus.Updated : MissionRefreshStatus.Unchanged; }
            finally { busy = false; }
        }

        private bool AdvanceWindow()
        {
            if (catalog.Period.Period == MissionPeriod.Permanent) return false;
            MissionWindow current = catalog.Period.GetWindow(clock.UtcNow);
            // The persisted start is the high-water mark. Rollback never reopens an old window.
            if (current.StartUtcTicks <= save.ActiveWindow.StartUtcTicks) return false;
            MissionSaveData next = Clone(save);
            next.ActiveWindow = CreateWindow(current);
            Commit(next);
            StateChanged?.Invoke();
            return true;
        }

        private MissionWindowData CreateWindow(MissionWindow? window = null)
        {
            MissionWindow value = window ?? catalog.Period.GetWindow(clock?.UtcNow ?? DateTimeOffset.MinValue);
            var active = new MissionWindowData
            {
                Id = value.Id, StartUtcTicks = value.StartUtcTicks, EndUtcTicks = value.EndUtcTicks,
                Revision = catalog.Revision, Period = Copy(catalog.Period), Selection = Copy(catalog.Selection),
                Definitions = Copy(catalog.Missions.ToList())
            };
            active.SelectedIds = Select(active.Definitions, active.Selection);
            NormalizeProgress(active);
            return active;
        }

        private void ApplyPermanentRevision()
        {
            MissionWindowData active = save.ActiveWindow;
            if (active.Revision == catalog.Revision)
            {
                if (JsonConvert.SerializeObject(active.Definitions) != JsonConvert.SerializeObject(catalog.Missions) ||
                    JsonConvert.SerializeObject(active.Selection) != JsonConvert.SerializeObject(catalog.Selection))
                    throw new InvalidOperationException("Permanent configuration changed without a revision; explicit migration required.");
                return;
            }
            // Permanent has no next boundary. Only additive, identical-existing-definition updates are safe.
            if (active.Definitions.Any(old => !catalog.Missions.Any(m => m.Id == old.Id &&
                JsonConvert.SerializeObject(m) == JsonConvert.SerializeObject(old))) ||
                JsonConvert.SerializeObject(active.Selection) != JsonConvert.SerializeObject(catalog.Selection))
                throw new InvalidOperationException("Incompatible Permanent revision; explicit migration required.");
            MissionSaveData next = Clone(save);
            next.ActiveWindow.Revision = catalog.Revision;
            next.ActiveWindow.Definitions = Copy(catalog.Missions.ToList());
            if (catalog.Selection == null) next.ActiveWindow.SelectedIds = catalog.Missions.Select(m => m.Id).ToList();
            NormalizeProgress(next.ActiveWindow);
            Commit(next);
        }

        private static void NormalizeProgress(MissionWindowData active)
        {
            foreach (MissionDefinition mission in active.Definitions)
            {
                if (!active.Missions.TryGetValue(mission.Id, out MissionProgressData progress) || progress == null)
                    active.Missions[mission.Id] = progress = new MissionProgressData();
                progress.DistinctValues ??= new List<string>();
                progress.Progress = Math.Max(0, Math.Min(mission.Target, progress.Progress));
                if (progress.Claimed) progress.Progress = mission.Target;
            }
        }

        private List<string> Select(List<MissionDefinition> definitions, MissionSelectionPolicy policy)
        {
            if (policy == null) return definitions.Select(m => m.Id).ToList();
            List<MissionDefinition> pool = definitions.Where(m => policy.PoolId == null || m.PoolId == policy.PoolId).ToList();
            // Shuffle once, then take one per conflict group; the relaxed pass always excludes selected IDs.
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            var selected = new List<string>();
            var groups = new HashSet<string>(StringComparer.Ordinal);
            foreach (MissionDefinition mission in pool)
            {
                if (!string.IsNullOrEmpty(mission.ConflictGroup) && !groups.Add(mission.ConflictGroup)) continue;
                selected.Add(mission.Id);
                if (selected.Count == policy.Count) return selected;
            }
            if (policy.ConflictPolicy == MissionConflictPolicy.PreferGroupsThenRelax)
                foreach (MissionDefinition mission in pool)
                {
                    if (selected.Contains(mission.Id)) continue;
                    selected.Add(mission.Id);
                    if (selected.Count == policy.Count) return selected;
                }
            throw new InvalidOperationException("Mission selection has insufficient candidates for the configured count/conflict policy.");
        }

        public MissionRerollStatus Reroll(string missionId)
        {
            if (busy) return MissionRerollStatus.Busy;
            busy = true;
            try
            {
                AdvanceWindow();
                MissionWindowData active = save.ActiveWindow;
                if (!active.SelectedIds.Contains(missionId)) return MissionRerollStatus.UnknownMission;
                MissionDefinition old = active.Definitions.First(m => m.Id == missionId);
                MissionProgressData progress = active.Missions[missionId];
                if (progress.Claimed || progress.Progress >= old.Target ||
                    save.PendingClaims.Any(p => p.WindowSnapshot.Id == active.Id && p.MissionId == missionId &&
                        p.Journal.State != RewardJournalState.Completed))
                    return MissionRerollStatus.Ineligible;
                if (active.RerollsUsed >= active.Period.FreeRerolls || active.Period.Period == MissionPeriod.Permanent)
                    return MissionRerollStatus.LimitReached;
                var candidates = active.Definitions.Where(m => !active.SelectedIds.Contains(m.Id) &&
                    (active.Selection?.PoolId == null || m.PoolId == active.Selection.PoolId)).ToList();
                var groups = new HashSet<string>(active.Definitions.Where(m => m.Id != missionId &&
                    active.SelectedIds.Contains(m.Id)).Select(m => m.ConflictGroup).Where(g => !string.IsNullOrEmpty(g)));
                var preferred = candidates.Where(m => string.IsNullOrEmpty(m.ConflictGroup) || !groups.Contains(m.ConflictGroup)).ToList();
                if (preferred.Count > 0 || active.Selection?.ConflictPolicy != MissionConflictPolicy.PreferGroupsThenRelax)
                    candidates = preferred;
                if (candidates.Count == 0) return MissionRerollStatus.NoCandidate;
                if (random == null) throw new InvalidOperationException("Reroll requires an injected random source.");
                MissionDefinition replacement = candidates[random.Next(candidates.Count)];
                MissionSaveData next = Clone(save);
                next.ActiveWindow.SelectedIds[active.SelectedIds.IndexOf(missionId)] = replacement.Id;
                next.ActiveWindow.Missions[replacement.Id] = new MissionProgressData();
                next.ActiveWindow.RerollsUsed++;
                Commit(next);
                StateChanged?.Invoke();
                return MissionRerollStatus.Rerolled;
            }
            finally { busy = false; }
        }

        public MissionProgressStatus ReportProgress(string eventKey, string eventId, long amount = 1) =>
            Report(eventKey, eventId, null, null, amount, null, true);

        public MissionProgressStatus ReportProgress(string eventKey, string eventId, string eventNamespace,
            string windowId, long amount = 1, string distinctValue = null) =>
            Report(eventKey, eventId, eventNamespace, windowId, amount, distinctValue, false);

        private MissionProgressStatus Report(string eventKey, string eventId, string eventNamespace,
            string windowId, long amount, string distinctValue, bool legacy)
        {
            if (busy) return MissionProgressStatus.Busy;
            if (string.IsNullOrWhiteSpace(eventKey) || string.IsNullOrWhiteSpace(eventId) || amount <= 0 ||
                (!legacy && (string.IsNullOrWhiteSpace(eventNamespace) || string.IsNullOrWhiteSpace(windowId))) ||
                (legacy && catalog.Period.Period != MissionPeriod.Permanent))
                return MissionProgressStatus.InvalidEvent;
            busy = true;
            try
            {
                AdvanceWindow();
                MissionWindowData active = save.ActiveWindow;
                if (!legacy && active.Id != windowId) return MissionProgressStatus.WrongWindow;
                string id = legacy ? eventId : RewardIdentity.Encode("mission-event", eventNamespace, windowId, eventId);
                if (active.ProcessedEventIds.Contains(id)) return MissionProgressStatus.AlreadyProcessed;
                var matching = active.Definitions.Where(m => active.SelectedIds.Contains(m.Id) && m.EventKey == eventKey).ToList();
                if (matching.Count == 0) return MissionProgressStatus.NoMatchingMission;
                if (matching.Any(m => m.Objective == MissionObjective.DistinctValue) && string.IsNullOrWhiteSpace(distinctValue))
                    return MissionProgressStatus.InvalidEvent;
                MissionSaveData next = Clone(save);
                foreach (MissionDefinition mission in matching)
                {
                    MissionProgressData progress = next.ActiveWindow.Missions[mission.Id];
                    if (progress.Claimed || progress.Progress >= mission.Target) continue;
                    if (mission.Objective == MissionObjective.DistinctValue)
                    {
                        if (progress.DistinctValues.Contains(distinctValue)) continue;
                        progress.DistinctValues.Add(distinctValue);
                        progress.Progress++;
                    }
                    else progress.Progress += Math.Min(amount, mission.Target - progress.Progress);
                }
                next.ActiveWindow.ProcessedEventIds.Add(id);
                if (active.Period.Period == MissionPeriod.Permanent &&
                    next.ActiveWindow.ProcessedEventIds.Count > historyLimit)
                    next.ActiveWindow.ProcessedEventIds.RemoveAt(0);
                Commit(next);
                StateChanged?.Invoke();
                return MissionProgressStatus.Updated;
            }
            finally { busy = false; }
        }

        public MissionClaimStatus Claim(string missionId)
        {
            if (busy) return MissionClaimStatus.Busy;
            busy = true;
            try
            {
                AdvanceWindow();
                MissionWindowData active = save.ActiveWindow;
                if (!active.SelectedIds.Contains(missionId)) return MissionClaimStatus.UnknownMission;
                MissionDefinition mission = active.Definitions.First(m => m.Id == missionId);
                MissionProgressData progress = active.Missions[missionId];
                if (progress.Claimed) return MissionClaimStatus.AlreadyClaimed;
                if (progress.Progress < mission.Target) return MissionClaimStatus.NotComplete;
                string transactionId = active.Period.Period == MissionPeriod.Permanent
                    ? $"mission:{catalog.CatalogId.Length}:{catalog.CatalogId}:{mission.Id.Length}:{mission.Id}:claim"
                    : RewardIdentity.Request(saveKey, "missions", catalog.CatalogId, active.Id, mission.Id);
                if (gateway == null)
                {
                    if (!wallet.TryGrant(new ResourceGrantRequest(transactionId, mission.Reward)))
                        return MissionClaimStatus.GrantFailed;
                    MissionSaveData next = Clone(save);
                    next.ActiveWindow.Missions[mission.Id].Claimed = true;
                    Commit(next);
                    StateChanged?.Invoke();
                    return MissionClaimStatus.Claimed;
                }
                MissionPendingClaim pending = save.PendingClaims.FirstOrDefault(p =>
                    p.Journal.RequestId == transactionId);
                if (pending == null)
                {
                    var bundle = mission.GetBundle();
                    string fingerprint = RewardIdentity.Fingerprint("mission-claim", transactionId, bundle.Fingerprint);
                    var coordinator = new RewardDeliveryCoordinator(gateway);
                    LastDeliveryResult = coordinator.Begin(transactionId, fingerprint, journal =>
                    {
                        MissionSaveData next = Clone(save);
                        next.PendingClaims.Add(new MissionPendingClaim
                        {
                            CatalogId = catalog.CatalogId, MissionId = missionId,
                            WindowSnapshot = Copy(active), Bundle = bundle, Journal = journal
                        });
                        Commit(next);
                    });
                    if (!LastDeliveryResult.IsSuccess) return DeliveryStatus();
                    pending = save.PendingClaims.First(p => p.Journal.RequestId == transactionId);
                }
                return Resume(pending);
            }
            finally { busy = false; }
        }

        public MissionClaimStatus RecoverClaims()
        {
            if (busy) return MissionClaimStatus.Busy;
            busy = true;
            try { return gateway == null ? MissionClaimStatus.GrantFailed : RecoverPending(); }
            finally { busy = false; }
        }

        private MissionClaimStatus RecoverPending()
        {
            MissionClaimStatus result = MissionClaimStatus.AlreadyClaimed;
            foreach (MissionPendingClaim pending in save.PendingClaims
                .Where(p => p.Journal.State != RewardJournalState.Completed).ToList())
            {
                MissionClaimStatus status = Resume(pending);
                if (status != MissionClaimStatus.Claimed) result = status;
                else if (result == MissionClaimStatus.AlreadyClaimed) result = status;
            }
            return result;
        }

        private MissionClaimStatus Resume(MissionPendingClaim pending)
        {
            string requestId = pending.Journal.RequestId;
            var coordinator = new RewardDeliveryCoordinator(gateway, pending.Journal);
            Action<RewardJournal> persist = journal =>
            {
                MissionSaveData next = Clone(save);
                next.PendingClaims.First(p => p.Journal.RequestId == requestId).Journal = journal;
                Commit(next);
            };
            if (pending.Journal.State == RewardJournalState.IntentPersisted)
            {
                LastDeliveryResult = coordinator.Resolve(pending.Bundle, persist);
                if (!LastDeliveryResult.IsSuccess) return DeliveryStatus();
            }
            LastDeliveryResult = coordinator.Deliver(persist);
            if (!LastDeliveryResult.IsSuccess) return DeliveryStatus();
            LastDeliveryResult = coordinator.Complete(journal =>
            {
                MissionSaveData next = Clone(save);
                MissionPendingClaim claim = next.PendingClaims.First(p => p.Journal.RequestId == requestId);
                claim.Journal = journal;
                claim.WindowSnapshot.Missions[claim.MissionId].Claimed = true;
                if (claim.CatalogId == next.CatalogId && claim.WindowSnapshot.Id == next.ActiveWindow.Id)
                    next.ActiveWindow.Missions[claim.MissionId].Claimed = true;
                Commit(next);
            });
            if (!LastDeliveryResult.IsSuccess) return DeliveryStatus();
            StateChanged?.Invoke();
            return MissionClaimStatus.Claimed;
        }

        private MissionClaimStatus DeliveryStatus() => LastDeliveryResult.Status == RewardDeliveryStatus.Pending
            ? MissionClaimStatus.Pending : MissionClaimStatus.GrantFailed;

        private void Commit(MissionSaveData next)
        {
            // Keep released save fields readable for Permanent migration and existing clients.
            next.Missions = next.ActiveWindow.Missions;
            next.ProcessedEventIds = next.ActiveWindow.ProcessedEventIds;
            datasave.Save(Clone(next), saveKey);
            save = next;
        }

        private static T Copy<T>(T data) => data == null ? default :
            JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(data));
        private static MissionSaveData Clone(MissionSaveData data) => data == null ? new MissionSaveData() : Copy(data);
    }
}
