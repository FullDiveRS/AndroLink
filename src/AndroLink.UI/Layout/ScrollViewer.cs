using SkiaSharp;

namespace AndroLink.UI;

/// <summary>
/// Clips oversized content to the viewport, adds wheel scrolling and a
/// self-drawn scrollbar. Content is measured with unbounded height (or width
/// for future horizontal support), then offset at render time.
/// </summary>
public class ScrollViewer : UIElement
{
    private UIElement? _content;

    public UIElement? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value)) return;
            if (_content is not null) _content.Parent = null;
            _content = value;
            if (_content is not null) _content.Parent = this;
            InvalidateMeasure();
        }
    }

    public float ScrollSpeed { get; set; } = 48f;
    public float ScrollOffsetY { get; set; }

    internal float ContentHeight { get; private set; }
    internal float ViewportHeight { get; private set; }
    public bool NeedsScroll => ContentHeight > ViewportHeight + 0.5f;

    private const float ScrollbarWidth = 6f;

    protected internal override IEnumerable<UIElement> VisualChildren
        => _content is null ? Enumerable.Empty<UIElement>() : new[] { _content };

    protected override Size MeasureOverride(Size available)
    {
        if (_content is null) return Size.Empty;

        var width = float.IsPositiveInfinity(available.Width) ? float.PositiveInfinity : available.Width;
        _content.MeasureInternal(new Size(width, float.PositiveInfinity));
        ContentHeight = _content.DesiredSize.Height;

        var desiredHeight = float.IsPositiveInfinity(available.Height)
            ? ContentHeight
            : Math.Min(available.Height, Math.Max(ContentHeight, 0));

        return new Size(
            float.IsPositiveInfinity(available.Width) ? _content.DesiredSize.Width : available.Width,
            desiredHeight);
    }

    protected override void ArrangeOverride(Rect contentRect)
    {
        if (_content is null) return;
        ViewportHeight = contentRect.Height;
        ClampOffset();

        // The content keeps its full desired height and is shifted up by the offset.
        _content.ArrangeInternal(new Rect(contentRect.X, contentRect.Y - ScrollOffsetY, contentRect.Width, ContentHeight));
    }

    public void SetScrollOffset(float offset)
    {
        ScrollOffsetY = offset;
        ClampOffset();
        InvalidateVisual();
    }

    public void ScrollToTop() => SetScrollOffset(0);
    public void ScrollToEnd() => SetScrollOffset(float.PositiveInfinity);

    private void ClampOffset()
    {
        var max = Math.Max(0, ContentHeight - ViewportHeight);
        ScrollOffsetY = float.IsPositiveInfinity(ScrollOffsetY) ? max : Math.Clamp(ScrollOffsetY, 0, max);
    }

    protected override void OnPointerWheel(WheelEventArgs args)
    {
        if (!NeedsScroll) return;
        SetScrollOffset(ScrollOffsetY - args.Delta * ScrollSpeed / 4f);
        args.Handled = true;
    }

    protected internal override void OnRenderOver(SKCanvas canvas, Theme theme)
    {
        // Drawn after the children: the thumb must stay visible above the
        // full-width row backgrounds inside the viewport.
        if (!NeedsScroll) return;

        var trackX = FinalRect.Width - ScrollbarWidth - 2;
        var ratio = ViewportHeight / ContentHeight;
        var thumbH = Math.Max(28, ViewportHeight * ratio);
        var maxOffset = Math.Max(1, ContentHeight - ViewportHeight);
        var thumbY = (FinalRect.Height - thumbH) * (ScrollOffsetY / maxOffset);

        UiRenderer.FillRoundRect(canvas, trackX, thumbY, ScrollbarWidth, thumbH, ScrollbarWidth / 2, theme.ScrollbarThumb);
    }
}
