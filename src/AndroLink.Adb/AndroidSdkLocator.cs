namespace AndroLink.Adb;

/// <summary>
/// Locates an installed Android SDK (the same one Android Studio uses) so the
/// Emulator backend can boot downloaded system images with the official
/// emulator binary.
/// </summary>
public static class AndroidSdkLocator
{
    /// <summary>
    /// Search order: explicit path -> ANDROID_HOME -> ANDROID_SDK_ROOT ->
    /// %LOCALAPPDATA%\Android\Sdk (Android Studio default) -> Program Files
    /// fallbacks. Returns the first root that contains emulator\emulator(.exe).
    /// </summary>
    public static SdkInfo? Locate(string? configuredSdkDir = null, string[]? extraRoots = null)
    {
        foreach (var root in CandidateRoots(configuredSdkDir, extraRoots))
        {
            if (!Directory.Exists(root)) continue;
            var emulator = EmulatorPath(root);
            if (emulator is not null)
            {
                return new SdkInfo(root, emulator, AdbPath(root));
            }
        }
        return null;
    }

    public static string? EmulatorPath(string sdkRoot)
    {
        var name = OperatingSystem.IsWindows() ? "emulator.exe" : "emulator";
        var candidate = Path.Combine(sdkRoot, "emulator", name);
        return File.Exists(candidate) ? candidate : null;
    }

    public static string? AdbPath(string sdkRoot)
    {
        var name = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        var candidate = Path.Combine(sdkRoot, "platform-tools", name);
        return File.Exists(candidate) ? candidate : null;
    }

    public static IEnumerable<string> CandidateRoots(string? configuredSdkDir, string[]? extraRoots = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredSdkDir)) yield return configuredSdkDir;

        foreach (var env in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
        {
            var value = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrWhiteSpace(value)) yield return value;
        }

        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk");
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Android", "android-sdk");
        }

        if (extraRoots is not null)
        {
            foreach (var extra in extraRoots) yield return extra;
        }
    }
}

/// <summary>An located SDK: root folder plus emulator/adb executables.</summary>
public sealed record SdkInfo(string Root, string EmulatorExe, string? AdbExe)
{
    public override string ToString() => Root;
}
