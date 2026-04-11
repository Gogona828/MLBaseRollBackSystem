using System.Collections.Generic;
using UnityEngine;

public class InputDelaySimulator
{
    private readonly List<DelayedInputEntry> delayedEntries = new List<DelayedInputEntry>();
    private readonly int fixedDelayFrames;

    public InputDelaySimulator(int fixedDelayFrames)
    {
        this.fixedDelayFrames = Mathf.Max(0, fixedDelayFrames);
    }

    public int FixedDelayFrames => fixedDelayFrames;

    public void Enqueue(InputPacket packet)
    {
        DelayedInputEntry entry = new DelayedInputEntry(packet, fixedDelayFrames);
        delayedEntries.Add(entry);
    }

    public List<InputPacket> TickAndCollectReleasedPackets()
    {
        List<InputPacket> releasedPackets = new List<InputPacket>();

        for (int i = delayedEntries.Count - 1; i >= 0; i--)
        {
            DelayedInputEntry entry = delayedEntries[i];
            entry.RemainingDelayFrames--;

            if (entry.RemainingDelayFrames <= 0)
            {
                releasedPackets.Add(entry.Packet);
                delayedEntries.RemoveAt(i);
            }
            else
            {
                delayedEntries[i] = entry;
            }
        }

        releasedPackets.Reverse();
        return releasedPackets;
    }

    public void Clear()
    {
        delayedEntries.Clear();
    }

    public int GetPendingCount()
    {
        return delayedEntries.Count;
    }
}
