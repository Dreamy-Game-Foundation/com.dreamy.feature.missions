using System;
using System.Collections.Generic;
using Dreamy.Economy.Rewards;

namespace Dreamy.Missions
{
    /// <summary>The instance cannot mutate until reconstructed from authoritative persistence.</summary>
    public sealed class MissionReloadRequiredException : InvalidOperationException
    {
        public MissionReloadRequiredException() : base(
            "Mission persistence outcome is unknown. Reload from authoritative storage before retrying original event IDs.") { }
    }

    public sealed partial class MissionModel
    {
        /// <summary>
        /// A save threw and may have written. Discard this instance and reconstruct with an authoritative
        /// store before retrying the original IDs. Reads return the last published snapshot without refresh.
        /// A cached datasave service must itself be recreated/invalidated before reconstruction.
        /// </summary>
        public bool RequiresReload { get; private set; }

        private void EnsureWritable()
        {
            if (RequiresReload)
                throw new MissionReloadRequiredException();
        }

        /// <summary>
        /// Applies inputs in order, with one period evaluation, at most one save and one notification.
        /// A persistence exception returns no statuses and sets RequiresReload. Subscriber exceptions occur
        /// after commit and do not set RequiresReload. Permanent replay protection is bounded by historyLimit.
        /// </summary>
        public IReadOnlyList<MissionProgressStatus> ReportProgressBatch(IReadOnlyList<MissionProgressEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            var results = new MissionProgressStatus[events.Count];
            if (busy)
            {
                Array.Fill(results, MissionProgressStatus.Busy);
                return Array.AsReadOnly(results);
            }
            EnsureWritable();
            if (events.Count == 0) return Array.AsReadOnly(results);
            busy = true;
            try
            {
                bool anyValid = false;
                for (int i = 0; i < events.Count; i++)
                {
                    results[i] = MissionProgressStatus.InvalidEvent;
                    anyValid |= IsValid(events[i]);
                }
                if (!anyValid) return Array.AsReadOnly(results);

                MissionSaveData next = null;
                MissionWindowData active = save.ActiveWindow;
                bool changed = false;
                if (catalog.Period.Period != MissionPeriod.Permanent)
                {
                    MissionWindow current = catalog.Period.GetWindow(clock.UtcNow);
                    if (current.StartUtcTicks > active.StartUtcTicks)
                    {
                        next = Clone(save);
                        next.ActiveWindow = CreateWindow(current);
                        active = next.ActiveWindow;
                        changed = true;
                    }
                }
                // Keep all seen IDs for this call even when Permanent evicts an earlier receipt.
                var seen = new HashSet<string>(active.ProcessedEventIds, StringComparer.Ordinal);
                for (int i = 0; i < events.Count; i++)
                {
                    MissionProgressEvent input = events[i];
                    if (!IsValid(input)) continue;
                    if (input.WindowId != active.Id) { results[i] = MissionProgressStatus.WrongWindow; continue; }
                    string id = RewardIdentity.Encode("mission-event", input.EventNamespace, input.WindowId, input.OriginalId);
                    if (seen.Contains(id)) { results[i] = MissionProgressStatus.AlreadyProcessed; continue; }

                    bool matching = false;
                    bool needsDistinct = false;
                    foreach (MissionDefinition mission in active.Definitions)
                    {
                        if (mission.EventKey != input.EventKey || !active.SelectedIds.Contains(mission.Id)) continue;
                        matching = true;
                        needsDistinct |= mission.Objective == MissionObjective.DistinctValue;
                    }
                    if (!matching) { results[i] = MissionProgressStatus.NoMatchingMission; continue; }
                    if (needsDistinct && string.IsNullOrWhiteSpace(input.DistinctValue)) continue;
                    if (next == null)
                    {
                        next = Clone(save);
                        active = next.ActiveWindow;
                    }
                    foreach (MissionDefinition mission in active.Definitions)
                    {
                        if (mission.EventKey != input.EventKey || !active.SelectedIds.Contains(mission.Id)) continue;
                        MissionProgressData progress = active.Missions[mission.Id];
                        if (progress.Claimed || progress.Progress >= mission.Target) continue;
                        if (mission.Objective == MissionObjective.DistinctValue)
                        {
                            if (progress.DistinctValues.Contains(input.DistinctValue)) continue;
                            progress.DistinctValues.Add(input.DistinctValue);
                            progress.Progress++;
                        }
                        else progress.Progress += Math.Min(input.Amount, mission.Target - progress.Progress);
                    }
                    seen.Add(id);
                    active.ProcessedEventIds.Add(id);
                    if (active.Period.Period == MissionPeriod.Permanent && active.ProcessedEventIds.Count > historyLimit)
                        active.ProcessedEventIds.RemoveAt(0);
                    results[i] = MissionProgressStatus.Updated;
                    changed = true;
                }
                if (changed)
                {
                    Commit(next);
                    StateChanged?.Invoke();
                }
                return Array.AsReadOnly(results);
            }
            finally { busy = false; }
        }

        private static bool IsValid(MissionProgressEvent input) =>
            !string.IsNullOrWhiteSpace(input.EventKey) && !string.IsNullOrWhiteSpace(input.OriginalId) &&
            !string.IsNullOrWhiteSpace(input.EventNamespace) && !string.IsNullOrWhiteSpace(input.WindowId) && input.Amount > 0;
    }
}
