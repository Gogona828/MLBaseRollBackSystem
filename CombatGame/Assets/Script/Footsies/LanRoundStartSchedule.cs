using System;

namespace Footsies
{
    // Uses monotonic clocks. No assumption that the two PCs have matching system time.
    public sealed class LanRoundStartSchedule
    {
        public bool HasClockSample { get; private set; }
        public double ServerOffset { get; private set; }
        public double BestRoundTrip { get; private set; } = double.PositiveInfinity;
        public int Round { get; private set; } = -1;
        public int NextFrame { get; private set; } = -1;
        public double LocalDeadline { get; private set; } = double.PositiveInfinity;
        public int ReleasedRound { get; private set; } = -1;

        public void ObserveClock(double sentAt, double serverTime, double receivedAt)
        {
            double rtt = receivedAt-sentAt;
            if(!Finite(sentAt) || !Finite(serverTime) || !Finite(receivedAt) || rtt < 0 || rtt > 2) return;
            if(rtt <= BestRoundTrip+0.001)
            {
                ServerOffset=serverTime-(sentAt+receivedAt)*0.5;
                BestRoundTrip=Math.Min(BestRoundTrip,rtt);
                HasClockSample=true;
            }
        }

        public bool Schedule(int round, int expectedRound, int nextFrame, int currentFrame, double serverDeadline)
        {
            if(!HasClockSample || round != expectedRound || round <= ReleasedRound || round == Round
                || nextFrame <= currentFrame || !Finite(serverDeadline)) return false;
            Round=round;
            NextFrame=nextFrame;
            LocalDeadline=serverDeadline-ServerOffset;
            return true;
        }

        public bool IsDue(double now) => Round > ReleasedRound && now >= LocalDeadline;
        public void MarkReleased() { ReleasedRound=Round; LocalDeadline=double.PositiveInfinity; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
