using UnityEngine;

namespace Footsies
{
    public class FootsiesPredictedRemoteInputSource : MonoBehaviour, IFootsiesPlayerInputSource
    {
        [Header("References")]
        [SerializeField] private NetworkInputReceiver networkInputReceiver;
        [SerializeField] private NetworkFrameClock frameClock;
        [SerializeField] private PredictionMismatchDetector predictionMismatchDetector;

        [Header("Remote Player")]
        [SerializeField] private int remotePlayerId = 0;

        [Header("Prediction")]
        [SerializeField] private bool useLastConfirmedAsPrediction = true;

        private byte lastPredictedBits = 0;
        private int lastPredictionFrame = -1;

        public FootsiesInputFrame GetCurrentInput()
        {
            if (networkInputReceiver == null || frameClock == null)
            {
                return FootsiesInputFrame.Empty();
            }

            int frame = frameClock.CurrentFrame;

            if (networkInputReceiver.TryGetRemoteInput(frame, out byte confirmedBits))
            {
                lastPredictedBits = confirmedBits;
                return FootsiesInputFrame.FromBits(confirmedBits);
            }

            byte predictedBits = 0;

            if (useLastConfirmedAsPrediction)
            {
                predictedBits = networkInputReceiver.GetLastConfirmedBitsForPlayer(remotePlayerId);
            }
            else
            {
                predictedBits = lastPredictedBits;
            }

            lastPredictedBits = predictedBits;

            if (predictionMismatchDetector != null && lastPredictionFrame != frame)
            {
                predictionMismatchDetector.RecordPrediction(frame, predictedBits);
                lastPredictionFrame = frame;
            }

            return FootsiesInputFrame.FromBits(predictedBits);
        }
    }
}
