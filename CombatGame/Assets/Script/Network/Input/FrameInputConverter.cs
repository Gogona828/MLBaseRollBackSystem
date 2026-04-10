public static class FrameInputConverter
{
    public static FrameInputState FromBits(byte inputBits)
    {
        bool left = InputEncoder.HasLeft(inputBits);
        bool right = InputEncoder.HasRight(inputBits);
        bool attack = InputEncoder.HasAttack(inputBits);

        return new FrameInputState(left, right, attack);
    }
}
