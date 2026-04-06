using System.Runtime.InteropServices;
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

    void Start()
    {
        // StreamingAssetsからモデルパスを取得
        string modelPath = Path.Combine(Application.streamingAssetsPath, "dummy_model.onnx");
        
        if (InitModel(modelPath))
        {
            Debug.Log("ONNX Model initialized successfully.");
        }
        else
        {
            Debug.LogError("Failed to initialize ONNX Model.");
        }
    }

    void FixedUpdate()
    {
        // FOOTSIESのステートに見立てたダミーデータの生成
        for (int i = 0; i < inputBuffer.Length; i++)
        {
            inputBuffer[i] = Random.value;
        }

        // 推論の実行 (毎フレーム呼び出し)
        if (Predict(inputBuffer, inputBuffer.Length, outputBuffer, outputBuffer.Length))
        {
            // FOOTSIESのC++ DLLが毎フレームエラーなく呼び出せる状態かの疎通確認
            // Debug.Log($"Predict Success: {outputBuffer[0]}, {outputBuffer[1]}"); 
        }
    }

    void OnDestroy()
    {
        ReleaseModel();
    }
}
