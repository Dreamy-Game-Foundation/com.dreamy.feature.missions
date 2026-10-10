using System;
using Dreamy.Missions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dreamy.Feature.Missions.Integration
{
    /// <summary>Optional simulated gameplay controls for the mission sample.</summary>
    public sealed class MissionDemoControls : MonoBehaviour
    {
        [SerializeField] private Button winStageButton;
        [SerializeField] private Button collectCoinsButton;
        [SerializeField] private TMP_Text statusText;
        private IMissionService service;
        public void Configure(IMissionService missions) => service = missions ?? throw new ArgumentNullException(nameof(missions));
        private void OnEnable()
        {
            winStageButton.onClick.AddListener(WinStage);
            collectCoinsButton.onClick.AddListener(CollectCoins);
        }
        private void OnDisable()
        {
            winStageButton.onClick.RemoveListener(WinStage);
            collectCoinsButton.onClick.RemoveListener(CollectCoins);
        }
        private void WinStage() => Report("stage.win", 1);
        private void CollectCoins() => Report("coin.collect", 100);
        private void Report(string key, long amount)
        {
            if (service == null) return;
            statusText.text = service.ReportProgress(key, "mission-demo/" + Guid.NewGuid().ToString("N"), amount).ToString();
        }
    }
}
