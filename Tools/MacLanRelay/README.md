# Mac LAN relay + FEP supervised prediction

## 対戦開始

1. Macで `Tools/MacLanRelay/start.command` をダブルクリックするか、リポジトリのルートで以下を実行します。Python 3.10以上が必要です。このMacでは既存のPython 3を使用できます。Unityの相手側にはPython/.NET/ONNXのインストールは不要です。

   ```sh
   Tools/MacLanRelay/start.command --port 6000 --delay 100
   ```

2. 両方のPCで同じ変更済みCombatGameプロジェクトをUnity Editor（このプロジェクトは6000.3.17f1）で開き、`CombatGame > LAN Match Settings` を選びます。
3. `Mac server IPv4` にサーバーMacのLAN IPv4を設定します。2026-09-10のこのMacでは `192.168.128.132` でした。Wi-Fi変更やDHCP更新時は起動スクリプトの表示を確認してください。
4. 片方は `Server UDP port = 6000`（P1）、もう片方は `6001`（P2）。両方で `Save and enable LAN + FEP` を押します。ポートでプレイヤー番号を決めるため、PC名・相手のIP・ローカル待受ポートの登録は不要です。
5. 両方で `Assets/Scenes/BattleScene.unity` を開いてPlayします。タイトルから開始する場合はオンラインのVSを選びます。`VS CPU` はオフラインです。操作は両方とも **A=左、D=右、Space=攻撃** です。

Mac自身も同じLAN IPで参加できます。同じPCの複数Editorで試す場合は、それぞれのPlay開始前に別の接続ポートを保存してください。設定はそのPCのPlayerPrefsに保存されます。既存のPC名プロファイルへ戻す場合は設定画面の `Use existing scene profiles` を押します。

サーバーは1組・2人用です。別の2人組を追加するときは `--port 6010` のように別の**偶数**ポートで別プロセスを起動し、6010/6011を使います。同じ枠への重複接続は拒否します。対戦をやり直すときは両方のPlayを停止して15秒待つか、サーバーを再起動してください。

サーバー終了は起動したターミナルでCtrl+C。Macをスリープさせないでください。既に6000/6001で起動中の場合、二重起動はエラーになります。

## 接続できない場合

- 両者とサーバーを相互通信できる同じLANへ接続します。ゲストWi-FiやAPのクライアント分離が有効なネットワークでは通信できません。
- macOSでPythonの受信接続許可を求められたら許可します。WindowsではUnity Editorのプライベートネットワーク通信を許可します。サーバーへのUDP 6000/6001が必要です。ルーターのインターネット向けポート開放は不要です。
- サーバーログにP1/P2両方の `registered` が出ること、Unityログに `[FEP] Loaded supervised XGBoost + FEP` と `Running started` が出ることを確認します。
- 同じ接続ポートを両方で使用していないか確認します。相手PCで127.0.0.1を指定すると、その相手PC自身への接続になります。

## 推論と通信

- 使用モデルは既存の `ML/artifacts/hybrid_experiments/models/xgboost_fep_{50ms,100ms,200ms}.json` と `fep.npz` です。既存の学習済みモデルを使用し、再学習していません。
- `export_model.py` がFEPパラメータとearly stoppingまでの木を `CombatGame/Assets/Resources/FepSupervisedModel.json` に出力します。Unityは共通C#で推論するため、Windows専用PredictionPlugin.dllに依存しません。
- 現在までにローカルで観測したシミュレーション状態をフレーム別に保存し、同じフレームの確定済みリモート入力が到着してから特徴量・FEP信念を更新します。予測対象までの差に近い3/6/12フレームのモデルを選びます。モデルはネットワークシミュレーションの毎フレームに評価します。対象フレームの確定入力があればそれを使用し、未受信のときだけキャッシュした予測を適用します。最初の確定入力前は現在の観測と初期FEP信念を使用します。12フレームを超える場合も予測を続けますが、現状のモデル選択は200msモデルが上限です。入力が欠落したときは、保存中の履歴について未確認入力を飛ばして信念を更新しません。
- 学習ラベルは **Wait/Approach/Retreat/Attack/Guardという行動クラス** です。入力への変換はWait→無入力、Approach→前、Retreat/Guard→後ろ、Attack→攻撃（既存の方向/攻撃保持を維持）。特殊技や攻撃ボタンの押下・解放列そのものを予測するモデルではありません。
- オンラインの状態はロールバックを含むローカル観測で、学習ログの確定状態とは差があります。特徴量もフレーム開始時点の観測であり、フレーム終了時のログとの位相差があります。オフライン評価精度がそのままLAN対戦時の精度になるとは限りません。既存の不一致検出・ロールバックは維持しています。
- Hello/Ready/Inputとラウンド結果は同じUDPソケットを経由します。Startはサーバーから両者へ返し、重複Startによる再スタートを防ぎます。時計同期プロトコルではないため、OSスケジューリングやLAN伝送による開始時刻の差までは保証しません。
- `--delay` は片道ミリ秒、`--jitter` は±ミリ秒、`--loss` は入力パケットの破棄率です。通常対戦はloss=0。制御パケットは擬似損失対象外です。入力の再送機能は既存実装にないため、損失実験では確定入力が欠落し、古い観測からの予測が続く場合があります。

## ロールバックの許容位置差

BattleSceneの `FootsiesBattleRollbackCoordinator` のInspectorで、`State-based rollback > Allowed Position Error` を変更できます。既定値は **0.05ゲーム座標単位**。両者それぞれの2次元位置差に適用し、0なら完全一致を要求します。

入力の不一致を検出すると、確定入力を使って過去の状態から**現在フレームの開始地点まで**比較用の再計算を行います。両者のHP・ガードHP、行動ID・行動フレーム・ヒットストップ、向き・速度とラウンド状態が一致し、位置差が許容値以内なら画面上の位置を戻さず続行します。内部の入力履歴は訂正して、次の攻撃判定等に古い入力が残らないようにします。許容範囲外は従来のロールバックと再シミュレーションを実行します。

比較用の計算ではダメージ演出、ラウンド演出、試合ログ、録画配列への書き込みを抑止します。**比較用再シミュレーションのCPUコストは発生します**。相手からHP/位置を受信する方式ではなく、受信した入力からローカルに訂正状態を計算する方式です。ラウンド境界や比較不能な状態では省略せず、通常のロールバックへ進みます。

`Skip Rollback When State Matches` で省略判定を切り替えられます。ログの `[Rollback] Continue without rollback` と `StateMatchedContinuations` が省略回数、予測ソースの `FepPredictionCount` が推論更新回数です。

## 検証・モデル更新

リポジトリのルートで実行します。

```sh
python3 -m unittest discover -s Tools/MacLanRelay -p test_server.py -v
ML/.venv/bin/python Tools/MacLanRelay/export_model.py
ML/.venv/bin/python Tools/MacLanRelay/verify_model.py
dotnet run --project Tools/MacLanRelay/tests/RuntimeCheck.csproj -- CombatGame/Assets/Resources/FepSupervisedModel.json Tools/MacLanRelay/parity_cases.json
```

最後のC#単体検証のみ.NET 10 SDKを使用します。Unity依存APIを小さなスタブに置き換え、実際のC#木評価コードを元モデルのlogitと比較します。サーバー運用には不要です。モデル再出力にはML環境のNumPy/XGBoost等が必要です。

実施済み: 実UDPソケットによる双方向転送・遅延・開始通知・損失・枠衝突・ラウンド結果転送テスト、3ホライズン×128入力（NaN含む）の元モデルおよびC#木評価の一致、プロジェクトのUnity参照DLLを使ったC#コンパイル、状態比較の許容差・HP・行動・KO等10ケース。

未実施: 別の物理Windows/Macを使ったUnity Editor同士の対戦、試合全体の同期・予測精度測定。
