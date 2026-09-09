# 能動的推論モデルの実MatchLogs適応

## 参照した構成

添付された `FEP_AI_CURRENT_STATE.md` と `fep-ai_snapshot_reconstructed_20260828.zip` をモデル仕様の参照資料として使用した。スナップショットには次の構成が含まれていた。

- 潜在状態: aggressive / defensive / approaching
- 観測: attack / guard / approach / retreat / wait
- 逐次ベイズ信念更新
- 状態遷移行列、観測尤度、初期信念、習慣事前分布
- 期待効用、情報利得、期待自由エネルギー（EFE）
- 習慣事前分布とEFEからのSoftmax行動確率

添付資料自身に「作業記録からの再構成でありサーバー原本と完全一致する保証はない」と記載されているため、本実験も再構成モデルを基準とする。

## 実データへの適応

観測クラスは既存XGBoost実験と同じ順序に統一した。

```text
Wait, Approach, Retreat, Attack, Guard
```

人工データ版の `probe` は、相手の反応を待つ情報収集行動という意味からWaitへ対応付けた。将来ラベルがDAMAGE、GUARD_BREAK、DEAD、WINの行はXGBoost実験と同様に除外した。

観測尤度 `A = P(observation | hidden_state)`、状態遷移 `B = P(state_t | state_t-1)`、初期信念 `D` は、trainに割り当てた38セッションだけを用いてBaum-Welch法で推定した。人工モデルの行列を意味の固定された初期値兼弱い事前分布として使用し、潜在状態のラベル交換を抑制した。習慣事前分布 `E` はtrain内の5行動頻度から推定した。

期待効用とcue尤度は、添付スナップショットの値をクラス順に並べ替えて固定した。したがって、validation/testの正解ラベルから効用を逆算していない。

## 推論

各フレームでは現在までのAction観測だけを使い、次式で信念を更新する。

```text
prior_t     = B @ belief_(t-1)
belief_t    ∝ A[observation_t] * prior_t
```

強制・終了状態は5観測に含まれないため、そのフレームでは状態遷移だけを適用する。未来ホライズン `H` の信念は次式で得る。

```text
belief_(t+H) = B^H @ belief_t
```

各行動について期待効用と情報利得を計算する。添付モデルと同様に、Wait（元のprobe）にはcue不確実性に基づく情報価値を与え、それ以外には小さな状態不確実性項を与える。

```text
G(action) = -expected_utility(action) - information_gain_weight * information_gain(action)
P(action) = softmax(habit_scale * log(habit_prior) - G(action))
```

utility、information gain、habitの尺度は小さな候補集合からvalidationのLog Lossが最小になる値をホライズンごとに選んだ。testは最終評価にだけ使用した。

## 学習後の潜在状態

学習済み観測尤度では、おおむね次の解釈が得られた。

- aggressive: Attackが約99.9%
- defensive: Retreatが約57.2%、Guardが約42.8%
- approaching: Waitが約69.7%、Approachが約30.3%

1フレーム遷移の自己遷移確率はaggressive約98.5%、defensive約96.9%、approaching約94.2%だった。

## 比較上の制約

これは対戦環境へ介入して方策を実行する制御実験ではなく、能動的推論の生成モデルとEFEを相手行動の確率予測へ用いるオフライン実験である。合法行動マスク、行動依存状態遷移、ゲーム結果に基づく選好学習はまだ含まれない。

特に長期ホライズンでは信念が定常分布へ近づき、Attackへ予測が偏った。3秒先のAccuracyだけを見るとXGBoostを上回るが、Macro F1が低いため、多クラス予測の改善とは解釈しない。
