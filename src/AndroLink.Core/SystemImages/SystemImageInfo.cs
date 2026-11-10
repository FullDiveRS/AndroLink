using System.Runtime.InteropServices;

namespace AndroLink.Core.SystemImages;

/// <summary>
/// One downloadable Android system image, exactly like the entries Android
/// Studio's Device Manager / SDK Manager offer: an API level ("Android
/// version"), a tag (Google APIs, Google Play, AOSP, TV, ...) and a CPU/ABI
/// (x86_64, arm64-v8a, x86, armeabi-v7a).
/// </summary>
public sealed class SystemImageInfo
{
    /// <summary>SDK package path, e.g. "system-images;android-34;google_apis;x86_64".</summary>
    public string PackagePath { get; set; } = "";

    public int ApiLevel { get; set; }

    /// <summary>Add-on tag id, e.g. "google_apis", "google_apis_playstore", "default".</summary>
    public string TagId { get; set; } = "default";

    /// <summary>Friendly tag name, e.g. "Google APIs".</summary>
    public string TagDisplay { get; set; } = "Default";

    /// <summary>CPU/ABI: x86_64, arm64-v8a, x86 or armeabi-v7a.</summary>
    public string Abi { get; set; } = "x86_64";

    /// <summary>Absolute download URL of the image zip.</summary>
    public string Url { get; set; } = "";

    public long SizeBytes { get; set; }

    /// <summary>SHA-1 of the zip, verified after download when non-empty.</summary>
    public string Sha1 { get; set; } = "";

    public int Revision { get; set; }

    /// <summary>Google's display name, e.g. "Google APIs Intel x86_64 Atom System Image".</summary>
    public string Display { get; set; } = "";

    public string VersionLabel => AndroidVersions.LabelFor(ApiLevel);

    /// <summary>"Android 14 · Google APIs · x86_64"</summary>
    public string Summary => $"{VersionLabel} · {TagDisplay} · {Abi}";

    public override string ToString() => PackagePath;
}

/// <summary>Known CPU/ABI values and host-architecture matching.</summary>
public static class CpuAbi
{
    public const string X8664 = "x86_64";
    public const string Arm64V8a = "arm64-v8a";
    public const string X86 = "x86";
    public const string ArmeabiV7a = "armeabi-v7a";

    public static readonly string[] All = { X8664, Arm64V8a, X86, ArmeabiV7a };

    public static bool IsKnown(string abi) => All.Contains(abi, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the ABI executes natively on the given host architecture.</summary>
    public static bool RunsNatively(string abi, Architecture host)
        => (host, abi?.ToLowerInvariant()) switch
        {
            (Architecture.X64, X8664) or (Architecture.X64, X86) => true,
            (Architecture.X86, X86) => true,
            (Architecture.Arm64, Arm64V8a) => true,
            (Architecture.Arm64, ArmeabiV7a) => true,
            (Architecture.Arm, ArmeabiV7a) => true,
            _ => false,
        };

    public static bool IsArm(string abi)
        => abi is not null && (abi.Equals(Arm64V8a, StringComparison.OrdinalIgnoreCase)
                               || abi.Equals(ArmeabiV7a, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Information about the PC AndroLink runs on (used to recommend ABIs and QEMU binaries).</summary>
public static class HostInfo
{
    public static Architecture CpuArchitecture => RuntimeInformation.ProcessArchitecture;

    /// <summary>"x64" / "ARM64" / ...</summary>
    public static string ArchitectureLabel => CpuArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.X86 => "x86",
        Architecture.Arm64 => "ARM64",
        Architecture.Arm => "ARM",
        _ => CpuArchitecture.ToString(),
    };

    /// <summary>The ABI that runs at native speed on this machine.</summary>
    public static string RecommendedAbi => CpuArchitecture switch
    {
        Architecture.X64 or Architecture.X86 => CpuAbi.X8664,
        Architecture.Arm64 or Architecture.Arm => CpuAbi.Arm64V8a,
        _ => CpuAbi.X8664,
    };

    /// <summary>Human explanation shown in the Images page banner.</summary>
    public static string Recommendation => CpuArchitecture switch
    {
        Architecture.Arm64 or Architecture.Arm =>
            $"This PC uses a {ArchitectureLabel} CPU - arm64-v8a images run at native speed; x86_64 images need full CPU emulation (slow).",
        _ =>
            $"This PC uses a {ArchitectureLabel} CPU - x86_64 images run at native speed; arm64-v8a images need full CPU emulation (slow).",
    };

    public static bool IsArmHost => CpuArchitecture is Architecture.Arm64 or Architecture.Arm;
}

/// <summary>API level to marketing-version mapping ("Android 14 (API 34)").</summary>
public static class AndroidVersions
{
    private static readonly (int Api, string Label)[] Table =
    {
        (36, "Android 16"),
        (35, "Android 15"),
        (34, "Android 14"),
        (33, "Android 13"),
        (32, "Android 12L"),
        (31, "Android 12"),
        (30, "Android 11"),
        (29, "Android 10"),
        (28, "Android 9 Pie"),
        (27, "Android 8.1 Oreo"),
        (26, "Android 8.0 Oreo"),
        (25, "Android 7.1 Nougat"),
        (24, "Android 7.0 Nougat"),
        (23, "Android 6.0 Marshmallow"),
        (22, "Android 5.1 Lollipop"),
        (21, "Android 5.0 Lollipop"),
    };

    public static string ShortLabel(int apiLevel)
        => Table.FirstOrDefault(t => t.Api <= apiLevel).Label ?? $"API {apiLevel}";

    public static string LabelFor(int apiLevel)
        => apiLevel <= 0 ? "Android (unspecified)" : $"{ShortLabel(apiLevel)} (API {apiLevel})";
}
