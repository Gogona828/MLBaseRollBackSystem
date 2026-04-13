using UnityEngine;

public static class InputEncoder
{
    public static byte ReadLocalInputBits(
        KeyCode leftKey,
        KeyCode rightKey,
        KeyCode attackKey)
    {
        InputBits bits = InputBits.None;

        if (Input.GetKey(leftKey))
        {
            bits |= InputBits.Left;
        }

        if (Input.GetKey(rightKey))
        {
            bits |= InputBits.Right;
        }

        if (Input.GetKey(attackKey))
        {
            bits |= InputBits.Attack;
        }

        return (byte)bits;
    }

    public static bool HasLeft(byte inputBits)
    {
        return (((InputBits)inputBits) & InputBits.Left) != 0;
    }

    public static bool HasRight(byte inputBits)
    {
        return (((InputBits)inputBits) & InputBits.Right) != 0;
    }

    public static bool HasAttack(byte inputBits)
    {
        return (((InputBits)inputBits) & InputBits.Attack) != 0;
    }

    public static string ToReadableString(byte inputBits)
    {
        InputBits bits = (InputBits)inputBits;

        if (bits == InputBits.None)
        {
            return "None";
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        if ((bits & InputBits.Left) != 0)
        {
            sb.Append("Left ");
        }

        if ((bits & InputBits.Right) != 0)
        {
            sb.Append("Right ");
        }

        if ((bits & InputBits.Attack) != 0)
        {
            sb.Append("Attack ");
        }

        return sb.ToString().TrimEnd();
    }
    
    public static byte ToBits(bool left, bool right, bool attack)
    {
        byte bits = 0;

        if (left)
        {
            bits |= 1;
        }

        if (right)
        {
            bits |= 2;
        }

        if (attack)
        {
            bits |= 4;
        }

        return bits;
    }
}
