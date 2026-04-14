using UnityEngine;

public class FileLoggerBootstrap : MonoBehaviour
{
    [SerializeField] private string machineLabel = "Unknown";
    [SerializeField] private int playerId = -1;
    [SerializeField] private string remoteIp = "";
    [SerializeField] private int localPort = -1;
    [SerializeField] private int remotePort = -1;
    public string MachineLabel => machineLabel;

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
