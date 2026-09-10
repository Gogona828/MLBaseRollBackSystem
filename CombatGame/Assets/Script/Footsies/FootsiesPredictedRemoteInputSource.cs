using UnityEngine;

namespace Footsies
{
    public class FootsiesPredictedRemoteInputSource : MonoBehaviour, IFootsiesPlayerInputSource
    {
        public enum RemotePredictionMode
        {
            NeutralWhenUnknown = 0,
            DirectionOnlyShortHold = 1,
            LastConfirmedUnsafe = 2,
            FepSupervised = 3
        }

        [Header("References")]
        [SerializeField] private NetworkInputReceiver networkInputReceiver;
        [SerializeField] private NetworkFrameClock frameClock;
        [SerializeField] private PredictionMismatchDetector predictionMismatchDetector;

        [Header("Remote Player")]
        [SerializeField] private int remotePlayerId = 0;

        [Header("Prediction")]
        [SerializeField] private RemotePredictionMode predictionMode = RemotePredictionMode.DirectionOnlyShortHold;

        [Tooltip("DirectionOnlyShortHold のときだけ使う。リレー遅延中に最後の方向入力を何フレーム保持するか。")]
        [SerializeField] private int directionalHoldFrames = 30;

        private FepSupervisedPredictor fep;
        private BattleCore core;
        private FootsiesBattleInputHistory inputHistory;
        private float hits, guards, breaks;
        public int FepPredictionCount { get; private set; }
        public byte LatestFepPrediction { get; private set; }
        private int preparedFrame = -1;
        private int lastCapture = -1;
        private int lastBattleFrame = -1;
        public void ConfigureReferences(NetworkInputReceiver receiver, NetworkFrameClock clock, PredictionMismatchDetector mismatch)
        { networkInputReceiver=receiver; frameClock=clock; predictionMismatchDetector=mismatch; }
        public void EnableFep()
        {
            core = FindObjectOfType<BattleCore>(true);
            inputHistory = FindObjectOfType<FootsiesBattleInputHistory>(true);
            fep = new FepSupervisedPredictor();
            predictionMode = RemotePredictionMode.FepSupervised;
            if (core != null) core.damageHandler += OnDamage;
            Debug.Log("[FEP] Loaded supervised XGBoost + FEP (50/100/200ms), managed CPU inference.");
        }
        private void OnDamage(Fighter fighter, Vector2 position, DamageResult result)
        {
            if (core.IsResimulating) return;
            if(result == DamageResult.Guard) guards++;
            else if(result == DamageResult.GuardBreak) breaks++;
            else hits++;
        }
        private void OnDestroy() { if(core != null) core.damageHandler -= OnDamage; }

        private byte lastConfirmedBits = 0;
        private int lastConfirmedFrame = -1;

        private byte lastPredictedBits = 0;
        private int lastPredictionFrame = -1;

        public bool LastReadWasConfirmed { get; private set; }

        public int RemotePlayerId => remotePlayerId;
        public RemotePredictionMode PredictionMode => predictionMode;
        public int DirectionalHoldFrames => directionalHoldFrames;

        /// <summary>
        /// 旧 API 互換用。
        /// true なら昔の LastConfirmedUnsafe、false なら NeutralWhenUnknown に寄せる。
        /// ただし今後は ConfigureRemotePlayer(remotePlayerId, mode, holdFrames) を使うこと。
        /// </summary>
        public void ConfigureRemotePlayer(int remotePlayerId, bool useLastConfirmedAsPrediction)
        {
            RemotePredictionMode mode = useLastConfirmedAsPrediction
                ? RemotePredictionMode.LastConfirmedUnsafe
                : RemotePredictionMode.NeutralWhenUnknown;

            ConfigureRemotePlayer(remotePlayerId, mode, directionalHoldFrames);
        }

        /// <summary>
        /// 推奨 API。prediction mode を明示的に指定する。
        /// </summary>
        public void ConfigureRemotePlayer(
            int remotePlayerId,
            RemotePredictionMode predictionMode,
            int directionalHoldFrames)
        {
            this.remotePlayerId = remotePlayerId;
            this.predictionMode = predictionMode;
            this.directionalHoldFrames = Mathf.Max(0, directionalHoldFrames);

            lastConfirmedBits = 0;
            lastConfirmedFrame = -1;
            lastPredictedBits = 0;
            lastPredictionFrame = -1;

            FileLogger.WriteLine(
                $"[FootsiesPredictedRemoteInputSource] ConfigureRemotePlayer remotePlayerId={remotePlayerId}, predictionMode={this.predictionMode}, directionalHoldFrames={this.directionalHoldFrames}");
        }

        public void ResetForNewRound()
        {
            fep?.Reset();
            preparedFrame=lastCapture=lastBattleFrame=lastConfirmedFrame=lastPredictionFrame=-1;
            lastConfirmedBits=lastPredictedBits=LatestFepPrediction=0;
            hits=guards=breaks=0;
        }

        public void PreparePredictionForFrame(int frame)
        {
            if(frame == preparedFrame) return;
            if(fep != null && core != null && core.fighter1 != null && core.fighter2 != null && !core.IsResimulating)
            {
                if(frame < lastCapture || core.CurrentFrameCount < lastBattleFrame) fep.Reset();
                lastBattleFrame = core.CurrentFrameCount;
                if(frame != lastCapture)
                {
                    fep.Capture(frame, remotePlayerId == 0 ? core.fighter1 : core.fighter2,
                        remotePlayerId == 0 ? core.fighter2 : core.fighter1, core, hits, guards, breaks);
                    hits=guards=breaks=0; lastCapture=frame;
                }
                fep.ObserveConfirmed(frame, networkInputReceiver);
            }
            if(fep == null || core == null || core.fighter1 == null || core.fighter2 == null) return;
            var fighter = remotePlayerId == 0 ? core.fighter1 : core.fighter2;
            SyncLatestReceivedRemoteInput(frame);
            LatestFepPrediction = fep.Predict(Mathf.Max(1, frame-fep.ObservedFrame), fighter.isFaceRight, lastConfirmedBits);
            FepPredictionCount++;
            preparedFrame=frame;
            if(!networkInputReceiver.TryGetRemoteInput(frame,out _))
            {
                inputHistory?.StoreAppliedPrediction(remotePlayerId,frame,LatestFepPrediction);
                RecordPredictionOnce(frame,LatestFepPrediction);
            }
        }

        public FootsiesInputFrame GetCurrentInput()
        {
            if (networkInputReceiver == null || frameClock == null)
            {
                return FootsiesInputFrame.Empty();
            }

            int frame = frameClock.CurrentFrame;
            return GetInputForFrame(frame);
        }

        public FootsiesInputFrame GetInputForFrame(int frame)
        {
            if (networkInputReceiver == null)
            {
                return FootsiesInputFrame.Empty();
            }

            PreparePredictionForFrame(frame);
            SyncLatestReceivedRemoteInput(frame);

            if (networkInputReceiver.TryGetRemoteInput(frame, out byte confirmedBits))
            {
                LastReadWasConfirmed = true;
                RememberConfirmedInput(frame, confirmedBits);
                return FootsiesInputFrame.FromBits(confirmedBits);
            }

            byte predictedBits = BuildPredictedBits(frame);
            LastReadWasConfirmed = false;
            lastPredictedBits = predictedBits;
            inputHistory?.StoreAppliedPrediction(remotePlayerId,frame,predictedBits);

            RecordPredictionOnce(frame, predictedBits);

            return FootsiesInputFrame.FromBits(predictedBits);
        }

        public FootsiesInputFrame GetInput()
        {
            int frame = frameClock != null ? frameClock.CurrentFrame : 0;
            return GetInputForFrame(frame);
        }

        private void SyncLatestReceivedRemoteInput(int upToFrame)
        {
            if (networkInputReceiver == null)
            {
                return;
            }

            if (!networkInputReceiver.TryGetLatestReceivedRemoteInput(out int latestFrame, out byte latestBits))
            {
                return;
            }

            if(latestFrame > upToFrame)
            {
                latestFrame=upToFrame;
                while(latestFrame >= 0 && !networkInputReceiver.Buffer.TryGetInput(latestFrame,out latestBits)) latestFrame--;
            }
            if (latestFrame > lastConfirmedFrame)
            {
                RememberConfirmedInput(latestFrame, latestBits);
            }
        }

        private void RememberConfirmedInput(int frame, byte bits)
        {
            lastConfirmedBits = bits;
            lastConfirmedFrame = frame;
            lastPredictedBits = bits;

            FileLogger.WriteLine(
                $"[FootsiesPredictedRemoteInputSource] remember confirmed frame={frame}, bits={bits}");
        }

        private void RecordPredictionOnce(int frame, byte bits)
        {
            if (predictionMismatchDetector != null && lastPredictionFrame != frame)
            {
                predictionMismatchDetector.RecordPrediction(frame, bits);
                lastPredictionFrame = frame;
            }
        }

        private byte BuildPredictedBits(int frame)
        {
            switch (predictionMode)
            {
                case RemotePredictionMode.FepSupervised:
                    return LatestFepPrediction;
                case RemotePredictionMode.NeutralWhenUnknown:
                    return 0;

                case RemotePredictionMode.DirectionOnlyShortHold:
                    if (lastConfirmedFrame < 0)
                    {
                        return 0;
                    }

                    if ((frame - lastConfirmedFrame) > directionalHoldFrames)
                    {
                        return 0;
                    }

                    // 攻撃は絶対に予測しない。左右入力だけ保持する。
                    return ExtractDirectionalBitsOnly(lastConfirmedBits);

                case RemotePredictionMode.LastConfirmedUnsafe:
                    return networkInputReceiver.GetLastConfirmedBitsForPlayer(remotePlayerId);

                default:
                    return 0;
            }
        }

        private byte ExtractDirectionalBitsOnly(byte bits)
        {
            int directionalMask = (int)InputDefine.Left | (int)InputDefine.Right;
            return (byte)(bits & directionalMask);
        }
    }
}
