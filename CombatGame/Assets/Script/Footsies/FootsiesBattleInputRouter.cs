using UnityEngine;

namespace Footsies
{
    public class FootsiesBattleInputRouter : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour player1InputSourceBehaviour;
        [SerializeField] private MonoBehaviour player2InputSourceBehaviour;

        private IFootsiesPlayerInputSource player1InputSource;
        private IFootsiesPlayerInputSource player2InputSource;

        private void Awake()
        {
            player1InputSource = player1InputSourceBehaviour as IFootsiesPlayerInputSource;
            player2InputSource = player2InputSourceBehaviour as IFootsiesPlayerInputSource;
        }

        public FootsiesInputFrame GetPlayer1Input()
        {
            if (player1InputSource == null)
            {
                return FootsiesInputFrame.Empty();
            }

            return player1InputSource.GetCurrentInput();
        }

        public FootsiesInputFrame GetPlayer2Input()
        {
            if (player2InputSource == null)
            {
                return FootsiesInputFrame.Empty();
            }

            return player2InputSource.GetCurrentInput();
        }
    }
}
