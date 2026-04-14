using UnityEngine;

public class RollbackObservationMonitor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RollbackStateTester player1StateTester;
    [SerializeField] private RollbackStateTester player2StateTester;

    [Header("Warp Settings")]
    [SerializeField] private float warpDistanceThreshold = 0.25f;

    [Header("Ghost Hit Settings")]
    [SerializeField] private int lateRollbackAllowanceFrames = 1;

    public int TotalWarpDetections { get; private set; }
    public int TotalGhostHitCandidates { get; private set; }

    private float lastPredictedObservedRemotePosX = 0f;
    private int lastMissFrame = -1;

    private void Awake()
    {
        FileLogger.WriteLine(
            $"[RollbackObservationMonitor] Awake player1StateTester={(player1StateTester != null)} player2StateTester={(player2StateTester != null)} warpDistanceThreshold={warpDistanceThreshold} lateRollbackAllowanceFrames={lateRollbackAllowanceFrames}");
    }

    public void RecordPredictedRemotePosition(float predictedRemotePosX)
    {
        lastPredictedObservedRemotePosX = predictedRemotePosX;

        FileLogger.WriteLine(
            $"[RollbackObservationMonitor] RecordPredictedRemotePosition predictedRemotePosX={predictedRemotePosX}");
    }

    public void RecordMissFrame(int frame)
    {
        lastMissFrame = frame;

        FileLogger.WriteLine(
            $"[RollbackObservationMonitor] RecordMissFrame frame={frame}");
    }

    public void ObserveWarpOnRollback(int rollbackFrame, float confirmedRemotePosX)
    {
        float distanceError = Mathf.Abs(confirmedRemotePosX - lastPredictedObservedRemotePosX);
        bool detected = distanceError >= warpDistanceThreshold;

        WarpObservationRecord record = new WarpObservationRecord(
            rollbackFrame,
            lastPredictedObservedRemotePosX,
            confirmedRemotePosX,
            distanceError,
            detected
        );

        if (detected)
        {
            TotalWarpDetections++;
            FileLogger.WriteLine($"[RollbackObservationMonitor] WarpDetected {record}");
        }
        else
        {
            FileLogger.WriteLine($"[RollbackObservationMonitor] WarpCheck {record}");
        }
    }

    public void ObserveGhostHitCandidate(int frame, byte predictedBits, byte confirmedBits, int rollbackFrame)
    {
        bool predictedAttack = (predictedBits & 4) != 0;
        bool confirmedAttack = (confirmedBits & 4) != 0;

        int rollbackDelayFrames = rollbackFrame - frame;
        bool rollbackWasLate = rollbackDelayFrames > lateRollbackAllowanceFrames;

        bool detected = predictedAttack && !confirmedAttack && rollbackWasLate;

        GhostHitObservationRecord record = new GhostHitObservationRecord(
            frame,
            predictedBits,
            confirmedBits,
            predictedAttack,
            confirmedAttack,
            rollbackWasLate,
            detected
        );

        if (detected)
        {
            TotalGhostHitCandidates++;
            FileLogger.WriteLine($"[RollbackObservationMonitor] GhostHitCandidate {record}");
        }
        else
        {
            FileLogger.WriteLine($"[RollbackObservationMonitor] GhostHitCheck {record}");
        }
    }

    public float GetObservedRemotePosX(bool localPlayerIsP1)
    {
        if (player1StateTester == null || player2StateTester == null)
        {
            FileLogger.WriteLine(
                $"[RollbackObservationMonitor] GetObservedRemotePosX failed because references are missing. player1StateTester={(player1StateTester != null)} player2StateTester={(player2StateTester != null)}");

            return 0f;
        }

        float observedRemotePosX = localPlayerIsP1
            ? player2StateTester.SimulatedPosX
            : player1StateTester.SimulatedPosX;

        FileLogger.WriteLine(
            $"[RollbackObservationMonitor] GetObservedRemotePosX localPlayerIsP1={localPlayerIsP1} observedRemotePosX={observedRemotePosX}");

        return observedRemotePosX;
    }

    public void ResetMonitor()
    {
        TotalWarpDetections = 0;
        TotalGhostHitCandidates = 0;
        lastPredictedObservedRemotePosX = 0f;
        lastMissFrame = -1;

        FileLogger.WriteLine("[RollbackObservationMonitor] ResetMonitor");
    }
}
