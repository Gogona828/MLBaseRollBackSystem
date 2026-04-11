public class RemoteFrameInputSource : IFrameInputSource
{
    private readonly RemoteInputBuffer remoteInputBuffer;
    private readonly byte defaultInputBits;

    public RemoteFrameInputSource(RemoteInputBuffer remoteInputBuffer, byte defaultInputBits = 0)
    {
        this.remoteInputBuffer = remoteInputBuffer;
        this.defaultInputBits = defaultInputBits;
    }

    public byte GetInputBits(int frame)
    {
        if (remoteInputBuffer != null && remoteInputBuffer.TryGetInput(frame, out byte inputBits))
        {
            return inputBits;
        }

        return defaultInputBits;
    }
}
