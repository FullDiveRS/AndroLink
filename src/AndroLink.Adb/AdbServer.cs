using System.Diagnostics;
using System.Net.Sockets;
using AndroLink.Core;

namespace AndroLink.Adb;

/// <summary>
/// Locates an adb executable and makes sure an adb server is running on
/// 127.0.0.1:5037. AndroLink speaks the wire protocol itself, but starting the
/// server binary is delegated to the official adb.exe (bundled by the
/// installer when present, otherwise discovered from PATH / Android SDK).
/// </summary>
public static class AdbServer
{
    public static bool IsServerRunning(int port = AdbConnection.DefaultPort)
    {
        try
        {
            using var probe = new TcpClient();
            var task = probe.ConnectAsync("127.0.0.1", port);
            return task.Wait(600) && probe.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Finds adb.exe: explicit configured path → PATH → standard SDK locations.
    /// </summary>
    public static string? FindAdbExecutable(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (File.Exists(configuredPath)) return configuredPath;
        }

        var name = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), name);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* ignore malformed PATH entries */ }
        }

        if (OperatingSystem.IsWindows())
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", name),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Android", "platform-tools", name),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Android", "Sdk", "platform-tools", name),
            };
            return candidates.FirstOrDefault(File.Exists);
        }
        return null;
    }

    /// <summary>Starts the adb server if it is not already listening. Safe to call repeatedly.</summary>
    public static async Task EnsureServerRunningAsync(string? configuredAdbPath, CancellationToken ct = default)
    {
        if (IsServerRunning()) return;

        var adb = FindAdbExecutable(configuredAdbPath);
        if (adb is null)
        {
            throw new AdbException(
                "No adb server is running and no adb executable was found. " +
                "Set the adb path in AndroLink settings or install Android platform-tools.");
        }

        Log.Info("adb", $"starting adb server via {adb}");
        var psi = new ProcessStartInfo
        {
            FileName = adb,
            Arguments = "start-server",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi) ?? throw new AdbException("could not launch adb start-server");

        // Wait for the server to accept connections.
        for (var i = 0; i < 100; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (IsServerRunning()) return;
            await Task.Delay(100, ct);
        }
        throw new AdbException($"adb server did not come up within 10 seconds (adb: {adb})");
    }
}
