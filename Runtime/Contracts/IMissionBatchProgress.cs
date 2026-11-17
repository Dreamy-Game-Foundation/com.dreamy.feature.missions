using System.Collections.Generic;

namespace Dreamy.Missions
{
    /// <summary>Optional capability; external IMissionService adapters need not implement it.</summary>
    public interface IMissionBatchProgress
    {
        IReadOnlyList<MissionProgressStatus> ReportProgressBatch(IReadOnlyList<MissionProgressEvent> events);
    }

    /// <summary>An original event and the window captured when it occurred. Validation belongs to the model.</summary>
    public readonly struct MissionProgressEvent
    {
        public MissionProgressEvent(string eventKey, string originalId, string eventNamespace,
            string windowId, long amount = 1, string distinctValue = null)
        {
            EventKey = eventKey;
            OriginalId = originalId;
            EventNamespace = eventNamespace;
            WindowId = windowId;
            Amount = amount;
            DistinctValue = distinctValue;
        }

        public string EventKey { get; }
        public string OriginalId { get; }
        public string EventNamespace { get; }
        public string WindowId { get; }
        public long Amount { get; }
        public string DistinctValue { get; }
    }
}
