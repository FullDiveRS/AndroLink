namespace AndroLink.Core;

/// <summary>
/// Well-known file system locations for AndroLink. Everything the subsystem
/// persists lives under %LOCALAPPDATA%\AndroLink, while the *user-visible*
/// emulated device storage lives in a shared root (default
/// %USERPROFILE%\AndroLink) that stays fully readable and editable outside
/// of the application.
/// </summary>
public static class AndroLinkPaths
{
    /// <summary>%LOCALAPPDATA%\AndroLink - application state.</summary>
    public static string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AndroLink");

    public static string LogsDir => Path.Combine(AppDataRoot, "logs");

    /// <summary>Directory scanned for IRuntimeBackend plugin assemblies.</summary>
    public static string PluginDir => Path.Combine(AppDataRoot, "backends");

    /// <summary>Install-directory backends folder (shipped with the product).</summary>
    public static string BundledPluginDir => Path.Combine(AppContext.BaseDirectory, "backends");

    public static string DevicesFile => Path.Combine(AppDataRoot, "devices.json");
    public static string SettingsFile => Path.Combine(AppDataRoot, "settings.json");

    /// <summary>
    /// Downloaded Android system images, laid out like an Android SDK folder
    /// (system-images/android-&lt;api&gt;/&lt;tag&gt;/&lt;abi&gt;/...) so the
    /// emulator can use it directly via ANDROID_SDK_ROOT.
    /// </summary>
    public static string SystemImagesRoot => Path.Combine(AppDataRoot, "system-images");

    /// <summary>AVD definitions created by the Emulator backend.</summary>
    public static string AvdHome => Path.Combine(AppDataRoot, "avd");

    /// <summary>Default shared root: %USERPROFILE%\AndroLink.</summary>
    public static string DefaultSharedRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AndroLink");

    public static string UserDataRoot(string sharedRoot) => Path.Combine(sharedRoot, "userdata");

    public static string DeviceDataDir(string sharedRoot, string deviceId)
        => Path.Combine(UserDataRoot(sharedRoot), deviceId);

    /// <summary>The device's emulated /sdcard as a plain Windows directory.</summary>
    public static string DeviceSdcardDir(string sharedRoot, string deviceId)
        => Path.Combine(DeviceDataDir(sharedRoot, deviceId), "sdcard");

    public static string DeviceAppsCatalogFile(string sharedRoot, string deviceId)
        => Path.Combine(DeviceDataDir(sharedRoot, deviceId), "appcatalog.json");

    public static string DeviceLogDir(string sharedRoot, string deviceId)
        => Path.Combine(UserDataRoot(sharedRoot), deviceId, "logs");
}
