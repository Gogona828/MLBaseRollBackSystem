using System;
using UnityEngine;

namespace Footsies
{
    [Serializable]
    public struct FootsiesInputFrame
    {
        public bool Left;
        public bool Right;
        public bool Attack;

        public FootsiesInputFrame(bool left, bool right, bool attack)
        {
            Left = left;
            Right = right;
            Attack = attack;
        }

        public InputData ToInputData(float time)
        {
            InputData data = new InputData();
            if (Left) data.input |= (int)InputDefine.Left;
            if (Right) data.input |= (int)InputDefine.Right;
            if (Attack) data.input |= (int)InputDefine.Attack;
            data.time = time;
            return data;
        }

        public static FootsiesInputFrame FromBits(byte bits)
        {
            return new FootsiesInputFrame(
                (bits & 1) != 0,
                (bits & 2) != 0,
                (bits & 4) != 0
            );
        }

        public static FootsiesInputFrame Empty()
        {
            return new FootsiesInputFrame(false, false, false);
        }

        public override string ToString()
        {
            return $"Left={Left}, Right={Right}, Attack={Attack}";
        }
    }
}
