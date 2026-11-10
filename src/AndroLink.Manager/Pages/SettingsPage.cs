using AndroLink.Core;
using AndroLink.UI;
using AndroLink.Win32;

namespace AndroLink.Manager.Pages;

/// <summary>Global settings: shared root, theme, adb path, polling, diagnostics.</summary>
public sealed class SettingsPage : UIElement
{
    private readonly TextBox _sharedRoot = new();
    private readonly TextBox _adbPath = new("auto-detect (PATH / Android SDK)");
    private readonly TextBox _pollInterval = new();
    private readonly ToggleSwitch _themeSwitch;
    private readonly ToggleSwitch _traySwitch;

    public SettingsPage()
    {
        ClipToBounds = true;
        var settings = AppServices.Manager.Settings;

        _themeSwitch = new ToggleSwitch("Use light theme", settings.ThemeName == "light");
        _traySwitch = new ToggleSwitch("Minimize to tray", settings.MinimizeToTray);

        _sharedRoot.Text = settings.SharedRoot;
        _adbPath.Text = settings.AdbPath ?? "";
        _pollInterval.Text = settings.DevicePollIntervalMs.ToString();

        var grid = new GridPanel()
            .AddColumn(230)
            .AddColumn(LayoutLength.Star())
            .AddColumn(120);

        int row = 0;
        AddRow(grid, row++, "Shared storage root", _sharedRoot, BrowseSharedRoot);
        AddRow(grid, row++, "adb.exe path (optional)", _adbPath, BrowseAdb);
        AddRow(grid, row++, "Device poll interval (ms)", _pollInterval, null);
        AddToggleRow(grid, row++, _themeSwitch);
        AddToggleRow(grid, row++, _traySwitch);

        var buttons = new StackPanel(Orientation.Horizontal, 10) { Padding = new Thickness(0, 18, 0, 0) };
        buttons.Add(Button.WithIcon("check", "Save settings", Save, primary: true));
        buttons.Add(Button.WithIcon("open-folder", "Open logs folder", OpenLogs));
        buttons.Add(Button.WithIcon("open-folder", "Open data folder", () => AppServices.Platform.RevealInFileManager(AndroLinkPaths.AppDataRoot)));
        buttons.Add(Button.WithIcon("info", "About AndroLink", ShowAbout));

        var header = new StackPanel(Orientation.Vertical, 4);
        header.Add(new TextBlock("Settings", 21, UiFonts.Weight.Bold));
        header.Add(new TextBlock(
            "Shared storage holds every device's /sdcard as ordinary Windows files. Changing the root affects new device folders (existing devices move when you recreate them).",
            12.5f) { TextColor = Theme.Current.TextSecondary, Wrapping = true });

        var note = new TextBlock(
            "Theme and tray changes apply immediately after saving.",
            11.5f) { TextColor = Theme.Current.TextSecondary, Padding = new Thickness(0, 10, 0, 0) };

        var content = new StackPanel(Orientation.Vertical)
        {
            Padding = new Thickness(26, 22, 26, 16),
            HAlign = HAlignment.Stretch,
        };
        content.Add(header);
        content.Add(new Border(grid) { Padding = new Thickness(0, 14, 0, 0) });
        content.Add(buttons);
        content.Add(note);

        Add(new Border(content) { HAlign = HAlignment.Stretch, VAlign = VAlignment.Stretch });
    }

    public void Activate() { /* fields already reflect current settings */ }

    private static void AddRow(GridPanel grid, int row, string label, TextBox input, Action? browse)
    {
        grid.SetCell(LabelCell(label), 0, row);
        grid.SetCell(input, 1, row);
        if (browse is not null)
        {
            grid.SetCell(new Border(new Button("Browse\u2026", browse) { Width = 96 })
            {
                Padding = new Thickness(8, 0, 0, 0),
                VAlign = VAlignment.Center,
            }, 2, row);
        }
        else
        {
            grid.SetCell(new Border { Padding = new Thickness(8, 0, 0, 0) }, 2, row);
        }
        grid.AddRow(44);
    }

    private static void AddToggleRow(GridPanel grid, int row, ToggleSwitch toggle)
    {
        grid.SetCell(toggle, 1, row);
        grid.SetCell(new Border(), 0, row);
        grid.SetCell(new Border(), 2, row);
        grid.AddRow(40);
    }

    private static UIElement LabelCell(string text)
        => new TextBlock(text, 13f) { HAlign = HAlignment.Left, VAlign = VAlignment.Center };

    private void BrowseSharedRoot()
    {
        var dir = AppServices.Platform.PickFolder("Choose the shared storage root folder");
        if (!string.IsNullOrEmpty(dir)) _sharedRoot.Text = dir;
    }

    private void BrowseAdb()
    {
        var file = AppServices.Platform.PickOpenFile("Locate adb.exe", "adb (adb.exe)|adb.exe|All files (*.*)|*.*");
        if (!string.IsNullOrEmpty(file)) _adbPath.Text = file;
    }

    private void Save()
    {
        var settings = AppServices.Manager.Settings;
        var oldTheme = settings.ThemeName;

        settings.SharedRoot = string.IsNullOrWhiteSpace(_sharedRoot.Text) ? AndroLinkPaths.DefaultSharedRoot : _sharedRoot.Text.Trim();
        settings.AdbPath = string.IsNullOrWhiteSpace(_adbPath.Text) ? null : _adbPath.Text.Trim();
        if (int.TryParse(_pollInterval.Text, out var interval))
        {
            settings.DevicePollIntervalMs = Math.Clamp(interval, 500, 60_000);
        }
        settings.ThemeName = _themeSwitch.IsOn ? "light" : "dark";
        settings.MinimizeToTray = _traySwitch.IsOn;
        settings.Save();

        if (oldTheme != settings.ThemeName)
        {
            Theme.Current = settings.ThemeName == "light" ? Theme.Light() : Theme.Dark();
            AppServices.MainWindow?.InvalidateVisual();
        }
        Win32WindowHost.MinimizeToTray = settings.MinimizeToTray;

        _ = MessageBox.Show(AppServices.MainWindow!, "Settings saved",
            "Settings saved.\n\nNote: the shared root change applies to newly created device folders.",
            "OK");
    }

    private void OpenLogs() => AppServices.Platform.RevealInFileManager(AndroLinkPaths.LogsDir);

    private void ShowAbout()
    {
        _ = MessageBox.Show(AppServices.MainWindow!, "About AndroLink",
            "AndroLink v0.1.0\nAndroid Subsystem for Windows\n\n" +
            "Runs Android devices through pluggable runtime backends, installs APKs and keeps the emulated /sdcard storage as plain Windows files you can open and edit any time.\n\n" +
            "Backends loaded:\n" +
            string.Join("\n", AppServices.Manager.Backends.All.Select(b => $"  - {b.DisplayName} ({b.Id})")),
            "OK");
    }
}
