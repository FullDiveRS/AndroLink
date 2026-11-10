using AndroLink.UI;
using SkiaSharp;
using Xunit;

namespace AndroLink.Tests;

/// <summary>
/// Visual probe: renders the ImagesPage widget composition (banner, rows,
/// badges) to a PNG so alignment can be checked by eye.
/// </summary>
public class ImagesPageVisualProbe
{
    [Fact]
    public void Render_images_page_rows()
    {
        var theme = Theme.Current = Theme.Dark();
        var outDir = "/tmp/render";
        Directory.CreateDirectory(outDir);

        var scale = 2f;
        var width = 1040f;

        var page = new StackPanel(Orientation.Vertical, 10)
        {
            Padding = new Thickness(26, 22, 26, 16),
            HAlign = HAlignment.Stretch,
        };

        // --- host banner (icon + wrapped text), same as ImagesPage
        var hostBanner = new StackPanel(Orientation.Horizontal, 10)
        {
            Background = theme.SurfaceAlt,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            HAlign = HAlignment.Stretch,
        };
        hostBanner.Add(new Icon("info", 18) { Tint = theme.Accent, VAlign = VAlignment.Center });
        hostBanner.Add(new TextBlock(
            "This PC is x86_64 - x86_64 images run at full speed; arm64-v8a images run through emulation.",
            12f) { TextColor = theme.TextSecondary, Wrapping = true, VAlign = VAlignment.Center, HAlign = HAlignment.Stretch });
        page.Add(hostBanner);

        // --- toolbar with status column
        var toolbar = new StackPanel(Orientation.Horizontal, 10) { Padding = new Thickness(0, 0, 0, 6) };
        toolbar.Add(Button.WithIcon("refresh", "Refresh catalog", null, primary: true));
        toolbar.Add(Button.WithIcon("open-folder", "Open images folder", null));
        var statusCol = new StackPanel(Orientation.Vertical, 2) { VAlign = VAlignment.Center };
        statusCol.Add(new TextBlock("Catalog: live from dl.google.com (42 packages)", 12f) { TextColor = theme.TextSecondary });
        toolbar.Add(statusCol);
        page.Add(toolbar);

        // --- installed row
        var installedRow = new StackPanel(Orientation.Horizontal, 10)
        {
            Background = theme.Surface,
            BorderBrush = theme.Border,
            BorderThickness = 1,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            HAlign = HAlignment.Stretch,
        };
        installedRow.Add(new Border(new Icon("package", 20) { Tint = theme.Accent })
        {
            Background = theme.SurfaceAlt,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            VAlign = VAlignment.Center,
        });
        var textCol = new StackPanel(Orientation.Vertical, 2) { VAlign = VAlignment.Center, HExpand = true };
        textCol.Add(new TextBlock("Android 15 (VanillaIceCream) - Google APIs", 13f, UiFonts.Weight.SemiBold));
        textCol.Add(new TextBlock("system-images;android-35;google_apis;x86_64   -   1.6 GB on disk", 11f) { TextColor = theme.TextSecondary });
        installedRow.Add(textCol);
        installedRow.Add(new Button("Open folder") { IconName = "open-folder", MinWidth = 118 });
        installedRow.Add(new Button("Delete") { IconName = "trash", MinWidth = 96, IsDanger = true });
        page.Add(installedRow);

        // --- available row (tag + ABI badge + recommended badge + size + download)
        var row = new StackPanel(Orientation.Horizontal, 10)
        {
            Background = theme.Surface,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8),
            Margin = new Thickness(14, 0, 0, 0),
            HAlign = HAlignment.Stretch,
        };
        row.Add(new TextBlock("google_apis", 12.5f, UiFonts.Weight.SemiBold) { VAlign = VAlignment.Center });
        row.Add(new Border(new TextBlock("x86_64", 11.5f, UiFonts.Weight.SemiBold) { TextColor = theme.Success })
        {
            Background = theme.SurfaceAlt,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 3),
            VAlign = VAlignment.Center,
        });
        row.Add(new Border(new TextBlock("recommended", 10.5f) { TextColor = theme.Accent })
        {
            BorderBrush = theme.Accent,
            BorderThickness = 1,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 2),
            VAlign = VAlignment.Center,
        });
        row.Add(new TextBlock("1.6 GB", 12f) { TextColor = theme.TextSecondary, VAlign = VAlignment.Center });
        row.Add(Spacer.Fill());
        row.Add(new Button("Download") { IconName = "download", MinWidth = 128 });
        page.Add(row);

        // layout + render
        page.MeasureInternal(new Size(width, float.PositiveInfinity));
        page.ArrangeInternal(new Rect(0, 0, width, page.DesiredSize.Height));

        var pixelH = (int)(page.DesiredSize.Height + 44) * (int)scale;
        using var surface = SKSurface.Create(new SKImageInfo((int)(width * scale), pixelH));
        var canvas = surface.Canvas;
        canvas.Scale(scale);
        canvas.Clear(theme.WindowBackground);
        canvas.Translate(0, 22);
        UiRenderer.RenderTree(page, canvas);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var fs = File.Create(Path.Combine(outDir, "imagespage-probe.png"));
        data.SaveTo(fs);
        fs.Close();

        Assert.True(File.Exists(Path.Combine(outDir, "imagespage-probe.png")));
    }
}
