using SkiaSharp;

namespace AndroLink.UI;

/// <summary>Checkbox with label.</summary>
public class CheckBox : UIElement
{
    private bool _isChecked;
    private const float BoxSize = 17f;

    public string Label { get; set; } = "";
    public float FontSize { get; set; } = 13.5f;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            InvalidateVisual();
            CheckedChanged?.Invoke();
        }
    }

    public event Action? CheckedChanged;

    /// <summary>Label hit = checkbox hit.</summary>
    protected internal override bool IsHitTestLeaf => true;

    public CheckBox(string label = "", bool isChecked = false)
    {
        Label = label;
        _isChecked = isChecked;
        Focusable = true;
        ClipToBounds = false;
        IsHitTestVisible = true;
    }

    protected override Size MeasureOverride(Size available)
    {
        var textWidth = string.IsNullOrEmpty(Label) ? 0 : UiFonts.MeasureWidth(Label, FontSize);
        return new Size(BoxSize + (textWidth > 0 ? 8 + textWidth : 0), Math.Max(BoxSize, UiFonts.LineHeight(FontSize)));
    }

    protected override void OnPointerPress(PointerEventArgs args)
    {
        args.Handled = true;
    }

    protected override void OnPointerRelease(PointerEventArgs args)
    {
        if (!args.IsOverTarget) return;
        args.Handled = true;
        IsChecked = !IsChecked;
        this.Focus();
    }

    protected override void OnKey(KeyEventArgs args)
    {
        if (args.Key is VirtualKey.Space or VirtualKey.Enter && !args.Handled)
        {
            args.Handled = true;
            IsChecked = !IsChecked;
        }
    }

    protected internal override void OnRender(SKCanvas canvas, Theme theme)
    {
        var boxY = (FinalRect.Height - BoxSize) / 2f;
        if (_isChecked)
        {
            UiRenderer.FillRoundRect(canvas, 0, boxY, BoxSize, BoxSize, 4, theme.Accent);
            using var checkPaint = new SKPaint
            {
                Color = theme.AccentText,
                IsAntialias = true,
                StrokeWidth = 2.2f,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
            };
            canvas.DrawLine(4.5f, boxY + BoxSize / 2f, BoxSize / 2f - 0.5f, boxY + BoxSize - 5, checkPaint);
            canvas.DrawLine(BoxSize / 2f - 0.5f, boxY + BoxSize - 5, BoxSize - 4.5f, boxY + 5, checkPaint);
        }
        else
        {
            UiRenderer.FillRoundRect(canvas, 0, boxY, BoxSize, BoxSize, 4, theme.Surface);
            UiRenderer.StrokeRoundRect(canvas, 0, boxY, BoxSize, BoxSize, 4, theme.Border, 1.4f);
        }

        if (!string.IsNullOrEmpty(Label))
        {
            UiFonts.DrawTextCenteredY(canvas, Label, BoxSize + 8, FinalRect.Height / 2f, FontSize,
                theme.TextPrimary);
        }
    }
}

/// <summary>Animated on/off switch.</summary>
public class ToggleSwitch : UIElement
{
    private bool _isOn;
    private float _knob; // 0..1 animated

    private const float TrackW = 40f;
    private const float TrackH = 22f;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            AnimateKnob(value ? 1 : 0);
            Toggled?.Invoke();
        }
    }

    public string? Label { get; set; }

    public event Action? Toggled;

    /// <summary>Self-drawn control: one hit target.</summary>
    protected internal override bool IsHitTestLeaf => true;

    public ToggleSwitch(string? label = null, bool isOn = false)
    {
        Label = label;
        _isOn = isOn;
        _knob = isOn ? 1 : 0;
        ClipToBounds = false;
    }

    private void AnimateKnob(float target)
    {
        var from = _knob;
        var owner = this;
        Animator.Animate(from, target, TimeSpan.FromMilliseconds(140), v => _knob = (float)v, Easing.EaseOut, owner: owner);
    }

    protected override Size MeasureOverride(Size available)
    {
        var labelW = string.IsNullOrEmpty(Label) ? 0 : UiFonts.MeasureWidth(Label, 13.5f);
        return new Size(TrackW + (labelW > 0 ? 10 + labelW : 0), Math.Max(TrackH, UiFonts.LineHeight(13.5f)));
    }

    protected override void OnPointerPress(PointerEventArgs args) => args.Handled = true;

    protected override void OnPointerRelease(PointerEventArgs args)
    {
        if (!args.IsOverTarget) return;
        args.Handled = true;
        IsOn = !IsOn;
    }

    protected internal override void OnRender(SKCanvas canvas, Theme theme)
    {
        var trackY = (FinalRect.Height - TrackH) / 2f;
        var trackColor = UiRenderer.Lerp(theme.Border, theme.Accent, _knob);
        UiRenderer.FillRoundRect(canvas, 0, trackY, TrackW, TrackH, TrackH / 2, trackColor);

        var knobDiameter = TrackH - 6;
        var knobX = 3f + _knob * (TrackW - knobDiameter - 6);
        var knobY = trackY + 3;
        UiRenderer.FillRoundRect(canvas, knobX, knobY, knobDiameter, knobDiameter, knobDiameter / 2, theme.TextPrimary);

        if (!string.IsNullOrEmpty(Label))
        {
            UiFonts.DrawTextCenteredY(canvas, Label!, TrackW + 10, FinalRect.Height / 2f, 13.5f, theme.TextPrimary);
        }
    }
}

/// <summary>Determinate / indeterminate progress bar.</summary>
public class ProgressBar : UIElement
{
    private double _value = -1; // -1 = indeterminate
    private float _pulse;

    public double Minimum { get; set; }
    public double Maximum { get; set; } = 100;
    public double Value
    {
        get => _value;
        set
        {
            if (Math.Abs(_value - value) < 0.001) return;
            _value = value;
            InvalidateVisual();
        }
    }

    public bool IsIndeterminate
    {
        get => _value < 0;
        set
        {
            var target = value ? -1.0 : 0.0;
            if (Math.Abs(_value - target) < 0.001) return;
            _value = target;
            if (value) StartPulse();
            InvalidateVisual();
        }
    }

    public float BarHeight { get; set; } = 6f;

    public ProgressBar() => ClipToBounds = false;

    private void StartPulse()
    {
        var owner = this;
        Animator.Animate(0, 1, TimeSpan.FromMilliseconds(1400), v =>
        {
            _pulse = (float)v;
            if (!IsIndeterminate) return;
        }, Easing.Linear, owner: owner);
    }

    protected override Size MeasureOverride(Size available)
        => new(float.IsPositiveInfinity(available.Width) ? 120 : available.Width, BarHeight);

    protected internal override void OnRender(SKCanvas canvas, Theme theme)
    {
        var h = BarHeight;
        var y = (FinalRect.Height - h) / 2f;
        UiRenderer.FillRoundRect(canvas, 0, y, FinalRect.Width, h, h / 2, theme.SurfaceAlt);

        if (IsIndeterminate)
        {
            var span = FinalRect.Width * 0.3f;
            var travel = FinalRect.Width + span;
            var x = -span + (_pulse % 1f) * travel;
            UiRenderer.FillRoundRect(canvas, Math.Max(0, x), y, Math.Min(span, FinalRect.Width - Math.Max(0, x)), h, h / 2, theme.Accent);
            if (!Animator.HasActiveAnimations(this)) StartPulse();
        }
        else
        {
            var range = Math.Max(0.001, Maximum - Minimum);
            var frac = Math.Clamp((_value - Minimum) / range, 0, 1);
            if (frac > 0)
            {
                UiRenderer.FillRoundRect(canvas, 0, y, (float)(FinalRect.Width * frac), h, h / 2, theme.Accent);
            }
        }
    }
}

/// <summary>Displays a SkiaSharp bitmap, scaled to fit.</summary>
public class ImageBox : UIElement
{
    public SKBitmap? Source { get; set; }
    public bool UniformFit { get; set; } = true;

    protected override Size MeasureOverride(Size available)
    {
        if (Source is null) return Size.Empty;
        if (float.IsPositiveInfinity(available.Width) && float.IsPositiveInfinity(available.Height))
        {
            return new Size(Source.Width, Source.Height);
        }
        var scaleX = float.IsPositiveInfinity(available.Width) ? float.MaxValue : available.Width / Source.Width;
        var scaleY = float.IsPositiveInfinity(available.Height) ? float.MaxValue : available.Height / Source.Height;
        var scale = UniformFit ? Math.Min(scaleX, scaleY) : Math.Min(1, Math.Min(scaleX, scaleY));
        return new Size(Source.Width * scale, Source.Height * scale);
    }

    protected internal override void OnRender(SKCanvas canvas, Theme theme)
    {
        if (Source is null || FinalRect.Width <= 0 || FinalRect.Height <= 0) return;
        var dest = new SKRect(0, 0, FinalRect.Width, FinalRect.Height);
        canvas.DrawBitmap(Source, dest);
    }
}
