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
    [SerializeField] private RollbackResimulationRunner rollbackResimulationRunner;

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

        if (rollbackCoordinator != null)
        {
            rollbackCoordinator.BeginStep();
        }

        // 1. 遅延入力を解放
        if (inputReceiver != null)
        {
            inputReceiver.ProcessDelayedInputsForCurrentStep();
        }

        // 2. miss があれば auto rollback request
        if (autoRollbackTrigger != null)
        {
            autoRollbackTrigger.ProcessAutoRollback();
        }

        // 3. rollback 実行
        if (rollbackCoordinator != null)
        {
            rollbackCoordinator.ProcessRollbackIfNeeded();
        }

        // 4. rollback 後の再シミュレーション
        if (rollbackResimulationRunner != null)
        {
            rollbackResimulationRunner.ProcessResimulationIfNeeded();
        }

        // 5. 現在フレームのローカル入力送信
        if (inputSender != null)
        {
            inputSender.ProcessSendForFrame(currentFrame);
        }

        // 6. rollback がなかったときだけ通常 simulation
        if (rollbackDebugTester != null)
        {
            if (rollbackCoordinator == null || !rollbackCoordinator.DidRollbackThisStep)
            {
                rollbackDebugTester.ProcessSimulationForFrame(currentFrame);
            }
            else
            {
                FileLogger.WriteLine(
                    $"[NetworkSimulationStepController] Skipped simulation for frame={currentFrame} because rollback occurred this step.");
            }

            rollbackDebugTester.ProcessDebugRollbackRequest();
        }

        // 7. tester
        if (predictedInputTester != null)
        {
            predictedInputTester.ProcessTestReadForFrame(currentFrame - 1);
        }

        // 8. frame を進める
        frameClock.Tick();
    }
}
