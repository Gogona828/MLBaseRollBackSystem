using System;
using UnityEngine;

namespace Footsies
{
    [Serializable]
    public class FootsiesBattleSnapshot
    {
        public int frameCount;
        public float roundStartTime;
        public float timer;
        public BattleCore.RoundStateType roundState;

        public uint fighter1RoundWon;
        public uint fighter2RoundWon;

        public FootsiesFighterSnapshot fighter1;
        public FootsiesFighterSnapshot fighter2;

        public FootsiesBattleSnapshot Clone()
        {
            return new FootsiesBattleSnapshot
            {
                frameCount = frameCount,
                roundStartTime = roundStartTime,
                timer = timer,
                roundState = roundState,
                fighter1RoundWon = fighter1RoundWon,
                fighter2RoundWon = fighter2RoundWon,
                fighter1 = fighter1 != null ? fighter1.Clone() : null,
                fighter2 = fighter2 != null ? fighter2.Clone() : null
            };
        }
    }
}
