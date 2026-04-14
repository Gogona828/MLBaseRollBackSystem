using UnityEngine;

public class RollbackCoordinator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkFrameClock frameClock;
    [SerializeField] private NetworkSessionManager sessionManager;
    [SerializeField] private RollbackStateTester player1StateTester;
    [SerializeField] private RollbackStateTester player2StateTester;
    [SerializeField] private RollbackObservationMonitor rollbackObservationMonitor;

    [Header("Role")]
    [SerializeField] private bool localPlayerIsP1 = true;

    [Header("Snapshot Settings")]
    [SerializeField] private int snapshotCapacity = 300;

    private SnapshotRingBuffer snapshotRingBuffer;
    private RollbackRequest pendingRequest = RollbackRequest.Invalid();

    public bool DidRollbackThisStep { get; private set; }
    public RollbackResimulationRequest PendingResimulationRequest { get; private set; } =
        RollbackResimulationRequest.Invalid();

    private void Awake()
    {
        snapshotRingBuffer = new SnapshotRingBuffer(snapshotCapacity);

        FileLogger.WriteLine(
            $"[RollbackCoordinator] Awake frameClock={(frameClock != null)} sessionManager={(sessionManager != null)} player1StateTester={(player1StateTester != null)} player2StateTester={(player2StateTester != null)} rollbackObservationMonitor={(rollbackObservationMonitor != null)} localPlayerIsP1={localPlayerIsP1} snapshotCapacity={snapshotCapacity}");
    }

    public void BeginStep()
    {
        DidRollbackThisStep = false;
        PendingResimulationRequest = RollbackResimulationRequest.Invalid();
    }

    public void SaveSnapshotForFrame(int frame)
    {
        if (player1StateTester == null || player2StateTester == null)
        {
            FileLogger.WriteLine("[RollbackCoordinator] SaveSnapshotForFrame skipped because player state testers are null.");
            return;
        }

        BattleStateSnapshot snapshot = new BattleStateSnapshot(
            frame,
            player1StateTester.SimulatedPosX,
            player2StateTester.SimulatedPosX,
            player1StateTester.Facing,
            player2StateTester.Facing,
            player1StateTester.StateId,
            player2StateTester.StateId
        );

        snapshotRingBuffer.Store(snapshot);

        FileLogger.WriteLine($"[RollbackCoordinator] Saved snapshot {snapshot}");

        if (rollbackObservationMonitor != null)
        {
            float predictedRemotePosX = rollbackObservationMonitor.GetObservedRemotePosX(localPlayerIsP1);
            rollbackObservationMonitor.RecordPredictedRemotePosition(predictedRemotePosX);
        }
    }

    public void RequestRollback(int targetFrame)
    {
        pendingRequest = new RollbackRequest(targetFrame);
        FileLogger.WriteLine($"[RollbackCoordinator] Rollback requested targetFrame={targetFrame}");
    }

    public void ProcessRollbackIfNeeded()
    {
        if (!pendingRequest.IsValid)
        {
            return;
        }

        if (!snapshotRingBuffer.TryGetSnapshot(pendingRequest.TargetFrame, out BattleStateSnapshot snapshot))
        {
            FileLogger.WriteLine($"[RollbackCoordinator] Snapshot not found for frame={pendingRequest.TargetFrame}");
            pendingRequest = RollbackRequest.Invalid();
            return;
        }

        int resumeFrameExclusive = frameClock != null ? frameClock.CurrentFrame : pendingRequest.TargetFrame + 1;

        player1StateTester.RestoreState(snapshot.P1PosX, snapshot.P1Facing, snapshot.P1StateId);
        player2StateTester.RestoreState(snapshot.P2PosX, snapshot.P2Facing, snapshot.P2StateId);

        DidRollbackThisStep = true;
        PendingResimulationRequest = new RollbackResimulationRequest(
            pendingRequest.TargetFrame,
            resumeFrameExclusive
        );

        if (rollbackObservationMonitor == null)
        {
            FileLogger.WriteLine(
                $"[RollbackCoordinator] rollbackObservationMonitor is null at rollback frame={pendingRequest.TargetFrame}");
        }
        else
        {
            FileLogger.WriteLine(
                $"[RollbackCoordinator] Calling observation monitor at rollback frame={pendingRequest.TargetFrame}");

            float confirmedRemotePosX = rollbackObservationMonitor.GetObservedRemotePosX(localPlayerIsP1);
            rollbackObservationMonitor.ObserveWarpOnRollback(pendingRequest.TargetFrame, confirmedRemotePosX);
        }

        FileLogger.WriteLine($"[RollbackCoordinator] Restored snapshot {snapshot}");
        FileLogger.WriteLine($"[RollbackCoordinator] Created resimulation request {PendingResimulationRequest}");

        pendingRequest = RollbackRequest.Invalid();
    }

    public bool TryConsumeResimulationRequest(out RollbackResimulationRequest request)
    {
        if (!PendingResimulationRequest.IsValid)
        {
            request = RollbackResimulationRequest.Invalid();
            return false;
        }

        request = PendingResimulationRequest;
        PendingResimulationRequest = RollbackResimulationRequest.Invalid();
        return true;
    }

    public bool TryGetSnapshot(int frame, out BattleStateSnapshot snapshot)
    {
        if (snapshotRingBuffer == null)
        {
            snapshot = default;
            return false;
        }

        return snapshotRingBuffer.TryGetSnapshot(frame, out snapshot);
    }

    public void ResetCoordinator()
    {
        snapshotRingBuffer?.Clear();
        pendingRequest = RollbackRequest.Invalid();
        PendingResimulationRequest = RollbackResimulationRequest.Invalid();
        DidRollbackThisStep = false;

        FileLogger.WriteLine("[RollbackCoordinator] ResetCoordinator");
    }
}
