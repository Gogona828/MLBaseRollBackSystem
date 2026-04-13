public struct RollbackResimulationRequest
{
    public int TargetFrame;
    public int ResumeFrameExclusive;
    public bool IsValid;

    public RollbackResimulationRequest(int targetFrame, int resumeFrameExclusive)
    {
        TargetFrame = targetFrame;
        ResumeFrameExclusive = resumeFrameExclusive;
        IsValid = true;
    }

    public static RollbackResimulationRequest Invalid()
    {
        return new RollbackResimulationRequest
        {
            TargetFrame = -1,
            ResumeFrameExclusive = -1,
            IsValid = false
        };
    }

    public override string ToString()
    {
        return $"targetFrame={TargetFrame}, resumeFrameExclusive={ResumeFrameExclusive}, isValid={IsValid}";
    }
}
