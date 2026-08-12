using UnityEngine;

namespace Footsies
{
    public class FootsiesBattleInputRouter : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour player1InputSourceBehaviour;
        [SerializeField] private MonoBehaviour player2InputSourceBehaviour;

        private IFootsiesPlayerInputSource player1InputSource;
        private IFootsiesPlayerInputSource player2InputSource;

        private bool useOverrideInputs = false;
        private FootsiesInputFrame overrideP1Input = FootsiesInputFrame.Empty();
        private FootsiesInputFrame overrideP2Input = FootsiesInputFrame.Empty();

        private void Awake()
        {
            CacheSources();
        }

        public void ConfigureSources(MonoBehaviour player1SourceBehaviour, MonoBehaviour player2SourceBehaviour)
        {
            player1InputSourceBehaviour = player1SourceBehaviour;
            player2InputSourceBehaviour = player2SourceBehaviour;
            CacheSources();

            FileLogger.WriteLine(
                $"[FootsiesBattleInputRouter] ConfigureSources p1={GetSourceName(player1InputSourceBehaviour)}, p2={GetSourceName(player2InputSourceBehaviour)}");
        }

        private void CacheSources()
        {
            player1InputSource = player1InputSourceBehaviour as IFootsiesPlayerInputSource;
            player2InputSource = player2InputSourceBehaviour as IFootsiesPlayerInputSource;

            if (player1InputSourceBehaviour != null && player1InputSource == null)
            {
                Debug.LogError("[FootsiesBattleInputRouter] player1InputSourceBehaviour does not implement IFootsiesPlayerInputSource.");
            }

            if (player2InputSourceBehaviour != null && player2InputSource == null)
            {
                Debug.LogError("[FootsiesBattleInputRouter] player2InputSourceBehaviour does not implement IFootsiesPlayerInputSource.");
            }
        }

        public FootsiesInputFrame GetPlayer1Input()
        {
            if (useOverrideInputs)
            {
                return overrideP1Input;
            }

            if (player1InputSource == null)
            {
                return FootsiesInputFrame.Empty();
            }

            return player1InputSource.GetCurrentInput();
        }

        public FootsiesInputFrame GetPlayer2Input()
        {
            if (useOverrideInputs)
            {
                return overrideP2Input;
            }

            if (player2InputSource == null)
            {
                return FootsiesInputFrame.Empty();
            }

            return player2InputSource.GetCurrentInput();
        }

        public void SetOverrideInputs(FootsiesInputFrame p1Input, FootsiesInputFrame p2Input)
        {
            useOverrideInputs = true;
            overrideP1Input = p1Input;
            overrideP2Input = p2Input;
        }

        public void ClearOverrideInputs()
        {
            useOverrideInputs = false;
        }

        public string GetInputSource(int playerNumber)
        {
            if (useOverrideInputs)
            {
                return "resimulation_override";
            }

            MonoBehaviour behaviour = playerNumber == 1
                ? player1InputSourceBehaviour
                : player2InputSourceBehaviour;

            FootsiesNetworkPlayerInputSource networkSource =
                behaviour as FootsiesNetworkPlayerInputSource;
            if (networkSource != null)
            {
                if (networkSource.CurrentReadMode == FootsiesNetworkPlayerInputSource.ReadMode.RemoteConfirmed)
                {
                    return "remote_confirmed";
                }

                return networkSource.UsesDebugAutoInput ? "debug_auto_input" : "human";
            }

            FootsiesPredictedRemoteInputSource predictedSource =
                behaviour as FootsiesPredictedRemoteInputSource;
            if (predictedSource != null)
            {
                return predictedSource.LastReadWasConfirmed
                    ? "remote_confirmed"
                    : "remote_predicted";
            }

            if (behaviour is FootsiesLocalPlayerInputSource)
            {
                return "human";
            }

            return behaviour != null ? behaviour.GetType().Name : "unknown";
        }

        public int GetPredictedRemotePlayerId()
        {
            FootsiesPredictedRemoteInputSource p1 =
                player1InputSourceBehaviour as FootsiesPredictedRemoteInputSource;
            if (p1 != null)
            {
                return p1.RemotePlayerId;
            }

            FootsiesPredictedRemoteInputSource p2 =
                player2InputSourceBehaviour as FootsiesPredictedRemoteInputSource;
            return p2 != null ? p2.RemotePlayerId : -1;
        }

        public string GetPredictionMode()
        {
            FootsiesPredictedRemoteInputSource p1 =
                player1InputSourceBehaviour as FootsiesPredictedRemoteInputSource;
            if (p1 != null)
            {
                return p1.PredictionMode.ToString();
            }

            FootsiesPredictedRemoteInputSource p2 =
                player2InputSourceBehaviour as FootsiesPredictedRemoteInputSource;
            return p2 != null ? p2.PredictionMode.ToString() : string.Empty;
        }

        private string GetSourceName(MonoBehaviour behaviour)
        {
            return behaviour != null ? behaviour.name : "null";
        }
    }
}
