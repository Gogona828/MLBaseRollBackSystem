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
    }

    [Header("Editor Override")]
    [SerializeField] private bool useOverrideMachineNameInEditor = false;
    [SerializeField] private string overrideMachineName = "PCA";

    [Header("Machine Profiles")]
    [SerializeField] private MachineProfile[] machineProfiles =
    {
        new MachineProfile
        {
            machineName = "PCA",
            machineLabel = "PCA",
            localPlayerId = 0,
            startDelayFrames = 60,
            remoteIp = "192.168.0.161",
            localPort = 5100,
            remotePort = 5101,
            enableDebugAutoInput = true,
            leftKey = KeyCode.A,
            rightKey = KeyCode.D,
            attackKey = KeyCode.Space,
            useFixedDelayForTest = false,
            fixedDelayFramesForTest = 2
        },
        new MachineProfile
        {
            machineName = "PCB",
            machineLabel = "PCB",
            localPlayerId = 1,
            startDelayFrames = 60,
            remoteIp = "192.168.0.121",
            localPort = 5101,
            remotePort = 5100,
            enableDebugAutoInput = false,
            leftKey = KeyCode.LeftArrow,
            rightKey = KeyCode.RightArrow,
            attackKey = KeyCode.Return,
            useFixedDelayForTest = false,
            fixedDelayFramesForTest = 2
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
                profile.enableDebugAutoInput
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

        // ローカル入力ソース
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

        // 予測リモート入力ソース
        if (footsiesP1PredictedRemoteInputSource != null)
        {
            footsiesP1PredictedRemoteInputSource.ConfigureRemotePlayer(0, true);
        }

        if (footsiesP2PredictedRemoteInputSource != null)
        {
            footsiesP2PredictedRemoteInputSource.ConfigureRemotePlayer(1, true);
        }

        // localPlayerId に応じて P1/P2 の入力源を差し替える
        if (inputRouter != null)
        {
            if (profile.localPlayerId == 0)
            {
                // 自分が P1
                inputRouter.ConfigureSources(
                    footsiesP1NetworkInputSource,
                    footsiesP2PredictedRemoteInputSource
                );
            }
            else
            {
                // 自分が P2
                inputRouter.ConfigureSources(
                    footsiesP1PredictedRemoteInputSource,
                    footsiesP2NetworkInputSource
                );
            }
        }

        Debug.Log(
            $"[BattleSceneRuntimeConfigurator] Applied profile machine={currentMachineName}, label={profile.machineLabel}, playerId={profile.localPlayerId}, remote={profile.remoteIp}:{profile.remotePort}, localPort={profile.localPort}, useDebugAutoInput={profile.enableDebugAutoInput}, useFixedDelayForTest={profile.useFixedDelayForTest}, fixedDelayFramesForTest={profile.fixedDelayFramesForTest}");

        FileLogger.WriteLine(
            $"[BattleSceneRuntimeConfigurator] Applied profile machine={currentMachineName}, label={profile.machineLabel}, playerId={profile.localPlayerId}, remote={profile.remoteIp}:{profile.remotePort}, localPort={profile.localPort}, useDebugAutoInput={profile.enableDebugAutoInput}, useFixedDelayForTest={profile.useFixedDelayForTest}, fixedDelayFramesForTest={profile.fixedDelayFramesForTest}");
    }
}
