public static class SimpleNetworkTestSummary
{
    public static string BuildSummary(
        NetworkInputReceiver networkInputReceiver,
        PredictionMismatchDetector predictionMismatchDetector)
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

        return $"delay={delay} | {detectorSummary} | pendingDelayedInputs={pendingDelayedInputs}";
    }
}
