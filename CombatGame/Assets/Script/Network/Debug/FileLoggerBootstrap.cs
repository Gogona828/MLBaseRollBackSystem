using System;
using UnityEngine;

public class FileLoggerBootstrap : MonoBehaviour
{
    [SerializeField] private bool initializeOnAwake = true;

    [Tooltip("空なら実行環境から自動決定する。Multiplayer Play ModeではPlayer1/Player2になる")]
    [SerializeField] private string overrideMachineLabel = "";

    [SerializeField] private string machineLabel = "Unknown";
    [SerializeField] private int playerId = -1;
    [SerializeField] private string remoteIp = "";
    [SerializeField] private int localPort = -1;
    [SerializeField] private int remotePort = -1;

    public string MachineLabel => machineLabel;
    public int PlayerId => playerId;
    public string RemoteIp => remoteIp;
    public int LocalPort => localPort;
    public int RemotePort => remotePort;

    public void Configure(string machineLabel, int playerId, string remoteIp, int localPort, int remotePort)
    {
        this.machineLabel = SanitizeFileName(machineLabel);
        this.playerId = playerId;
        this.remoteIp = remoteIp;
        this.localPort = localPort;
        this.remotePort = remotePort;

        FileLogger.Initialize(this.machineLabel);
        WriteCurrentSettings();
    }

    private void Awake()
    {
        if (!initializeOnAwake)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(overrideMachineLabel))
        {
            machineLabel = SanitizeFileName(overrideMachineLabel.Trim());
        }
        else
        {
            machineLabel = SanitizeFileName(ResolveLogLabel());
        }

        FileLogger.Initialize(machineLabel);
        WriteCurrentSettings();
    }

    private void WriteCurrentSettings()
    {
        FileLogger.WriteLine($"[FileLoggerBootstrap] machineLabel={machineLabel}");
        FileLogger.WriteLine($"[FileLoggerBootstrap] playerId={playerId}");
        FileLogger.WriteLine($"[FileLoggerBootstrap] remoteIp={remoteIp}");
        FileLogger.WriteLine($"[FileLoggerBootstrap] localPort={localPort}");
        FileLogger.WriteLine($"[FileLoggerBootstrap] remotePort={remotePort}");
    }

    private static string ResolveLogLabel()
    {
        string explicitProfile =
            GetCommandLineValue("-machineProfile") ??
            GetCommandLineValue("-profile") ??
            GetCommandLineValue("-runtimeProfile");

        if (!string.IsNullOrWhiteSpace(explicitProfile))
        {
            return NormalizePlayerName(explicitProfile);
        }

        string multiplayerPlayModeName = GetCommandLineValue("-name");

        if (!string.IsNullOrWhiteSpace(multiplayerPlayModeName))
        {
            return NormalizePlayerName(multiplayerPlayModeName);
        }

        return SystemInfo.deviceName;
    }

    private static string NormalizePlayerName(string value)
    {
        string trimmed = value.Trim();

        if (trimmed.StartsWith("Player ", StringComparison.OrdinalIgnoreCase))
        {
            string index = trimmed.Substring("Player ".Length).Trim();
            return "Player" + index;
        }

        return trimmed;
    }

    private static string GetCommandLineValue(string key)
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (string.Equals(arg, key, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    return args[i + 1];
                }

                return null;
            }

            string prefix = key + "=";

            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg.Substring(prefix.Length);
            }
        }

        return null;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        foreach (char invalidChar in System.IO.Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidChar, '_');
        }

        return value.Trim();
    }
}
