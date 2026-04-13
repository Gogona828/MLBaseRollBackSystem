using UnityEngine;

public class NetworkSimulationStepController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkFrameClock frameClock;
    [SerializeField] private NetworkSessionManager sessionManager;
    [SerializeField] private NetworkInputReceiver inputReceiver;
    [SerializeField] private NetworkInputSender inputSender;
    [SerializeField] private PredictedInputTester predictedInputTester;
    [SerializeField] private RollbackDebugTester rollbackDebugTester;
    [SerializeField] private RollbackCoordinator rollbackCoordinator;
    [SerializeField] private AutoRollbackTrigger autoRollbackTrigger;

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

        // 2. miss があれば自動 rollback request を出す
        if (autoRollbackTrigger != null)
        {
            autoRollbackTrigger.ProcessAutoRollback();
        }

        // 3. rollback request があれば復元
        if (rollbackCoordinator != null)
        {
            rollbackCoordinator.ProcessRollbackIfNeeded();
        }

        // 4. 現在フレームのローカル入力を送信
        if (inputSender != null)
        {
            inputSender.ProcessSendForFrame(currentFrame);
        }

        // 5. rollback 用の簡易状態を更新して snapshot 保存
        if (rollbackDebugTester != null)
        {
            rollbackDebugTester.ProcessSimulationForFrame(currentFrame);
            rollbackDebugTester.ProcessDebugRollbackRequest();
        }

        // 6. ひとつ前の frame を tester が読む
        if (predictedInputTester != null)
        {
            predictedInputTester.ProcessTestReadForFrame(currentFrame - 1);
        }

        // 7. 最後に frame を進める
        frameClock.Tick();
    }
}
