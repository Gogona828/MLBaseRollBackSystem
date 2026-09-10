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

    public IEnumerable<KeyValuePair<int, byte>> Entries => frameToInput;
    public void DiscardBefore(int minimumFrame)
    {
        var expired=new List<int>();
        foreach(int frame in frameToInput.Keys) if(frame < minimumFrame) expired.Add(frame);
        foreach(int frame in expired) frameToInput.Remove(frame);
    }

    public void Clear()
    {
        frameToInput.Clear();
    }
}
