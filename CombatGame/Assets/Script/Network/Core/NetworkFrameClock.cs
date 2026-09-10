using UnityEngine;

public class NetworkFrameClock : MonoBehaviour
{
    public int CurrentFrame { get; private set; }

    public void ResetClock()
    {
        CurrentFrame = 0;
    }

    public void SetFrame(int frame) { CurrentFrame = Mathf.Max(0, frame); }

    public void Tick()
    {
        CurrentFrame++;
    }
}
