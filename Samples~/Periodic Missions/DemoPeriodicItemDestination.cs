using System;
using System.Collections.Generic;
using Dreamy.Datasave;
using Dreamy.Economy.Rewards;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dreamy.Feature.Missions.PeriodicSample
{
    // DEMO ONLY: one fake item, not an inventory, backend, or production reward adapter.
    public sealed class DemoPeriodicItemDestination : MonoBehaviour, IRewardDestination
    {
        public const string DemoKindId = "demo.periodic-item";
        public const string DemoTargetId = "demo.periodic-token";
        public const string DemoSaveKey = "dreamy.periodic-sample.demo-items.v1";
        [SerializeField] private Toggle pauseDeliveriesToggle;
        [SerializeField] private Toggle rejectDeliveriesToggle;
        [SerializeField] private TMP_Text balanceText;
        private IDatasaveService save;
        private bool busy;
        public string KindId => DemoKindId;

        public void Configure(IDatasaveService datasave)
        {
            if (save != null) throw new InvalidOperationException("Demo item destination is already configured.");
            save = datasave ?? throw new ArgumentNullException(nameof(datasave));
            ShowBalance(Load());
        }

        public RewardDeliveryResult Validate(IReadOnlyList<RewardDeliveryRequest> requests)
        {
            if (save == null) return RewardDeliveryResult.Pending(RewardDeliveryReason.DestinationUnavailable);
            if (busy) return RewardDeliveryResult.Pending(RewardDeliveryReason.Busy);
            busy = true;
            try { return Validate(Load(), requests); }
            finally { busy = false; }
        }

        public RewardDeliveryResult Deliver(RewardDeliveryRequest request)
        {
            if (save == null) return RewardDeliveryResult.Pending(RewardDeliveryReason.DestinationUnavailable);
            if (busy) return RewardDeliveryResult.Pending(RewardDeliveryReason.Busy);
            busy = true;
            try
            {
                // Reload on every attempt, including after an ambiguous save failure.
                // Count + fingerprint receipt are committed in the SAME owner document.
                DemoPeriodicItemSaveData candidate = Load();
                RewardDeliveryResult validation = Validate(candidate, new[] { request });
                if (!validation.IsSuccess) return validation;
                if (candidate.Receipts.ContainsKey(request.TransactionId))
                    return RewardDeliveryResult.AlreadyDelivered();
                if (rejectDeliveriesToggle.isOn)
                    return RewardDeliveryResult.Failed(RewardDeliveryReason.DestinationUnavailable);
                if (pauseDeliveriesToggle.isOn)
                    return RewardDeliveryResult.Pending(RewardDeliveryReason.DestinationUnavailable);
                candidate.Count += request.Entry.Amount;
                candidate.Receipts.Add(request.TransactionId, request.Fingerprint);
                try { save.Save(candidate, DemoSaveKey); }
                catch (Exception) { return RewardDeliveryResult.Pending(RewardDeliveryReason.PersistenceFailed); }
                ShowBalance(candidate);
                return RewardDeliveryResult.Delivered();
            }
            finally { busy = false; }
        }

        private static RewardDeliveryResult Validate(DemoPeriodicItemSaveData state,
            IReadOnlyList<RewardDeliveryRequest> requests)
        {
            if (requests == null || requests.Count == 0)
                return RewardDeliveryResult.Failed(RewardDeliveryReason.InvalidRequest);
            long total = state.Count;
            var seen = new Dictionary<string, string>(state.Receipts, StringComparer.Ordinal);
            foreach (RewardDeliveryRequest request in requests)
            {
                if (!request.IsValid || request.Entry.KindId != DemoKindId)
                    return RewardDeliveryResult.Failed(RewardDeliveryReason.InvalidRequest);
                if (request.Entry.TargetId != DemoTargetId)
                    return RewardDeliveryResult.Failed(RewardDeliveryReason.UnknownTarget);
                if (seen.TryGetValue(request.TransactionId, out string fingerprint))
                {
                    if (fingerprint != request.Fingerprint)
                        return RewardDeliveryResult.Failed(RewardDeliveryReason.PayloadMismatch);
                    continue;
                }
                if (total > long.MaxValue - request.Entry.Amount)
                    return RewardDeliveryResult.Failed(RewardDeliveryReason.Overflow);
                total += request.Entry.Amount;
                seen.Add(request.TransactionId, request.Fingerprint);
            }
            // Failure toggles affect delivery, not preflight: currency can already have been delivered.
            return RewardDeliveryResult.Delivered();
        }

        private DemoPeriodicItemSaveData Load()
        {
            return JsonConvert.DeserializeObject<DemoPeriodicItemSaveData>(
                JsonConvert.SerializeObject(save.Load<DemoPeriodicItemSaveData>(DemoSaveKey)));
        }

        private void ShowBalance(DemoPeriodicItemSaveData state) =>
            balanceText.text = $"DEMO tokens owned: {state.Count}";
    }

    [Serializable]
    public sealed class DemoPeriodicItemSaveData : SaveData
    {
        [JsonProperty] public long Count { get; set; }
        [JsonProperty] public Dictionary<string, string> Receipts { get; set; } = new(StringComparer.Ordinal);
    }
}
