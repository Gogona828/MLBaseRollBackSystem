using UnityEngine;

namespace Footsies
{
    public class FootsiesBattleRollbackDriver : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NetworkSessionManager sessionManager;
        [SerializeField] private NetworkFrameClock frameClock;
        [SerializeField] private NetworkInputReceiver inputReceiver;
        [SerializeField] private PredictionMismatchDetector predictionMismatchDetector;
        [SerializeField] private AutoRollbackTrigger autoRollbackTrigger;
        [SerializeField] private FootsiesBattleRollbackCoordinator battleRollbackCoordinator;

        private void FixedUpdate()
        {
            if (sessionManager == null || frameClock == null || battleRollbackCoordinator == null)
            {
                return;
            }

            if (!sessionManager.Running)
            {
                return;
            }

            battleRollbackCoordinator.BeginStep();

            if (inputReceiver != null)
            {
                inputReceiver.ProcessDelayedInputsForCurrentStep();
            }

            if (autoRollbackTrigger != null)
            {
                autoRollbackTrigger.ProcessAutoRollback();
            }

            battleRollbackCoordinator.ProcessRollbackIfNeeded();
            battleRollbackCoordinator.SaveSnapshotForCurrentFrame();
        }
    }
}
