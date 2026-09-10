using System;

namespace Footsies
{
    // Immutable copy of the inference that produced an applied prediction.
    public sealed class PredictionTrace
    {
        public readonly int ObservationFrame, LatestConfirmedFrame, ModelHorizonFrames, SelectedAction;
        public readonly float FixedDeltaTime;
        public readonly bool FacingRight;
        public readonly byte KnownBits;
        public readonly string[] FeatureNames;
        public readonly float[] Features, PriorBelief, CueProbabilities, Logits, ActionProbabilities;
        public PredictionTrace(int observationFrame, int latestConfirmedFrame, int modelHorizonFrames,
            int selectedAction, float fixedDeltaTime, bool facingRight, byte knownBits, string[] featureNames,
            float[] features, float[] priorBelief, float[] cues, float[] logits, float[] probabilities)
        {
            ObservationFrame=observationFrame; LatestConfirmedFrame=latestConfirmedFrame;
            ModelHorizonFrames=modelHorizonFrames; SelectedAction=selectedAction; FixedDeltaTime=fixedDeltaTime;
            FacingRight=facingRight; KnownBits=knownBits;
            FeatureNames=(string[])featureNames.Clone(); Features=(float[])features.Clone();
            PriorBelief=(float[])priorBelief.Clone(); CueProbabilities=(float[])cues.Clone();
            Logits=(float[])logits.Clone(); ActionProbabilities=(float[])probabilities.Clone();
        }
        public static readonly string[] Actions = { "Wait", "Approach", "Retreat", "Attack", "Guard" };
    }
}
