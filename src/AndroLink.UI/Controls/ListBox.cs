using SkiaSharp;

namespace AndroLink.UI;

/// <summary>
/// Simple vertical list with selection, hover, custom item factories and
/// double-click activation. Rows are real UIElements (layout + reuse).
/// </summary>
public class ListBox : UIElement
{
    private readonly ScrollViewer _scroll = new() { HAlign = HAlignment.Stretch, VAlign = VAlignment.Stretch };
    private readonly StackPanel _rows = new(Orientation.Vertical);
    private int _selectedIndex = -1;
    private Func<object, UIElement>? _itemFactory;

    public List<object> Items { get; } = new();

    public Func<object, UIElement>? ItemFactory
    {
        get => _itemFactory;
        set { _itemFactory = value; RefreshItems(); }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value) return;
            _selectedIndex = value;
            UpdateRowSelection();
            SelectionChanged?.Invoke();
        }
    }

    public object? SelectedItem => _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;

    public event Action? SelectionChanged;
    public event Action<object>? ItemActivated;

    public ListBox()
    {
        HAlign = HAlignment.Stretch;
        VAlign = VAlignment.Stretch;
        _scroll.Content = _rows;
    }

    protected internal override IEnumerable<UIElement> VisualChildren => new[] { _scroll };

    public void RefreshItems()
    {
        _rows.ClearChildren();
        for (var i = 0; i < Items.Count; i++)
        {
            var index = i;
            var item = Items[i];

            var content = _itemFactory?.Invoke(item) ?? new TextBlock(item.ToString() ?? "");
            content.HAlign = HAlignment.Stretch;

            var row = new Border(content)
            {
                Padding = new Thickness(10, 7),
                CornerRadius = new CornerRadius(4),
                HAlign = HAlignment.Stretch,
                Tag = index,
            };

            row.PointerPressed += _ => { _pressPending = true; SelectedIndex = index; };
            row.PointerReleased += args =>
            {
                if (!_pressPending || !args.IsOverTarget) return;
                var now = Environment.TickCount64;
                if (now - _lastClickMs < 420) ItemActivated?.Invoke(item);
                _lastClickMs = now;
                _pressPending = false;
            };
            row.PointerEntered += _ =>
            {
                if (index != SelectedIndex) row.Background = Theme.Current.SurfaceHover;
            };
            row.PointerExited += _ =>
            {
                if (index != SelectedIndex) row.Background = null;
            };

            _rows.Add(row);
        }
        UpdateRowSelection();
        InvalidateMeasure();
    }

    private bool _pressPending;
    private long _lastClickMs;

    private void UpdateRowSelection()
    {
        var i = 0;
        foreach (var row in _rows.Children)
        {
            row.Background = i == SelectedIndex ? Theme.Current.Selection : null;
            i++;
        }
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size available) => available;

    protected override void ArrangeOverride(Rect contentRect)
    {
        _scroll.ArrangeInternal(contentRect);
    }
}
