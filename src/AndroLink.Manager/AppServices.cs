using AndroLink.Adb;
using AndroLink.Backend.Simulated;
using AndroLink.Core;
using AndroLink.Core.SystemImages;
using AndroLink.UI;
using AndroLink.Win32;

namespace AndroLink.Manager;

/// <summary>Composition root shared by every page: platform, device manager, polling.</summary>
public static class AppServices
{
    public static Win32Platform Platform { get; private set; } = null!;
    public static DeviceManager Manager { get; private set; } = null!;
    public static SystemImageStore ImageStore { get; private set; } = null!;
    public static Window? MainWindow { get; set; }

    private static System.Threading.Timer? _pollTimer;
    private static int _polling;
    private static IReadOnlyList<SystemImageInfo>? _catalog;

    /// <summary>Status updates from backends AND the periodic poller (backend thread).</summary>
    public static event Action<DeviceConfig, BackendStatus>? DeviceStatusChanged;

    public static void Initialize()
    {
        Log.SetLogFile(AndroLinkPaths.LogsDir, "androlink-manager");
        var settings = AppSettings.Load();

        Platform = new Win32Platform();
        Manager = new DeviceManager(settings);
        ImageStore = new SystemImageStore();

        Manager.RegisterBuiltInBackend(new SimulatedBackend());
        Manager.RegisterBuiltInBackend(new EmulatorBackend(ImageStore));
        Manager.RegisterBuiltInBackend(new AdbBackend());
        Manager.LoadPlugins();

        Manager.DeviceStatusChanged += (device, status) => DeviceStatusChanged?.Invoke(device, status);
        Win32WindowHost.MinimizeToTray = settings.MinimizeToTray;

        Log.Info("manager", $"AndroLink started; shared root = {settings.SharedRoot}; host = {HostInfo.ArchitectureLabel}");
    }

    /// <summary>
    /// The cached downloadable image catalog. Falls back to the built-in
    /// snapshot; call <see cref="RefreshCatalogAsync"/> to go online.
    /// </summary>
    public static IReadOnlyList<SystemImageInfo> ImageCatalog
        => _catalog ?? SystemImageCatalog.DefaultCatalog();

    public static bool CatalogIsOnline { get; private set; }

    public static event Action? CatalogChanged;

    /// <summary>Raised after the user changes the preferred downloaded image.</summary>
    public static event Action? DefaultImageChanged;

    /// <summary>
    /// The user's preferred downloaded system image (chosen on the Android
    /// images page); new devices preselect it. Null when nothing is picked.
    /// </summary>
    public static SystemImageInfo? DefaultImage
    {
        get
        {
            var package = Manager.Settings.DefaultImagePackage;
            return string.IsNullOrEmpty(package) ? null : ImageStore.FindInstalled(package);
        }
    }

    /// <summary>Sets (or clears with null) the preferred image and persists the choice.</summary>
    public static void SetDefaultImage(SystemImageInfo? image)
    {
        Manager.Settings.DefaultImagePackage = image?.PackagePath;
        Manager.Settings.Save();
        Log.Info("images", image is null
            ? "default system image cleared"
            : $"default system image set to {image.PackagePath}");
        DefaultImageChanged?.Invoke();
    }

    public static async Task RefreshCatalogAsync(CancellationToken ct = default)
    {
        var fresh = await SystemImageCatalog.FetchWithFallbackAsync(ct: ct);
        _catalog = fresh;
        CatalogIsOnline = !ReferenceEquals(fresh, SystemImageCatalog.DefaultCatalog())
                          || fresh.Count != SystemImageCatalog.DefaultCatalog().Count;
        CatalogChanged?.Invoke();
    }

    /// <summary>Runs an action on the UI thread (safe from backend threads).</summary>
    public static void Post(Action action) => Platform.Post(action);

    /// <summary>Periodically verifies device states so the UI stays truthful.</summary>
    public static void StartPolling()
    {
        var interval = Math.Clamp(Manager.Settings.DevicePollIntervalMs, 500, 60_000);
        _pollTimer = new System.Threading.Timer(async _ =>
        {
            if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0) return;
            try
            {
                foreach (var device in Manager.Registry.Devices.ToList())
                {
                    var status = await Manager.GetStatusAsync(device);
                    DeviceStatusChanged?.Invoke(device, status);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("poll", $"poll cycle failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        }, null, interval, interval);
    }
}
