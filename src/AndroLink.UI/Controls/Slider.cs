using SkiaSharp;

namespace AndroLink.UI;

/// <summary>Horizontal slider with animated thumb and drag interaction.</summary>
public class Slider : UIElement
{
    private double _minimum;
    private double _maximum = 100;
    private double _value;
    private bool _dragging;
    private float _hover;

    public double Minimum { get => _minimum; set { _minimum = value; if (_value < value) Value = value; } }
    public double Maximum { get => _maximum; set { _maximum = value; if (_value > value) Value = value; } }

    public double Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _minimum, _maximum);
            if (Math.Abs(_value - clamped) < 0.0001) return;
            _value = clamped;
            InvalidateVisual();
            ValueChanged?.Invoke();
        }
    }

    public event Action? ValueChanged;

    /// <summary>Self-drawn thumb/track: one hit target.</summary>
    protected internal override bool IsHitTestLeaf => true;

    /// <summary>Manually raises ValueChanged (used to sync labels right after construction).</summary>
    public void RaiseValueChanged() => ValueChanged?.Invoke();

    public Slider(double min = 0, double max = 100, double value = 0)
    {
        _minimum = min;
        _maximum = max;
        _value = value;
        ClipToBounds = false;
    }

    protected override Size MeasureOverride(Size available)
        => new(float.IsPositiveInfinity(available.Width) ? 140 : available.Width, 28);

    private const float TrackHeight = 4f;
    private const float ThumbDiameter = 14f;

    private float ValueToX()
    {
        var range = Math.Max(0.0001, _maximum - _minimum);
        var frac = (_value - _minimum) / range;
        var travel = FinalRect.Width - ThumbDiameter;
        return (float)(frac * travel);
    }

    private void UpdateFromPointer(float localX)
    {
        var travel = Math.Max(1, FinalRect.Width - ThumbDiameter);
        var frac = Math.Clamp((localX - ThumbDiameter / 2) / travel, 0, 1);
        Value = _minimum + frac * (_maximum - _minimum);
    }

    protected override void OnPointerPress(PointerEventArgs args)
    {
        _dragging = true;
        UpdateFromPointer(args.X);
        args.Handled = true;
    }

    protected override void OnPointerMove(PointerEventArgs args)
    {
        if (!_dragging) return;
        UpdateFromPointer(args.X);
        args.Handled = true;
    }

    protected override void OnPointerRelease(PointerEventArgs args)
    {
        _dragging = false;
        args.Handled = true;
    }

    protected override void OnPointerEnter(PointerEventArgs args) => AnimateHover(1);
    protected override void OnPointerExit(PointerEventArgs args) { _dragging = false; AnimateHover(0); }

    private void AnimateHover(float target)
    {
        var from = _hover;
        var owner = this;
        Animator.Animate(from, target, TimeSpan.FromMilliseconds(120), v => _hover = (float)v, Easing.EaseOut, owner: owner);
    }

    protected internal override void OnRender(SKCanvas canvas, Theme theme)
    {
        var trackY = FinalRect.Height / 2f - TrackHeight / 2f;
        var thumbX = ValueToX();

        UiRenderer.FillRoundRect(canvas, 0, trackY, FinalRect.Width, TrackHeight, TrackHeight / 2, theme.SurfaceAlt);
        var fillW = Math.Max(TrackHeight, thumbX + ThumbDiameter / 2);
        UiRenderer.FillRoundRect(canvas, 0, trackY, fillW, TrackHeight, TrackHeight / 2, theme.Accent);

        var thumbY = FinalRect.Height / 2f - ThumbDiameter / 2;
        var thumbColor = UiRenderer.Lerp(theme.TextSecondary, theme.TextPrimary, _hover);
        UiRenderer.FillRoundRect(canvas, thumbX, thumbY, ThumbDiameter, ThumbDiameter, ThumbDiameter / 2, thumbColor);
    }
}
