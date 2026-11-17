using System.Collections.Generic;

namespace Dreamy.Missions
{
    // Audited against save schema v2. Definition contains only scalars/strings and the immutable
    // ResolvedRewardBundle (defensive constructor copy, read-only entries of readonly structs).
    // Definition nodes may be shared; all mutable containers, policies and journals must be detached.
    internal static class MissionSaveCopy
    {
        internal static MissionSaveData Detach(MissionSaveData source)
        {
            if (source == null) return new MissionSaveData();
            var copy = new MissionSaveData
            {
                CatalogId = source.CatalogId,
                ActiveWindow = Window(source.ActiveWindow),
                PendingClaims = source.PendingClaims == null ? null : new List<MissionPendingClaim>(source.PendingClaims.Count)
            };
            copy.Missions = source.ActiveWindow != null && ReferenceEquals(source.Missions, source.ActiveWindow.Missions)
                ? copy.ActiveWindow.Missions : Progress(source.Missions);
            copy.ProcessedEventIds = source.ActiveWindow != null && ReferenceEquals(source.ProcessedEventIds, source.ActiveWindow.ProcessedEventIds)
                ? copy.ActiveWindow.ProcessedEventIds : List(source.ProcessedEventIds);
            if (source.PendingClaims != null)
                foreach (MissionPendingClaim claim in source.PendingClaims)
                    copy.PendingClaims.Add(claim == null ? null : new MissionPendingClaim
                    {
                        CatalogId = claim.CatalogId, MissionId = claim.MissionId,
                        WindowSnapshot = Window(claim.WindowSnapshot), Bundle = claim.Bundle, Journal = claim.Journal?.Clone()
                    });
            return copy;
        }

        private static MissionWindowData Window(MissionWindowData source) => source == null ? null : new MissionWindowData
        {
            Id = source.Id, StartUtcTicks = source.StartUtcTicks, EndUtcTicks = source.EndUtcTicks,
            Revision = source.Revision, RerollsUsed = source.RerollsUsed,
            Period = source.Period == null ? null : new MissionPeriodPolicy
            {
                Period = source.Period.Period, OffsetMinutes = source.Period.OffsetMinutes,
                CutoffMinutes = source.Period.CutoffMinutes, WeekStart = source.Period.WeekStart,
                FreeRerolls = source.Period.FreeRerolls
            },
            Selection = source.Selection == null ? null : new MissionSelectionPolicy
            {
                PoolId = source.Selection.PoolId, Count = source.Selection.Count, ConflictPolicy = source.Selection.ConflictPolicy
            },
            Definitions = source.Definitions == null ? null : new List<MissionDefinition>(source.Definitions),
            SelectedIds = List(source.SelectedIds), Missions = Progress(source.Missions), ProcessedEventIds = List(source.ProcessedEventIds)
        };

        private static List<string> List(List<string> source) => source == null ? null : new List<string>(source);

        private static Dictionary<string, MissionProgressData> Progress(Dictionary<string, MissionProgressData> source)
        {
            if (source == null) return null;
            var copy = new Dictionary<string, MissionProgressData>(source.Count, source.Comparer);
            foreach (var pair in source)
                copy.Add(pair.Key, pair.Value == null ? null : new MissionProgressData
                {
                    Progress = pair.Value.Progress, Claimed = pair.Value.Claimed, DistinctValues = List(pair.Value.DistinctValues)
                });
            return copy;
        }
    }
}
