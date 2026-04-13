public interface IRemoteInputPredictor
{
    byte PredictInputBits(int frame);
    void OnConfirmedInput(int frame, byte inputBits);
    void Reset();
}
