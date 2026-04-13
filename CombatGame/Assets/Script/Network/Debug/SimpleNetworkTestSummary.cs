public static class SimpleNetworkTestSummary
{
    public static string BuildSummary(
        NetworkInputReceiver networkInputReceiver,
        PredictionMismatchDetector predictionMismatchDetector,
        AutoRollbackTrigger autoRollbackTrigger,
        RollbackResimulationRunner rollbackResimulationRunner)
    {
        int delay = -1;
        int pendingDelayedInputs = -1;

        if (networkInputReceiver != null)
        {
            delay = networkInputReceiver.FixedInputDelayFrames;
            pendingDelayedInputs = networkInputReceiver.GetPendingDelayedInputCount();
        }

        string detectorSummary = "predictions=0, hits=0, misses=0";
        if (predictionMismatchDetector != null)
        {
            detectorSummary = predictionMismatchDetector.GetSummary();
        }

        int rollbackRequests = 0;
        int suppressedRollbacks = 0;

        if (autoRollbackTrigger != null)
        {
            rollbackRequests = autoRollbackTrigger.TotalRollbackRequests;
            suppressedRollbacks = autoRollbackTrigger.SuppressedRollbackRequests;
        }

        int totalResimulations = 0;
        int totalResimulatedFrames = 0;
        int lastResimulationFrameCount = 0;

        if (rollbackResimulationRunner != null)
        {
            totalResimulations = rollbackResimulationRunner.TotalResimulations;
            totalResimulatedFrames = rollbackResimulationRunner.TotalResimulatedFrames;
            lastResimulationFrameCount = rollbackResimulationRunner.LastResimulationFrameCount;
        }

        return
            $"delay={delay} | {detectorSummary} | " +
            $"rollbackRequests={rollbackRequests} | suppressedRollbacks={suppressedRollbacks} | " +
            $"resimulations={totalResimulations} | totalResimulatedFrames={totalResimulatedFrames} | " +
            $"lastResimulationFrameCount={lastResimulationFrameCount} | " +
            $"pendingDelayedInputs={pendingDelayedInputs}";
    }
}
