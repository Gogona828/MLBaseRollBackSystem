using System;
using System.IO;
using System.Text.Json;
using Footsies;
var opts=new JsonSerializerOptions{IncludeFields=true};
var model=JsonSerializer.Deserialize<FepSupervisedPredictor.Model>(File.ReadAllText(args[0]),opts);
var data=JsonDocument.Parse(File.ReadAllText(args[1])).RootElement;
int count=0;
foreach(var test in data.GetProperty("cases").EnumerateArray())
{
    var horizon=Array.Find(model.horizons,h=>h.frames==test.GetProperty("frames").GetInt32());int row=0;
    foreach(var input in data.GetProperty("values").EnumerateArray())
    {
        float[] x=new float[61];int i=0;foreach(var value in input.EnumerateArray())x[i++]=value.ValueKind==JsonValueKind.Null?float.NaN:value.GetSingle();
        float[] actual=FepSupervisedPredictor.EvaluateTrees(horizon,x);i=0;
        foreach(var expected in test.GetProperty("expected")[row++].EnumerateArray())
            if(Math.Abs(actual[i++]-expected.GetSingle())>2e-5)throw new Exception("C# inference mismatch");
        count++;
    }
}
Console.WriteLine($"Actual C# tree evaluator: {count} rows match original XGBoost logits.");
var shown=new FootsiesBattleSnapshot { fighter1=new FootsiesFighterSnapshot {vitalHealth=3,guardHealth=3},fighter2=new FootsiesFighterSnapshot {vitalHealth=3,guardHealth=3} };
void Check(bool expected,FootsiesBattleSnapshot corrected,float tolerance,string label)
{ if(RollbackStateComparison.CanContinue(shown,corrected,tolerance)!=expected)throw new Exception("State comparison: "+label); }
var correction=shown.Clone();Check(true,correction,0,"exact match");
correction.fighter1.position=new UnityEngine.Vector2(.04f,0);Check(true,correction,.05f,"P1 inside tolerance");Check(false,correction,0,"zero tolerance");
correction.fighter2.position=new UnityEngine.Vector2(.06f,0);Check(false,correction,.05f,"P2 outside tolerance");
correction=shown.Clone();correction.fighter1.vitalHealth--;Check(false,correction,10,"HP mismatch");
correction=shown.Clone();correction.fighter2.guardHealth--;Check(false,correction,10,"guard HP mismatch");
correction=shown.Clone();correction.fighter2.currentActionID=100;Check(false,correction,10,"action mismatch");
correction=shown.Clone();correction.fighter1.currentActionFrame++;Check(false,correction,10,"action frame mismatch");
correction=shown.Clone();correction.hasPendingKO=true;Check(false,correction,10,"KO mismatch");
Check(false,shown,float.NaN,"invalid tolerance");
Console.WriteLine("State comparison: 10 acceptance/rejection checks passed.");
var mac=new LanRoundStartSchedule();var windows=new LanRoundStartSchedule();
mac.ObserveClock(100,1000.01,100.02);
windows.ObserveClock(500,1000.01,500.02);
if(!mac.Schedule(1,1,408,400,1001) || !windows.Schedule(1,1,408,407,1001))throw new Exception("Round not scheduled");
if(mac.IsDue(100.9) || windows.IsDue(500.9))throw new Exception("Round started early");
if(!mac.IsDue(101) || !windows.IsDue(501))throw new Exception("Different local clocks failed to start together");
if(mac.Schedule(1,1,408,400,1002) || mac.LocalDeadline != 101)throw new Exception("Duplicate postponed start");
mac.MarkReleased();
if(mac.IsDue(102) || mac.Schedule(1,2,500,408,1003))throw new Exception("Stale round started again");
if(mac.Schedule(3,2,500,408,1003) || mac.Schedule(2,2,408,408,1003))throw new Exception("Invalid round/frame accepted");
Console.WriteLine("Round sync: separate clocks, common deadline, duplicate/stale messages and frame validation passed.");
var notice=new SimulatedDelayState();
notice.Update(1,true,1.1,100);
if(!notice.IsActive(1.01) || notice.IsActive(1.2))throw new Exception("Delay HUD expiry failed");
notice.Update(2,false,1.1,100);notice.Update(1,true,2,100);
if(notice.IsActive(1.05))throw new Exception("Old notification reactivated delay HUD");
Console.WriteLine("Delay HUD: active window, expiry without end packet, reordered messages passed.");
// A 1000ms server hold remains visible for its absolute window; periodic clock
// replies must not restart or shorten the window, including different PC clocks.
var status = new RelayDelayStatus { delayProtocolVersion=2, delayActive=true, delayRevision=3,
    delayMs=1000, eventDelayMs=1000, serverTime=1000, delayStartedAt=1000, delayUntil=1001 };
var localClock = new LanRoundStartSchedule();
localClock.ObserveClock(100,1000.01,100.02);
var hud = new SimulatedDelayState();
hud.Update(3,true,status.LocalExpiry(100,localClock),status.DisplayMilliseconds);
if(!hud.IsActive(100.01) || !hud.IsActive(100.99) || hud.IsActive(101.01) || hud.Milliseconds != 1000)
    throw new Exception("1000ms HUD window mismatch");
status.serverTime=1000.5;
hud.Update(3,true,status.LocalExpiry(100.5,localClock),status.DisplayMilliseconds);
if(hud.ExpiresAt != 101) throw new Exception("Heartbeat changed event deadline");
status.eventDelayMs=1200;
if(status.DisplayMilliseconds != 1200 || status.delayMs != 1000) throw new Exception("Configured/event jitter duration confused");
status.delayActive=false;
hud.Update(4,false,status.LocalExpiry(101,localClock),status.DisplayMilliseconds);
hud.Update(3,true,102,1000);
if(hud.IsActive(101)) throw new Exception("Stale start reactivated HUD");
Console.WriteLine("1000ms relay display: duration, clock refresh, jitter, end and stale packet checks passed.");
// Exercise the real FEP observation/prediction path and actual CSV serializer.
var predictor = new FepSupervisedPredictor(model);
var receiver = new NetworkInputReceiver();
receiver.Inputs[727]=2;
var self = new Fighter { position=(2f,0f), isFaceRight=false, vitalHealth=3, guardHealth=3,
    currentActionID=2, currentActionFrame=4 };
var opponent = new Fighter { position=(-2f,0f), isFaceRight=true, vitalHealth=3, guardHealth=3 };
predictor.Capture(727,self,opponent,new BattleCore { battleAreaWidth=10 },0,0,0);
predictor.ObserveConfirmed(727,receiver);
var rows = new System.Collections.Generic.List<PredictionLogEntry>();
PredictionTrace originalTrace=null;
foreach(int target in new[]{728,729,730,757,785})
{
    byte bits=predictor.Predict(target-predictor.ObservedFrame,false,2);
    var trace=predictor.CreateTrace(727,1f/60,false,2);
    originalTrace ??= trace;
    float sum=0; foreach(float p in trace.ActionProbabilities) sum+=p;
    if(Math.Abs(sum-1)>1e-5 || bits != FepSupervisedPredictor.ActionToInputBits(trace.SelectedAction,false,2))
        throw new Exception("Probability or action/input conversion mismatch");
    if(trace.Features[1] != 2 || trace.Features[14] != 2 || trace.ObservationFrame != 727)
        throw new Exception("Observation snapshot mismatch");
    float beliefSum=trace.Features[48]+trace.Features[49]+trace.Features[50];
    if(Math.Abs(beliefSum-1)>1e-5) throw new Exception("Belief not normalized");
    var prediction = new PredictionRecord(target,bits,trace);
    if(target==728) prediction.Confirm(bits);
    rows.Add(new PredictionLogEntry { Record=prediction,TargetPlayerId=2,Mode="FepSupervised" });
}
float[] expectedBelief=new float[3]; float normalization=0;
for(int state=0;state<3;state++)
{
    float prior=0;
    for(int previous=0;previous<3;previous++) prior+=model.transition[state*3+previous]*model.initial_belief[previous];
    if(Math.Abs(prior-originalTrace.PriorBelief[state])>1e-6) throw new Exception("Wrong logged belief prior");
    expectedBelief[state]=prior*model.observation_likelihood[2*3+state];normalization+=expectedBelief[state];
}
for(int state=0;state<3;state++)
    if(Math.Abs(expectedBelief[state]/normalization-originalTrace.Features[48+state])>1e-6)
        throw new Exception("Logged belief does not follow the observed retreat likelihood");
float oldBelief=originalTrace.Features[48];
self.currentActionID=100; receiver.Inputs[786]=4;
predictor.Capture(786,self,opponent,new BattleCore { battleAreaWidth=10 },1,0,0);
predictor.ObserveConfirmed(786,receiver); predictor.Predict(1,false,4);
if(originalTrace.Features[14] != 2 || originalTrace.Features[48] != oldBelief)
    throw new Exception("Later observations mutated past prediction trace");
string predictionCsv=PredictionCsv.Build("test",1,rows);
var csvLines=predictionCsv.Trim().Split('\n'); var columns=csvLines[0].TrimEnd('\r').Split(',');
string Field(int row,string column) => csvLines[row].TrimEnd('\r').Split(',')[Array.IndexOf(columns,column)];
string[] expectedFrames={"1","2","3","30","58"};
string[] expectedMs={"16.7","33.3","50.0","500.0","966.7"};
for(int row=1;row<=5;row++)
{
    if(csvLines[row].TrimEnd('\r').Split(',').Length != columns.Length) throw new Exception("CSV column mismatch");
    if(Field(row,"prediction_horizon_frames") != expectedFrames[row-1] || Field(row,"prediction_horizon_ms") != expectedMs[row-1])
        throw new Exception("Actual horizon was truncated to model horizon");
}
if(Field(5,"model_horizon_frames") != "12" || Field(5,"prediction_status") != "Pending" ||
    Field(5,"confirmation_timestamp") != "" || Field(5,"prediction_correct") != "")
    throw new Exception("Pending/capped model fields wrong");
if(Field(1,"confirmation_timestamp") == "" || Field(1,"prediction_timestamp") == "" || Field(1,"prediction_correct") != "true")
    throw new Exception("Prediction/confirmation timestamps lost");
var cold=new FepSupervisedPredictor(model);
cold.Capture(900,self,opponent,new BattleCore(),0,0,0);byte coldBits=cold.Predict(1,false,0);
var coldRows=new[]{new PredictionLogEntry {Record=new PredictionRecord(900,coldBits,cold.CreateTrace(-1,1f/60,false,0))}};
var coldCsv=PredictionCsv.Build("cold",1,coldRows).Trim().Split('\n');
if(coldCsv[1].Split(',')[Array.IndexOf(columns,"prediction_horizon_frames")] != "") throw new Exception("Cold start fabricated horizon");
var buffer=new PredictionHistoryBuffer();
buffer.RecordPrediction(10,2,originalTrace); buffer.TryGetRecord(10,out var first);
buffer.RecordPrediction(10,4); buffer.TryConfirmPrediction(10,2,out var confirmedPrediction);
if(confirmedPrediction.PredictedBits!=2 || confirmedPrediction.PredictionTimestamp!=first.PredictionTimestamp ||
    !ReferenceEquals(confirmedPrediction.Trace,originalTrace) || buffer.TryConfirmPrediction(10,4,out _))
    throw new Exception("Duplicate prediction/confirmation changed original evidence");
if(FepSupervisedPredictor.ActionToInputBits(0,false,2)!=0 ||
   FepSupervisedPredictor.ActionToInputBits(1,false,2)!=1 ||
   FepSupervisedPredictor.ActionToInputBits(2,false,2)!=2 ||
   FepSupervisedPredictor.ActionToInputBits(4,false,2)!=2 ||
   FepSupervisedPredictor.ActionToInputBits(3,false,2)!=6 ||
   FepSupervisedPredictor.ActionToInputBits(1,true,0)!=2)
    throw new Exception("Action mapping does not distinguish default neutral from right input");
Console.WriteLine("Prediction audit: real model observations/beliefs/probabilities, immutable traces, 1–58 frame horizons, pending/confirmed CSV timestamps and duplicate protection passed.");
File.WriteAllText(Path.Combine(Path.GetTempPath(),"prediction-audit-sample.csv"),predictionCsv);
namespace UnityEngine {
 public struct Vector2 { public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}public float sqrMagnitude=>x*x+y*y;public static Vector2 operator -(Vector2 a,Vector2 b)=>new Vector2(a.x-b.x,a.y-b.y); }
 public class TextAsset {public string text;}
 public static class Resources {public static T Load<T>(string name)=>default;}
 public static class JsonUtility {public static T FromJson<T>(string json)=>JsonSerializer.Deserialize<T>(json,new JsonSerializerOptions{IncludeFields=true});}
 public static class Time {public static float fixedDeltaTime=1f/60;}
 public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);public static float Abs(float a)=>Math.Abs(a);public static float Clamp(float a,float b,float c)=>Math.Clamp(a,b,c);public static float Log(float a)=>MathF.Log(a);public static float Exp(float a)=>MathF.Exp(a);}
}
namespace Footsies {
 public enum InputDefine{Left=1,Right=2,Attack=4}
 public class Pushbox {public float xMin,xMax;}
 public class Fighter {public (float x,float y) position;public float velocity_x;public bool isFaceRight,isDead,isInHitStun;public int vitalHealth,guardHealth,currentActionID,currentActionFrame,currentHitStunFrame;public Pushbox pushbox;}
 public class BattleCore {public enum RoundStateType {Stop,Intro,Fight,KO,End} public float battleAreaWidth;}
}
public class NetworkInputReceiver {
 public System.Collections.Generic.Dictionary<int,byte> Inputs=new();
 public bool TryGetRemoteInput(int frame,out byte bits)=>Inputs.TryGetValue(frame,out bits);
}
