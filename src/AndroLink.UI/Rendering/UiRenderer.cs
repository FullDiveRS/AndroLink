using SkiaSharp;

namespace AndroLink.UI;

/// <summary>
/// Tree renderer + shared drawing primitives. Every frame the host calls
/// <see cref="RenderTree"/> on the window root; each element gets a translated,
/// optionally clipped and alpha-layered canvas.
/// </summary>
public static class UiRenderer
{
    public static void RenderTree(UIElement element, SKCanvas canvas)
    {
        if (!element.Visible || element.Opacity <= 0) return;
        var theme = Theme.Current;
        var rect = element.FinalRect;

        canvas.Save();
        canvas.Translate(rect.X, rect.Y);

        var alpha = (byte)Math.Clamp(element.Opacity * 255, 0, 255);
        if (alpha < 255)
        {
            using var layerPaint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
            canvas.SaveLayer(layerPaint);
        }

        var localBounds = new SKRect(0, 0, rect.Width, rect.Height);

        if (element.Background is { } bg)
        {
            using var paint = new SKPaint { Color = bg, IsAntialias = true };
            canvas.DrawRoundRect(0, 0, rect.Width, rect.Height, element.CornerRadius.Uniform, element.CornerRadius.Uniform, paint);
        }

        element.OnRender(canvas, theme);

        if (element.BorderBrush is { } border && element.BorderThickness > 0)
        {
            using var paint = new SKPaint
            {
                Color = border,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = element.BorderThickness,
            };
            var inset = element.BorderThickness / 2f;
            canvas.DrawRoundRect(inset, inset,
                Math.Max(0, rect.Width - element.BorderThickness),
                Math.Max(0, rect.Height - element.BorderThickness),
                Math.Max(0, element.CornerRadius.Uniform - inset),
                Math.Max(0, element.CornerRadius.Uniform - inset), paint);
        }

        if (element.ClipToBounds)
        {
            canvas.ClipRoundRect(new SKRoundRect(localBounds, element.CornerRadius.Uniform), antialias: true);
        }

        foreach (var child in element.VisualChildren)
        {
            if (!child.Visible) continue;
            RenderTree(child, canvas);
        }

        element.OnRenderOver(canvas, theme);

        if (alpha < 255) canvas.Restore();
        canvas.Restore();
    }

    // ------------------------------------------------------------- primitives

    public static void FillRoundRect(SKCanvas canvas, float x, float y, float w, float h, float radius, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        canvas.DrawRoundRect(x, y, w, h, radius, radius, paint);
    }

    public static void StrokeRoundRect(SKCanvas canvas, float x, float y, float w, float h, float radius, SKColor color, float strokeWidth)
    {
        using var paint = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
        };
        var inset = strokeWidth / 2f;
        canvas.DrawRoundRect(x + inset, y + inset, Math.Max(0, w - strokeWidth), Math.Max(0, h - strokeWidth),
            Math.Max(0, radius - inset), Math.Max(0, radius - inset), paint);
    }

    public static void DrawSoftShadow(SKCanvas canvas, float x, float y, float w, float h, float radius, SKColor color)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(14, 14),
            Color = color,
        };
        canvas.DrawRoundRect(x, y + 2, w, h, radius, radius, paint);
    }

    public static void DrawLine(SKCanvas canvas, float x1, float y1, float x2, float y2, SKColor color, float strokeWidth = 1f)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true, StrokeWidth = strokeWidth };
        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    public static SKColor Lerp(SKColor a, SKColor b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new SKColor(
            (byte)(a.Red + (b.Red - a.Red) * t),
            (byte)(a.Green + (b.Green - a.Green) * t),
            (byte)(a.Blue + (b.Blue - a.Blue) * t),
            (byte)(a.Alpha + (b.Alpha - a.Alpha) * t));
    }

    /// <summary>Word-wraps text into lines that fit maxWidth; returns lines with their pixel widths.</summary>
    public static List<(string Text, float Width)> WrapText(string text, float fontSize, UiFonts.Weight weight, float maxWidth)
    {
        var lines = new List<(string, float)>();
        if (string.IsNullOrEmpty(text))
        {
            lines.Add(("", 0));
            return lines;
        }

        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (maxWidth <= 0 || UiFonts.MeasureWidth(rawLine, fontSize, weight) <= maxWidth)
            {
                lines.Add((rawLine, UiFonts.MeasureWidth(rawLine, fontSize, weight)));
                continue;
            }

            var current = "";
            foreach (var word in SplitWords(rawLine, fontSize, weight, maxWidth))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                var width = UiFonts.MeasureWidth(candidate, fontSize, weight);
                if (width <= maxWidth || current.Length == 0)
                {
                    current = candidate;
                }
                else
                {
                    lines.Add((current, UiFonts.MeasureWidth(current, fontSize, weight)));
                    current = word;
                }
            }
            lines.Add((current, UiFonts.MeasureWidth(current, fontSize, weight)));
        }
        return lines;
    }

    private static IEnumerable<string> SplitWords(string line, float fontSize, UiFonts.Weight weight, float maxWidth)
    {
        // Break on spaces, hard-breaking words that alone exceed maxWidth.
        foreach (var word in line.Split(' '))
        {
            if (word.Length == 0) continue;
            if (UiFonts.MeasureWidth(word, fontSize, weight) <= maxWidth)
            {
                yield return word;
            }
            else
            {
                var chunk = "";
                foreach (var ch in word)
                {
                    if (chunk.Length > 0 && UiFonts.MeasureWidth(chunk + ch, fontSize, weight) > maxWidth)
                    {
                        yield return chunk;
                        chunk = ch.ToString();
                    }
                    else
                    {
                        chunk += ch;
                    }
                }
                if (chunk.Length > 0) yield return chunk;
            }
        }
    }
}
