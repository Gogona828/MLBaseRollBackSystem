using UnityEngine;

public class LocalFrameInputSource : IFrameInputSource
{
    private readonly KeyCode leftKey;
    private readonly KeyCode rightKey;
    private readonly KeyCode attackKey;

    public LocalFrameInputSource()
    {
        leftKey = KeyCode.A;
        rightKey = KeyCode.D;
        attackKey = KeyCode.Space;
    }

    public LocalFrameInputSource(KeyCode leftKey, KeyCode rightKey, KeyCode attackKey)
    {
        this.leftKey = leftKey;
        this.rightKey = rightKey;
        this.attackKey = attackKey;
    }

    public byte GetInputBits(int frame)
    {
        return InputEncoder.ReadLocalInputBits(leftKey, rightKey, attackKey);
    }
}
