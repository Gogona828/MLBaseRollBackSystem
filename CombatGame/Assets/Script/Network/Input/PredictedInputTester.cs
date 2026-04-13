using UnityEngine;

public class PredictedInputTester : MonoBehaviour
{
    [Header("Role")]
    [SerializeField] private bool isLocalPlayerP1 = true;

    [Header("References")]
    [SerializeField] private NetworkInputReceiver networkInputReceiver;
    [SerializeField] private NetworkSessionManager sessionManager;
    [SerializeField] private PredictionMismatchDetector predictionMismatchDetector;

    [Header("Logging")]
    [SerializeField] private int logIntervalFrames = 30;

    private IFrameInputSource localSource;
    private PredictedRemoteFrameInputSource predictedRemoteSource;
    private NetworkBattleInputRouter router;
    private NetworkBattleInputBridge bridge;
    private IRemoteInputPredictor predictor;

    private int lastLoggedFrame = -1;

    private void Start()
    {
        if (networkInputReceiver == null)
        {
            FileLogger.WriteLine("[PredictedInputTester] networkInputReceiver is null");
            return;
        }

        if (sessionManager == null)
        {
            FileLogger.WriteLine("[PredictedInputTester] sessionManager is null");
            return;
        }

        if (predictionMismatchDetector == null)
        {
            FileLogger.WriteLine("[PredictedInputTester] predictionMismatchDetector is null");
            return;
        }

        localSource = new LocalFrameInputSource();
        predictor = new LastInputPredictor();

        predictedRemoteSource = new PredictedRemoteFrameInputSource(
            networkInputReceiver.Buffer,
            predictor,
            predictionMismatchDetector
        );

        if (isLocalPlayerP1)
        {
            router = new NetworkBattleInputRouter(localSource, predictedRemoteSource);
            FileLogger.WriteLine("[PredictedInputTester] Local player is P1");
        }
        else
        {
            router = new NetworkBattleInputRouter(predictedRemoteSource, localSource);
            FileLogger.WriteLine("[PredictedInputTester] Local player is P2");
        }

        bridge = new NetworkBattleInputBridge(router);
    }

    public void ProcessTestReadForFrame(int frame)
    {
        if (bridge == null || sessionManager == null)
        {
            return;
        }

        if (!sessionManager.Running)
        {
            return;
        }

        if (frame < 0)
        {
            return;
        }

        if (frame == lastLoggedFrame)
        {
            return;
        }

        if (logIntervalFrames > 1 && frame % logIntervalFrames != 0)
        {
            return;
        }

        lastLoggedFrame = frame;

        FrameInputState p1 = bridge.GetP1InputState(frame);
        FrameInputState p2 = bridge.GetP2InputState(frame);

        string summary = predictionMismatchDetector != null
            ? predictionMismatchDetector.GetSummary()
            : "predictions=0, hits=0, misses=0";

        FileLogger.WriteLine(
            $"[PredictedInputTester] frame={frame} | P1=({p1}) | P2=({p2}) | remoteUsedPrediction={predictedRemoteSource.LastReadUsedPrediction} | remoteBits={predictedRemoteSource.LastReadBits} | {summary}");
    }

    public void ResetTesterState()
    {
        lastLoggedFrame = -1;
        predictedRemoteSource?.ResetPredictor();
        predictionMismatchDetector?.ResetDetector();
    }
}
