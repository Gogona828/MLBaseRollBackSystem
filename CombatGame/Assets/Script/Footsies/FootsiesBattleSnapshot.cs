using System;

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

        // battle-level の進行状態も rollback 対象にする
        public uint currentRecordingInputIndex;
        public uint currentReplayingInputIndex;
        public uint lastRoundMaxRecordingInput;
        public bool isReplayingLastRoundInput;
        public bool isDebugPause;

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
                currentRecordingInputIndex = currentRecordingInputIndex,
                currentReplayingInputIndex = currentReplayingInputIndex,
                lastRoundMaxRecordingInput = lastRoundMaxRecordingInput,
                isReplayingLastRoundInput = isReplayingLastRoundInput,
                isDebugPause = isDebugPause,
                fighter1 = fighter1 != null ? fighter1.Clone() : null,
                fighter2 = fighter2 != null ? fighter2.Clone() : null
            };
        }
    }
}
