using System.Collections.Generic;

public class SnapshotRingBuffer
{
    private readonly int capacity;
    private readonly Dictionary<int, BattleStateSnapshot> snapshots = new Dictionary<int, BattleStateSnapshot>();
    private readonly Queue<int> frameOrder = new Queue<int>();

    public SnapshotRingBuffer(int capacity)
    {
        this.capacity = capacity;
    }

    public int Capacity => capacity;
    public int Count => snapshots.Count;

    public void Store(BattleStateSnapshot snapshot)
    {
        if (snapshots.ContainsKey(snapshot.Frame))
        {
            snapshots[snapshot.Frame] = snapshot;
            return;
        }

        snapshots[snapshot.Frame] = snapshot;
        frameOrder.Enqueue(snapshot.Frame);

        while (frameOrder.Count > capacity)
        {
            int oldestFrame = frameOrder.Dequeue();
            snapshots.Remove(oldestFrame);
        }
    }

    public bool TryGetSnapshot(int frame, out BattleStateSnapshot snapshot)
    {
        return snapshots.TryGetValue(frame, out snapshot);
    }

    public void Clear()
    {
        snapshots.Clear();
        frameOrder.Clear();
    }
}
