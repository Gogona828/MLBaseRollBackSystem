using UnityEngine;

namespace Footsies
{
    public static class FootsiesBattleSnapshotDebugFormatter
    {
        public static int ComputeHash(FootsiesBattleSnapshot snapshot)
        {
            unchecked
            {
                int hash = 17;

                if (snapshot == null)
                {
                    return hash;
                }

                Add(ref hash, snapshot.frameCount);
                Add(ref hash, snapshot.roundStartTime);
                Add(ref hash, snapshot.timer);
                Add(ref hash, (int)snapshot.roundState);
                Add(ref hash, (int)snapshot.fighter1RoundWon);
                Add(ref hash, (int)snapshot.fighter2RoundWon);
                Add(ref hash, (int)snapshot.currentRecordingInputIndex);
                Add(ref hash, (int)snapshot.currentReplayingInputIndex);
                Add(ref hash, (int)snapshot.lastRoundMaxRecordingInput);
                Add(ref hash, snapshot.isReplayingLastRoundInput);
                Add(ref hash, snapshot.isDebugPause);

                AddFighter(ref hash, snapshot.fighter1);
                AddFighter(ref hash, snapshot.fighter2);

                return hash;
            }
        }

        public static string BuildSummary(FootsiesBattleSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "snapshot=null";
            }

            return
                $"hash={ComputeHash(snapshot)}, " +
                $"battle(frameCount={snapshot.frameCount}, roundState={snapshot.roundState}, timer={snapshot.timer:F3}, " +
                $"recIdx={snapshot.currentRecordingInputIndex}, replayIdx={snapshot.currentReplayingInputIndex}, " +
                $"replayMax={snapshot.lastRoundMaxRecordingInput}, replaying={snapshot.isReplayingLastRoundInput}), " +
                $"p1[{BuildFighterSummary(snapshot.fighter1)}], " +
                $"p2[{BuildFighterSummary(snapshot.fighter2)}]";
        }

        private static string BuildFighterSummary(FootsiesFighterSnapshot fighter)
        {
            if (fighter == null)
            {
                return "null";
            }

            return
                $"pos=({fighter.position.x:F3},{fighter.position.y:F3}), " +
                $"vx={fighter.velocityX:F3}, " +
                $"faceRight={fighter.isFaceRight}, " +
                $"hp={fighter.vitalHealth}, " +
                $"guard={fighter.guardHealth}, " +
                $"action={fighter.currentActionID}, " +
                $"actionFrame={fighter.currentActionFrame}, " +
                $"hitCount={fighter.currentActionHitCount}, " +
                $"hitStun={fighter.currentHitStunFrame}, " +
                $"won={fighter.hasWon}";
        }

        private static void AddFighter(ref int hash, FootsiesFighterSnapshot fighter)
        {
            if (fighter == null)
            {
                Add(ref hash, -1);
                return;
            }

            Add(ref hash, fighter.position.x);
            Add(ref hash, fighter.position.y);
            Add(ref hash, fighter.velocityX);
            Add(ref hash, fighter.isFaceRight);
            Add(ref hash, fighter.vitalHealth);
            Add(ref hash, fighter.guardHealth);
            Add(ref hash, fighter.currentActionID);
            Add(ref hash, fighter.currentActionFrame);
            Add(ref hash, fighter.currentActionHitCount);
            Add(ref hash, fighter.currentHitStunFrame);
            Add(ref hash, fighter.isInputBackward);
            Add(ref hash, fighter.isReserveProximityGuard);
            Add(ref hash, fighter.bufferActionID);
            Add(ref hash, fighter.reserveDamageActionID);
            Add(ref hash, fighter.spriteShakePosition);
            Add(ref hash, fighter.hasWon);

            AddArray(ref hash, fighter.input);
            AddArray(ref hash, fighter.inputDown);
            AddArray(ref hash, fighter.inputUp);
        }

        private static void AddArray(ref int hash, int[] values)
        {
            if (values == null)
            {
                Add(ref hash, -1);
                return;
            }

            Add(ref hash, values.Length);

            for (int i = 0; i < values.Length; i++)
            {
                Add(ref hash, values[i]);
            }
        }

        private static void Add(ref int hash, int value)
        {
            hash = (hash * 31) + value;
        }

        private static void Add(ref int hash, float value)
        {
            hash = (hash * 31) + value.GetHashCode();
        }

        private static void Add(ref int hash, bool value)
        {
            hash = (hash * 31) + (value ? 1 : 0);
        }
    }
}
