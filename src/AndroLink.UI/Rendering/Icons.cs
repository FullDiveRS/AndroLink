using SkiaSharp;

namespace AndroLink.UI;

/// <summary>
/// Font-independent vector icon set drawn on a 24x24 design grid with
/// SkiaSharp paths. Icons are tintable and render identically on every
/// machine - the framework never depends on emoji or symbol fonts (which was
/// the cause of "empty square" icons on stock Windows).
/// </summary>
public static class IconGlyph
{
    private const float DesignSize = 24f;
    private const float StrokeWidth = 1.8f;

    private static readonly Dictionary<string, (string Stroke, string Fill)> Paths;
    private static readonly Dictionary<string, SKPath> StrokeCache = new();
    private static readonly Dictionary<string, SKPath> FillCache = new();

    static IconGlyph()
    {
        string RRect(double x, double y, double w, double h, double r)
        {
            var iw = Math.Max(0, w - 2 * r);
            var ih = Math.Max(0, h - 2 * r);
            return FormattableString.Invariant(
                $"M{x + r},{y}h{iw}a{r},{r} 0 0 1 {r},{r}v{ih}a{r},{r} 0 0 1 -{r},{r}h-{iw}a{r},{r} 0 0 1 -{r},-{r}v-{ih}a{r},{r} 0 0 1 {r},-{r}z");
        }

        string Circle(double cx, double cy, double r)
            => FormattableString.Invariant(
                $"M{cx},{cy - r}a{r},{r} 0 1 0 {2 * r},0a{r},{r} 0 1 0 -{2 * r},0z");

        Paths = new Dictionary<string, (string, string)>
        {
            // Smartphone - the Devices page.
            ["phone"] = (
                "M8.5,2.75h7a2,2 0 0 1 2,2v14.5a2,2 0 0 1 -2,2h-7a2,2 0 0 1 -2,-2v-14.5a2,2 0 0 1 2,-2z" +
                "M10.2,18.4h3.6", ""),

            // App grid - the Apps page.
            ["apps"] = (
                RRect(4, 4, 7, 7, 1.6f) + RRect(13, 4, 7, 7, 1.6f) +
                RRect(4, 13, 7, 7, 1.6f) + RRect(13, 13, 7, 7, 1.6f), ""),

            // Folder - the Files page.
            ["folder"] = (
                "M3.5,7.2c0,-1.1 0.9,-2 2,-2h3.2l2,2.2h7.8c1.1,0 2,0.9 2,2v7.4c0,1.1 -0.9,2 -2,2h-13c-1.1,0 -2,-0.9 -2,-2z", ""),

            // Sliders - the Settings page.
            ["settings"] = (
                "M4,7h2.6M11.4,7H20M4,12h8.6M17.4,12H20M4,17h0.6M9.4,17H20",
                Circle(9, 7, 2.2f) + Circle(15, 12, 2.2f) + Circle(7, 17, 2.2f)),

            // Down arrow into a tray - system image downloads.
            ["download"] = (
                "M12,3.5v10.4M7.5,9.4L12,13.9l4.5,-4.5" +
                "M4.5,16.5v2c0,1.1 0.9,2 2,2h11c1.1,0 2,-0.9 2,-2v-2", ""),

            ["play"] = ("", "M8.6,5.6v12.8L18.4,12z"),
            ["stop"] = ("", RRect(7, 7, 10, 10, 1.8f)),
            ["trash"] = (
                "M4.5,6.5h15M9.5,6.5V4.8c0,-0.7 0.6,-1.3 1.3,-1.3h2.4c0.7,0 1.3,0.6 1.3,1.3v1.7" +
                "M6.5,6.5l0.7,12.1c0.1,1.1 1,1.9 2,1.9h5.6c1,0 1.9,-0.8 2,-1.9l0.7,-12.1" +
                "M10,10.5v6M14,10.5v6", ""),
            ["plus"] = ("M12,5v14M5,12h14", ""),
            ["refresh"] = ("M21.5,4.5v5.5h-5.5M19.6,14.2a7.6,7.6 0 1 1 -1.8,-7.9L21.5,10", ""),

            // Android robot head - product logo.
            ["android"] = (
                "M8.7,7.6L7.1,5.4M15.3,7.6l1.6,-2.2" +
                RRect(5, 10.9, 14, 7.4, 1.8f),
                Circle(9.4, 14.6, 0.95f) + Circle(14.6, 14.6, 0.95f)),

            // CPU chip - architecture / backend info.
            ["chip"] = (
                RRect(7, 7, 10, 10, 2) +
                "M9.5,7V4.8M14.5,7V4.8M9.5,19.2V17M14.5,19.2V17M7,9.5H4.8M7,14.5H4.8M19.2,9.5H17M19.2,14.5H17",
                RRect(10.4, 10.4, 3.2, 3.2, 0.6f)),

            ["memory"] = (
                "M5.5,8h13c0.8,0 1.5,0.7 1.5,1.5V15H4V9.5C4,8.7 4.7,8 5.5,8z" +
                "M8,15v2.2M12,15v2.2M16,15v2.2M8,10.8v1.7M12,10.8v1.7M16,10.8v1.7", ""),
            ["display"] = (
                "M4.5,4.5h15A1.5,1.5 0 0 1 21,6v7a1.5,1.5 0 0 1 -1.5,1.5h-15A1.5,1.5 0 0 1 3,13V6a1.5,1.5 0 0 1 1.5,-1.5z" +
                "M9,20.5h6M12,14.5v6", ""),
            ["check"] = ("M4.5,12.6l4.7,4.7L19.5,6.9", ""),
            ["warning"] = (
                "M12,3.8L21,19.5H3zM12,9.8v4.2",
                Circle(12, 17.2, 1.1f)),
            ["chevron-right"] = ("M9,5.5l6.5,6.5L9,18.5", ""),
            ["open-folder"] = (
                "M14,5.5h5v5M19,5.5l-8,8" +
                "M18.2,13.5V18a1.6,1.6 0 0 1 -1.6,1.6H6A1.6,1.6 0 0 1 4.4,18V7.6A1.6,1.6 0 0 1 6,6h4.5", ""),

            // Archive box - a downloaded Android system image.
            ["package"] = (
                "M12,3l8,4.5v9L12,21l-8,-4.5v-9zM4,7.5l8,4.5l8,-4.5M12,12v9", ""),
            ["power"] = ("M12,3.5v8M6.2,6.9a8,8 0 1 0 11.6,0", ""),
            ["x"] = ("M6,6l12,12M18,6L6,18", ""),
            ["search"] = (Circle(10.5, 10.5, 6) + "M15.2,15.2L20,20", ""),
            ["image"] = (
                RRect(3.5, 5, 17, 14, 2) + "M6.5,18.5l5,-5l3,3l2.5,-2.5l3,3",
                Circle(8.6, 9.4, 1.5f)),
            ["shield"] = ("M12,3.4l7,2.5v5.4c0,4.6 -2.9,7.7 -7,9.3c-4.1,-1.6 -7,-4.7 -7,-9.3V5.9z", ""),
            ["info"] = (Circle(12, 12, 8.4) + "M12,11v5.2", Circle(12, 7.9, 1.05f)),
        };
    }

    public static IReadOnlyCollection<string> Names => Paths.Keys;

    public static bool Exists(string name) => Paths.ContainsKey(name);

    /// <summary>Draws the named icon with its top-left corner at (x, y), scaled to `size` pixels.</summary>
    public static void Draw(SKCanvas canvas, string name, float x, float y, float size, SKColor color)
    {
        if (size <= 0 || !Paths.TryGetValue(name, out var spec)) return;

        var strokePath = GetStrokePath(name, spec.Stroke);
        var fillPath = GetFillPath(name, spec.Fill);

        canvas.Save();
        canvas.Translate(x, y);
        var scale = size / DesignSize;
        canvas.Scale(scale, scale);

        if (strokePath is not null)
        {
            using var stroke = new SKPaint
            {
                Color = color,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = StrokeWidth,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
            };
            canvas.DrawPath(strokePath, stroke);
        }

        if (fillPath is not null)
        {
            using var fill = new SKPaint { Color = color, IsAntialias = true };
            canvas.DrawPath(fillPath, fill);
        }

        canvas.Restore();
    }

    private static SKPath? GetStrokePath(string name, string data)
    {
        if (data.Length == 0) return null;
        if (StrokeCache.TryGetValue(name, out var cached)) return cached;
        var path = SKPath.ParseSvgPathData(data);
        StrokeCache[name] = path;
        return path;
    }

    private static SKPath? GetFillPath(string name, string data)
    {
        if (data.Length == 0) return null;
        var key = name + "#fill";
        if (FillCache.TryGetValue(key, out var cached)) return cached;
        var path = SKPath.ParseSvgPathData(data);
        FillCache[key] = path;
        return path;
    }
}

/// <summary>Standalone icon element (measures to a square). Tint falls back to the theme foreground.</summary>
public sealed class Icon : UIElement
{
    public string IconName { get; set; }
    public float IconSize { get; set; }
    public SKColor? Tint { get; set; }

    public Icon(string name, float size = 18f)
    {
        IconName = name;
        IconSize = size;
        Width = size;
        Height = size;
        ClipToBounds = false;
    }

    protected override Size MeasureOverride(Size available) => new(IconSize, IconSize);

    protected internal override void OnRender(SKCanvas canvas, Theme theme)
    {
        var size = Math.Min(Math.Min(FinalRect.Width, FinalRect.Height), IconSize);
        if (size <= 0) size = IconSize;
        IconGlyph.Draw(canvas, IconName, (FinalRect.Width - size) / 2f, (FinalRect.Height - size) / 2f, size,
            Tint ?? theme.TextPrimary);
    }
}
