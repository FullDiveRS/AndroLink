using SkiaSharp;

namespace AndroLink.UI;

/// <summary>
/// Font resolution + text metrics for the framework. Typefaces are cached per
/// (family, weight); the default family follows the host OS (Segoe UI on
/// Windows, Noto Sans elsewhere) so text renders everywhere.
/// </summary>
public static class UiFonts
{
    public enum Weight { Regular, SemiBold, Bold }

    private static readonly Dictionary<(string Family, Weight Weight), SKTypeface> Cache = new();
    private static readonly object Gate = new();

    public static string DefaultFamily { get; set; } = DetectDefaultFamily();

    private static string DetectDefaultFamily()
    {
        foreach (var candidate in new[] { "Segoe UI", "Noto Sans", "DejaVu Sans", "Arial" })
        {
            using var probe = SKTypeface.FromFamilyName(candidate);
            if (probe is not null && probe.FamilyName == candidate) return candidate;
        }
        using var fallback = SKTypeface.FromFamilyName(null);
        return fallback?.FamilyName ?? "sans-serif";
    }

    public static SKTypeface Resolve(Weight weight) => Resolve(DefaultFamily, weight);

    public static SKTypeface Resolve(string family, Weight weight)
    {
        lock (Gate)
        {
            var key = (family, weight);
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var skiaWeight = weight switch
            {
                Weight.Bold => SKFontStyleWeight.Bold,
                Weight.SemiBold => SKFontStyleWeight.SemiBold,
                _ => SKFontStyleWeight.Normal,
            };
            var style = new SKFontStyle(skiaWeight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
            var typeface = SKTypeface.FromFamilyName(family, style) ?? SKTypeface.Default;
            Cache[key] = typeface;
            return typeface;
        }
    }

    public static SKFont CreateFont(float size, Weight weight = Weight.Regular)
        => new(Resolve(weight), size);

    public static float MeasureWidth(string text, float size, Weight weight = Weight.Regular)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        using var paint = new SKPaint
        {
            Typeface = Resolve(weight),
            TextSize = size,
            IsAntialias = true,
        };
        return paint.MeasureText(text);
    }

    public static float LineHeight(float size) => size * 1.35f;

    public static void DrawText(SKCanvas canvas, string text, float x, float y, float size,
        SKColor color, Weight weight = Weight.Regular, float maxWidth = float.MaxValue)
    {
        using var font = CreateFont(size, weight);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, x, y, font, paint);
    }

    /// <summary>
    /// Draws text whose glyph ink (ascent..descent span) is vertically centered
    /// on <paramref name="centerY"/>. Call sites previously hardcoded baseline
    /// offsets (0.78 × line height etc.) which made labels sit 1-3px off their
    /// icons and made different font sizes on one row disagree.
    /// </summary>
    public static void DrawTextCenteredY(SKCanvas canvas, string text, float x, float centerY, float size,
        SKColor color, Weight weight = Weight.Regular)
    {
        using var font = CreateFont(size, weight);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawText(text, x, BaselineForCentered(font, centerY), font, paint);
    }

    /// <summary>Baseline y that vertically centers the font's ascent..descent span on centerY.</summary>
    public static float BaselineForCentered(SKFont font, float centerY)
    {
        var metrics = font.Metrics;
        // Span center = baseline + (Descent - |Ascent|) / 2  =>  baseline = centerY + (|Ascent| - Descent) / 2.
        return centerY + (-metrics.Ascent - metrics.Descent) / 2f;
    }
}
