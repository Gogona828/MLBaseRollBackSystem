using UnityEngine;

public class RollbackStateTester : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private bool isPlayer1 = true;

    [Header("Debug State")]
    [SerializeField] private float simulatedPosX = 0f;
    [SerializeField] private int facing = 1;
    [SerializeField] private int stateId = 0;

    public bool IsPlayer1 => isPlayer1;
    public float SimulatedPosX => simulatedPosX;
    public int Facing => facing;
    public int StateId => stateId;

    public void ApplyInput(byte inputBits)
    {
        bool left = (inputBits & 1) != 0;
        bool right = (inputBits & 2) != 0;
        bool attack = (inputBits & 4) != 0;

        if (left && !right)
        {
            simulatedPosX -= 0.1f;
            facing = -1;
            stateId = 1;
        }
        else if (right && !left)
        {
            simulatedPosX += 0.1f;
            facing = 1;
            stateId = 2;
        }
        else if (attack)
        {
            stateId = 3;
        }
        else
        {
            stateId = 0;
        }

        transform.position = new Vector3(simulatedPosX, transform.position.y, transform.position.z);
    }

    public void RestoreState(float posX, int facing, int stateId)
    {
        simulatedPosX = posX;
        this.facing = facing;
        this.stateId = stateId;

        transform.position = new Vector3(simulatedPosX, transform.position.y, transform.position.z);
    }
}
