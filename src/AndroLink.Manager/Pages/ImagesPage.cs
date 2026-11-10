using AndroLink.Core;
using AndroLink.Core.SystemImages;
using AndroLink.UI;
using SkiaSharp;

namespace AndroLink.Manager.Pages;

/// <summary>
/// Android-version manager, modeled after Android Studio's Device Manager /
/// SDK Manager: shows installed system images and lets the user download
/// Android versions (API level x tag x ABI) straight from Google's
/// repository, with live progress and cancellation.
/// </summary>
public sealed class ImagesPage : UIElement
{
    private readonly StackPanel _list = new(Orientation.Vertical, 8);
    private readonly Button _refreshButton;
    private readonly TextBlock _catalogStatus = new("", 12f);

    public ImagesPage()
    {
        ClipToBounds = true;

        var header = new StackPanel(Orientation.Vertical, 4)
        {
            Padding = new Thickness(0, 0, 0, 12),
        };
        header.Add(new TextBlock("Android images", 21, UiFonts.Weight.Bold));
        header.Add(new TextBlock(
            "Download Android versions exactly like Android Studio's Device Manager - pick an API level, a build (Google APIs / Play / AOSP) and a CPU architecture.",
            12.5f) { TextColor = Theme.Current.TextSecondary, Wrapping = true });

        // Host architecture hint: which ABIs run natively on this PC.
        var hostBanner = new StackPanel(Orientation.Horizontal, 10)
        {
            Background = Theme.Current.SurfaceAlt,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            HAlign = HAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 10),
        };
        hostBanner.Add(new Icon("info", 18) { Tint = Theme.Current.Accent, VAlign = VAlignment.Center });
        hostBanner.Add(new TextBlock(HostInfo.Recommendation, 12f)
        {
            TextColor = Theme.Current.TextSecondary,
            Wrapping = true,
            VAlign = VAlignment.Center,
            HAlign = HAlignment.Stretch,
        });

        _refreshButton = Button.WithIcon("refresh", "Refresh catalog", RefreshCatalog, primary: true);
        var openFolder = Button.WithIcon("open-folder", "Open images folder", OpenImagesFolder);

        var catalogStatusColumn = new StackPanel(Orientation.Vertical, 2) { VAlign = VAlignment.Center };
        catalogStatusColumn.Add(_catalogStatus);

        var toolbar = new StackPanel(Orientation.Horizontal, 10)
        {
            Padding = new Thickness(0, 0, 0, 6),
        };
        toolbar.Add(_refreshButton);
        toolbar.Add(openFolder);
        toolbar.Add(catalogStatusColumn);

        var scroll = new ScrollViewer { HAlign = HAlignment.Stretch, VAlign = VAlignment.Stretch };
        scroll.Content = _list;

        var content = new StackPanel(Orientation.Vertical)
        {
            Padding = new Thickness(26, 22, 26, 16),
            HAlign = HAlignment.Stretch,
            VAlign = VAlignment.Stretch,
        };
        content.Add(header);
        content.Add(hostBanner);
        content.Add(toolbar);
        content.Add(scroll);

        // Centered page column on wide windows.
        var centered = new Border(content)
        {
            HAlign = HAlignment.Center,
            VAlign = VAlignment.Stretch,
            MaxWidth = 1120,
        };
        Add(centered);

        AppServices.ImageStore.Progress += OnDownloadProgress;
        AppServices.ImageStore.DownloadFinished += OnDownloadFinished;
        AppServices.CatalogChanged += OnCatalogChanged;
    }

    public void Activate()
    {
        Rebuild();
        // Refresh the online catalog in the background on first view.
        if (_needsFirstRefresh)
        {
            _needsFirstRefresh = false;
            RefreshCatalog();
        }
    }

    private bool _needsFirstRefresh = true;

    // ------------------------------------------------------------- rebuild

    private void Rebuild()
    {
        _list.ClearChildren();
        var theme = Theme.Current;

        var installed = AppServices.ImageStore.GetInstalled();
        var catalog = AppServices.ImageCatalog
            .GroupBy(i => i.PackagePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(i => i.ApiLevel)
            .ThenBy(i => i.TagId)
            .ThenBy(i => Array.IndexOf(CpuAbi.All, i.Abi))
            .ToList();

        _catalogStatus.Text = AppServices.CatalogIsOnline
            ? $"Catalog: live from dl.google.com ({catalog.Count} packages)"
            : $"Catalog: built-in snapshot ({catalog.Count} packages) - press Refresh to go online";
        _catalogStatus.TextColor = theme.TextSecondary;

        // ---- installed section
        _list.Add(SectionHeader($"Installed on this PC ({installed.Count})", theme));
        if (installed.Count == 0)
        {
            _list.Add(new Border(new TextBlock(
                "Nothing downloaded yet. Pick an Android version below - images install to\n"
                + AppServices.ImageStore.Root, 12f)
            {
                TextColor = theme.TextSecondary,
                Wrapping = true,
            })
            {
                Background = theme.Surface,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 14),
                HAlign = HAlignment.Stretch,
            });
        }
        else
        {
            _list.Add(new TextBlock(
                "Pick the image new devices should use - 'Set default' marks your preferred Android version.",
                11.5f) { TextColor = theme.TextSecondary, Margin = new Thickness(0, 0, 0, 4) });
            foreach (var image in installed)
            {
                _list.Add(InstalledRow(image, theme));
            }
        }

        // ---- available section
        _list.Add(SectionHeader("Available to download", theme));

        foreach (var group in catalog.GroupBy(i => i.ApiLevel).OrderByDescending(g => g.Key))
        {
            var first = group.First();
            _list.Add(new TextBlock($"{AndroidVersions.ShortLabel(first.ApiLevel)} - API {first.ApiLevel}", 14.5f, UiFonts.Weight.SemiBold)
            {
                Margin = new Thickness(0, 8, 0, 2),
            });

            foreach (var image in group)
            {
                _list.Add(AvailableRow(image, theme));
            }
        }

        InvalidateMeasure();
    }

    private static UIElement SectionHeader(string text, Theme theme)
        => new TextBlock(text, 14.5f, UiFonts.Weight.SemiBold)
        {
            Margin = new Thickness(0, 6, 0, 2),
        };

    private UIElement InstalledRow(SystemImageInfo image, Theme theme)
    {
        var isDefault = IsDefault(image);

        var row = new StackPanel(Orientation.Horizontal, 10)
        {
            Background = isDefault ? theme.SurfaceAlt : theme.Surface,
            BorderBrush = isDefault ? theme.Accent : theme.Border,
            BorderThickness = isDefault ? 1.5f : 1,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            HAlign = HAlignment.Stretch,
        };

        var iconBox = new Border(new Icon(isDefault ? "check" : "package", 20) { Tint = isDefault ? theme.Success : theme.Accent })
        {
            Background = theme.Surface,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            VAlign = VAlignment.Center,
        };
        row.Add(iconBox);

        var textCol = new StackPanel(Orientation.Vertical, 2) { VAlign = VAlignment.Center, HExpand = true };
        textCol.Add(new TextBlock(image.Summary, 13f, UiFonts.Weight.SemiBold));
        textCol.Add(new TextBlock($"{image.PackagePath}   -   {SystemImageStore.FormatBytes(image.SizeBytes)} on disk", 11f)
        {
            TextColor = theme.TextSecondary,
        });
        row.Add(textCol);

        if (isDefault)
        {
            row.Add(new Border(new TextBlock("Default image", 11f, UiFonts.Weight.SemiBold) { TextColor = theme.Success })
            {
                BorderBrush = theme.Success,
                BorderThickness = 1,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 3),
                VAlign = VAlignment.Center,
            });
            row.Add(new Button("Unset", () => AppServices.SetDefaultImage(null)) { IconName = "x", MinWidth = 100 });
        }
        else
        {
            row.Add(new Button("Set default", () => AppServices.SetDefaultImage(image)) { IconName = "check", MinWidth = 130 });
        }

        row.Add(new Button("Open folder", () => RevealImage(image)) { IconName = "open-folder", MinWidth = 118 });
        row.Add(new Button("Delete", () => DeleteImage(image)) { IconName = "trash", MinWidth = 96, IsDanger = true });

        return row;
    }

    private static bool IsDefault(SystemImageInfo image)
        => string.Equals(AppServices.Manager.Settings.DefaultImagePackage, image.PackagePath, StringComparison.OrdinalIgnoreCase);

    private UIElement AvailableRow(SystemImageInfo image, Theme theme)
    {
        var row = new StackPanel(Orientation.Horizontal, 10)
        {
            Background = theme.Surface,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8),
            Margin = new Thickness(14, 0, 0, 0),
            HAlign = HAlignment.Stretch,
        };

        row.Add(new TextBlock(image.TagDisplay, 12.5f, UiFonts.Weight.SemiBold) { VAlign = VAlignment.Center });

        var abiBadge = new Border(new TextBlock(image.Abi, 11.5f, UiFonts.Weight.SemiBold)
        {
            TextColor = CpuAbi.RunsNatively(image.Abi, HostInfo.CpuArchitecture) ? theme.Success : theme.TextSecondary,
        })
        {
            Background = theme.SurfaceAlt,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 3),
            VAlign = VAlignment.Center,
        };
        row.Add(abiBadge);

        if (image.Abi.Equals(HostInfo.RecommendedAbi, StringComparison.OrdinalIgnoreCase))
        {
            row.Add(new Border(new TextBlock("recommended", 10.5f) { TextColor = theme.Accent })
            {
                BorderBrush = theme.Accent,
                BorderThickness = 1,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 2),
                VAlign = VAlignment.Center,
            });
        }

        var size = new TextBlock(SystemImageStore.FormatBytes(image.SizeBytes), 12f)
        {
            TextColor = theme.TextSecondary,
            VAlign = VAlignment.Center,
        };
        row.Add(size);
        row.Add(Spacer.Fill());

        // Rows are rebuilt when a download starts/ends, so each state gets its
        // own layout (the progress column is taller than the button).
        if (_downloading.Contains(image.PackagePath))
        {
            var host = new StackPanel(Orientation.Vertical, 4) { VAlign = VAlignment.Center };
            var progress = new ProgressBar { Height = 6, HAlign = HAlignment.Stretch };
            var status = new TextBlock("Starting\u2026", 10.5f) { TextColor = theme.TextSecondary, HAlign = HAlignment.Stretch };
            var cancelButton = new Button("Cancel", () => AppServices.ImageStore.CancelDownload(image.PackagePath))
            {
                IconName = "x",
                MinWidth = 110,
                HAlign = HAlignment.Right,
            };
            host.Add(progress);
            host.Add(status);
            host.Add(cancelButton);
            row.Add(host);
            _rowWidgets[image.PackagePath] = new RowWidgets(null, progress, status, size);
        }
        else if (AppServices.ImageStore.IsInstalled(image))
        {
            row.Add(new Border(new TextBlock("Installed", 11f, UiFonts.Weight.SemiBold) { TextColor = theme.Success })
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 3),
                VAlign = VAlignment.Center,
            });
        }
        else
        {
            var button = new Button("Download", () => DownloadImage(image)) { IconName = "download", MinWidth = 128 };
            row.Add(button);
            _rowWidgets[image.PackagePath] = new RowWidgets(button, null, null, size);
        }

        return row;
    }

    private sealed record RowWidgets(Button? Button, ProgressBar? Progress, TextBlock? Status, TextBlock Size);

    private readonly Dictionary<string, RowWidgets> _rowWidgets = new(StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------- actions

    private async void DownloadImage(SystemImageInfo image)
    {
        _downloading.Add(image.PackagePath);
        Rebuild();

        try
        {
            await AppServices.ImageStore.DownloadAsync(image);
        }
        catch (OperationCanceledException)
        {
            // row is restored by the DownloadFinished handler
        }
        catch (Exception ex)
        {
            Log.Warn("images", $"download of {image.PackagePath} failed: {ex.Message}");
            AppServices.Post(() =>
            {
                _downloading.Remove(image.PackagePath);
                Rebuild();
                _ = MessageBox.Show(AppServices.MainWindow!, "Download failed",
                    $"{image.Summary}\n\n{ex.Message}", "OK");
            });
        }
    }

    private readonly HashSet<string> _downloading = new(StringComparer.OrdinalIgnoreCase);

    private void OnDownloadProgress(SystemImageProgress progress)
        => AppServices.Post(() =>
        {
            if (!_rowWidgets.TryGetValue(progress.PackagePath, out var widgets)) return;
            if (widgets.Progress is not null) widgets.Progress.Value = progress.Percent;
            if (widgets.Status is not null)
            {
                widgets.Status.Text = progress.Stage switch
                {
                    "Downloading" => $"{progress.Stage} - {SystemImageStore.FormatBytes(progress.ReceivedBytes)} / {SystemImageStore.FormatBytes(progress.TotalBytes)}",
                    _ => $"{progress.Stage}\u2026",
                };
            }
        });

    private void OnDownloadFinished(SystemImageInfo image, string? error)
        => AppServices.Post(() =>
        {
            _downloading.Remove(image.PackagePath);
            if (error is null && AppServices.DefaultImage is null)
            {
                // First download becomes the preferred image so 'Create device'
                // can offer it right away; the user can change it any time.
                AppServices.SetDefaultImage(image);
            }
            Rebuild();
        });

    private void OnCatalogChanged() => AppServices.Post(Rebuild);

    private async void RefreshCatalog()
    {
        if (_refreshing) return;
        _refreshing = true;
        _refreshButton.Text = "Refreshing\u2026";
        _catalogStatus.Text = "Contacting dl.google.com\u2026";
        try
        {
            await AppServices.RefreshCatalogAsync();
        }
        catch (Exception ex)
        {
            Log.Warn("images", $"refresh failed: {ex.Message}");
        }
        finally
        {
            _refreshing = false;
            _refreshButton.Text = "Refresh catalog";
        }
    }

    private bool _refreshing;

    private void OpenImagesFolder()
    {
        Directory.CreateDirectory(AppServices.ImageStore.Root);
        AppServices.Platform.RevealInFileManager(AppServices.ImageStore.Root);
    }

    private void RevealImage(SystemImageInfo image)
    {
        var dir = AppServices.ImageStore.InstallDirFor(image);
        Directory.CreateDirectory(dir);
        AppServices.Platform.RevealInFileManager(dir);
    }

    private async void DeleteImage(SystemImageInfo image)
    {
        var choice = await MessageBox.Show(
            AppServices.MainWindow!,
            "Delete system image",
            $"Delete {image.Summary}?\n\nIt can be re-downloaded any time.",
            "Cancel", "Delete");
        if (choice != 1) return;

        try
        {
            AppServices.ImageStore.Delete(image);
            if (IsDefault(image))
            {
                AppServices.SetDefaultImage(null);
            }
        }
        catch (Exception ex)
        {
            await MessageBox.Show(AppServices.MainWindow!, "Delete failed", ex.Message, "OK");
        }
        Rebuild();
    }
}
