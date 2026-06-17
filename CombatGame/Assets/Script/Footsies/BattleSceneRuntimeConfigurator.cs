using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public class BattleSceneRuntimeConfigurator : MonoBehaviour
{
    [Serializable]
    public class MachineProfile
    {
        [Header("Match")]
        [Tooltip("SystemInfo.deviceName, Multiplayer Play Mode の Player1 / Player2, または profile タグ名")]
        public string profileName;

        [Tooltip("同じ設定を使いたい別名。実PC名と Player1/Player2 を両方入れておくと便利")]
        public string[] aliases;

        [Header("Network")]
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

            if (string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Unity MPPM の "Player 1" / "Player1" 揺れ対策
            return Canonical(a) == Canonical(b);
        }

        private static string Canonical(string value)
        {
            return value.Trim()
                .Replace(" ", string.Empty)
                .Replace("　", string.Empty)
                .ToLowerInvariant();
        }
    }

    [Header("References")]
    [SerializeField] private UdpP2PTransport transport;
    [SerializeField] private NetworkSessionManager sessionManager;

    [Header("Profiles")]
    [SerializeField] private MachineProfile[] machineProfiles;

    [Tooltip("一致するProfileが見つからなかった場合に使う。不要ならnullでよい")]
    [SerializeField] private MachineProfile fallbackProfile;

    [Header("Timing")]
    [SerializeField] private bool configureOnAwake = true;

    [Tooltip("UdpP2PTransportが先に起動していた場合、いったん止めてから設定し直す")]
    [SerializeField] private bool restartTransportAfterConfigure = true;

    private void Awake()
    {
        if (configureOnAwake)
        {
            Configure();
        }
    }

    public void Configure()
    {
        string runtimeKey = RuntimeProfileKeyResolver.Resolve();
        MachineProfile profile = FindProfile(runtimeKey);

        if (profile == null)
        {
            Debug.LogWarning(
                $"[BattleSceneRuntimeConfigurator] No MachineProfile matched runtimeKey='{runtimeKey}'. " +
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
        Debug.Log(
            $"[BattleSceneRuntimeConfigurator] Apply profile='{profile.profileName}' " +
            $"runtimeKey='{runtimeKey}' playerId={profile.playerId} " +
            $"localPort={profile.localPort} remote={profile.remoteIp}:{profile.remotePort} " +
            $"startDelayFrames={profile.startDelayFrames}");

        FileLogger.WriteLine(
            $"[BattleSceneRuntimeConfigurator] Apply profile='{profile.profileName}' " +
            $"runtimeKey='{runtimeKey}' playerId={profile.playerId} " +
            $"localPort={profile.localPort} remote={profile.remoteIp}:{profile.remotePort} " +
            $"startDelayFrames={profile.startDelayFrames}");

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

    private static class RuntimeProfileKeyResolver
    {
        public static string Resolve()
        {
            // 1. 明示指定を最優先
            // 例: -machineProfile Player1
            // 例: -machineProfile=Player1
            string explicitProfile =
                GetCommandLineValue("-machineProfile") ??
                GetCommandLineValue("-profile") ??
                GetCommandLineValue("-runtimeProfile");

            if (!string.IsNullOrWhiteSpace(explicitProfile))
            {
                return explicitProfile.Trim();
            }

            // 2. Multiplayer Play Mode のタグ指定
            // Play Mode Scenario 側で profile:Player1 のようなタグを付ける場合
            string tagProfile = TryGetProfileFromMppmTags();
            if (!string.IsNullOrWhiteSpace(tagProfile))
            {
                return tagProfile.Trim();
            }

            // 3. Multiplayer Play Mode の -name
            // Unity公式FAQでは Player1 / Player2 / Player3 / Player4 を見る方法が案内されている
            string mppmPlayerName = GetCommandLineValue("-name");
            if (!string.IsNullOrWhiteSpace(mppmPlayerName))
            {
                return NormalizeMppmPlayerName(mppmPlayerName);
            }

            // 4. 通常起動時は従来通り機器名
            return SystemInfo.deviceName;
        }

        private static string NormalizeMppmPlayerName(string name)
        {
            string trimmed = name.Trim();

            // "Player 1" と "Player1" のどちらでも MachineProfiles 側は Player1 で書けるようにする
            if (trimmed.StartsWith("Player ", StringComparison.OrdinalIgnoreCase))
            {
                return "Player" + trimmed.Substring("Player ".Length).Trim();
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

        private static string TryGetProfileFromMppmTags()
        {
            foreach (string tag in TryGetCurrentPlayerTagsByReflection())
            {
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                const string profilePrefix = "profile:";
                const string machinePrefix = "machine:";

                if (tag.StartsWith(profilePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return tag.Substring(profilePrefix.Length);
                }

                if (tag.StartsWith(machinePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return tag.Substring(machinePrefix.Length);
                }
            }

            return null;
        }

        private static IEnumerable<string> TryGetCurrentPlayerTagsByReflection()
        {
            // com.unity.multiplayer.playmode が無い環境でもコンパイルを壊さないため、直接参照せずReflectionで読む。
            Type currentPlayerType = FindType(
                "Unity.Multiplayer.PlayMode.CurrentPlayer",
                "Unity.Multiplayer.Playmode.CurrentPlayer");

            if (currentPlayerType == null)
            {
                yield break;
            }

            PropertyInfo tagsProperty = currentPlayerType.GetProperty(
                "Tags",
                BindingFlags.Public | BindingFlags.Static);

            if (tagsProperty == null)
            {
                yield break;
            }

            object value = null;

            try
            {
                value = tagsProperty.GetValue(null);
            }
            catch
            {
                yield break;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    if (item != null)
                    {
                        yield return item.ToString();
                    }
                }
            }
        }

        private static Type FindType(params string[] fullNames)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (string fullName in fullNames)
                {
                    Type type = assembly.GetType(fullName);
                    if (type != null)
                    {
                        return type;
                    }
                }
            }

            return null;
        }
    }
}
