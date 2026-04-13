using System;

[Serializable]
public struct BattleStateSnapshot
{
    public int Frame;

    public float P1PosX;
    public float P2PosX;

    public int P1Facing;
    public int P2Facing;

    public int P1StateId;
    public int P2StateId;

    public BattleStateSnapshot(
        int frame,
        float p1PosX,
        float p2PosX,
        int p1Facing,
        int p2Facing,
        int p1StateId,
        int p2StateId)
    {
        Frame = frame;
        P1PosX = p1PosX;
        P2PosX = p2PosX;
        P1Facing = p1Facing;
        P2Facing = p2Facing;
        P1StateId = p1StateId;
        P2StateId = p2StateId;
    }

    public override string ToString()
    {
        return $"frame={Frame}, p1x={P1PosX}, p2x={P2PosX}, p1Facing={P1Facing}, p2Facing={P2Facing}, p1State={P1StateId}, p2State={P2StateId}";
    }
}
