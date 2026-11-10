using AndroLink.Core;
using AndroLink.UI;

namespace AndroLink.Manager.Pages;

/// <summary>
/// Browses the selected device's shared /sdcard folder as plain Windows files.
/// This page is the proof of AndroLink's storage promise: everything shown
/// here is a real file on disk, editable while the device runs.
/// </summary>
public sealed class FilesPage : UIElement
{
    private readonly StackPanel _deviceButtons = new(Orientation.Horizontal, 8);
    private readonly TextBlock _androidPath = new("", 12.5f, UiFonts.Weight.SemiBold);
    private readonly TextBlock _windowsPath = new("", 11.5f) { TextColor = Theme.Current.TextSecondary, Wrapping = true };
    private readonly ListView _list = new() { HAlign = HAlignment.Stretch, VAlign = VAlignment.Stretch };
    private readonly Button _syncFrom = new("Sync from device") { Visible = false };
    private readonly Button _pushTo = new("Push changes to device") { Visible = false };
    private readonly TextBlock _syncStatus = new("", 11.5f) { TextColor = Theme.Current.TextSecondary };

    private DeviceConfig? _device;
    private string _currentDir = "";
    private bool _syncing;

    public FilesPage()
    {
        ClipToBounds = true;

        var header = new StackPanel(Orientation.Vertical, 4);
        header.Add(new TextBlock("Files", 21, UiFonts.Weight.Bold));
        header.Add(new TextBlock(
            "The device's /sdcard storage as plain Windows folders. Edit anything here - the device sees your changes.",
            12.5f) { TextColor = Theme.Current.TextSecondary, Wrapping = true });

        _deviceButtons.Padding = new Thickness(0, 0, 0, 12);

        var pathPanel = new StackPanel(Orientation.Vertical, 2) { Padding = new Thickness(0, 0, 0, 10) };
        pathPanel.Add(_androidPath);
        pathPanel.Add(_windowsPath);

        _list.Columns.Add(new ListViewColumn { Header = "Name", Width = 340, TextFactory = f => ((FileRow)f).Name });
        _list.Columns.Add(new ListViewColumn { Header = "Type", Width = 90, TextFactory = f => ((FileRow)f).IsDirectory ? "Folder" : "File" });
        _list.Columns.Add(new ListViewColumn { Header = "Size", Width = 110, TextFactory = f => ((FileRow)f).SizeLabel });
        _list.Columns.Add(new ListViewColumn { Header = "Modified", Width = 170, TextFactory = f => ((FileRow)f).ModifiedLabel });

        var toolbar = new StackPanel(Orientation.Horizontal, 10)
        {
            Padding = new Thickness(0, 12, 0, 4),
        };
        toolbar.Add(Button.WithIcon("chevron-right", "Up", () => NavigateUp()));
        toolbar.Add(Button.WithIcon("open-folder", "Open in Explorer", () => AppServices.Platform.RevealInFileManager(_currentDir)));
        toolbar.Add(_syncFrom);
        toolbar.Add(_pushTo);
        toolbar.Add(Spacer.Fill());
        toolbar.Add(Button.WithIcon("folder", "Open shared root", () => AppServices.Platform.RevealInFileManager(AppServices.Manager.Settings.SharedRoot)));

        var content = new StackPanel(Orientation.Vertical)
        {
            Padding = new Thickness(26, 22, 26, 16),
            HAlign = HAlignment.Stretch,
            VAlign = VAlignment.Stretch,
        };
        content.Add(header);
        content.Add(_deviceButtons);
        content.Add(pathPanel);
        content.Add(new Border(_list)
        {
            Background = Theme.Current.Surface,
            BorderBrush = Theme.Current.Border,
            BorderThickness = 1,
            CornerRadius = new CornerRadius(8),
            HAlign = HAlignment.Stretch,
            VAlign = VAlignment.Stretch,
        });
        content.Add(toolbar);
        content.Add(_syncStatus);

        Add(new Border(content) { HAlign = HAlignment.Stretch, VAlign = VAlignment.Stretch });

        _list.ItemActivated += item =>
        {
            if (item is FileRow row && row.IsDirectory) NavigateTo(Path.Combine(_currentDir, row.Name));
        };

        _syncFrom.Click += () => _ = SyncAsync(push: false);
        _pushTo.Click += () => _ = SyncAsync(push: true);
    }

    public void Activate()
    {
        RebuildDeviceButtons();
    }

    private void RebuildDeviceButtons()
    {
        _deviceButtons.ClearChildren();
        var devices = AppServices.Manager.Registry.Devices;
        if (devices.Count == 0)
        {
            _deviceButtons.Add(new TextBlock("Create a device first (Devices page).", 12.5f) { TextColor = Theme.Current.TextSecondary });
            _device = null;
            UpdatePathLabels();
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
            button.Click += () =>
            {
                _device = d;
                _currentDir = "";
                HighlightSelectedDeviceButton();
                NavigateTo(AppServices.Manager.DeviceSdcardDir(d));
            };
            _deviceButtons.Add(button);
        }

        HighlightSelectedDeviceButton();
        NavigateTo(AppServices.Manager.DeviceSdcardDir(_device));
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

    private void NavigateTo(string directory)
    {
        _currentDir = directory;
        UpdatePathLabels();
        UpdateSyncButtons();

        _list.Items.Clear();
        _list.SelectedIndex = -1;

        if (!Directory.Exists(directory))
        {
            try { Directory.CreateDirectory(directory); }
            catch { }
        }

        try
        {
            var rows = new List<FileRow>();
            var dirs = Directory.EnumerateDirectories(directory)
                .Select(d => new DirectoryInfo(d))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var dir in dirs)
            {
                rows.Add(FileRow.Directory(dir.Name, dir.LastWriteTime));
            }

            var files = Directory.EnumerateFiles(directory)
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                rows.Add(FileRow.Of(file));
            }

            foreach (var row in rows) _list.Items.Add(row);
        }
        catch (Exception ex)
        {
            _syncStatus.Text = $"Could not list folder: {ex.Message}";
        }

        _list.Refresh();
        InvalidateVisual();
    }

    private void NavigateUp()
    {
        if (_device is null) return;
        var sdcard = AppServices.Manager.DeviceSdcardDir(_device);
        var parent = Directory.GetParent(Path.GetFullPath(_currentDir));
        if (parent is null) return;

        var parentPath = parent.FullName;
        var sdcardRoot = Path.GetFullPath(sdcard).TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(parentPath, sdcardRoot, StringComparison.OrdinalIgnoreCase) ||
            parentPath.StartsWith(sdcardRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            NavigateTo(parentPath);
        }
    }

    private void UpdatePathLabels()
    {
        if (_device is null)
        {
            _androidPath.Text = "";
            _windowsPath.Text = "";
            return;
        }

        var android = AppServices.Manager.AndroidPathForWindows(_device, _currentDir) ?? PathMapper.AndroidSdcardRoot;
        _androidPath.Text = $"Android path: {android}";
        _windowsPath.Text = $"Windows folder: {_currentDir}";
    }

    private void UpdateSyncButtons()
    {
        var backend = _device is null ? null : AppServices.Manager.BackendFor(_device);
        var needsSync = backend is { Capabilities.SupportsFileSync: true, Capabilities.LiveSharedFolder: false };
        _syncFrom.Visible = needsSync;
        _pushTo.Visible = needsSync;
        _syncStatus.Text = needsSync
            ? "This backend mirrors files over ADB - use Sync/Push to move changes between the device and these folders."
            : backend is { Capabilities.LiveSharedFolder: true }
                ? "Live shared folder: edits apply immediately, no sync needed."
                : "";
    }

    private async Task SyncAsync(bool push)
    {
        if (_device is null || _syncing) return;
        _syncing = true;
        _syncStatus.Text = push ? "Pushing changes to device\u2026" : "Syncing from device\u2026";
        try
        {
            var android = AppServices.Manager.AndroidPathForWindows(_device, _currentDir)
                          ?? PathMapper.AndroidSdcardRoot;
            if (push)
            {
                await AppServices.Manager.PushToDeviceAsync(_device, _currentDir, android);
            }
            else
            {
                await AppServices.Manager.SyncFromDeviceAsync(_device, android, _currentDir);
                NavigateTo(_currentDir);
            }
            _syncStatus.Text = push ? "Push complete." : "Sync complete.";
        }
        catch (Exception ex)
        {
            _syncStatus.Text = $"Sync failed: {ex.Message}";
        }
        finally
        {
            _syncing = false;
        }
    }

    public sealed record FileRow(string Name, bool IsDirectory, long Size, DateTime Modified)
    {
        public string SizeLabel => IsDirectory ? "" : FormatSize(Size);
        public string ModifiedLabel => Modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        public static FileRow Of(FileInfo file) => new(file.Name, false, file.Length, file.LastWriteTime);
        public static FileRow Directory(string name, DateTime modified) => new(name, true, 0, modified);

        private static string FormatSize(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
            _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB",
        };
    }
}
