public interface IRoundRuntimeState
{
    int RoundTimerFrames { get; set; }
    bool IsRoundOver { get; set; }
    int WinnerPlayerId { get; set; }
    uint RandomState { get; set; }
}
