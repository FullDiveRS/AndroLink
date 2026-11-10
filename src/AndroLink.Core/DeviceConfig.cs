namespace AndroLink.Core;

/// <summary>Lifecycle state of a single emulated device.</summary>
public enum DeviceRunState
{
    Stopped = 0,
    Starting = 1,
    Running = 2,
    Error = 3,
}

/// <summary>
/// Persistent definition of one emulated Android device. Instances are stored
/// in %LOCALAPPDATA%\AndroLink\devices.json; the heavy state (files, apps)
/// lives in the shared userdata folders instead, so users can manipulate it
/// with any Windows tool.
/// </summary>
public sealed class DeviceConfig
{
    public string Id { get; set; } = NewId();

    public string Name { get; set; } = "New device";

    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int Dpi { get; set; } = 240;

    public int RamMb { get; set; } = 2048;
    public int CpuCores { get; set; } = 2;

    /// <summary>Id of the IRuntimeBackend that executes this device (e.g. "adb", "simulated", "qemu").</summary>
    public string BackendId { get; set; } = "simulated";

    /// <summary>Optional disk image / ISO path - meaning is backend-specific (e.g. Android-x86 image for QEMU).</summary>
    public string? ImagePath { get; set; }

    /// <summary>Android API level this device targets (0 = backend default).</summary>
    public int ApiLevel { get; set; }

    /// <summary>CPU/ABI of the guest: x86_64, arm64-v8a, x86 or armeabi-v7a. Empty = host default.</summary>
    public string Abi { get; set; } = "";

    /// <summary>
    /// Package path of an installed system image ("system-images;android-34;google_apis;x86_64").
    /// Backends that run real Android (emulator/QEMU) boot this image; the
    /// simulated backend reports it as the device's Android version.
    /// </summary>
    public string? ImagePackage { get; set; }

    /// <summary>
    /// openGApps integration hook: when set, capable backends sideload this zip
    /// on first boot (see GAppsPlan). AndroLink itself never ships Google apps.
    /// </summary>
    public string? GAppsZipPath { get; set; }

    /// <summary>Free-form, backend-specific options (serial numbers, launch commands, ...).</summary>
    public Dictionary<string, string> BackendOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public override string ToString() => $"{Name} ({BackendId})";

    public static string NewId()
    {
        var raw = Guid.NewGuid().ToString("N");
        return "dev-" + raw[..12];
    }
}
