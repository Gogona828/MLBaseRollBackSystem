using UnityEngine;
using System.Diagnostics;

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
        private byte pendingPredictedBits;
        private byte pendingConfirmedBits;

        public bool DidRollbackThisStep { get; private set; }
        public int LastRollbackFrame { get; private set; } = -1;
        public int LastRollbackRestoreFromFrame { get; private set; } = -1;
        public int LastRollbackRestoreToFrame { get; private set; } = -1;
        public byte LastPredictedBits { get; private set; }
        public byte LastConfirmedBits { get; private set; }
        public double LastRestoreTimeMs { get; private set; }
        public float LastP1PositionBeforeRollback { get; private set; }
        public float LastP2PositionBeforeRollback { get; private set; }

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
            LastPredictedBits = 0;
            LastConfirmedBits = 0;
            LastRestoreTimeMs = 0d;
            LastP1PositionBeforeRollback = 0f;
            LastP2PositionBeforeRollback = 0f;
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
            RequestRollback(targetFrame, 0, 0);
        }

        public void RequestRollback(int targetFrame, byte predictedBits, byte confirmedBits)
        {
            pendingRollbackFrame = targetFrame;
            pendingPredictedBits = predictedBits;
            pendingConfirmedBits = confirmedBits;

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

            FootsiesBattleSnapshot currentSnapshot = battleStateBridge.CaptureSnapshot();
            LastP1PositionBeforeRollback = currentSnapshot != null && currentSnapshot.fighter1 != null
                ? currentSnapshot.fighter1.position.x
                : 0f;
            LastP2PositionBeforeRollback = currentSnapshot != null && currentSnapshot.fighter2 != null
                ? currentSnapshot.fighter2.position.x
                : 0f;

            Stopwatch stopwatch = Stopwatch.StartNew();
            battleStateBridge.RestoreSnapshot(snapshot);
            stopwatch.Stop();

            DidRollbackThisStep = true;
            LastRollbackFrame = pendingRollbackFrame;
            LastRollbackRestoreFromFrame = pendingRollbackFrame;
            LastRollbackRestoreToFrame = currentFrame;
            LastPredictedBits = pendingPredictedBits;
            LastConfirmedBits = pendingConfirmedBits;
            LastRestoreTimeMs = stopwatch.Elapsed.TotalMilliseconds;

            FileLogger.WriteLine(
                $"[FootsiesBattleRollbackCoordinator] Restored snapshot frame={pendingRollbackFrame}, " +
                $"{FootsiesBattleSnapshotDebugFormatter.BuildSummary(snapshot)}");

            pendingRollbackFrame = -1;
            pendingPredictedBits = 0;
            pendingConfirmedBits = 0;
        }

        public void ClearAll()
        {
            snapshotRingBuffer?.Clear();
            pendingRollbackFrame = -1;
            DidRollbackThisStep = false;
            LastRollbackFrame = -1;
            LastRollbackRestoreFromFrame = -1;
            LastRollbackRestoreToFrame = -1;
            LastPredictedBits = 0;
            LastConfirmedBits = 0;
            LastRestoreTimeMs = 0d;
            LastP1PositionBeforeRollback = 0f;
            LastP2PositionBeforeRollback = 0f;
        }
    }
}
