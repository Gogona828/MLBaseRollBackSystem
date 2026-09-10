using UnityEngine;
using UnityEngine.UI;

namespace Footsies
{
    // Server-driven: a prediction miss is not evidence of simulated network delay.
    public sealed class SimulatedDelayIndicator : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Text delayText;
        [SerializeField] private GameObject targetObject;

        [Header("Display Settings")]
        [SerializeField] private string textFormat = "擬似遅延発生中（{0:0} ms）";
        [SerializeField] private bool hideWhenInactive = true;

        private readonly SimulatedDelayState state = new SimulatedDelayState();

        public void UpdateStatus(int revision, bool active, double expiresAt, float milliseconds)
        {
            state.Update(revision, active, expiresAt, milliseconds);
            RefreshDisplay(Time.realtimeSinceStartupAsDouble);
        }

        private void Awake()
        {
            ResolveReferences();
            SetVisible(false);
        }

        private void Start()
        {
            ResolveReferences();
            RefreshDisplay(Time.realtimeSinceStartupAsDouble);
        }

        private void Update()
        {
            RefreshDisplay(Time.realtimeSinceStartupAsDouble);
        }

        private void ResolveReferences()
        {
            if (delayText == null)
            {
                var texts = Resources.FindObjectsOfTypeAll<Text>();
                foreach (var t in texts)
                {
                    if (t != null && t.gameObject.name == "DelayText" && t.gameObject.scene.isLoaded)
                    {
                        delayText = t;
                        break;
                    }
                }

                if (delayText == null)
                {
                    delayText = GetComponentInChildren<Text>(true);
                }
            }

            if (targetObject == null && delayText != null)
            {
                targetObject = delayText.gameObject;
            }
        }

        private void RefreshDisplay(double now)
        {
            if (delayText == null && targetObject == null)
            {
                ResolveReferences();
            }

            bool active = state.IsActive(now);

            if (active)
            {
                SetVisible(true);
                if (delayText != null)
                {
                    delayText.text = string.Format(textFormat, state.Milliseconds);
                }
            }
            else
            {
                SetVisible(false);
            }
        }

        private void SetVisible(bool visible)
        {
            if (!hideWhenInactive && !visible)
            {
                if (delayText != null)
                {
                    delayText.text = string.Empty;
                }
                return;
            }

            if (targetObject != null)
            {
                if (targetObject.activeSelf != visible)
                {
                    targetObject.SetActive(visible);
                }
            }
            else if (delayText != null)
            {
                if (delayText.enabled != visible)
                {
                    delayText.enabled = visible;
                }
                if (!visible)
                {
                    delayText.text = string.Empty;
                }
            }
        }
    }
}
