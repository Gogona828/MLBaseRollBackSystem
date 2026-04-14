using UnityEngine;

namespace Footsies
{
    public class FootsiesBattleRollbackDebugTester : MonoBehaviour
    {
        [SerializeField] private FootsiesBattleStateBridge battleStateBridge;

        private FootsiesBattleSnapshot savedSnapshot;

        private void Update()
        {
            if (battleStateBridge == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.S))
            {
                savedSnapshot = battleStateBridge.CaptureSnapshot();
                Debug.Log("[FootsiesBattleRollbackDebugTester] Snapshot saved.");
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                if (savedSnapshot != null)
                {
                    battleStateBridge.RestoreSnapshot(savedSnapshot);
                    Debug.Log("[FootsiesBattleRollbackDebugTester] Snapshot restored.");
                }
            }
        }
    }
}
