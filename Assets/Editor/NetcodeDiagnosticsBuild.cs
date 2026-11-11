using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Fragsurf.Movement;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// No source rewrites or recompilation during a build. The sidecar is copied
// with the complete build folder and loaded only while preparing diagnostics.
public sealed class NetcodeDiagnosticsBuild : IPostprocessBuildWithReport {
    public int callbackOrder => 1000;

    [MenuItem("Tools/Netcode Diagnostics/Build Windows Development Player")]
    public static void Build() {
        string destination = EditorUtility.SaveFolderPanel("Development build folder", "Builds", "LAN-Diagnostics");
        if (string.IsNullOrEmpty(destination)) return;
        BuildTo(destination);
    }

    // Batch entry point: Unity -batchmode -quit -executeMethod NetcodeDiagnosticsBuild.BuildBatch
    // [-netcodeBuildOutput <folder>]. Exits nonzero when the build fails.
    public static void BuildBatch() {
        string[] arguments = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(arguments, "-netcodeBuildOutput");
        string destination = index >= 0 && index + 1 < arguments.Length
            ? arguments[index + 1] : Path.Combine("Builds", "LAN-Diagnostics");
        BuildReport report = BuildTo(Path.GetFullPath(destination));
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    private static BuildReport BuildTo(string destination) {
        return BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = Path.Combine(destination, "GAUTNLET.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
    }

    public void OnPostprocessBuild(BuildReport report) {
        // The result is still Unknown while postprocess callbacks run, so only
        // a build already known to have failed or been cancelled is skipped.
        if ((report.summary.options & BuildOptions.Development) == 0) return;
        if (report.summary.result == BuildResult.Failed || report.summary.result == BuildResult.Cancelled) return;
        if (report.summary.platform != BuildTarget.StandaloneWindows64) return;
        string directory = Path.GetDirectoryName(report.summary.outputPath);
        var stamp = new TraceBuildStamp();
        stamp.revision = Git("rev-parse HEAD").Trim();
        stamp.dirty = !string.IsNullOrWhiteSpace(Git("status --porcelain --untracked-files=normal"));
        stamp.contentHash = HashText(string.Join("\n", EditorBuildSettings.scenes.Where(s => s.enabled)
            .Select(s => s.path + ":" + AssetDatabase.GetAssetDependencyHash(s.path))));
        // Hash path + per-file SHA256 in stable order. Exclude the stamp itself
        // and optional debug-symbol backups. This identifies the copied artifact.
        var entries = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith("netcode-build.json", StringComparison.OrdinalIgnoreCase) &&
                        !p.Contains("BackUpThisFolder_ButDontShipItWithYourGame") && !p.Contains("BurstDebugInformation_DoNotShip"))
            .OrderBy(p => p, StringComparer.Ordinal);
        var hashes = new StringBuilder();
        using (var sha = SHA256.Create()) {
            foreach (string path in entries) {
                using (var stream = File.OpenRead(path))
                    hashes.Append(path.Substring(directory.Length).Replace('\\', '/')).Append(':')
                          .Append(Convert.ToBase64String(sha.ComputeHash(stream))).Append('\n');
            }
        }
        stamp.artifactHash = HashText(hashes.ToString());
        File.WriteAllText(Path.Combine(directory, "netcode-build.json"), JsonUtility.ToJson(stamp, true));
    }

    private static string HashText(string text) {
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
    }
    private static string Git(string arguments) {
        try {
            using (var process = Process.Start(new ProcessStartInfo("git", arguments) {
                WorkingDirectory = Path.GetDirectoryName(Application.dataPath), UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            })) {
                string output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(5000) || process.ExitCode != 0) return "unknown";
                return output;
            }
        } catch (Exception) { return "unknown"; }
    }
}
