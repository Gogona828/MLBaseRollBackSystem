# 対戦格闘ゲーム次行動予測モデル

`MatchLogger` が生成する `frames.csv` / `events.csv` / `matches.csv` から、設計書で定義された5行動（Wait / Approach / Retreat / Attack / Guard）の確率を予測し、XGBoostと能動的推論を同一条件で比較します。

## 実装内容

- 50 ms、100 ms、200 ms、300 ms、500 ms、1秒、2秒、3秒先の8モデル
- aggressive / defensive / approaching の3潜在状態を持つ離散能動的推論モデル
- 学習セッションのみを使った観測尤度・状態遷移・習慣事前分布の推定
- EFE（期待効用・情報利得）による5行動確率と、validationによる尺度選択
- 現在状態と過去3/6/12/30フレームからの履歴特徴量
- 未来Actionが強制状態（DAMAGE / GUARD_BREAK）または終了状態（DEAD / WIN）の行を教師ラベルから除外
- `session_id` 単位の70% / 15% / 15%分割
- 最頻クラス、Persistence、Markovの3ベースライン
- Accuracy、Top-2 Accuracy、Macro F1、Log Loss、混同行列
- 予測時点から正解時点までに行動クラスが変化したケース／変化しなかったケース別の正答率と件数
- 50 msモデルを毎フレーム実行し、未着中は当該フレーム向けの過去予測を現在行動として再利用する連鎖評価
- 未来入力を含む `p1_buffer_action_id` / `p2_buffer_action_id` は特徴量から明示的に除外
- オフラインログと、canonicalフレームを持つオンラインログの両方に対応

## セットアップ

```bash
cd ML
python3 -m venv .venv
.venv/bin/python -m pip install -e .
```

## 学習

展開済みの `MatchLogs` ディレクトリまたはZIPを直接指定できます。

```bash
.venv/bin/train-action-models \
  --logs ../CombatGame/MatchLogs.zip \
  --output artifacts
```

主な成果物は次のとおりです。

- `artifacts/models/model_50ms.json` ～ `model_3s.json`: XGBoost標準モデル形式
- `artifacts/models/model_active_inference.npz`: 実データ適応済み能動的推論モデル
- `artifacts/metadata.json`: 特徴量順、クラス順、分割数、学習情報
- `artifacts/reports/metrics_summary.csv`: 全モデルとベースラインの評価
- `artifacts/reports/experiment_report.md`: 結果の要約と考察
- `ACTIVE_INFERENCE_ADAPTATION.md`: 添付FEPモデルから実データ版への適応方法
- `artifacts/reports/confusion_*.csv`: 混同行列
- `artifacts/reports/performance_by_horizon.png`: 予測時間と性能の関係

`metrics_summary.csv` と `metrics.json` には、通常の全体指標に加えて
`changed_action_samples` / `changed_action_accuracy` と
`unchanged_action_samples` / `unchanged_action_accuracy` が出力されます。変化の有無は、予測時点の5クラス行動と各ホライズン先の正解クラスが異なるかどうかで判定します。

## バッチ推論

```bash
.venv/bin/predict-actions \
  --model-dir artifacts \
  --logs ../CombatGame/MatchLogs.zip \
  --horizon 200ms \
  --output artifacts/predictions_200ms.csv
```

能動的推論モデルを指定する場合：

```bash
.venv/bin/predict-actions \
  --model-dir artifacts \
  --logs ../CombatGame/MatchLogs.zip \
  --horizon 200ms \
  --model-type active-inference \
  --output artifacts/fep_predictions_200ms.csv
```

出力には5クラスすべての確率、最大確率のクラス番号、行動名が含まれます。リアルタイムXGBoost実装では `metadata.json` の `feature_names` と同じ順序で現在・履歴特徴量を構築してください。能動的推論ではAction観測を逐次入力し、信念状態を更新します。

## 能動的推論モデルの位置付け

添付された `fep-ai` 再構成スナップショットのモデル構造とEFE式を基準にしています。人工データ版の `probe` は比較対象の5クラスへ合わせてWaitへ対応付けました。添付文書自身が示すとおりスナップショットはサーバー原本の完全なバックアップではないため、本成果物は「再構成モデルの実MatchLogs適応版」です。

数式、学習対象、固定したパラメータ、比較上の制約は [ACTIVE_INFERENCE_ADAPTATION.md](ACTIVE_INFERENCE_ADAPTATION.md) に記載しています。

## 1フレーム逐次予測による自己回帰実験

指定された実験形式では、開始フレームの実状態から1フレーム先を予測し、その予測行動を次の現在行動として再投入します。50 msなら3回、100 msなら6回というように1フレーム予測を繰り返し、50 / 100 / 200 / 500 / 1000 / 2000 / 3000 msの終端と途中過程を評価します。

```bash
.venv/bin/evaluate-autoregressive-horizons \
  --model-dir artifacts \
  --logs ../CombatGame/MatchLogs.zip \
  --output artifacts/autoregressive_horizons
```

出力は次のとおりです。

- `autoregressive_summary.csv`: 終端、途中、全過程、全経路一致率と行動変化別Accuracy
- `autoregressive_per_step.csv`: 各ホライズンの1フレーム目から終端までのstep別一致率
- `autoregressive_endpoints.csv`: 全開始フレームの終端予測とrollout内一致率
- `autoregressive_report.md`: 実験条件、結果表、制約の日本語レポート
- `models/model_1frame.json`: rollout専用の1フレーム先XGBoostモデル


## FEPハイブリッド一括比較

純粋なFEP生成予測、既存XGBoost再現、FEP信念特徴追加、二段式（特徴追加あり/なし）、
CatBoost、セッションOOFスタッキング、継続時間HSMM、状況依存Bを8ホライズンで比較します。
既存の `artifacts/split_assignment.json` を固定して使います。

```bash
.venv/bin/python -m pip install -r requirements.txt
.venv/bin/python -m action_prediction.hybrid_experiments \
  --logs ../CombatGame/MatchLogs.zip \
  --baseline artifacts \
  --output artifacts/hybrid_experiments
```

結果は `artifacts/hybrid_experiments/experiment_report.md`。
同じディレクトリへvalidation/test指標、test確率、OOF予測、モデル、設定を保存します。
HSMMは観測状態と継続年齢を用いる特殊ケース、状況依存Bは文脈別の近似推定です。
詳細条件と限界はレポートに記載しています。

## テスト実行

```bash
PYTHONPATH=. .venv/bin/python -m unittest discover -s tests -v
```

## 次イベント予測の初期実装

```bash
.venv/bin/python -m action_prediction.sequence --logs ../CombatGame/MatchLogs.zip --output artifacts/action_sequence
```

`event_model.joblib` に変化時刻ハザード・直後の変化先モデルを保存します。学習はセッション分離、末尾・欠落・強制行動で観測打切り。validationで変化重み（1/3）と閾値（0.25/0.5/0.75）を比較し、誤警報率10%以下で変化時正答率を優先します。該当候補がなければ誤警報率最小を選びます。

`report.json` の `next_frame_prediction_time.mean_ms` はウォームアップ後に最大500件を1件ずつ推論した平均時間（ミリ秒）です。特徴量生成・モデル読込・ゲーム再シミュレーションを含みません。p50/p95・計測件数も記録します。testとセッション別の指標は次の1フレームに対する評価です。

これは行動クラスのオフライン初期実装です。状態更新付き50/100/200ms入力列・FEP・複数経路は未実装です。厳密な再現に不足するログ情報は `artifacts/action_sequence/REPLAY_READINESS.md` を参照してください。

## 200ラウンドのオフライン行動列比較（ゲーム実行なし）

```bash
.venv/bin/python -m action_prediction.sequence_experiments \
  --logs ../CombatGame/MatchLogs.zip \
  --split artifacts/split_assignment.json \
  --output artifacts/sequence_offline
```

50/100/200msについて、継続予測・各オフセットの直接XGBoost・次イベントモデル（FEP信念あり/なし、beam幅1/3）を既存のセッション分割で比較します。重み・閾値はvalidationで選びます。`experiment_report.md` に比較表、`report.json` に全validation候補・5/10/20%誤警報許容水準・失敗内訳・セッション別指標・実測時間、`predictions_*.npz` に同一評価区間の正解/採用予測/候補を保存します。

`timing.*_raw_next_frame.mean_ms` は500件の単件推論平均、`timing.*_beam*_next_frame` は候補管理を含む1フレーム予測時間です。各ホライズンの `rollout_time` は区間全体の単件処理時間で、そのフレーム数割り値とは区別します。

予測は開始時までの情報だけを使用します。切替時刻kを進め、切替後は予測行動・履歴を更新します。位置・HP・相手状態とFEP信念は開始時の値に固定するオフライン比較です。未来ログの状態を予測へ渡すことや、未実行ゲームのHP誤差を報告することはありません。

最新の再実行結果は [sequence_offline_rerun/experiment_report.md](artifacts/sequence_offline_rerun/experiment_report.md) に保存しています。`verification.json` に正常終了と、保存予測からの指標再計算・前回予測との一致確認を記録しています。
