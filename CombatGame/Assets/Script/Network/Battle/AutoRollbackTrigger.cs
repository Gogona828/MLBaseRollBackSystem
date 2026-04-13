using UnityEngine;

public class AutoRollbackTrigger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PredictionMismatchDetector predictionMismatchDetector;
    [SerializeField] private RollbackCoordinator rollbackCoordinator;
    [SerializeField] private NetworkFrameClock frameClock;

    [Header("Settings")]
    [SerializeField] private bool enableAutoRollback = true;
    [SerializeField] private int rollbackSafetyOffsetFrames = 0;
    [SerializeField] private int rollbackCooldownFrames = 6;

    private int lastRequestedFrame = -1;
    private int lastRollbackExecutedAtFrame = -999999;

    public int TotalRollbackRequests { get; private set; }
    public int SuppressedRollbackRequests { get; private set; }

    public void ProcessAutoRollback()
    {
        if (!enableAutoRollback)
        {
            return;
        }

        if (predictionMismatchDetector == null || rollbackCoordinator == null || frameClock == null)
        {
            return;
        }

        if (!predictionMismatchDetector.TryConsumeEarliestPendingMiss(out PredictionMissInfo missInfo))
        {
            return;
        }

        if (!missInfo.IsValid)
        {
            return;
        }

        int currentFrame = frameClock.CurrentFrame;
        int framesSinceLastRollback = currentFrame - lastRollbackExecutedAtFrame;

        if (framesSinceLastRollback < rollbackCooldownFrames)
        {
            SuppressedRollbackRequests++;

            FileLogger.WriteLine(
                $"[AutoRollbackTrigger] Suppressed rollback for miss {missInfo} because cooldown is active. currentFrame={currentFrame}, framesSinceLastRollback={framesSinceLastRollback}, cooldown={rollbackCooldownFrames}");

            return;
        }

        int targetFrame = Mathf.Max(0, missInfo.Frame - rollbackSafetyOffsetFrames);

        if (targetFrame == lastRequestedFrame)
        {
            SuppressedRollbackRequests++;

            FileLogger.WriteLine(
                $"[AutoRollbackTrigger] Suppressed rollback because targetFrame={targetFrame} was already requested.");

            return;
        }

        rollbackCoordinator.RequestRollback(targetFrame);

        lastRequestedFrame = targetFrame;
        lastRollbackExecutedAtFrame = currentFrame;
        TotalRollbackRequests++;

        FileLogger.WriteLine(
            $"[AutoRollbackTrigger] Auto rollback requested from miss {missInfo} => targetFrame={targetFrame}");
    }

    public void ResetTrigger()
    {
        lastRequestedFrame = -1;
        lastRollbackExecutedAtFrame = -999999;
        TotalRollbackRequests = 0;
        SuppressedRollbackRequests = 0;
    }
}
