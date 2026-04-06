#include <onnxruntime_cxx_api.h>
#include <vector>
#include <stdexcept>

// Unityから呼び出すためのC言語インターフェース
extern "C" {

    Ort::Env* env = nullptr;
    Ort::Session* session = nullptr;
    Ort::MemoryInfo* memory_info = nullptr;

    // モデルの初期化
    __declspec(dllexport) bool InitModel(const char* model_path) {
        try {
            if (!env) {
                // ログレベルの設定（必要に応じて変更）
                env = new Ort::Env(ORT_LOGGING_LEVEL_WARNING, "PredictionEnv");
            }

            Ort::SessionOptions session_options;
            session_options.SetIntraOpNumThreads(1); // 軽量化のため1スレッドに制限
            session_options.SetGraphOptimizationLevel(GraphOptimizationLevel::ORT_ENABLE_ALL);

            // wchar_tへの変換 (Windows環境用)
            size_t len = strlen(model_path) + 1;
            std::vector<wchar_t> w_model_path(len);
            size_t converted = 0;
            mbstowcs_s(&converted, w_model_path.data(), len, model_path, _TRUNCATE);

            session = new Ort::Session(*env, w_model_path.data(), session_options);
            memory_info = new Ort::MemoryInfo(Ort::MemoryInfo::CreateCpu(OrtArenaAllocator, OrtMemTypeDefault));
            return true;
        }
        catch (const std::exception& e) {
            // 実際はエラーログをファイル出力するなどの処理を追加
            return false;
        }
    }

    // 推論の実行（メモリ確保を避けるため、入力・出力バッファのポインタを直接受け取る）
    __declspec(dllexport) bool Predict(const float* input_data, int input_size, float* output_data, int output_size) {
        if (!session) return false;

        try {
            const char* input_names[] = { "input" };
            const char* output_names[] = { "output" };

            std::vector<int64_t> input_shape = { 1, input_size };

            // コピーなしでテンソルを作成
            Ort::Value input_tensor = Ort::Value::CreateTensor<float>(
                *memory_info,
                const_cast<float*>(input_data), input_size,
                input_shape.data(), input_shape.size()
            );

            // 推論実行
            auto output_tensors = session->Run(
                Ort::RunOptions{ nullptr },
                input_names, &input_tensor, 1,
                output_names, 1
            );

            // 結果をUnity側の出力バッファへコピー
            float* floatarr = output_tensors.front().GetTensorMutableData<float>();
            for (int i = 0; i < output_size; ++i) {
                output_data[i] = floatarr[i];
            }

            return true;
        }
        catch (const std::exception& e) {
            return false;
        }
    }

    // メモリの解放
    __declspec(dllexport) void ReleaseModel() {
        if (session) { delete session; session = nullptr; }
        if (memory_info) { delete memory_info; memory_info = nullptr; }
        if (env) { delete env; env = nullptr; }
    }
}
