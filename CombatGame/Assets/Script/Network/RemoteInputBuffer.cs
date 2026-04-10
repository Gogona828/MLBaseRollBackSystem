using System.Collections.Generic;

public class RemoteInputBuffer
{
    private readonly Dictionary<int, byte> frameToInput = new Dictionary<int, byte>();

    public void Store(InputPacket packet)
    {
        frameToInput[packet.frame] = packet.inputBits;
    }

    public bool TryGetInput(int frame, out byte inputBits)
    {
        return frameToInput.TryGetValue(frame, out inputBits);
    }

    public bool ContainsFrame(int frame)
    {
        return frameToInput.ContainsKey(frame);
    }

    public void Clear()
    {
        frameToInput.Clear();
    }
}
