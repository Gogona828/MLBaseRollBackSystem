using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

public static class FileLogger
{
    private static string logFilePath;
    private static bool initialized = false;

    private static readonly object fileLock = new object();

    public static void Initialize(string machineLabel)
    {
        lock (fileLock)
        {
            if (initialized)
            {
                return;
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"network_log_{machineLabel}_{timestamp}.txt";

            logFilePath = Path.Combine(Application.persistentDataPath, fileName);
            initialized = true;

            WriteLineInternal($"[FileLogger] Initialized");
            WriteLineInternal($"[FileLogger] persistentDataPath={Application.persistentDataPath}");
            WriteLineInternal($"[FileLogger] logFilePath={logFilePath}");
        }
    }

    public static void WriteLine(string message)
    {
        if (!initialized)
        {
            return;
        }

        string line = $"{DateTime.Now:HH:mm:ss.fff} {message}";

        Debug.Log(line);

        lock (fileLock)
        {
            WriteLineInternal(line);
        }
    }

    public static void WriteError(string message)
    {
        if (!initialized)
        {
            return;
        }

        string line = $"{DateTime.Now:HH:mm:ss.fff} {message}";

        Debug.LogError(line);

        lock (fileLock)
        {
            WriteLineInternal(line);
        }
    }

    private static void WriteLineInternal(string line)
    {
        const int maxRetries = 3;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using (FileStream stream = new FileStream(
                    logFilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read))
                using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    writer.WriteLine(line);
                }

                return;
            }
            catch (IOException)
            {
                if (i == maxRetries - 1)
                {
                    Debug.LogError($"[FileLogger] Failed to write log file after retries: {logFilePath}");
                    return;
                }

                Thread.Sleep(5);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FileLogger] Failed to write log file: {e}");
                return;
            }
        }
    }
}
