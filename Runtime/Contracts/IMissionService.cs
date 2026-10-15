using System;
using System.Collections.Generic;
using Dreamy.Feature;

namespace Dreamy.Missions
{
    public interface IMissionService
    {
        event Action StateChanged;
        IReadOnlyList<MissionState> GetState();
        MissionProgressStatus ReportProgress(string eventKey, string eventId, long amount = 1);
        MissionProgressStatus ReportProgress(string eventKey, string eventId, string eventNamespace,
            string windowId, long amount = 1, string distinctValue = null);
        MissionClaimStatus Claim(string missionId);
        MissionRefreshStatus Refresh();
        MissionRerollStatus Reroll(string missionId);
        MissionClaimStatus RecoverClaims();
        string WindowId { get; }
    }

    public interface IMissionView : IFeatureView<IReadOnlyList<MissionState>>
    {
        event Action<string> ClaimRequested;
        event Action CloseRequested;
        void ShowClaimResult(MissionClaimStatus result);
        void Close();
    }

    public enum MissionProgressStatus { Updated, AlreadyProcessed, InvalidEvent, NoMatchingMission, Busy, WrongWindow }
    public enum MissionClaimStatus { Claimed, AlreadyClaimed, NotComplete, UnknownMission, GrantFailed, Busy, Pending }
    public enum MissionRefreshStatus { Unchanged, Updated, Busy }
    public enum MissionRerollStatus { Rerolled, UnknownMission, Ineligible, LimitReached, NoCandidate, Busy }

    public readonly struct MissionState
    {
        public MissionState(MissionDefinition definition, long progress, bool claimed)
        { Definition = definition; Progress = progress; IsClaimed = claimed; }
        public MissionDefinition Definition { get; }
        public long Progress { get; }
        public bool IsClaimed { get; }
        public bool IsComplete => Progress >= Definition.Target;
        public bool CanClaim => IsComplete && !IsClaimed;
    }
}
