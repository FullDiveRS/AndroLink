namespace AndroLink.Core;

/// <summary>
/// The openGApps integration hook. AndroLink never bundles or downloads Google
/// apps (that would violate Google's ToS). Instead, the user drops an
/// openGApps zip they obtained themselves into the device configuration, and
/// backends that declare <see cref="DeviceCapabilities.SupportsGApps"/> apply
/// it at first boot (sideload + wipe dalvik, the standard openGApps procedure).
/// The simulated backend and ADB backend validate and log; the QEMU reference
/// backend documents the full flow.
/// </summary>
public sealed class GAppsPlan
{
    public bool Enabled => !string.IsNullOrWhiteSpace(ZipPath);

    public string? ZipPath { get; init; }

    /// <summary>openGApps variant, e.g. "pico", "nano", "stock" - informational.</summary>
    public string? Variant { get; init; }

    public static GAppsPlan FromDevice(DeviceConfig device)
        => new() { ZipPath = device.GAppsZipPath, Variant = device.BackendOptions.TryGetValue("gappsVariant", out var v) ? v : null };

    /// <summary>Returns null when the plan is usable, otherwise a human-readable problem.</summary>
    public string? Validate()
    {
        if (!Enabled) return null;
        var path = ZipPath!;
        if (!File.Exists(path)) return $"GApps zip not found: {path}";
        if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            return "GApps package must be a .zip (openGApps)";
        if (new FileInfo(path).Length < 1024 * 1024)
            return "GApps zip looks too small to be a real openGApps package";
        return null;
    }
}
