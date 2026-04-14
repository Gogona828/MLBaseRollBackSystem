using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace Footsies
{
    public class RoundResultAgreementController : MonoBehaviour
    {
        [Serializable]
        private class RoundResultEnvelope
        {
            public RoundResultSignature signature;
        }

        [Header("References")]
        [SerializeField] private BattleCore battleCore;
        [SerializeField] private FootsiesBattleRollbackCoordinator rollbackCoordinator;
        [SerializeField] private NetworkFrameClock frameClock;

        [Header("Agreement")]
        [SerializeField] private float resendIntervalSeconds = 0.25f;
        [SerializeField] private int rollbackSafetyFrames = 8;
        [SerializeField] private bool verboseLog = true;

        private int localPlayerId;
        private string remoteIp;

        private int localListenPort;
        private int remoteSendPort;

        private bool runtimeConfigured;
        private bool socketInitialized;

        private UdpClient udp;
        private IPEndPoint receiveEndPoint;

        private bool waitingAgreement;
        private float lastSendTime;

        private RoundResultSignature localSignature;
        private bool localSignatureValid;

        private RoundResultSignature remoteSignature;
        private bool remoteSignatureValid;

        private bool wasInRoundFenceState;

        private void Awake()
        {
            if (battleCore == null)
            {
                battleCore = FindObjectOfType<BattleCore>();
            }

            if (rollbackCoordinator == null)
            {
                rollbackCoordinator = FindObjectOfType<FootsiesBattleRollbackCoordinator>();
            }

            if (frameClock == null)
            {
                frameClock = FindObjectOfType<NetworkFrameClock>();
            }
        }

        private void Start()
        {
            TryInitializeSocket();
        }

        private void OnDestroy()
        {
            CloseSocket();
        }

        /// <summary>
        /// BattleSceneRuntimeConfigurator から呼ばれる。
        /// PCごとの差分はここへ集約する。
        /// </summary>
        public void ConfigureRuntime(int localPlayerId, string remoteIp)
        {
            this.localPlayerId = localPlayerId;
            this.remoteIp = remoteIp;
            runtimeConfigured = true;

            // playerId だけで自動決定
            if (localPlayerId == 0)
            {
                localListenPort = 5200;
                remoteSendPort = 5201;
            }
            else
            {
                localListenPort = 5201;
                remoteSendPort = 5200;
            }

            if (verboseLog)
            {
                FileLogger.WriteLine(
                    $"[RoundResultAgreementController] ConfigureRuntime playerId={localPlayerId}, remoteIp={remoteIp}, listenPort={localListenPort}, sendPort={remoteSendPort}");
            }

            TryInitializeSocket();
        }

        private void TryInitializeSocket()
        {
            if (!runtimeConfigured || socketInitialized)
            {
                return;
            }

            try
            {
                udp = new UdpClient(localListenPort);
                udp.Client.Blocking = false;
                receiveEndPoint = new IPEndPoint(IPAddress.Any, 0);
                socketInitialized = true;

                if (verboseLog)
                {
                    FileLogger.WriteLine(
                        $"[RoundResultAgreementController] Socket initialized listenPort={localListenPort}, remoteIp={remoteIp}, remoteSendPort={remoteSendPort}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void CloseSocket()
        {
            socketInitialized = false;

            if (udp != null)
            {
                udp.Close();
                udp = null;
            }
        }

        private void Update()
        {
            PollIncomingMessages();

            if (battleCore == null)
            {
                return;
            }

            bool inFenceState = battleCore.roundState == BattleCore.RoundStateType.KO
                || battleCore.roundState == BattleCore.RoundStateType.End;

            if (!inFenceState)
            {
                if (wasInRoundFenceState)
                {
                    ClearAgreementState();
                    battleCore.ExternalRoundAdvanceBlocked = false;
                }

                wasInRoundFenceState = false;
                return;
            }

            wasInRoundFenceState = true;

            if (!waitingAgreement)
            {
                BeginAgreement();
            }

            battleCore.ExternalRoundAdvanceBlocked = true;

            if (Time.unscaledTime - lastSendTime >= resendIntervalSeconds)
            {
                SendLocalSignature();
            }

            if (localSignatureValid && remoteSignatureValid)
            {
                if (localSignature.EqualsForAgreement(remoteSignature))
                {
                    if (verboseLog)
                    {
                        FileLogger.WriteLine(
                            $"[RoundResultAgreementController] Agreement OK local={localSignature} remote={remoteSignature}");
                    }

                    battleCore.ExternalRoundAdvanceBlocked = false;
                    waitingAgreement = false;
                    return;
                }

                int rollbackTarget = Mathf.Max(
                    0,
                    Mathf.Min(localSignature.rollbackTargetFrame, remoteSignature.rollbackTargetFrame) - rollbackSafetyFrames);

                if (verboseLog)
                {
                    FileLogger.WriteLine(
                        $"[RoundResultAgreementController] Agreement NG local={localSignature} remote={remoteSignature} -> rollbackTarget={rollbackTarget}");
                }

                rollbackCoordinator?.RequestRollback(rollbackTarget);

                ClearAgreementState();
                battleCore.ExternalRoundAdvanceBlocked = false;
            }
        }

        private void BeginAgreement()
        {
            if (!runtimeConfigured)
            {
                return;
            }

            waitingAgreement = true;
            localSignature = BuildLocalSignature();
            localSignatureValid = true;
            remoteSignatureValid = false;

            SendLocalSignature();

            if (verboseLog)
            {
                FileLogger.WriteLine(
                    $"[RoundResultAgreementController] BeginAgreement local={localSignature}");
            }
        }

        private RoundResultSignature BuildLocalSignature()
        {
            int roundSerial = 0;
            if (battleCore != null)
            {
                roundSerial = (int)(battleCore.fighter1RoundWon + battleCore.fighter2RoundWon);
            }

            int koStateFrame = frameClock != null ? frameClock.CurrentFrame : 0;
            int rollbackTargetFrame = Mathf.Max(0, koStateFrame - rollbackSafetyFrames);

            return RoundResultSignature.Create(
                senderPlayerId: localPlayerId,
                roundSerial: roundSerial,
                koStateFrame: koStateFrame,
                rollbackTargetFrame: rollbackTargetFrame,
                fighter1: battleCore != null ? battleCore.fighter1 : null,
                fighter2: battleCore != null ? battleCore.fighter2 : null
            );
        }

        private void SendLocalSignature()
        {
            if (!socketInitialized || !localSignatureValid)
            {
                return;
            }

            RoundResultEnvelope envelope = new RoundResultEnvelope
            {
                signature = localSignature
            };

            string json = JsonUtility.ToJson(envelope);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            udp.Send(bytes, bytes.Length, remoteIp, remoteSendPort);
            lastSendTime = Time.unscaledTime;

            if (verboseLog)
            {
                FileLogger.WriteLine(
                    $"[RoundResultAgreementController] Sent {json}");
            }
        }

        private void PollIncomingMessages()
        {
            if (!socketInitialized)
            {
                return;
            }

            try
            {
                while (udp.Available > 0)
                {
                    byte[] bytes = udp.Receive(ref receiveEndPoint);
                    string json = Encoding.UTF8.GetString(bytes);

                    RoundResultEnvelope envelope = JsonUtility.FromJson<RoundResultEnvelope>(json);
                    remoteSignature = envelope.signature;
                    remoteSignatureValid = true;

                    if (verboseLog)
                    {
                        FileLogger.WriteLine(
                            $"[RoundResultAgreementController] Received {json}");
                    }
                }
            }
            catch (SocketException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void ClearAgreementState()
        {
            waitingAgreement = false;
            localSignatureValid = false;
            remoteSignatureValid = false;
            lastSendTime = 0f;
        }
    }
}
