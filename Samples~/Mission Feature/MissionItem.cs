using System;
using System.Text;
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
            if (state.Definition.RewardBundle == null)
                rewardText.text = $"+{state.Definition.RewardAmount} {state.Definition.RewardResourceId}";
            else
            {
                var text = new StringBuilder();
                foreach (var entry in state.Definition.RewardBundle.Entries)
                {
                    if (text.Length > 0) text.Append('\n');
                    text.Append('+').Append(entry.Amount).Append(' ').Append(entry.TargetId);
                }
                rewardText.text = text.ToString();
            }
            claimButton.interactable = state.CanClaim;
            claimLabel.text = state.IsClaimed ? "Claimed" : state.IsComplete ? "Claim" : "In progress";
        }
        private void RequestClaim() => ClaimRequested?.Invoke(missionId);
    }
}
