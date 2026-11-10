using AndroLink.Core;
using AndroLink.UI;

namespace AndroLink.Manager.Pages;

/// <summary>APK installation and installed-app management for the selected device.</summary>
public sealed class AppsPage : UIElement
{
    private readonly StackPanel _deviceButtons = new(Orientation.Horizontal, 8);
    private DeviceConfig? _device;

    private readonly ListView _apps = new() { HAlign = HAlignment.Stretch, VAlign = VAlignment.Stretch };
    private readonly ProgressBar _progress = new() { BarHeight = 6f, Visible = false };
    private readonly TextBlock _progressStage = new("", 12f) { TextColor = Theme.Current.TextSecondary, Visible = false };
    private readonly TextBlock _dropHint = new("", 12f) { Wrapping = true };
    private Button? _uninstallButton;
    private bool _installing;

    public AppsPage()
    {
        ClipToBounds = true;

        var header = new StackPanel(Orientation.Vertical, 4);
        header.Add(new TextBlock("Apps", 21, UiFonts.Weight.Bold));
        header.Add(new TextBlock(
            "Install APKs by picking a file or dragging it onto the window. Installed apps are listed per device.",
            12.5f) { TextColor = Theme.Current.TextSecondary, Wrapping = true });

        _deviceButtons.Padding = new Thickness(0, 0, 0, 12);

        _apps.Columns.Add(new ListViewColumn
        {
            Header = "App",
            Width = 240,
            TextFactory = app => app is InstalledApp a ? a.DisplayName : "",
        });
        _apps.Columns.Add(new ListViewColumn
        {
            Header = "Package",
            Width = 300,
            TextFactory = app => app is InstalledApp a ? a.Package : "",
        });
        _apps.Columns.Add(new ListViewColumn
        {
            Header = "Version",
            Width = 120,
            TextFactory = app => app is InstalledApp a ? a.VersionName : "",
        });
        _apps.Columns.Add(new ListViewColumn
        {
            Header = "Installed",
            Width = 170,
            TextFactory = app => app is InstalledApp a ? a.InstalledAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "",
        });

        var toolbar = new StackPanel(Orientation.Horizontal, 10)
        {
            Padding = new Thickness(0, 12, 0, 4),
        };
        toolbar.Add(Button.WithIcon("package", "Install APK\u2026", PickAndInstall, primary: true));
        _uninstallButton = new Button("Uninstall", UninstallSelected) { IconName = "trash", IsDanger = true };
        toolbar.Add(_uninstallButton);
        toolbar.Add(Button.WithIcon("open-folder", "Open apps folder", OpenAppsFolder));
        toolbar.Add(Spacer.Fill());

        _progressStage.Padding = new Thickness(0, 4, 0, 0);
        _dropHint.TextColor = Theme.Current.TextSecondary;
        _dropHint.Padding = new Thickness(0, 10, 0, 0);
        UpdateDropHint();

        var content = new StackPanel(Orientation.Vertical)
        {
            Padding = new Thickness(26, 22, 26, 16),
            HAlign = HAlignment.Stretch,
            VAlign = VAlignment.Stretch,
        };
        content.Add(header);
        content.Add(_deviceButtons);
        content.Add(new Border(_apps)
        {
            Background = Theme.Current.Surface,
            BorderBrush = Theme.Current.Border,
            BorderThickness = 1,
            CornerRadius = new CornerRadius(8),
            HAlign = HAlignment.Stretch,
            VAlign = VAlignment.Stretch,
        });
        content.Add(toolbar);
        content.Add(_progress);
        content.Add(_progressStage);
        content.Add(_dropHint);

        Add(new Border(content)
        {
            HAlign = HAlignment.Stretch,
            VAlign = VAlignment.Stretch,
        });

        _apps.SelectionChanged += () => { /* future: enable/disable per selection */ };
    }

    /// <summary>Called when the page becomes visible.</summary>
    public void Activate()
    {
        RebuildDeviceButtons();
        RefreshApps();
    }

    private void RebuildDeviceButtons()
    {
        _deviceButtons.ClearChildren();
        var devices = AppServices.Manager.Registry.Devices;
        if (devices.Count == 0)
        {
            _deviceButtons.Add(new TextBlock("Create a device first (Devices page).", 12.5f) { TextColor = Theme.Current.TextSecondary });
            _device = null;
            return;
        }

        if (_device is null || devices.All(d => d.Id != _device.Id))
        {
            _device = devices[0];
        }

        foreach (var device in devices)
        {
            var d = device;
            var button = new Button(d.Name) { Tag = d.Id };
            button.Click += () => { _device = d; RefreshApps(); };
            _deviceButtons.Add(button);
        }
        HighlightSelectedDeviceButton();
        RefreshApps();
    }

    private void HighlightSelectedDeviceButton()
    {
        foreach (var child in _deviceButtons.Children)
        {
            if (child is Button button && button.Tag is string id)
            {
                button.IsPrimary = _device is not null && id == _device.Id;
            }
        }
        _deviceButtons.InvalidateVisual();
    }

    public void RefreshApps()
    {
        _apps.Items.Clear();
        _apps.SelectedIndex = -1;
        _apps.Refresh();

        if (_device is null) return;
        var device = _device;
        HighlightSelectedDeviceButton();

        _ = Task.Run(async () =>
        {
            var apps = await AppServices.Manager.ListInstalledAppsAsync(device);
            UIApplication.Post(() =>
            {
                if (!ReferenceEquals(_device, device)) return; // user switched meanwhile
                _apps.Items.Clear();
                foreach (var app in apps) _apps.Items.Add(app);
                _apps.Refresh();
            });
        });
    }

    public void HandleDroppedFiles(string[] files)
    {
        var apk = files.FirstOrDefault(f => f.EndsWith(".apk", StringComparison.OrdinalIgnoreCase));
        if (apk is null)
        {
            _ = MessageBox.Show(AppServices.MainWindow!, "Not an APK",
                "Drop .apk files to install them.", "OK");
            return;
        }
        InstallInternal(apk);
    }

    private async void PickAndInstall()
    {
        var apk = AppServices.Platform.PickOpenFile(
            "Choose an Android package (.apk)",
            "Android packages (*.apk)|*.apk|All files (*.*)|*.*");
        if (apk is null) return;
        await InstallInternalAsync(apk);
    }

    private void InstallInternal(string apk) => _ = InstallInternalAsync(apk);

    private async Task InstallInternalAsync(string apkPath)
    {
        if (_device is null)
        {
            await MessageBox.Show(AppServices.MainWindow!, "No device", "Create and select a device first.", "OK");
            return;
        }
        if (_installing) return;

        var info = ApkInspector.Inspect(apkPath);
        if (info is null)
        {
            await MessageBox.Show(AppServices.MainWindow!, "Not an APK",
                "This file has no readable AndroidManifest.xml and cannot be installed.", "OK");
            return;
        }

        var confirm = await MessageBox.Show(
            AppServices.MainWindow!,
            "Install APK",
            $"App:      {info.DisplayName}\nPackage:  {info.Package}\nVersion:  {info.VersionName} ({info.VersionCode})\nSize:     {info.SizeBytes / 1024.0 / 1024.0:F1} MB\nDevice:   {_device.Name}",
            "Cancel", "Install");

        if (confirm != 1) return;

        _installing = true;
        _progress.Visible = true;
        _progressStage.Visible = true;
        _progress.Value = 0;
        _progressStage.Text = "Preparing\u2026";

        var progress = new Progress<InstallProgress>(p =>
        {
            _progress.IsIndeterminate = false;
            _progress.Value = p.Percent;
            _progressStage.Text = $"{p.Stage} \u2014 {p.Percent}%";
        });

        try
        {
            var result = await AppServices.Manager.InstallApkAsync(_device, apkPath, progress);
            if (result.Success)
            {
                _progressStage.Text = $"Installed {result.Info!.DisplayName} on {_device.Name}.";
            }
            else
            {
                _progress.Visible = false;
                _progressStage.Visible = false;
                await MessageBox.Show(AppServices.MainWindow!, "Install failed", result.Error ?? "Unknown error", "OK");
            }
        }
        catch (Exception ex)
        {
            _progress.Visible = false;
            _progressStage.Visible = false;
            await MessageBox.Show(AppServices.MainWindow!, "Install failed", ex.Message, "OK");
        }
        finally
        {
            _installing = false;
            if (_progressStage.Text.StartsWith("Installed", StringComparison.Ordinal))
            {
                _ = Task.Delay(2500).ContinueWith(_ => UIApplication.Post(() =>
                {
                    _progress.Visible = false;
                    _progressStage.Visible = false;
                    InvalidateVisual();
                }));
            }
            RefreshApps();
        }
    }

    private async void UninstallSelected()
    {
        if (_device is null) return;
        if (_apps.SelectedItem is not InstalledApp app)
        {
            await MessageBox.Show(AppServices.MainWindow!, "No selection", "Select an installed app first.", "OK");
            return;
        }

        var confirm = await MessageBox.Show(AppServices.MainWindow!, "Uninstall app",
            $"Uninstall {app.DisplayName} ({app.Package}) from {_device.Name}?", "Cancel", "Uninstall");
        if (confirm != 1) return;

        try
        {
            await AppServices.Manager.UninstallAppAsync(_device, app.Package);
        }
        catch (Exception ex)
        {
            await MessageBox.Show(AppServices.MainWindow!, "Uninstall failed", ex.Message, "OK");
        }
        RefreshApps();
    }

    private void OpenAppsFolder()
    {
        if (_device is null) return;
        var dir = Path.Combine(AppServices.Manager.DeviceSdcardDir(_device), "Android", "AndroLink", "apps");
        Directory.CreateDirectory(dir);
        AppServices.Platform.RevealInFileManager(dir);
    }

    private void UpdateDropHint()
    {
        _dropHint.Text = "Tip: drag .apk files anywhere onto the AndroLink window to install them. " +
                         "APK copies land in the device's shared folder under Android\\AndroLink\\apps.";
    }
}
