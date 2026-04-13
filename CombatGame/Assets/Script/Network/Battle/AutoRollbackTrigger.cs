using UnityEngine;

public class AutoRollbackTrigger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PredictionMismatchDetector predictionMismatchDetector;
    [SerializeField] private RollbackCoordinator rollbackCoordinator;

    [Header("Settings")]
    [SerializeField] private bool enableAutoRollback = true;
    [SerializeField] private int rollbackSafetyOffsetFrames = 0;

    private int lastRequestedFrame = -1;

    public void ProcessAutoRollback()
    {
        if (!enableAutoRollback)
        {
            return;
        }

        if (predictionMismatchDetector == null || rollbackCoordinator == null)
        {
            return;
        }

        if (!predictionMismatchDetector.TryConsumeLatestMiss(out PredictionMissInfo missInfo))
        {
            return;
        }

        if (!missInfo.IsValid)
        {
            return;
        }

        int targetFrame = Mathf.Max(0, missInfo.Frame - rollbackSafetyOffsetFrames);

        if (targetFrame == lastRequestedFrame)
        {
            return;
        }

        rollbackCoordinator.RequestRollback(targetFrame);
        lastRequestedFrame = targetFrame;

        FileLogger.WriteLine(
            $"[AutoRollbackTrigger] Auto rollback requested from miss {missInfo} => targetFrame={targetFrame}");
    }

    public void ResetTrigger()
    {
        lastRequestedFrame = -1;
    }
}
