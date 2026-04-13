using UnityEngine;

public class PredictedInputTester : MonoBehaviour
{
    [Header("Role")]
    [SerializeField] private bool isLocalPlayerP1 = true;

    [Header("References")]
    [SerializeField] private NetworkFrameClock frameClock;
    [SerializeField] private NetworkInputReceiver networkInputReceiver;

    private IFrameInputSource localSource;
    private PredictedRemoteFrameInputSource predictedRemoteSource;
    private NetworkBattleInputRouter router;
    private NetworkBattleInputBridge bridge;
    private IRemoteInputPredictor predictor;

    private void Start()
    {
        if (frameClock == null)
        {
            FileLogger.WriteLine("[PredictedInputTester] frameClock is null");
            return;
        }

        if (networkInputReceiver == null)
        {
            FileLogger.WriteLine("[PredictedInputTester] networkInputReceiver is null");
            return;
        }

        localSource = new LocalFrameInputSource();
        predictor = new LastInputPredictor();
        predictedRemoteSource = new PredictedRemoteFrameInputSource(
            networkInputReceiver.Buffer,
            predictor
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

    private void Update()
    {
        if (bridge == null || frameClock == null)
        {
            return;
        }

        int frame = frameClock.CurrentFrame;

        if (frame % 30 != 0)
        {
            return;
        }

        FrameInputState p1 = bridge.GetP1InputState(frame);
        FrameInputState p2 = bridge.GetP2InputState(frame);

        FileLogger.WriteLine(
            $"[PredictedInputTester] frame={frame} | P1=({p1}) | P2=({p2}) | remoteUsedPrediction={predictedRemoteSource.LastReadUsedPrediction} | remoteBits={predictedRemoteSource.LastReadBits}");
    }
}
