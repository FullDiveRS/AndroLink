namespace AndroLink.Core;

/// <summary>Metadata extracted from an APK file by <see cref="ApkInspector"/>.</summary>
public sealed class ApkInfo
{
    public string Package { get; init; } = "";
    public string Label { get; init; } = "";
    public string VersionName { get; init; } = "";
    public long VersionCode { get; init; }
    public int? MinSdk { get; init; }
    public int? TargetSdk { get; init; }
    public List<string> Permissions { get; init; } = new();

    public string FileName { get; init; } = "";
    public long SizeBytes { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Package : Label;
}
