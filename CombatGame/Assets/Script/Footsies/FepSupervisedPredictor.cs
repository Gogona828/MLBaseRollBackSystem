using System;
using System.Collections.Generic;
using UnityEngine;

namespace Footsies
{
    // Managed inference: no Windows DLL, Python process or network round trip.
    public sealed class FepSupervisedPredictor
    {
        [Serializable] public class Tree { public int[] left, right, feature, missingLeft; public float[] value; public int group; }
        [Serializable] public class Horizon { public int frames; public float[] baseline, projection; public Tree[] trees; }
        [Serializable] public class Model
        {
            public float[] observation_likelihood, transition, initial_belief, habit_prior, utility, cue_likelihood;
            public string[] featureNames;
            public Horizon[] horizons;
        }
        private static Model shared;
        private readonly Model model;
        private float[] belief;
        private readonly List<float[]> history = new List<float[]>();
        private readonly List<int> frames = new List<int>();
        private int previousAction = -1, previousOne = -1, previousTwo = -1, lastFrame = -1;
        private byte previousBits;
        private float[] features;
        private readonly SortedDictionary<int, float[]> snapshots = new SortedDictionary<int, float[]>();
        public int ObservedFrame => lastFrame;
        public FepSupervisedPredictor()
        {
            if (shared == null)
            {
                var asset = Resources.Load<TextAsset>("FepSupervisedModel");
                if (asset == null) throw new InvalidOperationException("FepSupervisedModel resource is missing. Run Tools/MacLanRelay/export_model.py.");
                shared = JsonUtility.FromJson<Model>(asset.text);
            }
            model = shared;
            Reset();
        }
        public void Reset()
        {
            belief = (float[])model.initial_belief.Clone();
            history.Clear(); frames.Clear(); lastFrame = -1;
            previousAction = previousOne = previousTwo = -1; previousBits = 0; features = null; snapshots.Clear();
        }
        public static int ActionClass(int id)
        {
            switch (id) {
                case 0: return 0; case 1: case 10: return 1; case 2: case 11: return 2;
                case 100: case 105: case 110: case 115: return 3;
                case 301: case 305: case 306: case 350: return 4; default: return -1;
            }
        }
        private static int Group(int id) { int c = ActionClass(id); return c >= 0 ? c : id == 200 || id == 310 ? 5 : id == 500 || id == 510 ? 6 : 7; }
        private static float[] Multiply(float[] matrix, float[] vector, int rows)
        {
            var result = new float[rows];
            for (int r=0;r<rows;r++) for(int c=0;c<vector.Length;c++) result[r] += matrix[r*vector.Length+c]*vector[c];
            return result;
        }
        private static void Normalize(float[] values) { float sum=0; foreach(float v in values) sum+=v; for(int i=0;i<values.Length;i++) values[i]/=Mathf.Max(sum,1e-12f); }
        private static float Entropy(float[] values) { float e=0; foreach(float v in values) { float p=Mathf.Clamp(v,1e-12f,1); e-=p*Mathf.Log(p); } return e; }
        private static void FighterFeatures(float[] x, int offset, Fighter f, BattleCore core)
        {
            x[offset]=f.position.x; x[offset+1]=f.position.y; x[offset+2]=f.velocity_x;
            x[offset+3]=f.isFaceRight?1:0; x[offset+4]=f.vitalHealth; x[offset+5]=f.guardHealth;
            x[offset+6]=f.isDead?1:0; x[offset+7]=f.currentActionID; x[offset+8]=Group(f.currentActionID);
            x[offset+9]=f.currentActionFrame; x[offset+10]=f.currentHitStunFrame; x[offset+11]=f.isInHitStun?1:0;
            x[offset+12]=f.pushbox != null && (f.pushbox.xMin <= -core.battleAreaWidth*.5f+.25f || f.pushbox.xMax >= core.battleAreaWidth*.5f-.25f)?1:0;
        }
        // State is captured at the prediction origin, then paired with its confirmed input.
        public void Capture(int frame, Fighter self, Fighter opponent, BattleCore core, float hits, float guards, float breaks)
        {
            var x = new float[48];
            x[0]=Time.fixedDeltaTime;
            FighterFeatures(x,7,self,core); FighterFeatures(x,20,opponent,core);
            x[33]=opponent.position.x-self.position.x; x[34]=Mathf.Abs(x[33]);
            x[35]=opponent.velocity_x*(opponent.isFaceRight?1:-1)-self.velocity_x*(self.isFaceRight?1:-1);
            x[45]=hits; x[46]=guards; x[47]=breaks;
            snapshots[frame]=x;
            if(features == null)
            {
                features=(float[])x.Clone();
                features[36]=features[37]=features[38]=features[40]=float.NaN;
                features[41]=features[42]=-1;
            }
            if(snapshots.Count>512){int oldest=-1;foreach(int key in snapshots.Keys){oldest=key;break;}snapshots.Remove(oldest);}
        }
        public void ObserveConfirmed(int currentFrame, NetworkInputReceiver receiver)
        {
            foreach(var pair in snapshots)
            {
                int frame=pair.Key;
                if(frame<=lastFrame || frame>currentFrame) continue;
                if(!receiver.TryGetRemoteInput(frame,out byte bits)) break;
                Observe(frame,bits,(float[])pair.Value.Clone());
            }
        }
        private void Observe(int frame, byte bits, float[] x)
        {
            x[1]=bits;
            x[2]=(bits&(int)InputDefine.Left)!=0?1:0; x[3]=(bits&(int)InputDefine.Right)!=0?1:0; x[4]=(bits&(int)InputDefine.Attack)!=0?1:0;
            x[5]=bits & ~previousBits; x[6]=previousBits & ~bits; previousBits=bits;
            for(int j=0;j<3;j++) { int lag=j==0?3:j==1?6:12; int i=frames.IndexOf(frame-lag); x[36+j]=i<0?float.NaN:history[i][34]; }
            float sum=x[35]; int count=1;
            for(int i=Math.Max(0,history.Count-11);i<history.Count;i++){sum+=history[i][35];count++;}
            x[39]=sum/count; x[40]=x[34]-x[38];
            if(previousAction>=0 && previousAction!=(int)x[14]){ previousTwo=previousOne;previousOne=previousAction; }
            previousAction=(int)x[14]; x[41]=previousOne; x[42]=previousTwo;
            x[43]=ActionClass((int)x[14])==3?1:0; x[44]=ActionClass((int)x[14])==4?1:0;
            features=(float[])x.Clone();
            for(int i=Math.Max(0,history.Count-29);i<history.Count;i++) for(int j=43;j<48;j++) features[j]+=history[i][j];
            history.Add(x); frames.Add(frame); if(history.Count>30){history.RemoveAt(0);frames.RemoveAt(0);}
            belief=Multiply(model.transition,belief,3);
            int observation=ActionClass((int)x[14]);
            if(observation>=0){for(int i=0;i<3;i++)belief[i]*=model.observation_likelihood[observation*3+i];Normalize(belief);}
            lastFrame=frame;
        }
        public static float[] EvaluateTrees(Horizon h, float[] values)
        {
            var scores=(float[])h.baseline.Clone();
            foreach(var tree in h.trees)
            {
                int node=0;
                while(tree.left[node]>=0){float v=values[tree.feature[node]];node=(float.IsNaN(v)?tree.missingLeft[node]!=0:v<tree.value[node])?tree.left[node]:tree.right[node];}
                scores[tree.group]+=tree.value[node];
            }
            return scores;
        }
        public byte Predict(int missingFrames, bool facingRight, byte knownBits)
        {
            if(features==null) return 0;
            Horizon h=model.horizons[0];
            foreach(var candidate in model.horizons) if(Math.Abs(candidate.frames-missingFrames)<Math.Abs(h.frames-missingFrames)) h=candidate;
            var projected=Multiply(h.projection,belief,3); Normalize(projected);
            var utility=Multiply(model.utility,projected,5);
            var cues=Multiply(model.cue_likelihood,projected,4);
            float entropy=Entropy(projected), max=float.NegativeInfinity;
            for(int i=0;i<5;i++){utility[i]+=Mathf.Log(Mathf.Max(model.habit_prior[i],1e-12f))+(i==0?Mathf.Max(0,entropy-.5f*Entropy(cues)):.1f*entropy);max=Mathf.Max(max,utility[i]);}
            for(int i=0;i<5;i++)utility[i]=Mathf.Exp(utility[i]-max); Normalize(utility);
            var values=new float[61]; Array.Copy(features,values,48); Array.Copy(belief,0,values,48,3); Array.Copy(projected,0,values,51,3); Array.Copy(utility,0,values,54,5);
            values[59]=Entropy(belief);values[60]=Entropy(utility);
            var scores=EvaluateTrees(h,values);
            int action=0; for(int i=1;i<5;i++)if(scores[i]>scores[action])action=i;
            byte forward=(byte)(facingRight?InputDefine.Right:InputDefine.Left), back=(byte)(facingRight?InputDefine.Left:InputDefine.Right);
            // The learned labels are action classes, not button sequences. Preserve an existing
            // attack hold; initiate a neutral attack for Attack, and use back for Guard.
            switch(action){case 1:return forward;case 2:case 4:return back;case 3:return (byte)(knownBits|(int)InputDefine.Attack);default:return 0;}
        }
    }
}
