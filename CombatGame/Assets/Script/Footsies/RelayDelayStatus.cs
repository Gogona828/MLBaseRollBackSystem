using System;

namespace Footsies
{
    // Server monotonic timestamps; delayMs is configuration, eventDelayMs includes jitter.
    [Serializable]
    public class RelayDelayStatus
    {
        public int delayProtocolVersion;
        public int delayRevision;
        public bool delayActive;
        public double serverTime;
        public double delayStartedAt;
        public double delayUntil;
        public float delayMs;
        public float eventDelayMs;
        public float delayJitterMs;
        public float lossPercent;
        public double intervalMinSeconds;
        public double intervalMaxSeconds;
        public bool continuous;

        public double LocalExpiry(double now, LanRoundStartSchedule clock)
        {
            if (!delayActive) return now;
            if (continuous) return now + 2; // Refreshed by 0.5s clock replies.
            return clock.HasClockSample ? delayUntil - clock.ServerOffset
                : now + Math.Max(0, delayUntil - serverTime);
        }

        public float DisplayMilliseconds => delayProtocolVersion >= 2 && !continuous && delayActive
            ? eventDelayMs : delayMs;
    }
}
