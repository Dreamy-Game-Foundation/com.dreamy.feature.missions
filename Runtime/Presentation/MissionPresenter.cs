using System;
using Dreamy.UI;

namespace Dreamy.Missions
{
    public sealed class MissionPresenter : IPanelPresenter
    {
        private readonly IMissionService service;
        private readonly IMissionView view;
        private bool bound;
        public MissionPresenter(IMissionService service, IMissionView view)
        { this.service = service ?? throw new ArgumentNullException(nameof(service)); this.view = view ?? throw new ArgumentNullException(nameof(view)); }
        public void Show()
        {
            if (!bound)
            {
                service.StateChanged += Refresh;
                view.ClaimRequested += Claim;
                view.CloseRequested += Close;
                bound = true;
            }
            Refresh();
        }
        public void Refresh() => view.Render(service.GetState());
        public void Dispose()
        {
            if (!bound) return;
            service.StateChanged -= Refresh;
            view.ClaimRequested -= Claim;
            view.CloseRequested -= Close;
            bound = false;
        }
        private void Claim(string id)
        {
            try { view.ShowClaimResult(service.Claim(id)); }
            finally { Refresh(); }
        }
        private void Close() { Dispose(); view.Close(); }
    }
}
