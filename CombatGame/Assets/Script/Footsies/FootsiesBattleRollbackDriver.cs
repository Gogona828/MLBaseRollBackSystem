using UnityEngine;

namespace Footsies
{
    public class FootsiesBattleRollbackDriver : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NetworkSessionManager sessionManager;
        [SerializeField] private FootsiesBattleRollbackCoordinator battleRollbackCoordinator;
        [SerializeField] private NetworkInputReceiver inputReceiver;
        [SerializeField] private AutoRollbackTrigger autoRollbackTrigger;
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

            // 先に現在 frame の状態を保存する
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
        }
    }
}
