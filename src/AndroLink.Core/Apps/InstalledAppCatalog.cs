namespace AndroLink.Core;

/// <summary>One installed application on a device, as recorded in appcatalog.json.</summary>
public sealed record InstalledApp(
    string Package,
    string Label,
    string VersionName,
    long VersionCode,
    string ApkFileName,
    DateTime InstalledAt,
    string SourcePath)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Package : Label;
}

/// <summary>
/// Per-device index of installed apps, persisted as
/// userdata/&lt;deviceId&gt;/appcatalog.json next to the shared sdcard folder.
/// The simulated backend and the ADB backend both keep it up to date after
/// installs/uninstalls so the Manager can list apps instantly.
/// </summary>
public sealed class InstalledAppCatalog
{
    private readonly List<InstalledApp> _apps = new();
    private readonly string _filePath;

    public InstalledAppCatalog(string filePath)
    {
        _filePath = filePath;
        _apps.AddRange(Json.TryLoad<List<InstalledApp>>(filePath) ?? new List<InstalledApp>());
    }

    public IReadOnlyList<InstalledApp> Apps => _apps;

    public InstalledApp? Find(string package) => _apps.FirstOrDefault(a =>
        string.Equals(a.Package, package, StringComparison.OrdinalIgnoreCase));

    public void AddOrUpdate(InstalledApp app)
    {
        var existing = Find(app.Package);
        if (existing is not null) _apps.Remove(existing);
        _apps.Add(app);
        _apps.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
    }

    public bool Remove(string package)
    {
        var existing = Find(package);
        if (existing is null) return false;
        _apps.Remove(existing);
        return true;
    }

    public void Save() => Json.Save(_filePath, _apps);

    public static InstalledAppCatalog LoadFor(DeviceConfig device, string sharedRoot)
        => new(AndroLinkPaths.DeviceAppsCatalogFile(sharedRoot, device.Id));
}
