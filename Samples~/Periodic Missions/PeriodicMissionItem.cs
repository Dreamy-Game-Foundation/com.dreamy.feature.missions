using System;
using System.Text;
using Dreamy.Missions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dreamy.Feature.Missions.PeriodicSample
{
    public sealed class PeriodicMissionItem : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private TMP_Text rewardText;
        [SerializeField] private TMP_Text claimLabel;
        [SerializeField] private Button claimButton;
        [SerializeField] private Button rerollButton;
        private string missionId;

        public event Action<string> ClaimRequested;
        public event Action<string> RerollRequested;

        private void OnEnable()
        {
            claimButton.onClick.AddListener(RequestClaim);
            rerollButton.onClick.AddListener(RequestReroll);
        }

        private void OnDisable()
        {
            claimButton.onClick.RemoveListener(RequestClaim);
            rerollButton.onClick.RemoveListener(RequestReroll);
        }

        public void Render(MissionState state, bool freeRerollAvailable)
        {
            missionId = state.Definition.Id;
            titleText.text = state.Definition.TitleKey;
            progressText.text = $"{state.Progress} / {state.Definition.Target}";
            var rewards = new StringBuilder();
            if (state.Definition.RewardBundle == null)
                rewards.Append('+').Append(state.Definition.RewardAmount).Append(' ')
                    .Append(state.Definition.RewardResourceId);
            else
                foreach (var entry in state.Definition.RewardBundle.Entries)
                {
                    if (rewards.Length > 0) rewards.Append('\n');
                    rewards.Append('+').Append(entry.Amount).Append(' ').Append(entry.TargetId);
                    if (entry.KindId == DemoPeriodicItemDestination.DemoKindId) rewards.Append(" (DEMO item)");
                }
            rewardText.text = rewards.ToString();
            claimButton.interactable = state.CanClaim;
            claimLabel.text = state.IsClaimed ? "Claimed" : state.IsComplete ? "Claim / Retry" : "In progress";
            rerollButton.interactable = freeRerollAvailable && !state.IsComplete && !state.IsClaimed;
        }

        private void RequestClaim() => ClaimRequested?.Invoke(missionId);
        private void RequestReroll() => RerollRequested?.Invoke(missionId);
    }
}
