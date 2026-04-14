using UnityEngine;

public class FootsiesBattleNetworkDriver : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkFrameClock frameClock;
    [SerializeField] private NetworkSessionManager sessionManager;
    [SerializeField] private NetworkInputSender inputSender;
    [SerializeField] private NetworkInputReceiver inputReceiver;

    private void FixedUpdate()
    {
        if (frameClock == null || sessionManager == null || inputSender == null)
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

        inputSender.ProcessSendForFrame(currentFrame);
        frameClock.Tick();
    }
}
