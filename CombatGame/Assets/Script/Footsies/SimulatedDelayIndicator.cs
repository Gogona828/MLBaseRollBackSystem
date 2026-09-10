using UnityEngine;

namespace Footsies
{
    // Server-driven: a prediction miss is not evidence of simulated network delay.
    public sealed class SimulatedDelayIndicator : MonoBehaviour
    {
        private readonly SimulatedDelayState state = new SimulatedDelayState();
        private GUIStyle labelStyle;
        private Font displayFont;

        public void UpdateStatus(int revision, bool active, double expiresAt, float milliseconds)
        {
            state.Update(revision,active,expiresAt,milliseconds);
        }

        private void OnGUI()
        {
            if(!state.IsActive(Time.realtimeSinceStartupAsDouble)) return;
            if(labelStyle == null)
            {
                displayFont=Font.CreateDynamicFontFromOSFont(new[] { "Hiragino Sans", "Yu Gothic", "Meiryo", "Arial" },24);
                labelStyle=new GUIStyle(GUI.skin.label) { alignment=TextAnchor.MiddleCenter, font=displayFont };
                labelStyle.normal.textColor=Color.black;
            }
            labelStyle.fontSize=Mathf.Max(18,Mathf.RoundToInt(Screen.height*0.026f));
            Color previousColor=GUI.color;
            int previousDepth=GUI.depth;
            GUI.color=Color.white;
            GUI.depth=-100;
            GUI.Label(new Rect(0,Screen.height-52,Screen.width,40),$"擬似遅延発生中（{state.Milliseconds:0} ms）",labelStyle);
            GUI.color=previousColor;
            GUI.depth=previousDepth;
        }

        private void OnDestroy() { if(displayFont != null) Destroy(displayFont); }
    }
}
