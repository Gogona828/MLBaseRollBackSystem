using System;
using UnityEngine;

public class SimpleNetworkTestRunner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkSessionManager sessionManager;
    [SerializeField] private NetworkInputReceiver networkInputReceiver;
    [SerializeField] private PredictionMismatchDetector predictionMismatchDetector;
    [SerializeField] private AutoRollbackTrigger autoRollbackTrigger;
    [SerializeField] private RollbackResimulationRunner rollbackResimulationRunner;
    [SerializeField] private RollbackObservationMonitor rollbackObservationMonitor;
    [SerializeField] private FileLoggerBootstrap fileLoggerBootstrap;

    [Header("Test Settings")]
    [SerializeField] private float testDurationSeconds = 8f;
    [SerializeField] private bool autoStartWhenRunning = true;
    [SerializeField] private bool autoQuitPlayModeInEditor = false;
    [SerializeField] private string testLabel = "default";

    private bool testStarted = false;
    private bool testFinished = false;
    private float elapsedSeconds = 0f;

    private void Update()
    {
        if (testFinished)
        {
            return;
        }

        if (sessionManager == null)
        {
            return;
        }

        if (!testStarted)
        {
            if (autoStartWhenRunning && sessionManager.Running)
            {
                testStarted = true;
                elapsedSeconds = 0f;

                FileLogger.WriteLine(
                    $"[SimpleNetworkTestRunner] TEST STARTED | duration={testDurationSeconds:0.00}s | label={testLabel}");
            }

            return;
        }

        elapsedSeconds += Time.deltaTime;

        if (elapsedSeconds >= testDurationSeconds)
        {
            FinishTest();
        }
    }

    private void FinishTest()
    {
        if (testFinished)
        {
            return;
        }

        testFinished = true;

        string summary = SimpleNetworkTestSummary.BuildSummary(
            networkInputReceiver,
            predictionMismatchDetector,
            autoRollbackTrigger,
            rollbackResimulationRunner,
            rollbackObservationMonitor
        );

        FileLogger.WriteLine($"[SimpleNetworkTestRunner] TEST FINISHED | {summary}");

        ExperimentResultRecord record = BuildRecord();
        ExperimentCsvLogger.AppendRecord(record);

#if UNITY_EDITOR
        if (autoQuitPlayModeInEditor)
        {
            UnityEditor.EditorApplication.isPlaying = false;
        }
#endif
    }

    private ExperimentResultRecord BuildRecord()
    {
        ExperimentResultRecord record = new ExperimentResultRecord();

        record.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        record.MachineLabel = fileLoggerBootstrap != null ? fileLoggerBootstrap.MachineLabel : "Unknown";
        record.TestLabel = testLabel;
        record.DelayFrames = networkInputReceiver != null ? networkInputReceiver.FixedInputDelayFrames : -1;

        record.Predictions = predictionMismatchDetector != null ? predictionMismatchDetector.TotalPredictions : 0;
        record.Hits = predictionMismatchDetector != null ? predictionMismatchDetector.TotalHits : 0;
        record.Misses = predictionMismatchDetector != null ? predictionMismatchDetector.TotalMisses : 0;

        record.RollbackRequests = autoRollbackTrigger != null ? autoRollbackTrigger.TotalRollbackRequests : 0;
        record.SuppressedRollbacks = autoRollbackTrigger != null ? autoRollbackTrigger.SuppressedRollbackRequests : 0;

        record.Resimulations = rollbackResimulationRunner != null ? rollbackResimulationRunner.TotalResimulations : 0;
        record.TotalResimulatedFrames = rollbackResimulationRunner != null ? rollbackResimulationRunner.TotalResimulatedFrames : 0;
        record.LastResimulationFrameCount = rollbackResimulationRunner != null ? rollbackResimulationRunner.LastResimulationFrameCount : 0;

        record.WarpDetections = rollbackObservationMonitor != null ? rollbackObservationMonitor.TotalWarpDetections : 0;
        record.GhostHitCandidates = rollbackObservationMonitor != null ? rollbackObservationMonitor.TotalGhostHitCandidates : 0;

        return record;
    }

    public void ResetRunnerState()
    {
        testStarted = false;
        testFinished = false;
        elapsedSeconds = 0f;
    }
}
