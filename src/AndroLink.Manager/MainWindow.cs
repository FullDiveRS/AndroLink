using AndroLink.Core;
using AndroLink.Core.SystemImages;
using AndroLink.Manager.Pages;
using AndroLink.UI;

namespace AndroLink.Manager;

/// <summary>Builds and wires the main window shell: nav rail, pages, status bar, tray, drag-drop.</summary>
public static class MainWindow
{
    public static Window Create()
    {
        var theme = Theme.Current = AppServices.Manager.Settings.ThemeName == "light" ? Theme.Light() : Theme.Dark();

        var window = new Window
        {
            Title = "AndroLink",
            InitialClientSize = new Size(1200, 780),
        };

        var devicesPage = new DevicesPage();
        var appsPage = new AppsPage();
        var filesPage = new FilesPage();
        var imagesPage = new ImagesPage();
        var settingsPage = new SettingsPage();

        var nav = new NavRail(
            ("devices", "Devices", "phone"),
            ("apps", "Apps", "apps"),
            ("files", "Files", "folder"),
            ("images", "Android images", "package"),
            ("settings", "Settings", "settings"));

        var host = new Border
        {
            Background = theme.WindowBackground,
            HAlign = HAlignment.Stretch,
            VAlign = VAlignment.Stretch,
        };
        host.Child = devicesPage;

        nav.SelectionChanged += index =>
        {
            switch (index)
            {
                case 1: appsPage.Activate(); host.Child = appsPage; break;
                case 2: filesPage.Activate(); host.Child = filesPage; break;
                case 3: imagesPage.Activate(); host.Child = imagesPage; break;
                case 4: settingsPage.Activate(); host.Child = settingsPage; break;
                default: devicesPage.Activate(); host.Child = devicesPage; break;
            }
        };

        var statusText = new TextBlock(BuildStatusLine(), 11.5f) { TextColor = theme.TextSecondary };

        var statusBar = new Border(statusText)
        {
            Background = theme.Surface,
            Height = 30,
            Padding = new Thickness(16, 0),
            VAlign = VAlignment.Stretch,
        };

        var root = new DockPanel();
        DockPanel.SetDock(nav, Dock.Left);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Add(nav);
        root.Add(statusBar);
        root.Add(host);

        window.Content = root;

        // Refresh status line when devices or the preferred image change.
        AppServices.DeviceStatusChanged += (_, _) => statusText.Text = BuildStatusLine();
        AppServices.DefaultImageChanged += () => statusText.Text = BuildStatusLine();

        // APK drag-drop always routes to the Apps page.
        AppServices.Platform.EnableFileDrop(window, files =>
        {
            nav.SelectedIndex = 1;
            appsPage.HandleDroppedFiles(files);
        });

        // Tray icon.
        var icon = AppServices.Platform.ExtractAppIcon();
        AppServices.Platform.AddTrayIcon(
            "AndroLink - Android Subsystem for Windows",
            icon,
            activated: () => AppServices.Platform.ShowMainWindow(),
            quitRequested: () => window.Close());

        return window;
    }

    private static string BuildStatusLine()
    {
        var devices = AppServices.Manager.Registry.Devices;
        var images = AppServices.ImageStore.GetInstalled().Count;
        var sb = new System.Text.StringBuilder();
        sb.Append($"{devices.Count} device(s)");
        sb.Append($"   |   {images} Android image(s) downloaded");
        if (AppServices.DefaultImage is { } preferred)
        {
            sb.Append($"   |   default image: {preferred.Summary}");
        }
        sb.Append($"   |   host: {HostInfo.ArchitectureLabel}");
        sb.Append($"   |   shared storage: {AppServices.Manager.Settings.SharedRoot}");
        sb.Append("   |   files stay editable outside the app");
        return sb.ToString();
    }
}
