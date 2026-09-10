using System;
using System.Collections.Generic;
using System.Reflection;

// Exercise the production sender with a counted AI and an in-memory transport.
static class Program
{
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name); }
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);
    static void Main()
    {
        var core = new Footsies.BattleCore();
        UnityEngine.MonoBehaviour.Core = core;
        var sender = new NetworkInputSender();
        var transport = new UdpP2PTransport();
        var session = new NetworkSessionManager();
        var history = new Footsies.FootsiesBattleInputHistory();
        Set(sender, "transport", transport); Set(sender, "sessionManager", session); Set(sender, "inputHistory", history);
        sender.ConfigureRuntime(1, UnityEngine.KeyCode.A, UnityEngine.KeyCode.D, UnityEngine.KeyCode.Space, false);
        sender.EnableLanInputBuffer(); sender.EnableCpuInput();
        sender.ProcessSendForFrame(0);
        Check(Footsies.BattleAI.Calls == 0 && transport.Packets.Count == 0, "no CPU input before handshake");
        session.Running = true; transport.IsStarted = true;
        sender.ProcessSendForFrame(0);
        Check(Footsies.BattleAI.Calls == 1 && transport.Packets.Count == 4, "one AI input plus three neutral buffer seeds");
        Check(transport.Packets[3].Player == 1 && transport.Packets[3].Frame == 3 && transport.Packets[3].Bits == 5, "CPU input is sent as P2 at buffered frame");
        Check(sender.LastLocalInputBits == 0 && history.TryGetInput(1, 3, out var bits) && bits == 5, "local application uses the same delayed timeline");
        sender.ProcessSendForFrame(0);
        typeof(NetworkInputSender).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(sender, null);
        Check(Footsies.BattleAI.Calls == 1 && transport.Packets.Count == 4, "duplicate send and render update do not advance AI");
        sender.ProcessSendForFrame(1); sender.ProcessSendForFrame(2); sender.ProcessSendForFrame(3);
        Check(sender.LastLocalInputBits == 5, "recorded CPU input becomes local input at frame 3");
        core.roundState = Footsies.BattleCore.RoundStateType.Intro;
        int calls = Footsies.BattleAI.Calls;
        sender.ProcessSendForFrame(4);
        Check(Footsies.BattleAI.Calls == calls && transport.Packets[^1].Bits == 0, "intro sends neutral without advancing AI");
        int instances = Footsies.BattleAI.Instances;
        sender.ResetSenderState();
        Check(Footsies.BattleAI.Instances == instances + 1 && sender.LastSentInputFrame == -1, "synchronized round resets CPU plan and sender");
        core.roundState = Footsies.BattleCore.RoundStateType.Fight;
        sender.ProcessSendForFrame(100);
        Check(transport.Packets[^1].Frame == 103 && Footsies.BattleAI.Calls == calls + 1, "CPU resumes on next round timeline");
    }
}

namespace UnityEngine
{
    public class MonoBehaviour { public static Footsies.BattleCore Core; protected static T FindObjectOfType<T>(bool inactive) where T : class => Core as T; }
    public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string value) {} }
    public sealed class SerializeField : Attribute {}
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string value) {} }
    public sealed class MinAttribute : Attribute { public MinAttribute(int value) {} }
    public enum KeyCode { A, D, Space }
    public static class Mathf { public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max); }
}
namespace Footsies
{
    public class BattleCore { public enum RoundStateType { Fight, Intro } public RoundStateType roundState; }
    public class BattleAI { public static int Calls, Instances; public BattleAI(BattleCore core) { Instances++; } public int GetNextAIInput() { Calls++; return 5; } }
    public class FootsiesBattleInputHistory
    {
        readonly Dictionary<(int,int), byte> inputs = new();
        public void StoreInput(int player, int frame, byte bits) => inputs[(player,frame)] = bits;
        public bool TryGetInput(int player, int frame, out byte bits) => inputs.TryGetValue((player,frame), out bits);
    }
}
public class UdpP2PTransport { public bool IsStarted; public List<NetworkPacket> Packets = new(); public void Send(NetworkPacket packet) => Packets.Add(packet); }
public class NetworkSessionManager { public bool Running; }
public class DebugAutoInputSequence { public byte GetBits() => 0; public void ResetSequence() {} }
public enum NetworkPacketType { Input }
public record NetworkPacket(NetworkPacketType Type, int Player, int Frame, byte Bits, int Reserved);
public static class FileLogger { public static void WriteLine(string message) {} }
public static class InputEncoder { public static byte ReadLocalInputBits(UnityEngine.KeyCode l, UnityEngine.KeyCode r, UnityEngine.KeyCode a) => throw new Exception("CPU must not read keyboard"); }
