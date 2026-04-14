using UnityEngine;

public class FileLoggerBootstrap : MonoBehaviour
{
    [SerializeField] private string machineLabel = "Unknown";
    [SerializeField] private int playerId = -1;
    [SerializeField] private string remoteIp = "";
    [SerializeField] private int localPort = -1;
    [SerializeField] private int remotePort = -1;

    public string MachineLabel => machineLabel;

    public void Configure(string machineLabel, int playerId, string remoteIp, int localPort, int remotePort)
    {
        this.machineLabel = machineLabel;
        this.playerId = playerId;
        this.remoteIp = remoteIp;
        this.localPort = localPort;
        this.remotePort = remotePort;
    }

    private void Awake()
    {
        FileLogger.Initialize(machineLabel);
        FileLogger.WriteLine($"[FileLoggerBootstrap] machineLabel={machineLabel}");
        FileLogger.WriteLine($"[FileLoggerBootstrap] playerId={playerId}");
        FileLogger.WriteLine($"[FileLoggerBootstrap] remoteIp={remoteIp}");
        FileLogger.WriteLine($"[FileLoggerBootstrap] localPort={localPort}");
        FileLogger.WriteLine($"[FileLoggerBootstrap] remotePort={remotePort}");
    }
}
