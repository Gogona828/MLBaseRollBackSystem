using System.Collections.Generic;

namespace Footsies
{
    public class FootsiesBattleSnapshotRingBuffer
    {
        private readonly int capacity;
        private readonly Dictionary<int, FootsiesBattleSnapshot> snapshots = new Dictionary<int, FootsiesBattleSnapshot>();
        private readonly Queue<int> order = new Queue<int>();

        public FootsiesBattleSnapshotRingBuffer(int capacity)
        {
            this.capacity = capacity;
        }

        public void Store(int frame, FootsiesBattleSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            if (snapshots.ContainsKey(frame))
            {
                snapshots[frame] = snapshot.Clone();
                return;
            }

            snapshots[frame] = snapshot.Clone();
            order.Enqueue(frame);

            while (order.Count > capacity)
            {
                int oldest = order.Dequeue();
                snapshots.Remove(oldest);
            }
        }

        public bool TryGetSnapshot(int frame, out FootsiesBattleSnapshot snapshot)
        {
            if (snapshots.TryGetValue(frame, out FootsiesBattleSnapshot found))
            {
                snapshot = found.Clone();
                return true;
            }

            snapshot = null;
            return false;
        }

        public void Clear()
        {
            snapshots.Clear();
            order.Clear();
        }
    }
}
