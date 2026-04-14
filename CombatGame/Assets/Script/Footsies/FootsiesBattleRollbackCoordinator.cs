using UnityEngine;

namespace Footsies
{
    public class FootsiesBattleRollbackCoordinator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private FootsiesBattleStateBridge battleStateBridge;
        [SerializeField] private NetworkFrameClock frameClock;

        [Header("Settings")]
        [SerializeField] private int snapshotCapacity = 300;

        private FootsiesBattleSnapshotRingBuffer snapshotRingBuffer;
        private int pendingRollbackFrame = -1;

        public bool DidRollbackThisStep { get; private set; }
        public int LastRollbackFrame { get; private set; } = -1;
        public int LastRollbackRestoreFromFrame { get; private set; } = -1;
        public int LastRollbackRestoreToFrame { get; private set; } = -1;

        private void Awake()
        {
            snapshotRingBuffer = new FootsiesBattleSnapshotRingBuffer(snapshotCapacity);
        }

        public void BeginStep()
        {
            DidRollbackThisStep = false;
            LastRollbackFrame = -1;
            LastRollbackRestoreFromFrame = -1;
            LastRollbackRestoreToFrame = -1;
        }

        public void SaveSnapshotForCurrentFrame()
        {
            if (battleStateBridge == null || frameClock == null)
            {
                return;
            }

            FootsiesBattleSnapshot snapshot = battleStateBridge.CaptureSnapshot();
            if (snapshot == null)
            {
                FileLogger.WriteLine(
                    $"[FootsiesBattleRollbackCoordinator] SaveSnapshot skipped because captured snapshot is null. frame={frameClock.CurrentFrame}");
                return;
            }

            snapshotRingBuffer.Store(frameClock.CurrentFrame, snapshot);

            FileLogger.WriteLine(
                $"[FootsiesBattleRollbackCoordinator] Saved snapshot frame={frameClock.CurrentFrame}, " +
                $"{FootsiesBattleSnapshotDebugFormatter.BuildSummary(snapshot)}");
        }

        public void RequestRollback(int targetFrame)
        {
            pendingRollbackFrame = targetFrame;

            FileLogger.WriteLine(
                $"[FootsiesBattleRollbackCoordinator] Rollback requested targetFrame={targetFrame}");
        }

        public void ProcessRollbackIfNeeded()
        {
            if (pendingRollbackFrame < 0)
            {
                return;
            }

            if (battleStateBridge == null || frameClock == null)
            {
                pendingRollbackFrame = -1;
                return;
            }

            int currentFrame = frameClock.CurrentFrame;

            if (!snapshotRingBuffer.TryGetSnapshot(pendingRollbackFrame, out FootsiesBattleSnapshot snapshot))
            {
                FileLogger.WriteLine(
                    $"[FootsiesBattleRollbackCoordinator] Snapshot not found for frame={pendingRollbackFrame}");
                pendingRollbackFrame = -1;
                return;
            }

            battleStateBridge.RestoreSnapshot(snapshot);

            DidRollbackThisStep = true;
            LastRollbackFrame = pendingRollbackFrame;
            LastRollbackRestoreFromFrame = pendingRollbackFrame;
            LastRollbackRestoreToFrame = currentFrame;

            FileLogger.WriteLine(
                $"[FootsiesBattleRollbackCoordinator] Restored snapshot frame={pendingRollbackFrame}, " +
                $"{FootsiesBattleSnapshotDebugFormatter.BuildSummary(snapshot)}");

            pendingRollbackFrame = -1;
        }

        public void ClearAll()
        {
            snapshotRingBuffer?.Clear();
            pendingRollbackFrame = -1;
            DidRollbackThisStep = false;
            LastRollbackFrame = -1;
            LastRollbackRestoreFromFrame = -1;
            LastRollbackRestoreToFrame = -1;
        }
    }
}
