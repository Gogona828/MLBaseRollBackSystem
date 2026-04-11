using System;
using System.IO;
using UnityEngine;

public static class FileLogger
{
    private static string logFilePath;
    private static bool initialized = false;

    public static void Initialize(string machineLabel)
    {
        if (initialized)
        {
            return;
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"network_log_{machineLabel}_{timestamp}.txt";

        logFilePath = Path.Combine(Application.persistentDataPath, fileName);
        initialized = true;

        WriteLine($"[FileLogger] Initialized");
        WriteLine($"[FileLogger] persistentDataPath={Application.persistentDataPath}");
        WriteLine($"[FileLogger] logFilePath={logFilePath}");
    }

    public static void WriteLine(string message)
    {
        if (!initialized)
        {
            return;
        }

        string line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        Debug.Log(line);

        try
        {
            File.AppendAllText(logFilePath, line + Environment.NewLine);
        }
        catch (Exception e)
        {
            Debug.LogError($"[FileLogger] Failed to write log file: {e}");
        }
    }
}
