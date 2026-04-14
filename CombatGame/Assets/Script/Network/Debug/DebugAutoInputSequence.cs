using UnityEngine;

public class DebugAutoInputSequence : MonoBehaviour
{
    [System.Serializable]
    public class SequenceStep
    {
        public int durationFrames = 10;
        public bool left = false;
        public bool right = false;
        public bool attack = false;
    }

    [Header("Sequence")]
    [SerializeField] private SequenceStep[] steps = new SequenceStep[]
    {
        new SequenceStep { durationFrames = 8, left = false, right = false, attack = false },
        new SequenceStep { durationFrames = 2, left = false, right = true,  attack = false },
        new SequenceStep { durationFrames = 2, left = false, right = false, attack = false },
        new SequenceStep { durationFrames = 2, left = false, right = false, attack = true  },
        new SequenceStep { durationFrames = 3, left = false, right = false, attack = false },
        new SequenceStep { durationFrames = 2, left = true,  right = false, attack = false },
        new SequenceStep { durationFrames = 2, left = false, right = false, attack = false },
        new SequenceStep { durationFrames = 2, left = false, right = true,  attack = true  },
        new SequenceStep { durationFrames = 4, left = false, right = false, attack = false },
    };

    [Header("Playback")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool loop = true;

    private int currentStepIndex = 0;
    private int currentStepFrame = 0;
    private bool isPlaying = false;

    private void Start()
    {
        isPlaying = playOnStart;
        currentStepIndex = 0;
        currentStepFrame = 0;
    }

    public byte GetBits()
    {
        if (!isPlaying || steps == null || steps.Length == 0)
        {
            return 0;
        }

        SequenceStep step = steps[currentStepIndex];
        byte bits = InputEncoder.ToBits(step.left, step.right, step.attack);

        AdvanceFrame();

        return bits;
    }

    private void AdvanceFrame()
    {
        if (steps == null || steps.Length == 0)
        {
            return;
        }

        currentStepFrame++;

        SequenceStep step = steps[currentStepIndex];
        int duration = Mathf.Max(1, step.durationFrames);

        if (currentStepFrame < duration)
        {
            return;
        }

        currentStepFrame = 0;
        currentStepIndex++;

        if (currentStepIndex >= steps.Length)
        {
            if (loop)
            {
                currentStepIndex = 0;
            }
            else
            {
                currentStepIndex = steps.Length - 1;
                isPlaying = false;
            }
        }
    }

    public void ResetSequence()
    {
        currentStepIndex = 0;
        currentStepFrame = 0;
        isPlaying = playOnStart;
    }

    public void SetPlaying(bool playing)
    {
        isPlaying = playing;
    }
}
