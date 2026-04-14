using UnityEngine;

namespace Footsies
{
    public class FootsiesPredictedRemoteInputSource : MonoBehaviour, IFootsiesPlayerInputSource
    {
        public enum RemotePredictionMode
        {
            NeutralWhenUnknown = 0,
            DirectionOnlyShortHold = 1,
            LastConfirmedUnsafe = 2
        }

        [Header("References")]
        [SerializeField] private NetworkInputReceiver networkInputReceiver;
        [SerializeField] private NetworkFrameClock frameClock;
        [SerializeField] private PredictionMismatchDetector predictionMismatchDetector;

        [Header("Remote Player")]
        [SerializeField] private int remotePlayerId = 0;

        [Header("Prediction")]
        [SerializeField] private RemotePredictionMode predictionMode = RemotePredictionMode.NeutralWhenUnknown;

        [Tooltip("DirectionOnlyShortHold のときだけ使う。最後の方向入力を何フレーム保持するか。まずは 1 を推奨。")]
        [SerializeField] private int directionalHoldFrames = 1;

        private byte lastConfirmedBits = 0;
        private int lastConfirmedFrame = -1;

        private byte lastPredictedBits = 0;
        private int lastPredictionFrame = -1;

        public void ConfigureRemotePlayer(int remotePlayerId, bool useLastConfirmedAsPrediction)
        {
            this.remotePlayerId = remotePlayerId;

            // 旧 API 互換:
            // true なら昔の「最後の確定入力をそのまま予測」、
            // false なら安全側の NeutralWhenUnknown に寄せる。
            predictionMode = useLastConfirmedAsPrediction
                ? RemotePredictionMode.LastConfirmedUnsafe
                : RemotePredictionMode.NeutralWhenUnknown;

            lastConfirmedBits = 0;
            lastConfirmedFrame = -1;
            lastPredictedBits = 0;
            lastPredictionFrame = -1;

            FileLogger.WriteLine(
                $"[FootsiesPredictedRemoteInputSource] ConfigureRemotePlayer remotePlayerId={remotePlayerId}, predictionMode={predictionMode}, directionalHoldFrames={directionalHoldFrames}");
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

            if (networkInputReceiver.TryGetRemoteInput(frame, out byte confirmedBits))
            {
                lastConfirmedBits = confirmedBits;
                lastConfirmedFrame = frame;
                lastPredictedBits = confirmedBits;

                if (predictionMismatchDetector != null && lastPredictionFrame != frame)
                {
                    predictionMismatchDetector.RecordPrediction(frame, confirmedBits);
                    lastPredictionFrame = frame;
                }

                return FootsiesInputFrame.FromBits(confirmedBits);
            }

            byte predictedBits = BuildPredictedBits(frame);
            lastPredictedBits = predictedBits;

            if (predictionMismatchDetector != null && lastPredictionFrame != frame)
            {
                predictionMismatchDetector.RecordPrediction(frame, predictedBits);
                lastPredictionFrame = frame;
            }

            return FootsiesInputFrame.FromBits(predictedBits);
        }

        public FootsiesInputFrame GetInput()
        {
            int frame = frameClock != null ? frameClock.CurrentFrame : 0;
            return GetInputForFrame(frame);
        }

        private byte BuildPredictedBits(int frame)
        {
            switch (predictionMode)
            {
                case RemotePredictionMode.NeutralWhenUnknown:
                    return 0;

                case RemotePredictionMode.DirectionOnlyShortHold:
                    if (lastConfirmedFrame < 0)
                    {
                        return 0;
                    }

                    if ((frame - lastConfirmedFrame) > Mathf.Max(0, directionalHoldFrames))
                    {
                        return 0;
                    }

                    // 攻撃は絶対に予測しない。
                    // 左右入力だけを短く保持する。
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
