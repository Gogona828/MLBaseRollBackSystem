public struct FrameInputState
{
    public bool Left;
    public bool Right;
    public bool Attack;

    public FrameInputState(bool left, bool right, bool attack)
    {
        Left = left;
        Right = right;
        Attack = attack;
    }

    public static FrameInputState Neutral()
    {
        return new FrameInputState(false, false, false);
    }

    public override string ToString()
    {
        return $"Left={Left}, Right={Right}, Attack={Attack}";
    }
}
