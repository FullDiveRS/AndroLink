using System.Diagnostics;
using AndroLink.Core;
using AndroLink.Core.SystemImages;

namespace AndroLink.Adb;

/// <summary>
/// Boots one of AndroLink's downloaded Android versions with the official
/// Android SDK emulator - the same engine Android Studio's Device Manager
/// uses. Works with every downloaded image (x86_64 AND arm64-v8a, because
/// the emulator itself ships per-host binaries), detects an existing Android
/// Studio installation, and bridges the running guest into AndroLink through
/// the ADB backend.
///
/// Requirements: the Android SDK "emulator" + "platform-tools" packages
/// (installed automatically with Android Studio, or via cmdline-tools).
///
/// Backend options (DeviceConfig.BackendOptions):
///   sdkDir       - explicit Android SDK root (else auto-detected)
///   emulatorPort - even port for the emulator console (default 5554)
///   bootTimeoutSec - boot wait in seconds (default 240)
///   extraArgs    - appended verbatim to the emulator command line
/// </summary>
public sealed class EmulatorBackend : IRuntimeBackend
{
    private BackendContext? _context;
    private readonly AdbBackend _adb = new();
    private Process? _emulator;
    private readonly object _gate = new();

    private readonly SystemImageStore _images;

    public EmulatorBackend(SystemImageStore? imageStore = null)
        => _images = imageStore ?? new SystemImageStore();

    public string Id => "emulator";
    public string DisplayName => "Android emulator (SDK images)";
    public string Description =>
        "Boots downloaded Android versions with the official SDK emulator (x86_64 and arm64-v8a) - the engine behind Android Studio's Device Manager.";

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

    public void Initialize(BackendContext context)
    {
        _context = context;
        _adb.Initialize(context);
        _adb.StateChanged += (_, e) => StateChanged?.Invoke(this, e);
        _adb.LogEmitted += (d, m) => LogEmitted?.Invoke(d, m);
    }

    public async Task StartAsync(DeviceConfig device, CancellationToken ct = default)
    {
        var options = device.BackendOptions;

        // 1) The system image must be installed locally.
        var image = RequireImage(device);
        LogEmitted?.Invoke(device, $"system image: {image.Summary} ({image.PackagePath})");

        // 2) Locate the Android SDK emulator.
        var sdk = AndroidSdkLocator.Locate(options.GetValueOrDefault("sdkDir"));
        if (sdk is null)
        {
            throw new InvalidOperationException(
                "The Android SDK emulator was not found. Install Android Studio (or the cmdline-tools + emulator + platform-tools packages), " +
                "or set the sdkDir backend option to your SDK folder.");
        }
        LogEmitted?.Invoke(device, $"Android SDK: {sdk.Root}");

        // 3) Create the AVD and launch the emulator.
        var avdHome = AndroLinkPaths.AvdHome;
        Directory.CreateDirectory(avdHome);
        var avdName = AvdWriter.CreateAvd(avdHome, device, image, _images.Root);

        if (!IsEmulatorAlive())
        {
            var port = int.TryParse(options.GetValueOrDefault("emulatorPort"), out var p) ? p : 5554;
            LaunchEmulator(sdk, avdName, device, options, port);

            // The emulator picks the next free port if the requested one is
            // taken; give it a moment and relaunch without a fixed port if it
            // exited immediately.
            await Task.Delay(4000, ct);
            if (!IsEmulatorAlive())
            {
                LogEmitted?.Invoke(device, $"emulator exited right after launch (port {port} busy?) - retrying with auto port");
                LaunchEmulator(sdk, avdName, device, options, port: null);
            }
        }
        else
        {
            LogEmitted?.Invoke(device, "emulator already running - reusing it");
        }

        // 4) Bridge into the ADB backend and wait for full boot.
        var adbDevice = WithAdbOptions(device, options, sdk);
        await _adb.StartAsync(adbDevice, ct);
    }

    public async Task StopAsync(DeviceConfig device, CancellationToken ct = default)
    {
        try
        {
            await _adb.StopAsync(device, ct);
        }
        finally
        {
            lock (_gate)
            {
                if (_emulator is { HasExited: false })
                {
                    if (!_emulator.WaitForExitAsync(CancellationToken.None).Wait(8000))
                    {
                        LogEmitted?.Invoke(device, "emulator did not exit after 'emu kill' - terminating");
                        try { _emulator.Kill(entireProcessTree: true); }
                        catch { }
                    }
                }
                _emulator?.Dispose();
                _emulator = null;
            }
        }
    }

    public Task<BackendStatus> GetStatusAsync(DeviceConfig device, CancellationToken ct = default)
        => _adb.GetStatusAsync(device, ct);

    public Task InstallAppAsync(DeviceConfig device, string apkPath, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
        => _adb.InstallAppAsync(device, apkPath, progress, ct);

    public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(DeviceConfig device, CancellationToken ct = default)
        => _adb.ListInstalledAppsAsync(device, ct);

    public Task UninstallAppAsync(DeviceConfig device, string packageName, CancellationToken ct = default)
        => _adb.UninstallAppAsync(device, packageName, ct);

    public Task SyncFromDeviceAsync(DeviceConfig device, string androidPath, string windowsDir, CancellationToken ct = default)
        => _adb.SyncFromDeviceAsync(device, androidPath, windowsDir, ct);

    public Task PushToDeviceAsync(DeviceConfig device, string windowsDir, string androidPath, CancellationToken ct = default)
        => _adb.PushToDeviceAsync(device, windowsDir, androidPath, ct);

    public Task<string?> ResolveAdbSerialAsync(DeviceConfig device, CancellationToken ct = default)
        => _adb.ResolveAdbSerialAsync(device, ct);

    // ------------------------------------------------------------ internals

    private SystemImageInfo RequireImage(DeviceConfig device)
    {
        var package = device.ImagePackage;
        if (string.IsNullOrWhiteSpace(package))
        {
            throw new InvalidOperationException(
                "This device has no Android version selected. Open 'Create device' and pick one of the downloaded images (see the Android images page).");
        }
        return _images.FindInstalled(package)
            ?? throw new InvalidOperationException(
                $"System image '{package}' is not downloaded yet - grab it from the Android images page first.");
    }

    private void LaunchEmulator(SdkInfo sdk, string avdName, DeviceConfig device,
        IReadOnlyDictionary<string, string> options, int? port)
    {
        var psi = new ProcessStartInfo
        {
            FileName = sdk.EmulatorExe,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        psi.ArgumentList.Add("-avd");
        psi.ArgumentList.Add(avdName);
        if (port is not null)
        {
            psi.ArgumentList.Add("-port");
            psi.ArgumentList.Add(port.Value.ToString());
        }
        psi.ArgumentList.Add("-gpu");
        psi.ArgumentList.Add("auto");
        psi.ArgumentList.Add("-no-boot-anim");

        var extra = options.GetValueOrDefault("extraArgs");
        if (!string.IsNullOrWhiteSpace(extra))
        {
            foreach (var arg in SplitArgs(extra)) psi.ArgumentList.Add(arg);
        }

        // Point the emulator at AndroLink's image store + AVD home.
        psi.EnvironmentVariables["ANDROID_SDK_ROOT"] = _images.Root;
        psi.EnvironmentVariables["ANDROID_AVD_HOME"] = AndroLinkPaths.AvdHome;
        psi.EnvironmentVariables["ANDROID_USER_HOME"] = AndroLinkPaths.AvdHome;

        var abiNote = device.Abi is { Length: > 0 } ? $" (guest {device.Abi})" : "";
        LogEmitted?.Invoke(device, $"launching emulator -avd {avdName}{abiNote}");
        _emulator = Process.Start(psi) ?? throw new InvalidOperationException("failed to start the Android emulator");
    }

    private static DeviceConfig WithAdbOptions(DeviceConfig device, IReadOnlyDictionary<string, string> options, SdkInfo sdk)
    {
        var clone = CloneDevice(device);
        var port = int.TryParse(options.GetValueOrDefault("emulatorPort"), out var p) ? p : 5554;
        clone.BackendOptions["serial"] = $"emulator-{port}";
        if (!clone.BackendOptions.ContainsKey("adbPath") && sdk.AdbExe is not null)
        {
            clone.BackendOptions["adbPath"] = sdk.AdbExe;
        }
        clone.BackendOptions["bootTimeoutSec"] = options.GetValueOrDefault("bootTimeoutSec", "240");
        return clone;
    }

    private static DeviceConfig CloneDevice(DeviceConfig device) => new()
    {
        Id = device.Id,
        Name = device.Name,
        Width = device.Width,
        Height = device.Height,
        Dpi = device.Dpi,
        RamMb = device.RamMb,
        CpuCores = device.CpuCores,
        BackendId = device.BackendId,
        ImagePath = device.ImagePath,
        ApiLevel = device.ApiLevel,
        Abi = device.Abi,
        ImagePackage = device.ImagePackage,
        GAppsZipPath = device.GAppsZipPath,
        BackendOptions = new Dictionary<string, string>(device.BackendOptions, StringComparer.OrdinalIgnoreCase),
        Notes = device.Notes,
        CreatedAt = device.CreatedAt,
    };

    private static IEnumerable<string> SplitArgs(string raw)
        => raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private bool IsEmulatorAlive()
    {
        lock (_gate) return _emulator is { HasExited: false };
    }

    public void Dispose()
    {
        try
        {
            lock (_gate)
            {
                if (_emulator is { HasExited: false }) _emulator.Kill(entireProcessTree: true);
                _emulator?.Dispose();
                _emulator = null;
            }
        }
        catch { }
    }
}
