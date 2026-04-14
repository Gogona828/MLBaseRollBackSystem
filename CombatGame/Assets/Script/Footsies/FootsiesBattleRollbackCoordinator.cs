using UnityEngine;

namespace Footsies
{
    public class FootsiesBattleRollbackCoordinator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BattleCore battleCore;
        [SerializeField] private FootsiesBattleStateBridge battleStateBridge;
        [SerializeField] private NetworkFrameClock frameClock;

        [Header("Settings")]
        [SerializeField] private int snapshotCapacity = 300;

        private FootsiesBattleSnapshotRingBuffer snapshotRingBuffer;
        private int pendingRollbackFrame = -1;

        public bool DidRollbackThisStep { get; private set; }

        private void Awake()
        {
            snapshotRingBuffer = new FootsiesBattleSnapshotRingBuffer(snapshotCapacity);
        }

        public void BeginStep()
        {
            DidRollbackThisStep = false;
        }

        public void SaveSnapshotForCurrentFrame()
        {
            if (battleStateBridge == null || frameClock == null)
            {
                return;
            }

            FootsiesBattleSnapshot snapshot = battleStateBridge.CaptureSnapshot();
            snapshotRingBuffer.Store(frameClock.CurrentFrame, snapshot);

            FileLogger.WriteLine(
                $"[FootsiesBattleRollbackCoordinator] Saved snapshot frame={frameClock.CurrentFrame}");
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

            if (battleStateBridge == null)
            {
                pendingRollbackFrame = -1;
                return;
            }

            if (!snapshotRingBuffer.TryGetSnapshot(pendingRollbackFrame, out FootsiesBattleSnapshot snapshot))
            {
                FileLogger.WriteLine(
                    $"[FootsiesBattleRollbackCoordinator] Snapshot not found for frame={pendingRollbackFrame}");
                pendingRollbackFrame = -1;
                return;
            }

            battleStateBridge.RestoreSnapshot(snapshot);
            DidRollbackThisStep = true;

            FileLogger.WriteLine(
                $"[FootsiesBattleRollbackCoordinator] Restored snapshot frame={pendingRollbackFrame}");

            pendingRollbackFrame = -1;
        }

        public void ClearAll()
        {
            snapshotRingBuffer?.Clear();
            pendingRollbackFrame = -1;
            DidRollbackThisStep = false;
        }
    }
}
