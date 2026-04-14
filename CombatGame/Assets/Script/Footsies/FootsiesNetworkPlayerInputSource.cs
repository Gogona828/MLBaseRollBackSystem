using UnityEngine;

namespace Footsies
{
    public class FootsiesNetworkPlayerInputSource : MonoBehaviour, IFootsiesPlayerInputSource
    {
        public enum ReadMode
        {
            LocalSender,
            RemoteConfirmed
        }

        [Header("Mode")]
        [SerializeField] private ReadMode readMode = ReadMode.LocalSender;

        [Header("References")]
        [SerializeField] private NetworkInputSender networkInputSender;
        [SerializeField] private NetworkInputReceiver networkInputReceiver;

        [Header("Remote Player")]
        [SerializeField] private int remotePlayerId = 0;

        public FootsiesInputFrame GetCurrentInput()
        {
            switch (readMode)
            {
                case ReadMode.LocalSender:
                    if (networkInputSender == null)
                    {
                        return FootsiesInputFrame.Empty();
                    }

                    return FootsiesInputFrame.FromBits(networkInputSender.LastLocalInputBits);

                case ReadMode.RemoteConfirmed:
                    if (networkInputReceiver == null)
                    {
                        return FootsiesInputFrame.Empty();
                    }

                    return FootsiesInputFrame.FromBits(
                        networkInputReceiver.GetLastConfirmedBitsForPlayer(remotePlayerId)
                    );

                default:
                    return FootsiesInputFrame.Empty();
            }
        }
    }
}
