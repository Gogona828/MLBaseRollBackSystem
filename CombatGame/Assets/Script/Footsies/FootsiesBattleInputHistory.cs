using System.Collections.Generic;
using UnityEngine;

namespace Footsies
{
    public class FootsiesBattleInputHistory : MonoBehaviour
    {
        private readonly Dictionary<int, byte> player0Inputs = new Dictionary<int, byte>();
        private readonly Dictionary<int, byte> player1Inputs = new Dictionary<int, byte>();

        public void StoreInput(int playerId, int frame, byte bits)
        {
            if (playerId == 0)
            {
                player0Inputs[frame] = bits;
            }
            else if (playerId == 1)
            {
                player1Inputs[frame] = bits;
            }
        }

        public bool TryGetInput(int playerId, int frame, out byte bits)
        {
            if (playerId == 0)
            {
                return player0Inputs.TryGetValue(frame, out bits);
            }

            if (playerId == 1)
            {
                return player1Inputs.TryGetValue(frame, out bits);
            }

            bits = 0;
            return false;
        }

        public void ClearAll()
        {
            player0Inputs.Clear();
            player1Inputs.Clear();
        }
    }
}
