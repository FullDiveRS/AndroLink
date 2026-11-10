using System.Diagnostics;
using System.Text.RegularExpressions;
using AndroLink.Core;

namespace AndroLink.Adb;

/// <summary>
/// Runtime backend that bridges AndroLink to any Android instance visible
/// through ADB: the official emulator, a physical device with USB debugging,
/// a Genymotion box, or a future AndroLink VM that exposes adbd.
///
/// Backend options (DeviceConfig.BackendOptions):
///   adbPath        - explicit adb.exe path (falls back to global settings / PATH / SDK)
///   serial         - pin a specific device serial; empty = first online device
///   startCommand   - optional external command run before boot wait (e.g. your QEMU script)
///   stopCommand    - optional external command run on stop
///   bootTimeoutSec - how long to wait for sys.boot_completed (default 120)
///
/// The shared /sdcard folder works as a MIRROR in this mode: use the Files
/// page "Sync from device" / "Push to device" actions to copy between the
/// device storage and the Windows folders.
/// </summary>
public sealed class AdbBackend : IRuntimeBackend
{
    private BackendContext? _context;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, BackendStatus> _status = new();

    public string Id => "adb";
    public string DisplayName => "ADB device bridge";
    public string Description => "Connects to any Android visible over ADB (emulator, USB device, custom VM).";

    public DeviceCapabilities Capabilities { get; } = new()
    {
        SupportsInstall = true,
        SupportsShell = true,
        SupportsFileSync = true,
        LiveSharedFolder = false,
        SupportsGApps = false,
    };

    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;
    public event Action<DeviceConfig, string>? LogEmitted;

    public void Initialize(BackendContext context) => _context = context;

    public async Task StartAsync(DeviceConfig device, CancellationToken ct = default)
    {
        var options = device.BackendOptions;
        var timeoutSec = GetInt(options, "bootTimeoutSec", 120);

        SetStatus(device, BackendStatus.Of(DeviceRunState.Starting, "Ensuring ADB server"));

        var client = CreateClient(options);
        await AdbServer.EnsureServerRunningAsync(GetAdbPath(options), ct);

        var startCommand = options.GetValueOrDefault("startCommand");
        if (!string.IsNullOrWhiteSpace(startCommand))
        {
            LogEmitted?.Invoke(device, $"launching start command: {startCommand}");
            await RunExternalCommandAsync(startCommand, ct);
        }

        // Wait for the transport to appear
        SetStatus(device, BackendStatus.Of(DeviceRunState.Starting, "Waiting for device", 20));
        string? serial = null;
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var devices = await client.ListDevicesAsync(ct);
            serial = PickSerial(options, devices);
            if (serial is not null) break;
            await Task.Delay(500, ct);
        }

        if (serial is null)
        {
            var state = BackendStatus.Of(DeviceRunState.Error, $"no ADB device appeared within {timeoutSec}s");
            SetStatus(device, state);
            throw new AdbException(state.Detail!);
        }

        // Wait for boot completion
        SetStatus(device, BackendStatus.Of(DeviceRunState.Starting, $"Waiting for boot ({serial})", 50, serial));
        deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var boot = (await client.RunShellAsync(serial, "getprop sys.boot_completed", ct)).Trim();
                if (boot == "1")
                {
                    SetStatus(device, BackendStatus.Of(DeviceRunState.Running, "Running", 100, serial));
                    LogEmitted?.Invoke(device, $"device {serial} is fully booted");
                    return;
                }
            }
            catch (AdbException)
            {
                // device rebooting or transport still settling - keep waiting
            }
            await Task.Delay(1000, ct);
        }

        var err = BackendStatus.Of(DeviceRunState.Error, $"device {serial} did not finish booting within {timeoutSec}s", 100, serial);
        SetStatus(device, err);
        throw new AdbException(err.Detail!);
    }

    public async Task StopAsync(DeviceConfig device, CancellationToken ct = default)
    {
        var options = device.BackendOptions;
        var stopCommand = options.GetValueOrDefault("stopCommand");
        var serial = _status.GetValueOrDefault(device.Id)?.AdbSerial;

        if (!string.IsNullOrWhiteSpace(stopCommand))
        {
            LogEmitted?.Invoke(device, $"running stop command: {stopCommand}");
            await RunExternalCommandAsync(stopCommand, CancellationToken.None);
        }
        else if (serial is not null && serial.StartsWith("emulator-", StringComparison.Ordinal))
        {
            try
            {
                var client = CreateClient(options);
                await client.RunShellAsync(serial, "emu kill", CancellationToken.None);
            }
            catch (Exception ex)
            {
                LogEmitted?.Invoke(device, $"emu kill failed: {ex.Message}");
            }
        }
        // Generic devices are external; AndroLink never force-reboots them.

        SetStatus(device, BackendStatus.Of(DeviceRunState.Stopped, "Stopped"));
    }

    public async Task<BackendStatus> GetStatusAsync(DeviceConfig device, CancellationToken ct = default)
    {
        var cached = _status.GetValueOrDefault(device.Id) ?? BackendStatus.Of(DeviceRunState.Stopped);
        if (cached.State is not (DeviceRunState.Running or DeviceRunState.Starting))
        {
            return cached;
        }

        // Light re-verification so the UI reflects reality if the VM died.
        try
        {
            var client = CreateClient(device.BackendOptions);
            var devices = await client.ListDevicesAsync(ct);
            var serial = cached.AdbSerial;
            var match = devices.FirstOrDefault(d => d.Serial == serial);
            if (serial is null || match is null || match.State != "device")
            {
                var state = BackendStatus.Of(DeviceRunState.Stopped, "Device not visible over ADB");
                SetStatus(device, state);
                return state;
            }
            return cached;
        }
        catch
        {
            return cached;
        }
    }

    public async Task InstallAppAsync(DeviceConfig device, string apkPath, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        var client = CreateClient(device.BackendOptions);
        var serial = await RequireSerialAsync(client, device, ct);
        var result = await AdbInstaller.InstallAsync(client, serial, apkPath, progress, ct);
        if (!result.Success) throw new AdbException(result.Error ?? "install failed");
    }

    public async Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(DeviceConfig device, CancellationToken ct = default)
    {
        var client = CreateClient(device.BackendOptions);
        var serial = await RequireSerialAsync(client, device, ct);
        var output = await client.RunShellAsync(serial, "pm list packages -3", ct);

        var result = new List<InstalledApp>();
        var catalog = _context is null
            ? new InstalledAppCatalog(Path.GetTempFileName())
            : InstalledAppCatalog.LoadFor(device, _context.SharedRoot);

        foreach (var line in output.Split('\n'))
        {
            var m = Regex.Match(line.Trim(), @"^package:(\S+)$");
            if (!m.Success) continue;
            var package = m.Groups[1].Value;
            var known = catalog.Find(package);
            result.Add(new InstalledApp(
                package,
                known?.Label ?? package,
                known?.VersionName ?? "",
                known?.VersionCode ?? 0,
                known?.ApkFileName ?? "",
                known?.InstalledAt ?? DateTime.UtcNow,
                known?.SourcePath ?? ""));
        }
        return result;
    }

    public async Task UninstallAppAsync(DeviceConfig device, string packageName, CancellationToken ct = default)
    {
        var client = CreateClient(device.BackendOptions);
        var serial = await RequireSerialAsync(client, device, ct);
        await AdbInstaller.UninstallAsync(client, serial, packageName, ct);
    }

    public async Task SyncFromDeviceAsync(DeviceConfig device, string androidPath, string windowsDir, CancellationToken ct = default)
    {
        var client = CreateClient(device.BackendOptions);
        var serial = await RequireSerialAsync(client, device, ct);
        using var sync = await client.OpenSyncAsync(serial, ct);
        await sync.PullDirectoryAsync(androidPath, windowsDir, new Progress<string>(msg => LogEmitted?.Invoke(device, msg)), ct);
    }

    public async Task PushToDeviceAsync(DeviceConfig device, string windowsDir, string androidPath, CancellationToken ct = default)
    {
        var client = CreateClient(device.BackendOptions);
        var serial = await RequireSerialAsync(client, device, ct);
        using var sync = await client.OpenSyncAsync(serial, ct);
        await sync.PushDirectoryAsync(windowsDir, androidPath, new Progress<string>(msg => LogEmitted?.Invoke(device, msg)), ct);
    }

    public Task<string?> ResolveAdbSerialAsync(DeviceConfig device, CancellationToken ct = default)
        => ResolveSerialAsync(device.BackendOptions, ct);

    // ------------------------------------------------------------ internals

    private static AdbClient CreateClient(IReadOnlyDictionary<string, string> options)
        => new()
        {
            Host = options.GetValueOrDefault("adbHost", "127.0.0.1"),
            Port = int.TryParse(options.GetValueOrDefault("adbPort"), out var p) ? p : AdbConnection.DefaultPort,
        };

    private static string? GetAdbPath(IReadOnlyDictionary<string, string> options)
        => options.TryGetValue("adbPath", out var p) && !string.IsNullOrWhiteSpace(p) ? p : null;

    private static string? PickSerial(IReadOnlyDictionary<string, string> options, List<AdbDeviceInfo> devices)
    {
        var pinned = options.TryGetValue("serial", out var s) ? s.Trim() : null;
        if (!string.IsNullOrEmpty(pinned))
        {
            var match = devices.FirstOrDefault(d => d.Serial == pinned && d.State == "device");
            return match?.Serial;
        }
        return devices.FirstOrDefault(d => d.State == "device")?.Serial;
    }

    private async Task<string?> ResolveSerialAsync(IReadOnlyDictionary<string, string> options, CancellationToken ct)
    {
        var pinned = options.TryGetValue("serial", out var s) ? s.Trim() : null;
        if (!string.IsNullOrEmpty(pinned)) return pinned;

        var client = CreateClient(options);
        await AdbServer.EnsureServerRunningAsync(GetAdbPath(options), ct);
        var devices = await client.ListDevicesAsync(ct);
        return PickSerial(options, devices);
    }

    private static async Task<string> RequireSerialAsync(AdbClient client, DeviceConfig device, CancellationToken ct)
    {
        var serial = device.BackendOptions.TryGetValue("serial", out var s) && !string.IsNullOrWhiteSpace(s)
            ? s
            : (await client.ListDevicesAsync(ct)).FirstOrDefault(d => d.State == "device")?.Serial;
        return serial ?? throw new AdbException($"device '{device.Name}' is not running (no ADB transport)");
    }

    private static int GetInt(IReadOnlyDictionary<string, string> options, string key, int fallback)
        => int.TryParse(options.GetValueOrDefault(key), out var v) ? v : fallback;

    private void SetStatus(DeviceConfig device, BackendStatus status)
    {
        _status[device.Id] = status;
        StateChanged?.Invoke(this, new DeviceStateChangedEventArgs { DeviceId = device.Id, Status = status });
    }

    private static async Task RunExternalCommandAsync(string command, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi) ?? throw new AdbException($"could not start: {command}");
        await process.WaitForExitAsync(ct);
    }

    public void Dispose() { }
}
