using System;
using UnityEngine;
using Footsies;

[DefaultExecutionOrder(-10000)]
public class BattleSceneRuntimeConfigurator : MonoBehaviour
{
    [Serializable]
    public class MachineProfile
    {
        [Header("Machine")]
        public string machineName = "PCA";
        public string machineLabel = "PCA";

        [Header("Role")]
        public int localPlayerId = 0;
        public int startDelayFrames = 60;

        [Header("Network")]
        public string remoteIp = "127.0.0.1";
        public int localPort = 5100;
        public int remotePort = 5101;

        [Header("Local Input")]
        public bool enableDebugAutoInput = false;
        public KeyCode leftKey = KeyCode.A;
        public KeyCode rightKey = KeyCode.D;
        public KeyCode attackKey = KeyCode.Space;

        [Header("Temporary Delay Simplification")]
        public bool useFixedDelayForTest = false;
        public int fixedDelayFramesForTest = 2;

        [Header("Remote Prediction")]
        public FootsiesPredictedRemoteInputSource.RemotePredictionMode remotePredictionMode
            = FootsiesPredictedRemoteInputSource.RemotePredictionMode.NeutralWhenUnknown;

        public int remoteDirectionalHoldFrames = 1;
    }

    [Header("Editor Override")]
    [SerializeField] private bool useOverrideMachineNameInEditor = false;
    [SerializeField] private string overrideMachineName = "PCA";

    [Header("Machine Profiles")]
    [SerializeField] private MachineProfile[] machineProfiles =
    {
        new MachineProfile
        {
            machineName = "MSI",
            machineLabel = "PCA",
            localPlayerId = 0,
            startDelayFrames = 60,
            remoteIp = "192.168.0.90",
            localPort = 5000,
            remotePort = 6000,
            enableDebugAutoInput = false,
            leftKey = KeyCode.A,
            rightKey = KeyCode.D,
            attackKey = KeyCode.Space,
            useFixedDelayForTest = false,
            fixedDelayFramesForTest = 4,
            remotePredictionMode = FootsiesPredictedRemoteInputSource.RemotePredictionMode.NeutralWhenUnknown,
            remoteDirectionalHoldFrames = 1
        },
        new MachineProfile
        {
            machineName = "MKLAB03",
            machineLabel = "PCB",
            localPlayerId = 1,
            startDelayFrames = 60,
            remoteIp = "192.168.0.90",
            localPort = 5001,
            remotePort = 6000,
            enableDebugAutoInput = false,
            leftKey = KeyCode.LeftArrow,
            rightKey = KeyCode.RightArrow,
            attackKey = KeyCode.Return,
            useFixedDelayForTest = false,
            fixedDelayFramesForTest = 4,
            remotePredictionMode = FootsiesPredictedRemoteInputSource.RemotePredictionMode.NeutralWhenUnknown,
            remoteDirectionalHoldFrames = 1
        }
    };

    [Header("References")]
    [SerializeField] private UdpP2PTransport transport;
    [SerializeField] private NetworkSessionManager sessionManager;
    [SerializeField] private FileLoggerBootstrap fileLoggerBootstrap;
    [SerializeField] private NetworkInputSender inputSender;
    [SerializeField] private NetworkInputReceiver inputReceiver;
    [SerializeField] private FootsiesBattleInputRouter inputRouter;

    [Header("Input Sources")]
    [SerializeField] private FootsiesNetworkPlayerInputSource footsiesP1NetworkInputSource;
    [SerializeField] private FootsiesNetworkPlayerInputSource footsiesP2NetworkInputSource;
    [SerializeField] private FootsiesPredictedRemoteInputSource footsiesP1PredictedRemoteInputSource;
    [SerializeField] private FootsiesPredictedRemoteInputSource footsiesP2PredictedRemoteInputSource;

    [Header("Round Result Agreement")]
    [SerializeField] private RoundResultAgreementController roundResultAgreementController;

    [Header("Optional")]
    [SerializeField] private DebugAutoInputSequence debugAutoInputSequence;
    [SerializeField] private bool resetDebugSequenceOnConfigure = true;

    private void Awake()
    {
        string currentMachineName = ResolveMachineName();
        MachineProfile profile = FindProfile(currentMachineName);

        if (profile == null)
        {
            Debug.LogError(
                $"[BattleSceneRuntimeConfigurator] Machine profile not found. machineName={currentMachineName}");
            return;
        }

        ApplyProfile(profile, currentMachineName);
    }

    private string ResolveMachineName()
    {
#if UNITY_EDITOR
        if (useOverrideMachineNameInEditor && !string.IsNullOrWhiteSpace(overrideMachineName))
        {
            return overrideMachineName.Trim();
        }
#endif
        return Environment.MachineName;
    }

    private MachineProfile FindProfile(string currentMachineName)
    {
        if (machineProfiles == null || machineProfiles.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < machineProfiles.Length; i++)
        {
            MachineProfile profile = machineProfiles[i];
            if (profile == null || string.IsNullOrWhiteSpace(profile.machineName))
            {
                continue;
            }

            if (string.Equals(
                    profile.machineName.Trim(),
                    currentMachineName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        return null;
    }

    private void ApplyProfile(MachineProfile profile, string currentMachineName)
    {
        // BattleScene に古い serialized 値が残っていても、Player0 は必ず手動入力にする。
        bool resolvedDebugAutoInput = profile.localPlayerId == 0
            ? false
            : profile.enableDebugAutoInput;

        if (transport != null)
        {
            transport.Configure(profile.remoteIp, profile.localPort, profile.remotePort);
        }

        if (sessionManager != null)
        {
            sessionManager.ConfigureRuntime(profile.localPlayerId, profile.startDelayFrames);
        }

        if (fileLoggerBootstrap != null)
        {
            fileLoggerBootstrap.Configure(
                profile.machineLabel,
                profile.localPlayerId,
                profile.remoteIp,
                profile.localPort,
                profile.remotePort
            );
        }

        if (inputSender != null)
        {
            inputSender.ConfigureRuntime(
                profile.localPlayerId,
                profile.leftKey,
                profile.rightKey,
                profile.attackKey,
                resolvedDebugAutoInput
            );
        }

        if (debugAutoInputSequence != null && resetDebugSequenceOnConfigure)
        {
            debugAutoInputSequence.ResetSequence();
        }

        if (inputReceiver != null)
        {
            inputReceiver.ClearBuffer();

            if (profile.useFixedDelayForTest)
            {
                inputReceiver.UseFixedDelayForTest(profile.fixedDelayFramesForTest);
            }
            else
            {
                inputReceiver.UseSceneConfiguredDelayMode();
            }
        }

        if (footsiesP1NetworkInputSource != null)
        {
            footsiesP1NetworkInputSource.Configure(
                FootsiesNetworkPlayerInputSource.ReadMode.LocalSender,
                0
            );
        }

        if (footsiesP2NetworkInputSource != null)
        {
            footsiesP2NetworkInputSource.Configure(
                FootsiesNetworkPlayerInputSource.ReadMode.LocalSender,
                1
            );
        }

        if (footsiesP1PredictedRemoteInputSource != null)
        {
            footsiesP1PredictedRemoteInputSource.ConfigureRemotePlayer(
                0,
                profile.remotePredictionMode,
                profile.remoteDirectionalHoldFrames
            );
        }

        if (footsiesP2PredictedRemoteInputSource != null)
        {
            footsiesP2PredictedRemoteInputSource.ConfigureRemotePlayer(
                1,
                profile.remotePredictionMode,
                profile.remoteDirectionalHoldFrames
            );
        }

        if (inputRouter != null)
        {
            if (profile.localPlayerId == 0)
            {
                inputRouter.ConfigureSources(
                    footsiesP1NetworkInputSource,
                    footsiesP2PredictedRemoteInputSource
                );
            }
            else
            {
                inputRouter.ConfigureSources(
                    footsiesP1PredictedRemoteInputSource,
                    footsiesP2NetworkInputSource
                );
            }
        }

        if (roundResultAgreementController != null)
        {
            roundResultAgreementController.ConfigureRuntime(
                profile.localPlayerId,
                profile.remoteIp
            );
        }

        Debug.Log(
            $"[BattleSceneRuntimeConfigurator] Applied profile machine={currentMachineName}, label={profile.machineLabel}, playerId={profile.localPlayerId}, remote={profile.remoteIp}:{profile.remotePort}, localPort={profile.localPort}, useDebugAutoInput={resolvedDebugAutoInput}, useFixedDelayForTest={profile.useFixedDelayForTest}, fixedDelayFramesForTest={profile.fixedDelayFramesForTest}, remotePredictionMode={profile.remotePredictionMode}, remoteDirectionalHoldFrames={profile.remoteDirectionalHoldFrames}");

        FileLogger.WriteLine(
            $"[BattleSceneRuntimeConfigurator] Applied profile machine={currentMachineName}, label={profile.machineLabel}, playerId={profile.localPlayerId}, remote={profile.remoteIp}:{profile.remotePort}, localPort={profile.localPort}, useDebugAutoInput={resolvedDebugAutoInput}, useFixedDelayForTest={profile.useFixedDelayForTest}, fixedDelayFramesForTest={profile.fixedDelayFramesForTest}, remotePredictionMode={profile.remotePredictionMode}, remoteDirectionalHoldFrames={profile.remoteDirectionalHoldFrames}");
    }
}
