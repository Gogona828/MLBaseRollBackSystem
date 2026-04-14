using UnityEngine;

namespace Footsies
{
    public class FootsiesBattleStateBridge : MonoBehaviour
    {
        [SerializeField] private BattleCore battleCore;

        public FootsiesBattleSnapshot CaptureSnapshot()
        {
            if (battleCore == null)
            {
                return null;
            }

            return battleCore.CaptureSnapshot();
        }

        public void RestoreSnapshot(FootsiesBattleSnapshot snapshot)
        {
            if (battleCore == null || snapshot == null)
            {
                return;
            }

            battleCore.RestoreSnapshot(snapshot);
        }
    }
}
