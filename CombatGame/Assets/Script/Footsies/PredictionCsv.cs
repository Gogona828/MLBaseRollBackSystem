using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Footsies
{
    public sealed class PredictionLogEntry
    {
        public PredictionRecord Record;
        public int TargetPlayerId;
        public string Mode;
    }

    public static class PredictionCsv
    {
        public static string Build(string matchId, int roundId, IList<PredictionLogEntry> records)
        {
            string[] featureNames = records.Select(r => r.Record.Trace).FirstOrDefault(t => t != null)?.FeatureNames
                ?? Array.Empty<string>();
            var header = new List<object> {
                "match_id","round_id","timestamp","prediction_timestamp","confirmation_timestamp","network_frame",
                "ai_prediction_target","prediction_status","observation_frame","latest_confirmed_input_frame",
                "prediction_horizon","prediction_horizon_frames","prediction_horizon_ms","input_age_frames",
                "model_horizon_frames","model_horizon_ms","has_confirmed_observation","observation",
                "predicted_action","confirmed_action","prediction_correct","correctness_basis","prediction_confidence",
                "selected_action_probability_percent","selection_rule","facing_right","known_input_bits",
                "predicted_input_bits","confirmed_input_bits","prediction_model",
                "belief_prior_aggressive","belief_prior_defensive","belief_prior_approaching",
                "belief_aggressive","belief_defensive","belief_approaching",
                "projected_belief_aggressive","projected_belief_defensive","projected_belief_approaching",
                "action_prob_wait","action_prob_forward","action_prob_backward","action_prob_attack","action_prob_guard",
                "fep_policy_prob_wait","fep_policy_prob_forward","fep_policy_prob_backward","fep_policy_prob_attack","fep_policy_prob_guard",
                "cue_predictive_prob_0","cue_predictive_prob_1","cue_predictive_prob_2","cue_predictive_prob_3",
                "logit_wait","logit_forward","logit_backward","logit_attack","logit_guard",
                "belief_entropy","fep_policy_entropy"
            };
            header.AddRange(featureNames.Select(n => (object)("observation_" + n)));
            var csv = new StringBuilder(); Append(csv, header);
            foreach (var entry in records)
            {
                var r = entry.Record; var t = r.Trace;
                bool confirmed = r.ResultState != PredictionResultState.Pending;
                int? horizon = t != null && t.ObservationFrame >= 0 ? (int?)Math.Max(0,r.Frame-t.ObservationFrame) : null;
                int? age = t != null && t.LatestConfirmedFrame >= 0 ? (int?)Math.Max(0,r.Frame-t.LatestConfirmedFrame) : null;
                float? confidence = t != null ? (float?)t.ActionProbabilities[t.SelectedAction] : null;
                int observation = t != null ? (int)t.Features[15] : -1;
                var row = new List<object> {
                    matchId, roundId, r.PredictionTimestamp.ToString("O"), r.PredictionTimestamp.ToString("O"),
                    r.ConfirmationTimestamp?.ToString("O"), r.Frame, entry.TargetPlayerId > 0 ? "P"+entry.TargetPlayerId : null,
                    r.ResultState, t?.ObservationFrame, t?.LatestConfirmedFrame,
                    horizon,horizon, horizon.HasValue ? (horizon.Value*t.FixedDeltaTime*1000d).ToString("F1",CultureInfo.InvariantCulture) : null,
                    age,t?.ModelHorizonFrames,t != null ? (t.ModelHorizonFrames*t.FixedDeltaTime*1000d).ToString("F1",CultureInfo.InvariantCulture) : null,
                    t != null && t.ObservationFrame >= 0, observation >= 0 && observation < 5 ? PredictionTrace.Actions[observation] : null,
                    t != null ? PredictionTrace.Actions[t.SelectedAction] : null,
                    null, // Action classes (e.g. Guard vs Retreat) cannot be recovered from raw buttons alone.
                    confirmed ? (object)(r.ResultState == PredictionResultState.Hit) : null,"input_bits",confidence,
                    confidence.HasValue ? (object)(confidence.Value*100f) : null, t != null ? "argmax" : null,
                    t?.FacingRight,t?.KnownBits,r.PredictedBits,confirmed ? (object)r.ConfirmedBits : null,entry.Mode
                };
                Add(row,t?.PriorBelief,0,3); Add(row,t?.Features,48,3); Add(row,t?.Features,51,3);
                Add(row,t?.ActionProbabilities,0,5); Add(row,t?.Features,54,5);
                Add(row,t?.CueProbabilities,0,4); Add(row,t?.Logits,0,5); Add(row,t?.Features,59,2);
                Add(row,t?.Features,0,featureNames.Length); Append(csv,row);
            }
            return csv.ToString();
        }
        private static void Add(List<object> row,float[] values,int start,int count)
        { for(int i=0;i<count;i++) row.Add(values != null && start+i<values.Length ? (object)values[start+i] : null); }
        private static void Append(StringBuilder csv,IEnumerable<object> row)
        {
            csv.AppendLine(string.Join(",",row.Select(value => {
                if(value == null || value is float f && (float.IsNaN(f) || float.IsInfinity(f))) return "";
                string text = value is bool b ? (b ? "true" : "false") : Convert.ToString(value,CultureInfo.InvariantCulture);
                return text.IndexOfAny(new[]{',','"','\n','\r'}) >= 0 ? "\""+text.Replace("\"","\"\"")+"\"" : text;
            })));
        }
    }
}
