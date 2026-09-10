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
public class NetworkInputReceiver {public bool TryGetRemoteInput(int frame,out byte bits){bits=0;return false;}}
