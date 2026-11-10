using System.IO.Compression;
using AndroLink.Core;
using AndroLink.Core.SystemImages;

namespace AndroLink.Backend.Simulated;

/// <summary>
/// A fully working demo device that runs entirely inside AndroLink - no VM,
/// no Android system image. It exists so the whole product (device lifecycle,
/// APK installs, the shared /sdcard folder, the file browser, GApps hook
/// validation) can be exercised immediately, and so plugin authors can study
/// a complete IRuntimeBackend implementation.
///
/// Storage model: the device's /sdcard IS a real Windows folder
/// (userdata/&lt;id&gt;/sdcard) - install an APK and the file lands in
/// sdcard\Android\AndroLink\apps; drop files into Download and they "appear
/// on the device". That is exactly the storage model future VM backends get
/// when they bind-mount the same folder over 9p/virtfs.
/// </summary>
public sealed class SimulatedBackend : IRuntimeBackend
{
    private BackendContext? _context;
    private readonly Dictionary<string, BackendStatus> _status = new();
    private readonly Dictionary<string, CancellationTokenSource> _boots = new();

    public string Id => "simulated";
    public string DisplayName => "Simulated device (demo)";
    public string Description => "Fully working demo device: real shared folders, simulated boot and app installs.";

    public DeviceCapabilities Capabilities { get; } = new()
    {
        SupportsInstall = true,
        SupportsShell = false,
        SupportsFileSync = false,      // folders are live - no sync needed
        LiveSharedFolder = true,
        SupportsGApps = true,
    };

    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;
    public event Action<DeviceConfig, string>? LogEmitted;

    public void Initialize(BackendContext context) => _context = context;

    public async Task StartAsync(DeviceConfig device, CancellationToken ct = default)
    {
        // Create the full shared layout (standard /sdcard dirs, README, marker).
        string? sdcard = null;
        if (_context is not null)
        {
            sdcard = SharedFolderLayout.EnsureDeviceFolders(device, _context.SharedRoot);
        }

        var gapps = GAppsPlan.FromDevice(device);
        if (gapps.Enabled)
        {
            var problem = gapps.Validate();
            if (problem is not null)
            {
                LogEmitted?.Invoke(device, $"GApps hook: {problem} (continuing without GApps)");
            }
            else
            {
                ApplyGAppsMarker(device, gapps.ZipPath!);
                LogEmitted?.Invoke(device, $"GApps hook: openGApps package '{Path.GetFileName(gapps.ZipPath)}' validated and staged for this device");
            }
        }

        // Simulated boot sequence with progress - reflects the picked Android
        // version / ABI so the demo feels like the real thing.
        _boots.GetValueOrDefault(device.Id)?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _boots[device.Id] = cts;

        var imageLabel = SystemImageLabels.Describe(device);

        SetStatus(device, BackendStatus.Of(DeviceRunState.Starting, "Powering on", 5));
        var stages = new (int Percent, string Label, int DelayMs)[]
        {
            (20, $"Loading system image{imageLabel}", 500),
            (45, "Starting Zygote", 700),
            (70, "Starting apps", 800),
            (90, "Optimizing apps", 900),
        };

        try
        {
            foreach (var (percent, label, delay) in stages)
            {
                await Task.Delay(delay, cts.Token);
                SetStatus(device, BackendStatus.Of(DeviceRunState.Starting, label, percent));
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus(device, BackendStatus.Of(DeviceRunState.Stopped, "Stopped during boot"));
            return;
        }

        SeedSampleFiles(device);
        SetStatus(device, BackendStatus.Of(DeviceRunState.Running, "Running", 100));
        LogEmitted?.Invoke(device, $"device is up{imageLabel}; /sdcard bound to {sdcard ?? "(unset)"}");
    }

    public Task StopAsync(DeviceConfig device, CancellationToken ct = default)
    {
        _boots.GetValueOrDefault(device.Id)?.Cancel();
        _boots.Remove(device.Id);
        SetStatus(device, BackendStatus.Of(DeviceRunState.Stopped, "Stopped"));
        return Task.CompletedTask;
    }

    public Task<BackendStatus> GetStatusAsync(DeviceConfig device, CancellationToken ct = default)
        => Task.FromResult(_status.GetValueOrDefault(device.Id, BackendStatus.Of(DeviceRunState.Stopped)));

    public async Task InstallAppAsync(DeviceConfig device, string apkPath, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        if (!File.Exists(apkPath))
        {
            throw new FileNotFoundException("APK not found", apkPath);
        }

        var info = ApkInspector.Inspect(apkPath) ?? ApkInspector.InspectOrFallback(apkPath);
        var sdcard = RequireContext().DeviceSdcardDir(device);
        var appsDir = Path.Combine(sdcard, "Android", "AndroLink", "apps");
        Directory.CreateDirectory(appsDir);

        progress?.Report(new InstallProgress { Percent = 15, Stage = "Copying APK to shared storage" });
        var target = Path.Combine(appsDir, $"{info.Package}.apk");
        await using (var source = File.OpenRead(apkPath))
        await using (var dest = File.Create(target))
        {
            var buffer = new byte[81920];
            long total = source.Length, copied = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                copied += read;
                var pct = 15 + (int)(55.0 * copied / Math.Max(1, total));
                progress?.Report(new InstallProgress { Percent = pct, Stage = "Copying APK to shared storage" });
            }
        }

        progress?.Report(new InstallProgress { Percent = 80, Stage = "Registering package" });
        await Task.Delay(350, ct); // simulated dexopt/package-manager work

        var catalog = InstalledAppCatalog.LoadFor(device, RequireContext().SharedRoot);
        catalog.AddOrUpdate(new InstalledApp(
            info.Package,
            info.DisplayName,
            info.VersionName,
            info.VersionCode,
            Path.GetFileName(target),
            DateTime.UtcNow,
            Path.GetFullPath(apkPath)));
        catalog.Save();

        progress?.Report(new InstallProgress { Percent = 100, Stage = "Done" });
        LogEmitted?.Invoke(device, $"installed {info.DisplayName} {info.VersionName} ({info.Package})");
    }

    public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(DeviceConfig device, CancellationToken ct = default)
    {
        var catalog = InstalledAppCatalog.LoadFor(device, RequireContext().SharedRoot);
        IReadOnlyList<InstalledApp> apps = catalog.Apps.ToList();
        return Task.FromResult(apps);
    }

    public async Task UninstallAppAsync(DeviceConfig device, string packageName, CancellationToken ct = default)
    {
        var catalog = InstalledAppCatalog.LoadFor(device, RequireContext().SharedRoot);
        var app = catalog.Find(packageName);
        if (app is not null)
        {
            var apkPath = Path.Combine(RequireContext().DeviceSdcardDir(device), "Android", "AndroLink", "apps", app.ApkFileName);
            if (File.Exists(apkPath))
            {
                try { File.Delete(apkPath); }
                catch (IOException ex) { LogEmitted?.Invoke(device, $"could not delete {apkPath}: {ex.Message}"); }
            }
        }
        await Task.Delay(250, ct);
        catalog.Remove(packageName);
        catalog.Save();
        LogEmitted?.Invoke(device, $"uninstalled {packageName}");
    }

    // The sdcard IS the Windows folder, so there is nothing to sync.
    public Task SyncFromDeviceAsync(DeviceConfig device, string androidPath, string windowsDir, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task PushToDeviceAsync(DeviceConfig device, string windowsDir, string androidPath, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<string?> ResolveAdbSerialAsync(DeviceConfig device, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    // ------------------------------------------------------------ internals

    private void SetStatus(DeviceConfig device, BackendStatus status)
    {
        _status[device.Id] = status;
        StateChanged?.Invoke(this, new DeviceStateChangedEventArgs { DeviceId = device.Id, Status = status });
    }

    private BackendContext RequireContext()
        => _context ?? throw new InvalidOperationException("backend not initialized");

    private void SeedSampleFiles(DeviceConfig device)
    {
        var sdcard = RequireContext().DeviceSdcardDir(device);
        try
        {
            var welcome = Path.Combine(sdcard, "Download", "welcome.txt");
            if (!File.Exists(welcome))
            {
                File.WriteAllText(welcome,
                    "Welcome to AndroLink!\r\n\r\n" +
                    "This file lives in the device's /sdcard/Download folder - which is this\r\n" +
                    "exact Windows folder. Edit it here and the device sees your changes;\r\n" +
                    "files the device writes show up here in Explorer.\r\n");
            }

            var pictures = Path.Combine(sdcard, "Pictures");
            Directory.CreateDirectory(pictures);
            var note = Path.Combine(pictures, "about-these-folders.txt");
            if (!File.Exists(note))
            {
                File.WriteAllText(note,
                    "DCIM, Download, Music, Movies and Pictures map 1:1 to Android's\r\n" +
                    "standard /sdcard folders. Android\\AndroLink\\apps stores installed APKs.\r\n");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("simulated", $"could not seed sample files: {ex.Message}");
        }
    }

    private void ApplyGAppsMarker(DeviceConfig device, string zipPath)
    {
        var dir = RequireContext().DeviceDataDir(device);
        Json.Save(Path.Combine(dir, "gapps-applied.json"), new GAppsMarker
        {
            ZipPath = Path.GetFullPath(zipPath),
            SizeBytes = new FileInfo(zipPath).Length,
            AppliedAt = DateTime.UtcNow,
            Note = "Demo backend: the openGApps zip was validated and staged. VM backends sideload it on first boot.",
        });
    }

    public void Dispose()
    {
        foreach (var cts in _boots.Values) cts.Cancel();
        _boots.Clear();
    }
}

public sealed class GAppsMarker
{
    public string ZipPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime AppliedAt { get; set; }
    public string Note { get; set; } = "";
}

/// <summary>Shared display helper: describes a device's Android version + ABI.</summary>
public static class SystemImageLabels
{
    /// <summary>" (Android 14 · API 34 · x86_64)" or "" when no image is set.</summary>
    public static string Describe(DeviceConfig device)
    {
        if (device.ApiLevel <= 0 && string.IsNullOrEmpty(device.Abi)) return "";
        var parts = new List<string>();
        if (device.ApiLevel > 0)
        {
            parts.Add(AndroidVersions.LabelFor(device.ApiLevel));
        }
        if (!string.IsNullOrEmpty(device.Abi))
        {
            parts.Add(device.Abi);
        }
        return parts.Count == 0 ? "" : " (" + string.Join(" \u00B7 ", parts) + ")";
    }
}
