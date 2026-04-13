using System;

[Serializable]
public struct GhostHitObservationRecord
{
    public int Frame;
    public byte PredictedBits;
    public byte ConfirmedBits;
    public bool PredictedAttack;
    public bool ConfirmedAttack;
    public bool RollbackWasLate;
    public bool Detected;

    public GhostHitObservationRecord(
        int frame,
        byte predictedBits,
        byte confirmedBits,
        bool predictedAttack,
        bool confirmedAttack,
        bool rollbackWasLate,
        bool detected)
    {
        Frame = frame;
        PredictedBits = predictedBits;
        ConfirmedBits = confirmedBits;
        PredictedAttack = predictedAttack;
        ConfirmedAttack = confirmedAttack;
        RollbackWasLate = rollbackWasLate;
        Detected = detected;
    }

    public override string ToString()
    {
        return $"frame={Frame}, predictedBits={PredictedBits}, confirmedBits={ConfirmedBits}, predictedAttack={PredictedAttack}, confirmedAttack={ConfirmedAttack}, rollbackWasLate={RollbackWasLate}, detected={Detected}";
    }
}
