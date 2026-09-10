using System;

namespace Footsies
{
    public sealed class SimulatedDelayState
    {
        public int Revision { get; private set; } = -1;
        public double ExpiresAt { get; private set; }
        public float Milliseconds { get; private set; }
        public void Update(int revision, bool active, double expiresAt, float milliseconds)
        {
            if(revision < Revision || double.IsNaN(expiresAt) || double.IsInfinity(expiresAt)) return;
            Revision=revision;
            ExpiresAt=active ? expiresAt : double.NegativeInfinity;
            Milliseconds=milliseconds;
        }
        public bool IsActive(double now) => now < ExpiresAt;
    }
}
