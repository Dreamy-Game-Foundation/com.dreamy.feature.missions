using System;
using Dreamy.Datasave;
using Dreamy.Economy.Rewards;
using Dreamy.Missions;
using Newtonsoft.Json;
using UnityEngine;

namespace Dreamy.Feature.Missions.PeriodicSample
{
    public sealed class PeriodicMissionDemoController : MonoBehaviour
    {
        public const string DailySaveKey = "dreamy.periodic-sample.daily.v1";
        public const string WeeklySaveKey = "dreamy.periodic-sample.weekly.v1";
        [SerializeField] private TextAsset dailyCatalog;
        [SerializeField] private TextAsset weeklyCatalog;
        [SerializeField] private PeriodicMissionPanel panel;
        private MissionModel daily;
        private MissionModel weekly;
        private IMissionClock clock;
        private bool weeklySelected;
        private bool listening;
        private MissionModel Selected => weeklySelected ? weekly : daily;

        // Call once after save, wallet and gateway initialization. There is deliberately no Awake bootstrap.
        public void Configure(IDatasaveService save, IRewardDeliveryGateway gateway, IMissionClock missionClock)
        {
            if (daily != null) throw new InvalidOperationException("Periodic sample is already configured.");
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (gateway == null) throw new ArgumentNullException(nameof(gateway));
            if (missionClock == null) throw new ArgumentNullException(nameof(missionClock));
            if (panel == null) throw new InvalidOperationException("Assign the periodic panel.");
            MissionCatalogConfig dailyConfig = ReadCatalog(dailyCatalog, MissionPeriod.Daily);
            MissionCatalogConfig weeklyConfig = ReadCatalog(weeklyCatalog, MissionPeriod.Weekly);
            var dailyModel = new MissionModel(dailyConfig, save, gateway, DailySaveKey,
                clock: missionClock, random: new DemoRandom());
            var weeklyModel = new MissionModel(weeklyConfig, save, gateway, WeeklySaveKey,
                clock: missionClock, random: new DemoRandom());
            daily = dailyModel;
            weekly = weeklyModel;
            clock = missionClock;
            if (isActiveAndEnabled) Attach();
        }

        private static MissionCatalogConfig ReadCatalog(TextAsset asset, MissionPeriod period)
        {
            if (asset == null) throw new InvalidOperationException($"Assign the {period} TextAsset catalog.");
            var config = JsonConvert.DeserializeObject<MissionCatalogConfig>(asset.text);
            if (config == null) throw new InvalidOperationException($"Empty catalog: {asset.name}");
            config.Initialize(asset.name);
            if (config.Period.Period != period || config.Period.FreeRerolls != 1)
                throw new InvalidOperationException($"The sample requires {period} with one free reroll.");
            return config;
        }

        private void OnEnable()
        {
            if (daily != null) Attach();
        }

        private void OnDisable()
        {
            if (!listening) return;
            panel.DailyRequested -= SelectDaily;
            panel.WeeklyRequested -= SelectWeekly;
            panel.WinRequested -= SimulateWin;
            panel.CollectRequested -= SimulateCollect;
            panel.RetryRequested -= Retry;
            panel.RefreshRequested -= Refresh;
            panel.RerollRequested -= Reroll;
            panel.Unbind();
            listening = false;
        }

        private void Attach()
        {
            if (listening) return;
            panel.DailyRequested += SelectDaily;
            panel.WeeklyRequested += SelectWeekly;
            panel.WinRequested += SimulateWin;
            panel.CollectRequested += SimulateCollect;
            panel.RetryRequested += Retry;
            panel.RefreshRequested += Refresh;
            panel.RerollRequested += Reroll;
            listening = true;
            Select(weeklySelected);
        }

        private void SelectDaily() => Select(false);
        private void SelectWeekly() => Select(true);

        private void Select(bool selectWeekly)
        {
            Run(() =>
            {
                weeklySelected = selectWeekly;
                panel.Bind(Selected, clock, weeklySelected);
            });
        }

        private void SimulateWin() => Report("stage.win", 1);
        private void SimulateCollect() => Report("coin.collect", 100);

        private void Report(string eventKey, long amount)
        {
            Run(() =>
            {
                // One click is one demo business event, delivered to both independent window owners.
                string eventId = Guid.NewGuid().ToString("N");
                daily.Refresh();
                weekly.Refresh();
                var day = daily.ReportProgress(eventKey, eventId, "periodic-sample", daily.WindowId, amount);
                var week = weekly.ReportProgress(eventKey, eventId, "periodic-sample", weekly.WindowId, amount);
                panel.ShowStatus($"DEMO {eventKey}: Daily {day}; Weekly {week}. No currency was collected by this button.");
            });
        }

        private void Reroll(string id) => Run(() => panel.ShowStatus($"Free reroll: {Selected.Reroll(id)}"));

        private void Retry()
        {
            Run(() =>
            {
                // Recover old-window claims too; never reconstruct a reward or request ID in the view.
                var day = daily.RecoverClaims();
                var week = weekly.RecoverClaims();
                var dayDelivery = daily.LastDeliveryResult;
                var weekDelivery = weekly.LastDeliveryResult;
                string dayDetail = dayDelivery.Reason == RewardDeliveryReason.None
                    ? string.Empty : $" ({dayDelivery.Status}/{dayDelivery.Reason})";
                string weekDetail = weekDelivery.Reason == RewardDeliveryReason.None
                    ? string.Empty : $" ({weekDelivery.Status}/{weekDelivery.Reason})";
                panel.ShowStatus($"Retry: Daily {day}{dayDetail}; Weekly {week}{weekDetail}.");
            });
        }

        private void Refresh()
        {
            Run(() =>
            {
                var day = daily.Refresh();
                var week = weekly.Refresh();
                panel.ShowStatus($"Domain refresh: Daily {day}; Weekly {week}");
            });
        }

        private void Run(Action action)
        {
            try { action(); }
            catch (Exception error) { panel.ShowError(error); }
        }

        private sealed class DemoRandom : IMissionRandom
        {
            private readonly System.Random random = new();
            public int Next(int exclusiveMaximum) => random.Next(exclusiveMaximum);
        }
    }
}
