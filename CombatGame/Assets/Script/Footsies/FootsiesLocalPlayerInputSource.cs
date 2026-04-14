using UnityEngine;

namespace Footsies
{
    public class FootsiesLocalPlayerInputSource : MonoBehaviour, IFootsiesPlayerInputSource
    {
        [SerializeField] private bool isPlayer1 = true;

        public FootsiesInputFrame GetCurrentInput()
        {
            if (InputManager.Instance == null)
            {
                return FootsiesInputFrame.Empty();
            }

            if (isPlayer1)
            {
                return new FootsiesInputFrame(
                    InputManager.Instance.GetButton(InputManager.Command.p1Left),
                    InputManager.Instance.GetButton(InputManager.Command.p1Right),
                    InputManager.Instance.GetButton(InputManager.Command.p1Attack)
                );
            }

            return new FootsiesInputFrame(
                InputManager.Instance.GetButton(InputManager.Command.p2Left),
                InputManager.Instance.GetButton(InputManager.Command.p2Right),
                InputManager.Instance.GetButton(InputManager.Command.p2Attack)
            );
        }
    }
}
