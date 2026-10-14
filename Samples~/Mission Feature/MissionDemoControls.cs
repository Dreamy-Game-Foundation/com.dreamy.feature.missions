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
        [SerializeField] private Button rerollButton;
        [SerializeField] private Button refreshButton;
        private IMissionService service;
        public void Configure(IMissionService missions) => service = missions ?? throw new ArgumentNullException(nameof(missions));
        private void OnEnable()
        {
            winStageButton.onClick.AddListener(WinStage);
            collectCoinsButton.onClick.AddListener(CollectCoins);
            if (rerollButton != null) rerollButton.onClick.AddListener(Reroll);
            if (refreshButton != null) refreshButton.onClick.AddListener(RefreshWindow);
        }
        private void OnDisable()
        {
            winStageButton.onClick.RemoveListener(WinStage);
            collectCoinsButton.onClick.RemoveListener(CollectCoins);
            if (rerollButton != null) rerollButton.onClick.RemoveListener(Reroll);
            if (refreshButton != null) refreshButton.onClick.RemoveListener(RefreshWindow);
        }
        private void WinStage() => Report("stage.win", 1);
        private void CollectCoins() => Report("coin.collect", 100);
        private void Report(string key, long amount)
        {
            if (service == null) return;
            statusText.text = service.ReportProgress(key, Guid.NewGuid().ToString("N"),
                "mission-demo", service.WindowId, amount).ToString();
        }
        private void Reroll()
        {
            if (service == null) return;
            foreach (var state in service.GetState())
                if (!state.IsComplete && !state.IsClaimed)
                {
                    statusText.text = service.Reroll(state.Definition.Id).ToString();
                    return;
                }
            statusText.text = "No incomplete mission to reroll";
        }
        private void RefreshWindow()
        {
            if (service == null) return;
            statusText.text = $"{service.Refresh()} | {service.RecoverClaims()}";
        }
    }
}
