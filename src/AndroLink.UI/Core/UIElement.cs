using SkiaSharp;

namespace AndroLink.UI;

/// <summary>
/// The base class of every node in the retained visual tree. Provides layout
/// properties, a two-pass measure/arrange engine, input event routing and the
/// SkiaSharp render hook. Custom controls override MeasureOverride,
/// ArrangeOverride and OnRender.
/// </summary>
public abstract class UIElement
{
    private readonly List<UIElement> _children = new();

    // ------------------------------------------------------------- properties

    public UIElement? Parent { get; internal set; }
    public IReadOnlyList<UIElement> Children => _children;
    public string? Name { get; set; }
    public object? Tag { get; set; }

    public LayoutLength Width { get; set; } = LayoutLength.Auto;
    public LayoutLength Height { get; set; } = LayoutLength.Auto;
    public float MinWidth { get; set; }
    public float MinHeight { get; set; }

    /// <summary>Upper bound applied at measure and arrange time (0 = unbounded). Use with HAlign.Center to build centered, width-capped page columns.</summary>
    public float MaxWidth { get; set; }
    public float MaxHeight { get; set; }

    /// <summary>Inside a horizontal StackPanel: shares the leftover main-axis space equally with other expanding children.</summary>
    public bool HExpand { get; set; }
    public Thickness Margin { get; set; } = Thickness.Zero;
    public Thickness Padding { get; set; } = Thickness.Zero;
    public CornerRadius CornerRadius { get; set; } = CornerRadius.None;
    public HAlignment HAlign { get; set; } = HAlignment.Stretch;
    public VAlignment VAlign { get; set; } = VAlignment.Stretch;
    public bool Visible { get; set; } = true;
    public float Opacity { get; set; } = 1f;

    /// <summary>Background fill painted before OnRender; null = transparent.</summary>
    public SKColor? Background { get; set; }
    public SKColor? BorderBrush { get; set; }
    public float BorderThickness { get; set; }
    public bool ClipToBounds { get; set; } = true;

    public bool IsHitTestVisible { get; set; } = true;
    public bool Focusable { get; set; }

    /// <summary>
    /// When true, hit-testing returns this element for its whole subtree
    /// without descending into children (their content is purely visual).
    /// Interactive controls override this so hover/press work over labels.
    /// </summary>
    protected internal virtual bool IsHitTestLeaf => false;

    public bool IsFocused { get; internal set; }
    internal bool IsPointerOver;

    // ------------------------------------------------------------- tree mgmt

    public T Add<T>(T child) where T : UIElement
    {
        if (this is Window) throw new InvalidOperationException("Window content is assigned via the Content property.");
        _children.Add(child);
        child.Parent = this;
        InvalidateMeasure();
        return child;
    }

    public void Remove(UIElement child)
    {
        if (_children.Remove(child))
        {
            child.Parent = null;
            InvalidateMeasure();
        }
    }

    public void ClearChildren()
    {
        foreach (var child in _children) child.Parent = null;
        _children.Clear();
        InvalidateMeasure();
    }

    public bool IsVisibleInTree
    {
        get
        {
            var e = this;
            while (e is not null)
            {
                if (!e.Visible) return false;
                e = e.Parent;
            }
            return true;
        }
    }

    /// <summary>Children considered for layout, hit-testing and rendering. Window overrides this to supply Content + Overlay.</summary>
    protected internal virtual IEnumerable<UIElement> VisualChildren => _children;

    // --------------------------------------------------------------- layout

    internal Size DesiredSize;
    internal Rect FinalRect;

    public void InvalidateMeasure() => this.FindWindow()?.RequestLayout();
    public void InvalidateVisual() => this.FindWindow()?.RequestRender();

    /// <summary>Engine pass 1: constraints minus margins; result includes margin.</summary>
    internal void MeasureInternal(Size available)
    {
        if (!Visible)
        {
            DesiredSize = Size.Empty;
            return;
        }

        var coreAvailable = new Size(
            Math.Max(0, available.Width - Margin.Horizontal),
            Math.Max(0, available.Height - Margin.Vertical));

        if (Width.Type == LayoutLength.UnitType.Pixel) coreAvailable = coreAvailable.With(width: Width.Value);
        if (Height.Type == LayoutLength.UnitType.Pixel) coreAvailable = coreAvailable.With(height: Height.Value);

        var content = MeasureOverride(coreAvailable);
        var desired = new Size(content.Width + Padding.Horizontal, content.Height + Padding.Vertical);

        if (Width.Type == LayoutLength.UnitType.Pixel) desired = desired.With(width: Width.Value);
        if (Height.Type == LayoutLength.UnitType.Pixel) desired = desired.With(height: Height.Value);
        if (MinWidth > 0) desired = desired.With(width: Math.Max(desired.Width, MinWidth));
        if (MinHeight > 0) desired = desired.With(height: Math.Max(desired.Height, MinHeight));
        if (MaxWidth > 0) desired = desired.With(width: Math.Min(desired.Width, MaxWidth));
        if (MaxHeight > 0) desired = desired.With(height: Math.Min(desired.Height, MaxHeight));

        DesiredSize = new Size(desired.Width + Margin.Horizontal, desired.Height + Margin.Vertical);
    }

    /// <summary>Engine pass 2: element rect minus margin/padding; virtual override positions content.</summary>
    internal void ArrangeInternal(Rect finalWithMargin)
    {
        var own = new Rect(
            finalWithMargin.X + Margin.Left,
            finalWithMargin.Y + Margin.Top,
            Math.Max(0, finalWithMargin.Width - Margin.Horizontal),
            Math.Max(0, finalWithMargin.Height - Margin.Vertical));
        // FinalRect is PARENT-RELATIVE (renderer/hit-test accumulate it down the tree).
        FinalRect = own;

        if (!Visible) return;

        // Content is laid out in SELF-relative coordinates: the origin is the
        // padding, never the parent's position. (Passing FinalRect down used to
        // re-accumulate ancestor offsets and pushed nested content off-centre.)
        var contentRect = new Rect(
            Padding.Left,
            Padding.Top,
            Math.Max(0, own.Width - Padding.Horizontal),
            Math.Max(0, own.Height - Padding.Vertical));
        ArrangeOverride(contentRect);
    }

    /// <summary>Override: measure content; receives constraint already reduced by Padding.</summary>
    protected virtual Size MeasureOverride(Size available)
    {
        // Default: children measured with the same constraint; content = union stretched.
        var size = Size.Empty;
        foreach (var child in VisualChildren)
        {
            if (!child.Visible) continue;
            child.MeasureInternal(available);
            size = new Size(Math.Max(size.Width, child.DesiredSize.Width), Math.Max(size.Height, child.DesiredSize.Height));
        }
        return size;
    }

    /// <summary>Override: position content within contentRect (bounds minus Padding).</summary>
    protected virtual void ArrangeOverride(Rect contentRect)
    {
        foreach (var child in VisualChildren)
        {
            if (!child.Visible) continue;
            var childRect = AlignChild(contentRect, child.DesiredSize, child.HAlign, child.VAlign, MaxWidth, MaxHeight);
            child.ArrangeInternal(childRect);
        }
    }

    internal static Rect AlignChild(Rect slot, Size desired, HAlignment h, VAlignment v)
        => AlignChild(slot, desired, h, v, 0, 0);

    internal static Rect AlignChild(Rect slot, Size desired, HAlignment h, VAlignment v, float maxWidth, float maxHeight)
    {
        float x, y;
        var w = h switch
        {
            HAlignment.Stretch when maxWidth > 0 => Math.Min(slot.Width, maxWidth),
            HAlignment.Stretch => slot.Width,
            _ => Math.Min(desired.Width, slot.Width),
        };
        var hh = v switch
        {
            VAlignment.Stretch when maxHeight > 0 => Math.Min(slot.Height, maxHeight),
            VAlignment.Stretch => slot.Height,
            _ => Math.Min(desired.Height, slot.Height),
        };
        // slot is parent-relative (its origin never contains grandparent
        // offsets), so position children relative to the slot origin.
        x = h switch
        {
            HAlignment.Center => slot.X + (slot.Width - w) / 2,
            HAlignment.Right => slot.Right - w,
            _ => slot.X,
        };
        y = v switch
        {
            VAlignment.Center => slot.Y + (slot.Height - hh) / 2,
            VAlignment.Bottom => slot.Bottom - hh,
            _ => slot.Y,
        };
        return new Rect(x, y, w, hh);
    }

    // ---------------------------------------------------------------- input

    public event Action<PointerEventArgs>? PointerEntered;
    public event Action<PointerEventArgs>? PointerExited;
    public event Action<PointerEventArgs>? PointerMoved;
    public event Action<PointerEventArgs>? PointerPressed;
    public event Action<PointerEventArgs>? PointerReleased;
    public event Action<WheelEventArgs>? PointerWheel;
    public event Action<KeyEventArgs>? KeyDown;
    public event Action<TextInputEventArgs>? TextInput;
    public event Action? GotFocus;
    public event Action? LostFocus;

    internal void RaiseEnter(PointerEventArgs args) { OnPointerEnter(args); PointerEntered?.Invoke(args); }
    internal void RaiseExit(PointerEventArgs args) { OnPointerExit(args); PointerExited?.Invoke(args); }
    internal void RaiseMove(PointerEventArgs args) { OnPointerMove(args); PointerMoved?.Invoke(args); }
    internal void RaisePress(PointerEventArgs args) { OnPointerPress(args); PointerPressed?.Invoke(args); }
    internal void RaiseRelease(PointerEventArgs args) { OnPointerRelease(args); PointerReleased?.Invoke(args); }
    internal void RaiseWheel(WheelEventArgs args) { OnPointerWheel(args); PointerWheel?.Invoke(args); }
    internal void RaiseKey(KeyEventArgs args) { OnKey(args); KeyDown?.Invoke(args); }
    internal void RaiseText(TextInputEventArgs args) { OnTextInput(args); TextInput?.Invoke(args); }

    protected virtual void OnPointerEnter(PointerEventArgs args) { }
    protected virtual void OnPointerExit(PointerEventArgs args) { }
    protected virtual void OnPointerMove(PointerEventArgs args) { }
    protected virtual void OnPointerPress(PointerEventArgs args) { }
    protected virtual void OnPointerRelease(PointerEventArgs args) { }
    protected virtual void OnPointerWheel(WheelEventArgs args) { }
    protected virtual void OnKey(KeyEventArgs args) { }
    protected virtual void OnTextInput(TextInputEventArgs args) { }

    internal void RaiseGotFocus() { IsFocused = true; OnGotFocus(); GotFocus?.Invoke(); }
    internal void RaiseLostFocus() { IsFocused = false; OnLostFocus(); LostFocus?.Invoke(); }
    protected virtual void OnGotFocus() { }
    protected virtual void OnLostFocus() { }

    // -------------------------------------------------------------- rendering

    /// <summary>Draws control-specific visuals. Base chrome (background/border) is drawn by the renderer.</summary>
    protected internal virtual void OnRender(SKCanvas canvas, Theme theme) { }

    /// <summary>
    /// Draws overlays AFTER the children have rendered (unlike OnRender, which
    /// runs before them and can be painted over by child backgrounds - e.g. a
    /// ScrollViewer's scrollbar thumb under full-width row backgrounds).
    /// </summary>
    protected internal virtual void OnRenderOver(SKCanvas canvas, Theme theme) { }

    // ----------------------------------------------------------- convenience

    public override string ToString() => $"{GetType().Name}{(Name is null ? "" : $"'{Name}')")}";
}
