using System;
using UnityEngine;

namespace Footsies
{
    [Serializable]
    public class FootsiesFighterSnapshot
    {
        public Vector2 position;
        public float velocityX;
        public bool isFaceRight;

        public int vitalHealth;
        public int guardHealth;

        public int currentActionID;
        public int currentActionFrame;
        public int currentActionHitCount;
        public int currentHitStunFrame;

        public bool isInputBackward;
        public bool isReserveProximityGuard;

        public int bufferActionID;
        public int reserveDamageActionID;

        public int spriteShakePosition;
        public bool hasWon;

        public int[] input;
        public int[] inputDown;
        public int[] inputUp;

        public FootsiesFighterSnapshot Clone()
        {
            return new FootsiesFighterSnapshot
            {
                position = position,
                velocityX = velocityX,
                isFaceRight = isFaceRight,
                vitalHealth = vitalHealth,
                guardHealth = guardHealth,
                currentActionID = currentActionID,
                currentActionFrame = currentActionFrame,
                currentActionHitCount = currentActionHitCount,
                currentHitStunFrame = currentHitStunFrame,
                isInputBackward = isInputBackward,
                isReserveProximityGuard = isReserveProximityGuard,
                bufferActionID = bufferActionID,
                reserveDamageActionID = reserveDamageActionID,
                spriteShakePosition = spriteShakePosition,
                hasWon = hasWon,
                input = input != null ? (int[])input.Clone() : null,
                inputDown = inputDown != null ? (int[])inputDown.Clone() : null,
                inputUp = inputUp != null ? (int[])inputUp.Clone() : null
            };
        }
    }
}
