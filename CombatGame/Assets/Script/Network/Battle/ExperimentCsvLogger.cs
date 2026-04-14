using System;
using System.IO;
using UnityEngine;

public static class ExperimentCsvLogger
{
    private static string fileName = "rollback_experiment_results.csv";

    public static void AppendRecord(ExperimentResultRecord record)
    {
        try
        {
            string path = Path.Combine(Application.persistentDataPath, fileName);

            bool exists = File.Exists(path);

            using (StreamWriter writer = new StreamWriter(path, true))
            {
                if (!exists)
                {
                    writer.WriteLine(ExperimentResultRecord.CsvHeader());
                }

                writer.WriteLine(record.ToCsvRow());
            }

            FileLogger.WriteLine($"[ExperimentCsvLogger] Appended record to {path}");
            FileLogger.WriteLine($"[ExperimentCsvLogger] Record = {record.ToCsvRow()}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ExperimentCsvLogger] Failed to append CSV: {ex}");
        }
    }
}
