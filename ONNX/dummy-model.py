import torch
import torch.nn as nn

# FOOTSIESのステート（距離、入力など）を想定したダミーネットワーク
class DummyPredictor(nn.Module):
    def __init__(self, input_size=10, output_size=2):
        super(DummyPredictor, self).__init__()
        self.linear = nn.Linear(input_size, output_size)

    def forward(self, x):
        return self.linear(x)

# モデルの初期化とエクスポート
model = DummyPredictor()
model.eval()

# ダミー入力（バッチサイズ1, 特徴量10）
dummy_input = torch.randn(1, 10)

# ONNX形式でエクスポート
torch.onnx.export(
    model, 
    dummy_input, 
    "dummy_model.onnx", 
    export_params=True,
    opset_version=15, 
    do_constant_folding=True,
    input_names=['input'], 
    output_names=['output'],
    dynamic_axes={'input': {0: 'batch_size'}, 'output': {0: 'batch_size'}}
)
print("dummy_model.onnx exported successfully.")
