namespace AndroLink.Core;

/// <summary>Context handed to every backend once, before first use.</summary>
public sealed class BackendContext
{
    public required string SharedRoot { get; init; }

    public string DeviceDataDir(DeviceConfig device) => AndroLinkPaths.DeviceDataDir(SharedRoot, device.Id);
    public string DeviceSdcardDir(DeviceConfig device) => AndroLinkPaths.DeviceSdcardDir(SharedRoot, device.Id);
    public string DeviceCatalogFile(DeviceConfig device) => AndroLinkPaths.DeviceAppsCatalogFile(SharedRoot, device.Id);
    public string DeviceLogDir(DeviceConfig device) => AndroLinkPaths.DeviceLogDir(SharedRoot, device.Id);
}

/// <summary>What a backend can do - the Manager UI adapts to this.</summary>
public sealed class DeviceCapabilities
{
    /// <summary>Backend can install APKs via <see cref="IRuntimeBackend.InstallAppAsync"/>.</summary>
    public bool SupportsInstall { get; init; } = true;

    /// <summary>Backend exposes a shell (adb-level or better).</summary>
    public bool SupportsShell { get; init; }

    /// <summary>Backend can mirror files between the device and the shared Windows folders.</summary>
    public bool SupportsFileSync { get; init; }

    /// <summary>
    /// True when the emulated device reads/writes the Windows shared folder
    /// directly (simulated device, or a VM backend with a 9p/virtfs bind mount).
    /// In that case file edits on disk are immediately visible to Android and
    /// no explicit sync is needed.
    /// </summary>
    public bool LiveSharedFolder { get; init; }

    /// <summary>Backend knows how to sideload the openGApps zip from the device's GApps hook.</summary>
    public bool SupportsGApps { get; init; }
}

/// <summary>Status snapshot for one device.</summary>
public sealed class BackendStatus
{
    public DeviceRunState State { get; init; }
    /// <summary>0-100 while starting, 100 when running.</summary>
    public int BootProgress { get; init; }
    public string? AdbSerial { get; init; }
    public string? Detail { get; init; }

    public static BackendStatus Of(DeviceRunState state, string? detail = null, int progress = 0, string? serial = null)
        => new() { State = state, Detail = detail, BootProgress = state == DeviceRunState.Running ? 100 : progress, AdbSerial = serial };
}

/// <summary>Progress report for APK installs.</summary>
public sealed class InstallProgress
{
    public int Percent { get; init; }
    /// <summary>Human-readable stage, e.g. "Pushing APK", "Installing", "Done".</summary>
    public string Stage { get; init; } = "";
}

public sealed class DeviceStateChangedEventArgs : EventArgs
{
    public required string DeviceId { get; init; }
    public required BackendStatus Status { get; init; }
}

/// <summary>
/// The AndroLink runtime plugin contract. A backend knows how to actually
/// execute an Android instance for a DeviceConfig: boot it, report status,
/// install APKs and move files. AndroLink ships three implementations:
/// the simulated device (built-in demo), the ADB bridge, and a QEMU reference
/// plugin. New engines (Hyper-V, WSL2, ...) are authored exactly like the QEMU
/// example - see docs/PLUGIN_AUTHORING.md.
/// </summary>
public interface IRuntimeBackend : IDisposable
{
    /// <summary>Stable id referenced by DeviceConfig.BackendId, e.g. "qemu".</summary>
    string Id { get; }

    /// <summary>Friendly name shown in the Manager UI.</summary>
    string DisplayName { get; }

    /// <summary>One-line description shown in backend pickers.</summary>
    string Description => "";

    DeviceCapabilities Capabilities { get; }

    /// <summary>Raised whenever a device's state changes. Must be raised on a background thread.</summary>
    event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    /// <summary>Free-form diagnostics from the backend: (device, message).</summary>
    event Action<DeviceConfig, string>? LogEmitted;

    /// <summary>Called once by the host before any other member.</summary>
    void Initialize(BackendContext context);

    Task StartAsync(DeviceConfig device, CancellationToken ct = default);
    Task StopAsync(DeviceConfig device, CancellationToken ct = default);

    Task<BackendStatus> GetStatusAsync(DeviceConfig device, CancellationToken ct = default);

    Task InstallAppAsync(DeviceConfig device, string apkPath, IProgress<InstallProgress>? progress = null, CancellationToken ct = default);

    Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(DeviceConfig device, CancellationToken ct = default);

    Task UninstallAppAsync(DeviceConfig device, string packageName, CancellationToken ct = default);

    /// <summary>
    /// Copy files from the device's storage (androidPath, e.g. /sdcard/DCIM)
    /// into the given Windows directory. Backends with <see cref="DeviceCapabilities.LiveSharedFolder"/>
    /// may simply return (the folders already ARE the device storage).
    /// </summary>
    Task SyncFromDeviceAsync(DeviceConfig device, string androidPath, string windowsDir, CancellationToken ct = default);

    /// <summary>Inverse of <see cref="SyncFromDeviceAsync"/> for edited files.</summary>
    Task PushToDeviceAsync(DeviceConfig device, string windowsDir, string androidPath, CancellationToken ct = default);

    /// <summary>ADB serial of the running device, if any (used by tooling / debug bridges).</summary>
    Task<string?> ResolveAdbSerialAsync(DeviceConfig device, CancellationToken ct = default);
}

public static class RuntimeBackendExtensions
{
    /// <summary>True when the type is a concrete IRuntimeBackend usable as a plugin.</summary>
    public static bool IsRuntimeBackendPlugin(this Type type)
        => type is { IsAbstract: false, IsInterface: false }
           && typeof(IRuntimeBackend).IsAssignableFrom(type)
           && type.GetConstructor(Type.EmptyTypes) is not null;
}
