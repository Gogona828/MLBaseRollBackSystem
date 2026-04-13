using UnityEngine;

public class SimpleNetworkTestRunner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkSessionManager sessionManager;
    [SerializeField] private NetworkInputReceiver networkInputReceiver;
    [SerializeField] private PredictionMismatchDetector predictionMismatchDetector;

    [Header("Test Settings")]
    [SerializeField] private float testDurationSeconds = 5f;
    [SerializeField] private bool autoStartWhenRunning = true;
    [SerializeField] private bool autoQuitPlayModeInEditor = false;

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
                    $"[SimpleNetworkTestRunner] TEST STARTED | duration={testDurationSeconds:0.00}s");
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
            predictionMismatchDetector
        );

        FileLogger.WriteLine($"[SimpleNetworkTestRunner] TEST FINISHED | {summary}");

#if UNITY_EDITOR
        if (autoQuitPlayModeInEditor)
        {
            UnityEditor.EditorApplication.isPlaying = false;
        }
#endif
    }

    public void ResetRunnerState()
    {
        testStarted = false;
        testFinished = false;
        elapsedSeconds = 0f;
    }
}
