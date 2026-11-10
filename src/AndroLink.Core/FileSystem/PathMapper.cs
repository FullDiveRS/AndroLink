namespace AndroLink.Core;

/// <summary>
/// Maps Android paths to Windows paths and back (Mixed storage mode).
///
/// The device's system image stays read-only inside the backend, but
/// <c>/sdcard</c> is bound to a plain Windows folder:
///
///     /sdcard  ->  &lt;SharedRoot&gt;\userdata\&lt;deviceId&gt;\sdcard
///
/// So "/sdcard/DCIM/photo.jpg" is simply "%USERPROFILE%\AndroLink\userdata\
/// dev-xxxxxxxx\sdcard\DCIM\photo.jpg" on disk - editable in Explorer, VS Code
/// or anything else, even while the device is running. Anything outside
/// /sdcard is intentionally not mapped (that storage is backend-internal).
/// </summary>
public static class PathMapper
{
    public const string AndroidSdcardRoot = "/sdcard";

    public static string NormalizeAndroidPath(string path)
    {
        var p = path.Trim().Replace('\\', '/');
        // collapse duplicate separators
        while (p.Contains("//")) p = p.Replace("//", "/");
        return p.Length > 1 ? p.TrimEnd('/') : p;
    }

    /// <summary>True when rel is a clean relative path with no traversal or invalid characters.</summary>
    public static bool IsSafeRelative(string rel)
    {
        if (rel.Length == 0) return true; // "" == the /sdcard root itself
        if (rel.Contains("..") || rel.StartsWith('/')) return false;
        var invalid = Path.GetInvalidPathChars();
        return rel.All(c => !invalid.Contains(c));
    }

    /// <summary>
    /// Windows path for an Android path, or null when the path is outside the
    /// shared /sdcard mapping.
    /// </summary>
    public static string? ToWindowsPath(DeviceConfig device, string sharedRoot, string androidPath)
    {
        var rel = RelativeUnderSdcard(androidPath);
        if (rel is null) return null;
        if (rel.Length > 0 && !IsSafeRelative(rel)) return null;
        var sdcard = AndroLinkPaths.DeviceSdcardDir(sharedRoot, device.Id);
        return rel.Length == 0 ? sdcard : Path.Combine(sdcard, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// Android path for a Windows path under the device's shared folder, or
    /// null when the Windows path is outside it.
    /// </summary>
    public static string? ToAndroidPath(DeviceConfig device, string sharedRoot, string windowsPath)
    {
        var sdcard = Path.GetFullPath(AndroLinkPaths.DeviceSdcardDir(sharedRoot, device.Id))
            .TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(windowsPath).TrimEnd(Path.DirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!string.Equals(full, sdcard, comparison))
        {
            if (!full.StartsWith(sdcard + Path.DirectorySeparatorChar, comparison)) return null;
        }

        var rel = full.Length == sdcard.Length
            ? string.Empty
            : full[(sdcard.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
        return AndroidSdcardRoot + (rel.Length == 0 ? "" : "/" + rel);
    }

    /// <summary>Path relative to /sdcard ("DCIM/photo.jpg"), or null when outside /sdcard.</summary>
    public static string? RelativeUnderSdcard(string androidPath)
    {
        var p = NormalizeAndroidPath(androidPath);
        if (!p.StartsWith(AndroidSdcardRoot, StringComparison.OrdinalIgnoreCase)) return null;
        // Boundary check: "/sdcardx" is NOT under "/sdcard".
        if (p.Length > AndroidSdcardRoot.Length && p[AndroidSdcardRoot.Length] != '/') return null;
        var rest = p.Length == AndroidSdcardRoot.Length
            ? string.Empty
            : p[AndroidSdcardRoot.Length..].TrimStart('/');
        if (rest.Length > 0 && !IsSafeRelative(rest)) return null;
        return rest;
    }
}
