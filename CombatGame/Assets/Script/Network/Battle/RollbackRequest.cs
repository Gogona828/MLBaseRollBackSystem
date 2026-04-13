public struct RollbackRequest
{
    public int TargetFrame;
    public bool IsValid;

    public RollbackRequest(int targetFrame)
    {
        TargetFrame = targetFrame;
        IsValid = true;
    }

    public static RollbackRequest Invalid()
    {
        return new RollbackRequest
        {
            TargetFrame = -1,
            IsValid = false
        };
    }
}
