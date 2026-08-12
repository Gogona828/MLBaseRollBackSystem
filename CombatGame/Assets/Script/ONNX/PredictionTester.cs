using System.Runtime.InteropServices;
using System;
using UnityEngine;
using System.IO;

public class PredictionTester : MonoBehaviour
{
    [DllImport("PredictionPlugin", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool InitModel(string modelPath);

    [DllImport("PredictionPlugin", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Predict(float[] inputData, int inputSize, float[] outputData, int outputSize);

    [DllImport("PredictionPlugin", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ReleaseModel();

    private float[] inputBuffer = new float[10];
    private float[] outputBuffer = new float[2];
    private bool modelInitialized;
    private bool pluginUnavailable;

    void Start()
    {
        if (Application.platform != RuntimePlatform.WindowsEditor
            && Application.platform != RuntimePlatform.WindowsPlayer)
        {
            DisablePlugin("PredictionPlugin is a Windows-only native plugin.");
            return;
        }

        // StreamingAssetsからモデルパスを取得
        string modelPath = Path.Combine(Application.streamingAssetsPath, "dummy_model.onnx");

        try
        {
            modelInitialized = InitModel(modelPath);
            if (modelInitialized)
            {
                Debug.Log("ONNX Model initialized successfully.");
            }
            else
            {
                DisablePlugin("Failed to initialize ONNX Model.");
            }
        }
        catch (Exception exception) when (IsNativePluginLoadException(exception))
        {
            DisablePlugin($"PredictionPlugin could not be loaded: {exception.Message}");
        }
    }

    void FixedUpdate()
    {
        if (!modelInitialized || pluginUnavailable)
        {
            return;
        }

        // FOOTSIESのステートに見立てたダミーデータの生成
        for (int i = 0; i < inputBuffer.Length; i++)
        {
            inputBuffer[i] = UnityEngine.Random.value;
        }

        // 推論の実行 (毎フレーム呼び出し)
        try
        {
            if (Predict(inputBuffer, inputBuffer.Length, outputBuffer, outputBuffer.Length))
            {
                // FOOTSIESのC++ DLLが毎フレームエラーなく呼び出せる状態かの疎通確認
                // Debug.Log($"Predict Success: {outputBuffer[0]}, {outputBuffer[1]}");
            }
        }
        catch (Exception exception) when (IsNativePluginLoadException(exception))
        {
            modelInitialized = false;
            DisablePlugin($"PredictionPlugin became unavailable: {exception.Message}");
        }
    }

    void OnDestroy()
    {
        if (!modelInitialized || pluginUnavailable)
        {
            return;
        }

        try
        {
            ReleaseModel();
        }
        catch (Exception exception) when (IsNativePluginLoadException(exception))
        {
            Debug.LogWarning($"PredictionPlugin release was skipped: {exception.Message}");
        }
        finally
        {
            modelInitialized = false;
        }
    }

    private void DisablePlugin(string reason)
    {
        if (!pluginUnavailable)
        {
            Debug.LogWarning($"[PredictionTester] {reason} Prediction testing is disabled.");
        }

        pluginUnavailable = true;
        modelInitialized = false;
        enabled = false;
    }

    private static bool IsNativePluginLoadException(Exception exception)
    {
        return exception is DllNotFoundException
            || exception is EntryPointNotFoundException
            || exception is BadImageFormatException;
    }
}
