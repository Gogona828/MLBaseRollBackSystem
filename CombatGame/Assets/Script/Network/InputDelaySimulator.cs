using System.Collections.Generic;

public class InputDelaySimulator
{
    private class DelayedPacket
    {
        public InputPacket packet;
        public int releaseStep;
    }

    private readonly List<DelayedPacket> pendingPackets = new List<DelayedPacket>();
    private int currentStep = 0;

    public void Enqueue(InputPacket packet, int delayFrames)
    {
        pendingPackets.Add(new DelayedPacket
        {
            packet = packet,
            releaseStep = currentStep + delayFrames
        });
    }

    public List<InputPacket> TickAndCollectReleasedPackets()
    {
        currentStep++;

        List<InputPacket> released = new List<InputPacket>();

        for (int i = pendingPackets.Count - 1; i >= 0; i--)
        {
            if (pendingPackets[i].releaseStep <= currentStep)
            {
                released.Add(pendingPackets[i].packet);
                pendingPackets.RemoveAt(i);
            }
        }

        released.Sort((a, b) => a.frame.CompareTo(b.frame));
        return released;
    }

    public int GetPendingCount()
    {
        return pendingPackets.Count;
    }

    public void Clear()
    {
        pendingPackets.Clear();
        currentStep = 0;
    }
}
