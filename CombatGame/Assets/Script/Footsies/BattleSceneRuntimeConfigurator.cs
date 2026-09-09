using System;
using System.Linq;
using Footsies;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
public class BattleSceneRuntimeConfigurator : MonoBehaviour
{
    [Serializable]
    public class MachineProfile
    {
        [Header("Match")]
        public string profileName;

        [Tooltip("実PC名、Player1、Player2などの別名")]
        public string[] aliases;

        [Header("Transport")]
        public string remoteIp = "127.0.0.1";
        public int localPort = 5000;
        public int remotePort = 6000;

        [Header("Session")]
        public int playerId = 0;
        public int startDelayFrames = 60;

        public bool Matches(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (IsSameName(profileName, key))
            {
                return true;
            }

            if (aliases == null)
            {
                return false;
            }

            return aliases.Any(alias => IsSameName(alias, key));
        }

        private static bool IsSameName(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            return Normalize(a) == Normalize(b);
        }

        private static string Normalize(string value)
        {
            return value
                .Trim()
                .Replace(" ", string.Empty)
                .Replace("　", string.Empty)
                .ToLowerInvariant();
        }
    }

    [Header("References")]
    [SerializeField] private UdpP2PTransport transport;
    [SerializeField] private NetworkSessionManager sessionManager;

    [Header("Offline Battle")]
    [SerializeField] private BattleCore battleCore;
    [SerializeField] private FootsiesBattleInputRouter battleInputRouter;
    [SerializeField] private FootsiesLocalPlayerInputSource player1LocalInputSource;
    [SerializeField] private GameObject rollbackRoot;

    [Header("Profiles")]
    [SerializeField] private MachineProfile[] machineProfiles;

    [SerializeField] private MachineProfile fallbackProfile;

    [Header("Apply")]
    [SerializeField] private bool configureOnAwake = true;

    [Tooltip("UdpP2PTransportが先に起動していた場合、止めてから設定し直す")]
    [SerializeField] private bool restartTransportAfterConfigure = true;

    private void Awake()
    {
        if (IsOfflineBattleRequested())
        {
            ConfigureOfflineBattle();
            return;
        }

        if (configureOnAwake)
        {
            Configure();
        }
    }

    private bool IsOfflineBattleRequested()
    {
        GameManager gameManager = FindObjectOfType<GameManager>();
        return gameManager != null && gameManager.isOfflineMode;
    }

    private void ConfigureOfflineBattle()
    {
        ResolveOfflineReferences();

        if (transport != null)
        {
            transport.StopTransport();
            transport.gameObject.SetActive(false);
        }

        if (rollbackRoot != null)
        {
            rollbackRoot.SetActive(false);
        }

        if (battleInputRouter != null)
        {
            battleInputRouter.ConfigureSources(player1LocalInputSource, null);
        }

        if (battleCore != null)
        {
            battleCore.ConfigureOfflineBattle();
            battleCore.enabled = true;
        }

        Debug.Log("[BattleSceneRuntimeConfigurator] Offline VS CPU mode enabled. Network and rollback are disabled.");
    }

    private void ResolveOfflineReferences()
    {
        if (battleCore == null)
        {
            battleCore = FindObjectOfType<BattleCore>(true);
        }

        if (battleInputRouter == null)
        {
            battleInputRouter = FindObjectOfType<FootsiesBattleInputRouter>(true);
        }

        if (player1LocalInputSource == null)
        {
            FootsiesLocalPlayerInputSource[] localSources =
                FindObjectsOfType<FootsiesLocalPlayerInputSource>(true);
            player1LocalInputSource = localSources.FirstOrDefault(source => source.IsPlayer1);
        }
    }

    public void Configure()
    {
        if (PlayerPrefs.GetInt("CombatGame.LAN.Enabled", 0) == 1)
        {
            int port = PlayerPrefs.GetInt("CombatGame.LAN.Port", 6000);
            var lan = new MachineProfile { profileName = "Mac LAN + FEP", remoteIp = PlayerPrefs.GetString("CombatGame.LAN.IP", "127.0.0.1"), remotePort = port, localPort = 0, playerId = port % 2 };
            Application.runInBackground = true;
            ApplyProfile(lan, "LAN settings");
            ConfigureLanBattle(lan.playerId);
            return;
        }
        string runtimeKey = ResolveRuntimeProfileKey();
        MachineProfile profile = FindProfile(runtimeKey);

        if (profile == null)
        {
            Debug.LogWarning(
                $"[BattleSceneRuntimeConfigurator] No profile matched runtimeKey='{runtimeKey}'. " +
                $"fallback={(fallbackProfile != null ? fallbackProfile.profileName : "null")}");

            profile = fallbackProfile;
        }

        if (profile == null)
        {
            Debug.LogError("[BattleSceneRuntimeConfigurator] No valid MachineProfile. Configuration skipped.");
            return;
        }

        ApplyProfile(profile, runtimeKey);
    }

    private void ConfigureLanBattle(int playerId)
    {
        var sender = FindObjectOfType<NetworkInputSender>(true);
        var receiver = FindObjectOfType<NetworkInputReceiver>(true);
        var clock = FindObjectOfType<NetworkFrameClock>(true);
        var mismatch = FindObjectOfType<PredictionMismatchDetector>(true);
        var local = FindObjectOfType<FootsiesNetworkPlayerInputSource>(true);
        var predicted = FindObjectOfType<FootsiesPredictedRemoteInputSource>(true);
        var router = FindObjectOfType<FootsiesBattleInputRouter>(true);
        if(sender == null || receiver == null || local == null || predicted == null || router == null)
            throw new InvalidOperationException("LAN requires the CombatGame battle scene with network and rollback components.");
        sender.ConfigureRuntime(playerId, KeyCode.A, KeyCode.D, KeyCode.Space, false);
        local.Configure(FootsiesNetworkPlayerInputSource.ReadMode.LocalSender, 1-playerId);
        predicted.ConfigureReferences(receiver, clock, mismatch);
        predicted.ConfigureRemotePlayer(1-playerId, FootsiesPredictedRemoteInputSource.RemotePredictionMode.FepSupervised, 12);
        predicted.EnableFep();
        router.ConfigureSources(playerId == 0 ? (MonoBehaviour)local : predicted, playerId == 0 ? (MonoBehaviour)predicted : local);
        sessionManager.UseRelayStartEcho = true;
        FindObjectOfType<RoundResultAgreementController>(true)?.ConfigureRelay(playerId, transport);
    }

    private MachineProfile FindProfile(string runtimeKey)
    {
        if (machineProfiles == null)
        {
            return null;
        }

        return machineProfiles.FirstOrDefault(profile =>
            profile != null && profile.Matches(runtimeKey));
    }

    private void ApplyProfile(MachineProfile profile, string runtimeKey)
    {
        string message =
            $"[BattleSceneRuntimeConfigurator] Apply profile='{profile.profileName}' " +
            $"runtimeKey='{runtimeKey}' " +
            $"playerId={profile.playerId} " +
            $"localPort={profile.localPort} " +
            $"remote={profile.remoteIp}:{profile.remotePort} " +
            $"startDelayFrames={profile.startDelayFrames}";

        Debug.Log(message);
        FileLogger.WriteLine(message);

        if (transport != null)
        {
            if (restartTransportAfterConfigure && transport.IsStarted)
            {
                transport.StopTransport();
            }

            transport.Configure(profile.remoteIp, profile.localPort, profile.remotePort);

            if (restartTransportAfterConfigure && !transport.IsStarted)
            {
                transport.StartTransport();
            }
        }

        if (sessionManager != null)
        {
            sessionManager.ConfigureRuntime(profile.playerId, profile.startDelayFrames);
        }
    }

    private static string ResolveRuntimeProfileKey()
    {
        // 手動指定を最優先。
        // 例: -machineProfile Player2
        // 例: -machineProfile=Player2
        string explicitProfile =
            GetCommandLineValue("-machineProfile") ??
            GetCommandLineValue("-profile") ??
            GetCommandLineValue("-runtimeProfile");

        if (!string.IsNullOrWhiteSpace(explicitProfile))
        {
            return NormalizePlayerName(explicitProfile);
        }

        // Unity Multiplayer Play Mode。
        // Main Editor: Player1
        // Virtual Player 1: Player2
        // Virtual Player 2: Player3
        // Virtual Player 3: Player4
        string mppmName = GetCommandLineValue("-name");

        if (!string.IsNullOrWhiteSpace(mppmName))
        {
            return NormalizePlayerName(mppmName);
        }

        // 通常起動時は従来通りPC名。
        return SystemInfo.deviceName;
    }

    private static string NormalizePlayerName(string value)
    {
        string trimmed = value.Trim();

        // "Player 2" と "Player2" の両方に対応
        if (trimmed.StartsWith("Player ", StringComparison.OrdinalIgnoreCase))
        {
            string index = trimmed.Substring("Player ".Length).Trim();
            return "Player" + index;
        }

        return trimmed;
    }

    private static string GetCommandLineValue(string key)
    {
        string[] args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (string.Equals(arg, key, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    return args[i + 1];
                }

                return null;
            }

            string prefix = key + "=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg.Substring(prefix.Length);
            }
        }

        return null;
    }
}
