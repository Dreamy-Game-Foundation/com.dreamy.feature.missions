using System;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using Dreamy.Missions;
using Dreamy.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dreamy.Feature.Missions.Integration
{
    public sealed class MissionPanel : UIPanel, IMissionView
    {
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Transform itemContainer;
        [SerializeField] private MissionItem itemPrefab;
        private readonly List<MissionItem> items = new();
        private MissionPresenter presenter;
        public override bool CanBack => true;
        public event Action<string> ClaimRequested;
        public event Action CloseRequested;
        public void Configure(IMissionService service)
        {
            presenter?.Dispose();
            presenter = new MissionPresenter(service, this);
            if (isActiveAndEnabled) presenter.Show();
        }
        private void OnEnable()
        {
            closeButton.onClick.AddListener(RequestClose);
            presenter?.Show();
        }
        protected override void OnDisable()
        {
            closeButton.onClick.RemoveListener(RequestClose);
            presenter?.Dispose();
            base.OnDisable();
        }
        public void Render(IReadOnlyList<MissionState> state)
        {
            while (items.Count < state.Count)
            {
                MissionItem item = Instantiate(itemPrefab, itemContainer);
                item.ClaimRequested += RequestClaim;
                items.Add(item);
            }
            for (int i = 0; i < items.Count; i++)
            {
                items[i].gameObject.SetActive(i < state.Count);
                if (i < state.Count) items[i].Render(state[i]);
            }
        }
        public void ShowClaimResult(MissionClaimStatus result) =>
            statusText.text = result == MissionClaimStatus.Claimed ? "Reward received" : result.ToString();
        public void Close() => Hide().Forget();
        protected override void OnDestroy()
        {
            presenter?.Dispose();
            foreach (MissionItem item in items) if (item != null) item.ClaimRequested -= RequestClaim;
            base.OnDestroy();
        }
        private void RequestClaim(string id) => ClaimRequested?.Invoke(id);
        private void RequestClose() => CloseRequested?.Invoke();
    }
}
