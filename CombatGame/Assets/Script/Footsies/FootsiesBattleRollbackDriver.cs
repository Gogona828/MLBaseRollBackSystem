using UnityEngine;

namespace Footsies
{
    public class FootsiesBattleRollbackDriver : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NetworkSessionManager sessionManager;
        [SerializeField] private NetworkFrameClock frameClock;
        [SerializeField] private NetworkInputSender inputSender;
        [SerializeField] private NetworkInputReceiver inputReceiver;
        [SerializeField] private AutoRollbackTrigger autoRollbackTrigger;
        [SerializeField] private FootsiesBattleRollbackCoordinator battleRollbackCoordinator;
        [SerializeField] private FootsiesBattleResimulationDriver battleResimulationDriver;

        private void FixedUpdate()
        {
            if (sessionManager == null || frameClock == null || battleRollbackCoordinator == null || inputSender == null)
            {
                return;
            }

            if (!sessionManager.Running)
            {
                return;
            }

            battleRollbackCoordinator.BeginStep();

            int currentFrame = frameClock.CurrentFrame;

            battleRollbackCoordinator.SaveSnapshotForCurrentFrame();

            if (inputReceiver != null)
            {
                inputReceiver.ProcessDelayedInputsForCurrentStep();
            }

            if (autoRollbackTrigger != null)
            {
                autoRollbackTrigger.ProcessAutoRollback();
            }

            battleRollbackCoordinator.ProcessRollbackIfNeeded();

            if (battleResimulationDriver != null)
            {
                battleResimulationDriver.ProcessResimulationIfNeeded();
            }

            inputSender.ProcessSendForFrame(currentFrame);
            frameClock.Tick();
        }
    }
}
