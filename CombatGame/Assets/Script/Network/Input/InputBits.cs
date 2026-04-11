using System;

[Flags]
public enum InputBits : byte
{
    None   = 0,
    Left   = 1 << 0,
    Right  = 1 << 1,
    Attack = 1 << 2
}