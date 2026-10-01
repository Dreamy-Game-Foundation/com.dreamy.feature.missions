using System;
using System.Collections.Generic;

namespace Dreamy.Missions
{
    public interface IMissionService
    {
        event Action StateChanged;
        IReadOnlyList<MissionState> GetState();
        MissionProgressStatus ReportProgress(string eventKey, string eventId, long amount = 1);
        MissionClaimStatus Claim(string missionId);
    }

    public interface IMissionView
    {
        event Action<string> ClaimRequested;
        event Action CloseRequested;
        void Render(IReadOnlyList<MissionState> state);
        void ShowClaimResult(MissionClaimStatus result);
        void Close();
    }

    public enum MissionProgressStatus { Updated, AlreadyProcessed, InvalidEvent, NoMatchingMission, Busy }
    public enum MissionClaimStatus { Claimed, AlreadyClaimed, NotComplete, UnknownMission, GrantFailed, Busy }

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
