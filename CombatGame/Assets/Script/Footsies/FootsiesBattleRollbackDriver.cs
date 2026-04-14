using UnityEngine;

namespace Footsies
{
    public class FootsiesBattleRollbackDriver : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NetworkSessionManager sessionManager;
        [SerializeField] private NetworkInputReceiver inputReceiver;
        [SerializeField] private AutoRollbackTrigger autoRollbackTrigger;
        [SerializeField] private FootsiesBattleRollbackCoordinator battleRollbackCoordinator;
        [SerializeField] private FootsiesBattleResimulationDriver battleResimulationDriver;

        private void FixedUpdate()
        {
            if (sessionManager == null || battleRollbackCoordinator == null)
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

            if (battleResimulationDriver != null)
            {
                battleResimulationDriver.ProcessResimulationIfNeeded();
            }

            battleRollbackCoordinator.SaveSnapshotForCurrentFrame();
        }
    }
}
