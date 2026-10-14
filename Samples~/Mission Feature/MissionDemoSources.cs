using System;
using Dreamy.Missions;

namespace Dreamy.Feature.Missions.Integration
{
    public sealed class MissionDemoClock : IMissionClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class MissionDemoRandom : IMissionRandom
    {
        private readonly Random random;
        public MissionDemoRandom(int seed = 20261014) => random = new Random(seed);
        public int Next(int exclusiveMaximum) => random.Next(exclusiveMaximum);
    }
}
