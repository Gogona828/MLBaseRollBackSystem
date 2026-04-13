using UnityEngine;

public class DebugAutoInputSequence : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private NetworkSessionManager sessionManager;
    [SerializeField] private NetworkFrameClock frameClock;

    [Header("Enable")]
    [SerializeField] private bool enableAutoSequence = true;

    [Header("Sequence Frames")]
    [SerializeField] private int startFrame = 20;
    [SerializeField] private int leftStartFrame = 25;
    [SerializeField] private int leftEndFrame = 32;
    [SerializeField] private int neutralStartFrame = 33;
    [SerializeField] private int neutralEndFrame = 36;
    [SerializeField] private int attackStartFrame = 37;
    [SerializeField] private int attackEndFrame = 40;
    [SerializeField] private int rightStartFrame = 41;
    [SerializeField] private int rightEndFrame = 48;
    [SerializeField] private int bothStartFrame = 49;
    [SerializeField] private int bothEndFrame = 54;

    [Header("Loop")]
    [SerializeField] private bool loopSequence = true;
    [SerializeField] private int loopLengthFrames = 70;

    public bool Left { get; private set; }
    public bool Right { get; private set; }
    public bool Attack { get; private set; }

    private void Update()
    {
        if (!enableAutoSequence)
        {
            Left = false;
            Right = false;
            Attack = false;
            return;
        }

        if (sessionManager == null || frameClock == null)
        {
            Left = false;
            Right = false;
            Attack = false;
            return;
        }

        if (!sessionManager.Running)
        {
            Left = false;
            Right = false;
            Attack = false;
            return;
        }

        int frame = frameClock.CurrentFrame;

        if (loopSequence && loopLengthFrames > 0)
        {
            frame %= loopLengthFrames;
        }

        Left = false;
        Right = false;
        Attack = false;

        if (frame < startFrame)
        {
            return;
        }

        if (frame >= leftStartFrame && frame <= leftEndFrame)
        {
            Left = true;
            return;
        }

        if (frame >= neutralStartFrame && frame <= neutralEndFrame)
        {
            return;
        }

        if (frame >= attackStartFrame && frame <= attackEndFrame)
        {
            Attack = true;
            return;
        }

        if (frame >= rightStartFrame && frame <= rightEndFrame)
        {
            Right = true;
            return;
        }

        if (frame >= bothStartFrame && frame <= bothEndFrame)
        {
            Left = true;
            Attack = true;
            return;
        }
    }

    public byte GetBits()
    {
        return InputEncoder.ToBits(Left, Right, Attack);
    }
}
