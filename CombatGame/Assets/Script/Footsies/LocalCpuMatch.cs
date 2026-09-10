using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace Footsies
{
    // Owns only the relay and opponent processes started for this match.
    public sealed class LocalCpuMatch : IDisposable
    {
        public static readonly bool IsCpuClient = Array.IndexOf(Environment.GetCommandLineArgs(), "-cpuClient") >= 0;
        public static int CpuRelayPort => int.Parse(Argument("-cpuRelayPort"));
        public int Port { get; private set; }
        private Process relay;
        private Process opponent;
        public bool HasExited => (relay != null && relay.HasExited) || (opponent != null && opponent.HasExited);

        public static string Argument(string key)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, key);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        public static bool ParentIsAlive()
        {
            try { return !Process.GetProcessById(int.Parse(Argument("-cpuParent"))).HasExited; }
            catch { return false; }
        }

        public void Start()
        {
            string executable = Application.isEditor
                ? PlayerPrefs.GetString("CombatGame.CPU.ClientPath", "")
                : Process.GetCurrentProcess().MainModule.FileName;
            if (!File.Exists(executable))
                throw new InvalidOperationException("先に Play を停止し、CombatGame > Build CPU Client を実行してください。");
            string script = Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "../../Tools/MacLanRelay/server.py"))
                : Path.Combine(Application.streamingAssetsPath, "CpuRelay/server.py");
            if (!File.Exists(script)) throw new FileNotFoundException("CPU relay script is missing. Rebuild the client.", script);
            Port = FindPortPair();
            try
            {
                string python = Environment.GetEnvironmentVariable("COMBATGAME_PYTHON");
                if (string.IsNullOrEmpty(python)) python = Application.platform == RuntimePlatform.WindowsPlayer ? "python" : "python3";
                relay = Launch(python, Quote(script) + " --bind 127.0.0.1 --port " + Port + " --delay 100");
                opponent = Launch(executable, "-cpuClient -machineProfile CPU -cpuRelayPort " + (Port + 1)
                    + " -cpuParent " + Process.GetCurrentProcess().Id
                    + " -screen-fullscreen 0 -screen-width 640 -screen-height 360 -logFile "
                    + Quote(Path.Combine(Application.persistentDataPath, "cpu-client-" + Port + ".log")));
            }
            catch { Dispose(); throw; }
        }

        private static Process Launch(string file, string arguments)
        {
            return Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true });
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

        private static int FindPortPair()
        {
            for (int port = 16000; port < 32000; port += 2)
            {
                try
                {
                    using (var first = new UdpClient(new IPEndPoint(IPAddress.Loopback, port)))
                    using (var second = new UdpClient(new IPEndPoint(IPAddress.Loopback, port + 1))) return port;
                }
                catch (SocketException) { }
            }
            throw new InvalidOperationException("CPU対戦用のUDPポートを確保できませんでした。");
        }

        public void Dispose()
        {
            Stop(ref opponent);
            Stop(ref relay);
        }

        private static void Stop(ref Process process)
        {
            if (process == null) return;
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception ex) { UnityEngine.Debug.LogWarning(ex.Message); }
            finally { process.Dispose(); process = null; }
        }
    }
}
