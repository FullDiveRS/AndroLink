namespace AndroLink.Core;

/// <summary>Outcome of an APK install attempt.</summary>
public sealed record InstallResult(ApkInfo? Info, string? Error)
{
    public bool Success => Error is null;
}

/// <summary>
/// The central orchestration facade used by the Manager app: owns the device
/// registry, the backend catalog and the shared-root settings, and routes
/// start/stop/install/file operations to the right backend while keeping the
/// appcatalog and shared folders in order.
/// </summary>
public sealed class DeviceManager : IDisposable
{
    public AppSettings Settings { get; }
    public DeviceRegistry Registry { get; }
    public BackendCatalog Backends { get; } = new();

    private readonly BackendContext _context;

    public DeviceManager(AppSettings settings)
    {
        Settings = settings;
        Registry = new DeviceRegistry();
        _context = new BackendContext { SharedRoot = settings.SharedRoot };
        Backends.InitializeAll(_context);
        Backends.BackendAdded += backend =>
        {
            backend.StateChanged += OnBackendStateChanged;
            backend.LogEmitted += (device, message) =>
                Log.Info(device.BackendId, $"{device.Name}: {message}");
        };
    }

    /// <summary>Status changes aggregated from all backends.</summary>
    public event Action<DeviceConfig, BackendStatus>? DeviceStatusChanged;

    public IRuntimeBackend? BackendFor(DeviceConfig device) => Backends.Find(device.BackendId);

    /// <summary>Registers host built-ins (simulated device, ADB bridge) before use.</summary>
    public void RegisterBuiltInBackend(IRuntimeBackend backend) => Backends.RegisterBuiltIn(backend);

    /// <summary>Loads plugin backends from the install dir and %LOCALAPPDATA% folders.</summary>
    public int LoadPlugins()
        => Backends.LoadPluginsFromDirectory(AndroLinkPaths.BundledPluginDir)
           + Backends.LoadPluginsFromDirectory(AndroLinkPaths.PluginDir);

    // ---------------------------------------------------------------- devices

    public string EnsureDeviceFolders(DeviceConfig device)
        => SharedFolderLayout.EnsureDeviceFolders(device, Settings.SharedRoot);

    public async Task StartDeviceAsync(DeviceConfig device, CancellationToken ct = default)
    {
        EnsureDeviceFolders(device);
        var backend = RequireBackend(device);
        await backend.StartAsync(device, ct);
    }

    public async Task StopDeviceAsync(DeviceConfig device, CancellationToken ct = default)
    {
        var backend = RequireBackend(device);
        await backend.StopAsync(device, ct);
    }

    public async Task<BackendStatus> GetStatusAsync(DeviceConfig device, CancellationToken ct = default)
    {
        var backend = BackendFor(device);
        if (backend is null)
        {
            return BackendStatus.Of(DeviceRunState.Error, $"Unknown backend '{device.BackendId}'");
        }
        try
        {
            return await backend.GetStatusAsync(device, ct);
        }
        catch (Exception ex)
        {
            return BackendStatus.Of(DeviceRunState.Error, ex.Message);
        }
    }

    public async Task DeleteDeviceAsync(DeviceConfig device, bool deleteSharedFiles, CancellationToken ct = default)
    {
        if (deleteSharedFiles)
        {
            var dir = AndroLinkPaths.DeviceDataDir(Settings.SharedRoot, device.Id);
            if (Directory.Exists(dir))
            {
                // Retry a few times - Explorer windows holding files can briefly lock them.
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                        break;
                    }
                    catch (IOException) when (attempt < 3)
                    {
                        await Task.Delay(300, ct);
                    }
                    catch (UnauthorizedAccessException) when (attempt < 3)
                    {
                        await Task.Delay(300, ct);
                    }
                }
            }
        }
        Registry.Delete(device.Id);
    }

    // ------------------------------------------------------------------ apps

    public async Task<InstallResult> InstallApkAsync(
        DeviceConfig device,
        string apkPath,
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default)
    {
        var backend = RequireBackend(device);
        var info = ApkInspector.Inspect(apkPath);
        if (info is null)
        {
            return new InstallResult(null, "The file is not a readable APK (missing or invalid AndroidManifest.xml).");
        }

        try
        {
            await backend.InstallAppAsync(device, apkPath, progress, ct);

            var catalog = InstalledAppCatalog.LoadFor(device, Settings.SharedRoot);
            catalog.AddOrUpdate(new InstalledApp(
                info.Package,
                info.Label,
                info.VersionName,
                info.VersionCode,
                info.FileName,
                DateTime.UtcNow,
                Path.GetFullPath(apkPath)));
            catalog.Save();

            Log.Info("manager", $"installed {info.DisplayName} ({info.Package}) on {device.Name}");
            return new InstallResult(info, null);
        }
        catch (OperationCanceledException)
        {
            return new InstallResult(info, "Install cancelled.");
        }
        catch (Exception ex)
        {
            Log.Error("manager", $"install failed on {device.Name}: {ex}");
            return new InstallResult(info, ex.Message);
        }
    }

    public async Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(DeviceConfig device, CancellationToken ct = default)
    {
        var backend = BackendFor(device);
        if (backend is null) return Array.Empty<InstalledApp>();
        try
        {
            return await backend.ListInstalledAppsAsync(device, ct);
        }
        catch (Exception ex)
        {
            Log.Warn("manager", $"listing apps on {device.Name} failed: {ex.Message}");
            return Array.Empty<InstalledApp>();
        }
    }

    public async Task UninstallAppAsync(DeviceConfig device, string packageName, CancellationToken ct = default)
    {
        var backend = RequireBackend(device);
        await backend.UninstallAppAsync(device, packageName, ct);
        var catalog = InstalledAppCatalog.LoadFor(device, Settings.SharedRoot);
        if (catalog.Remove(packageName))
        {
            catalog.Save();
        }
    }

    // ----------------------------------------------------------------- files

    public string DeviceSdcardDir(DeviceConfig device)
        => AndroLinkPaths.DeviceSdcardDir(Settings.SharedRoot, device.Id);

    public string? WindowsPathForAndroid(DeviceConfig device, string androidPath)
        => PathMapper.ToWindowsPath(device, Settings.SharedRoot, androidPath);

    public string? AndroidPathForWindows(DeviceConfig device, string windowsPath)
        => PathMapper.ToAndroidPath(device, Settings.SharedRoot, windowsPath);

    public async Task SyncFromDeviceAsync(DeviceConfig device, string androidPath, string windowsDir, CancellationToken ct = default)
    {
        var backend = RequireBackend(device);
        await backend.SyncFromDeviceAsync(device, androidPath, windowsDir, ct);
    }

    public async Task PushToDeviceAsync(DeviceConfig device, string windowsDir, string androidPath, CancellationToken ct = default)
    {
        var backend = RequireBackend(device);
        await backend.PushToDeviceAsync(device, windowsDir, androidPath, ct);
    }

    // -------------------------------------------------------------- internal

    private IRuntimeBackend RequireBackend(DeviceConfig device)
        => BackendFor(device)
           ?? throw new InvalidOperationException(
               $"No backend '{device.BackendId}' is loaded (device '{device.Name}').");

    private void OnBackendStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        var device = Registry.Find(e.DeviceId);
        if (device is null) return;
        DeviceStatusChanged?.Invoke(device, e.Status);
    }

    public void Dispose() => Backends.Dispose();
}
