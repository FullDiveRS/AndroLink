using AndroLink.Core;

namespace AndroLink.Adb;

/// <summary>
/// Installs an APK through ADB the same way `adb install` does: push the file
/// to /data/local/tmp, run `pm install -r`, verify the output, clean up.
/// </summary>
public static class AdbInstaller
{
    private const string TempDir = "/data/local/tmp";

    public static async Task<InstallResult> InstallAsync(
        AdbClient client,
        string? serial,
        string apkPath,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(apkPath);
        var remote = $"{TempDir}/androlink_{Guid.NewGuid():N}_{fileName}";

        progress?.Report(new InstallProgress { Percent = 5, Stage = "Pushing APK to device" });
        using (var sync = await client.OpenSyncAsync(serial, ct))
        {
            var size = new FileInfo(apkPath).Length;
            var pushProgress = new Progress<long>(sent =>
            {
                var pct = size == 0 ? 40 : Math.Min(40, 5 + (int)(35.0 * sent / size));
                progress?.Report(new InstallProgress { Percent = pct, Stage = "Pushing APK to device" });
            });
            await sync.PushFileAsync(apkPath, remote, pushProgress, ct);
        }

        try
        {
            progress?.Report(new InstallProgress { Percent = 50, Stage = "Running pm install" });
            var output = await client.RunShellAsync(serial, $"pm install -r -t '{remote}'", ct);

            if (output.Contains("Success", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(new InstallProgress { Percent = 100, Stage = "Done" });
                return new InstallResult(null, null);
            }

            var failure = ExtractFailure(output);
            return new InstallResult(null, $"pm install failed: {failure}");
        }
        finally
        {
            try
            {
                await client.RunShellAsync(serial, $"rm -f '{remote}'", CancellationToken.None);
            }
            catch
            {
                // cleanup is best-effort
            }
        }
    }

    public static async Task UninstallAsync(AdbClient client, string? serial, string package, CancellationToken ct = default)
    {
        var output = await client.RunShellAsync(serial, $"pm uninstall {package}", ct);
        if (!output.Contains("Success", StringComparison.OrdinalIgnoreCase))
        {
            throw new AdbException($"pm uninstall failed: {output}");
        }
    }

    private static string ExtractFailure(string output)
    {
        // pm output looks like:  "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: ...]"
        var start = output.IndexOf("Failure", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return Truncate(output);
        var line = output[start..];
        return Truncate(line.Replace('\n', ' '));
    }

    private static string Truncate(string s) => s.Length <= 160 ? s : s[..157] + "...";
}
