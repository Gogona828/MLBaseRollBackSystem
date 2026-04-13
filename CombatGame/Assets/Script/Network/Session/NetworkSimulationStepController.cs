using UnityEngine;

public class NetworkSimulationStepController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkFrameClock frameClock;
    [SerializeField] private NetworkSessionManager sessionManager;
    [SerializeField] private NetworkInputReceiver inputReceiver;
    [SerializeField] private NetworkInputSender inputSender;
    [SerializeField] private PredictedInputTester predictedInputTester;

    private void FixedUpdate()
    {
        if (frameClock == null || sessionManager == null)
        {
            return;
        }

        if (!sessionManager.Running)
        {
            return;
        }

        int currentFrame = frameClock.CurrentFrame;

        // 1. 遅延入力を解放
        if (inputReceiver != null)
        {
            inputReceiver.ProcessDelayedInputsForCurrentStep();
        }

        // 2. 現在フレームのローカル入力を送信
        if (inputSender != null)
        {
            inputSender.ProcessSendForFrame(currentFrame);
        }

        // 3. ひとつ前のフレームを読む
        if (predictedInputTester != null)
        {
            predictedInputTester.ProcessTestReadForFrame(currentFrame - 1);
        }

        // 4. 最後にフレームを進める
        frameClock.Tick();
    }
}
