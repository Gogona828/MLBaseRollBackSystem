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

        public bool TryGetLatestInputAtOrBefore(int playerId, int frame, out byte bits)
        {
            Dictionary<int, byte> target = null;

            if (playerId == 0)
            {
                target = player0Inputs;
            }
            else if (playerId == 1)
            {
                target = player1Inputs;
            }

            if (target == null || target.Count == 0)
            {
                bits = 0;
                return false;
            }

            int bestFrame = int.MinValue;
            byte bestBits = 0;
            bool found = false;

            foreach (KeyValuePair<int, byte> pair in target)
            {
                if (pair.Key > frame)
                {
                    continue;
                }

                if (!found || pair.Key > bestFrame)
                {
                    found = true;
                    bestFrame = pair.Key;
                    bestBits = pair.Value;
                }
            }

            bits = bestBits;
            return found;
        }

        public void ClearAll()
        {
            player0Inputs.Clear();
            player1Inputs.Clear();
        }
    }
}
