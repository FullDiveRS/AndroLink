using AndroLink.Core;
using AndroLink.Core.SystemImages;
using AndroLink.Manager.Dialogs;
using AndroLink.UI;
using SkiaSharp;

namespace AndroLink.Manager.Pages;

/// <summary>Device management: cards with live state, create/delete, start/stop.</summary>
public sealed class DevicesPage : UIElement
{
    private readonly StackPanel _cards = new(Orientation.Vertical, 10);
    private readonly Dictionary<string, DeviceCard> _cardByDevice = new(StringComparer.OrdinalIgnoreCase);

    public DevicesPage()
    {
        ClipToBounds = true;

        var toolbar = new StackPanel(Orientation.Horizontal, 10)
        {
            Padding = new Thickness(0, 0, 0, 14),
        };
        toolbar.Add(Button.WithIcon("plus", "Create device", CreateDevice, primary: true));
        toolbar.Add(Button.WithIcon("play", "Start", StartSelected));
        toolbar.Add(Button.WithIcon("stop", "Stop", StopSelected));
        toolbar.Add(new Button("Delete", DeleteSelected) { IconName = "trash", IsDanger = true });
        toolbar.Add(Spacer.Fill());

        var header = new StackPanel(Orientation.Vertical, 4)
        {
            Padding = new Thickness(0, 0, 0, 16),
        };
        header.Add(new TextBlock("Devices", 21, UiFonts.Weight.Bold));
        header.Add(new TextBlock(
            "Each device keeps its /sdcard in a real Windows folder under the shared root - open it in Explorer any time.",
            12.5f) { TextColor = Theme.Current.TextSecondary, Wrapping = true });

        var scroll = new ScrollViewer { HAlign = HAlignment.Stretch, VAlign = VAlignment.Stretch };
        scroll.Content = _cards;

        var content = new StackPanel(Orientation.Vertical)
        {
            Padding = new Thickness(26, 22, 26, 16),
            HAlign = HAlignment.Stretch,
            VAlign = VAlignment.Stretch,
        };
        content.Add(header);
        content.Add(toolbar);
        content.Add(scroll);

        // Centered page column keeps the layout balanced on wide windows.
        var centered = new Border(content)
        {
            HAlign = HAlignment.Center,
            VAlign = VAlignment.Stretch,
            MaxWidth = 1120,
        };
        Add(centered);

        AppServices.DeviceStatusChanged += OnDeviceStatusChanged;
        Rebuild();
    }

    public void Activate() => Rebuild();

    private DeviceConfig? SelectedDevice
    {
        get
        {
            var card = _cards.Children.OfType<DeviceCard>().FirstOrDefault(c => c.IsSelected);
            return card is null ? null : AppServices.Manager.Registry.Find(card.DeviceId);
        }
    }

    private async Task StartDevice(DeviceConfig? device)
    {
        if (device is null)
        {
            await MessageBox.Show(AppServices.MainWindow!, "No selection", "Select a device first.", "OK");
            return;
        }
        try
        {
            await AppServices.Manager.StartDeviceAsync(device);
        }
        catch (Exception ex)
        {
            await MessageBox.Show(AppServices.MainWindow!, "Start failed", ex.Message, "OK");
        }
    }

    private async Task StopDevice(DeviceConfig? device)
    {
        if (device is null) return;
        try
        {
            await AppServices.Manager.StopDeviceAsync(device);
        }
        catch (Exception ex)
        {
            await MessageBox.Show(AppServices.MainWindow!, "Stop failed", ex.Message, "OK");
        }
    }

    public void Rebuild()
    {
        _cards.ClearChildren();
        _cardByDevice.Clear();

        var devices = AppServices.Manager.Registry.Devices;
        if (devices.Count == 0)
        {
            var emptyColumn = new StackPanel(Orientation.Vertical, 10) { HAlign = HAlignment.Center, VAlign = VAlignment.Center };
            emptyColumn.Add(new Icon("android", 40) { Tint = Theme.Current.TextSecondary, HAlign = HAlignment.Center });
            emptyColumn.Add(new TextBlock(
                "No devices yet.",
                14.5f, UiFonts.Weight.SemiBold) { Align = TextAlignment.Center, HAlign = HAlignment.Center });
            emptyColumn.Add(new TextBlock(
                "Click 'Create device' to add your first emulated Android device -\nor download an Android version from the 'Android images' page.",
                12.5f) { TextColor = Theme.Current.TextSecondary, Align = TextAlignment.Center, HAlign = HAlignment.Center, Wrapping = true });

            var empty = new Border(emptyColumn)
            {
                Background = Theme.Current.Surface,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(22, 26),
                HAlign = HAlignment.Stretch,
            };
            _cards.Add(empty);
            return;
        }

        foreach (var device in devices)
        {
            var card = new DeviceCard(device);
            card.StartRequested += () => _ = StartDevice(device);
            card.StopRequested += () => _ = StopDevice(device);
            _cardByDevice[device.Id] = card;
            _cards.Add(card);
        }
        InvalidateMeasure();
    }

    private void OnDeviceStatusChanged(DeviceConfig device, BackendStatus status)
    {
        if (_cardByDevice.TryGetValue(device.Id, out var card))
        {
            card.UpdateStatus(status);
        }
    }

    private async void CreateDevice()
    {
        var created = await CreateDeviceDialog.ShowAsync();
        if (created is null) return;
        AppServices.Manager.Registry.Update(created);
        AppServices.Manager.EnsureDeviceFolders(created);
        Rebuild();
    }

    private async void StartSelected()
    {
        var device = SelectedDevice;
        if (device is null)
        {
            await MessageBox.Show(AppServices.MainWindow!, "No selection", "Select a device first.", "OK");
            return;
        }
        await StartDevice(device);
    }

    private void StopSelected() => _ = StopDevice(SelectedDevice);

    private async void DeleteSelected()
    {
        var device = SelectedDevice;
        if (device is null) return;
        var choice = await MessageBox.Show(
            AppServices.MainWindow!,
            "Delete device",
            $"Delete '{device.Name}'?\n\nIts shared folder under\n{AppServices.Manager.DeviceSdcardDir(device)}\nwill also be deleted.",
            "Cancel", "Delete device and files");
        if (choice != 1) return;

        try
        {
            await AppServices.Manager.DeleteDeviceAsync(device, deleteSharedFiles: true);
        }
        catch (Exception ex)
        {
            await MessageBox.Show(AppServices.MainWindow!, "Delete failed", ex.Message, "OK");
        }
        Rebuild();
    }

    // ------------------------------------------------------------- card

    private sealed class DeviceCard : Border
    {
        public string DeviceId { get; }
        public bool IsSelected { get; private set; }

        private readonly TextBlock _name = new("", 14.5f, UiFonts.Weight.SemiBold);
        private readonly TextBlock _state = new("Stopped", 12f);
        private readonly TextBlock _meta = new("", 11.5f) { Wrapping = true, TextColor = Theme.Current.TextSecondary };
        private readonly Button _start = new Button("Start") { IconName = "play", MinWidth = 96 };
        private readonly Button _stop = new Button("Stop") { IconName = "stop", MinWidth = 96 };

        public DeviceCard(DeviceConfig device)
        {
            DeviceId = device.Id;
            Background = Theme.Current.Surface;
            BorderBrush = Theme.Current.Border;
            BorderThickness = 1;
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(18, 14);
            HAlign = HAlignment.Stretch;

            _name.Text = device.Name;
            _meta.Text = Describe(device);

            _state.TextColor = Theme.Current.TextSecondary;

            var iconBadge = new Border(new Icon("phone", 20) { Tint = Theme.Current.Accent })
            {
                Background = Theme.Current.SurfaceAlt,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9),
                VAlign = VAlignment.Center,
            };

            var left = new StackPanel(Orientation.Vertical, 3) { HExpand = true };
            left.Add(_name);
            left.Add(_meta);

            var statePill = new Border(_state)
            {
                Background = Theme.Current.SurfaceAlt,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 4),
                HAlign = HAlignment.Left,
                Margin = new Thickness(16, 0),
                VAlign = VAlignment.Center,
            };

            var buttons = new StackPanel(Orientation.Horizontal, 8) { VAlign = VAlignment.Center };
            buttons.Add(_start);
            buttons.Add(_stop);

            var row = new StackPanel(Orientation.Horizontal, 12);
            row.Add(iconBadge);
            left.VAlign = VAlignment.Center;
            row.Add(left);
            row.Add(statePill);
            row.Add(Spacer.Fill());
            row.Add(buttons);
            Child = row;

            PointerPressed += _ => SetSelected(true);

            _start.Click += () => { var e = StartRequested; e?.Invoke(); };
            _stop.Click += () => StopRequested?.Invoke();
        }

        public event Action? StartRequested;
        public event Action? StopRequested;

        public void SetSelected(bool selected)
        {
            if (IsSelected == selected) return;
            IsSelected = selected;
            Background = selected ? Theme.Current.SurfaceAlt : Theme.Current.Surface;
            InvalidateVisual();

            // Deselect siblings.
            foreach (var sibling in this.Siblings())
            {
                if (!ReferenceEquals(sibling, this) && sibling is DeviceCard card && card.IsSelected)
                {
                    card.SetSelected(false);
                }
            }
        }

        public void UpdateStatus(BackendStatus status)
        {
            _state.Text = status.State switch
            {
                DeviceRunState.Stopped => "Stopped",
                DeviceRunState.Starting => $"Starting {status.BootProgress}%",
                DeviceRunState.Running => "Running",
                DeviceRunState.Error => "Error",
                _ => status.State.ToString(),
            };
            _state.TextColor = status.State switch
            {
                DeviceRunState.Stopped => Theme.Current.TextSecondary,
                DeviceRunState.Starting => Theme.Current.Warning,
                DeviceRunState.Running => Theme.Current.Success,
                DeviceRunState.Error => Theme.Current.Danger,
                _ => Theme.Current.TextPrimary,
            };
            _start.Visible = status.State is DeviceRunState.Stopped or DeviceRunState.Error;
            _stop.Visible = !_start.Visible;
            InvalidateVisual();
        }

        private static string Describe(DeviceConfig d)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"{d.Width}x{d.Height} @{d.Dpi}dpi   |   {d.RamMb} MB RAM   |   {d.CpuCores} vCPU   |   ");
            var backend = AppServices.Manager.BackendFor(d);
            sb.Append(backend?.DisplayName ?? $"{d.BackendId} (missing)");

            if (d.ApiLevel > 0 || !string.IsNullOrEmpty(d.Abi))
            {
                sb.Append('\n');
                var bits = new List<string>();
                if (d.ApiLevel > 0) bits.Add(AndroidVersions.LabelFor(d.ApiLevel));
                if (!string.IsNullOrEmpty(d.Abi)) bits.Add(d.Abi);
                if (!string.IsNullOrEmpty(d.ImagePackage)) bits.Add(d.ImagePackage);
                sb.Append(string.Join("   \u00B7   ", bits));
            }
            return sb.ToString();
        }
    }
}

internal static class SiblingExtensions
{
    public static IEnumerable<UIElement> Siblings(this UIElement element)
        => element.Parent?.Children ?? Enumerable.Empty<UIElement>();
}
