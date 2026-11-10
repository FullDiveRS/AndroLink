using SkiaSharp;

namespace AndroLink.UI;

/// <summary>
/// Top-level window: owns the visual tree root (Content), the modal overlay
/// layer, keyboard focus, pointer routing and the layout/render lifecycle.
/// The platform host drives it via the internal Dispatch*/RenderFrame methods.
/// </summary>
public sealed class Window : UIElement
{
    /// <summary>Set by the host platform before any window is shown.</summary>
    public static IPlatform? Platform { get; set; }

    private UIElement? _content;
    private readonly OverlayLayer _overlay = new();
    private IWindowHandle? _handle;
    private bool _layoutDirty = true;
    private Size _clientSize;
    private UIElement? _hovered;
    private UIElement? _captured;

    public Window()
    {
        _overlay.Parent = this;
    }

    public string Title { get; set; } = "AndroLink";
    public Size InitialClientSize { get; set; } = new(1120, 720);
    public bool Resizable { get; set; } = true;

    public IWindowHandle? Handle => _handle;
    public OverlayLayer Overlay => _overlay;

    internal Action<string[]>? FileDropHandler;

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

    protected internal override IEnumerable<UIElement> VisualChildren
    {
        get
        {
            if (_content is not null) yield return _content;
            if (_overlay.Children.Count > 0) yield return _overlay;
        }
    }

    // ------------------------------------------------------------- lifecycle

    public void Show()
    {
        if (Platform is null)
            throw new InvalidOperationException("Window.Platform is not set (the host sets it on startup).");
        _handle = Platform.CreateWindow(this);
    }

    public void Close() => _handle?.Close();

    public void SetTitle(string title)
    {
        Title = title;
        _handle?.SetTitle(title);
    }

    internal void AttachHandle(IWindowHandle handle) => _handle = handle;

    internal void ResizeFromPlatform(float width, float height)
    {
        _clientSize = new Size(width, height);
        _layoutDirty = true;
        RequestRender();
    }

    internal float DpiScale { get; set; } = 1f;

    internal void RequestLayout()
    {
        _layoutDirty = true;
        RequestRender();
    }

    internal void RequestRender() => _handle?.Invalidate();

    internal void DoLayout()
    {
        MeasureInternal(_clientSize);
        ArrangeInternal(new Rect(0, 0, _clientSize.Width, _clientSize.Height));
        _layoutDirty = false;
    }

    internal void RenderFrame(SKCanvas canvas, float scale)
    {
        if (_layoutDirty) DoLayout();
        canvas.Save();
        canvas.Scale(scale);
        canvas.Clear(Theme.Current.WindowBackground);
        UiRenderer.RenderTree(this, canvas);
        canvas.Restore();
    }

    // -------------------------------------------------------------- focus

    public UIElement? FocusedElement { get; private set; }

    public void FocusElement(UIElement? element)
    {
        if (ReferenceEquals(FocusedElement, element)) return;
        FocusedElement?.RaiseLostFocus();
        FocusedElement = element;
        FocusedElement?.RaiseGotFocus();
    }

    // ---------------------------------------------------------- hit testing

    public UIElement? HitTest(float x, float y) => HitTestElement(this, x, y);

    /// <summary>
    /// px/py are in the ELEMENT'S PARENT coordinate frame (the frame FinalRect
    /// lives in). Children are tested with coordinates converted into the
    /// element's own frame. (The previous version translated the point by
    /// child.FinalRect *before* the child's own bounds check, so any container
    /// whose FinalRect origin was not (0,0) - e.g. the page host at x=208 -
    /// failed its own Contains check and the descent stopped there. That made
    /// every nested button inert while the nav rail, sitting at (0,0), worked.)
    /// </summary>
    private static UIElement? HitTestElement(UIElement element, float px, float py)
    {
        if (!element.Visible || !element.IsHitTestVisible) return null;
        if (element.FinalRect.Width <= 0 || element.FinalRect.Height <= 0) return null;

        var inside = element.FinalRect.Contains(px, py);
        if (!inside && element.ClipToBounds) return null; // clipped children cannot overflow

        // Interactive controls take the whole subtree hit so hover/press
        // states work even when the pointer is over their label content.
        if (element.IsHitTestLeaf) return inside ? element : null;

        var lx = px - element.FinalRect.X;
        var ly = py - element.FinalRect.Y;
        foreach (var child in element.VisualChildren.Reverse())
        {
            if (!child.Visible) continue;
            var hit = HitTestElement(child, lx, ly);
            if (hit is not null) return hit;
        }
        return inside ? element : null;
    }

    // --------------------------------------------------------- input routing

    private static PointerEventArgs MakeArgs(UIElement target, float windowX, float windowY, bool wheel = false, float delta = 0)
    {
        var (ax, ay) = target.AbsolutePosition();
        var bounds = target.AbsoluteBounds();
        PointerEventArgs args = wheel ? new WheelEventArgs() : new PointerEventArgs();
        args.X = windowX - ax;
        args.Y = windowY - ay;
        args.WindowX = windowX;
        args.WindowY = windowY;
        args.Source = target;
        args.IsOverTarget = bounds.Contains(windowX, windowY);
        if (args is WheelEventArgs wheelArgs) wheelArgs.Delta = delta;
        return args;
    }

    internal void DispatchPointerMove(float x, float y)
    {
        var hit = HitTest(x, y);

        if (_captured is not null)
        {
            _captured.RaiseMove(MakeArgs(_captured, x, y));
            return;
        }

        if (!ReferenceEquals(hit, _hovered))
        {
            if (_hovered is not null)
            {
                _hovered.IsPointerOver = false;
                _hovered.RaiseExit(MakeArgs(_hovered, x, y));
            }
            _hovered = hit;
            if (hit is not null)
            {
                hit.IsPointerOver = true;
                hit.RaiseEnter(MakeArgs(hit, x, y));
            }
        }
        _hovered?.RaiseMove(MakeArgs(_hovered, x, y));
    }

    internal void DispatchPointerPress(float x, float y)
    {
        var hit = HitTest(x, y);

        // Keyboard focus goes to the first focusable element on the chain
        // (otherwise focus clears - clicking empty space defocuses controls).
        UIElement? focusTarget = null;
        for (var current = hit; current is not null; current = current.Parent)
        {
            if (current.Focusable)
            {
                focusTarget = current;
                break;
            }
        }
        FocusElement(focusTarget);

        // Press bubbles like wheel: the deepest element first, then ancestors,
        // until someone handles it. (Buttons contain visual-only children such
        // as labels; without bubbling those children swallowed the press.)
        UIElement? handler = null;
        for (var current = hit; current is not null; current = current.Parent)
        {
            var args = MakeArgs(current, x, y);
            current.RaisePress(args);
            if (args.Handled)
            {
                handler = current;
                break;
            }
        }
        _captured = handler;
    }

    internal void DispatchPointerRelease(float x, float y)
    {
        var target = _captured ?? HitTest(x, y);
        if (target is not null)
        {
            // Release bubbles too, so controls reached through a visual child
            // (label inside a button) still complete their click gesture.
            for (var current = target; current is not null; current = current.Parent)
            {
                var args = MakeArgs(current, x, y);
                args.IsOverTarget = current.AbsoluteBounds().Contains(x, y);
                current.RaiseRelease(args);
                if (args.Handled) break;
            }
        }
        _captured = null;
    }

    internal void DispatchPointerWheel(float x, float y, float delta)
    {
        var current = HitTest(x, y);
        while (current is not null)
        {
            // Build a real WheelEventArgs here - a blind cast of the base
            // PointerEventArgs threw InvalidCastException on every wheel tick,
            // which the WndProc guard swallowed, so wheel scrolling never worked.
            var args = (WheelEventArgs)MakeArgs(current, x, y, wheel: true, delta: delta);
            current.RaiseWheel(args);
            if (args.Handled) return;
            current = current.Parent;
        }
    }

    internal void DispatchKey(KeyEventArgs args)
    {
        var current = FocusedElement;
        while (current is not null && !args.Handled)
        {
            current.RaiseKey(args);
            current = current.Parent;
        }
    }

    internal void DispatchText(TextInputEventArgs args)
    {
        var current = FocusedElement;
        while (current is not null && !args.Handled)
        {
            current.RaiseText(args);
            current = current.Parent;
        }
    }

    internal void DispatchFileDrop(string[] files)
    {
        if (_overlay.Children.Count > 0) return; // modal blocks drops
        FileDropHandler?.Invoke(files);
    }
}

/// <summary>Top layer for modal dialogs; rendered after (and hit-tested before) window content.</summary>
public sealed class OverlayLayer : UIElement
{
}
