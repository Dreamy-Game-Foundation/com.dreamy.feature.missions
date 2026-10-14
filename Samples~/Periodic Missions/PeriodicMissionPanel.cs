using System;
using System.Collections.Generic;
using Dreamy.Missions;
using Dreamy.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dreamy.Feature.Missions.PeriodicSample
{
    // This sample owns its presenter directly. Do not also register it in a presenter factory.
    public sealed class PeriodicMissionPanel : UIPanel, IMissionView
    {
        [SerializeField] private Button closeButton;
        [SerializeField] private Button dailyTabButton;
        [SerializeField] private Button weeklyTabButton;
        [SerializeField] private Button winButton;
        [SerializeField] private Button collectButton;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button refreshButton;
        [SerializeField] private TMP_Text headingText;
        [SerializeField] private TMP_Text countdownText;
        [SerializeField] private TMP_Text rerollText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Transform itemContainer;
        [SerializeField] private PeriodicMissionItem itemPrefab;
        private readonly List<PeriodicMissionItem> items = new();
        private MissionModel model;
        private IMissionClock clock;
        private MissionPresenter presenter;
        private long observedSecond = long.MinValue;
        private long displayedSeconds = long.MinValue;
        private string displayedWindow;

        public override bool CanBack => true;
        public override bool CanCache => true;
        public event Action<string> ClaimRequested;
        public event Action CloseRequested;
        public event Action DailyRequested;
        public event Action WeeklyRequested;
        public event Action WinRequested;
        public event Action CollectRequested;
        public event Action RetryRequested;
        public event Action RefreshRequested;
        public event Action<string> RerollRequested;

        public void Bind(MissionModel service, IMissionClock missionClock, bool weekly)
        {
            Unbind();
            model = service ?? throw new ArgumentNullException(nameof(service));
            clock = missionClock ?? throw new ArgumentNullException(nameof(missionClock));
            headingText.text = weekly ? "Weekly Missions" : "Daily Missions";
            dailyTabButton.interactable = weekly;
            weeklyTabButton.interactable = !weekly;
            statusText.text = "Demo progress only. Rewards use the injected gateway.";
            if (isActiveAndEnabled) BindPresenter();
        }

        public void Unbind()
        {
            presenter?.Dispose();
            presenter = null;
            model = null;
            clock = null;
            observedSecond = long.MinValue;
            displayedSeconds = long.MinValue;
            displayedWindow = null;
        }

        private void OnEnable()
        {
            closeButton.onClick.AddListener(RequestClose);
            dailyTabButton.onClick.AddListener(RequestDaily);
            weeklyTabButton.onClick.AddListener(RequestWeekly);
            winButton.onClick.AddListener(RequestWin);
            collectButton.onClick.AddListener(RequestCollect);
            retryButton.onClick.AddListener(RequestRetry);
            refreshButton.onClick.AddListener(RequestRefresh);
            if (model != null && presenter == null) BindPresenter();
        }

        protected override void OnDisable()
        {
            closeButton.onClick.RemoveListener(RequestClose);
            dailyTabButton.onClick.RemoveListener(RequestDaily);
            weeklyTabButton.onClick.RemoveListener(RequestWeekly);
            winButton.onClick.RemoveListener(RequestWin);
            collectButton.onClick.RemoveListener(RequestCollect);
            retryButton.onClick.RemoveListener(RequestRetry);
            refreshButton.onClick.RemoveListener(RequestRefresh);
            presenter?.Dispose();
            presenter = null;
            observedSecond = long.MinValue;
            displayedSeconds = long.MinValue;
            base.OnDisable();
        }

        private void BindPresenter()
        {
            presenter = new MissionPresenter(model, this);
            presenter.Show();
            if (model.LastDeliveryResult.Reason != Dreamy.Economy.Rewards.RewardDeliveryReason.None)
                ShowDelivery("Recovered delivery");
            Tick();
        }

        private void Update()
        {
            if (presenter == null) return;
            try { Tick(); }
            catch (Exception error) { ShowError(error); }
        }

        private void Tick()
        {
            long nowTicks = clock.UtcNow.UtcDateTime.Ticks;
            long second = nowTicks / TimeSpan.TicksPerSecond;
            if (observedSecond == second) return;
            observedSecond = second;
            // Domain alone chooses whether to roll over. No GetState or row rebuild on this timer.
            model.Refresh();
            MissionWindow window = model.Window;
            long remainingTicks = Math.Max(0, window.EndUtcTicks - nowTicks);
            long remaining = remainingTicks / TimeSpan.TicksPerSecond +
                (remainingTicks % TimeSpan.TicksPerSecond == 0 ? 0 : 1);
            if (displayedSeconds == remaining && displayedWindow == window.Id) return;
            displayedSeconds = remaining;
            displayedWindow = window.Id;
            TimeSpan time = TimeSpan.FromSeconds(remaining);
            countdownText.text = $"Resets in {time.Days}d {time.Hours:00}:{time.Minutes:00}:{time.Seconds:00}";
        }

        public void Render(IReadOnlyList<MissionState> state)
        {
            while (items.Count < state.Count)
            {
                PeriodicMissionItem item = Instantiate(itemPrefab, itemContainer);
                item.ClaimRequested += RequestClaim;
                item.RerollRequested += RequestReroll;
                items.Add(item);
            }
            // Both supplied catalogs permit exactly one free reroll per window.
            bool free = model.RerollsUsed < 1;
            rerollText.text = free ? "1 free reroll remaining" : "Free reroll used";
            for (int i = 0; i < items.Count; i++)
            {
                items[i].gameObject.SetActive(i < state.Count);
                if (i < state.Count) items[i].Render(state[i], free);
            }
        }

        public void ShowClaimResult(MissionClaimStatus result)
        {
            if (result == MissionClaimStatus.Pending || result == MissionClaimStatus.GrantFailed)
                ShowDelivery(result.ToString());
            else statusText.text = result == MissionClaimStatus.Claimed ? "Reward received" : result.ToString();
        }

        private void ShowDelivery(string result)
        {
            var delivery = model.LastDeliveryResult;
            statusText.text = $"{result}: {delivery.Status} / {delivery.Reason}. " +
                (delivery.Retryable ? "Retry pending after fixing the destination." : "Inspect the error before retrying.");
        }

        public void ShowStatus(string message) => statusText.text = message;
        public void ShowError(Exception error) => statusText.text = $"Error: {error.Message}";

        // Standalone activation lifecycle: host reopens with gameObject.SetActive(true).
        public void Close() => gameObject.SetActive(false);

        protected override void OnDestroy()
        {
            Unbind();
            foreach (PeriodicMissionItem item in items)
                if (item != null)
                {
                    item.ClaimRequested -= RequestClaim;
                    item.RerollRequested -= RequestReroll;
                }
            base.OnDestroy();
        }

        private void RequestClaim(string id)
        {
            try { ClaimRequested?.Invoke(id); }
            catch (Exception error) { ShowError(error); }
        }

        private void RequestClose() => CloseRequested?.Invoke();
        private void RequestDaily() => DailyRequested?.Invoke();
        private void RequestWeekly() => WeeklyRequested?.Invoke();
        private void RequestWin() => WinRequested?.Invoke();
        private void RequestCollect() => CollectRequested?.Invoke();
        private void RequestRetry() => RetryRequested?.Invoke();
        private void RequestRefresh() => RefreshRequested?.Invoke();
        private void RequestReroll(string id) => RerollRequested?.Invoke(id);
    }
}
