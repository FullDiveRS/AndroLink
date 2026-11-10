namespace AndroLink.UI;

/// <summary>Arranges children in a single line with optional spacing.</summary>
public class StackPanel : UIElement
{
    public Orientation Orientation { get; set; } = Orientation.Vertical;
    public float Spacing { get; set; }

    public StackPanel(Orientation orientation = Orientation.Vertical, float spacing = 0)
    {
        Orientation = orientation;
        Spacing = spacing;
        ClipToBounds = false;
    }

    protected override Size MeasureOverride(Size available)
    {
        bool horizontal = Orientation == Orientation.Horizontal;
        float main = 0, cross = 0, count = 0;

        // Padding belongs to the stack itself (desired = content + padding),
        // so children must be constrained by the space WITHOUT padding - the
        // arrange pass gives them exactly that (contentRect). Constraining
        // them by the padded size used to double-count the padding and push
        // fill children (ScrollViewer) past the bottom edge.
        var avail = new Size(
            float.IsPositiveInfinity(available.Width) ? available.Width : Math.Max(0, available.Width - Padding.Horizontal),
            float.IsPositiveInfinity(available.Height) ? available.Height : Math.Max(0, available.Height - Padding.Vertical));

        foreach (var child in VisualChildren)
        {
            if (!child.Visible) continue;
            // Later children are measured with the REMAINING main-axis space,
            // so a fill control placed after fixed headers (e.g. a ScrollViewer
            // under a page title + toolbar) sizes to the leftover height
            // instead of the full page height - which used to push it past the
            // bottom edge and made the overflowed rows unreachable.
            var remaining = horizontal
                ? new Size(
                    float.IsPositiveInfinity(avail.Width) ? avail.Width : Math.Max(0, avail.Width - main),
                    avail.Height)
                : new Size(
                    avail.Width,
                    float.IsPositiveInfinity(avail.Height) ? avail.Height : Math.Max(0, avail.Height - main));
            child.MeasureInternal(remaining);
            var d = child.DesiredSize;
            main += horizontal ? d.Width : d.Height;
            cross = Math.Max(cross, horizontal ? d.Height : d.Width);
            count++;
        }

        if (count > 1) main += Spacing * (count - 1);
        return horizontal ? new Size(main, cross) : new Size(cross, main);
    }

    protected override void ArrangeOverride(Rect contentRect)
    {
        bool horizontal = Orientation == Orientation.Horizontal;
        var visible = VisualChildren.Where(c => c.Visible).ToList();
        if (visible.Count == 0) return;

        if (horizontal)
        {
            // Children with HExpand share the leftover width equally.
            float used = 0;
            int expandCount = 0;
            for (var i = 0; i < visible.Count; i++)
            {
                var d = visible[i].DesiredSize;
                used += d.Width + (i < visible.Count - 1 ? Spacing : 0);
                if (visible[i].HExpand) expandCount++;
            }
            var leftover = Math.Max(0, contentRect.Width - used);

            // ArrangeInternal strips the child margin, so pass the full desired
            // size and only position the outer edge.
            float cursor = 0;
            for (var i = 0; i < visible.Count; i++)
            {
                var child = visible[i];
                var d = child.DesiredSize; // includes margin
                var m = child.Margin;

                var w = Math.Max(0, d.Width - m.Horizontal + (child.HExpand && expandCount > 0 ? leftover / expandCount : 0));
                var ownH = Math.Min(d.Height - m.Vertical, contentRect.Height);
                var fillH = child.VAlign == VAlignment.Stretch;
                var h = fillH ? contentRect.Height : d.Height;
                var y = (fillH ? 0 : child.VAlign switch
                {
                    VAlignment.Center => (contentRect.Height - ownH) / 2 - m.Top,
                    VAlignment.Bottom => contentRect.Height - ownH - m.Vertical,
                    _ => 0,
                });
                child.ArrangeInternal(new Rect(contentRect.X + cursor, contentRect.Y + y, w + m.Horizontal, h));
                cursor += w + m.Horizontal + Spacing;
            }
        }
        else
        {
            float cursor = 0;
            foreach (var child in visible)
            {
                var d = child.DesiredSize; // includes margin
                var m = child.Margin;

                var ownW = Math.Min(d.Width - m.Horizontal, contentRect.Width);
                var fillW = child.HAlign == HAlignment.Stretch;
                var w = fillW ? contentRect.Width : d.Width;
                var x = fillW ? 0 : child.HAlign switch
                {
                    HAlignment.Center => (contentRect.Width - ownW) / 2 - m.Left,
                    HAlignment.Right => contentRect.Width - ownW - m.Right,
                    _ => m.Left,
                };
                child.ArrangeInternal(new Rect(contentRect.X + x, contentRect.Y + cursor, w, d.Height));
                cursor += d.Height + Spacing;
            }
        }
    }
}

/// <summary>Docks children to the edges of the available space; optionally fills the remainder with the last child.</summary>
public class DockPanel : UIElement
{
    private static readonly Dictionary<UIElement, Dock> Sides = new();

    public bool LastChildFill { get; set; } = true;

    public DockPanel(bool lastChildFill = true) => LastChildFill = lastChildFill;

    public static void SetDock(UIElement element, Dock dock) => Sides[element] = dock;
    public static Dock GetDock(UIElement element) => Sides.GetValueOrDefault(element, Dock.Left);

    protected override Size MeasureOverride(Size available)
    {
        float usedW = 0, usedH = 0, maxCross = 0;
        var children = VisibleChildren().ToList();

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var dock = GetDock(child);
            var isLastFill = LastChildFill && i == children.Count - 1;

            var constraint = isLastFill
                ? new Size(Math.Max(0, available.Width - usedW), Math.Max(0, available.Height - usedH))
                : dock switch
                {
                    Dock.Left or Dock.Right => new Size(Math.Max(0, available.Width - usedW), Math.Max(0, available.Height)),
                    _ => new Size(Math.Max(0, available.Width), Math.Max(0, available.Height - usedH)),
                };

            child.MeasureInternal(constraint);
            var d = child.DesiredSize;

            if (isLastFill)
            {
                maxCross = Math.Max(maxCross, 0);
                usedW = Math.Max(usedW, usedW + d.Width); // fill occupies the rest
                usedH = Math.Max(usedH, usedH + d.Height);
            }
            else
            {
                switch (dock)
                {
                    case Dock.Left:
                    case Dock.Right:
                        usedW += d.Width;
                        maxCross = Math.Max(maxCross, d.Height);
                        break;
                    default:
                        usedH += d.Height;
                        maxCross = Math.Max(maxCross, d.Width);
                        break;
                }
            }
        }

        return new Size(Math.Max(usedW, maxCross), Math.Max(usedH, maxCross));
    }

    protected override void ArrangeOverride(Rect contentRect)
    {
        var remaining = new Rect(contentRect.X, contentRect.Y, contentRect.Width, contentRect.Height);
        var children = VisibleChildren().ToList();

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var dock = GetDock(child);
            var isLastFill = LastChildFill && i == children.Count - 1;

            if (isLastFill)
            {
                child.ArrangeInternal(remaining);
                return;
            }

            switch (dock)
            {
                case Dock.Left:
                {
                    var w = Math.Min(child.DesiredSize.Width, remaining.Width);
                    child.ArrangeInternal(new Rect(remaining.X, remaining.Y, w, remaining.Height));
                    remaining = new Rect(remaining.X + w, remaining.Y, Math.Max(0, remaining.Width - w), remaining.Height);
                    break;
                }
                case Dock.Right:
                {
                    var w = Math.Min(child.DesiredSize.Width, remaining.Width);
                    child.ArrangeInternal(new Rect(remaining.Right - w, remaining.Y, w, remaining.Height));
                    remaining = new Rect(remaining.X, remaining.Y, Math.Max(0, remaining.Width - w), remaining.Height);
                    break;
                }
                case Dock.Top:
                {
                    var h = Math.Min(child.DesiredSize.Height, remaining.Height);
                    child.ArrangeInternal(new Rect(remaining.X, remaining.Y, remaining.Width, h));
                    remaining = new Rect(remaining.X, remaining.Y + h, remaining.Width, Math.Max(0, remaining.Height - h));
                    break;
                }
                case Dock.Bottom:
                {
                    var h = Math.Min(child.DesiredSize.Height, remaining.Height);
                    child.ArrangeInternal(new Rect(remaining.X, remaining.Bottom - h, remaining.Width, h));
                    remaining = new Rect(remaining.X, remaining.Y, remaining.Width, Math.Max(0, remaining.Height - h));
                    break;
                }
            }
        }
    }

    private IEnumerable<UIElement> VisibleChildren() => VisualChildren.Where(c => c.Visible);
}

/// <summary>Single-child container that draws chrome (background/border via base class) and applies Padding.</summary>
public class Border : UIElement
{
    private UIElement? _child;

    public UIElement? Child
    {
        get => _child;
        set
        {
            if (ReferenceEquals(_child, value)) return;
            if (_child is not null) _child.Parent = null;
            _child = value;
            if (_child is not null) _child.Parent = this;
            InvalidateMeasure();
        }
    }

    public Border() { }

    public Border(UIElement child) => Child = child;

    protected internal override IEnumerable<UIElement> VisualChildren
        => _child is null ? Enumerable.Empty<UIElement>() : new[] { _child };

    protected override Size MeasureOverride(Size available)
    {
        if (_child is null) return Size.Empty;
        _child.MeasureInternal(available);
        return _child.DesiredSize;
    }

    protected override void ArrangeOverride(Rect contentRect)
    {
        _child?.ArrangeInternal(UIElement.AlignChild(contentRect, _child.DesiredSize, _child.HAlign, _child.VAlign, MaxWidth, MaxHeight));
    }
}
