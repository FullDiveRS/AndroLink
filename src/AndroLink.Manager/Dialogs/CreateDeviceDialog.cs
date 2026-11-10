using AndroLink.Core;
using AndroLink.Core.SystemImages;
using AndroLink.UI;

namespace AndroLink.Manager.Dialogs;

/// <summary>
/// Modal "Create device" dialog rendered on the window overlay: name,
/// resolution presets, RAM/CPU sliders, backend picker, Android version
/// (downloaded system image) + CPU/ABI selection, and the openGApps hook.
/// Returns the configured DeviceConfig or null when cancelled.
/// </summary>
public static class CreateDeviceDialog
{
    private static readonly (string Label, int Width, int Height, int Dpi)[] Resolutions =
    {
        ("720p  (1280x720)", 1280, 720, 240),
        ("1080p (1920x1080)", 1920, 1080, 320),
        ("Tablet (1600x2560)", 1600, 2560, 280),
        ("Phone (1080x2340)", 1080, 2340, 400),
    };

    public static Task<DeviceConfig?> ShowAsync()
    {
        var owner = AppServices.MainWindow!;
        var tcs = new TaskCompletionSource<DeviceConfig?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var theme = Theme.Current;

        var name = new TextBox("e.g. Pixel Demo") { HAlign = HAlignment.Stretch };

        var resolutionButtons = new StackPanel(Orientation.Horizontal, 8);
        var selectedRes = Resolutions[0];
        foreach (var preset in Resolutions)
        {
            var p = preset;
            var button = new Button(p.Label) { Tag = false };
            button.Click += () =>
            {
                selectedRes = p;
                foreach (var child in resolutionButtons.Children)
                {
                    if (child is Button b) b.IsPrimary = ReferenceEquals(b, button);
                }
                resolutionButtons.InvalidateVisual();
            };
            resolutionButtons.Add(button);
        }
        ((Button)resolutionButtons.Children[0]).IsPrimary = true;

        var ramLabel = new TextBlock("", 12.5f);
        var ram = new Slider(512, 8192, 2048);
        ram.ValueChanged += () => ramLabel.Text = $"RAM: {Snap(ram.Value, 256):0} MB";
        ram.RaiseValueChanged();

        var cpuLabel = new TextBlock("", 12.5f);
        var cpu = new Slider(1, 8, 2);
        cpu.ValueChanged += () => cpuLabel.Text = $"CPU cores: {Math.Round(cpu.Value):0}";
        cpu.RaiseValueChanged();

        // Backend picker: horizontal buttons, preselect "simulated" when present.
        var backends = AppServices.Manager.Backends.All.ToList();
        var backendButtons = new StackPanel(Orientation.Horizontal, 8);
        var selectedBackend = backends.FirstOrDefault(b => b.Id == "simulated") ?? backends.FirstOrDefault();
        foreach (var backend in backends)
        {
            var b = backend;
            var button = new Button(b.DisplayName);
            button.Click += () =>
            {
                selectedBackend = b;
                foreach (var child in backendButtons.Children)
                {
                    if (child is Button other) other.IsPrimary = ReferenceEquals(other, button);
                }
                backendButtons.InvalidateVisual();
            };
            if (ReferenceEquals(b, selectedBackend)) button.IsPrimary = true;
            backendButtons.Add(button);
        }

        // ---- Android version (downloaded system image) picker.
        var installedImages = AppServices.ImageStore.GetInstalled();
        var versionList = new ListBox
        {
            Height = Math.Min(148, 44 + 34 * Math.Max(1, installedImages.Count)),
            HAlign = HAlignment.Stretch,
        };
        versionList.Items.Add("Backend default (no specific Android version)");
        foreach (var image in installedImages)
        {
            versionList.Items.Add(image);
        }
        versionList.ItemFactory = item =>
        {
            var text = item is SystemImageInfo img ? $"{img.Summary}   ({img.PackagePath})" : (item as string ?? "");
            return new TextBlock(text, 12.5f) { Margin = new Thickness(8, 7, 8, 7) };
        };

        // Preselect the user's preferred image ("Set default" on the Android
        // images page); fall back to the backend default entry.
        var preferredPackage = AppServices.Manager.Settings.DefaultImagePackage;
        var preferredIndex = -1;
        if (!string.IsNullOrEmpty(preferredPackage))
        {
            for (var i = 0; i < installedImages.Count; i++)
            {
                if (installedImages[i].PackagePath.Equals(preferredPackage, StringComparison.OrdinalIgnoreCase))
                {
                    preferredIndex = i;
                    break;
                }
            }
        }
        versionList.SelectedIndex = preferredIndex >= 0 ? preferredIndex + 1 : 0; // +1: "Backend default" entry
        var hint = installedImages.Count == 0
            ? "No images downloaded yet - grab one from the 'Android images' page, or use the simulated backend."
            : $"{installedImages.Count} image(s) installed. Download more from the 'Android images' page.";
        var versionHint = new TextBlock(hint, 11f) { TextColor = theme.TextSecondary, Wrapping = true };

        // ---- CPU / ABI picker.
        var abiButtons = new StackPanel(Orientation.Horizontal, 8);
        Button? recommendedButton = null;
        Button? firstAbiButton = null;
        foreach (var abi in CpuAbi.All)
        {
            var current = abi;
            var native = CpuAbi.RunsNatively(current, HostInfo.CpuArchitecture);
            var button = new Button(current) { MinWidth = 108 };
            button.Click += () =>
            {
                foreach (var child in abiButtons.Children)
                {
                    if (child is Button other) other.IsPrimary = ReferenceEquals(other, button);
                }
                abiButtons.InvalidateVisual();
            };
            if (native && recommendedButton is null) recommendedButton = button;
            if (firstAbiButton is null) firstAbiButton = button;
            abiButtons.Add(button);
        }
        // Preselect the host-recommended ABI, falling back to the first.
        var selectedAbiButton = recommendedButton ?? firstAbiButton;
        if (selectedAbiButton is not null) selectedAbiButton.IsPrimary = true;
        var abiHint = new TextBlock(
            $"{HostInfo.Recommendation}", 11f) { TextColor = theme.TextSecondary, Wrapping = true };

        var gappsBox = new TextBox("no Google Apps (openGApps zip path - optional)") { ReadOnly = true, HAlign = HAlignment.Stretch };

        var error = new TextBlock("", 12f) { TextColor = theme.Danger, Wrapping = true };

        var buttons = new StackPanel(Orientation.Horizontal, 8) { HAlign = HAlignment.Right };
        var createButton = new Button("Create device", primary: true);
        var cancelButton = new Button("Cancel");
        buttons.Add(cancelButton);
        buttons.Add(createButton);

        var form = new StackPanel(Orientation.Vertical, 12) { HAlign = HAlignment.Stretch };
        form.Add(new TextBlock("Create device", 17, UiFonts.Weight.SemiBold));
        form.Add(Label("Device name"));
        form.Add(name);
        form.Add(Label("Screen resolution"));
        form.Add(resolutionButtons);
        form.Add(Label("Android version (downloaded image - see the 'Android images' page)"));
        form.Add(versionList);
        form.Add(versionHint);
        form.Add(Label("CPU / ABI"));
        form.Add(abiButtons);
        form.Add(abiHint);
        form.Add(ramLabel);
        form.Add(ram);
        form.Add(cpuLabel);
        form.Add(cpu);
        form.Add(Label("Runtime backend"));
        form.Add(backendButtons);
        form.Add(Label("Google Apps (openGApps hook - you provide the zip)"));
        var gappsRow = new StackPanel(Orientation.Horizontal, 8) { HAlign = HAlignment.Stretch };
        gappsRow.Add(gappsBox);
        gappsRow.Add(new Button("Browse\u2026", () =>
        {
            var zip = AppServices.Platform.PickOpenFile("Choose an openGApps zip", "openGApps (*.zip)|*.zip|All files (*.*)|*.*");
            if (zip is not null) gappsBox.Text = zip;
        }) { Width = 96 });
        form.Add(gappsRow);
        form.Add(error);
        form.Add(buttons);

        // Small windows: the whole form scrolls instead of being cut off.
        var formScroll = new ScrollViewer
        {
            Content = form,
            HAlign = HAlignment.Stretch,
            VAlign = VAlignment.Stretch,
        };

        var dialog = new Border(formScroll)
        {
            Background = theme.Surface,
            BorderBrush = theme.Border,
            BorderThickness = 1,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(26, 20),
            HAlign = HAlignment.Center,
            VAlign = VAlignment.Center,
            Width = 640f,
            MaxHeight = Math.Max(260f, (owner.FinalRect.Height > 0 ? owner.FinalRect.Height : owner.InitialClientSize.Height) - 72f),
        };

        var scrim = new Border(dialog)
        {
            Background = theme.Scrim,
            HAlign = HAlignment.Stretch,
            VAlign = VAlignment.Stretch,
            Padding = new Thickness(20),
            Focusable = true,
        };

        void Close(DeviceConfig? result)
        {
            owner.Overlay.Remove(scrim);
            tcs.TrySetResult(result);
        }

        cancelButton.Click += () => Close(null);
        scrim.KeyDown += args =>
        {
            if (args.Key == VirtualKey.Escape) Close(null);
        };

        createButton.Click += () =>
        {
            var deviceName = name.Text.Trim();
            if (deviceName.Length == 0)
            {
                error.Text = "Give the device a name.";
                return;
            }
            if (selectedBackend is null)
            {
                error.Text = "No runtime backend is loaded.";
                return;
            }

            var image = versionList.SelectedItem as SystemImageInfo;
            var abi = (abiButtons.Children.OfType<Button>().FirstOrDefault(b => b.IsPrimary)?.Text) ?? HostInfo.RecommendedAbi;

            if (image is null && selectedBackend.Id is "emulator" or "qemu")
            {
                error.Text = selectedBackend.Id == "emulator"
                    ? "The Android emulator backend needs a downloaded system image - pick one above or download it on the 'Android images' page."
                    : "The QEMU backend needs an Android-x86 disk image (set imagePath in the backend options).";
                return;
            }

            var device = new DeviceConfig
            {
                Name = deviceName,
                Width = selectedRes.Width,
                Height = selectedRes.Height,
                Dpi = selectedRes.Dpi,
                RamMb = (int)Snap(ram.Value, 256),
                CpuCores = Math.Max(1, (int)Math.Round(cpu.Value)),
                BackendId = selectedBackend.Id,
                ApiLevel = image?.ApiLevel ?? 0,
                Abi = image?.Abi ?? abi,
                ImagePackage = image?.PackagePath,
                GAppsZipPath = string.IsNullOrWhiteSpace(gappsBox.Text) ? null : gappsBox.Text.Trim(),
            };

            var gapps = GAppsPlan.FromDevice(device);
            var problem = gapps.Validate();
            if (problem is not null)
            {
                error.Text = problem;
                return;
            }

            Close(device);
        };

        owner.Overlay.Add(scrim);
        scrim.Opacity = 0;
        Animator.Animate(0, 1, TimeSpan.FromMilliseconds(130), v => scrim.Opacity = (float)v, Easing.EaseOut, owner: scrim);
        name.Focus();

        return tcs.Task;
    }

    private static TextBlock Label(string text)
        => new(text, 12f, UiFonts.Weight.SemiBold) { TextColor = Theme.Current.TextSecondary };

    private static double Snap(double value, double step) => Math.Round(value / step) * step;
}
