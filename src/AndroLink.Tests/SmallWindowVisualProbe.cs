using AndroLink.UI;
using SkiaSharp;
using Xunit;

namespace AndroLink.Tests;

/// <summary>
/// Visual probe for the small-window scrolling work: renders (1) the page
/// shell scrolled to the end at the minimum window size, (2) the new
/// default-image installed row designs, and (3) the Create-device dialog form
/// inside its height-capped ScrollViewer. Output goes to /tmp/render/*.png.
/// </summary>
public class SmallWindowVisualProbe
{
    private static void Render(UIElement root, float width, float height, string fileName, float scale = 2f)
    {
        root.MeasureInternal(new Size(width, height));
        root.ArrangeInternal(new Rect(0, 0, width, height));

        using var surface = SKSurface.Create(new SKImageInfo((int)(width * scale), (int)(height * scale)));
        var canvas = surface.Canvas;
        canvas.Scale(scale);
        canvas.Clear(Theme.Current.WindowBackground);
        UiRenderer.RenderTree(root, canvas);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var fs = File.Create(Path.Combine("/tmp/render", fileName));
        data.SaveTo(fs);
        fs.Close();
        Assert.True(File.Exists(Path.Combine("/tmp/render", fileName)));
    }

    [Fact]
    public void Render_small_window_page_scrolled_to_end()
    {
        Directory.CreateDirectory("/tmp/render");
        var theme = Theme.Current = Theme.Dark();

        var window = new Window { InitialClientSize = new Size(560, 380) };
        var page = new ProbeFillPage();
        var host = new Border { Background = theme.WindowBackground, HAlign = HAlignment.Stretch, VAlign = VAlignment.Stretch };
        host.Child = page;

        var nav = new StackPanel(Orientation.Vertical) { Width = 150, Background = theme.Surface };
        var statusBar = new Border(new TextBlock("status bar", 11.5f)) { Background = theme.Surface, Height = 26 };

        var root = new DockPanel();
        DockPanel.SetDock(nav, Dock.Left);
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Add(nav);
        root.Add(statusBar);
        root.Add(host);
        window.Content = root;

        window.ResizeFromPlatform(560, 380);
        window.DoLayout();
        page.Scroll.ScrollToEnd();
        window.DoLayout();

        Render(window, 560, 380, "smallwindow-scrolled.png");
    }

    [Fact]
    public void Render_default_image_rows()
    {
        Directory.CreateDirectory("/tmp/render");
        var theme = Theme.Current = Theme.Dark();

        var page = new StackPanel(Orientation.Vertical, 8)
        {
            Padding = new Thickness(20, 16, 20, 16),
            HAlign = HAlignment.Stretch,
        };

        // Default row: accent border + check icon + "Default image" pill + Unset.
        var defaultRow = new StackPanel(Orientation.Horizontal, 10)
        {
            Background = theme.SurfaceAlt,
            BorderBrush = theme.Accent,
            BorderThickness = 1.5f,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            HAlign = HAlignment.Stretch,
        };
        defaultRow.Add(new Border(new Icon("check", 20) { Tint = theme.Success })
        {
            Background = theme.Surface,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            VAlign = VAlignment.Center,
        });
        var defaultCol = new StackPanel(Orientation.Vertical, 2) { VAlign = VAlignment.Center, HExpand = true };
        defaultCol.Add(new TextBlock("Android 16 \u00B7 google_apis \u00B7 x86_64", 13f, UiFonts.Weight.SemiBold));
        defaultCol.Add(new TextBlock("system-images;android-36;google_apis;x86_64   -   1.6 GB on disk", 11f) { TextColor = theme.TextSecondary });
        defaultRow.Add(defaultCol);
        defaultRow.Add(new Border(new TextBlock("Default image", 11f, UiFonts.Weight.SemiBold) { TextColor = theme.Success })
        {
            BorderBrush = theme.Success,
            BorderThickness = 1,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 3),
            VAlign = VAlignment.Center,
        });
        defaultRow.Add(new Button("Unset") { IconName = "x", MinWidth = 100 });
        defaultRow.Add(new Button("Open folder") { IconName = "open-folder", MinWidth = 118 });
        defaultRow.Add(new Button("Delete") { IconName = "trash", MinWidth = 96, IsDanger = true });
        page.Add(defaultRow);

        // Non-default row: normal border + "Set default".
        var normalRow = new StackPanel(Orientation.Horizontal, 10)
        {
            Background = theme.Surface,
            BorderBrush = theme.Border,
            BorderThickness = 1,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            HAlign = HAlignment.Stretch,
        };
        normalRow.Add(new Border(new Icon("package", 20) { Tint = theme.Accent })
        {
            Background = theme.SurfaceAlt,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            VAlign = VAlignment.Center,
        });
        var normalCol = new StackPanel(Orientation.Vertical, 2) { VAlign = VAlignment.Center, HExpand = true };
        normalCol.Add(new TextBlock("Android 16 \u00B7 android-tv \u00B7 arm64-v8a", 13f, UiFonts.Weight.SemiBold));
        normalCol.Add(new TextBlock("system-images;android-36;android-tv;arm64-v8a   -   1.1 GB on disk", 11f) { TextColor = theme.TextSecondary });
        normalRow.Add(normalCol);
        normalRow.Add(new Button("Set default") { IconName = "check", MinWidth = 130 });
        normalRow.Add(new Button("Open folder") { IconName = "open-folder", MinWidth = 118 });
        normalRow.Add(new Button("Delete") { IconName = "trash", MinWidth = 96, IsDanger = true });
        page.Add(normalRow);

        Render(page, 1000, page.DesiredSize.Height > 0 ? page.DesiredSize.Height : 200, "default-rows.png");
    }

    [Fact]
    public void Render_dialog_form_scrolled()
    {
        Directory.CreateDirectory("/tmp/render");
        var theme = Theme.Current = Theme.Dark();

        // Dialog replica: 640-wide Border with the height cap, form inside a
        // ScrollViewer; tall content forces scrolling.
        var form = new StackPanel(Orientation.Vertical, 12) { HAlign = HAlignment.Stretch };
        form.Add(new TextBlock("Create device", 17, UiFonts.Weight.SemiBold));
        for (var i = 0; i < 12; i++)
        {
            form.Add(new TextBlock($"Field row {i}", 13f));
            form.Add(new TextBox("value"));
        }
        var buttons = new StackPanel(Orientation.Horizontal, 8) { HAlign = HAlignment.Right };
        buttons.Add(new Button("Cancel"));
        buttons.Add(new Button("Create device", null, primary: true));
        form.Add(buttons);

        var formScroll = new ScrollViewer { Content = form, HAlign = HAlignment.Stretch, VAlign = VAlignment.Stretch };
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
            MaxHeight = 420,
        };
        var scrim = new Border(dialog)
        {
            Background = theme.Scrim,
            HAlign = HAlignment.Stretch,
            VAlign = VAlignment.Stretch,
            Padding = new Thickness(20),
        };

        scrim.MeasureInternal(new Size(900, 500));
        scrim.ArrangeInternal(new Rect(0, 0, 900, 500));
        formScroll.ScrollToEnd();
        scrim.MeasureInternal(new Size(900, 500));
        scrim.ArrangeInternal(new Rect(0, 0, 900, 500));

        Render(scrim, 900, 500, "dialog-scrolled.png");
    }

    private sealed class ProbeFillPage : UIElement
    {
        public ScrollViewer Scroll = null!;

        public ProbeFillPage()
        {
            ClipToBounds = true;

            var header = new StackPanel(Orientation.Vertical, 4) { Padding = new Thickness(0, 0, 0, 10) };
            header.Add(new TextBlock("Android images", 21, UiFonts.Weight.Bold));

            var toolbar = new StackPanel(Orientation.Horizontal, 8) { Padding = new Thickness(0, 0, 0, 6) };
            toolbar.Add(new Button("Refresh catalog", null, primary: true));
            toolbar.Add(new Button("Open images folder"));

            Scroll = new ScrollViewer { HAlign = HAlignment.Stretch, VAlign = VAlignment.Stretch };
            var list = new StackPanel(Orientation.Vertical, 6);
            for (var i = 1; i <= 20; i++)
            {
                var row = new StackPanel(Orientation.Horizontal, 8)
                {
                    Background = Theme.Current.Surface,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 7),
                    HAlign = HAlignment.Stretch,
                };
                row.Add(new TextBlock($"Android TV \u00B7 x86_64 \u00B7 image {i}", 12f) { VAlign = VAlignment.Center });
                row.Add(Spacer.Fill());
                row.Add(new Button("Download") { MinWidth = 100 });
                list.Add(row);
            }
            Scroll.Content = list;

            var content = new StackPanel(Orientation.Vertical)
            {
                Padding = new Thickness(18, 14, 18, 10),
                HAlign = HAlignment.Stretch,
                VAlign = VAlignment.Stretch,
            };
            content.Add(header);
            content.Add(toolbar);
            content.Add(Scroll);

            Add(new Border(content) { HAlign = HAlignment.Center, VAlign = VAlignment.Stretch, MaxWidth = 1120 });
        }
    }
}
