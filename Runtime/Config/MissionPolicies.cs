using System;
using System.Globalization;
using Dreamy.Economy.Rewards;

namespace Dreamy.Missions
{
    public enum MissionPeriod { Permanent, Daily, Weekly }
    public enum MissionObjective { Additive, DistinctValue }
    public enum MissionConflictPolicy { Strict, PreferGroupsThenRelax }

    public interface IMissionClock { DateTimeOffset UtcNow { get; } }
    public interface IMissionRandom { int Next(int exclusiveMaximum); }

    public sealed class MissionPeriodPolicy
    {
        public MissionPeriod Period { get; set; }
        public int OffsetMinutes { get; set; }
        public int CutoffMinutes { get; set; }
        public DayOfWeek WeekStart { get; set; } = DayOfWeek.Monday;
        public int FreeRerolls { get; set; } = 1;

        public MissionWindow GetWindow(DateTimeOffset now)
        {
            Validate();
            if (Period == MissionPeriod.Permanent) return new MissionWindow("permanent", 0, long.MaxValue);
            DateTime shifted = now.UtcDateTime.AddMinutes(OffsetMinutes - CutoffMinutes);
            DateTime start = shifted.Date;
            if (Period == MissionPeriod.Weekly)
                start = start.AddDays(-((7 + (int)start.DayOfWeek - (int)WeekStart) % 7));
            DateTime utcStart = start.AddMinutes(CutoffMinutes - OffsetMinutes);
            DateTime utcEnd = utcStart.AddDays(Period == MissionPeriod.Daily ? 1 : 7);
            string id = RewardIdentity.Encode("mission-window", Period.ToString(),
                utcStart.Ticks.ToString(CultureInfo.InvariantCulture), utcEnd.Ticks.ToString(CultureInfo.InvariantCulture));
            return new MissionWindow(id, utcStart.Ticks, utcEnd.Ticks);
        }

        internal void Validate()
        {
            if (!Enum.IsDefined(typeof(MissionPeriod), Period) || OffsetMinutes < -840 || OffsetMinutes > 840 ||
                CutoffMinutes < 0 || CutoffMinutes >= 1440 || !Enum.IsDefined(typeof(DayOfWeek), WeekStart) || FreeRerolls < 0)
                throw new ArgumentException("Invalid mission period policy.");
        }
    }

    public readonly struct MissionWindow
    {
        public MissionWindow(string id, long startUtcTicks, long endUtcTicks)
        { Id = id; StartUtcTicks = startUtcTicks; EndUtcTicks = endUtcTicks; }
        public string Id { get; }
        public long StartUtcTicks { get; }
        public long EndUtcTicks { get; }
    }

    public sealed class MissionSelectionPolicy
    {
        public string PoolId { get; set; }
        public int Count { get; set; }
        public MissionConflictPolicy ConflictPolicy { get; set; } = MissionConflictPolicy.Strict;
    }
}
