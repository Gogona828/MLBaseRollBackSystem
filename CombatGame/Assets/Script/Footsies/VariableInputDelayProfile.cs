using UnityEngine;

[System.Serializable]
public class VariableInputDelayProfile
{
    [Header("Base Delay")]
    public int baseDelayFrames = 2;

    [Header("Jitter")]
    public bool enableJitter = true;
    public int jitterMin = -1;
    public int jitterMax = 1;

    [Header("Spike")]
    public bool enableSpike = true;
    [Range(0f, 1f)]
    public float spikeChance = 0.05f;
    public int spikeExtraMin = 3;
    public int spikeExtraMax = 5;

    public int SampleDelayFrames()
    {
        int delay = baseDelayFrames;

        if (enableJitter)
        {
            delay += Random.Range(jitterMin, jitterMax + 1);
        }

        if (enableSpike && Random.value < spikeChance)
        {
            delay += Random.Range(spikeExtraMin, spikeExtraMax + 1);
        }

        if (delay < 0)
        {
            delay = 0;
        }

        return delay;
    }
}
