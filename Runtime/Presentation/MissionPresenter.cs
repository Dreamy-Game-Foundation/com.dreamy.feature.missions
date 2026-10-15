using System;
using Dreamy.Feature;
using Dreamy.UI;

namespace Dreamy.Missions
{
    public sealed class MissionPresenter : FeaturePresenter, IPanelPresenter
    {
        private readonly IMissionService service;
        private readonly IMissionView view;
        public MissionPresenter(IMissionService service, IMissionView view)
        { this.service = service ?? throw new ArgumentNullException(nameof(service)); this.view = view ?? throw new ArgumentNullException(nameof(view)); }
        protected override void Bind()
        {
            service.StateChanged += Refresh;
            view.ClaimRequested += Claim;
            view.CloseRequested += Close;
        }
        public override void Refresh() => view.Render(service.GetState());
        protected override void Unbind()
        {
            service.StateChanged -= Refresh;
            view.ClaimRequested -= Claim;
            view.CloseRequested -= Close;
        }
        private void Claim(string id)
        {
            try { view.ShowClaimResult(service.Claim(id)); }
            finally { Refresh(); }
        }
        private void Close() { Dispose(); view.Close(); }
    }
}
