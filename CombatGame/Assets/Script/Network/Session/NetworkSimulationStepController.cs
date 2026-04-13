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

        if (inputReceiver != null)
        {
            inputReceiver.ProcessDelayedInputsForCurrentStep();
        }

        if (inputSender != null)
        {
            inputSender.ProcessSendForFrame(currentFrame);
        }

        if (rollbackDebugTester != null)
        {
            rollbackDebugTester.ProcessSimulationForFrame(currentFrame);
            rollbackDebugTester.ProcessDebugRollbackRequest();
        }

        if (predictedInputTester != null)
        {
            predictedInputTester.ProcessTestReadForFrame(currentFrame - 1);
        }

        if (rollbackCoordinator != null)
        {
            rollbackCoordinator.ProcessRollbackIfNeeded();
        }

        frameClock.Tick();
    }
}
