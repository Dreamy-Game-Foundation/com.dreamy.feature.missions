using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Dreamy.Missions.Tests
{
    public sealed class MissionPresenterTests
    {
        [Test]
        public void ShowDisposeAndReopen_KeepOneWriterAndReleaseAllListeners()
        {
            var service = new Service();
            var view = new View();
            var presenter = new MissionPresenter(service, view);
            presenter.Show();
            presenter.Show();
            Assert.That(service.ListenerCount, Is.EqualTo(1));
            Assert.That(view.ClaimListenerCount, Is.EqualTo(1));
            view.RequestClaim();
            Assert.That(service.Claims, Is.EqualTo(1));
            view.RequestClose();
            Assert.That(view.Closes, Is.EqualTo(1));
            Assert.That(service.ListenerCount, Is.Zero);
            Assert.That(view.ClaimListenerCount, Is.Zero);
            int renders = view.Renders;
            service.Raise();
            Assert.That(view.Renders, Is.EqualTo(renders));
            presenter.Dispose();
            presenter.Show();
            view.RequestClaim();
            Assert.That(service.Claims, Is.EqualTo(2));
            presenter.Dispose();
            Assert.That(service.ListenerCount, Is.Zero);
            Assert.That(view.ClaimListenerCount, Is.Zero);
        }

        [Test]
        public void RenderFailureDuringShow_UnsubscribesServiceAndViewBeforeRetry()
        {
            var service = new Service();
            var view = new View { FailRender = true };
            var presenter = new MissionPresenter(service, view);
            Assert.Throws<InvalidOperationException>(() => presenter.Show());
            Assert.That(service.ListenerCount, Is.Zero);
            Assert.That(view.ClaimListenerCount, Is.Zero);
            view.FailRender = false;
            presenter.Show();
            Assert.That(service.ListenerCount, Is.EqualTo(1));
            Assert.That(view.ClaimListenerCount, Is.EqualTo(1));
            presenter.Dispose();
        }

        private sealed class View : IMissionView
        {
            public event Action<string> ClaimRequested;
            public event Action CloseRequested;
            public int ClaimListenerCount => ClaimRequested?.GetInvocationList().Length ?? 0;
            public int Renders { get; private set; }
            public int Closes { get; private set; }
            public bool FailRender { get; set; }
            public void Render(IReadOnlyList<MissionState> state)
            {
                if (FailRender) throw new InvalidOperationException("Injected view failure.");
                Renders++;
            }
            public void ShowClaimResult(MissionClaimStatus result) { }
            public void Close() => Closes++;
            public void RequestClaim() => ClaimRequested?.Invoke("mission");
            public void RequestClose() => CloseRequested?.Invoke();
        }

        private sealed class Service : IMissionService
        {
            public event Action StateChanged;
            public int ListenerCount => StateChanged?.GetInvocationList().Length ?? 0;
            public int Claims { get; private set; }
            public string WindowId => "window";
            public IReadOnlyList<MissionState> GetState() => Array.Empty<MissionState>();
            public void Raise() => StateChanged?.Invoke();
            public MissionClaimStatus Claim(string id) { Claims++; return MissionClaimStatus.Claimed; }
            public MissionProgressStatus ReportProgress(string key, string id, long amount = 1) => MissionProgressStatus.NoMatchingMission;
            public MissionProgressStatus ReportProgress(string key, string id, string ns, string window,
                long amount = 1, string distinctValue = null) => MissionProgressStatus.NoMatchingMission;
            public MissionRefreshStatus Refresh() => MissionRefreshStatus.Unchanged;
            public MissionRerollStatus Reroll(string id) => MissionRerollStatus.Ineligible;
            public MissionClaimStatus RecoverClaims() => MissionClaimStatus.AlreadyClaimed;
        }
    }
}
