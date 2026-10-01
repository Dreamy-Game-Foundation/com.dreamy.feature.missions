using System;
using Dreamy.Missions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dreamy.Feature.Missions.Integration
{
    public sealed class MissionItem : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private TMP_Text rewardText;
        [SerializeField] private TMP_Text claimLabel;
        [SerializeField] private Button claimButton;
        private string missionId;
        public event Action<string> ClaimRequested;
        private void OnEnable() => claimButton.onClick.AddListener(RequestClaim);
        private void OnDisable() => claimButton.onClick.RemoveListener(RequestClaim);
        public void Render(MissionState state)
        {
            missionId = state.Definition.Id;
            titleText.text = state.Definition.TitleKey;
            progressText.text = $"{state.Progress} / {state.Definition.Target}";
            rewardText.text = $"+{state.Definition.RewardAmount} {state.Definition.RewardResourceId}";
            claimButton.interactable = state.CanClaim;
            claimLabel.text = state.IsClaimed ? "Claimed" : state.IsComplete ? "Claim" : "In progress";
        }
        private void RequestClaim() => ClaimRequested?.Invoke(missionId);
    }
}
