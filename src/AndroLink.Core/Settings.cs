namespace AndroLink.Core;

/// <summary>Global, user-editable settings persisted to settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Root folder that holds the per-device shared storage (userdata/&lt;id&gt;/sdcard).</summary>
    public string SharedRoot { get; set; } = AndroLinkPaths.DefaultSharedRoot;

    /// <summary>"dark" or "light".</summary>
    public string ThemeName { get; set; } = "dark";

    /// <summary>Path to adb.exe; when null the ADB backend probes PATH and SDK locations.</summary>
    public string? AdbPath { get; set; }

    /// <summary>How often the Manager polls device status, in milliseconds.</summary>
    public int DevicePollIntervalMs { get; set; } = 2000;

    /// <summary>Minimize to the notification-area tray icon instead of the taskbar.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Package path of the downloaded system image the user picked as the
    /// preferred Android version ("Set default" on the Android images page).
    /// New devices preselect it in the Create device dialog.
    /// </summary>
    public string? DefaultImagePackage { get; set; }

    public static AppSettings Load() => Json.Load<AppSettings>(AndroLinkPaths.SettingsFile);

    public void Save() => Json.Save(AndroLinkPaths.SettingsFile, this);
}
