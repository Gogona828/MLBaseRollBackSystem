using UnityEngine;

public class NetworkFrameClock : MonoBehaviour
{
    public int CurrentFrame { get; private set; }

    public void ResetClock()
    {
        CurrentFrame = 0;
    }

    public void Tick()
    {
        CurrentFrame++;
    }
}
