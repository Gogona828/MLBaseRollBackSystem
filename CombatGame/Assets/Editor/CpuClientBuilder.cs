using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class CpuClientBuilder : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    [MenuItem("CombatGame/Build CPU Client")]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Stop Play Mode before building the CPU client.");
        var target = EditorUserBuildSettings.activeBuildTarget;
        string name = target == BuildTarget.StandaloneOSX ? "CombatGame.app"
            : target == BuildTarget.StandaloneWindows64 ? "CombatGame.exe"
            : target == BuildTarget.StandaloneLinux64 ? "CombatGame" : null;
        if (name == null) throw new BuildFailedException("Select a desktop standalone build target first.");
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/CpuClient", name));
        // CPU matches on this Mac need an Intel player. Do not inherit Universal/ARM64
        // from Build Profiles: those also require Apple silicon graphics support.
        const string macPlatform = "OSXUniversal";
        const string architectureSetting = "Architecture";
        string previousArchitecture = target == BuildTarget.StandaloneOSX
            ? EditorUserBuildSettings.GetPlatformSettings(macPlatform, architectureSetting) : null;
        BuildReport result;
        try
        {
            if (target == BuildTarget.StandaloneOSX)
            {
                EditorUserBuildSettings.SetPlatformSettings(macPlatform, architectureSetting, "x64");
                Debug.Log("Building CPU client for Intel Mac (x86_64).");
            }
            result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = output, target = target, options = BuildOptions.None
            });
        }
        finally
        {
            if (target == BuildTarget.StandaloneOSX)
                EditorUserBuildSettings.SetPlatformSettings(macPlatform, architectureSetting, previousArchitecture);
        }
        if (result.summary.result != BuildResult.Succeeded) throw new BuildFailedException("CPU client build failed.");
        string executable = target == BuildTarget.StandaloneOSX
            ? Path.Combine(output, "Contents/MacOS", PlayerSettings.productName) : output;
        // Unity's executable name can differ from productName after sanitization.
        if (target == BuildTarget.StandaloneOSX && !File.Exists(executable))
            executable = Directory.GetFiles(Path.Combine(output, "Contents/MacOS")).Single();
        PlayerPrefs.SetString("CombatGame.CPU.ClientPath", executable);
        PlayerPrefs.Save();
        Debug.Log("CPU client ready: " + executable);
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        var target = report.summary.platform;
        if (target != BuildTarget.StandaloneOSX && target != BuildTarget.StandaloneWindows64
            && target != BuildTarget.StandaloneLinux64) return;
        string output = report.summary.outputPath;
        string data = target == BuildTarget.StandaloneOSX ? Path.Combine(output, "Contents/Resources/Data")
            : Path.Combine(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output) + "_Data");
        string destination = Path.Combine(data, "StreamingAssets/CpuRelay");
        Directory.CreateDirectory(destination);
        File.Copy(Path.GetFullPath(Path.Combine(Application.dataPath, "../../Tools/MacLanRelay/server.py")),
            Path.Combine(destination, "server.py"), true);
    }
}
