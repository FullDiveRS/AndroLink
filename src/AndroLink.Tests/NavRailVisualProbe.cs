using AndroLink.UI;
using SkiaSharp;
using Xunit;

namespace AndroLink.Tests;

/// <summary>Visual probe for the NavRail label/icon alignment.</summary>
public class NavRailVisualProbe
{
    [Fact]
    public void Render_nav_rail()
    {
        var theme = Theme.Current = Theme.Dark();
        Directory.CreateDirectory("/tmp/render");

        // Mirror MainWindow's NavRail construction.
        var rail = NavRailShim.Create(
            ("devices", "Devices", "phone"),
            ("apps", "Apps", "apps"),
            ("files", "Files", "folder"),
            ("images", "Android images", "package"),
            ("settings", "Settings", "settings"));
        rail.SetSelected(0);

        rail.MeasureInternal(new Size(208, 780));
        rail.ArrangeInternal(new Rect(0, 0, 208, 780));

        using var surface = SKSurface.Create(new SKImageInfo(416, 1560));
        var canvas = surface.Canvas;
        canvas.Scale(2);
        canvas.Clear(theme.WindowBackground);
        UiRenderer.RenderTree(rail, canvas);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var fs = File.Create("/tmp/render/navrail-probe.png");
        data.SaveTo(fs);

        Assert.True(File.Exists("/tmp/render/navrail-probe.png"));
    }
}

/// <summary>Bridge to the internal Manager NavRail via reflection-free shim.</summary>
internal static class NavRailShim
{
    internal static ProbeNavRail Create(params (string Id, string Label, string Icon)[] items)
    {
        // The Manager assembly is not referenced by tests; rebuild a minimal
        // equivalent rail here using the same metrics.
        return new ProbeNavRail(items);
    }

    internal sealed class ProbeNavRail : UIElement
    {
        private readonly List<(string Label, string Icon)> _items = new();
        private int _selectedIndex;

        public void SetSelected(int index) => _selectedIndex = index;

        public ProbeNavRail(params (string Id, string Label, string Icon)[] items)
        {
            foreach (var (_, label, icon) in items) _items.Add((label, icon));
            Width = 208f;
            Background = Theme.Current.Surface;
            ClipToBounds = true;
        }

        public int SelectedIndex
        {
            get => _selectedIndex;
            set { _selectedIndex = value; InvalidateVisual(); }
        }

        protected override Size MeasureOverride(Size available) => new(208, 780);

        protected internal override void OnRender(SKCanvas canvas, Theme theme)
        {
            const float ItemHeight = 44f;
            const float LogoHeight = 84f;

            UiRenderer.FillRoundRect(canvas, 0, 0, FinalRect.Width, LogoHeight, 0, theme.SurfaceAlt);
            IconGlyph.Draw(canvas, "android", 22, 24, 30, theme.Accent);
            UiFonts.DrawText(canvas, "AndroLink", 58, 40, 19, theme.TextPrimary, UiFonts.Weight.Bold);
            UiFonts.DrawText(canvas, "Subsystem for Windows", 58, 58, 10, theme.TextSecondary);

            float y = LogoHeight + 14;
            for (var i = 0; i < _items.Count; i++)
            {
                var (label, icon) = _items[i];
                var selected = i == _selectedIndex;

                if (selected)
                {
                    UiRenderer.FillRoundRect(canvas, 12, y, FinalRect.Width - 24, ItemHeight - 8, 6, theme.Selection);
                    UiRenderer.FillRoundRect(canvas, 12, y + 8, 3, ItemHeight - 24, 1.5f, theme.Accent);
                }

                var textColor = selected ? theme.TextPrimary : theme.TextSecondary;
                IconGlyph.Draw(canvas, icon, 26, y + ItemHeight / 2f - 9, 18, selected ? theme.Accent : theme.TextSecondary);
                UiFonts.DrawTextCenteredY(canvas, label, 58, y + ItemHeight / 2f, 13.5f, textColor,
                    selected ? UiFonts.Weight.SemiBold : UiFonts.Weight.Regular);
                y += ItemHeight;
            }
        }
    }
}
