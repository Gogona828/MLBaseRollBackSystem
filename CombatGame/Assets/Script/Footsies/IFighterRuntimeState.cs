using UnityEngine;

public interface IFighterRuntimeState
{
    Vector3 Velocity { get; set; }

    int FacingSign { get; set; }
    int ActionId { get; set; }
    int ActionFrame { get; set; }

    int HP { get; set; }
    bool IsKO { get; set; }

    bool IsGrounded { get; set; }
    int HitstunFrames { get; set; }
    int BlockstunFrames { get; set; }
    int RecoveryFrames { get; set; }
}
