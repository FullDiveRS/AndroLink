using SkiaSharp;

namespace AndroLink.UI;

/// <summary>Renders a single run of text, optionally wrapped and aligned.</summary>
public class TextBlock : UIElement
{
    private string _text = "";
    private List<(string Text, float Width)> _lines = new() { ("", 0) };
    private bool _wrap;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value ?? "";
            InvalidateMeasure();
        }
    }

    public float FontSize { get; set; } = 13.5f;
    public UiFonts.Weight FontWeight { get; set; } = UiFonts.Weight.Regular;
    public SKColor? TextColor { get; set; }
    public bool Wrapping
    {
        get => _wrap;
        set { _wrap = value; InvalidateMeasure(); }
    }
    public TextAlignment Align { get; set; } = TextAlignment.Left;

    public TextBlock()
    {
        ClipToBounds = false;
        VAlign = VAlignment.Center;
    }

    public TextBlock(string text, float fontSize = 13.5f, UiFonts.Weight weight = UiFonts.Weight.Regular)
    {
        _text = text;
        FontSize = fontSize;
        FontWeight = weight;
        ClipToBounds = false;
        VAlign = VAlignment.Center;
    }

    protected override Size MeasureOverride(Size available)
    {
        var maxWidth = Wrapping ? available.Width : float.PositiveInfinity;
        _lines = UiRenderer.WrapText(_text, FontSize, FontWeight, maxWidth);
        float w = 0;
        foreach (var (_, width) in _lines) w = Math.Max(w, width);
        var h = _lines.Count * UiFonts.LineHeight(FontSize);
        return new Size(w, h);
    }

    protected internal override void OnRender(SKCanvas canvas, Theme theme)
    {
        var color = TextColor ?? theme.TextPrimary;
        var lineHeight = UiFonts.LineHeight(FontSize);
        float y = (FinalRect.Height - _lines.Count * lineHeight) / 2f; // vertical center within own bounds

        using var font = UiFonts.CreateFont(FontSize, FontWeight);
        using var paint = new SKPaint { Color = color, IsAntialias = true };

        foreach (var (line, width) in _lines)
        {
            var x = Align switch
            {
                TextAlignment.Center => (FinalRect.Width - width) / 2f,
                TextAlignment.Right => FinalRect.Width - width,
                _ => 0f,
            };
            // Real font metrics instead of a hardcoded 0.78 ratio: glyph ink is
            // centered in the line box for every size, so labels, badges and
            // button captions on one row line up optically.
            canvas.DrawText(line, Math.Max(0, x), UiFonts.BaselineForCentered(font, y + lineHeight / 2f), font, paint);
            y += lineHeight;
        }
    }
}

public enum TextAlignment { Left, Center, Right }

/// <summary>Invisible flexible spacer for StackPanel-style layouts.</summary>
public sealed class Spacer : UIElement
{
    public Spacer(float size = 0) { Width = size; Height = size; }

    /// <summary>Expands to absorb all leftover width inside a horizontal StackPanel.</summary>
    public static Spacer Fill() => new() { HAlign = HAlignment.Stretch, VAlign = VAlignment.Stretch, HExpand = true };
}

/// <summary>Thin horizontal rule.</summary>
public sealed class Separator : UIElement
{
    protected override Size MeasureOverride(Size available) => new(available.Width is float.PositiveInfinity ? 0 : available.Width, 1);

    protected internal override void OnRender(SKCanvas canvas, Theme theme)
    {
        UiRenderer.DrawLine(canvas, 0, 0.5f, FinalRect.Width, 0.5f, theme.Border);
    }
}
