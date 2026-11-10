using SkiaSharp;

namespace AndroLink.UI;

/// <summary>Standard push button with hover/press states and keyboard activation.</summary>
public class Button : UIElement
{
    private UIElement? _content;
    private bool _isPressed;
    private float _hover; // animated 0..1

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

    /// <summary>Text convenience: sets/creates an internal TextBlock as content.</summary>
    public string Text
    {
        get => (_content as TextBlock)?.Text ?? "";
        set
        {
            if (_content is TextBlock tb)
            {
                tb.Text = value;
            }
            else
            {
                Content = new TextBlock(value, Theme.Current.BaseFontSize, UiFonts.Weight.SemiBold);
            }
        }
    }

    public bool IsPrimary { get; set; }
    public bool IsDanger { get; set; }

    /// <summary>Optional vector icon (see IconGlyph) drawn left of the content.</summary>
    public string? IconName { get; set; }
    private const float IconSlot = 16f;
    private const float IconGap = 7f;

    public event Action? Click;

    public Button() { }

    public Button(string text, Action? onClick = null, bool primary = false)
    {
        Text = text;
        IsPrimary = primary;
        Click += onClick;
        Padding = new Thickness(14, 8);
        Focusable = true;
        CornerRadius = new CornerRadius(6);
        ClipToBounds = false;
    }

    /// <summary>Convenience factory: button with a leading vector icon.</summary>
    public static Button WithIcon(string icon, string text, Action? onClick = null, bool primary = false)
        => new(text, onClick, primary) { IconName = icon };

    /// <summary>Content participates in rendering and hit-testing (previously it was measured but never drawn).</summary>
    protected internal override IEnumerable<UIElement> VisualChildren
        => _content is null ? Enumerable.Empty<UIElement>() : new[] { _content };

    /// <summary>The whole button (including its label) is one hit target.</summary>
    protected internal override bool IsHitTestLeaf => true;

    protected override Size MeasureOverride(Size available)
    {
        if (_content is null) return IconName is null ? new Size(0, 0) : new Size(IconSlot, 30);
        _content.MeasureInternal(new Size(
            float.IsPositiveInfinity(available.Width) ? float.PositiveInfinity : Math.Max(0, available.Width - 8),
            float.PositiveInfinity));
        var d = _content.DesiredSize;
        var w = IconName is null ? d.Width : d.Width + IconGap + IconSlot;
        return new Size(w, Math.Max(30, d.Height));
    }

    protected override void ArrangeOverride(Rect contentRect)
    {
        if (_content is null) return;

        if (IconName is null)
        {
            _content.ArrangeInternal(UIElement.AlignChild(contentRect, _content.DesiredSize, HAlignment.Center, VAlignment.Center));
            return;
        }

        // Icon + content centered as a group.
        var total = _content.DesiredSize.Width + IconGap + IconSlot;
        var startX = contentRect.X + Math.Max(0, (contentRect.Width - total) / 2);
        var iconY = contentRect.Y + (contentRect.Height - IconSlot) / 2;
        _iconRect = new Rect(startX, iconY, IconSlot, IconSlot);
        var textSlot = new Rect(startX + IconGap + IconSlot, contentRect.Y,
            Math.Max(0, contentRect.Right - (startX + IconGap + IconSlot)), contentRect.Height);
        _content.ArrangeInternal(UIElement.AlignChild(textSlot, _content.DesiredSize, HAlignment.Left, VAlignment.Center));
    }

    private Rect _iconRect;

    protected override void OnPointerEnter(PointerEventArgs args)
    {
        AnimateHover(1);
    }

    protected override void OnPointerExit(PointerEventArgs args)
    {
        _isPressed = false;
        AnimateHover(0);
    }

    private void AnimateHover(float target)
    {
        var from = _hover;
        var owner = this;
        Animator.Animate(from, target, TimeSpan.FromMilliseconds(120), v =>
        {
            _hover = (float)v;
        }, Easing.EaseOut, owner: owner);
    }

    protected override void OnPointerPress(PointerEventArgs args)
    {
        _isPressed = true;
        InvalidateVisual();
        args.Handled = true;
    }

    protected override void OnPointerRelease(PointerEventArgs args)
    {
        var wasPressed = _isPressed;
        _isPressed = false;
        InvalidateVisual();
        if (wasPressed && args.IsOverTarget)
        {
            args.Handled = true;
            Click?.Invoke();
        }
    }

    protected override void OnKey(KeyEventArgs args)
    {
        if (args.Key is VirtualKey.Enter or VirtualKey.Space && !args.Handled)
        {
            args.Handled = true;
            Click?.Invoke();
        }
    }

    protected internal override void OnRender(SKCanvas canvas, Theme theme)
    {
        SKColor fill;
        SKColor textColor;
        if (IsDanger)
        {
            fill = _isPressed ? theme.Danger.WithAlpha(220) : _hover > 0.5f ? theme.Danger : theme.Danger.WithAlpha(200);
            textColor = theme.AccentText;
        }
        else if (IsPrimary)
        {
            fill = _isPressed ? theme.AccentPressed : _hover > 0.01f ? theme.AccentHover : theme.Accent;
            textColor = theme.AccentText;
        }
        else
        {
            fill = _isPressed ? theme.SurfaceAlt : UiRenderer.Lerp(theme.Surface, theme.SurfaceHover, _hover);
            textColor = theme.TextPrimary;
        }

        UiRenderer.FillRoundRect(canvas, 0, 0, FinalRect.Width, FinalRect.Height, CornerRadius.Uniform, fill);
        UiRenderer.StrokeRoundRect(canvas, 0, 0, FinalRect.Width, FinalRect.Height, CornerRadius.Uniform, theme.Border, 1f);

        if (_content is TextBlock label)
        {
            label.TextColor = textColor;
        }

        if (IconName is not null)
        {
            var size = Math.Min(IconSlot, _iconRect.Width > 0 ? _iconRect.Width : IconSlot);
            IconGlyph.Draw(canvas, IconName, _iconRect.X, _iconRect.Y, size, textColor);
        }
    }
}
